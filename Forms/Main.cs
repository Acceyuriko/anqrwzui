using System.Diagnostics;
using System.Runtime.InteropServices;

namespace anqrwzui;

public partial class Main : Form
{
    private DxgiScreenCapture? _screenCapture;
    private YoloV8Detector? _yoloDetector;
    private PictureBox? _pictureBox;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _isCapturing = false;
    private Bitmap? _currentFrame;
    private readonly object _frameLock = new object();
    private Label? _deviceLabel; // 添加成员变量
    private Label? _fpsLabel;
    private Button? _toggleCaptureButton;
    private int _fpsCount = 0;
    private DateTime _fpsWindowStart = DateTime.UtcNow;
    private long _lastCaptureTicks = 0;
    private readonly double _targetFrameMs = 16.0; // 约60FPS
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private IntPtr _mouseHookId = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _mouseProc;
    private readonly MouseController _mouseController = new MouseController();
    private CancellationTokenSource? _mouseMoveCts;

    public Main()
    {
        Logger.Info("应用程序启动");
        InitializeComponent();
        InitializeCaptureComponents();
        InitializeDetection();
        SetupGlobalMouseHook();
        Logger.Info("应用程序初始化完成");
    }

    private void InitializeCaptureComponents()
    {
        Logger.Debug("初始化截取组件");

        // 设置窗体属性
        this.Text = "DXGI 屏幕截取 + YOLOv8 目标检测";
        this.ClientSize = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;

        // 创建图片框用于显示截取的屏幕
        _pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };
        this.Controls.Add(_pictureBox);

