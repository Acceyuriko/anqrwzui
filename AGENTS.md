# `anqrwzui` 仓库 AI 代理指南

## 仓库简介
- 这是一个基于 .NET 8.0 的 Windows 窗体应用，提供屏幕截取、实时 YOLOv8 ONNX 推理，以及可选的低级输入控制。
- 应用仅支持 Windows，依赖原生互操作库 `gbild64.dll`，并支持 GPU/CPU ONNX 推理。
- 仓库还包含一个独立的训练管道 `YoloTrain/`，用于数据集准备和模型训练。

## 构建与运行
- 在仓库根目录构建应用：
  - `dotnet build anqrwzui.csproj`
- 在 Windows 上本地运行：
  - `dotnet run --project anqrwzui.csproj`
- 如需发布：
  - `dotnet publish -c Release --self-contained false -r win-x64 --project anqrwzui.csproj`
- 仓库中没有自动化测试项目或测试文件。

## 关键文件和目录
- `anqrwzui.csproj` - 主要 Windows 窗体项目，包含 `Microsoft.ML.OnnxRuntime.Gpu`、`SharpDX` 等依赖，并配置原生 DLL 输出复制。
- `Program.cs` - 应用入口。
- `Forms/` - UI 和主要逻辑，使用 `Main` 部分类拆分。
- `Utils/` - 平台互操作、屏幕截取、鼠标/键盘控制、日志、模型检测等辅助类。
- `Model/` - 运行时 ONNX 模型文件。
- `Libs/gbild64.dll` - 原生设备库，通过 P/Invoke 调用。
- `YoloTrain/` - Python 训练脚本、数据集和模型产物，与 .NET 应用逻辑分离。

## 重要约定与注意事项
- 应用使用 `Main` 的部分类实现，并遵循 Windows 窗体事件驱动模式。
- 现有注释和 UI 文本为中文；未经明确要求，不要翻译或改写这些文本。
- 原生依赖和运行时依赖非常关键：不要移动或重命名 `Model/best.onnx` 或 `Libs/gbild64.dll`，除非同时更新加载路径。
- 修改代码时优先关注应用结构，不要在没有必要时做大范围重构。
- 除非明确要求处理训练流程，否则 `YoloTrain/` 应视为辅助训练模块。

## 在此仓库中的工作方式
- 使用仓库根目录的 `dotnet` 命令。
- 注意 Windows 平台及原生互操作约束。
- 修改或新增原生依赖处理时，务必检查 `CopyToOutputDirectory` 和路径解析。
- 如果需要补充文档，请优先更新 `readme.md`，而不是在 `AGENTS.md` 中重复说明。
