using System.Drawing;
using System.Drawing.Imaging;

namespace anqrwzui
{
    public static class DetectionRenderer
    {
        private static readonly Color[] _classColors = 
        {
            Color.Red,    // person - 红色
            Color.Blue    // head - 蓝色
        };

        private static readonly Font _font = new Font("Arial", 12, FontStyle.Bold);
        private static readonly Brush _textBrush = Brushes.White;
        private static readonly Brush _backgroundBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));

        public static Bitmap DrawDetections(Bitmap originalImage, List<DetectionResult> detections)
        {
            var resultImage = new Bitmap(originalImage);
            
            using (var graphics = Graphics.FromImage(resultImage))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                
                foreach (var detection in detections)
                {
                    DrawDetection(graphics, detection);
                }
            }
            
            return resultImage;
        }

        private static void DrawDetection(Graphics graphics, DetectionResult detection)
        {
            var colorIndex = detection.ClassId % _classColors.Length;
            var color = _classColors[colorIndex];
            
            using (var pen = new Pen(color, 3))
            {
                var rect = detection.BoundingBox;
                graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                
                // 绘制标签
                var label = $"{detection.ClassName} {detection.Confidence:P0}";
                var labelSize = graphics.MeasureString(label, _font);
                var labelRect = new RectangleF(rect.X, rect.Y - labelSize.Height, labelSize.Width, labelSize.Height);
                
                // 绘制背景
                graphics.FillRectangle(_backgroundBrush, labelRect);
                
                // 绘制文本
                graphics.DrawString(label, _font, _textBrush, labelRect.X, labelRect.Y);
            }
        }
    }
}