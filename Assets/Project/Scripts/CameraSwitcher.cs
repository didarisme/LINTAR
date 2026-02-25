using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    [SerializeField] private ViewCamera[] cameras;

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
        currentIndex--;

        if (currentIndex < 0)
            currentIndex = cameras.Length - 1;

        ActivateCamera(currentIndex);
    }

    public void ActivateCamera(int cameraIndex)
    {
        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i].camera.enabled = i == cameraIndex;
        }
    }

    [System.Serializable]
    private class ViewCamera
    {
        public string cameraName;
        public Camera camera;
    }
}
