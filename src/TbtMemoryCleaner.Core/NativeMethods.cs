using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TbtMemoryCleaner.Core;

internal static class NativeMethods
{
    internal const int SystemMemoryListInformation = 0x50;
    internal const int SystemRegistryReconciliationInformation = 0x9B;

    internal const int MemoryEmptyWorkingSets = 2;
    internal const int MemoryFlushModifiedList = 3;
    internal const int MemoryPurgeStandbyList = 4;
    internal const int MemoryPurgeLowPriorityStandbyList = 5;

    internal const uint SePrivilegeEnabled = 0x00000002;
    internal const int ErrorNotAllAssigned = 1300;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemInfo
    {
        public ushort ProcessorArchitecture;
        public ushort Reserved;
        public uint PageSize;
        public IntPtr MinimumApplicationAddress;
        public IntPtr MaximumApplicationAddress;
        public IntPtr ActiveProcessorMask;
        public uint NumberOfProcessors;
        public uint ProcessorType;
        public uint AllocationGranularity;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenPrivileges
    {
        public int PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    internal static extern void GetSystemInfo(out SystemInfo systemInfo);

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation,
        int systemInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    internal static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation,
        int systemInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushFileBuffers(SafeFileHandle file);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateMemoryResourceNotification(int notificationType);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out int state);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges, ref TokenPrivileges newState,
        int bufferLength, IntPtr previousState, IntPtr returnLength);
}
