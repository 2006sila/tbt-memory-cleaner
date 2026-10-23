namespace TbtMemoryCleaner.Core;

public interface IMemoryCleaner
{
    /// <summary>当前进程是否以管理员完整性运行（内存列表 API 的前提）。</summary>
    bool IsElevated { get; }

    /// <summary>当前系统支持的区域掩码。</summary>
    MemoryArea SupportedAreas { get; }

    /// <summary>提升特权失败时的原因；成功为 null。</summary>
    string? PrivilegeError { get; }

    MemorySnapshot Sample();

    CleanReport Clean(MemoryArea areas, CancellationToken cancellationToken = default);
}
