using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TbtMemoryCleaner.Core;

internal static class Privileges
{
    internal const string ProfileSingleProcess = "SeProfileSingleProcessPrivilege";
    internal const string IncreaseQuota = "SeIncreaseQuotaPrivilege";

    internal static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>逐个启用指定特权。任何一个未授予都会返回 false 并给出原因。</summary>
    internal static bool TryEnable(out string? error, params string[] privilegeNames)
    {
        error = null;
        try
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);
            foreach (var name in privilegeNames)
            {
                if (!NativeMethods.LookupPrivilegeValueW(null, name, out var luid))
                {
                    error = name + ": LookupPrivilegeValue 失败 (" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                var state = new NativeMethods.TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = NativeMethods.SePrivilegeEnabled,
                };

                if (!NativeMethods.AdjustTokenPrivileges(identity.Token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    error = name + ": AdjustTokenPrivileges 失败 (" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                var win32 = Marshal.GetLastWin32Error();
                if (win32 == NativeMethods.ErrorNotAllAssigned)
                {
                    error = name + ": 未授予。请以管理员身份运行 ThinkBook Toolkit。";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
