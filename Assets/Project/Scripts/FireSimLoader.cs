using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

public class FireSimLoader : MonoBehaviour
{
    [Header("File Settings")]
    [SerializeField] private string fileName = "fire_run.txt";

    [Header("Cesium")]
    [SerializeField] private CesiumGeoreference geoRef;

    [Header("Visual Settings")]
    [SerializeField] private float cubeSize = 10f;
    [SerializeField] private float tickDuration = 0.05f;

    private Dictionary<int, List<Vector2Int>> fireData = new Dictionary<int, List<Vector2Int>>();
    private GameObject fireParent;

    // WORLD data
    private int minPx, maxPx, minPy, maxPy;
    private float patchWidthMeters;
    private float patchHeightMeters;
    private float centerPx;
    private float centerPy;

    private double centerLon;
    private double centerLat;

    private void Awake()
    {
        LoadFile();
        CreateFireParent();
    }

    public void StartSimulation()
    {
        StartCoroutine(PlayFire());
    }

    private void LoadFile()
    {
        string path = Path.Combine(Application.dataPath, fileName);

        if (!File.Exists(path))
        {
            Debug.LogError("File not found: " + path);
            return;
        }

        string[] lines = File.ReadAllLines(path);

        foreach (string line in lines)
        {
            if (line.StartsWith("WORLD"))
            {
                string[] parts = line.Split(' ');

                minPx = int.Parse(parts[1]);
                maxPx = int.Parse(parts[2]);
                minPy = int.Parse(parts[3]);
                maxPy = int.Parse(parts[4]);

                centerLon = double.Parse(parts[5], CultureInfo.InvariantCulture);
                centerLat = double.Parse(parts[6], CultureInfo.InvariantCulture);

                patchWidthMeters = float.Parse(parts[7], CultureInfo.InvariantCulture);
                patchHeightMeters = float.Parse(parts[8], CultureInfo.InvariantCulture);

                centerPx = (minPx + maxPx) / 2f;
                centerPy = (minPy + maxPy) / 2f;

                continue;
            }

            string clean = line.Replace("[", "").Replace("]", "");
            string[] partsLine = clean.Split(' ');

            if (partsLine.Length != 3)
                continue;

            int tick = int.Parse(partsLine[0]);
            int x = int.Parse(partsLine[1]);
            int y = int.Parse(partsLine[2]);

            if (!fireData.ContainsKey(tick))
                fireData[tick] = new List<Vector2Int>();

            fireData[tick].Add(new Vector2Int(x, y));
        }

        Debug.Log("Fire data loaded.");

        float totalWidth = (maxPx - minPx) * patchWidthMeters;
        float totalHeight = (maxPy - minPy) * patchHeightMeters;

        Debug.Log("World width meters: " + totalWidth);
        Debug.Log("World height meters: " + totalHeight);
    }

    private void CreateFireParent()
    {
        if (geoRef == null)
        {
            geoRef = FindObjectOfType<CesiumGeoreference>();
        }

        fireParent = new GameObject("FireHandler");
        fireParent.transform.SetParent(geoRef.transform, false);

        var anchor = fireParent.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = new double3(centerLon, centerLat, 1000f);

        Debug.Log($"FireHandler centered at Lon:{centerLon}, Lat:{centerLat}");
    }

    private IEnumerator PlayFire()
    {
        int maxTick = 0;

        foreach (int tick in fireData.Keys)
        {
            if (tick > maxTick)
                maxTick = tick;
        }

        for (int currentTick = 0; currentTick <= maxTick; currentTick++)
        {
            if (fireData.ContainsKey(currentTick))
            {
                foreach (Vector2Int pos in fireData[currentTick])
                {
                    CreateCube(pos.x, pos.y);
                }
            }

            yield return new WaitForSeconds(tickDuration);
        }

        Debug.Log("Playback finished.");
    }

    private void CreateCube(int px, int py)
    {
        float localX = (px - centerPx) * patchWidthMeters;
        float localZ = (py - centerPy) * patchHeightMeters;

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

        cube.transform.SetParent(fireParent.transform, false);
        cube.transform.localPosition = new Vector3(localX, 0, localZ);
        cube.transform.localScale = Vector3.one * cubeSize;
    }
}