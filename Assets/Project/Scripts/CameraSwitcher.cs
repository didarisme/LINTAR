using TMPro;
using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    [SerializeField] private ViewCamera[] cameras;
    [SerializeField] private TextMeshProUGUI cameraText;

    private Camera activeCamera;
    private int currentIndex = 0;

    private void Start()
    {
        ActivateCamera(0);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            NextCamera();
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            PreviousCamera();
        }
    }

    public void NextCamera()
    {
        currentIndex = (currentIndex + 1) % cameras.Length;
        ActivateCamera(currentIndex);
    }

    public void PreviousCamera()
    {
        currentIndex = (currentIndex - 1 + cameras.Length) % cameras.Length;
        ActivateCamera(currentIndex);
    }

    public void ActivateCamera(int cameraIndex)
    {
        if (activeCamera != null)
            activeCamera.enabled = false;

        activeCamera = cameras[cameraIndex].viewCamera;
        activeCamera.enabled = true;

        cameraText.text = cameras[cameraIndex].cameraName;
    }

    [System.Serializable]
    private class ViewCamera
    {
        public string cameraName;
        public Camera viewCamera;
    }
}
