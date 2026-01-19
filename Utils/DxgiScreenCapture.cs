using System.Drawing.Imaging;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace anqrwzui
{
  public class DxgiScreenCapture : IDisposable
  {
    private Device? _device;
    private OutputDuplication? _duplicatedOutput;
    private Texture2D? _screenTexture;
    private int _captureWidth = 640;
    private int _captureHeight = 640;
    private bool _isDisposed = false;

    public DxgiScreenCapture()
    {
      Initialize();
    }

    private void Initialize()
    {
      Logger.Info("初始化DXGI屏幕截取");

      try
      {
        // 创建设备
        _device = new Device(SharpDX.Direct3D.DriverType.Hardware);

        // 获取适配器
        using var factory = new Factory1();
        using var adapter = factory.GetAdapter1(0);

        // 获取输出
        using var output = adapter.GetOutput(0);
        using var output1 = output.QueryInterface<Output1>();

        // 获取输出描述
        var outputDescription = output.Description;

        // 计算屏幕中心坐标
        int centerX = outputDescription.DesktopBounds.Right / 2;
        int centerY = outputDescription.DesktopBounds.Bottom / 2;
        int startX = centerX - _captureWidth / 2;
        int startY = centerY - _captureHeight / 2;

        // 创建复制输出
        _duplicatedOutput = output1.DuplicateOutput(_device);

        // 创建纹理用于存储截取的图像
        var textureDesc = new Texture2DDescription
        {
          Width = _captureWidth,
          Height = _captureHeight,
          MipLevels = 1,
          ArraySize = 1,
          Format = Format.B8G8R8A8_UNorm,
          SampleDescription = new SampleDescription(1, 0),
          Usage = ResourceUsage.Staging,
          BindFlags = BindFlags.None,
          CpuAccessFlags = CpuAccessFlags.Read,
          OptionFlags = ResourceOptionFlags.None
        };

        _screenTexture = new Texture2D(_device, textureDesc);
        Logger.Info("DXGI屏幕截取初始化成功");
      }
      catch (Exception ex)
      {
        Logger.Error("DXGI屏幕截取初始化失败", ex);
        throw;
      }
    }

    public Bitmap? CaptureScreen()
    {
      ObjectDisposedException.ThrowIf(_isDisposed, nameof(DxgiScreenCapture));

      try
      {
        SharpDX.DXGI.Resource screenResource;
        OutputDuplicateFrameInformation duplicateFrameInformation;

        // 尝试获取帧
        _duplicatedOutput!.TryAcquireNextFrame(100, out duplicateFrameInformation, out screenResource);

        using (screenResource)
        {
          using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
          {
            // 获取输出描述以确定屏幕尺寸
            var outputDescription = _duplicatedOutput.Description;
            int screenWidth = outputDescription.ModeDescription.Width;
            int screenHeight = outputDescription.ModeDescription.Height;

            // 计算屏幕中心坐标
            int centerX = screenWidth / 2;
            int centerY = screenHeight / 2;
            int startX = Math.Max(0, centerX - _captureWidth / 2);
            int startY = Math.Max(0, centerY - _captureHeight / 2);

            // 确保不超出屏幕边界
            int actualWidth = Math.Min(_captureWidth, screenWidth - startX);
            int actualHeight = Math.Min(_captureHeight, screenHeight - startY);

            // 复制屏幕区域到纹理
            var sourceRegion = new ResourceRegion
            {
              Left = startX,
              Top = startY,
              Right = startX + actualWidth,
              Bottom = startY + actualHeight,
              Front = 0,
              Back = 1
            };

            _device!.ImmediateContext.CopySubresourceRegion(
                screenTexture2D, 0, sourceRegion,
                _screenTexture, 0, 0, 0, 0);
          }
        }

        _duplicatedOutput.ReleaseFrame();

        // 从纹理读取数据并转换为Bitmap
        var mapSource = _device.ImmediateContext.MapSubresource(
            _screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

        try
        {
          var bitmap = new Bitmap(_captureWidth, _captureHeight, PixelFormat.Format32bppArgb);
          var bitmapData = bitmap.LockBits(
              new Rectangle(0, 0, _captureWidth, _captureHeight),
              ImageLockMode.WriteOnly,
              PixelFormat.Format32bppArgb);

          var sourcePtr = mapSource.DataPointer;
          var destPtr = bitmapData.Scan0;

          for (int y = 0; y < _captureHeight; y++)
          {
            Utilities.CopyMemory(
                destPtr,
                sourcePtr,
                _captureWidth * 4);

            sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
            destPtr = IntPtr.Add(destPtr, bitmapData.Stride);
          }

          bitmap.UnlockBits(bitmapData);
          return bitmap;
        }
        finally
        {
          _device.ImmediateContext.UnmapSubresource(_screenTexture, 0);
        }
      }
      catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Code)
      {
        // 超时，没有新帧可用
        Logger.Debug("屏幕截取超时，没有新帧可用");
        return null;
      }
      catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Code)
      {
        // 访问丢失，需要重新初始化
        Logger.Warning("屏幕截取访问丢失，重新初始化");
        Dispose();
        Initialize();
        return null;
      }
      catch (Exception ex)
      {
        Logger.Error($"屏幕截取失败: {ex.Message}\n堆栈跟踪: {ex.StackTrace}", ex);
        return null;
      }
    }

    public void Dispose()
    {
      if (!_isDisposed)
      {
        Logger.Info("释放DXGI屏幕截取资源");
        _screenTexture?.Dispose();
        _duplicatedOutput?.Dispose();
        _device?.Dispose();
        _isDisposed = true;
      }
    }
  }
}