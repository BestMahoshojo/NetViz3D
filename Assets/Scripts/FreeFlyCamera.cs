using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FreeFlyCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("基础移动速度")]
    public float baseSpeed = 5.0f;

    [Tooltip("按住Shift键时的加速倍率")]
    public float shiftMultiplier = 2.5f;

    [Tooltip("鼠标滚轮调节速度的灵敏度")]
    public float speedChangeSensitivity = 1.0f;

    [Header("Rotation Settings")]
    [Tooltip("鼠标旋转的灵敏度")]
    public float lookSensitivity = 3.0f;

    private float rotationX = 0.0f;
    private float rotationY = 0.0f;
    // [删除] 我们不再需要这个中间变量
    // private float currentSpeed; 

    void Start()
    {
        // [删除] 初始化currentSpeed的这行代码也不再需要
        // currentSpeed = baseSpeed;
        
        Vector3 startEuler = transform.eulerAngles;
        rotationX = startEuler.y;
        rotationY = startEuler.x;
    }

    void Update()
    {
        // --- 速度调节 ---
        // [修改] 鼠标滚轮现在直接修改 baseSpeed
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        baseSpeed += scroll * speedChangeSensitivity;
        baseSpeed = Mathf.Max(0.1f, baseSpeed); // 确保速度不会变为负数或零

        // --- 旋转控制 ---
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
            rotationY -= Input.GetAxis("Mouse Y") * lookSensitivity;
            rotationY = Mathf.Clamp(rotationY, -90f, 90f);

            transform.localRotation = Quaternion.Euler(rotationY, rotationX, 0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // --- 移动控制 ---
        // [修改] finalSpeed现在直接从baseSpeed计算
        float finalSpeed = Input.GetKey(KeyCode.LeftShift) ? baseSpeed * shiftMultiplier : baseSpeed;

        // 获取键盘输入
        float moveForward = Input.GetAxis("Vertical");
        float moveSideways = Input.GetAxis("Horizontal");
        float moveUp = 0f;
        if (Input.GetKey(KeyCode.E)) moveUp = 1f;
        if (Input.GetKey(KeyCode.Q)) moveUp = -1f;

        // 计算移动向量
        Vector3 moveDirection = new Vector3(moveSideways, moveUp, moveForward);
        moveDirection = transform.TransformDirection(moveDirection);

        // 应用移动
        transform.position += moveDirection * finalSpeed * Time.deltaTime;
    }
}