using System.Globalization;

namespace TbtMemoryCleaner.Core;

/// <summary>清理结果的短文案：优先给 GB 数字，而不是“清理了几个区域”。</summary>
public static class CleanSummary
{
    public static string Build(CleanReport report, bool chinese)
    {
        var workingSetRequested = (report.Requested & MemoryArea.WorkingSet) != 0;
        var before = WorkingSetBytes(report.Before);
        var after = WorkingSetBytes(report.After);
        var released = before > after ? before - after : 0;
        var freeDelta = (long)report.After.FreeBytes - (long)report.Before.FreeBytes;
        var standbyDelta = (long)report.After.StandbyBytes - (long)report.Before.StandbyBytes;

        const long negligible = 64L * 1024 * 1024;
        if (released < negligible && Math.Abs(freeDelta) < negligible && Math.Abs(standbyDelta) < negligible)
        {
            return chinese
                ? "内存本来就宽裕，没有可释放的部分"
                : "Nothing to release; memory is already comfortable";
        }

        if (workingSetRequested)
        {
            return chinese
                ? $"工作集 -{Gb(released)}，空闲 {Signed(freeDelta)}"
                : $"working set -{Gb(released)}, free {Signed(freeDelta)}";
        }

        return chinese
            ? $"空闲 {Signed(freeDelta)}，待机 {Signed(standbyDelta)}"
            : $"free {Signed(freeDelta)}, standby {Signed(standbyDelta)}";
    }

    public static string BuildAuto(CleanReport report, uint loadPercent, bool chinese)
    {
        var body = Build(report, chinese);
        return chinese
            ? $"内存占用 {loadPercent}%：{body}"
            : $"Memory at {loadPercent}%: {body}";
    }

    private static ulong WorkingSetBytes(MemorySnapshot snapshot)
    {
        var lists = snapshot.FreeBytes + snapshot.StandbyBytes + snapshot.ModifiedBytes;
        return snapshot.TotalBytes > lists ? snapshot.TotalBytes - lists : 0;
    }

    private static string Gb(ulong bytes) =>
        (bytes / 1073741824d).ToString("0.00", CultureInfo.CurrentCulture) + " GB";

    private static string Signed(long delta) =>
        (delta >= 0 ? "+" : "-") + Gb((ulong)Math.Abs(delta));
}
