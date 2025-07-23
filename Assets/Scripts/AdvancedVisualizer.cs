using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

#region Data Structures for JSON Deserialization
// 这些结构必须与Python发送的JSON键匹配
[Serializable] public class LayerTopologyInfo { public string name; public string type; public List<int> output_shape; }
[Serializable] public class ConvStepData { public string input_layer_name; public string output_layer_name; public List<int> input_start_coords; public int kernel_size; public List<int> output_coord; public float output_value; }
[Serializable] public class ActivationUpdateData { public string layer_name_to_update; public List<List<List<float>>> activations; }
[Serializable] public class PoolStepData { public string input_layer_name; public string output_layer_name; public List<int> input_start_coords; public int pool_size; public List<int> output_coord; public List<int> winner_coord_in_patch; public float output_value; }
#endregion

public class AdvancedVisualizer : MonoBehaviour
{
    #region Inspector Fields
    [Header("Network Connection")]
    public string serverHost = "127.0.0.1";
    public int serverPort = 65432;

    [Header("Layer Prefabs")]
    // [修改] 将单个neuronPrefab替换为多个特定类型的Prefab
    public GameObject convNeuronPrefab;
    public GameObject poolNeuronPrefab;
    public GameObject reluNeuronPrefab;
    public GameObject defaultNeuronPrefab; // 用于其他未指定类型的层

    [Header("Animation Prefabs")]
    public GameObject kernelPrefab;
    public GameObject poolRegionPrefab;

    [Header("Scene Setup")]
    public Transform networkContainer;

    [Header("Visualization Tuning (在这里调节大小和间距)")]
    // [新增] 用于控制神经元方块大小的变量 (通过Prefab的Scale控制更直观，但也可代码控制)
    // 我们主要通过调节间距来控制视觉大小

    [Tooltip("同一层内，单个神经元之间的水平/垂直距离")]
    [Range(0.1f, 2.0f)]
    public float neuronSpacing = 0.2f;

    [Tooltip("同一层内，不同通道（深度方向）之间的距离")]
    [Range(0.1f, 5.0f)]
    public float channelSpacing = 0.8f;

    [Tooltip("不同网络层之间的距离")]
    [Range(1.0f, 20.0f)]
    public float layerSpacing = 5f;
    #endregion

    #region Private Fields
    private TcpClient client;
    private NetworkStream stream;
    private Thread clientReceiveThread;
    private bool isRunning = true;
    private readonly Queue<string> messageQueue = new Queue<string>();

    private Dictionary<string, GameObject> layerContainers = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject[,,]> neuronObjects = new Dictionary<string, GameObject[,,]>();
    private Dictionary<string, Vector3Int> layerDimensions = new Dictionary<string, Vector3Int>(); // C, H, W

    private GameObject kernelInstance;
    private GameObject poolRegionInstance;
    private float lastNeuronSpacing;
    private float lastChannelSpacing;
    private float lastLayerSpacing;
    private List<string> orderedLayerNames = new List<string>();
    #endregion

    #region Unity Lifecycle Methods
    void Start()
    {
        ConnectToServer();
        // [新增] 初始化上一帧的值，以当前Inspector中的值为准
        lastNeuronSpacing = neuronSpacing;
        lastChannelSpacing = channelSpacing;
        lastLayerSpacing = layerSpacing;
    }

