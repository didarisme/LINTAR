using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class FireSimLoader : MonoBehaviour
{
    [SerializeField] private string fileName = "fire_run.txt";
    [SerializeField] private float cubeSize = 1f;
    [SerializeField] private float tickDuration = 0.05f;

    private Dictionary<int, List<Vector2Int>> fireData = new Dictionary<int, List<Vector2Int>>();
    private GameObject fireParent;

    private void Awake()
    {
        LoadFile();
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
                continue;

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

        fireParent = new GameObject("FireCubes");
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

    private void CreateCube(int x, int y)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(x, 0, y);
        cube.transform.localScale = Vector3.one * cubeSize;
        cube.transform.SetParent(fireParent.transform);
    }
}