        // 创建控制面板（使用 FlowLayout 避免控件重叠）
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.LightGray,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 8, 10, 8)
        };

        _toggleCaptureButton = new Button
        {
            Text = "开始",
            Size = new Size(96, 28),
            Margin = new Padding(0, 0, 12, 0)
        };
        _toggleCaptureButton.Click += ToggleCapture_Click;

        // 创建推理设备标签
        _deviceLabel = new Label
        {
            Text = "推理设备: 未知",
            AutoSize = true,
            ForeColor = Color.DarkBlue,
            Margin = new Padding(0, 6, 18, 0)
        };
        _fpsLabel = new Label
        {
            Text = "检测FPS: -",
            AutoSize = true,
            ForeColor = Color.Black,
            Margin = new Padding(0, 6, 0, 0)
        };

        panel.Controls.AddRange(new Control[] { _toggleCaptureButton, _deviceLabel, _fpsLabel });
        this.Controls.Add(panel);
        this.Controls.SetChildIndex(panel, 0);

        Logger.Debug("截取组件初始化完成");
    }

    private void InitializeDetection()
    {
        Logger.Info("初始化目标检测");

        try
        {
            var modelPath = @"Model\yolov8n.onnx";
            _yoloDetector = new YoloV8Detector(modelPath, UpdateDeviceLabel);
            Logger.Info("目标检测初始化成功");
        }
        catch (Exception ex)
        {
            Logger.Error("目标检测初始化失败", ex);
            MessageBox.Show($"加载YOLOv8模型失败: {ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateDeviceLabel("推理设备: 初始化失败");
        }
    }

    // 添加设备标签更新方法
    private void UpdateDeviceLabel(string device)
    {
        if (_deviceLabel == null) return;
        if (_deviceLabel.InvokeRequired)
        {
            _deviceLabel.BeginInvoke(new Action(() => _deviceLabel.Text = $"推理设备: {device}"));
        }
        else
        {
            _deviceLabel.Text = $"推理设备: {device}";
        }
    }

    private void ToggleCapture_Click(object? sender, EventArgs e)
    {
        if (_isCapturing)
        {
            StopCapture();
        }
        else
        {
            StartCapture();
        }
    }

    private void StartCapture()
    {
        if (_isCapturing) return;

        Logger.Info("开始屏幕截取");

        try
        {
            _screenCapture = new DxgiScreenCapture();
            _isCapturing = true;
            _lastCaptureTicks = _stopwatch.ElapsedTicks;
            _captureCts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoopAsync(_captureCts.Token));
            UpdateToggleButtonText();
            Logger.Info("屏幕截取已启动");
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            UpdateToggleButtonText();
            Logger.Error("屏幕截取启动失败", ex);
            MessageBox.Show($"初始化屏幕截取失败: {ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopCapture()
    {
        if (!_isCapturing) return;

        Logger.Info("停止屏幕截取");

        _isCapturing = false;
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureCts = null;
        _screenCapture?.Dispose();
        _screenCapture = null;
        ResetFps();

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }

        if (_pictureBox != null)
        {
            _pictureBox.Image = null;
        }

        UpdateToggleButtonText();

        Logger.Info("屏幕截取已停止");
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var nowTicks = _stopwatch.ElapsedTicks;
            var elapsedMs = (nowTicks - _lastCaptureTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < _targetFrameMs)
            {
                var delayMs = Math.Max(1, (int)(_targetFrameMs - elapsedMs));
                try { await Task.Delay(delayMs, token); } catch (TaskCanceledException) { break; }
                continue;
            }
            _lastCaptureTicks = nowTicks;

            if (!_isCapturing || _screenCapture == null || _pictureBox == null)
            {
                try { await Task.Delay(50, token); } catch (TaskCanceledException) { break; }
                continue;
            }

            try
            {
                var bitmap = _screenCapture.CaptureScreen();
                if (bitmap != null)
                {
                    Bitmap displayBitmap = bitmap;

                    if (_yoloDetector != null)
                    {
                        try
                        {
                            var detections = _yoloDetector.Detect(bitmap);
                            displayBitmap = DetectionRenderer.DrawDetections(bitmap, detections);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("目标检测过程中发生错误", ex);
                            displayBitmap = bitmap;
                        }
                    }

                    lock (_frameLock)
                    {
                        _currentFrame?.Dispose();
                        _currentFrame = displayBitmap;
                    }

                    UpdatePictureBox(displayBitmap);

                    UpdateFps();

                    if (!ReferenceEquals(displayBitmap, bitmap))
                    {
                        bitmap.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("屏幕截取过程中发生错误", ex);
            }
        }
    }

    private void UpdateFps()
    {
        _fpsCount++;
        var now = DateTime.UtcNow;
        var elapsed = now - _fpsWindowStart;
        if (elapsed.TotalSeconds >= 1.0)
        {
            var fps = _fpsCount / elapsed.TotalSeconds;
            if (_fpsLabel != null)
            {
                _fpsLabel.Text = $"检测FPS: {fps:F1}";
            }
            _fpsCount = 0;
            _fpsWindowStart = now;
        }
    }

    private void StartMouseDownMove()
    {
        if (_mouseMoveCts != null)
            return;

        _mouseMoveCts = new CancellationTokenSource();
        var token = _mouseMoveCts.Token;
        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    _mouseController.MoveRelative(0, 2);
                    try { await Task.Delay(10, token); } catch (TaskCanceledException) { break; }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("持续鼠标下移任务异常", ex);
            }
        }, token);
    }

    private void StopMouseDownMove()
    {
        _mouseMoveCts?.Cancel();
        _mouseMoveCts?.Dispose();
        _mouseMoveCts = null;
    }

    private void ResetFps()
    {
        _fpsCount = 0;
        _fpsWindowStart = DateTime.UtcNow;
        if (_fpsLabel != null)
        {
            _fpsLabel.Text = "检测FPS: -";
        }
    }

    private void UpdateToggleButtonText()
    {
        if (_toggleCaptureButton == null) return;
        var text = _isCapturing ? "停止" : "开始";
        if (_toggleCaptureButton.InvokeRequired)
        {
            _toggleCaptureButton.BeginInvoke(new Action(() => _toggleCaptureButton.Text = text));
        }
        else
        {
            _toggleCaptureButton.Text = text;
        }
    }

    private void UpdatePictureBox(Bitmap bitmap)
    {
        if (_pictureBox == null) return;

        if (_pictureBox.InvokeRequired)
        {
            _pictureBox.BeginInvoke(new Action<Bitmap>(UpdatePictureBoxInternal), bitmap);
        }
        else
        {
            UpdatePictureBoxInternal(bitmap);
        }
    }

    private void UpdatePictureBoxInternal(Bitmap bitmap)
    {
        if (_pictureBox == null) return;

        var oldImage = _pictureBox.Image;
        _pictureBox.Image = bitmap;
        oldImage?.Dispose();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Logger.Info("应用程序正在关闭");

        StopCapture();
        _yoloDetector?.Dispose();
        StopMouseDownMove();
        ReleaseGlobalMouseHook();

        base.OnFormClosing(e);

        Logger.Info("应用程序已关闭");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 再次确保资源释放
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

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;

        if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
        {
            StartMouseDownMove();
        }
        else if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
        {
            StopMouseDownMove();
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static class NativeMethods
    {
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

#pragma warning disable CS0649 // 字段由 Win32 填充，代码中不直接赋值
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
#pragma warning restore CS0649

        private const int WH_MOUSE_LL = 14;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

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
    }
}