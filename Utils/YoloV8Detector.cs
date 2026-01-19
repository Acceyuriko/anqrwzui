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
    private readonly float _confidenceThreshold = 0.5f;
    private readonly float _iouThreshold = 0.45f;
    private readonly int _inputSize = 640;
    private readonly string _inputName;

    public YoloV8Detector(string modelPath)
    {
      Initialize(modelPath);
      _inputName = _session?.InputMetadata.Keys.First() ?? "images";
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

        // 尝试使用GPU
        try
        {
          options.AppendExecutionProvider_CUDA(0);
          Logger.Info("使用CUDA进行推理");
        }
        catch (Exception ex)
        {
          // 如果CUDA不可用，使用CPU
          options.AppendExecutionProvider_CPU();
          Logger.Warning($"CUDA不可用，使用CPU进行推理: {ex.Message}");
        }

        // 加载ONNX模型
        _session = new InferenceSession(modelPath, options);
        Logger.Info("YOLOv8 ONNX模型加载成功");
      }
      catch (Exception ex)
      {
        Logger.Error("YOLOv8模型加载失败", ex);
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

      Logger.Debug($"开始目标检测，图像尺寸: {image.Width}x{image.Height}");

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
        var resultsList = Postprocess(output, image.Width, image.Height);
        Logger.Debug($"检测完成，发现 {resultsList.Count} 个目标");
        return resultsList;
      }
    }

    private DenseTensor<float> PreprocessImage(Bitmap image)
    {
      Logger.Debug("开始图像预处理");

      // 调整大小为640x640（YOLOv8标准输入尺寸）
      var resizedImage = new Bitmap(_inputSize, _inputSize);
      using (var graphics = Graphics.FromImage(resizedImage))
      {
        graphics.DrawImage(image, 0, 0, _inputSize, _inputSize);
      }

      // 转换为RGB
      var rgbImage = resizedImage.Clone(new Rectangle(0, 0, resizedImage.Width, resizedImage.Height),
          PixelFormat.Format24bppRgb);

      // 转换为Tensor [1, 3, 640, 640]
      var tensor = BitmapToTensor(rgbImage);

      Logger.Debug("图像预处理完成");
      return tensor;
    }

    private DenseTensor<float> BitmapToTensor(Bitmap bitmap)
    {
      var dimensions = new[] { 1, 3, _inputSize, _inputSize };
      var tensor = new DenseTensor<float>(dimensions);
      var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
          ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

      unsafe
      {
        byte* ptr = (byte*)bitmapData.Scan0;
        int stride = bitmapData.Stride;

        for (int y = 0; y < bitmap.Height; y++)
        {
          byte* row = ptr + (y * stride);
          for (int x = 0; x < bitmap.Width; x++)
          {
            // BGR to RGB and normalize to 0-1
            tensor[0, 0, y, x] = row[x * 3 + 2] / 255.0f; // R
            tensor[0, 1, y, x] = row[x * 3 + 1] / 255.0f; // G
            tensor[0, 2, y, x] = row[x * 3 + 0] / 255.0f; // B
          }
        }
      }

      bitmap.UnlockBits(bitmapData);
      return tensor;
    }

    private List<DetectionResult> Postprocess(Tensor<float> output, int originalWidth, int originalHeight)
    {
      var results = new List<DetectionResult>();

      try
      {
        // YOLOv8输出格式: [1, 84, n] 或 [1, n, 84]
        var shape = output.Dimensions.ToArray();
        Logger.Debug($"模型输出形状: [{string.Join(", ", shape)}]");

        // 处理不同的输出格式
        int numDetections;
        int featuresPerDetection;

        if (shape.Length == 3)
        {
          if (shape[1] == 84) // [1, 84, n]
          {
            numDetections = shape[2];
            featuresPerDetection = shape[1];
          }
          else if (shape[2] == 84) // [1, n, 84]
          {
            numDetections = shape[1];
            featuresPerDetection = shape[2];
          }
          else
          {
            Logger.Error($"不支持的输出格式: [{string.Join(", ", shape)}]");
            return results;
          }
        }
        else
        {
          Logger.Error($"不支持的输出维度: {shape.Length}");
          return results;
        }

        // 解析检测结果
        for (int i = 0; i < numDetections; i++)
        {
          float x_center, y_center, width, height, confidence;

          if (shape[1] == 84) // [1, 84, n] 格式
          {
            x_center = output[0, 0, i];
            y_center = output[0, 1, i];
            width = output[0, 2, i];
            height = output[0, 3, i];
            confidence = output[0, 4, i];
          }
          else // [1, n, 84] 格式
          {
            x_center = output[0, i, 0];
            y_center = output[0, i, 1];
            width = output[0, i, 2];
            height = output[0, i, 3];
            confidence = output[0, i, 4];
          }

          // 找到最大类别分数
          float maxScore = 0;
          int classId = 0;

          for (int j = 5; j < featuresPerDetection; j++)
          {
            float score;
            if (shape[1] == 84)
              score = output[0, j, i];
            else
              score = output[0, i, j];

            if (score > maxScore)
            {
              maxScore = score;
              classId = j - 5;
            }
          }

          // 计算最终置信度
          confidence *= maxScore;

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

          var className = classId < _classNames.Length ? _classNames[classId] : "unknown";

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
      var results = new List<DetectionResult>();
      var sortedDetections = detections.OrderByDescending(d => d.Confidence).ToList();

      while (sortedDetections.Count > 0)
      {
        var current = sortedDetections[0];
        results.Add(current);
        sortedDetections.RemoveAt(0);

        for (int i = sortedDetections.Count - 1; i >= 0; i--)
        {
          var iou = CalculateIOU(current.BoundingBox, sortedDetections[i].BoundingBox);
          if (iou > _iouThreshold)
          {
            sortedDetections.RemoveAt(i);
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