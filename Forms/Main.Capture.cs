using System.Diagnostics;

namespace anqrwzui;

public partial class Main
{
    private void InitializeDetection()
    {
        Logger.Info("初始化目标检测");

        try
        {
            var modelPath = @"Model\best.onnx";
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
}