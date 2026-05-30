using System.Diagnostics;
using System.Runtime.InteropServices;

namespace anqrwzui;

public partial class Main
{
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
          var step = Volatile.Read(ref _downMovePixels);
          if (step != 0.0)
          {
            var intervalSeconds = MouseMoveIntervalMs / 1000.0;

            var sineNoise = NoiseAmplitudePixels * Math.Sin(2 * Math.PI * _noisePhase);
            var randomNudge = (_rand.NextDouble() - 0.5) * 0.2;
            _noisePhase += NoiseFrequencyHz * intervalSeconds;

            _moveAccumulator += step + sineNoise + randomNudge;
            var movePixels = (int)Math.Round(_moveAccumulator);

            var sineNoiseX = HorizontalNoiseAmplitudePixels * Math.Sin(2 * Math.PI * _noisePhaseX);
            var randomNudgeX = (_rand.NextDouble() - 0.5) * 0.2;
            _noisePhaseX += HorizontalNoiseFrequencyHz * intervalSeconds;

            _horizontalAccumulator += sineNoiseX + randomNudgeX;
            var movePixelsX = (int)Math.Round(_horizontalAccumulator);

            if (movePixels != 0 || movePixelsX != 0)
            {
              MoveMouseRelativeSerialized(movePixelsX, movePixels);
              _moveAccumulator -= movePixels;
              _horizontalAccumulator -= movePixelsX;
            }
          }

          try { await Task.Delay(MouseMoveIntervalMs, token); } catch (TaskCanceledException) { break; }
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

  private void StartMousePhysics()
  {
    if (_mousePhysicsCts != null)
      return;

    _mousePhysicsCts = new CancellationTokenSource();
    var token = _mousePhysicsCts.Token;
    _mousePhysicsTask = Task.Run(async () =>
    {
      var lastProcessedVersion = 0L;
      var lastTimestamp = Stopwatch.GetTimestamp();

      try
      {
        while (!token.IsCancellationRequested)
        {
          var currentTimestamp = Stopwatch.GetTimestamp();
          var dtSeconds = (currentTimestamp - lastTimestamp) / (double)Stopwatch.Frequency;
          lastTimestamp = currentTimestamp;

          if (dtSeconds <= 0.0 || dtSeconds > 0.05)
          {
            dtSeconds = AimPhysicsFixedDtSeconds;
          }

          if (TryStepAimPhysics(dtSeconds, ref lastProcessedVersion, out var moveX, out var moveY) && (moveX != 0 || moveY != 0))
          {
            MoveMouseRelativeSerialized(moveX, moveY);
          }

          try { await Task.Delay(Math.Max(1, (int)Math.Round(AimPhysicsFixedDtSeconds * 1000.0)), token); } catch (TaskCanceledException) { break; }
        }
      }
      catch (Exception ex)
      {
        Logger.Error("鼠标物理跟踪任务异常", ex);
      }
    }, token);
  }

  private void StopMousePhysics(bool clearTargetSnapshot)
  {
    _mousePhysicsCts?.Cancel();
    _mousePhysicsCts?.Dispose();
    _mousePhysicsCts = null;
    _mousePhysicsTask = null;
    ResetAimPhysicsState(clearTargetSnapshot);
  }

  private void EvaluateMouseMoveState()
  {
    EvaluateRecoilMouseMoveState();
    EvaluateAimPhysicsState();
  }

  private void SetDownMovePixels(double step)
  {
    Interlocked.Exchange(ref _downMovePixels, step);
    _moveAccumulator = 0;
    _noisePhase = 0;
    _horizontalAccumulator = 0;
    _noisePhaseX = 0;
    Logger.Info($"下移步进已设置为 {step}");
    EvaluateMouseMoveState();
  }

