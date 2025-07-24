using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks; 

public class AdvancedVisualizer : MonoBehaviour
{
    #region Inspector Fields
    [Header("Network Connection")]
    public string serverHost = "127.0.0.1";
    public int serverPort = 65432;

    [Header("Layer Prefabs")]
    public GameObject convNeuronPrefab;
    public GameObject poolNeuronPrefab;
    public GameObject reluNeuronPrefab;
    public GameObject defaultNeuronPrefab;

    [Header("Animation Prefabs")]
    public GameObject kernelPrefab;
    public GameObject poolRegionPrefab;

    [Header("Scene Setup")]
    public Transform networkContainer;

    [Header("Visualization Tuning")]
    [Tooltip("同一层内，单个神经元之间的水平/垂直距离")]
    [Range(0.05f, 2.0f)]
    public float neuronSpacing = 0.15f;
    [Tooltip("同一层内，不同通道（深度方向）之间的距离")]
    [Range(0.1f, 5.0f)]
    public float channelSpacing = 0.5f;
    [Tooltip("不同网络层之间的距离")]
    [Range(1.0f, 20.0f)]
    public float layerSpacing = 3f;

    [Header("System References")]
    public UIManager uiManager;
    #endregion

    #region Public State
    [HideInInspector]
    public bool isPaused = false;
    #endregion

    #region Private Fields
    private TcpClient client;
    private NetworkStream stream;
    private Thread clientReceiveThread;
    private bool isRunning = true;
    private readonly Queue<string> messageQueue = new Queue<string>();

    private Dictionary<string, GameObject> layerContainers = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject[,,]> neuronObjects = new Dictionary<string, GameObject[,,]>();
    private Dictionary<string, Vector3Int> layerDimensions = new Dictionary<string, Vector3Int>();

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
        // ConnectToServer(); 
        
