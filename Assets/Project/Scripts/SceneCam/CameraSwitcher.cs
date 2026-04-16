using TMPro;
using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI cameraText;
    [SerializeField] private ViewCamera[] cameras;

    private PlayerInputActions input;
    private FreeFlyCamera freeFlyCamera;
    private Camera activeCamera;

    private int currentIndex;

    private void Start()
    {
        input = InputAccess.Instance.Input;

        ActivateCamera(0);
    }

    private void Update()
    {
        if (input.Gameplay.NextCamera.WasPressedThisFrame())
            NextCamera();

        if (input.Gameplay.PrevCamera.WasPressedThisFrame())
            PreviousCamera();
    }

    private void NextCamera()
    {
        currentIndex = (currentIndex + 1) % cameras.Length;
        ActivateCamera(currentIndex);
    }

    private void PreviousCamera()
    {
        currentIndex = (currentIndex - 1 + cameras.Length) % cameras.Length;
        ActivateCamera(currentIndex);
    }

    private void ActivateCamera(int index)
    {
        if (activeCamera != null)
            activeCamera.enabled = false;

        activeCamera = cameras[index].viewCamera;
        activeCamera.enabled = true;

        if (freeFlyCamera != null)
        {
            freeFlyCamera.SetCameraActive(false);
            freeFlyCamera = null;
        }

        if (activeCamera.TryGetComponent(out FreeFlyCamera fly))
        {
            freeFlyCamera = fly;
            freeFlyCamera.SetCameraActive(true);
        }

        cameraText.text = cameras[index].cameraName;
    }

    [System.Serializable]
    private class ViewCamera
    {
        public string cameraName;
        public Camera viewCamera;
        public bool isFly;
    }
}