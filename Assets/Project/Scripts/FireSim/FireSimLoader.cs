using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;
using UnityEngine;

public class FireSimLoader : MonoBehaviour
{
    [Header("File")]
    [SerializeField] private FireScenarioSelectorSO fireScenario;

    public event Action<FireSimConfig> OnDataLoaded;

    private void Start()
    {
        if (fireScenario == null || string.IsNullOrEmpty(fireScenario.SelectedScenarioPath))
        {
            Debug.LogError("[FireSimLoader] Fire scenario not selected!");
            return;
        }

        LoadFile();
    }

    private void LoadFile()
    {
        string fileName = fireScenario.SelectedScenarioPath;
        string content = SimulationMemoryManager.Instance.GetSimulationContent(fileName);

#if !UNITY_WEBGL || UNITY_EDITOR
        if (content == null)
            content = TryLoadFromDisk(fileName);
#endif

        if (content == null)
        {
            Debug.LogError($"[FireSimLoader] Simulation '{fileName}' not found in memory or on disk.");
            return;
        }

        ParseContent(content);
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private string TryLoadFromDisk(string fileName)
    {
        string[] candidates =
        {
            // Persistent data)
            System.IO.Path.Combine(Application.persistentDataPath, fileName),

            // StreamingAssets
            System.IO.Path.Combine(Application.streamingAssetsPath, "FireScenarios", fileName),

            // На случай, если в SO вдруг записан полный абсолютный путь
            fileName
        };

        foreach (string path in candidates)
        {
            if (!System.IO.File.Exists(path))
                continue;

            try
            {
                string content = System.IO.File.ReadAllText(path);
                SimulationMemoryManager.Instance.StoreSimulation(fileName, content);
                Debug.Log($"[FireSimLoader] Loaded '{fileName}' from disk: {path}");
                return content;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FireSimLoader] Failed to read '{path}': {ex.Message}");
            }
        }

        return null;
    }
#endif

    private void ParseContent(string content)
    {
        var fireData = new Dictionary<int, List<Vector2Int>>();

        int minPx = 0, maxPx = 0, minPy = 0, maxPy = 0;
        double centerLon = 0, centerLat = 0;
        float patchWidthMeters = 0f, patchHeightMeters = 0f;
        float centerPx = 0f, centerPy = 0f;
        int maxTick = 0;

        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("WORLD"))
            {
                string[] p = line.Split(' ');

                if (p.Length < 9)
                {
                    Debug.LogWarning($"[FireSimLoader] Malformed WORLD line: {line}");
                    continue;
                }

                minPx = int.Parse(p[1]);
                maxPx = int.Parse(p[2]);
                minPy = int.Parse(p[3]);
                maxPy = int.Parse(p[4]);

                centerLon = double.Parse(p[5], CultureInfo.InvariantCulture);
                centerLat = double.Parse(p[6], CultureInfo.InvariantCulture);

                patchWidthMeters  = float.Parse(p[7], CultureInfo.InvariantCulture);
                patchHeightMeters = float.Parse(p[8], CultureInfo.InvariantCulture);

                centerPx = (minPx + maxPx) / 2f;
                centerPy = (minPy + maxPy) / 2f;

                continue;
            }

            string clean = line.Replace("[", "").Replace("]", "");
            string[] parts = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 3)
                continue;

            if (!int.TryParse(parts[0], out int tick)  ||
                !int.TryParse(parts[1], out int x)     ||
                !int.TryParse(parts[2], out int y))
            {
                Debug.LogWarning($"[FireSimLoader] Skipping unparseable line: {line}");
                continue;
            }

            if (tick > maxTick)
                maxTick = tick;

            if (!fireData.ContainsKey(tick))
                fireData[tick] = new List<Vector2Int>();

            fireData[tick].Add(new Vector2Int(x, y));
        }

        var config = new FireSimConfig
        {
            PatchWidthMeters  = patchWidthMeters,
            PatchHeightMeters = patchHeightMeters,
            CenterPx          = centerPx,
            CenterPy          = centerPy,
            MapCenter         = new double3(centerLon, centerLat, 600f),
            FireData          = fireData,
            MaxTick           = maxTick
        };

        Debug.Log($"[FireSimLoader] Parsed OK — ticks: {maxTick}, cells: {fireData.Count}");
        OnDataLoaded?.Invoke(config);
    }
}