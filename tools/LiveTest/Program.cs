using System.Globalization;
using TbtMemoryCleaner.Core;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cleaner = new WindowsMemoryCleaner();

        if (args.Contains("--toast"))
        {
            static MemorySnapshot Snap(double freeGb, double standbyGb, double modifiedGb) => new(
                DateTimeOffset.Now, 27, (ulong)(32 * 1073741824d), (ulong)((freeGb + standbyGb) * 1073741824d),
                0, (ulong)(40 * 1073741824d), (ulong)(freeGb * 1073741824d),
                (ulong)(standbyGb * 1073741824d), (ulong)(modifiedGb * 1073741824d), 4096, true);

            var report = new CleanReport(DateTimeOffset.Now, TimeSpan.FromMilliseconds(420), MemoryArea.Safe,
                Array.Empty<AreaOutcome>(), Snap(19.17, 3.86, 0.31), Snap(19.62, 5.46, 4.17));
            var text = CleanSummary.Build(report, true) + " | " + CleanSummary.BuildAuto(report, 87, true);
            Console.WriteLine(text);
            var shown = ShellNotifier.Show("TBT 内存清理", CleanSummary.Build(report, true), false);
            Thread.Sleep(1500);
            Console.WriteLine("shown=" + shown + " | " + ShellNotifier.Diagnostics);
            Thread.Sleep(15000);
            return shown ? 0 : 1;
        }

        if (args.Contains("--sample"))
        {
            Print("sample", cleaner.Sample());
            return 0;
        }

        if (!cleaner.IsElevated)
        {
            Console.WriteLine("SKIP: 需要管理员权限。请用管理员 PowerShell 运行本程序，或在 Toolkit 内使用页面按钮。");
            return 2;
        }

        var mask = MemoryArea.Safe;
        var repeat = 1;

        foreach (var argument in args)
        {
            if (argument.StartsWith("--mask=", StringComparison.Ordinal))
                mask = (MemoryArea)uint.Parse(argument["--mask=".Length..], CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--repeat=", StringComparison.Ordinal))
                repeat = int.Parse(argument["--repeat=".Length..], CultureInfo.InvariantCulture);
        }

        Console.WriteLine($"mask=0x{(uint)mask:X} repeat={repeat} supported=0x{(uint)cleaner.SupportedAreas:X}");
        Console.WriteLine($"privileges: {(cleaner.PrivilegeError ?? "ok")}");

        var failures = 0;

        for (var i = 1; i <= repeat; i++)
        {
            var before = cleaner.Sample();
            var report = cleaner.Clean(mask);
            var after = cleaner.Sample();

            Console.WriteLine();
            Console.WriteLine($"--- run {i} ({report.Duration.TotalMilliseconds:0} ms)");
            Print("before", before);
            Print("after ", after);

            foreach (var outcome in report.Outcomes)
                Console.WriteLine($"  {outcome.Area,-22} executed={outcome.Executed} status={outcome.StatusText} {outcome.Message}");

            if (report.SuccessCount == 0) failures++;
        }

        Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    private static void Print(string label, MemorySnapshot snapshot)
    {
        Console.WriteLine($"{label}: free={Gb(snapshot.FreeBytes)} GB standby={Gb(snapshot.StandbyBytes)} GB " +
                          $"modified={Gb(snapshot.ModifiedBytes)} GB available={Gb(snapshot.AvailableBytes)} GB load={snapshot.LoadPercent}%");
    }

    private static string Gb(ulong bytes) => (bytes / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture);
}
