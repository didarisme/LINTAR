using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

public class CesiumSpawnTest : MonoBehaviour
{
    [Header("Assign in Inspector")]
    [SerializeField] private CesiumGeoreference geoRef;

    [Header("Test Coordinates (from NetLogo WORLD line)")]
    [SerializeField] private double longitude = 8.698479023828993;
    [SerializeField] private double latitude = 45.85979141957454;
    [SerializeField] private double height = 0;

    [SerializeField] private float cubeSize = 10f;

    private void Start()
    {
        SpawnTestCube();
    }

    private void SpawnTestCube()
    {
        if (geoRef == null)
        {
            Debug.LogError("CesiumGeoreference not assigned!");
            return;
        }

        // создаём куб
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = Vector3.one * cubeSize;

        // делаем дочерним к Georeference
        cube.transform.SetParent(geoRef.transform, false);

        // добавляем Anchor
        var anchor = cube.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = new double3(longitude, latitude, height);

        Debug.Log($"Spawned cube at Lon:{longitude}, Lat:{latitude}");
    }
}
