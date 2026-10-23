using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TbtMemoryCleaner.Core;

/// <summary>Windows 内存列表 API 的托管封装。所有结论都来自真实调用返回值，不做推测。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryCleaner : IMemoryCleaner
{
    private const string MutexName = @"Local\tbt.memory.cleaner.clean";
    private const int StatusPrivilegeNotHeld = unchecked((int)0xC0000061);
    private const int StatusNotSupported = unchecked((int)0xC00000BB);
    private const int StatusBusy = unchecked((int)0xC0000043);

    private readonly SemaphoreSlim _local = new(1, 1);
    private readonly object _privilegeSync = new();
    private readonly int _osBuild;
    private readonly MemoryArea _supported;

    private bool _privilegeChecked;
    private bool _privilegeOk;
    private string? _privilegeError;

    public WindowsMemoryCleaner(int? osBuild = null, bool? elevated = null)
    {
        _osBuild = osBuild ?? Environment.OSVersion.Version.Build;
        _supported = AreaCatalog.SupportedMask(_osBuild);
        IsElevated = elevated ?? Privileges.IsElevated();
    }

    public bool IsElevated { get; }

    public MemoryArea SupportedAreas => _supported;

    public string? PrivilegeError
    {
        get
        {
            EnsurePrivileges(out var error);
            return _privilegeOk ? null : error;
        }
    }

    public int OsBuild => _osBuild;

    public MemorySnapshot Sample()
    {
        var status = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>(),
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalMemoryStatusEx 失败");

        NativeMethods.GetSystemInfo(out var systemInfo);
        var listAvailable = TryQueryMemoryList(systemInfo.PageSize, out var free, out var standby, out var modified);
        var commitUsed = status.TotalPageFile >= status.AvailPageFile ? status.TotalPageFile - status.AvailPageFile : 0;

        return new MemorySnapshot(
            DateTimeOffset.Now,
            status.MemoryLoad,
            status.TotalPhys,
            status.AvailPhys,
            commitUsed,
            status.TotalPageFile,
            free,
            standby,
            modified,
            systemInfo.PageSize,
            listAvailable);
    }

    public CleanReport Clean(MemoryArea areas, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        var watch = Stopwatch.StartNew();
        var before = Sample();
        var outcomes = new List<AreaOutcome>();

        areas &= MemoryArea.All;
        if (areas == MemoryArea.None)
            return new CleanReport(startedAt, TimeSpan.Zero, areas, outcomes, before, before);

        if (!_local.Wait(TimeSpan.FromSeconds(5)))
            return BusyReport(startedAt, watch, areas, outcomes, before);

        Mutex? mutex = null;
        var mutexEntered = false;

        try
        {
            mutexEntered = TryEnterMutex(out mutex, TimeSpan.FromSeconds(5));
            if (!mutexEntered)
                return BusyReport(startedAt, watch, areas, outcomes, before);

            var privileged = IsElevated;
            string? privilegeError = null;
            if (privileged) privileged = EnsurePrivileges(out privilegeError);

            if (!privileged)
            {
                foreach (var info in AreaCatalog.All)
                    if ((areas & info.Area) != 0)
                        outcomes.Add(new AreaOutcome(info.Area, false, StatusPrivilegeNotHeld,
                            privilegeError ?? "需要管理员权限。请以管理员身份运行 ThinkBook Toolkit。"));

                return new CleanReport(startedAt, watch.Elapsed, areas, outcomes, before, before);
            }

            foreach (var info in AreaCatalog.All)
            {
                if ((areas & info.Area) == 0) continue;
                if (cancellationToken.IsCancellationRequested) break;

                if ((_supported & info.Area) == 0)
                {
                    outcomes.Add(new AreaOutcome(info.Area, false, StatusNotSupported, "当前系统不支持此区域"));
                    continue;
                }

                var (status, message) = Execute(info.Area);
                outcomes.Add(new AreaOutcome(info.Area, true, status, message));
            }

            return new CleanReport(startedAt, watch.Elapsed, areas, outcomes, before, Sample());
        }
        finally
        {
            if (mutex is not null)
            {
                try { if (mutexEntered) mutex.ReleaseMutex(); } catch { /* 忽略释放异常 */ }
                mutex.Dispose();
            }

            _local.Release();
        }
    }

    private static (int Status, string? Message) Execute(MemoryArea area) => area switch
    {
        MemoryArea.WorkingSet => MemoryListCommand(NativeMethods.MemoryEmptyWorkingSets, "MemoryEmptyWorkingSets"),
        MemoryArea.StandbyLowPriority => MemoryListCommand(NativeMethods.MemoryPurgeLowPriorityStandbyList, "MemoryPurgeLowPriorityStandbyList"),
        MemoryArea.StandbyList => MemoryListCommand(NativeMethods.MemoryPurgeStandbyList, "MemoryPurgeStandbyList"),
        MemoryArea.ModifiedPageList => MemoryListCommand(NativeMethods.MemoryFlushModifiedList, "MemoryFlushModifiedList"),
        MemoryArea.SystemFileCache => SetSystemFileCache(),
        MemoryArea.ModifiedFileCache => FlushVolumes(),
        MemoryArea.RegistryCache => RegistryCache(),
        _ => (StatusNotSupported, "未知区域"),
    };

    private static (int Status, string? Message) MemoryListCommand(int command, string name)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, command);
            var status = NativeMethods.NtSetSystemInformation(NativeMethods.SystemMemoryListInformation, buffer, sizeof(int));
            return status >= 0 ? (0, name) : (status, name + " 失败");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (int Status, string? Message) SetSystemFileCache()
    {
        var unlimited = new IntPtr(-1);
        if (NativeMethods.SetSystemFileCacheSize(unlimited, unlimited, 0))
            return (0, "SetSystemFileCacheSize");

        return (HResult(Marshal.GetLastWin32Error()), "SetSystemFileCacheSize 失败");
    }

    private static (int Status, string? Message) FlushVolumes()
    {
        var flushed = new List<string>();
        var failed = new List<string>();
        var firstError = 0;

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;

                var name = drive.Name.TrimEnd('\\');
                using var handle = NativeMethods.CreateFileW(@"\\.\" + name, 0xC0000000u, 3u, IntPtr.Zero, 3u, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    if (firstError == 0) firstError = Marshal.GetLastWin32Error();
                    failed.Add(name);
                    continue;
                }

                if (!NativeMethods.FlushFileBuffers(handle))
                {
                    if (firstError == 0) firstError = Marshal.GetLastWin32Error();
                    failed.Add(name);
                    continue;
                }

                flushed.Add(name);
            }
        }
        catch (Exception ex)
        {
            return (HResult(ex.HResult), "枚举固定卷失败");
        }

        var message = string.Join(", ", flushed);
        if (failed.Count > 0)
            message = (message.Length > 0 ? message + "; " : string.Empty) + string.Join(", ", failed) + " 失败";

        return firstError == 0 ? (0, message) : (HResult(firstError), message);
    }

    private static (int Status, string? Message) RegistryCache()
    {
        var status = NativeMethods.NtSetSystemInformation(NativeMethods.SystemRegistryReconciliationInformation, IntPtr.Zero, 0);
        return status >= 0 ? (0, "SystemRegistryReconciliationInformation") : (status, "SystemRegistryReconciliationInformation 失败");
    }

    private static bool TryQueryMemoryList(uint pageSize, out ulong freeBytes, out ulong standbyBytes, out ulong modifiedBytes)
    {
        freeBytes = 0;
        standbyBytes = 0;
        modifiedBytes = 0;

        const int capacity = 4096;
        var buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            var status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemMemoryListInformation, buffer, capacity, out _);
            if (status < 0) return false;

            var stride = IntPtr.Size;

            ulong Read(int index)
            {
                var offset = index * stride;
                return offset + stride <= capacity ? (ulong)Marshal.ReadIntPtr(buffer, offset).ToInt64() : 0UL;
            }

            var freePages = Read(0) + Read(1);
            var modifiedPages = Read(2);
            ulong standbyPages = 0;
            for (var i = 0; i < 8; i++) standbyPages += Read(5 + i);

            freeBytes = freePages * pageSize;
            standbyBytes = standbyPages * pageSize;
            modifiedBytes = modifiedPages * pageSize;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private bool EnsurePrivileges(out string? error)
    {
        lock (_privilegeSync)
        {
            if (!_privilegeChecked)
            {
                _privilegeChecked = true;
                _privilegeOk = Privileges.TryEnable(out var message, Privileges.ProfileSingleProcess, Privileges.IncreaseQuota);
                _privilegeError = message;
            }

            error = _privilegeError;
            return _privilegeOk;
        }
    }

    private static bool TryEnterMutex(out Mutex? mutex, TimeSpan timeout)
    {
        try
        {
            var candidate = new Mutex(false, MutexName);
            bool acquired;
            try
            {
                acquired = candidate.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                candidate.Dispose();
                mutex = null;
                return false;
            }

            mutex = candidate;
            return true;
        }
        catch
        {
            // 无法创建命名互斥体时退化为仅进程内串行，不影响清理本身。
            mutex = null;
            return true;
        }
    }

    private static CleanReport BusyReport(DateTimeOffset startedAt, Stopwatch watch, MemoryArea areas,
        List<AreaOutcome> outcomes, MemorySnapshot before)
    {
        foreach (var info in AreaCatalog.All)
            if ((areas & info.Area) != 0)
                outcomes.Add(new AreaOutcome(info.Area, false, StatusBusy, "另一次清理正在进行"));

        return new CleanReport(startedAt, watch.Elapsed, areas, outcomes, before, before);
    }

    private static int HResult(int win32Error) => unchecked((int)(0x80070000u | (uint)win32Error));
}
