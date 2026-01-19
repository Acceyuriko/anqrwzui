from ultralytics import YOLO

model = YOLO("Model/yolov8n.pt")  # 你的模型路径：yolov8s.pt/yolov8l-seg.pt/yolov8n-pose.pt 等
model.export(format="onnx", opset=12) # 关键参数：simplify=True 简化模型，opset=12 兼容性最佳