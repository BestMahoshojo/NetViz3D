using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Script References")]
    public AdvancedVisualizer visualizer;
    public FreeFlyCamera flyCamera;

    [Header("Visualization UI")]
    public Slider neuronSpacingSlider;
    public Slider channelSpacingSlider;
    public Slider layerSpacingSlider;
    public TMP_Text neuronSpacingValueText;
    public TMP_Text channelSpacingValueText;
    public TMP_Text layerSpacingValueText;

    [Header("Camera UI")]
    public Slider cameraSpeedSlider; 
    public Slider lookSensitivitySlider;
    public TMP_Text cameraSpeedValueText; 
    public TMP_Text lookSensitivityValueText;

    void Start()
    {
        // --- 初始化UI控件的初始值 ---

        if (visualizer != null)
        {
            neuronSpacingSlider.value = visualizer.neuronSpacing;
            channelSpacingSlider.value = visualizer.channelSpacing;
            layerSpacingSlider.value = visualizer.layerSpacing;
        }

        if (flyCamera != null)
        {
            // [修改] 初始化相机速度滑条
            cameraSpeedSlider.value = flyCamera.baseSpeed; 
            lookSensitivitySlider.value = flyCamera.lookSensitivity;
        }
        
        UpdateAllValueTexts(); // 一次性更新所有文本显示

        // --- 为UI控件添加监听器 ---

        neuronSpacingSlider.onValueChanged.AddListener(OnNeuronSpacingChanged);
        channelSpacingSlider.onValueChanged.AddListener(OnChannelSpacingChanged);
        layerSpacingSlider.onValueChanged.AddListener(OnLayerSpacingChanged);

        // [修改] 为相机速度滑条添加监听器
        cameraSpeedSlider.onValueChanged.AddListener(OnCameraSpeedChanged);
        lookSensitivitySlider.onValueChanged.AddListener(OnLookSensitivityChanged);
    }

    // --- Visualization Panel的回调函数 ---
    public void OnNeuronSpacingChanged(float value)
    {
        if (visualizer != null)
        {
            visualizer.neuronSpacing = value;
            UpdateAllValueTexts();
        }
    }

    public void OnChannelSpacingChanged(float value)
    {
        if (visualizer != null)
        {
            visualizer.channelSpacing = value;
            UpdateAllValueTexts();
        }
    }

    public void OnLayerSpacingChanged(float value)
    {
        if (visualizer != null)
        {
            visualizer.layerSpacing = value;
            UpdateAllValueTexts();
        }
    }

    public void OnCameraSpeedChanged(float value)
    {
        if (flyCamera != null)
        {
            flyCamera.baseSpeed = value;
            UpdateAllValueTexts();
        }
    }

    public void OnLookSensitivityChanged(float value)
    {
        if (flyCamera != null)
        {
            flyCamera.lookSensitivity = value;
            UpdateAllValueTexts();
        }
    }

    private void UpdateAllValueTexts()
    {
        if (neuronSpacingValueText != null) neuronSpacingValueText.text = neuronSpacingSlider.value.ToString("F2");
        if (channelSpacingValueText != null) channelSpacingValueText.text = channelSpacingSlider.value.ToString("F2");
        if (layerSpacingValueText != null) layerSpacingValueText.text = layerSpacingSlider.value.ToString("F2");
        if (cameraSpeedValueText != null) cameraSpeedValueText.text = cameraSpeedSlider.value.ToString("F2");
        if (lookSensitivityValueText != null) lookSensitivityValueText.text = lookSensitivitySlider.value.ToString("F2");
    }

    void OnDestroy()
    {
        // 确保移除所有监听器
        neuronSpacingSlider.onValueChanged.RemoveAllListeners();
        channelSpacingSlider.onValueChanged.RemoveAllListeners();
        layerSpacingSlider.onValueChanged.RemoveAllListeners();
        cameraSpeedSlider.onValueChanged.RemoveAllListeners();
        lookSensitivitySlider.onValueChanged.RemoveAllListeners();
    }
}