    void Update()
    {
        // 1. 处理网络消息队列
        while (messageQueue.Count > 0)
        {
            string message = "";
            lock (messageQueue) { if (messageQueue.Count > 0) message = messageQueue.Dequeue(); }
            if (!string.IsNullOrEmpty(message)) ProcessMessage(message);
        }

        // 2. 每一帧都检查布局参数是否被修改
        if (Mathf.Abs(neuronSpacing - lastNeuronSpacing) > 0.001f ||
            Mathf.Abs(channelSpacing - lastChannelSpacing) > 0.001f ||
            Mathf.Abs(layerSpacing - lastLayerSpacing) > 0.001f)
        {
            // 如果有变化，则调用更新函数
            UpdateNetworkLayout();

            // 更新"上一帧"的值，为下一次比较做准备
            lastNeuronSpacing = neuronSpacing;
            lastChannelSpacing = channelSpacing;
            lastLayerSpacing = layerSpacing;
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (clientReceiveThread != null && clientReceiveThread.IsAlive) clientReceiveThread.Abort();
        if (stream != null) stream.Close();
        if (client != null) client.Close();
    }
    #endregion

    #region Network Handling
    private void ConnectToServer()
    {
        clientReceiveThread = new Thread(() =>
        {
            try
            {
                client = new TcpClient("127.0.0.1", 65432);
                stream = client.GetStream();
                Debug.Log("Successfully connected to Python director!");
                byte[] lengthBytes = new byte[4];
                while (isRunning)
                {
                    int bytesRead = stream.Read(lengthBytes, 0, 4);
                    if (bytesRead < 4) continue;
                    Array.Reverse(lengthBytes);
                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                    byte[] messageBytes = new byte[messageLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < messageLength) totalBytesRead += stream.Read(messageBytes, totalBytesRead, messageLength - totalBytesRead);
                    string serverMessage = Encoding.UTF8.GetString(messageBytes);
                    lock (messageQueue) { messageQueue.Enqueue(serverMessage); }
                }
            }
            catch (Exception e) { Debug.LogError("Socket exception: " + e); }
        })
        { IsBackground = true };
        clientReceiveThread.Start();
    }
    #endregion

    #region Message Processing
    private void ProcessMessage(string message)
    {
        // 打印接收到的原始消息，便于调试
        Debug.Log("Received from Python: " + message);

        var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
        string type = json["type"].ToString();

        // [新增的修复逻辑]
        // 检查是否是结束消息，如果是，则直接处理并返回，不再往下执行
        if (type == "visualization_complete")
        {
            Debug.Log("--- VISUALIZATION COMPLETE ---");
            // 这里可以添加任何你希望在可视化结束时执行的逻辑
            // 例如，让动画模型停留在最后一帧，或者显示一个结束面板
            return; // 处理完毕，提前退出函数
        }

        // [新增的修复逻辑]
        // 添加一个更安全的检查，确保 'data' 键存在才继续
        if (!json.ContainsKey("data"))
        {
            Debug.LogWarning($"Received message of type '{type}' with no 'data' field. Skipping.");
            return;
        }

        // 如果程序能走到这里，说明'data'键肯定是存在的
        string data = json["data"].ToString();

        switch (type)
        {
            case "topology_init":
                var topology = JsonConvert.DeserializeObject<List<LayerTopologyInfo>>(data);
                StartCoroutine(CreateDetailedArchitecture(topology));
                break;
            case "conv_step":
                var convData = JsonConvert.DeserializeObject<ConvStepData>(data);
                StartCoroutine(AnimateConvStep(convData));
                break;
            case "activation_update":
                var actData = JsonConvert.DeserializeObject<ActivationUpdateData>(data);
                StartCoroutine(AnimateActivation(actData));
                break;
            case "pool_step":
                var poolData = JsonConvert.DeserializeObject<PoolStepData>(data);
                StartCoroutine(AnimatePoolStep(poolData));
                break;
        }
    }
    #endregion

    #region Coroutine Animations
    private IEnumerator CreateDetailedArchitecture(List<LayerTopologyInfo> topology)
    {
        Debug.Log("Creating detailed architecture with custom settings...");
        orderedLayerNames.Clear();
        float xOffset = 0f;
        foreach (var layerInfo in topology)
        {
            orderedLayerNames.Add(layerInfo.name);
            // [新增] 根据层类型选择要使用的Prefab
            GameObject prefabToUse = defaultNeuronPrefab;
            switch (layerInfo.type)
            {
                case "Conv2d":
                    prefabToUse = convNeuronPrefab;
                    break;
                case "MaxPool2d":
                    prefabToUse = poolNeuronPrefab;
                    break;
                case "ReLU":
                    prefabToUse = reluNeuronPrefab;
                    break;
            }
            if (prefabToUse == null) prefabToUse = defaultNeuronPrefab; // 如果没指定，就用默认的

            GameObject layerContainer = new GameObject(layerInfo.name + " (" + layerInfo.type + ")");
            layerContainer.transform.SetParent(networkContainer);
            layerContainer.transform.position = new Vector3(xOffset, 0, 0);
            layerContainers[layerInfo.name] = layerContainer;

            int channels = layerInfo.output_shape[1];
            int height = layerInfo.output_shape[2];
            int width = layerInfo.output_shape[3];
            layerDimensions[layerInfo.name] = new Vector3Int(channels, height, width);
            neuronObjects[layerInfo.name] = new GameObject[channels, height, width];

            for (int c = 0; c < channels; c++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // [修改] 使用我们暴露在Inspector中的变量来计算位置
                        Vector3 position = new Vector3(
                            (x - width / 2f) * neuronSpacing,
                            (y - height / 2f) * neuronSpacing,
                            (c - channels / 2f) * channelSpacing
                        );
                        GameObject neuron = Instantiate(prefabToUse, layerContainer.transform); // 使用选择好的Prefab
                        neuron.transform.localPosition = position;
                        neuron.name = $"N_{c}_{y}_{x}";
                        neuronObjects[layerInfo.name][c, y, x] = neuron;
                    }
                }
                yield return null;
            }
            // [修改] 使用暴露的变量来计算下一层的偏移
            xOffset += layerSpacing + (channels * channelSpacing);
        }
        UpdateNetworkLayout();
        Debug.Log("Architecture creation complete.");
        yield return null;
    }

    private IEnumerator AnimateConvStep(ConvStepData data)
    {
        if (kernelInstance == null) kernelInstance = Instantiate(kernelPrefab);

        // Position kernel on input layer
        GameObject inputContainer = layerContainers[data.input_layer_name];
        Vector3Int inputDims = layerDimensions[data.input_layer_name];
        float kernelScale = data.kernel_size * neuronSpacing;
        kernelInstance.transform.localScale = new Vector3(kernelScale, kernelScale, inputDims.x * channelSpacing); // Cover all input channels
        kernelInstance.transform.SetParent(inputContainer.transform, false);

        Vector3 kernelPos = new Vector3(
            (data.input_start_coords[1] + data.kernel_size / 2f - inputDims.z / 2f) * neuronSpacing,
            (data.input_start_coords[0] + data.kernel_size / 2f - inputDims.y / 2f) * neuronSpacing,
            0
        );
        kernelInstance.transform.localPosition = kernelPos;

        // Update output neuron
        UpdateNeuronValue(neuronObjects[data.output_layer_name][data.output_coord[0], data.output_coord[1], data.output_coord[2]], data.output_value);

        yield return new WaitForSeconds(0.01f);
    }

    private IEnumerator AnimateActivation(ActivationUpdateData data)
    {
        // [修改] 使用新的字段
        Debug.Log($"Animating Activation, updating layer {data.layer_name_to_update}");
        var activations = data.activations;
        for(int c=0; c < activations.Count; c++)
        {
            for(int y=0; y < activations[c].Count; y++)
            {
                for (int x=0; x < activations[c][y].Count; x++)
                {
                    // [修改] 使用新的字段作为key
                    UpdateNeuronValue(neuronObjects[data.layer_name_to_update][c, y, x], activations[c][y][x]);
                }
            }
            yield return null;
        }
    }

    private IEnumerator AnimatePoolStep(PoolStepData data)
    {
        if (poolRegionInstance == null) poolRegionInstance = Instantiate(poolRegionPrefab);

        // Position pool region on input layer
        GameObject inputContainer = layerContainers[data.input_layer_name];
        Vector3Int inputDims = layerDimensions[data.input_layer_name];
        float regionScale = data.pool_size * neuronSpacing;
        poolRegionInstance.transform.localScale = new Vector3(regionScale, regionScale, 0.1f); // Only on one channel
        poolRegionInstance.transform.SetParent(inputContainer.transform, false);

        Vector3 regionPos = new Vector3(
            (data.input_start_coords[1] + data.pool_size / 2f - inputDims.z / 2f) * neuronSpacing,
            (data.input_start_coords[0] + data.pool_size / 2f - inputDims.y / 2f) * neuronSpacing,
            (data.output_coord[0] - inputDims.x / 2f) * channelSpacing // Position on correct channel
        );
        poolRegionInstance.transform.localPosition = regionPos;

        // Update output neuron
        UpdateNeuronValue(neuronObjects[data.output_layer_name][data.output_coord[0], data.output_coord[1], data.output_coord[2]], data.output_value);

        yield return new WaitForSeconds(0.02f);
    }
    #endregion

    #region Helper Methods
    private void UpdateNeuronValue(GameObject neuron, float value)
    {
        var renderer = neuron.GetComponent<Renderer>();
        // Normalize value for color mapping. You might need to adjust the divisor.
        float normalizedValue = Mathf.Clamp(value / 2.0f, -1.0f, 1.0f);
        Color valueColor = normalizedValue > 0 ? Color.Lerp(Color.white, Color.green, normalizedValue) : Color.Lerp(Color.white, Color.red, -normalizedValue);
        renderer.material.SetColor("_Color", valueColor);
        // For URP/HDRP, you might use "_BaseColor" and "_EmissiveColor"
        renderer.material.SetColor("_EmissionColor", valueColor * Mathf.Abs(normalizedValue));
    }
    public void UpdateNetworkLayout()
    {
        if (layerContainers.Count == 0) return;

        // [修改] 将 xOffset 重命名为 zOffset，使其含义更清晰
        float zOffset = 0f;

        foreach (string layerName in orderedLayerNames)
        {
            if (!layerContainers.ContainsKey(layerName)) continue;

            GameObject layerContainer = layerContainers[layerName];
            
            // 1. [核心修改] 重新定位整个层的容器，从X轴改为Z轴
            layerContainer.transform.position = new Vector3(0, 0, zOffset);

            // 2. 重新定位该层内部的所有神经元 (这部分逻辑不变)
            Vector3Int dims = layerDimensions[layerName];
            int channels = dims.x;
            int height = dims.y;
            int width = dims.z;

            for (int c = 0; c < channels; c++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector3 position = new Vector3(
                            (x - width / 2f) * neuronSpacing,
                            (y - height / 2f) * neuronSpacing,
                            (c - channels / 2f) * channelSpacing
                        );
                        neuronObjects[layerName][c, y, x].transform.localPosition = position;
                    }
                }
            }

            // 3. [修改] 计算下一层在Z轴上的起始偏移量
            //    我们使用层的“厚度”（即通道跨度）来决定下一层的起始位置
            float layerDepth = channels * channelSpacing;
            zOffset += layerSpacing + layerDepth;
        }
    }
    #endregion
}