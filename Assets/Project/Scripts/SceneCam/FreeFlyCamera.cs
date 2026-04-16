using UnityEngine;
using UnityEngine.InputSystem;

public class FreeFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float boostMultiplier = 4f;
    [SerializeField] private float acceleration = 10f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseXSens = 1.5f;
    [SerializeField] private float mouseYSens = 0.4f;
    [SerializeField] private float smoothSpeed = 12f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private PlayerInputActions input;
    private InputAction lookHoldAction;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private float yaw;
    private float pitch;

    private Vector2 smoothLook;
    private Vector3 velocity;

    private bool isActive;

    private void Awake()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
    }

    private void Update()
    {
        if (!isActive) return;

        HandleLook();
        HandleMovement();
        HandleSpeedScroll();
        HandleReset();
    }

    public void SetCameraActive(bool value)
    {
        if (isActive == value)
            return;

        isActive = value;

        if (value)
            Activate();
        else
            Deactivate();
    }

    private void Activate()
    {
        input = InputAccess.Instance.Input;
        lookHoldAction = input.Gameplay.LookHold;

        lookHoldAction.performed += OnLookStarted;
        lookHoldAction.canceled += OnLookCanceled;
    }

    private void Deactivate()
    {
        if (lookHoldAction != null)
        {
            lookHoldAction.performed -= OnLookStarted;
            lookHoldAction.canceled -= OnLookCanceled;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        smoothLook = Vector2.zero;
        velocity = Vector3.zero;
    }

    private void HandleLook()
    {
        if (lookHoldAction == null || !lookHoldAction.IsPressed())
            return;

        Vector2 rawLook = input.Gameplay.Look.ReadValue<Vector2>();

        smoothLook = Vector2.Lerp(
            smoothLook,
            rawLook,
            smoothSpeed * Time.deltaTime
        );

        yaw += smoothLook.x * mouseXSens;
        pitch -= smoothLook.y * mouseYSens;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void OnLookStarted(InputAction.CallbackContext ctx)
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnLookCanceled(InputAction.CallbackContext ctx)
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        smoothLook = Vector2.zero;
    }

    private void HandleMovement()
    {
        Vector2 moveInput = input.Gameplay.Move.ReadValue<Vector2>();

        Vector3 direction =
            transform.forward * moveInput.y +
            transform.right * moveInput.x;

        if (input.Gameplay.MoveUp.IsPressed())
            direction += Vector3.up;

        if (input.Gameplay.MoveDown.IsPressed())
            direction += Vector3.down;

        float speed = moveSpeed;

        if (Keyboard.current.leftShiftKey.isPressed)
            speed *= boostMultiplier;

        Vector3 targetVelocity = direction * speed;

        velocity = Vector3.Lerp(
            velocity,
            targetVelocity,
            acceleration * Time.deltaTime
        );

        transform.position += velocity * Time.deltaTime;
    }

    private void HandleSpeedScroll()
    {
        float scroll = input.Gameplay.Speed.ReadValue<float>();

        if (Mathf.Abs(scroll) > 0.01f)
        {
            moveSpeed += scroll * 2f;
            moveSpeed = Mathf.Clamp(moveSpeed, 1f, 100f);
        }
    }

    private void HandleReset()
    {
        if (input.Gameplay.Reset.WasPressedThisFrame())
        {
            transform.SetPositionAndRotation(startPosition, startRotation);

            yaw = startRotation.eulerAngles.y;
            pitch = startRotation.eulerAngles.x;

            velocity = Vector3.zero;
        }
    }
}