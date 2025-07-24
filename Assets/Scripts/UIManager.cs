using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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

    [Header("Layer Info Panel UI")]
    public GameObject layerInfoItemPrefab;
    public Transform layerInfoContentPanel;

    void Start()
    {
        if (visualizer != null)
        {
            neuronSpacingSlider.value = visualizer.neuronSpacing;
            channelSpacingSlider.value = visualizer.channelSpacing;
            layerSpacingSlider.value = visualizer.layerSpacing;
        }
        if (flyCamera != null)
        {
            cameraSpeedSlider.value = flyCamera.baseSpeed; 
            lookSensitivitySlider.value = flyCamera.lookSensitivity;
        }
        UpdateAllValueTexts();
        neuronSpacingSlider.onValueChanged.AddListener(OnNeuronSpacingChanged);
        channelSpacingSlider.onValueChanged.AddListener(OnChannelSpacingChanged);
        layerSpacingSlider.onValueChanged.AddListener(OnLayerSpacingChanged);
        cameraSpeedSlider.onValueChanged.AddListener(OnCameraSpeedChanged);
        lookSensitivitySlider.onValueChanged.AddListener(OnLookSensitivityChanged);
    }

    public void OnNeuronSpacingChanged(float value)
    {
        if (visualizer != null) { visualizer.neuronSpacing = value; UpdateAllValueTexts(); }
    }
    public void OnChannelSpacingChanged(float value)
    {
        if (visualizer != null) { visualizer.channelSpacing = value; UpdateAllValueTexts(); }
    }
    public void OnLayerSpacingChanged(float value)
    {
        if (visualizer != null) { visualizer.layerSpacing = value; UpdateAllValueTexts(); }
    }
    public void OnCameraSpeedChanged(float value)
    {
        if (flyCamera != null) { flyCamera.baseSpeed = value; UpdateAllValueTexts(); }
    }
    public void OnLookSensitivityChanged(float value)
    {
        if (flyCamera != null) { flyCamera.lookSensitivity = value; UpdateAllValueTexts(); }
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
        neuronSpacingSlider.onValueChanged.RemoveAllListeners();
        channelSpacingSlider.onValueChanged.RemoveAllListeners();
        layerSpacingSlider.onValueChanged.RemoveAllListeners();
        cameraSpeedSlider.onValueChanged.RemoveAllListeners();
        lookSensitivitySlider.onValueChanged.RemoveAllListeners();
    }

    public void PopulateLayerInfoPanel(List<DetailedLayerInfo> topology)
    {
        foreach (Transform child in layerInfoContentPanel)
        {
            Destroy(child.gameObject);
        }

        foreach (var layerInfo in topology)
        {
            CreateLayerInfoItem(layerInfo.name, layerInfo.type, layerInfo.details);
        }
    }
    
    private void CreateLayerInfoItem(string layerName, string layerType, string layerDetails)
    {
        GameObject itemGO = Instantiate(layerInfoItemPrefab, layerInfoContentPanel);
        Toggle toggle = itemGO.GetComponentInChildren<Toggle>();
        TMP_Text infoText = itemGO.GetComponentInChildren<TMP_Text>();

        toggle.isOn = true;
        infoText.text = $"<b>[{layerName}] {layerType}</b>\n<size=18><i>  {layerDetails}</i></size>";

        toggle.onValueChanged.AddListener((isOn) => {
            if (visualizer != null)
            {
                visualizer.ToggleLayerVisibility(layerName, isOn);
            }
        });
    }
}