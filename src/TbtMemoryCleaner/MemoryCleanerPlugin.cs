using System.Text.Json;
using ThinkBookToolkit.PluginApi;
using TbtMemoryCleaner.Core;

namespace TbtMemoryCleaner;

/// <summary>插件工作进程入口：每秒发布内存传感器，并在阈值触发时后台清理安全区域。</summary>
public sealed class MemoryCleanerPlugin : IToolkitPlugin
{
    internal const string AvailableSensor = "tbt.memory.cleaner.available-mb";
    internal const string UsedSensor = "tbt.memory.cleaner.used-percent";
    internal const string StandbySensor = "tbt.memory.cleaner.standby-mb";
    internal const string SummaryItem = "tbt.memory.cleaner.summary";

    private readonly IMemoryCleaner _cleaner;
    private readonly object _sync = new();

    private Task? _autoTask;
    private DateTimeOffset _lastAutoClean = DateTimeOffset.MinValue;
    private MemorySnapshot? _lastSnapshot;
    private bool _standbyArmed = true;
    private DateTimeOffset _standbyHoldUntil = DateTimeOffset.MinValue;

    public MemoryCleanerPlugin()
        : this(new WindowsMemoryCleaner())
    {
    }

    internal MemoryCleanerPlugin(IMemoryCleaner cleaner) => _cleaner = cleaner;

    public ValueTask<PluginResult> EvaluateAsync(PluginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return new ValueTask<PluginResult>(Tick(request));
        }
        catch
        {
            // 任何异常都不能让宿主重启工作进程；返回空结果即可。
            return new ValueTask<PluginResult>(EmptyResult());
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private PluginResult Tick(PluginRequest request)
    {
        var settings = MemoryCleanerSettings.From(request.PluginSettings);

        MemorySnapshot? snapshot = null;
        try
        {
            snapshot = _cleaner.Sample();
            _lastSnapshot = snapshot;
        }
        catch
        {
            snapshot = _lastSnapshot;
        }

        MaybeStartAutoClean(settings, snapshot, IsChinese(request.Context));

        var values = new Dictionary<string, double?>(3, StringComparer.Ordinal);
        if (snapshot is { } sample)
        {
            values[AvailableSensor] = Math.Round(sample.AvailableBytes / 1048576d, 0);
            values[UsedSensor] = sample.LoadPercent;
            values[StandbySensor] = sample.ListAvailable ? Math.Round(sample.StandbyBytes / 1048576d, 0) : null;
        }
        else
        {
            values[AvailableSensor] = null;
            values[UsedSensor] = null;
            values[StandbySensor] = null;
        }

        var overview = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SummaryItem] = BuildSummary(snapshot),
        };

        return new PluginResult(values, null, null) { OverviewValues = overview };
    }

    private void MaybeStartAutoClean(MemoryCleanerSettings settings, MemorySnapshot? snapshot, bool chinese)
    {
        if (snapshot is not { } sample || !settings.AutoEnabled) return;
        if (!_cleaner.IsElevated) return;

        var standbyGb = sample.ListAvailable ? sample.StandbyBytes / 1073741824d : 0d;

        // 待机回落到阈值 90% 以下才重新武装，避免“清了没用还每分钟再清一次”。
        if (settings.StandbyThresholdGb > 0 && sample.ListAvailable &&
            standbyGb < settings.StandbyThresholdGb * 0.9d)
            _standbyArmed = true;
        if (!_standbyArmed && DateTimeOffset.Now >= _standbyHoldUntil)
            _standbyArmed = true;

        var loadTriggered = sample.LoadPercent >= settings.Threshold;
        var standbyTriggered = settings.StandbyThresholdGb > 0 && sample.ListAvailable &&
                               standbyGb >= settings.StandbyThresholdGb && _standbyArmed;

        var pressureTriggered = MemoryPressure.IsLow;

        if (!loadTriggered && !standbyTriggered && !pressureTriggered) return;

        var mask = settings.Areas & MemoryArea.Safe;
        if (standbyTriggered && !loadTriggered)
        {
            // 只是待机涨了：别裁工作集。真正的降待机只能靠“全清”，低优先级往往只占很小一块。
            var standbyOnly = settings.Areas &
                (MemoryArea.StandbyList | MemoryArea.StandbyLowPriority | MemoryArea.SystemFileCache | MemoryArea.ModifiedFileCache);
            if (standbyOnly != MemoryArea.None) mask = standbyOnly;
        }

        if (mask == MemoryArea.None) return;

        lock (_sync)
        {
            if (_autoTask is { IsCompleted: false }) return;

            var now = DateTimeOffset.Now;
            if (now - _lastAutoClean < TimeSpan.FromSeconds(settings.IntervalSeconds)) return;

            _lastAutoClean = now;
            var load = sample.LoadPercent;
            _autoTask = Task.Run(() =>
            {
                try
                {
                    var report = _cleaner.Clean(mask);

                    if (standbyTriggered && settings.StandbyThresholdGb > 0)
                    {
                        var afterGb = report.After.StandbyBytes / 1073741824d;
                        if (!report.After.ListAvailable || afterGb >= settings.StandbyThresholdGb * 0.9d)
                        {
                            _standbyArmed = false;
                            _standbyHoldUntil = DateTimeOffset.Now.AddMinutes(Math.Max(10d, settings.IntervalSeconds * 4d / 60d));
                        }
                    }

                    if (!settings.Notify || report.SuccessCount == 0) return;

                    ShellNotifier.Show(
                        chinese ? "TBT 内存清理" : "TBT Memory cleaner",
                        CleanSummary.BuildAuto(report, load, chinese));
                }
                catch
                {
                    // 自动清理失败不影响传感器发布。
                }
            });
        }
    }

    private static bool IsChinese(PluginContext context)
    {
        try
        {
            if (context.Settings.ValueKind == JsonValueKind.Object &&
                context.Settings.TryGetProperty("Language", out var language) &&
                language.ValueKind == JsonValueKind.String)
                return language.GetString() is not "en-US";
        }
        catch
        {
            // 语言读取失败按中文处理。
        }

        return true;
    }

    private static string BuildSummary(MemorySnapshot? snapshot)
    {
        if (snapshot is not { } sample) return "--";

        var free = sample.FreeBytes / 1073741824d;
        if (sample.ListAvailable)
            return $"{free:0.0} / {sample.StandbyBytes / 1073741824d:0.0} GB";

        return $"{sample.AvailableBytes / 1073741824d:0.0} GB";
    }

    private static PluginResult EmptyResult()
    {
        var values = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            [AvailableSensor] = null,
            [UsedSensor] = null,
            [StandbySensor] = null,
        };

        return new PluginResult(values, null, null);
    }
}
