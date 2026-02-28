using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

public class FireSimLoader : MonoBehaviour
{
    [Header("File")]
    [SerializeField] private TextAsset fireFile;

    public event Action<FireSimConfig> OnDataLoaded;

    private void Awake()
    {
        LoadFile();
    }

    private void LoadFile()
    {
        var fireData = new Dictionary<int, List<Vector2Int>>();

        int minPx = 0, maxPx = 0, minPy = 0, maxPy = 0;
        double centerLon = 0, centerLat = 0;
        float patchWidthMeters = 0f, patchHeightMeters = 0f;
        float centerPx = 0f, centerPy = 0f;

        string[] lines = fireFile.text.Split('\n');

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

            if (parts.Length != 3)
                continue;

            int tick = int.Parse(parts[0]);
            int x = int.Parse(parts[1]);
            int y = int.Parse(parts[2]);

            if (!fireData.ContainsKey(tick))
                fireData[tick] = new List<Vector2Int>();

            fireData[tick].Add(new Vector2Int(x, y));
        }

        var config = new FireSimConfig
        {
            PatchWidthMeters = patchWidthMeters,
            PatchHeightMeters = patchHeightMeters,
            CenterPx = centerPx,
            CenterPy = centerPy,
            MapCenter = new double3(centerLon, centerLat, 600f),
            FireData = fireData
        };

        OnDataLoaded?.Invoke(config);
    }
}