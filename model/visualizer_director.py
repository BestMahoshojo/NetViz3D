import torch
import torch.nn as nn
import torch.nn.functional as F
from torchvision import transforms, datasets
import socket
import json
import time
from alexnet_model import AlexNet
from PIL import Image
import random

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

            # 2. 从CIFAR-10测试集中随机抽取一张图片
            print("从CIFAR-10测试集中随机抽取图片...")
            test_dataset_pil = datasets.CIFAR10(root='./data', train=False, download=True, transform=None)
            random_index = random.randint(0, len(test_dataset_pil) - 1)
            img, label = test_dataset_pil[random_index]
            print(f"已选择图片索引: {random_index}, 类别: {test_dataset_pil.classes[label]}")
            pixel_data = list(img.getdata())
            flat_pixel_data = [int(channel) for pixel in pixel_data for channel in pixel]
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

            # 5. [核心修改] 恢复分步、精细化的可视化逻辑
            x = input_tensor
            for i, layer in enumerate(model.features):
                layer_name = str(i)
                prev_layer_name = "input" if i == 0 else str(i-1)
                
                print(f"\n--- 可视化层 {layer_name}: {layer.__class__.__name__} ---")

                if isinstance(layer, nn.Conv2d):
                    if not visualize_convolution(conn, layer, x, prev_layer_name, layer_name): return
                elif isinstance(layer, nn.ReLU):
                    if not visualize_activation_as_grayscale_update(conn, x, prev_layer_name): return
                elif isinstance(layer, nn.MaxPool2d):
                    if not visualize_maxpool(conn, layer, x, prev_layer_name, layer_name): return

                x = layer(x)
                print(f"层 {layer_name} 计算完成。")
                time.sleep(2)

            print("\n所有层可视化完毕!")
            send_data(conn, {"type": "visualization_complete"})

def visualize_convolution(conn, layer, input_map, input_name, output_name):
    print("  预计算整层输出...")
    output_map = F.conv2d(input_map, layer.weight, layer.bias)
    
    min_val, max_val = output_map.min().item(), output_map.max().item()
    val_range = max_val - min_val if (max_val - min_val) > 1e-5 else 1.0

    _, C_out, H_out, W_out = output_map.shape
    p, s, k = layer.padding[0], layer.stride[0], layer.kernel_size[0]

    print("  开始分步发送卷积动画...")
    for c_out in range(C_out):
        for y_out in range(H_out):
            for x_out in range(W_out):
                output_val = output_map[0, c_out, y_out, x_out].item()
                step_data = {
                    "type": "conv_step",
                    "data": {
                        "input_layer_name": input_name,
                        "output_layer_name": output_name,
                        "input_start_coords": [(y_out * s) - p, (x_out * s) - p],
                        "kernel_size": k,
                        "output_coord": [c_out, y_out, x_out],
                        "output_value": output_val,
                        "min_val": min_val,
                        "val_range": val_range
                    }
                }
                if not send_data(conn, step_data): return False
                time.sleep(0.001)
        print(f"    通道 {c_out+1}/{C_out} 完成")
    return True

def visualize_maxpool(conn, layer, input_map, input_name, output_name):
    print("  预计算整层输出...")
    output_map = F.max_pool2d(input_map, layer.kernel_size, layer.stride)

    min_val, max_val = output_map.min().item(), output_map.max().item()
    val_range = max_val - min_val if (max_val - min_val) > 1e-5 else 1.0
    
    k, s = layer.kernel_size, layer.stride
    _, C_out, H_out, W_out = output_map.shape
    
    print("  开始分步发送池化动画...")
    for c in range(C_out):
        for y_out in range(H_out):
            for x_out in range(W_out):
                output_val = output_map[0, c, y_out, x_out].item()
                step_data = {
                    "type": "pool_step",
                    "data": {
                        "input_layer_name": input_name,
                        "output_layer_name": output_name,
                        "input_start_coords": [y_out * s, x_out * s],
                        "pool_size": k,
                        "output_coord": [c, y_out, x_out],
                        "output_value": output_val,
                        "min_val": min_val,
                        "val_range": val_range
                    }
                }
                if not send_data(conn, step_data): return False
                time.sleep(0.005)
    return True

def visualize_activation_as_grayscale_update(conn, input_map, input_layer_name_to_update):
    print("  计算ReLU激活...")
    output_map = F.relu(input_map)
    min_val, max_val = 0, output_map.max().item() # ReLU的最小值永远是0
    val_range = max_val - min_val if (max_val - min_val) > 1e-5 else 1.0

    update_data = {
        "type": "layer_update",
        "data": {
            "layer_name": input_layer_name_to_update,
            "activations": output_map.detach().squeeze(0).tolist(),
            "min_val": min_val,
            "val_range": val_range
        }
    }
    return send_data(conn, update_data)

if __name__ == '__main__':
    run_visualization()