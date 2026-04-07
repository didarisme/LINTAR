using UnityEngine;

namespace LemiGame
{
    public class FireSpreadTest : MonoBehaviour
    {
        public FireSpreadController fireSpreadController;
        public Camera targetCamera;
        public Material targetMaterial;
        public bool enableClickToFire = true;
        [Tooltip("Radius for extinguishing fire on right click (world units)")]
        public float extinguishRadius = 5f;
        
        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }
        
        private void Update()
        {
            if (enableClickToFire)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;
                    
                    if (Physics.Raycast(ray, out hit))
                    {
                        fireSpreadController.StartFireAt(hit.point);
                    }
                }
            }

            // Right click to extinguish fire
            if (Input.GetMouseButtonDown(1))
            {
                Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                
                if (Physics.Raycast(ray, out hit, Mathf.Infinity))
                {
                    Debug.Log($"Extinguishing fire at position: {hit.point}, radius: {extinguishRadius}");
                    fireSpreadController.ExtinguishAt(hit.point, extinguishRadius);
                }
                else
                {
                    Debug.LogWarning("Right click did not hit any object. Make sure there's a collider in the scene.");
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                SetObjectOnFire();
            }
            if (Input.GetKeyUp(KeyCode.Space))
            {
                SetObjectOffFire();
            }
        }

        public void SetObjectOnFire()
        {
            targetMaterial.SetColor("_BaseColor", Color.red);
        }
        
        public void SetObjectOffFire()
        {
            targetMaterial.SetColor("_BaseColor", Color.white);
        }
    }
}

