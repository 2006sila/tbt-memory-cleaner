using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TbtMemoryCleaner.Core;

public readonly record struct ProcessMemoryInfo(int ProcessId, string Name, ulong WorkingSetBytes);

public static class ProcessMemoryReader
{
    /// <summary>按工作集从大到小取前 count 个进程（跳过 Idle/System 与小于 50 MB 的进程）。</summary>
    public static IReadOnlyList<ProcessMemoryInfo> Top(int count)
    {
        var list = new List<ProcessMemoryInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id <= 4) continue;

                var bytes = (ulong)process.WorkingSet64;
                if (bytes < 50UL * 1024 * 1024) continue;
                list.Add(new ProcessMemoryInfo(process.Id, process.ProcessName, bytes));
            }
            catch
            {
                // 进程已退出或无权读取，跳过。
            }
            finally
            {
                process.Dispose();
            }
        }

        return list.OrderByDescending(x => x.WorkingSetBytes).Take(count).ToArray();
    }
}

public static class ProcessTrimmer
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    /// <summary>只裁一个进程的工作集（EmptyWorkingSet）。需要管理员权限。</summary>
    public static bool Trim(int processId, out string? error)
    {
        var handle = NativeMethods.OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, processId);
        if (handle == IntPtr.Zero)
        {
            error = "OpenProcess 失败 (" + Marshal.GetLastWin32Error() + ")";
            return false;
        }

        try
        {
            if (!NativeMethods.EmptyWorkingSet(handle))
            {
                error = "EmptyWorkingSet 失败 (" + Marshal.GetLastWin32Error() + ")";
                return false;
            }

            error = null;
            return true;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}

/// <summary>系统级低内存通知对象（CreateMemoryResourceNotification），事件驱动，无需轮询采样。</summary>
public static class MemoryPressure
{
    private static readonly object Gate = new();
    private static IntPtr _handle = IntPtr.Zero;

    public static bool IsLow
    {
        get
        {
            try
            {
                lock (Gate)
                {
                    if (_handle == IntPtr.Zero)
                        _handle = NativeMethods.CreateMemoryResourceNotification(0); // LowMemoryResourceNotification

                    if (_handle == IntPtr.Zero) return false;
                    return NativeMethods.WaitForSingleObject(_handle, 0) == 0; // WAIT_OBJECT_0
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
