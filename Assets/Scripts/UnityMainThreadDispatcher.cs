// 文件名: UnityMainThreadDispatcher.cs (修正版)

using System;
using System.Collections.Generic;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance = null;
    
    // [修改] Instance()现在只负责返回已存在的实例，不再创建
    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            // 抛出一个明确的错误，而不是试图在错误的线程创建
            throw new Exception("UnityMainThreadDispatcher not initialized. Please ensure an instance of UnityMainThreadDispatcher is in your scene.");
        }
        return _instance;
    }

    // [新增] Awake()方法在对象加载时由Unity主线程自动调用
    void Awake()
    {
        // 实现单例模式
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject); // 确保它在切换场景时不会被销毁
        }
        else if (_instance != this)
        {
            // 如果场景中已经存在一个实例，就销毁这个新的
            Destroy(gameObject);
        }
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        // [修改] 增加一个检查，确保在非播放模式下（例如在编辑器里）调用时不会出错
        if (_executionQueue != null)
        {
            lock (_executionQueue)
            {
                _executionQueue.Enqueue(action);
            }
        }
    }
}