using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

public class FireSimLoader : MonoBehaviour
{
    [Header("File")]
    [SerializeField] private string fileName = "fire_run.txt";

    [Header("Cesium")]
    [SerializeField] private CesiumGeoreference geoRef;

    [Header("Rendering")]
    [SerializeField] private Mesh cubeMesh;
    [SerializeField] private Material instancedMaterial;
    [SerializeField] private float cubeSize = 8f;
    [SerializeField] private float tickDuration = 0.05f;
    [SerializeField] private float raycastHeight = 500f;

    private Dictionary<int, List<Vector2Int>> fireData = new();
    private List<Matrix4x4> matrices = new();

    private int minPx, maxPx, minPy, maxPy;
    private float patchWidthMeters, patchHeightMeters;
    private float centerPx, centerPy;
    private double centerLon, centerLat;

    private GameObject fireAnchorObject;

    private void Awake()
    {
        LoadFile();
        CreateAnchor();
    }

    public void StartSimulation()
    {
        StartCoroutine(PlayFire());
    }

    private void Update()
    {
        RenderInstances();
    }

    private void LoadFile()
    {
        string path = Path.Combine(Application.dataPath, fileName);
        string[] lines = File.ReadAllLines(path);

        foreach (string line in lines)
        {
            if (line.StartsWith("WORLD"))
            {
                string[] p = line.Split(' ');

                minPx = int.Parse(p[1]);
                maxPx = int.Parse(p[2]);
                minPy = int.Parse(p[3]);
                maxPy = int.Parse(p[4]);

                centerLon = double.Parse(p[5], CultureInfo.InvariantCulture);
                centerLat = double.Parse(p[6], CultureInfo.InvariantCulture);

                patchWidthMeters = float.Parse(p[7], CultureInfo.InvariantCulture);
                patchHeightMeters = float.Parse(p[8], CultureInfo.InvariantCulture);

                centerPx = (minPx + maxPx) / 2f;
                centerPy = (minPy + maxPy) / 2f;
                continue;
            }

            string clean = line.Replace("[", "").Replace("]", "");
            string[] parts = clean.Split(' ');

            if (parts.Length != 3) continue;

            int tick = int.Parse(parts[0]);
            int x = int.Parse(parts[1]);
            int y = int.Parse(parts[2]);

            if (!fireData.ContainsKey(tick))
                fireData[tick] = new List<Vector2Int>();

            fireData[tick].Add(new Vector2Int(x, y));
        }
    }

    private void CreateAnchor()
    {
        if (!geoRef)
            geoRef = FindObjectOfType<CesiumGeoreference>();

        fireAnchorObject = new GameObject("FireAnchor");
        fireAnchorObject.transform.SetParent(geoRef.transform, false);

        var anchor = fireAnchorObject.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = new double3(centerLon, centerLat, 600f);
    }

    private IEnumerator PlayFire()
    {
        int maxTick = 0;
        foreach (var t in fireData.Keys)
            if (t > maxTick) maxTick = t;

        for (int tick = 0; tick <= maxTick; tick++)
        {
            if (fireData.ContainsKey(tick))
            {
                foreach (var pos in fireData[tick])
                    AddInstance(pos.x, pos.y);
            }

            yield return new WaitForSeconds(tickDuration);
        }
    }

    private void AddInstance(int px, int py)
    {
        float localX = (px - centerPx) * patchWidthMeters;
        float localZ = (py - centerPy) * patchHeightMeters;

        Vector3 worldPos = fireAnchorObject.transform.TransformPoint(
            new Vector3(localX, raycastHeight, localZ)
        );

        if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, raycastHeight * 2))
        {
            Vector3 finalPos = hit.point;

            Matrix4x4 matrix = Matrix4x4.TRS(
                finalPos,
                Quaternion.identity,
                Vector3.one * cubeSize
            );

            matrices.Add(matrix);
        }
    }

    private void RenderInstances()
    {
        if (cubeMesh == null || instancedMaterial == null) return;

        const int batchSize = 1023;

        for (int i = 0; i < matrices.Count; i += batchSize)
        {
            int count = Mathf.Min(batchSize, matrices.Count - i);
            Graphics.DrawMeshInstanced(
                cubeMesh,
                0,
                instancedMaterial,
                matrices.GetRange(i, count)
            );
        }
    }
}