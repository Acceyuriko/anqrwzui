using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

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
            _yoloDetector.SetSelfFilterAreaRatioThreshold(_selfFilterAreaRatio);
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
            EvaluateMouseMoveState();
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
        StopMousePhysics(clearTargetSnapshot: true);

        if (_pictureBox != null)
        {
            var oldImage = _pictureBox.Image;
            _pictureBox.Image = null;
            oldImage?.Dispose();
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
                            detections = FilterSelfHeadDetections(detections, bitmap.Width, bitmap.Height);
                            UpdateAimTargetSnapshot(detections, bitmap.Width, bitmap.Height);
                            displayBitmap = DetectionRenderer.DrawDetections(bitmap, detections);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("目标检测过程中发生错误", ex);
                            displayBitmap = bitmap;
                        }
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

    private List<DetectionResult> FilterSelfHeadDetections(List<DetectionResult> detections, int screenWidth, int screenHeight)
    {
        if (detections.Count == 0 || IsFirstPersonModeActive())
        {
            return detections;
        }

        var uniqueLargestDetection = GetUniqueLargestDetection(detections);
        if (uniqueLargestDetection != null)
        {
            return detections.Where(d => !ReferenceEquals(d, uniqueLargestDetection)).ToList();
        }

        return detections.Where(d => !IsLikelySelfHeadByScreenHeuristic(d.BoundingBox, screenWidth, screenHeight)).ToList();
    }

    private DetectionResult? GetUniqueLargestDetection(IReadOnlyList<DetectionResult> detections)
    {
        if (detections.Count == 0)
        {
            return null;
        }

        var maxArea = detections.Max(d => GetDetectionArea(d.BoundingBox));
        if (maxArea <= 0f)
        {
            return null;
        }

        var nearMaxDetections = detections
            .Where(d => GetDetectionArea(d.BoundingBox) >= maxArea * LargestDetectionAreaSimilarityThreshold)
            .ToList();

        return nearMaxDetections.Count == 1 ? nearMaxDetections[0] : null;
    }

    private void UpdateAimTargetSnapshot(IReadOnlyList<DetectionResult> detections, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            lock (_aimTargetLock)
            {
                ClearAimTargetStateLocked(clearPublishedSnapshot: true);
            }

            return;
        }

        lock (_aimTargetLock)
        {
            if (TrySelectAimTargetLocked(detections, imageWidth, imageHeight, out _, out var offsetX, out var offsetY, out var staleFrames))
            {
                _latestAimTargetOffsetX = offsetX;
                _latestAimTargetOffsetY = offsetY;
                _latestAimTargetStaleFrames = staleFrames;
                if (staleFrames == 0)
                {
                    _latestAimTargetVersion++;
                }

                return;
            }

            ClearAimTargetStateLocked(clearPublishedSnapshot: true);
        }
    }

    private bool TrySelectAimTargetLocked(IReadOnlyList<DetectionResult> detections, int imageWidth, int imageHeight, out RectangleF selectedBox, out double offsetX, out double offsetY, out int staleFrames)
    {
        selectedBox = RectangleF.Empty;
        offsetX = 0.0;
        offsetY = 0.0;
        staleFrames = 0;

        if (_hasLockedAimTarget)
        {
            var associatedDetection = FindAssociatedDetectionLocked(detections, imageWidth, imageHeight);
            if (associatedDetection != null)
            {
                selectedBox = associatedDetection.BoundingBox;
                UpdateLockedAimTargetLocked(selectedBox, imageWidth, imageHeight, out offsetX, out offsetY);
                return true;
            }

            if (TryHoldLockedAimTargetLocked(out selectedBox, out offsetX, out offsetY, out staleFrames))
            {
                return true;
            }
        }

        var nearestDetection = GetNearestDetectionToCenter(detections, imageWidth, imageHeight);
        if (nearestDetection == null)
        {
            return false;
        }

        selectedBox = nearestDetection.BoundingBox;
        if (IsAimTargetOffsetOutOfRange(selectedBox, imageWidth, imageHeight))
        {
            return false;
        }

        UpdateLockedAimTargetLocked(selectedBox, imageWidth, imageHeight, out offsetX, out offsetY);
        return true;
    }

    private DetectionResult? FindAssociatedDetectionLocked(IReadOnlyList<DetectionResult> detections, int imageWidth, int imageHeight)
    {
        if (!_hasLockedAimTarget || detections.Count == 0)
        {
            return null;
        }

        var associationDistance = GetAssociationDistanceThresholdLocked(imageWidth, imageHeight);
        DetectionResult? bestDetection = null;
        double bestScore = double.MinValue;

        foreach (var detection in detections)
        {
            var box = detection.BoundingBox;
            if (IsAimTargetOffsetOutOfRange(box, imageWidth, imageHeight))
            {
                continue;
            }

            var centerDistance = GetBoxCenterDistance(box, _lockedAimTargetBox);
            var overlap = CalculateBoxIoU(box, _lockedAimTargetBox);
            if (centerDistance > associationDistance && overlap <= 0.02f)
            {
                continue;
            }

            var normalizedDistance = centerDistance / Math.Max(associationDistance, 1.0);
            var score = overlap - normalizedDistance;
            if (score > bestScore)
            {
                bestScore = score;
                bestDetection = detection;
            }
        }

        return bestDetection;
    }

    private double GetAssociationDistanceThresholdLocked(int imageWidth, int imageHeight)
    {
        var imageThreshold = Math.Min(imageWidth, imageHeight) * ClampAimTargetAssociationDistanceRatio(_aimTargetAssociationDistanceRatio);
        var boxThreshold = Math.Max(Math.Max(_lockedAimTargetBox.Width, _lockedAimTargetBox.Height) * 0.85, 18.0);
        return Math.Max(imageThreshold, boxThreshold);
    }

    private bool TryHoldLockedAimTargetLocked(out RectangleF selectedBox, out double offsetX, out double offsetY, out int staleFrames)
    {
        selectedBox = RectangleF.Empty;
        offsetX = 0.0;
        offsetY = 0.0;
        staleFrames = 0;

        if (!_hasLockedAimTarget)
        {
            return false;
        }

        _lockedAimTargetLostFrames++;
        if (_lockedAimTargetLostFrames > ClampAimTargetHoldFrames(_aimTargetHoldFrames))
        {
            ClearAimTargetStateLocked(clearPublishedSnapshot: false);
            return false;
        }

        selectedBox = _lockedAimTargetBox;
        offsetX = _lockedAimTargetOffsetX;
        offsetY = _lockedAimTargetOffsetY;
        staleFrames = _lockedAimTargetLostFrames;
        return true;
    }

    private void UpdateLockedAimTargetLocked(RectangleF box, int imageWidth, int imageHeight, out double offsetX, out double offsetY)
    {
        GetAimTargetOffset(box, imageWidth, imageHeight, out offsetX, out offsetY);
        _hasLockedAimTarget = true;
        _lockedAimTargetBox = box;
        _lockedAimTargetOffsetX = offsetX;
        _lockedAimTargetOffsetY = offsetY;
        _lockedAimTargetLostFrames = 0;
    }

    private bool IsAimTargetOffsetOutOfRange(RectangleF box, int imageWidth, int imageHeight)
    {
        GetAimTargetOffset(box, imageWidth, imageHeight, out var offsetX, out var offsetY);
        var maxOffsetRatio = ClampAimTargetMaxOffsetRatio(_aimTargetMaxOffsetRatio);
        return Math.Abs(offsetX) > imageWidth * maxOffsetRatio || Math.Abs(offsetY) > imageHeight * maxOffsetRatio;
    }

    private static void GetAimReferencePoint(int imageWidth, int imageHeight, out double referenceX, out double referenceY)
    {
        referenceX = imageWidth * 0.5;
        referenceY = imageHeight * 0.5 + AimReferenceYOffsetPixels;
    }

    private static void GetAimTargetOffset(RectangleF box, int imageWidth, int imageHeight, out double offsetX, out double offsetY)
    {
        var boxCenterX = box.Left + box.Width * 0.5f;
        var boxCenterY = box.Top + box.Height * 0.5f;
        GetAimReferencePoint(imageWidth, imageHeight, out var referenceX, out var referenceY);
        offsetX = boxCenterX - referenceX;
        offsetY = boxCenterY - referenceY;
    }

    private static double GetBoxCenterDistance(RectangleF firstBox, RectangleF secondBox)
    {
        var firstCenterX = firstBox.Left + firstBox.Width * 0.5f;
        var firstCenterY = firstBox.Top + firstBox.Height * 0.5f;
        var secondCenterX = secondBox.Left + secondBox.Width * 0.5f;
        var secondCenterY = secondBox.Top + secondBox.Height * 0.5f;
        var deltaX = firstCenterX - secondCenterX;
        var deltaY = firstCenterY - secondCenterY;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static float CalculateBoxIoU(RectangleF firstBox, RectangleF secondBox)
    {
        var left = Math.Max(firstBox.Left, secondBox.Left);
        var top = Math.Max(firstBox.Top, secondBox.Top);
        var right = Math.Min(firstBox.Right, secondBox.Right);
        var bottom = Math.Min(firstBox.Bottom, secondBox.Bottom);

        var overlapWidth = right - left;
        var overlapHeight = bottom - top;
        if (overlapWidth <= 0f || overlapHeight <= 0f)
        {
            return 0f;
        }

        var overlapArea = overlapWidth * overlapHeight;
        var unionArea = (firstBox.Width * firstBox.Height) + (secondBox.Width * secondBox.Height) - overlapArea;
        if (unionArea <= 0f)
        {
            return 0f;
        }

        return overlapArea / unionArea;
    }

    private void ClearAimTargetStateLocked(bool clearPublishedSnapshot)
    {
        _hasLockedAimTarget = false;
        _lockedAimTargetBox = RectangleF.Empty;
        _lockedAimTargetOffsetX = 0.0;
        _lockedAimTargetOffsetY = 0.0;
        _lockedAimTargetLostFrames = 0;

        if (!clearPublishedSnapshot)
        {
            return;
        }

        _latestAimTargetVersion = 0;
        _latestAimTargetStaleFrames = 0;
        _latestAimTargetOffsetX = 0.0;
        _latestAimTargetOffsetY = 0.0;
    }

    private DetectionResult? GetNearestDetectionToCenter(IReadOnlyList<DetectionResult> detections, int imageWidth, int imageHeight)
    {
        if (detections.Count == 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return null;
        }

        GetAimReferencePoint(imageWidth, imageHeight, out var referenceX, out var referenceY);
        DetectionResult? nearestDetection = null;
        double nearestDistanceSquared = double.MaxValue;

        foreach (var detection in detections)
        {
            var box = detection.BoundingBox;
            var boxCenterX = box.Left + box.Width * 0.5f;
            var boxCenterY = box.Top + box.Height * 0.5f;
            var deltaX = boxCenterX - referenceX;
            var deltaY = boxCenterY - referenceY;
            var distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestDetection = detection;
            }
        }

        return nearestDetection;
    }

    private void ResetAimTargetSnapshot()
    {
        lock (_aimTargetLock)
        {
            ClearAimTargetStateLocked(clearPublishedSnapshot: true);
        }
    }

    private static float GetDetectionArea(RectangleF box)
    {
        return Math.Max(0f, box.Width) * Math.Max(0f, box.Height);
    }

    private bool IsLikelySelfHeadByScreenHeuristic(RectangleF box, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            return false;

        float areaRatio = (box.Width * box.Height) / (screenWidth * screenHeight);
        float heightRatio = box.Height / screenHeight;
        float centerX = box.Left + box.Width * 0.5f;
        float centerY = box.Top + box.Height * 0.5f;
        const float selfFilterMinHeightRatio = 0.12f;
        const float crosshairLowerLeftMinOffsetXRatio = -0.28f;
        const float crosshairLowerLeftMaxOffsetXRatio = 0.08f;
        const float crosshairLowerLeftMinOffsetYRatio = 0.02f;
        const float crosshairLowerLeftMaxOffsetYRatio = 0.48f;
        const float nearBottomOffsetYRatio = 0.18f;

        GetAimReferencePoint(screenWidth, screenHeight, out var referenceX, out var referenceY);

        // 使用统一的瞄准参考点；自身头部常出现在其左下区域
        bool inCrosshairLowerLeftRegion =
            centerX >= referenceX + screenWidth * crosshairLowerLeftMinOffsetXRatio &&
            centerX <= referenceX + screenWidth * crosshairLowerLeftMaxOffsetXRatio &&
            centerY >= referenceY + screenHeight * crosshairLowerLeftMinOffsetYRatio &&
            centerY <= referenceY + screenHeight * crosshairLowerLeftMaxOffsetYRatio;

        bool nearBottom = centerY >= referenceY + screenHeight * nearBottomOffsetYRatio;
        bool isLargeBox = areaRatio >= _selfFilterAreaRatio || heightRatio >= selfFilterMinHeightRatio;

        return isLargeBox && nearBottom && inCrosshairLowerLeftRegion;
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