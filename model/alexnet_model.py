import torch
import torch.nn as nn

class AlexNet(nn.Module): # 我们保持类名不变，以便无缝替换
    def __init__(self, num_classes=10):
        super(AlexNet, self).__init__()
        self.features = nn.Sequential(
            # 第一个卷积块
            nn.Conv2d(3, 8, kernel_size=3, padding=1), # 3输入通道, 8输出通道
            nn.ReLU(inplace=True),
            nn.MaxPool2d(kernel_size=2, stride=2), # 图像尺寸减半
            
            # 第二个卷积块
            nn.Conv2d(8, 16, kernel_size=3, padding=1), # 8输入通道, 16输出通道
            nn.ReLU(inplace=True),
            nn.MaxPool2d(kernel_size=2, stride=2), # 图像尺寸再次减半
        )
        
        # CIFAR-10 图像 32x32 -> Pool 1 -> 16x16 -> Pool 2 -> 8x8
        # 所以分类器的输入是 16 (通道数) * 8 * 8
        self.classifier = nn.Sequential(
            nn.Linear(16 * 8 * 8, 128),
            nn.ReLU(inplace=True),
            nn.Linear(128, num_classes),
        )

    def forward(self, x):
        x = self.features(x)
        # 为了可视化，我们暂时不执行分类器部分
        x = x.view(-1, 16 * 8 * 8) # 展平操作
        x = self.classifier(x)
        return x