  private void QueueSetStepFromActiveGroup()
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action(SetStepFromActiveGroup));
    }
    else
    {
      SetStepFromActiveGroup();
    }
  }

  private void SetStepFromActiveGroup()
  {
    if (_activeComboGroup == 0)
    {
      return;
    }

    var value = GetSelectedOptionValue(_activeComboGroup);
    if (value.HasValue)
    {
      SetDownMovePixels(value.Value);
    }
    else
    {
      Logger.Warning("无法从当前选择获取下移步进值");
    }
  }

  private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
  {
    const int WM_LBUTTONDOWN = 0x0201;
    const int WM_LBUTTONUP = 0x0202;
    const int WM_RBUTTONDOWN = 0x0204;
    const int WM_RBUTTONUP = 0x0205;
    const int WM_MOUSEWHEEL = 0x020A;
    const int VK_CONTROL = 0x11;

    if (nCode >= 0)
    {
      var perspectiveModeChanged = false;

      if (wParam == (IntPtr)WM_LBUTTONDOWN)
      {
        _isLeftButtonDown = true;
      }
      else if (wParam == (IntPtr)WM_LBUTTONUP)
      {
        _isLeftButtonDown = false;
      }
      else if (wParam == (IntPtr)WM_RBUTTONDOWN)
      {
        _isRightButtonDown = true;
        perspectiveModeChanged = true;
      }
      else if (wParam == (IntPtr)WM_RBUTTONUP)
      {
        _isRightButtonDown = false;
        perspectiveModeChanged = true;
      }
      else if (wParam == (IntPtr)WM_MOUSEWHEEL)
      {
        var ctrlDown = (NativeMethods.GetKeyState(VK_CONTROL) & 0x8000) != 0;
        if (ctrlDown)
        {
          var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
          var delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
          if (delta > 0)
          {
            QueueMoveActiveSelection(-1);
          }
          else if (delta < 0)
          {
            QueueMoveActiveSelection(1);
          }
        }
      }

      if (perspectiveModeChanged)
      {
        UpdatePerspectiveModeLabel();
      }

      EvaluateMouseMoveState();
    }

    return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
  }

  private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
  {
    const int WM_KEYDOWN = 0x0100;
    const int WM_KEYUP = 0x0101;

    if (nCode >= 0)
    {
      var vkCode = Marshal.ReadInt32(lParam);
      var key = (Keys)vkCode;

      if (wParam == (IntPtr)WM_KEYDOWN)
      {
        switch (key)
        {
          case Keys.V:
            if (!_isVKeyDown)
            {
              _isVKeyDown = true;
              ToggleBasePerspectiveModeState();
              UpdatePerspectiveModeLabel();
            }
            break;
          case Keys.D1:
          case Keys.NumPad1:
            SetActiveComboGroup(1);
            QueueSetStepFromActiveGroup();
            break;
          case Keys.D2:
          case Keys.NumPad2:
            SetActiveComboGroup(2);
            QueueSetStepFromActiveGroup();
            break;
          case Keys.D3:
          case Keys.NumPad3:
            SetDownMovePixels(0.0);
            SetActiveComboGroup(0);
            break;
        }
      }
      else if (wParam == (IntPtr)WM_KEYUP)
      {
        if (key == Keys.V)
        {
          _isVKeyDown = false;
        }
      }
    }

    return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
  }

  private void QueueMoveActiveSelection(int delta)
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action(() => MoveActiveSelection(delta)));
    }
    else
    {
      MoveActiveSelection(delta);
    }
  }

  private void MoveActiveSelection(int delta)
  {
    if (_activeComboGroup == 0)
    {
      return;
    }

    MoveSelectionInGroup(_activeComboGroup, delta);
  }

  private void EvaluateRecoilMouseMoveState()
  {
    var shouldMove = _isLeftButtonDown && _isRightButtonDown && Volatile.Read(ref _downMovePixels) != 0.0;
    if (shouldMove)
    {
      StartMouseDownMove();
    }
    else
    {
      StopMouseDownMove();
    }
  }

  private void EvaluateAimPhysicsState()
  {
    var shouldRun = _isLeftButtonDown && _isCapturing;
    if (shouldRun)
    {
      StartMousePhysics();
    }
    else if (_mousePhysicsCts != null)
    {
      StopMousePhysics(clearTargetSnapshot: true);
    }
  }

  private void MoveMouseRelativeSerialized(int dx, int dy)
  {
    if (dx == 0 && dy == 0)
    {
      return;
    }

    lock (_mouseOutputLock)
    {
      _mouseController.MoveRelative(dx, dy);
    }
  }

  private bool TryGetAimTargetSnapshot(out double offsetX, out double offsetY, out long version, out int staleFrames)
  {
    lock (_aimTargetLock)
    {
      version = _latestAimTargetVersion;
      offsetX = _latestAimTargetOffsetX;
      offsetY = _latestAimTargetOffsetY;
      staleFrames = _latestAimTargetStaleFrames;
      return version > 0;
    }
  }

  private void ResetAimPhysicsState(bool clearTargetSnapshot)
  {
    _aimPhysicsPositionX = 0.0;
    _aimPhysicsPositionY = 0.0;
    _aimPhysicsVelocityX = 0.0;
    _aimPhysicsVelocityY = 0.0;
    _aimPhysicsAccelerationX = 0.0;
    _aimPhysicsAccelerationY = 0.0;
    _aimPhysicsAccumulatorX = 0.0;
    _aimPhysicsAccumulatorY = 0.0;

    if (!clearTargetSnapshot)
    {
      return;
    }

    lock (_aimTargetLock)
    {
      ClearAimTargetStateLocked(clearPublishedSnapshot: true);
    }
  }

  private bool TryStepAimPhysics(double dtSeconds, ref long lastProcessedVersion, out int moveX, out int moveY)
  {
    moveX = 0;
    moveY = 0;

    if (!TryGetAimTargetSnapshot(out var targetOffsetX, out var targetOffsetY, out var version, out var staleFrames))
    {
      if (lastProcessedVersion != 0)
      {
        ResetAimPhysicsState(clearTargetSnapshot: false);
        lastProcessedVersion = 0;
      }

      return false;
    }

    if (version != lastProcessedVersion)
    {
      _aimPhysicsPositionX = -targetOffsetX;
      _aimPhysicsPositionY = -targetOffsetY;
      lastProcessedVersion = version;
    }
    else if (staleFrames > 0)
    {
      var holdDamping = 1.0 / (1.0 + staleFrames * 0.75);
      _aimPhysicsPositionX *= holdDamping;
      _aimPhysicsPositionY *= holdDamping;
      _aimPhysicsVelocityX *= holdDamping;
      _aimPhysicsVelocityY *= holdDamping;
    }

    var kp = ClampAimKp(Volatile.Read(ref _aimPhysicsKp));
    var kd = ComputeCriticalDampingKd(kp);
    _aimPhysicsKd = kd;

    var previousPositionX = _aimPhysicsPositionX;
    var previousPositionY = _aimPhysicsPositionY;

    _aimPhysicsAccelerationX = (-kp * _aimPhysicsPositionX) - (kd * _aimPhysicsVelocityX);
    _aimPhysicsAccelerationY = (-kp * _aimPhysicsPositionY) - (kd * _aimPhysicsVelocityY);
    ClampVectorMagnitude(ref _aimPhysicsAccelerationX, ref _aimPhysicsAccelerationY, AimPhysicsMaxAcceleration);

    _aimPhysicsVelocityX += _aimPhysicsAccelerationX * dtSeconds;
    _aimPhysicsVelocityY += _aimPhysicsAccelerationY * dtSeconds;
    ClampVectorMagnitude(ref _aimPhysicsVelocityX, ref _aimPhysicsVelocityY, AimPhysicsMaxSpeed);

    _aimPhysicsPositionX += _aimPhysicsVelocityX * dtSeconds;
    _aimPhysicsPositionY += _aimPhysicsVelocityY * dtSeconds;

    var deltaX = _aimPhysicsPositionX - previousPositionX;
    var deltaY = _aimPhysicsPositionY - previousPositionY;
    ClampVectorMagnitude(ref deltaX, ref deltaY, AimPhysicsMaxMovePerTick);

    _aimPhysicsAccumulatorX += deltaX;
    _aimPhysicsAccumulatorY += deltaY;
    moveX = (int)Math.Round(_aimPhysicsAccumulatorX);
    moveY = (int)Math.Round(_aimPhysicsAccumulatorY);

    if (moveX != 0)
    {
      _aimPhysicsAccumulatorX -= moveX;
    }

    if (moveY != 0)
    {
      _aimPhysicsAccumulatorY -= moveY;
    }

    return true;
  }

  private static void ClampVectorMagnitude(ref double x, ref double y, double maxMagnitude)
  {
    if (maxMagnitude <= 0.0)
    {
      x = 0.0;
      y = 0.0;
      return;
    }

    var magnitude = Math.Sqrt((x * x) + (y * y));
    if (magnitude <= maxMagnitude || magnitude <= double.Epsilon)
    {
      return;
    }

    var scale = maxMagnitude / magnitude;
    x *= scale;
    y *= scale;
  }
}