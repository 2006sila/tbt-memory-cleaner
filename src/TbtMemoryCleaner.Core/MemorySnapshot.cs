namespace TbtMemoryCleaner.Core;

/// <summary>一次内存采样。全部字段来自 GlobalMemoryStatusEx 与 NtQuerySystemInformation(0x50)，不做加权或推测。</summary>
public readonly record struct MemorySnapshot(
    DateTimeOffset At,
    uint LoadPercent,
    ulong TotalBytes,
    ulong AvailableBytes,
    ulong CommitUsedBytes,
    ulong CommitLimitBytes,
    ulong FreeBytes,
    ulong StandbyBytes,
    ulong ModifiedBytes,
    uint PageSize,
    bool ListAvailable)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double LoadRatio => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes;
}
