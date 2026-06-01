using TMPro;
using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI cameraText;
    [SerializeField] private ViewCamera[] cameras;

    private Camera activeCamera;

    private int currentIndex;

    private void Start()
    {
        ActivateCamera(0);
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

    private void ActivateCamera(int index)
    {
        if (activeCamera != null)
            activeCamera.enabled = false;

        activeCamera = cameras[index].viewCamera;
        activeCamera.enabled = true;

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