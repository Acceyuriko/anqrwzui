import os
from ultralytics import YOLO

# 数据集路径（需替换）
DATASET_DIR = r"./datasets/"
TRAIN_IMAGES = os.path.join(DATASET_DIR, "images", "train")
VAL_IMAGES = os.path.join(DATASET_DIR, "images", "val")
DATA_YAML = os.path.join(DATASET_DIR, "data.yaml")  # 确保定义 train/val/test 与类别 names

# ============================================================
# 优化说明（针对每张图 0~1 个目标的稀疏检测场景，且分类为 head/self_head/team_head）：
# 1. 使用预训练权重 yolov8n.pt 迁移学习，而非从零训练
# 2. 适度提高 cls 权重（三分类任务需要更强类别区分能力）
# 3. 提高 box 权重，让模型更关注定位精度
# 4. 关闭 Mosaic/MixUp/CopyPaste，避免稀疏目标被增强切掉
# 5. 提前关闭 Mosaic（close_mosaic=5），前几轮就用正常图片训练
# 6. 保持较小学习率，提升迁移学习稳定性
# 7. 增大 epochs，迁移学习需要更多轮次微调
# ============================================================

MODEL_CFG = "yolo11n.pt"   # ★ 使用预训练权重（迁移学习），而非从零训练
OUTPUT_DIR = "."

def main():
    # 加载预训练模型（自动下载 yolo11n.pt）
    model = YOLO(MODEL_CFG)

    # 启动训练
    model.train(
        data=DATA_YAML,
        imgsz=640,
        epochs=200,          # 迁移学习需要更多轮次微调
        batch=16,
        workers=8,
        project=OUTPUT_DIR,
        name="exp",
        device=0,            # CUDA:0，若无GPU则改为 "cpu"
        amp=True,            # 自动混合精度(AMP)，加速训练

        # ---- 损失权重调整（核心优化） ----
        box=10.0,            # 默认 7.5 → 提高，增强定位学习
        cls=0.7,             # 三分类任务提升分类损失权重，降低敌我/队友混淆
        dfl=1.5,             # 默认 1.5，保持不变

        # ---- 数据增强调整（稀疏目标场景） ----
        mosaic=0.0,          # ★ 关闭 Mosaic，避免把仅有的目标切掉或混入过多背景
        mixup=0.0,           # ★ 关闭 MixUp，稀疏目标下混合图片无意义
        copy_paste=0.0,      # ★ 关闭 CopyPaste，单目标场景收益极低
        close_mosaic=5,      # 提前关闭 Mosaic 的 epoch（即使 mosaic=0 也设小值保险）

        # ---- 其他优化 ----
        label_smoothing=0.0, # 三分类建议关闭或很小平滑，保持类别边界清晰
        patience=50,         # 早停耐心值，避免过拟合
        cos_lr=True,         # 余弦退火学习率，后期微调更平滑
        lr0=0.002,           # 初始学习率（迁移学习通常用较小值）
        lrf=0.01,            # 最终学习率 = lr0 * lrf
    )

    # 训练完成后导出 ONNX
    best_pt = os.path.join(model.trainer.save_dir, "weights", "best.pt")
    model = YOLO(best_pt)
    model.export(format="onnx", opset=12, simplify=True, dynamic=False, half=True)

if __name__ == "__main__":
    main()