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

            device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
            model = AlexNet(num_classes=10).to(device)
            try:
                model.load_state_dict(torch.load('./weights/alexnet_cifar10.pth', map_location=device))
            except FileNotFoundError:
                print("错误: 未找到 './weights/alexnet_cifar10.pth'。请检查路径并确保已运行训练脚本。")
                return
            model.eval()

            print("从CIFAR-10测试集中随机抽取图片...")
            test_dataset_pil = datasets.CIFAR10(root='./data', train=False, download=True, transform=None)
            random_index = random.randint(0, len(test_dataset_pil) - 1)
            img, label = test_dataset_pil[random_index]
            print(f"已选择图片索引: {random_index}, 类别: {test_dataset_pil.classes[label]}")
            pixel_data = list(img.getdata())
            flat_pixel_data = [int(channel) for pixel in pixel_data for channel in pixel]
            transform = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.5, 0.5, 0.5), (0.5, 0.5, 0.5))])
            input_tensor = transform(img).unsqueeze(0).to(device)

            explanations = {
                "conv": {
                    "title": "Convolution Operation",
                    "text": "The kernel (red box) slides over the input layer. At each step, it performs a dot product to produce a single value in the output feature map. This process detects features like edges and textures."
                },
                "relu": {
                    "title": "ReLU Activation",
                    "text": "Applying the Rectified Linear Unit function. It's a simple rule: if a value is negative, it becomes zero; otherwise, it stays the same. This introduces non-linearity, allowing the network to learn more complex patterns."
                },
                "pool": {
                    "title": "Max Pooling",
                    "text": "The pooling window (red box) slides over the input. It takes the maximum value from the area it covers. This reduces the size of the feature map, making the network more efficient and robust to small variations."
                }
            }

            # 发送包含详细参数的拓扑结构
            print("正在计算并发送详细的模型拓扑...")
            topology = []
            x_for_topo = input_tensor
            for name, layer in model.features.named_children():
                output_shape = list(layer(x_for_topo).shape)
                details = ""
                if isinstance(layer, nn.Conv2d):
                    details = f"In: {layer.in_channels}, Out: {layer.out_channels}, K: {layer.kernel_size}, S: {layer.stride}, P: {layer.padding}"
                elif isinstance(layer, nn.MaxPool2d):
                    details = f"K: {layer.kernel_size}, S: {layer.stride}"
                
                topology.append({
                    "name": name, 
                    "type": layer.__class__.__name__, 
                    "output_shape": output_shape,
                    "details": details
                })
                x_for_topo = layer(x_for_topo)

            if not send_data(conn, {"type": "topology_init", "data": topology}): return
            
            print("拓扑已发送。等待Unity构建场景 (10秒)...")
            time.sleep(10)

            # 发送原始图像数据
            print("正在发送输入图像数据...")
            image_message = {
                "type": "input_image_data",
                "data": {"pixels": flat_pixel_data, "width": img.width, "height": img.height}
            }
            if not send_data(conn, image_message): return
            time.sleep(1.5)

            #分步可视化逻辑
            x = input_tensor
            for i, layer in enumerate(model.features):
                layer_name = str(i)
                prev_layer_name = "input" if i == 0 else str(i-1)
                
                print(f"\n--- 可视化层 {layer_name}: {layer.__class__.__name__} ---")

                # 根据层类型发送对应的解说词
                current_explanation = None
                if isinstance(layer, nn.Conv2d):
                    current_explanation = explanations["conv"]
                elif isinstance(layer, nn.ReLU):
                    current_explanation = explanations["relu"]
                elif isinstance(layer, nn.MaxPool2d):
                    current_explanation = explanations["pool"]

                if current_explanation:
                    explanation_message = {
                        "type": "explanation_update", # [新增] 新的消息类型
                        "data": current_explanation
                    }
                    if not send_data(conn, explanation_message): return
                    time.sleep(1) # 等待用户阅读

                # [修改] 调用分步动画函数 (这部分逻辑恢复)
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