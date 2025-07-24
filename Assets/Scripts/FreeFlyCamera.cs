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

    void Start()
    {
        // currentSpeed = baseSpeed;
        
        Vector3 startEuler = transform.eulerAngles;
        rotationX = startEuler.y;
        rotationY = startEuler.x;
    }

    void Update()
    {

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        baseSpeed += scroll * speedChangeSensitivity;
        baseSpeed = Mathf.Max(0.1f, baseSpeed); 

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

        float finalSpeed = Input.GetKey(KeyCode.LeftShift) ? baseSpeed * shiftMultiplier : baseSpeed;

        float moveForward = Input.GetAxis("Vertical");
        float moveSideways = Input.GetAxis("Horizontal");
        float moveUp = 0f;
        if (Input.GetKey(KeyCode.E)) moveUp = 1f;
        if (Input.GetKey(KeyCode.Q)) moveUp = -1f;

        Vector3 moveDirection = new Vector3(moveSideways, moveUp, moveForward);
        moveDirection = transform.TransformDirection(moveDirection);

        transform.position += moveDirection * finalSpeed * Time.deltaTime;
    }
}