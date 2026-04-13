using UnityEngine;

namespace LemiGame
{
    [ExecuteInEditMode]
    public class BillboardObject : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        private void LateUpdate()
        {
            if (targetCamera != null)
            {
                transform.LookAt(targetCamera.transform);
            }
        }
    }
}