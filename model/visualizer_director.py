import torch
import torch.nn as nn
import torch.nn.functional as F
from torchvision import datasets, transforms
import socket
import json
import time
from alexnet_model import AlexNet

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

            # 1. 加载模型和数据
            device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
            model = AlexNet(num_classes=10).to(device)
            try:
                model.load_state_dict(torch.load('./weights/alexnet_cifar10.pth'))
                model.to(device) # 再次确认模型在设备上
            except FileNotFoundError:
                print("错误: 未找到 'alexnet_cifar10.pth'。请先运行初始的训练脚本。")
                return
            model.eval()

            transform = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.5, 0.5, 0.5), (0.5, 0.5, 0.5))])
            test_dataset = datasets.CIFAR10(root='./data', train=False, download=True, transform=transform)
            input_tensor, label = test_dataset[1] # 换一张图片试试
            input_tensor = input_tensor.unsqueeze(0).to(device)

            # 2. 发送拓扑结构
            print("正在计算并发送模型拓扑...")
            topology = [{"name": "input", "type": "Input", "output_shape": list(input_tensor.shape)}]
            x = input_tensor
            for name, layer in model.features.named_children():
                x = layer(x)
                topology.append({"name": name, "type": layer.__class__.__name__, "output_shape": list(x.shape)})
            if not send_data(conn, {"type": "topology_init", "data": topology}): return
            
            print("拓扑已发送。等待Unity构建场景 (10秒)...")
            time.sleep(10)

            # 3. 逐层可视化前向传播
            x = input_tensor
            for i, layer in enumerate(model.features):
                layer_name = str(i)
                prev_layer_name = "input" if i == 0 else str(i-1)
                
                print(f"\n--- 可视化层 {layer_name}: {layer.__class__.__name__} ---")

                if isinstance(layer, nn.Conv2d):
                    if not visualize_convolution(conn, layer, x, prev_layer_name, layer_name): return
                elif isinstance(layer, nn.ReLU):
                    if not visualize_activation(conn, x, layer_name): return
                elif isinstance(layer, nn.MaxPool2d):
                    if not visualize_maxpool(conn, layer, x, prev_layer_name, layer_name): return

                # 计算当前层的实际输出，作为下一层的输入
                x = layer(x)
                print(f"层 {layer_name} 计算完成。")
                time.sleep(2) # 每层结束后暂停一下

            print("\n所有层可视化完毕!")
            send_data(conn, {"type": "visualization_complete"})


def visualize_convolution(conn, layer, input_map, input_name, output_name):
    p = layer.padding[0]
    s = layer.stride[0]
    k = layer.kernel_size[0]
    input_padded = F.pad(input_map, (p, p, p, p))
    _, _, H_in, W_in = input_padded.shape
    H_out, W_out = (H_in - k) // s + 1, (W_in - k) // s + 1
    
    # 遍历输出特征图的每一个像素
    for y_out in range(H_out):
        for x_out in range(W_out):
            y_start, x_start = y_out * s, x_out * s
            
            # 为了简化，我们只显示对第一个输出通道的计算
            # 完整的计算会涉及输入的所有通道
            output_val = F.conv2d(input_padded[:, :, y_start:y_start+k, x_start:x_start+k], layer.weight, layer.bias)[0, 0, 0, 0].item()

            step_data = {
                "type": "conv_step",
                "data": {
                    "input_layer_name": input_name,
                    "output_layer_name": output_name,
                    "input_start_coords": [y_start, x_start],
                    "kernel_size": k,
                    "output_coord": [0, y_out, x_out], # 只显示第一个输出通道
                    "output_value": output_val
                }
            }
            if not send_data(conn, step_data): return False
            time.sleep(0.01) # 控制动画速度
    return True

def visualize_activation(conn, input_map, layer_name): # layer_name 是ReLU层的名字
    output_map = F.relu(input_map)
    
    # 找到ReLU层前面的那个层的名字
    previous_layer_name = str(int(layer_name) - 1)

    update_data = {
        "type": "activation_update",
        "data": {
            # [修改] 我们不再发送ReLU自己的名字，而是发送它应该更新的层的名字
            "layer_name_to_update": previous_layer_name, 
            "activations": output_map.detach().squeeze(0).tolist()
        }
    }
    return send_data(conn, update_data)

def visualize_maxpool(conn, layer, input_map, input_name, output_name):
    k = layer.kernel_size
    s = layer.stride
    _, _, H_in, W_in = input_map.shape
    H_out, W_out = (H_in - k) // s + 1, (W_in - k) // s + 1

    for c in range(input_map.shape[1]): # 遍历通道
        for y_out in range(H_out):
            for x_out in range(W_out):
                y_start, x_start = y_out * s, x_out * s
                patch = input_map[0, c, y_start:y_start+k, x_start:x_start+k]
                max_val = patch.max()
                
                # 找到最大值在patch内的相对坐标
                max_idx_flat = torch.argmax(patch)
                max_idx_y = max_idx_flat // k
                max_idx_x = max_idx_flat % k

                step_data = {
                    "type": "pool_step",
                    "data": {
                        "input_layer_name": input_name,
                        "output_layer_name": output_name,
                        "input_start_coords": [y_start, x_start],
                        "pool_size": k,
                        "output_coord": [c, y_out, x_out],
                        "winner_coord_in_patch": [max_idx_y.item(), max_idx_x.item()],
                        "output_value": max_val.item()
                    }
                }
                if not send_data(conn, step_data): return False
                time.sleep(0.02)
    return True

if __name__ == '__main__':
    run_visualization()