using UnityEngine;
using UnityEngine.SceneManagement; // 必须引入这个命名空间来管理场景

public class MainMenuManager : MonoBehaviour
{
    // 这个函数将被“神经网络可视化”按钮调用
    public void LoadNeuralNetworkScene()
    {
        Debug.Log("Loading Neural Network Scene...");
        // "NeuralNetworkScene" 是您保存神经网络可视化的场景文件名
        // 请确保文件名完全匹配！
        SceneManager.LoadScene("NeuralNetworkScene"); 
    }

    // 这个函数将被“排序算法可视化”按钮调用
    public void LoadSortingAlgorithmScene()
    {
        Debug.Log("Loading Sorting Algorithm Scene...");
        // "SortingAlgorithmScene" 是您导入的排序算法场景文件名
        SceneManager.LoadScene("SortingAlgorithmScene");
    }
}