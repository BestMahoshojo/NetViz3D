# 交互式3D神经网络与算法可视化平台

这是一个基于Unity (C#) 和 Python (PyTorch) 开发的桌面应用程序，旨在通过实时、可交互的3D渲染技术，将抽象的神经网络和算法概念进行可视化，为计算机科学学习者提供一个沉浸式的辅助教学工具。

![项目截图占位符](https://via.placeholder.com/800x450.png?text=在此处替换为您的项目截图或GIF)
*(在此处替换为您的项目截图或GIF动图)*

---

## 核心功能与特点

*   **后端实时驱动**: 所有可视化动画均由后台运行的真实PyTorch模型实时计算驱动，保证了科学准确性。
*   **真三维自由探索**: 提供类似第一人称游戏的自由相机，用户可在3D空间中无限制地漫游、观察模型的内部结构。
*   **高度交互性**:
    *   **一键启动**: 自动管理后台Python进程，若模型不存在则会自动触发训练，实现零配置启动。
    *   **实时控制**: 提供开始、暂停/恢复、退出等功能，完全掌控可视化进程。
    *   **参数调节**: 可在运行时通过UI滑块实时调整3D模型的布局和相机参数。
*   **引导式教学设计**:
    *   **图层信息面板**: 动态生成包含所有网络层详细参数的列表，并可独立控制每一层的显示/隐藏。
    *   **实时解说面板**: 在执行关键计算步骤（卷积、激活、池化）前，自动更新教学文本，解释当前操作的原理与作用。

## 项目架构

本项目采用客户端/服务器 (C/S) 架构，实现了前后端的分离：

*   **前端 (客户端)**: **Unity (C#)** 负责所有3D场景渲染、动画表现、UI系统和用户交互。
*   **后端 (服务器)**: **Python (PyTorch)** 负责所有神经网络的计算、算法逻辑的执行，并将结果通过TCP Sockets实时发送给前端。

```
[ Unity前端 (C#) ] <--- (TCP Socket) ---> [ Python后端 (PyTorch) ]
      |                                        |
[ 用户交互与渲染 ]                         [ 模型计算与数据生成 ]
```

## 技术栈

*   **前端**:
    *   引擎: Unity 2022.3.x LTS
    *   语言: C#
    *   UI: UGUI + TextMeshPro
    *   JSON解析: Newtonsoft.Json
*   **后端**:
    *   语言: Python 3.9+ (推荐使用Anaconda管理环境)
    *   框架: PyTorch
    *   库: Torchvision, Pillow

## 安装与配置指南

请遵循以下步骤来配置和运行本项目。

### 1. 环境准备

*   安装 **Unity Hub** 和 **Unity Editor 2022.3.x LTS** 版本。
*   安装 **Anaconda** (或Miniconda)。

### 2. 后端 (Python) 配置

1.  **克隆或下载项目** 到本地。

2.  **创建Conda环境**:
    打开Anaconda Prompt或终端，导航到您的Python脚本文件夹 (例如 `.../my_project/model/`)，然后运行以下命令：
    ```bash
    # 创建一个名为 dy311 的新环境
    conda create -n dy311 python=3.9
    # 激活环境
    conda activate dy311
    ```

3.  **安装依赖**:
    将上面的 `requirements.txt` 文件放到Python脚本文件夹中，然后运行：
    ```bash
    pip install -r requirements.txt
    ```

4.  **准备模型权重**:
    *   确保您的预训练模型 `alexnet_cifar10.pth` 文件位于 `.../python_folder/weights/` 目录下。
    *   如果该文件不存在，项目首次启动时会自动运行 `train_and_save_model.py` 脚本进行训练，这可能需要几分钟时间。

### 3. 前端 (Unity) 配置

1.  **打开项目**:
    *   启动 Unity Hub。
    *   点击 "Open" -> "Add project from disk"。
    *   选择本项目的Unity项目文件夹 (包含 `Assets`, `Packages` 等子文件夹的目录)。

2.  **配置Python启动器**:
    *   在Unity编辑器中，打开 `MainMenuScene` (如果项目有) 或主可视化场景。
    *   在 `Hierarchy` 窗口中，找到 `VisualizerController` 对象并选中它。
    *   在 `Inspector` 窗口中，找到 **`Python Launcher`** 组件。
    *   **[最关键步骤]** 配置以下两个路径：
        *   `Python Executable Path`: 填入您Conda环境中的 **python.exe** 的**绝对路径**。
          *   *如何查找?* 在已激活 `dy311` 环境的终端中，输入 `where python` (Windows) 或 `which python` (macOS/Linux)。
          *   *示例 (Windows)*: `C:\Users\YourName\anaconda3\envs\dy311\python.exe`
        *   `Python Scripts Path`: 填入您所有 **.py** 脚本所在文件夹的**绝对路径**。
          *   *示例 (Windows)*: `D:\my_project\model`

3.  **连接UI引用**:
    *   检查 `VisualizerController` 和 `Canvas` 对象上的脚本组件 (`AdvancedVisualizer`, `UIManager`, `PythonLauncher` 等)。
    *   确保所有在 `Inspector` 中暴露出的字段（如按钮、滑块、预制件、其他脚本引用等）都已正确地从 `Hierarchy` 或 `Project` 窗口中拖拽赋值。

## 如何运行

1.  完成以上所有配置步骤。
2.  在Unity编辑器中，点击顶部的 **Play** 按钮。
3.  在应用程序的UI界面上，点击 **"Start"** 按钮来启动整个可视化流程。

## 操作控制

*   **移动**: `W`, `A`, `S`, `D` 键进行前后左右移动。
*   **升降**: `E` 键上升, `Q` 键下降。
*   **环顾**: **按住鼠标右键** 并移动鼠标。
*   **加速**: 按住 `Left Shift` 键。
*   **调节速度**: 使用鼠标滚轮，或UI面板上的滑块。

---
## 许可证

本项目采用 [MIT License](LICENSE.md) 授权。