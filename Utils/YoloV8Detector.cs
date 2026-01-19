using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace anqrwzui
{
  public class YoloV8Detector : IDisposable
  {
    private InferenceSession? _session;
    private bool _isDisposed = false;
    private readonly string[] _classNames = { "person", "head" };
    private readonly float _confidenceThreshold = 0.25f;
    private readonly float _iouThreshold = 0.45f;
    private readonly int _inputSize = 640;
    private readonly string _inputName;
    private readonly Action<string>? _deviceCallback;
    private readonly float[] _tensorBuffer;
    private readonly DenseTensor<float> _tensor;
    private readonly Bitmap _preprocessBitmap;
    private readonly Graphics _preprocessGraphics;

    public YoloV8Detector(string modelPath, Action<string>? deviceCallback = null)
    {
      _deviceCallback = deviceCallback;
      Initialize(modelPath);
      _inputName = _session?.InputMetadata.Keys.First() ?? "images";

      // 预分配固定的预处理缓冲
      _tensorBuffer = new float[1 * 3 * _inputSize * _inputSize];
      _tensor = new DenseTensor<float>(_tensorBuffer, new[] { 1, 3, _inputSize, _inputSize });
      _preprocessBitmap = new Bitmap(_inputSize, _inputSize, PixelFormat.Format24bppRgb);
      _preprocessGraphics = Graphics.FromImage(_preprocessBitmap);
    }

    private void Initialize(string modelPath)
    {
      Logger.Info($"开始初始化YOLOv8检测器，模型路径: {modelPath}");

      try
      {
        // 检查模型文件是否存在
        if (!File.Exists(modelPath))
        {
          throw new FileNotFoundException($"模型文件不存在: {modelPath}");
        }

        // 配置ONNX Runtime选项
        var options = new SessionOptions();
        string device = "CPU";

        // 尝试使用GPU
        try
        {
          options.AppendExecutionProvider_CUDA(0);
          Logger.Info("使用CUDA进行推理");
          device = "GPU";
        }
        catch (Exception ex)
        {
          // 如果CUDA不可用，使用CPU
          options.AppendExecutionProvider_CPU();
          Logger.Warning($"CUDA不可用，使用CPU进行推理: {ex.Message}");
          device = "CPU";
        }

        // 加载ONNX模型
        _session = new InferenceSession(modelPath, options);
        Logger.Info("YOLOv8 ONNX模型加载成功");
        _deviceCallback?.Invoke(device);
      }
      catch (Exception ex)
      {
        Logger.Error("YOLOv8模型加载失败", ex);
        _deviceCallback?.Invoke("初始化失败");
        throw;
      }
    }

    public List<DetectionResult> Detect(Bitmap image)
    {
      if (_isDisposed || _session == null)
      {
        Logger.Warning("检测器已释放或模型未初始化");
        return new List<DetectionResult>();
      }

      // 预处理图像
      var inputTensor = PreprocessImage(image);

      // 创建输入
      var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
            };

      // 推理
      using (var results = _session.Run(inputs))
      {
        // 获取输出
        var output = results.First().AsTensor<float>();
        return Postprocess(output, image.Width, image.Height);
      }
    }

    private DenseTensor<float> PreprocessImage(Bitmap image)
    {
      _preprocessGraphics.DrawImage(image, 0, 0, _inputSize, _inputSize);
      return BitmapToTensor(_preprocessBitmap);
    }

    private DenseTensor<float> BitmapToTensor(Bitmap bitmap)
    {
      var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
          ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

      unsafe
      {
        byte* ptr = (byte*)bitmapData.Scan0;
        int stride = bitmapData.Stride;
        const float inv255 = 1.0f / 255.0f;

        fixed (float* tensorPtr = _tensorBuffer)
        {
          float* dstBase = tensorPtr;
          for (int y = 0; y < bitmap.Height; y++)
          {
            byte* row = ptr + (y * stride);
            float* dstR = dstBase + (0 * _inputSize + y) * _inputSize;
            float* dstG = dstBase + (1 * _inputSize + y) * _inputSize;
            float* dstB = dstBase + (2 * _inputSize + y) * _inputSize;

            for (int x = 0; x < bitmap.Width; x++)
            {
              byte b = row[x * 3 + 0];
              byte g = row[x * 3 + 1];
              byte r = row[x * 3 + 2];

              dstR[x] = r * inv255;
              dstG[x] = g * inv255;
              dstB[x] = b * inv255;
            }
          }
        }
      }

      bitmap.UnlockBits(bitmapData);
      return _tensor;
    }

    private List<DetectionResult> Postprocess(Tensor<float> output, int originalWidth, int originalHeight)
    {
      var results = new List<DetectionResult>();

      try
      {
        // YOLOv8输出格式: [1, 84, n] 或 [1, n, 84]
        var dims = output.Dimensions;
        int dim0 = dims[0];
        int dim1 = dims[1];
        int dim2 = dims.Length > 2 ? dims[2] : 0;

        // 处理不同的输出格式
        int numDetections;
        int featuresPerDetection;

        if (dims.Length == 3)
        {
          if (dim1 == 84) // [1, 84, n]
          {
            numDetections = dim2;
            featuresPerDetection = dim1;
          }
          else if (dim2 == 84) // [1, n, 84]
          {
            numDetections = dim1;
            featuresPerDetection = dim2;
          }
          else
          {
            Logger.Error($"不支持的输出格式: [1, {dim1}, {dim2}]");
            return results;
          }
        }
        else
        {
          Logger.Error($"不支持的输出维度: {dims.Length}");
          return results;
        }

        // 解析检测结果
        for (int i = 0; i < numDetections; i++)
        {
          // YOLOv8 默认输出: 前4个是 bbox，后面是每类置信度；有些导出会在第5位带 objectness。
          // 自适应判断是否包含 objectness。
          int classCountNoObj = featuresPerDetection - 4;
          int classCountWithObj = featuresPerDetection - 5;
          bool hasObjectness = classCountWithObj > 0 && (featuresPerDetection == 85 || featuresPerDetection == 7 || classCountWithObj == _classNames.Length);
          int classStart = hasObjectness ? 5 : 4;
          int classCount = hasObjectness ? classCountWithObj : classCountNoObj;

          float x_center, y_center, width, height, objectness;

          if (dim1 == featuresPerDetection) // [1, C, n]
          {
            x_center = output[0, 0, i];
            y_center = output[0, 1, i];
            width = output[0, 2, i];
            height = output[0, 3, i];
            objectness = hasObjectness ? output[0, 4, i] : 1.0f;
          }
          else // [1, n, C]
          {
            x_center = output[0, i, 0];
            y_center = output[0, i, 1];
            width = output[0, i, 2];
            height = output[0, i, 3];
            objectness = hasObjectness ? output[0, i, 4] : 1.0f;
          }

          // 找到最大类别分数
          float maxScore = 0;
          int classId = 0;

          for (int j = 0; j < classCount; j++)
          {
            int idx = classStart + j;
            float score;
            if (dim1 == featuresPerDetection)
              score = output[0, idx, i];
            else
              score = output[0, i, idx];

            if (score > maxScore)
            {
              maxScore = score;
              classId = j;
            }
          }

          // 计算最终置信度（若无 objectness 则直接使用类别分数）
          float confidence = hasObjectness ? objectness * maxScore : maxScore;

          // 早筛低置信度，减少后续计算
          if (confidence < _confidenceThreshold)
            continue;

          // 转换为边界框坐标
          var x1 = x_center - width / 2;
          var y1 = y_center - height / 2;
          var x2 = x_center + width / 2;
          var y2 = y_center + height / 2;

          // 映射回原始图像尺寸
          var scaleX = (float)originalWidth / _inputSize;
          var scaleY = (float)originalHeight / _inputSize;

          var rect = new RectangleF(
              x1 * scaleX,
              y1 * scaleY,
              (x2 - x1) * scaleX,
              (y2 - y1) * scaleY);

          if (classId >= _classNames.Length)
            continue; // 跳过未定义类别

          var className = _classNames[classId];

          results.Add(new DetectionResult
          {
            BoundingBox = rect,
            Confidence = confidence,
            ClassName = className,
            ClassId = classId
          });
        }
      }
      catch (Exception ex)
      {
        Logger.Error("后处理错误", ex);
      }

      // 应用NMS
      return ApplyNMS(results);
    }

    private List<DetectionResult> ApplyNMS(List<DetectionResult> detections)
    {
      if (detections.Count == 0)
        return detections;

      var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
      int n = sorted.Count;
      var suppressed = new bool[n];
      var results = new List<DetectionResult>(n);

      for (int i = 0; i < n; i++)
      {
        if (suppressed[i]) continue;
        var current = sorted[i];
        results.Add(current);

        for (int j = i + 1; j < n; j++)
        {
          if (suppressed[j]) continue;
          var iou = CalculateIOU(current.BoundingBox, sorted[j].BoundingBox);
          if (iou > _iouThreshold)
          {
            suppressed[j] = true;
          }
        }
      }

      return results;
    }

    private float CalculateIOU(RectangleF rect1, RectangleF rect2)
    {
      var intersection = RectangleF.Intersect(rect1, rect2);
      if (intersection.IsEmpty)
        return 0;

      var union = rect1.Width * rect1.Height + rect2.Width * rect2.Height - intersection.Width * intersection.Height;
      return intersection.Width * intersection.Height / union;
    }

    public void Dispose()
    {
      if (!_isDisposed)
      {
        Logger.Info("释放YOLOv8检测器资源");
        _session?.Dispose();
        _preprocessGraphics.Dispose();
        _preprocessBitmap.Dispose();
        _isDisposed = true;
      }
    }
  }

  public class DetectionResult
  {
    public RectangleF BoundingBox { get; set; }
    public float Confidence { get; set; }
    public string ClassName { get; set; } = "";
    public int ClassId { get; set; }
  }
}