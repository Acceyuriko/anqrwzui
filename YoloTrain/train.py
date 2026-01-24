import os
from ultralytics import YOLO

# 数据集路径（需替换）
DATASET_DIR = r"./datasets/"
TRAIN_IMAGES = os.path.join(DATASET_DIR, "images", "train")
VAL_IMAGES = os.path.join(DATASET_DIR, "images", "val")
DATA_YAML = os.path.join(DATASET_DIR, "data.yaml")  # 确保定义 train/val/test 与类别 names

# 从零开始训练配置（使用模型结构 yaml，而非预训练权重）
MODEL_CFG = "yolov8n.yaml"  # 或自定义 yaml
OUTPUT_DIR = "."

def main():
    # 创建模型（从零开始，不加载预训练权重）
    model = YOLO(MODEL_CFG)

    # 启动训练
    model.train(
        data=DATA_YAML,
        imgsz=640,
        epochs=100,
        batch=16,
        workers=8,
        project=OUTPUT_DIR,
        name="exp",
        device=0,   # CUDA:0，若无GPU则改为 "cpu"
    )

    # 训练完成后导出 ONNX
    best_pt = os.path.join(model.trainer.save_dir, "weights", "best.pt")
    model = YOLO(best_pt)
    model.export(format="onnx", opset=12, simplify=True, dynamic=False)

if __name__ == "__main__":
    main()