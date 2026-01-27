using System.Diagnostics;
using System.Runtime.InteropServices;

namespace anqrwzui;

public partial class Main : Form
{
    public Main()
    {
        Logger.Info("应用程序启动");
        InitializeComponent();
        InitializeConfigPath();
        EnsureConfigFileExists();
        LoadConfigOptions();
        InitializeCaptureComponents();
        InitializeDetection();
        SetupConfigWatcher();
        SetupGlobalMouseHook();
        SetupGlobalKeyboardHook();
        Logger.Info("应用程序初始化完成");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Logger.Info("应用程序正在关闭");

        StopCapture();
        _yoloDetector?.Dispose();
        StopMouseDownMove();
        ReleaseGlobalMouseHook();
        ReleaseGlobalKeyboardHook();
        DisposeConfigWatcher();

        base.OnFormClosing(e);

        Logger.Info("应用程序已关闭");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopCapture();
        StopMouseDownMove();
        base.OnFormClosed(e);
    }

    private void SetupGlobalMouseHook()
    {
        _mouseProc = MouseHookCallback;
        _mouseHookId = NativeMethods.SetHook(_mouseProc);
        if (_mouseHookId == IntPtr.Zero)
        {
            Logger.Error("全局鼠标钩子设置失败");
        }
        else
        {
            Logger.Info("全局鼠标钩子已启动");
        }
    }

    private void ReleaseGlobalMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
            Logger.Info("全局鼠标钩子已释放");
        }
        _mouseProc = null;
    }

    private void SetupGlobalKeyboardHook()
    {
        _keyboardProc = KeyboardHookCallback;
        _keyboardHookId = NativeMethods.SetKeyboardHook(_keyboardProc);
        if (_keyboardHookId == IntPtr.Zero)
        {
            Logger.Error("全局键盘钩子设置失败");
        }
        else
        {
            Logger.Info("全局键盘钩子已启动");
        }
    }

    private void ReleaseGlobalKeyboardHook()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
            Logger.Info("全局键盘钩子已释放");
        }
        _keyboardProc = null;
    }

    private static class NativeMethods
    {
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

#pragma warning disable CS0649
        public struct POINT
        {
            public int x;
            public int y;
        }

        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }
#pragma warning restore CS0649

        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        public static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            var handle = GetModuleHandle(curModule.ModuleName);
            return SetWindowsHookEx(WH_MOUSE_LL, proc, handle, 0);
        }

        public static IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            var handle = GetModuleHandle(curModule.ModuleName);
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, handle, 0);
        }
    }
}
