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
              _mouseController.MoveRelative(movePixelsX, movePixels);
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

  private void EvaluateMouseMoveState()
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
      }
      else if (wParam == (IntPtr)WM_RBUTTONUP)
      {
        _isRightButtonDown = false;
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
}