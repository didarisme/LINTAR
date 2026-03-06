using UnityEngine;

public class FreeFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float speedChangeStep = 2f;
    [SerializeField] private float minSpeed = 1f;
    [SerializeField] private float maxSpeed = 50f;

    [Header("Keys")]
    [SerializeField] private KeyCode moveUpKey = KeyCode.E;
    [SerializeField] private KeyCode moveDownKey = KeyCode.Q;
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private float yaw;
    private float pitch;

    private void Start()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
    }

    private void Update()
    {
        HandleMouseLook();
        HandleMovement();
        HandleSpeedChange();
        HandleReset();
    }

    private void HandleMouseLook()
    {
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            yaw += mouseX;
            pitch -= mouseY;

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    private void HandleMovement()
    {
        Vector3 move = Vector3.zero;

        move += transform.forward * Input.GetAxis("Vertical");
        move += transform.right * Input.GetAxis("Horizontal");

        if (Input.GetKey(moveUpKey))
            move += Vector3.up;

        if (Input.GetKey(moveDownKey))
            move += Vector3.down;

        transform.position += moveSpeed * Time.deltaTime * move;
    }

    private void HandleSpeedChange()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (scroll != 0)
        {
            moveSpeed += scroll * speedChangeStep * 10f;
            moveSpeed = Mathf.Clamp(moveSpeed, minSpeed, maxSpeed);
        }
    }

    private void HandleReset()
    {
        if (Input.GetKeyDown(resetKey))
        {
            transform.SetPositionAndRotation(startPosition, startRotation);

            yaw = startRotation.eulerAngles.y;
            pitch = startRotation.eulerAngles.x;
        }
    }

}