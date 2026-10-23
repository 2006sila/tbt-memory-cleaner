using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TbtMemoryCleaner.Core;

/// <summary>
/// 通过隐藏托盘图标发系统气泡通知（Shell_NotifyIcon + NIF_INFO）。
/// 不依赖 WinRT 投影、不注册 AUMID、不落盘；图标以 NIS_HIDDEN 添加，托盘里看不见。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellNotifier : IDisposable
{
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;

    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint NIF_STATE = 0x8;
    private const uint NIF_INFO = 0x10;

    private const uint NIS_HIDDEN = 0x1;
    private const uint NIIF_INFO = 0x1;
    private const uint NIIF_ERROR = 0x3;

    private const int WM_APP_SHOW = 0x8000 + 1;
    private const int NIN_BALLOONUSERCLICK = 0x0405;
    private const int WM_APP_TRAY = 0x8000 + 2;
    private const int WM_CLOSE = 0x0010;
    private const uint IDI_APPLICATION = 32512;

    private static readonly object Gate = new();
    private static ShellNotifier? _instance;
    private static WndProcDelegate? _wndProc;

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _sync = new();
    private Thread? _thread;
    private IntPtr _window;
    private bool _readyOk;
    private string _pendingTitle = "";
    private string _pendingText = "";
    private bool _pendingError;
    private Action? _onClick;
    private bool _disposed;

    public static string Diagnostics { get; private set; } = "";

    private static bool NotificationsAllowed()
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out var state) != 0) return true;
            return state is not (3 or 4); // D3D 全屏 / 演示模式时不打扰
        }
        catch
        {
            return true;
        }
    }

    public static bool Show(string title, string message, bool isError = false, Action? onClick = null)
    {
        try
        {
            if (!NotificationsAllowed()) return false;
            var notifier = _instance;
            if (notifier is null)
            {
                lock (Gate)
                {
                    notifier = _instance ??= Create();
                }
            }

            return notifier is not null && notifier.TryNotify(title, message, isError, onClick);
        }
        catch
        {
            return false;
        }
    }

    private static ShellNotifier? Create()
    {
        var notifier = new ShellNotifier();
        return notifier.Start() ? notifier : null;
    }

    private bool Start()
    {
        _wndProc = WindowProc;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "TbtMemoryCleaner.Notifier",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return _ready.Wait(TimeSpan.FromSeconds(2)) && _readyOk;
    }

    private void ThreadMain()
    {
        try
        {
            var wndClass = new WndClass
            {
                lpszClassName = "TbtMemoryCleanerNotify",
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc!),
            };

            if (Native.RegisterClassW(ref wndClass) == 0 && Marshal.GetLastWin32Error() != 1410)
                return;

            _window = Native.CreateWindowEx(0, "TbtMemoryCleanerNotify", "TbtMemoryCleanerNotify", 0x80000000u,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_window == IntPtr.Zero) return;

            var data = NewData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_STATE;
            data.uCallbackMessage = WM_APP_TRAY;
            data.hIcon = Native.LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
            data.szTip = "TBT Memory Cleaner";
            data.dwState = NIS_HIDDEN;
            data.dwStateMask = NIS_HIDDEN;

            var added = Native.Shell_NotifyIcon(NIM_ADD, ref data);
            Diagnostics = "add=" + added + "(err " + Marshal.GetLastWin32Error() + ")";
            if (!added) return;

            var version = data;
            version.uFlags = 0;
            version.uTimeoutOrVersion = 4;
            var versioned = Native.Shell_NotifyIcon(NIM_SETVERSION, ref version);
            Diagnostics += " ver=" + versioned + "(err " + Marshal.GetLastWin32Error() + ")";

            _readyOk = true;
            _ready.Set();

            Msg message;
            while (Native.GetMessage(out message, IntPtr.Zero, 0, 0))
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
        }
        catch
        {
            // 通知不可用不应该影响插件本身。
        }
        finally
        {
            try { _ready.Set(); } catch { }
            if (_window != IntPtr.Zero)
            {
                var data = NewData();
                Native.Shell_NotifyIcon(NIM_DELETE, ref data);
            }
        }
    }

    private bool TryNotify(string title, string message, bool isError, Action? onClick)
    {
        if (_disposed || !_readyOk || _window == IntPtr.Zero) return false;

        lock (_sync)
        {
            _pendingTitle = Truncate(title, 63);
            _pendingText = Truncate(message, 255);
            _pendingError = isError;
            _onClick = onClick;
        }

        return Native.PostMessage(_window, WM_APP_SHOW, IntPtr.Zero, IntPtr.Zero);
    }

    private void InvokeClick()
    {
        Action? callback;
        lock (_sync)
        {
            callback = _onClick;
            _onClick = null;
        }

        try { callback?.Invoke(); } catch { }
    }

    private void HandleShow()
    {
        string title;
        string text;
        bool isError;

        lock (_sync)
        {
            title = _pendingTitle;
            text = _pendingText;
            isError = _pendingError;
        }

        var data = NewData();
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = isError ? NIIF_ERROR : NIIF_INFO;
        data.uTimeoutOrVersion = 8000;
        data.szInfoTitle = title;
        data.szInfo = text;
        var modified = Native.Shell_NotifyIcon(NIM_MODIFY, ref data);
        Diagnostics = "add/ver ok; last info=" + modified + "(err " + Marshal.GetLastWin32Error() + ")";
    }

    private NotifyIconData NewData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window,
        uID = 0x4D43,
    };

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value[..max];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_window != IntPtr.Zero) Native.PostMessage(_window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // 忽略
        }

        _ready.Dispose();
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_APP_SHOW)
        {
            _instance?.HandleShow();
            return IntPtr.Zero;
        }

        if (message == WM_APP_TRAY)
        {
            // 托盘事件（NIN_BALLOONSHOW/TIMEOUT/CLICK…）由 shell 发到这个回调消息。
            var code = (int)(lParam.ToInt64() & 0xFFFF);
            if (code == NIN_BALLOONUSERCLICK) _instance?.InvokeClick();
            return IntPtr.Zero;
        }

        if (message == WM_CLOSE)
        {
            Native.DestroyWindow(hwnd);
            Native.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    private static class Native
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassW(ref WndClass wndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        internal static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

        [DllImport("user32.dll")]
        internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool GetMessage(out Msg message, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        internal static extern bool TranslateMessage(ref Msg message);

        [DllImport("user32.dll")]
        internal static extern IntPtr DispatchMessage(ref Msg message);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern void PostQuitMessage(int code);
    }
}
