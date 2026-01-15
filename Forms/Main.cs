using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace anqrwzui;

public partial class Main : Form
{
    private DxgiScreenCapture? _screenCapture;
    private PictureBox? _pictureBox;
    private System.Windows.Forms.Timer? _captureTimer;
    private int _targetFps = 60;
    private bool _isCapturing = false;

    public Main()
    {
        InitializeComponent();
        InitializeCaptureComponents();
    }

    private void InitializeCaptureComponents()
    {
        // 设置窗体属性
        this.Text = "DXGI 屏幕截取 - 60-120 FPS";
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

        var fpsLabel = new Label
        {
            Text = "FPS:",
            Location = new Point(200, 12),
            Size = new Size(40, 20)
        };

        var fpsComboBox = new ComboBox
        {
            Location = new Point(240, 8),
            Size = new Size(80, 25)
        };
        fpsComboBox.Items.AddRange(new object[] { "60", "75", "90", "120" });
        fpsComboBox.SelectedIndex = 0;
        fpsComboBox.SelectedIndexChanged += (s, e) =>
        {
            if (int.TryParse(fpsComboBox.SelectedItem?.ToString(), out int fps))
            {
                _targetFps = fps;
                if (_isCapturing)
                {
                    RestartCaptureTimer();
                }
            }
        };

        panel.Controls.AddRange(new Control[] { startButton, stopButton, fpsLabel, fpsComboBox });
        this.Controls.Add(panel);
        this.Controls.SetChildIndex(panel, 0);

        // 初始化截取计时器
        _captureTimer = new System.Windows.Forms.Timer();
        _captureTimer.Tick += CaptureTimer_Tick;
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_isCapturing) return;

        try
        {
            _screenCapture = new DxgiScreenCapture();
            _isCapturing = true;
            RestartCaptureTimer();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化屏幕截取失败: {ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        if (!_isCapturing) return;

        _isCapturing = false;
        _captureTimer?.Stop();
        _screenCapture?.Dispose();
        _screenCapture = null;
        if (_pictureBox != null)
        {
            _pictureBox.Image = null;
        }
    }

    private void RestartCaptureTimer()
    {
        if (_captureTimer == null) return;
        _captureTimer.Stop();
        _captureTimer.Interval = 1000 / _targetFps;
        _captureTimer.Start();
    }

    private void CaptureTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isCapturing || _screenCapture == null || _pictureBox == null) return;

        try
        {
            var bitmap = _screenCapture.CaptureScreen();
            if (bitmap != null)
            {
                // 使用BeginInvoke确保UI线程安全
                if (_pictureBox.InvokeRequired)
                {
                    _pictureBox.BeginInvoke(new Action<Bitmap>(UpdatePictureBox), bitmap);
                }
                else
                {
                    UpdatePictureBox(bitmap);
                }
            }
        }
        catch (Exception ex)
        {
            // 记录错误但不中断截取
            System.Diagnostics.Debug.WriteLine($"截取错误: {ex.Message}");
        }
    }

    private void UpdatePictureBox(Bitmap bitmap)
    {
        if (_pictureBox == null) return;

        var oldImage = _pictureBox.Image;
        _pictureBox.Image = bitmap;
        oldImage?.Dispose();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _isCapturing = false;
        _captureTimer?.Stop();
        _screenCapture?.Dispose();
        base.OnFormClosing(e);
    }
}