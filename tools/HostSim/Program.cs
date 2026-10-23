using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using ThinkBookToolkit.PluginApi;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "dist/tbt.memory.cleaner");
        var manifestPath = Path.Combine(directory, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            Console.WriteLine("missing plugin.json: " + manifestPath);
            return 1;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var entryAssembly = root.GetProperty("EntryAssembly").GetString()!;
        var entryType = root.GetProperty("EntryType").GetString()!;

        var host = Environment.GetEnvironmentVariable("TBT_HOST_EXE")
                   ?? @"C:\Program Files\ThinkBook Toolkit\ThinkBookToolkit.PluginHost.exe";
        if (!File.Exists(host))
        {
            Console.WriteLine("missing plugin host: " + host);
            return 1;
        }

        var pipeName = "TbtMemoryCleaner.Sim." + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(Path.Combine(directory, entryAssembly));
        start.ArgumentList.Add(entryType);

        using var process = Process.Start(start);
        if (process is null)
        {
            Console.WriteLine("failed to start plugin host");
            return 1;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var failures = 0;

        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            var handshake = await PluginWire.ReadAsync<string>(pipe, timeout.Token);
            Console.WriteLine("handshake: " + handshake);
            if (handshake != "TBT_PLUGIN_1")
            {
                Console.WriteLine("unexpected handshake");
                return 1;
            }

            var request = BuildRequest();
            for (var i = 1; i <= 5; i++)
            {
                var watch = Stopwatch.StartNew();
                await PluginWire.WriteAsync(pipe, request, timeout.Token);
                var response = await PluginWire.ReadAsync<PluginResponse>(pipe, timeout.Token);
                watch.Stop();

                if (!response.Success || response.Result is null)
                {
                    Console.WriteLine($"tick {i}: FAILED {response.Error}");
                    failures++;
                    continue;
                }

                var result = response.Result;
                var keys = result.Values.Keys.ToArray();
                var finite = result.Values.Values.All(v => v is { } number && double.IsFinite(number));
                var summary = result.OverviewValues is not null &&
                              result.OverviewValues.TryGetValue("tbt.memory.cleaner.summary", out var text) &&
                              !string.IsNullOrWhiteSpace(text);
                var keysOk = keys.Length == 3 && keys.All(k => k.StartsWith("tbt.memory.cleaner.", StringComparison.Ordinal));

                Console.WriteLine($"tick {i}: {watch.ElapsedMilliseconds} ms | keys={keys.Length} finite={finite} summary={summary}");
                Console.WriteLine("        " + string.Join(", ", result.Values.Select(p => $"{p.Key}={p.Value?.ToString("0.##") ?? "null"}")));

                if (!keysOk || !finite || !summary || watch.ElapsedMilliseconds > 1500) failures++;
                await Task.Delay(1000, timeout.Token);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("exception: " + ex.GetBaseException().Message);
            failures++;
        }
        finally
        {
            try { pipe.Dispose(); } catch { }
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(true); } catch { }
            }
        }

        Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    private static PluginRequest BuildRequest()
    {
        var empty = JsonDocument.Parse("{}").RootElement;
        var settings = new Dictionary<string, JsonElement>
        {
            ["areas"] = JsonSerializer.SerializeToElement(15d),
            ["auto-enabled"] = JsonSerializer.SerializeToElement(false),
            ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
            ["auto-interval"] = JsonSerializer.SerializeToElement(600d),
        };

        return new PluginRequest(
            "tick",
            new PluginContext(DateTimeOffset.UtcNow, new Dictionary<string, SensorReading>(), empty, empty, Array.Empty<HostOperation>()),
            settings);
    }
}
