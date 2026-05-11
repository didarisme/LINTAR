using System.Collections.Generic;
using UnityEngine;

public class SimulationMemoryManager : MonoBehaviour
{
    public static SimulationMemoryManager Instance { get; private set; }

    private readonly Dictionary<string, string> _simulationMemory = new Dictionary<string, string>();

    private void Awake()
    {
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public void StoreSimulation(string fileName, string content)
    {
        _simulationMemory[fileName] = content;
    }

    public string GetSimulationContent(string fileName)
    {
        return _simulationMemory.TryGetValue(fileName, out string content)? content : null;
    }

    public void RemoveSimulation(string fileName)
    {
        _simulationMemory.Remove(fileName);
    }

    public IEnumerable<string> GetAllSimulationNames()
    {
        return _simulationMemory.Keys;
    }
}