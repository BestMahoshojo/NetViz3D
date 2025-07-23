import torch
import torch.nn as nn
from torchvision import datasets, transforms
import torch.optim as optim
from torch.utils.data import DataLoader
import os
from alexnet_model import AlexNet 

# --- 确保 'data' 和 'weights' 文件夹存在 ---
if not os.path.exists('./data'):
    os.makedirs('./data')
if not os.path.exists('./weights'):
    os.makedirs('./weights')

# 1. 定义AlexNet模型结构

# 2. 加载并预处理CIFAR-10数据集 (这部分不变)
transform = transforms.Compose([
    transforms.ToTensor(),
    transforms.Normalize((0.5, 0.5, 0.5), (0.5, 0.5, 0.5))
])

train_dataset = datasets.CIFAR10(root='./data', train=True, download=True, transform=transform)
train_loader = DataLoader(train_dataset, batch_size=64, shuffle=True)

test_dataset = datasets.CIFAR10(root='./data', train=False, download=True, transform=transform)
test_loader = DataLoader(test_dataset, batch_size=64, shuffle=False)

# 确定运行设备 (这部分不变)
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
print(f"将使用 {device} 设备进行训练...")

# 直接使用导入的AlexNet类来实例化模型
model = AlexNet(num_classes=10).to(device)
criterion = nn.CrossEntropyLoss()
optimizer = optim.SGD(model.parameters(), lr=0.01, momentum=0.9)

# 3. 训练模型 (函数定义不变)
def train_model(model, criterion, optimizer, train_loader, num_epochs=10):
    print("开始训练...")
    model.train()
    for epoch in range(num_epochs):
        running_loss = 0.0
        for i, data in enumerate(train_loader, 0):
            inputs, labels = data[0].to(device), data[1].to(device)
            optimizer.zero_grad()
            outputs = model(inputs)
            loss = criterion(outputs, labels)
            loss.backward()
            optimizer.step()

            running_loss += loss.item()
            if i % 100 == 99:
                print(f'[Epoch {epoch + 1}, Batch {i + 1}] loss: {running_loss / 100:.3f}')
                running_loss = 0.0
    print("训练完成！")

# 调用训练函数 (这部分不变)
train_model(model, criterion, optimizer, train_loader, num_epochs=10)

# 4. 保存训练好的模型权重 (这部分不变)
PATH = './weights/alexnet_cifar10.pth'
torch.save(model.state_dict(), PATH)
print(f"模型已保存至 {PATH}")

# 5. 在测试集上评估模型准确率 (这部分不变)
print("\n开始在测试集上评估模型...")
correct = 0
total = 0
model.eval()
with torch.no_grad():
    for data in test_loader:
        images, labels = data[0].to(device), data[1].to(device)
        outputs = model(images)
        _, predicted = torch.max(outputs.data, 1)
        total += labels.size(0)
        correct += (predicted == labels).sum().item()

accuracy = 100 * correct / total
print(f'模型在10000张测试图像上的准确率: {accuracy:.2f} %')