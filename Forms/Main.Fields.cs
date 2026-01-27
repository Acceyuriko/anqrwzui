using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace anqrwzui;

public partial class Main
{
    private DxgiScreenCapture? _screenCapture;
    private YoloV8Detector? _yoloDetector;
    private PictureBox? _pictureBox;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _isCapturing = false;
    private Bitmap? _currentFrame;
    private readonly object _frameLock = new();
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
    private ComboBox? _firstPrimaryCombo;
    private ComboBox? _firstSecondaryCombo;
    private ComboBox? _secondPrimaryCombo;
    private ComboBox? _secondSecondaryCombo;
    private Dictionary<string, Dictionary<string, double>> _configOptions = new();
    private string _configPath = string.Empty;
    private string _selectionStatePath = string.Empty;
    private FileSystemWatcher? _configWatcher;
    private System.Threading.Timer? _configReloadTimer;
    private volatile bool _isLeftButtonDown;
    private volatile bool _isRightButtonDown;
    private double _downMovePixels = 0;
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
}