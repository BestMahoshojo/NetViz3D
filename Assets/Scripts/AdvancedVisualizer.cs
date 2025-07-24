using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

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
        ConnectToServer();
        lastNeuronSpacing = neuronSpacing;
        lastChannelSpacing = channelSpacing;
        lastLayerSpacing = layerSpacing;
    }

    void Update()
    {
        while (messageQueue.Count > 0)
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
    private void ConnectToServer()
    {
        clientReceiveThread = new Thread(() =>
        {
            try
            {
                client = new TcpClient(serverHost, serverPort);
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
                //使用新的数据结构并通知UI Manager
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
        }
    }
    #endregion

    #region Coroutine Animations
    //CreateDetailedArchitecture现在接收新的数据结构
    private IEnumerator CreateDetailedArchitecture(List<DetailedLayerInfo> topology)
    {
        Debug.Log("Creating detailed architecture...");
        orderedLayerNames.Clear();
        var inputLayerInfo = new DetailedLayerInfo { name = "input", type = "Input", output_shape = new List<int> { 1, 3, 32, 32 }, details = "32x32 RGB Image" };
        topology.Insert(0, inputLayerInfo);

        foreach (var layerInfo in topology)
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
    #endregion
}