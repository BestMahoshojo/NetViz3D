using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

public class PythonLauncher : MonoBehaviour
{
    [Header("Python Configuration")]
    [Tooltip("您的Conda环境中的python.exe的绝对路径。\n例如: C:/Users/YourUser/anaconda3/envs/dy311/python.exe")]
    public string pythonExecutablePath;

    [Tooltip("您的Python项目文件夹的绝对路径。\n例如: C:/Users/YourUser/my_project/model")]
    public string pythonScriptsPath;

    [Header("System References")]
    [Tooltip("拖拽场景中挂载了AdvancedVisualizer脚本的对象到这里")]
    public AdvancedVisualizer visualizer; 

    // 私有变量
    private Process pythonProcess;
    private string weightsFileName = "alexnet_cifar10.pth";
    private string trainingScriptName = "train_and_save_model.py";
    private string visualizerScriptName = "visualizer_director.py";

    /// <summary>
    /// UI按钮将调用的公共方法
    /// </summary>
    public async void StartVisualizationProcess()
    {
        UnityEngine.Debug.Log("启动流程开始...");

        // 在后台线程上运行耗时的操作，以避免冻结Unity
        await Task.Run(() => {
            string weightsPath = Path.Combine(pythonScriptsPath, "weights", weightsFileName);
            
            // 1. 检查权重文件是否存在
            if (!File.Exists(weightsPath))
            {
                UnityEngine.Debug.LogWarning($"权重文件未找到于: {weightsPath}。正在启动训练脚本...");
                
                // 运行训练脚本并等待其完成
                RunPythonScript(trainingScriptName, true);
                
                UnityEngine.Debug.Log("训练脚本执行完毕。");
            }
            else
            {
                UnityEngine.Debug.Log($"权重文件已找到于: {weightsPath}。");
            }
            
            // 2. 启动可视化脚本
            UnityEngine.Debug.Log("正在启动可视化脚本...");
            RunPythonScript(visualizerScriptName, false);
        });

        // 在启动Python脚本后，稍等片刻，然后命令Visualizer开始连接
        // 等待1秒，给Python服务器足够的时间来启动和监听端口
        await Task.Delay(1000); 
        
        if (visualizer != null)
        {
            UnityEngine.Debug.Log("命令 Visualizer 开始连接服务器...");
            visualizer.StartConnecting();
        }
        else
        {
            UnityEngine.Debug.LogError("PythonLauncher中的Visualizer引用未设置！请在Inspector中拖拽赋值。");
        }
    }

    private void RunPythonScript(string scriptName, bool waitForExit)
    {
        // 检查路径是否为空
        if (string.IsNullOrEmpty(pythonExecutablePath) || string.IsNullOrEmpty(pythonScriptsPath))
        {
            UnityEngine.Debug.LogError("Python可执行文件路径或脚本路径未在Inspector中设置！");
            return;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutablePath,
            Arguments = Path.Combine(pythonScriptsPath, scriptName),
            WorkingDirectory = pythonScriptsPath, 
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Process process = new Process { StartInfo = startInfo };

        // 捕获并打印Python脚本的输出，便于调试
        process.OutputDataReceived += (sender, args) => { if(args.Data != null) UnityEngine.Debug.Log($"[Python Out]: {args.Data}"); };
        process.ErrorDataReceived += (sender, args) => { if(args.Data != null) UnityEngine.Debug.LogError($"[Python Error]: {args.Data}"); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"启动Python进程失败: {e.Message}\n请检查Python可执行文件路径是否正确: '{pythonExecutablePath}'");
            return;
        }

        if (waitForExit)
        {
            process.WaitForExit(); // 阻塞，直到这个进程结束
        }
        else
        {
            pythonProcess = process; // 保存可视化进程的引用，以便后续可以关闭它
        }
    }

    private void KillPythonProcess()
    {
        if (pythonProcess != null && !pythonProcess.HasExited)
        {
            UnityEngine.Debug.Log("正在准备关闭Python子进程...");

            // 1. 停止监听输出，防止死锁
            if (pythonProcess.StartInfo.RedirectStandardOutput)
            {
                pythonProcess.CancelOutputRead();
            }
            if (pythonProcess.StartInfo.RedirectStandardError)
            {
                pythonProcess.CancelErrorRead();
            }

            // 2. 强行终止进程
            UnityEngine.Debug.Log($"正在发送Kill信号到进程 ID: {pythonProcess.Id}...");
            pythonProcess.Kill();

            // 3. 等待一小段时间确保进程已退出
            if (!pythonProcess.WaitForExit(500))
            {
                UnityEngine.Debug.LogWarning("Python进程在Kill后500ms内未能退出。");
            }
            else
            {
                 UnityEngine.Debug.Log("Python进程已成功退出。");
            }

            // 4. 释放资源
            pythonProcess.Close();
            pythonProcess = null;
        }
    }

    void OnApplicationQuit()
    {
        KillPythonProcess();
    }
}   