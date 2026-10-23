namespace TbtMemoryCleaner.Core;

/// <summary>内存清理区域。位掩码与插件设置 areas 一一对应。</summary>
[Flags]
public enum MemoryArea : uint
{
    None = 0,

    /// <summary>全系统工作集：NtSetSystemInformation(SystemMemoryListInformation, MemoryEmptyWorkingSets)。</summary>
    WorkingSet = 1,

    /// <summary>系统文件缓存：SetSystemFileCacheSize(-1, -1, 0)。</summary>
    SystemFileCache = 2,

    /// <summary>卷写缓存刷盘：对每个固定卷 FlushFileBuffers。</summary>
    ModifiedFileCache = 4,

    /// <summary>待机列表低优先级部分：MemoryPurgeLowPriorityStandbyList。</summary>
    StandbyLowPriority = 8,

    /// <summary>待机列表全清：MemoryPurgeStandbyList。风险高。</summary>
    StandbyList = 16,

    /// <summary>已修改页列表：MemoryFlushModifiedList。风险高。</summary>
    ModifiedPageList = 32,

    /// <summary>注册表缓存：SystemRegistryReconciliationInformation（8.1+）。</summary>
    RegistryCache = 64,

    /// <summary>默认与自动清理允许的安全组合。</summary>
    Safe = WorkingSet | SystemFileCache | ModifiedFileCache | StandbyLowPriority,

    /// <summary>需要用户明确开启的高风险/谨慎区域。</summary>
    Risky = StandbyList | ModifiedPageList | RegistryCache,

    All = Safe | Risky,

    Default = Safe,
}

public enum AreaRisk
{
    Safe = 0,
    Caution = 1,
    High = 2,
}

public sealed record AreaInfo(
    MemoryArea Area,
    string Chinese,
    string English,
    string NoteChinese,
    string NoteEnglish,
    AreaRisk Risk,
    int MinimumBuild);

public static class AreaCatalog
{
    /// <summary>顺序即执行顺序：先把工作集与脏页落下，再清待机列表。</summary>
    public static IReadOnlyList<AreaInfo> All { get; } = new AreaInfo[]
    {
        new(MemoryArea.WorkingSet, "全系统工作集", "System working sets",
            "让所有进程交出可回收的工作集页；这些页会先进入待机/已修改列表，随后可能重新从磁盘读回。",
            "Trims reclaimable working-set pages; they land in the standby/modified lists first and may be faulted back in later.",
            AreaRisk.Caution, 6000),

        new(MemoryArea.ModifiedFileCache, "卷写缓存刷盘", "Volume write cache",
            "把各固定卷的脏页刷到磁盘，不释放内存。",
            "Flushes dirty volume buffers to disk. Does not free memory.",
            AreaRisk.Safe, 6000),

        new(MemoryArea.SystemFileCache, "系统文件缓存", "System file cache",
            "把系统文件缓存收缩到最小，释放被文件缓存占用的内存。",
            "Shrinks the system file cache working set to free cached memory.",
            AreaRisk.Safe, 6000),

        new(MemoryArea.ModifiedPageList, "已修改页列表", "Modified page list",
            "把未写回的脏页写盘并转入待机列表；写盘抖动时可能卡顿数秒。",
            "Writes dirty pages to disk and moves them to the standby list; can stall for seconds.",
            AreaRisk.High, 6000),

        new(MemoryArea.StandbyList, "待机列表（全清）", "Standby list (full)",
            "清空全部待机缓存；下次打开同样内容需要重新读盘，可能卡顿数秒。",
            "Clears the entire standby cache; the same content must be re-read from disk later. May stall.",
            AreaRisk.High, 6000),

        new(MemoryArea.StandbyLowPriority, "待机列表（低优先级）", "Standby list (low priority)",
            "只清最低优先级的待机页，代价最小。",
            "Clears only the lowest-priority standby pages. Cheapest option.",
            AreaRisk.Safe, 6000),

        new(MemoryArea.RegistryCache, "注册表缓存", "Registry cache",
            "触发注册表配置单元回收；随后访问会重新读盘。",
            "Triggers registry hive reconciliation; later access re-reads from disk.",
            AreaRisk.Caution, 9600),
    };

    public static MemoryArea SupportedMask(int osBuild)
    {
        var mask = MemoryArea.None;
        foreach (var info in All)
            if (osBuild >= info.MinimumBuild) mask |= info.Area;
        return mask;
    }

    public static AreaInfo? Find(MemoryArea area) => All.FirstOrDefault(x => x.Area == area);
}