        lastNeuronSpacing = neuronSpacing;
        lastChannelSpacing = channelSpacing;
        lastLayerSpacing = layerSpacing;
    }

    void Update()
    {
        while (!isPaused && messageQueue.Count > 0)
        {
            string message = "";
            lock (messageQueue) { if (messageQueue.Count > 0) message = messageQueue.Dequeue(); }
            if (!string.IsNullOrEmpty(message)) ProcessMessage(message);
        }

        if (Mathf.Abs(neuronSpacing - lastNeuronSpacing) > 0.001f ||
            Mathf.Abs(channelSpacing - lastChannelSpacing) > 0.001f ||
            Mathf.Abs(layerSpacing - lastLayerSpacing) > 0.001f)
        {
            UpdateNetworkLayout();
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
    // [修改] 将原有的ConnectToServer逻辑改为由StartConnecting启动的协程
    public void StartConnecting()
    {
        // 检查是否已经有一个活动的连接线程或客户端已连接，防止重复启动
        if ((clientReceiveThread != null && clientReceiveThread.IsAlive) || (client != null && client.Connected))
        {
            Debug.LogWarning("连接已在进行中或已建立，请勿重复调用。");
            return;
        }
        
        // 启动一个新的协程来处理连接和重试的逻辑
        StartCoroutine(ConnectWithRetries());
    }

    /// <summary>
    /// 尝试连接服务器，并在失败时进行重试。
    /// </summary>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="retryDelay">每次重试前的等待时间（秒）</param>
    private IEnumerator ConnectWithRetries(int maxRetries = 5, float retryDelay = 1.0f)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            Debug.Log($"正在尝试连接服务器... (第 {attempt}/{maxRetries} 次)");
            
            // [核心修改] 使用一个Task变量来启动后台连接，但不await它
            bool connectionSuccess = false;
            Exception connectionException = null;
            Task connectionTask = Task.Run(() => {
                try
                {
                    client = new TcpClient();
                    client.Connect(serverHost, serverPort);
                    connectionSuccess = true;
                }
                catch (Exception e)
                {
                    connectionException = e; // 在后台线程中捕获异常
                }
            });

            // [核心修改] 使用 yield return new WaitUntil(...) 来等待Task完成
            // 这可以让协程在这里暂停，直到后台任务结束，同时不会冻结Unity主线程
            yield return new WaitUntil(() => connectionTask.IsCompleted);

            // Task完成后，检查结果
            if (connectionSuccess)
            {
                Debug.Log("服务器连接成功！");
                stream = client.GetStream();
                isRunning = true;
                clientReceiveThread = new Thread(ReceiveDataLoop) { IsBackground = true };
                clientReceiveThread.Start();
                yield break; 
            }
            
            // 如果有异常，就在主线程中打印出来
            if (connectionException != null)
            {
                Debug.LogWarning($"第 {attempt} 次连接失败: {connectionException.Message}");
            }

            if (attempt == maxRetries)
            {
                Debug.LogError("已达到最大重试次数，连接失败。");
                yield break;
            }
            
            Debug.Log($"将在 {retryDelay} 秒后重试...");
            yield return new WaitForSeconds(retryDelay);
        }
    }

    /// <summary>
    /// 一个专门用于在后台线程中循环接收数据的函数。
    /// </summary>
    private void ReceiveDataLoop()
    {
        try
        {
            byte[] lengthBytes = new byte[4];
            while (isRunning && client != null && client.Connected)
            {
                // Read() 是一个阻塞操作，会在此等待直到有数据可读
                int bytesRead = stream.Read(lengthBytes, 0, 4);
                
                // 如果Read返回0或更少，通常表示连接已从另一端关闭
                if (bytesRead <= 0)
                {
                    if (isRunning) Debug.LogWarning("连接已由服务器端关闭。");
                    break; 
                }

                Array.Reverse(lengthBytes);
                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                byte[] messageBytes = new byte[messageLength];
                int totalBytesRead = 0;
                while (totalBytesRead < messageLength)
                {
                    totalBytesRead += stream.Read(messageBytes, totalBytesRead, messageLength - totalBytesRead);
                }
                
                string serverMessage = Encoding.UTF8.GetString(messageBytes);
                
                // 使用主线程调度器将消息安全地放入队列
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    lock (messageQueue) { messageQueue.Enqueue(serverMessage); }
                });
            }
        }
        catch (Exception e)
        {
            // 如果isRunning为true，说明这不是我们主动关闭的，而是意外错误
            if (isRunning)
            {
                 UnityMainThreadDispatcher.Instance().Enqueue(() => 
                    Debug.LogError("网络接收线程异常: " + e)
                );
            }
        }
        finally
        {
            Debug.Log("网络接收线程已退出。");
        }
    }
    #endregion

    #region Message Processing
    private void ProcessMessage(string message)
    {
        var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
        string type = json["type"].ToString();

        if (type == "visualization_complete")
        {
            Debug.Log("--- VISUALIZATION COMPLETE ---");
            if (kernelInstance) kernelInstance.SetActive(false);
            if (poolRegionInstance) poolRegionInstance.SetActive(false);
            return;
        }
        if (!json.ContainsKey("data"))
        {
            Debug.LogWarning($"Received message of type '{type}' with no 'data' field. Skipping.");
            return;
        }
        string data = json["data"].ToString();

        switch (type)
        {
            case "topology_init":
                var topology = JsonConvert.DeserializeObject<List<DetailedLayerInfo>>(data);
                StartCoroutine(CreateDetailedArchitecture(topology));
                if (uiManager != null)
                {
                    uiManager.PopulateLayerInfoPanel(topology);
                }
                break;
            case "input_image_data":
                var imageData = JsonConvert.DeserializeObject<InputImageData>(data);
                StartCoroutine(AnimateInputLayer(imageData));
                break;
            case "layer_update":
                var layerData = JsonConvert.DeserializeObject<LayerUpdateData>(data);
                StartCoroutine(AnimateLayerUpdate(layerData));
                break;
            case "conv_step":
                var convData = JsonConvert.DeserializeObject<ConvStepData>(data);
                StartCoroutine(AnimateConvStep(convData));
                break;
            case "pool_step":
                var poolData = JsonConvert.DeserializeObject<PoolStepData>(data);
                StartCoroutine(AnimatePoolStep(poolData));
                break;
            case "explanation_update":
                if (uiManager != null)
                {
                    var explanationData = JsonConvert.DeserializeObject<ExplanationData>(data);
                    uiManager.UpdateExplanationPanel(explanationData.title, explanationData.text);
                }
                break;
        }
    }
    #endregion

    #region Coroutine Animations
    private IEnumerator CreateDetailedArchitecture(List<DetailedLayerInfo> topology)
    {
        Debug.Log("Creating detailed architecture...");
        orderedLayerNames.Clear();
        // Python脚本现在不发送input层，这里手动创建
        var inputLayerInfo = new DetailedLayerInfo { name = "input", type = "Input", output_shape = new List<int> { 1, 3, 32, 32 }, details = "32x32 RGB Image" };
        List<DetailedLayerInfo> fullTopology = new List<DetailedLayerInfo>(topology);
        fullTopology.Insert(0, inputLayerInfo);

        foreach (var layerInfo in fullTopology)
        {
            orderedLayerNames.Add(layerInfo.name);
            GameObject prefabToUse = defaultNeuronPrefab;
            switch (layerInfo.type)
            {
                case "Input": prefabToUse = defaultNeuronPrefab; break;
                case "Conv2d": prefabToUse = convNeuronPrefab; break;
                case "MaxPool2d": prefabToUse = poolNeuronPrefab; break;
                case "ReLU": prefabToUse = reluNeuronPrefab; break;
            }
            if (prefabToUse == null) prefabToUse = defaultNeuronPrefab;
            GameObject layerContainer = new GameObject(layerInfo.name + " (" + layerInfo.type + ")");
            layerContainer.transform.SetParent(networkContainer);
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
                        GameObject neuron = Instantiate(prefabToUse, layerContainer.transform);
                        neuron.name = $"N_{c}_{y}_{x}";
                        neuronObjects[layerInfo.name][c, y, x] = neuron;
                    }
                }
            }
            yield return new WaitForEndOfFrame();
        }
        UpdateNetworkLayout();
        Debug.Log("Architecture creation complete.");
    }
    
    private IEnumerator AnimateInputLayer(InputImageData data)
    {
        Debug.Log("Mapping input image to the input layer...");
        string inputLayerName = "input";
        if (!neuronObjects.ContainsKey(inputLayerName)) yield break;
        Color[] pixels = new Color[data.width * data.height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32((byte)data.pixels[i * 3], (byte)data.pixels[i * 3 + 1], (byte)data.pixels[i * 3 + 2], 255);
        }
        Vector3Int dims = layerDimensions[inputLayerName];
        for (int c = 0; c < dims.x; c++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int x = 0; x < dims.z; x++)
                {
                    Color pixelColor = pixels[(dims.y - 1 - y) * dims.z + x];
                    float channelValue = (c == 0) ? pixelColor.r : (c == 1) ? pixelColor.g : pixelColor.b;
                    Color finalColor = new Color(channelValue, channelValue, channelValue, 1.0f);
                    var renderer = neuronObjects[inputLayerName][c, y, x].GetComponent<Renderer>();
                    renderer.material.SetColor("_Color", finalColor);
                    renderer.material.SetColor("_EmissionColor", finalColor);
                }
            }
            yield return null;
        }
        Debug.Log("Input image mapped.");
    }

    private IEnumerator AnimateConvStep(ConvStepData data)
    {
        if (kernelInstance == null)
        {
            kernelInstance = Instantiate(kernelPrefab);
            kernelInstance.name = "ConvolutionKernel";
        }
        kernelInstance.SetActive(true);
        if (poolRegionInstance) poolRegionInstance.SetActive(false);
        GameObject inputContainer = layerContainers[data.input_layer_name];
        Vector3Int inputDims = layerDimensions[data.input_layer_name];
        float kernelScale = data.kernel_size * neuronSpacing;
        kernelInstance.transform.localScale = new Vector3(kernelScale, kernelScale, inputDims.x * channelSpacing);
        kernelInstance.transform.SetParent(inputContainer.transform, false);
        Vector3 kernelPos = new Vector3(
            (data.input_start_coords[1] + data.kernel_size / 2f - inputDims.z / 2f) * neuronSpacing,
            (data.input_start_coords[0] + data.kernel_size / 2f - inputDims.y / 2f) * neuronSpacing,
            0);
        kernelInstance.transform.localPosition = kernelPos;
        var neuronToUpdate = neuronObjects[data.output_layer_name][data.output_coord[0], data.output_coord[1], data.output_coord[2]];
        UpdateNeuronGrayscale(neuronToUpdate, data.output_value, data.min_val, data.val_range);
        yield return new WaitForEndOfFrame();
    }

    private IEnumerator AnimatePoolStep(PoolStepData data)
    {
        if (poolRegionInstance == null)
        {
            poolRegionInstance = Instantiate(poolRegionPrefab);
            poolRegionInstance.name = "PoolingRegion";
        }
        poolRegionInstance.SetActive(true);
        if (kernelInstance) kernelInstance.SetActive(false);
        GameObject inputContainer = layerContainers[data.input_layer_name];
        Vector3Int inputDims = layerDimensions[data.input_layer_name];
        float regionScale = data.pool_size * neuronSpacing;
        poolRegionInstance.transform.localScale = new Vector3(regionScale, regionScale, 0.1f);
        poolRegionInstance.transform.SetParent(inputContainer.transform, false);
        Vector3 regionPos = new Vector3(
            (data.input_start_coords[1] + data.pool_size / 2f - inputDims.z / 2f) * neuronSpacing,
            (data.input_start_coords[0] + data.pool_size / 2f - inputDims.y / 2f) * neuronSpacing,
            (data.output_coord[0] - inputDims.x / 2f) * channelSpacing);
        poolRegionInstance.transform.localPosition = regionPos;
        var neuronToUpdate = neuronObjects[data.output_layer_name][data.output_coord[0], data.output_coord[1], data.output_coord[2]];
        UpdateNeuronGrayscale(neuronToUpdate, data.output_value, data.min_val, data.val_range);
        yield return new WaitForEndOfFrame();
    }

    private IEnumerator AnimateLayerUpdate(LayerUpdateData data)
    {
        Debug.Log($"Updating layer {data.layer_name} entirely (for ReLU)...");
        if (kernelInstance) kernelInstance.SetActive(false);
        if (poolRegionInstance) poolRegionInstance.SetActive(false);
        var activations = data.activations;
        string layerName = data.layer_name;
        if (!neuronObjects.ContainsKey(layerName)) yield break;
        for (int c = 0; c < activations.Count; c++)
        {
            for (int y = 0; y < activations[c].Count; y++)
            {
                for (int x = 0; x < activations[c][y].Count; x++)
                {
                    UpdateNeuronGrayscale(neuronObjects[layerName][c, y, x], activations[c][y][x], data.min_val, data.val_range);
                }
            }
        }
        yield return null;
    }
    #endregion

    #region Helper Methods
    private void UpdateNeuronGrayscale(GameObject neuron, float value, float min, float range)
    {
        var renderer = neuron.GetComponent<Renderer>();
        float normalizedValue = (value - min) / range;
        Color grayscale = Color.Lerp(Color.black, Color.white, normalizedValue);
        renderer.material.SetColor("_Color", grayscale);
        renderer.material.SetColor("_EmissionColor", grayscale * 0.8f);
    }

    public void UpdateNetworkLayout()
    {
        if (layerContainers.Count == 0) return;
        float zOffset = 0f;
        foreach (string layerName in orderedLayerNames)
        {
            if (!layerContainers.ContainsKey(layerName)) continue;
            GameObject layerContainer = layerContainers[layerName];
            layerContainer.transform.position = new Vector3(0, 0, zOffset);
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
                            (c - channels / 2f) * channelSpacing);
                        neuronObjects[layerName][c, y, x].transform.localPosition = position;
                    }
                }
            }
            float layerDepth = channels * channelSpacing;
            zOffset += layerSpacing + layerDepth;
        }
    }

    public void ToggleLayerVisibility(string layerName, bool isVisible)
    {
        if (layerContainers.ContainsKey(layerName))
        {
            layerContainers[layerName].SetActive(isVisible);
        }
    }
    
    public void TogglePause()
    {
        isPaused = !isPaused;
        Debug.Log(isPaused ? "Visualization Paused." : "Visualization Resumed.");
    }
    #endregion
}