using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThinkBookToolkit.PluginApi;
using TbtMemoryCleaner;
using TbtMemoryCleaner.Core;

internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static async Task<int> Main(string[] args)
    {
        var pluginDir = args.Length > 0 ? args[0] : null;

        Check("sample: physical totals", () =>
        {
            var snapshot = new WindowsMemoryCleaner().Sample();
            return snapshot.TotalBytes > 0 && snapshot.PageSize >= 4096 && snapshot.LoadPercent <= 100;
        });

        Check("catalog: win10 supports everything", () =>
            AreaCatalog.SupportedMask(19045) == MemoryArea.All);

        Check("catalog: win7 lacks registry cache", () =>
            (AreaCatalog.SupportedMask(7601) & MemoryArea.RegistryCache) == 0 &&
            (AreaCatalog.SupportedMask(7601) & MemoryArea.WorkingSet) != 0);

        Check("settings: defaults", () =>
        {
            var settings = MemoryCleanerSettings.From(null);
            return settings.Areas == MemoryArea.Safe && !settings.AutoEnabled && settings.Notify &&
                   Math.Abs(settings.Threshold - 85) < 0.001 && Math.Abs(settings.IntervalSeconds - 600) < 0.001 &&
                   Math.Abs(settings.StandbyThresholdGb - 4) < 0.001;
        });

        Check("settings: clamping", () =>
        {
            var map = new Dictionary<string, JsonElement>
            {
                ["areas"] = JsonSerializer.SerializeToElement(127d),
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(10d),
                ["auto-interval"] = JsonSerializer.SerializeToElement(5d),
            };
            var settings = MemoryCleanerSettings.From(map);
            return settings.Areas == (MemoryArea)127 && settings.AutoEnabled &&
                   Math.Abs(settings.Threshold - 50) < 0.001 && Math.Abs(settings.IntervalSeconds - 60) < 0.001;
        });

        Check("settings: garbage ignored", () =>
        {
            var map = new Dictionary<string, JsonElement>
            {
                ["areas"] = JsonSerializer.SerializeToElement("nope"),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(true),
            };
            var settings = MemoryCleanerSettings.From(map);
            return settings.Areas == MemoryArea.Safe && Math.Abs(settings.Threshold - 85) < 0.001;
        });

        Check("process: top list has entries", () =>
        {
            var top = ProcessMemoryReader.Top(5);
            return top.Count > 0 && top.All(x => x.WorkingSetBytes > 0) &&
                   top.SequenceEqual(top.OrderByDescending(x => x.WorkingSetBytes));
        });

        Check("process: trim self succeeds", () =>
        {
            var ok = ProcessTrimmer.Trim(Environment.ProcessId, out var error);
            return ok || error is not null; // 自进程偶尔被拒也算"有明确错误"，不能崩
        });

        Check("pressure: event object queryable", () =>
        {
            var first = MemoryPressure.IsLow;
            return first == MemoryPressure.IsLow;
        });

        Check("clean: empty mask returns empty report", () =>
        {
            var cleaner = new WindowsMemoryCleaner(elevated: false);
            var report = cleaner.Clean(MemoryArea.None);
            return report.Outcomes.Count == 0 && report.Requested == MemoryArea.None;
        });

        Check("clean: non-elevated reports privilege error", () =>
        {
            if (PrivilegesElevated()) return true; // 已提权时由 LiveTest 覆盖
            var cleaner = new WindowsMemoryCleaner(elevated: false);
            var report = cleaner.Clean(MemoryArea.Safe);
            return report.Outcomes.Count == 4 && report.Outcomes.All(x => !x.Executed && x.Status == unchecked((int)0xC0000061));
        });

        Check("summary: shows GB, not area count", () =>
        {
            static MemorySnapshot Snap(double freeGb, double standbyGb, double modifiedGb) => new(
                DateTimeOffset.Now, 27, (ulong)(32 * 1073741824d), (ulong)((freeGb + standbyGb) * 1073741824d),
                0, (ulong)(40 * 1073741824d), (ulong)(freeGb * 1073741824d),
                (ulong)(standbyGb * 1073741824d), (ulong)(modifiedGb * 1073741824d), 4096, true);

            var report = new CleanReport(DateTimeOffset.Now, TimeSpan.FromMilliseconds(420), MemoryArea.Safe,
                Array.Empty<AreaOutcome>(), Snap(19.17, 3.86, 0.31), Snap(19.62, 5.46, 4.17));

            var chinese = CleanSummary.Build(report, true);
            var english = CleanSummary.Build(report, false);
            var auto = CleanSummary.BuildAuto(report, 87, true);

            var flatReport = new CleanReport(DateTimeOffset.Now, TimeSpan.FromMilliseconds(70), MemoryArea.Safe & ~MemoryArea.WorkingSet,
                Array.Empty<AreaOutcome>(), Snap(18.01, 6.82, 0.38), Snap(18.00, 6.86, 0.38));

            return chinese.Contains("5.91 GB") && !chinese.Contains("区域") &&
                   english.Contains("5.91 GB") && auto.Contains("87%") && auto.Contains("5.91 GB") &&
                   CleanSummary.Build(flatReport, true).Contains("宽裕");
        });

        await CheckAsync("plugin: publishes three finite sensors", async () =>
        {
            var plugin = new MemoryCleanerPlugin(new FakeCleaner());
            var result = await plugin.EvaluateAsync(Request(), CancellationToken.None);
            var values = result.Values;
            return values.Count == 3 &&
                   values.ContainsKey(MemoryCleanerPlugin.AvailableSensor) &&
                   values.ContainsKey(MemoryCleanerPlugin.UsedSensor) &&
                   values.ContainsKey(MemoryCleanerPlugin.StandbySensor) &&
                   values.Values.All(v => v is { } number && double.IsFinite(number)) &&
                   result.OverviewValues is not null &&
                   result.OverviewValues.ContainsKey(MemoryCleanerPlugin.SummaryItem);
        });

        await CheckAsync("plugin: auto clean disabled does nothing", async () =>
        {
            var fake = new FakeCleaner();
            var plugin = new MemoryCleanerPlugin(fake);
            await plugin.EvaluateAsync(Request(new() { ["auto-enabled"] = JsonSerializer.SerializeToElement(false) }), CancellationToken.None);
            await Task.Delay(150);
            return fake.CleanCalls == 0;
        });

        await CheckAsync("plugin: auto clean triggers above threshold", async () =>
        {
            var fake = new FakeCleaner { Snapshot = SnapshotWith(load: 90) };
            var plugin = new MemoryCleanerPlugin(fake);
            var settings = new Dictionary<string, JsonElement>
            {
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["auto-interval"] = JsonSerializer.SerializeToElement(60d),
                ["areas"] = JsonSerializer.SerializeToElement(15d),
            };

            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            var watch = Stopwatch.StartNew();
            while (fake.CleanCalls == 0 && watch.ElapsedMilliseconds < 3000) await Task.Delay(20);

            return fake.CleanCalls == 1 && fake.LastMask == MemoryArea.Safe;
        });

        await CheckAsync("plugin: auto clean ignores risky-only mask", async () =>
        {
            var fake = new FakeCleaner { Snapshot = SnapshotWith(load: 95) };
            var plugin = new MemoryCleanerPlugin(fake);
            var settings = new Dictionary<string, JsonElement>
            {
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["areas"] = JsonSerializer.SerializeToElement(16d),
            };

            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            await Task.Delay(300);
            return fake.CleanCalls == 0;
        });

        await CheckAsync("plugin: standby trigger fires at low load", async () =>
        {
            var fake = new FakeCleaner { Snapshot = SnapshotWith(20, 6) };
            var plugin = new MemoryCleanerPlugin(fake);
            var settings = new Dictionary<string, JsonElement>
            {
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["auto-standby-gb"] = JsonSerializer.SerializeToElement(4d),
                ["areas"] = JsonSerializer.SerializeToElement(15d),
            };

            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            var watch = Stopwatch.StartNew();
            while (fake.CleanCalls == 0 && watch.ElapsedMilliseconds < 3000) await Task.Delay(20);

            // 只由待机触发时不能裁剪工作集：掩码应为 2|4|8 = 14
            return fake.CleanCalls == 1 && fake.LastMask == (MemoryArea.SystemFileCache | MemoryArea.ModifiedFileCache | MemoryArea.StandbyLowPriority);
        });

        await CheckAsync("plugin: standby trigger uses full purge when enabled", async () =>
        {
            var fake = new FakeCleaner { Snapshot = SnapshotWith(20, 6) };
            var plugin = new MemoryCleanerPlugin(fake);
            var settings = new Dictionary<string, JsonElement>
            {
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["auto-standby-gb"] = JsonSerializer.SerializeToElement(4d),
                ["areas"] = JsonSerializer.SerializeToElement(31d), // 15 | StandbyList(16)
            };

            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            var watch = Stopwatch.StartNew();
            while (fake.CleanCalls == 0 && watch.ElapsedMilliseconds < 3000) await Task.Delay(20);

            return fake.CleanCalls == 1 && (fake.LastMask & MemoryArea.StandbyList) != 0;
        });

        await CheckAsync("plugin: standby trigger disabled at zero", async () =>
        {
            var fake = new FakeCleaner { Snapshot = SnapshotWith(20, 12) };
            var plugin = new MemoryCleanerPlugin(fake);
            var settings = new Dictionary<string, JsonElement>
            {
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["auto-standby-gb"] = JsonSerializer.SerializeToElement(0d),
            };

            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            await Task.Delay(300);
            return fake.CleanCalls == 0;
        });

        await CheckAsync("plugin: auto clean respects interval", async () =>
        {
            var fake = new FakeCleaner { Snapshot = SnapshotWith(load: 95) };
            var plugin = new MemoryCleanerPlugin(fake);
            var settings = new Dictionary<string, JsonElement>
            {
                ["auto-enabled"] = JsonSerializer.SerializeToElement(true),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["auto-interval"] = JsonSerializer.SerializeToElement(3600d),
            };

            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            await Task.Delay(300);
            await plugin.EvaluateAsync(Request(settings), CancellationToken.None);
            await Task.Delay(300);
            return fake.CleanCalls == 1;
        });

        if (pluginDir is not null)
            Check("manifest: " + pluginDir, () => CheckManifest(pluginDir));

        Console.WriteLine();
        Console.WriteLine($"passed: {_passed}, failed: {Failures.Count}");
        foreach (var failure in Failures) Console.WriteLine("  - " + failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static bool PrivilegesElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static MemorySnapshot SnapshotWith(uint load, double standbyGb = 2) =>
        new(DateTimeOffset.Now, load, 32UL << 30, 8UL << 30, 4UL << 30, 8UL << 30, 6UL << 30,
            (ulong)(standbyGb * 1073741824d), 1UL << 30, 4096, true);

    private static PluginRequest Request(Dictionary<string, JsonElement>? settings = null)
    {
        var empty = JsonDocument.Parse("{}").RootElement;
        return new PluginRequest(
            "tick",
            new PluginContext(DateTimeOffset.UtcNow, new Dictionary<string, SensorReading>(), empty, empty, Array.Empty<HostOperation>()),
            settings ?? new Dictionary<string, JsonElement>());
    }

    private static bool CheckManifest(string directory)
    {
        var path = Path.Combine(directory, "plugin.json");
        if (!File.Exists(path)) return false;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var id = root.GetProperty("Id").GetString()!;

        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9._-]{0,100}$")) return false;
        if (root.GetProperty("ApiVersion").GetInt32() != 1) return false;
        if (!File.Exists(Path.Combine(directory, root.GetProperty("EntryAssembly").GetString()!))) return false;

        var permissions = root.GetProperty("Permissions").EnumerateArray().Select(x => x.GetString()).ToArray();
        var allowed = new[] { "sensors.read", "data.read", "settings.read", "host.control", "replace", "fan.backend", "ui.custom", "ui.toast" };
        if (permissions.Any(p => !allowed.Contains(p))) return false;

        foreach (var page in root.GetProperty("Pages").EnumerateArray())
        {
            if (!page.GetProperty("Id").GetString()!.StartsWith(id + ".", StringComparison.Ordinal)) return false;
            if (!page.TryGetProperty("View", out var view)) continue;
            if (!permissions.Contains("ui.custom")) return false;
            if (!File.Exists(Path.Combine(directory, view.GetProperty("Assembly").GetString()!))) return false;
            if (string.IsNullOrWhiteSpace(view.GetProperty("Type").GetString())) return false;
        }

        foreach (var sensor in root.GetProperty("Sensors").EnumerateArray())
        {
            if (!sensor.GetProperty("Id").GetString()!.StartsWith(id + ".", StringComparison.Ordinal)) return false;
            if (string.IsNullOrWhiteSpace(sensor.GetProperty("Name").GetProperty("Chinese").GetString())) return false;
        }

        foreach (var item in root.GetProperty("OverviewItems").EnumerateArray())
            if (!item.GetProperty("Id").GetString()!.StartsWith(id + ".", StringComparison.Ordinal)) return false;

        foreach (var setting in root.GetProperty("Settings").EnumerateArray())
        {
            var kind = setting.GetProperty("Kind").GetString();
            if (kind is not ("boolean" or "number" or "string" or "choice")) return false;

            if (kind != "number") continue;
            var value = setting.GetProperty("DefaultValue").GetDouble();
            if (setting.TryGetProperty("Minimum", out var minimum) && value < minimum.GetDouble()) return false;
            if (setting.TryGetProperty("Maximum", out var maximum) && value > maximum.GetDouble()) return false;
        }

        return true;
    }

    private static void Check(string name, Func<bool> test)
    {
        try
        {
            if (test()) Pass(name);
            else Fail(name, "returned false");
        }
        catch (Exception ex)
        {
            Fail(name, ex.GetBaseException().Message);
        }
    }

    private static async Task CheckAsync(string name, Func<Task<bool>> test)
    {
        try
        {
            if (await test()) Pass(name);
            else Fail(name, "returned false");
        }
        catch (Exception ex)
        {
            Fail(name, ex.GetBaseException().Message);
        }
    }

    private static void Pass(string name)
    {
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Fail(string name, string reason)
    {
        Failures.Add(name + ": " + reason);
        Console.WriteLine("FAIL " + name + " - " + reason);
    }

    private sealed class FakeCleaner : IMemoryCleaner
    {
        public int CleanCalls;
        public MemoryArea LastMask;
        public MemorySnapshot Snapshot = SnapshotWith(50);

        public bool IsElevated => true;

        public MemoryArea SupportedAreas => MemoryArea.All;

        public string? PrivilegeError => null;

        public MemorySnapshot Sample() => Snapshot;

        public CleanReport Clean(MemoryArea areas, CancellationToken cancellationToken = default)
        {
            CleanCalls++;
            LastMask = areas;
            return new CleanReport(DateTimeOffset.Now, TimeSpan.FromMilliseconds(5), areas, Array.Empty<AreaOutcome>(), Snapshot, Snapshot);
        }
    }
}
