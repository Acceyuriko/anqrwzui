using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace anqrwzui;

public partial class Main
{
    private enum PerspectiveMode
    {
        FirstPerson,
        ThirdPerson
    }

    private DxgiScreenCapture? _screenCapture;
    private YoloV8Detector? _yoloDetector;
    private PictureBox? _pictureBox;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _isCapturing = false;
    private Label? _deviceLabel;
    private Label? _fpsLabel;
    private Button? _toggleCaptureButton;
    private int _fpsCount = 0;
    private DateTime _fpsWindowStart = DateTime.UtcNow;
    private long _lastCaptureTicks = 0;
    private readonly double _targetFrameMs = 16.0; // ~60 FPS
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private IntPtr _mouseHookId = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _mouseProc;
    private readonly MouseController _mouseController = new();
    private CancellationTokenSource? _mouseMoveCts;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private FlowLayoutPanel? _firstOptionGroupPanel;
    private FlowLayoutPanel? _secondOptionGroupPanel;
    private FlowLayoutPanel? _firstComboGroupPanel;
    private FlowLayoutPanel? _secondComboGroupPanel;
    private Label? _perspectiveModeLabel;
    private Label? _activeComboLabel;
    private int _activeComboGroup = 1;
    private int _basePerspectiveModeValue = (int)PerspectiveMode.ThirdPerson;
    private List<ConfigOption> _configOptions = new();
    private string _configPath = string.Empty;
    private string _selectionStatePath = string.Empty;
    private FileSystemWatcher? _configWatcher;
    private System.Threading.Timer? _configReloadTimer;
    private System.Threading.Timer? _configSaveDebounceTimer;
    private TrackBar? _selfFilterSlider;
    private Label? _selfFilterSliderValueLabel;
    private volatile bool _suppressSliderEvent;
    private float _selfFilterAreaRatio = 0.018f;
    private const int SelfFilterSliderMin = 5;
    private const int SelfFilterSliderMax = 80;
    private const int SelfFilterSliderScale = 1000;
    private TrackBar? _aimKpSlider;
    private Label? _aimKpSliderValueLabel;
    private volatile bool _suppressAimKpSliderEvent;
    private const int AimKpSliderMin = 50;
    private const int AimKpSliderMax = 1600;
    private const int AimKpSliderScale = 10;
    private volatile bool _isLeftButtonDown;
    private volatile bool _isRightButtonDown;
    private volatile bool _isVKeyDown;
    private double _downMovePixels = 0;
    private const float LargestDetectionAreaSimilarityThreshold = 0.9f;
    private const int MouseMoveIntervalMs = 10;
    private double _moveAccumulator = 0;
    private double _noisePhase = 0;
    private const double NoiseAmplitudePixels = 0.2;
    private const double NoiseFrequencyHz = 3.0;
    private double _horizontalAccumulator = 0;
    private double _noisePhaseX = 0;
    private const double HorizontalNoiseAmplitudePixels = 0.1;
    private const double HorizontalNoiseFrequencyHz = 3;
    private readonly Random _rand = new();
    private readonly object _mouseOutputLock = new();
    private readonly object _aimTargetLock = new();
    private CancellationTokenSource? _mousePhysicsCts;
    private Task? _mousePhysicsTask;
    private long _latestAimTargetVersion;
    private int _latestAimTargetStaleFrames;
    private double _latestAimTargetOffsetX;
    private double _latestAimTargetOffsetY;
    private bool _hasLockedAimTarget;
    private RectangleF _lockedAimTargetBox = RectangleF.Empty;
    private double _lockedAimTargetOffsetX;
    private double _lockedAimTargetOffsetY;
    private int _lockedAimTargetLostFrames;
    private double _aimPhysicsPositionX;
    private double _aimPhysicsPositionY;
    private double _aimPhysicsVelocityX;
    private double _aimPhysicsVelocityY;
    private double _aimPhysicsAccelerationX;
    private double _aimPhysicsAccelerationY;
    private double _aimPhysicsAccumulatorX;
    private double _aimPhysicsAccumulatorY;
    private double _aimPhysicsKp = DefaultAimKp;
    private double _aimPhysicsKd = DefaultAimKd;
    private int _aimTargetHoldFrames = DefaultAimTargetHoldFrames;
    private double _aimTargetAssociationDistanceRatio = DefaultAimTargetAssociationDistanceRatio;
    private double _aimTargetMaxOffsetRatio = DefaultAimTargetMaxOffsetRatio;
    private const double DefaultAimKp = 64.0;
    private const double DefaultAimKd = 16.0;
    private const int DefaultAimTargetHoldFrames = 4;
    private const double DefaultAimTargetAssociationDistanceRatio = 0.08;
    private const double DefaultAimTargetMaxOffsetRatio = 0.45;
    private const double AimPhysicsTargetHz = 120.0;
    private const double AimPhysicsFixedDtSeconds = 1.0 / AimPhysicsTargetHz;
    private const double AimPhysicsMaxAcceleration = 50000.0;
    private const double AimPhysicsMaxSpeed = 2500.0;
    private const double AimPhysicsMaxMovePerTick = 32.0;
    private const double AimReferenceYOffsetPixels = -5.0;

    private PerspectiveMode GetEffectivePerspectiveMode()
    {
        return _isRightButtonDown ? PerspectiveMode.FirstPerson : GetBasePerspectiveMode();
    }

    private PerspectiveMode GetBasePerspectiveMode()
    {
        return (PerspectiveMode)Volatile.Read(ref _basePerspectiveModeValue);
    }

    private bool IsFirstPersonModeActive()
    {
        return GetEffectivePerspectiveMode() == PerspectiveMode.FirstPerson;
    }

    private void ToggleBasePerspectiveModeState()
    {
        var nextMode = GetBasePerspectiveMode() == PerspectiveMode.FirstPerson
            ? PerspectiveMode.ThirdPerson
            : PerspectiveMode.FirstPerson;
        Volatile.Write(ref _basePerspectiveModeValue, (int)nextMode);
    }

    private static string GetPerspectiveModeText(PerspectiveMode mode)
    {
        return mode == PerspectiveMode.FirstPerson ? "第一人称" : "第三人称";
    }

    private static int AimKpToSliderValue(double kp)
    {
        return (int)Math.Round(ClampAimKp(kp) * AimKpSliderScale);
    }

    private static double SliderValueToAimKp(int sliderValue)
    {
        return sliderValue / (double)AimKpSliderScale;
    }

    private static double ClampAimKp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultAimKp;
        }

        return Math.Clamp(value, AimKpSliderMin / (double)AimKpSliderScale, AimKpSliderMax / (double)AimKpSliderScale);
    }

    private static double ComputeCriticalDampingKd(double kp)
    {
        // 阻力调大 1.1 倍，减少吸附过头导致的跳动
        return 2.0 * Math.Sqrt(Math.Max(0.0, ClampAimKp(kp))) * 1.1;
    }

    private static int ClampAimTargetHoldFrames(int value)
    {
        return Math.Clamp(value, 0, 10);
    }

    private static double ClampAimTargetAssociationDistanceRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultAimTargetAssociationDistanceRatio;
        }

        return Math.Clamp(value, 0.02, 0.25);
    }

    private static double ClampAimTargetMaxOffsetRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultAimTargetMaxOffsetRatio;
        }

        return Math.Clamp(value, 0.15, 0.9);
    }

    private sealed class ConfigOption
    {
        public ConfigOption(string key, double value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public double Value { get; }
    }
}