using System.Collections.Generic;
using UnityEngine;

public class BurntZoneRenderer : MonoBehaviour
{
    [SerializeField] private FireSimulation FireSimulationInstance;

    [SerializeField] private Mesh quadMesh;
    [SerializeField] private Material burntMaterial;
    [SerializeField] private float meshSize = 25f;
    [SerializeField] private float yOffset = 0.1f;

    private const int batchSize = 1023;

    private bool isVisible = false;

    public float YOffset => yOffset;
    public float MeshSize => meshSize;

    public void SetVisible(bool visible)
    {
        isVisible = visible;
    }

    private void Update()
    {
        if (!isVisible) return;

        if (FireSimulationInstance == null) return;

        var instances = FireSimulationInstance.FireInstances;
        if (instances == null || instances.Count == 0) return;

        Render(instances);
    }

    private void Render(List<FireSimulation.FireInstance> instances)
    {
        int count = instances.Count;

        List<Matrix4x4> batch = new();

        for (int i = 0; i < count; i++)
        {
            batch.Add(Matrix4x4.TRS(
                instances[i].position + Vector3.up * yOffset,
                Quaternion.identity,
                Vector3.one * meshSize
            ));

            if (batch.Count == batchSize)
            {
                Graphics.DrawMeshInstanced(quadMesh, 0, burntMaterial, batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            Graphics.DrawMeshInstanced(quadMesh, 0, burntMaterial, batch);
        }
    }

    public void SetSize(float newSize)
    {
        meshSize = Mathf.Clamp(newSize, 1f, 200f);
    }

    public void SetYOffset(float offset)
    {
        yOffset = offset;
    }
}