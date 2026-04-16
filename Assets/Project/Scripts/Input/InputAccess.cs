using UnityEngine;

public class InputAccess : MonoBehaviour
{
    public static InputAccess Instance { get; private set; }
    
    public PlayerInputActions Input { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Input = new PlayerInputActions();
    }

    private void OnEnable()
    {
        Input.Enable();
    }

    private void OnDisable()
    {
        Input?.Disable();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}