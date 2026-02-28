using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class FireSimConfig
{
    public float PatchWidthMeters;
    public float PatchHeightMeters;
    public float CenterPx;
    public float CenterPy;

    public double3 MapCenter;

    public Dictionary<int, List<Vector2Int>> FireData;
}