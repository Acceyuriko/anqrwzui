using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace anqrwzui;

public partial class Main : Form
{
    private DxgiScreenCapture? _screenCapture;
    private YoloV8Detector? _yoloDetector;
    private PictureBox? _pictureBox;
    private System.Windows.Forms.Timer? _captureTimer;
    private System.Windows.Forms.Timer? _detectionTimer;
    private bool _isCapturing = false;
    private bool _isDetecting = false;
    private Bitmap? _currentFrame;
    private readonly object _frameLock = new object();

    public Main()
    {
        Logger.Info("应用程序启动");
        InitializeComponent();
        InitializeCaptureComponents();
        InitializeDetection();
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

        // 创建控制面板
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.LightGray
        };

        var startButton = new Button
        {
            Text = "开始截取",
            Location = new Point(10, 8),
            Size = new Size(80, 25)
        };
        startButton.Click += StartButton_Click;

        var stopButton = new Button
        {
            Text = "停止截取",
            Location = new Point(100, 8),
            Size = new Size(80, 25)
        };
        stopButton.Click += StopButton_Click;

        var detectButton = new Button
        {
            Text = "开始检测",
            Location = new Point(190, 8),
            Size = new Size(80, 25)
        };
        detectButton.Click += DetectButton_Click;

        var statusLabel = new Label
        {
            Text = "就绪",
            Location = new Point(280, 12),
            Size = new Size(200, 20),
            ForeColor = Color.DarkGreen
        };

        panel.Controls.AddRange(new Control[] { startButton, stopButton, detectButton, statusLabel });
        this.Controls.Add(panel);
        this.Controls.SetChildIndex(panel, 0);

        // 初始化截取计时器 - 设置为最快速度（约16ms间隔，约60FPS）
        _captureTimer = new System.Windows.Forms.Timer();
        _captureTimer.Interval = 16; // 约60FPS
        _captureTimer.Tick += CaptureTimer_Tick;

        // 初始化检测计时器 - 检测频率可以比截取频率低一些
        _detectionTimer = new System.Windows.Forms.Timer();
        _detectionTimer.Interval = 33; // 约30FPS检测
        _detectionTimer.Tick += DetectionTimer_Tick;

        Logger.Debug("截取组件初始化完成");
    }

    private void InitializeDetection()
    {
        Logger.Info("初始化目标检测");

        try
        {
            // 加载YOLOv8模型
            var modelPath = @"Model\yolov8n.onnx";
            _yoloDetector = new YoloV8Detector(modelPath);
            Logger.Info("目标检测初始化成功");
        }
        catch (Exception ex)
        {
            Logger.Error("目标检测初始化失败", ex);
            MessageBox.Show($"加载YOLOv8模型失败: {ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_isCapturing) return;

        Logger.Info("开始屏幕截取");

        try
        {
            _screenCapture = new DxgiScreenCapture();
            _isCapturing = true;
            _captureTimer?.Start();
            Logger.Info("屏幕截取已启动");
        }
        catch (Exception ex)
        {
            Logger.Error("屏幕截取启动失败", ex);
            MessageBox.Show($"初始化屏幕截取失败: {ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        if (!_isCapturing) return;

        Logger.Info("停止屏幕截取");

        _isCapturing = false;
        _isDetecting = false;
        _captureTimer?.Stop();
        _detectionTimer?.Stop();
        _screenCapture?.Dispose();
        _screenCapture = null;

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }

        if (_pictureBox != null)
        {
            _pictureBox.Image = null;
        }

        Logger.Info("屏幕截取已停止");
    }

    private void DetectButton_Click(object? sender, EventArgs e)
    {
        if (_yoloDetector == null)
        {
            Logger.Warning("尝试启动检测但模型未加载");
            MessageBox.Show("YOLOv8模型未加载成功", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _isDetecting = !_isDetecting;
        var button = sender as Button;
        if (button != null)
        {
            button.Text = _isDetecting ? "停止检测" : "开始检测";
        }

        if (_isDetecting)
        {
            Logger.Info("开始目标检测");
            _detectionTimer?.Start();
        }
        else
        {
            Logger.Info("停止目标检测");
            _detectionTimer?.Stop();
        }
    }

    private void CaptureTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isCapturing || _screenCapture == null || _pictureBox == null) return;

        try
        {
            var bitmap = _screenCapture.CaptureScreen();
            if (bitmap != null)
            {
                lock (_frameLock)
                {
                    // 更新当前帧
                    _currentFrame?.Dispose();
                    _currentFrame = bitmap;
                }

                // 如果不进行检测，直接显示原始图像
                if (!_isDetecting)
                {
                    UpdatePictureBox(bitmap);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("屏幕截取过程中发生错误", ex);
        }
    }

    private void DetectionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDetecting || _yoloDetector == null || _pictureBox == null) return;

        Bitmap? frameToProcess = null;
        lock (_frameLock)
        {
            if (_currentFrame != null)
            {
                frameToProcess = new Bitmap(_currentFrame);
            }
        }

        if (frameToProcess != null)
        {
            try
            {
                Logger.Debug("开始目标检测处理");

                // 进行目标检测
                var detections = _yoloDetector.Detect(frameToProcess);

                // 绘制检测结果
                var resultImage = DetectionRenderer.DrawDetections(frameToProcess, detections);

                // 更新显示
                UpdatePictureBox(resultImage);

                frameToProcess.Dispose();
                Logger.Debug($"目标检测完成，发现 {detections.Count} 个目标");
            }
            catch (Exception ex)
            {
                Logger.Error("目标检测过程中发生错误", ex);
                frameToProcess.Dispose();
            }
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

        _isCapturing = false;
        _isDetecting = false;
        _captureTimer?.Stop();
        _detectionTimer?.Stop();
        _screenCapture?.Dispose();
        _yoloDetector?.Dispose();

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
        }

        base.OnFormClosing(e);

        Logger.Info("应用程序已关闭");
    }
}