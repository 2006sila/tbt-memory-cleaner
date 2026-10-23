namespace TbtMemoryCleaner.Core;

/// <summary>单个区域的执行结果。Status 保留原始 NTSTATUS 或 HRESULT_FROM_WIN32，不吞错误。</summary>
public readonly record struct AreaOutcome(MemoryArea Area, bool Executed, int Status, string? Message)
{
    public bool Success => Executed && Status == 0;

    public string StatusText => Status == 0 ? "OK" : "0x" + ((uint)Status).ToString("X8");
}

public sealed record CleanReport(
    DateTimeOffset At,
    TimeSpan Duration,
    MemoryArea Requested,
    IReadOnlyList<AreaOutcome> Outcomes,
    MemorySnapshot Before,
    MemorySnapshot After)
{
    public int SuccessCount => Outcomes.Count(x => x.Success);

    public IReadOnlyList<AreaOutcome> Failures => Outcomes.Where(x => !x.Success).ToArray();

    public AreaOutcome? OutcomeOf(MemoryArea area)
    {
        foreach (var outcome in Outcomes)
            if (outcome.Area == area) return outcome;
        return null;
    }
}
