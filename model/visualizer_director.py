import torch
import torch.nn as nn
from torchvision import transforms,datasets
import socket
import json
import time
from alexnet_model import AlexNet
from PIL import Image
import random

# --- 通信设置 ---
HOST = '127.0.0.1'
PORT = 65432

def send_data(connection, data):
    """将字典转换为JSON字符串并安全发送"""
    try:
        message = json.dumps(data).encode('utf-8')
        connection.sendall(len(message).to_bytes(4, 'big'))
        connection.sendall(message)
        return True
    except (ConnectionResetError, BrokenPipeError, ConnectionAbortedError) as e:
        print(f"连接中断: {e}")
        return False

# --- 主可视化函数 ---
def run_visualization():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind((HOST, PORT))
        s.listen()
        print(f"服务器正在监听 {HOST}:{PORT}...")
        conn, addr = s.accept()
        with conn:
            print(f"Unity已连接: {addr}")

            # 1. 加载模型
            device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
            model = AlexNet(num_classes=10).to(device)
            try:
                model.load_state_dict(torch.load('./weights/alexnet_cifar10.pth', map_location=device))
            except FileNotFoundError:
                print("错误: 未找到 './weights/alexnet_cifar10.pth'。请检查路径并确保已运行训练脚本。")
                return
            model.eval()

            # 2. [核心修改] 从CIFAR-10测试集中随机抽取一张图片
            print("从CIFAR-10测试集中随机抽取图片...")
            # 加载数据集以获取原始PIL Image (transform=None)
            test_dataset_pil = datasets.CIFAR10(root='./data', train=False, download=True, transform=None)
            
            # 随机选择一个索引
            random_index = random.randint(0, len(test_dataset_pil) - 1)
            img, label = test_dataset_pil[random_index] # 'img' 现在是一个原始的PIL图像对象
            print(f"已选择图片索引: {random_index}, 类别: {test_dataset_pil.classes[label]}")

            # 将图像像素数据转换为列表以发送给Unity
            # CIFAR-10图像本身就是32x32，无需resize
            pixel_data = list(img.getdata())
            flat_pixel_data = [int(channel) for pixel in pixel_data for channel in pixel]

            # 定义并应用transform，以创建送入模型的张量
            transform = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.5, 0.5, 0.5), (0.5, 0.5, 0.5))])
            input_tensor = transform(img).unsqueeze(0).to(device)


            # 3. 发送拓扑结构
            print("正在计算并发送模型拓扑...")
            topology = []
            x_for_topo = input_tensor
            for name, layer in model.features.named_children():
                x_for_topo = layer(x_for_topo)
                topology.append({"name": name, "type": layer.__class__.__name__, "output_shape": list(x_for_topo.shape)})
            if not send_data(conn, {"type": "topology_init", "data": topology}): return
            
            print("拓扑已发送。等待Unity构建场景 (10秒)...")
            time.sleep(10)

            # 4. 发送原始图像数据
            print("正在发送输入图像数据...")
            image_message = {
                "type": "input_image_data",
                "data": {"pixels": flat_pixel_data, "width": img.width, "height": img.height}
            }
            if not send_data(conn, image_message): return
            time.sleep(1.5)

            # 5. 统一的逐层可视化逻辑
            x = input_tensor
            for i, layer in enumerate(model.features):
                layer_name = str(i)
                print(f"\n--- 计算并发送层 {layer_name}: {layer.__class__.__name__} 的数据 ---")
                
                x = layer(x)

                layer_update_message = {
                    "type": "layer_update",
                    "data": {
                        "layer_name": layer_name,
                        "activations": x.detach().squeeze(0).tolist()
                    }
                }
                if not send_data(conn, layer_update_message): return
                
                time.sleep(1.5) 

            print("\n所有层可视化完毕!")
            send_data(conn, {"type": "visualization_complete"})

if __name__ == '__main__':
    run_visualization()