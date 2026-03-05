using System.Collections.Generic;
using UnityEngine;

public class FirePool : MonoBehaviour
{
    [SerializeField] private GameObject firePrefab;
    [SerializeField] private int initialSize = 100;

    private Queue<GameObject> pool = new();

    private void Awake()
    {
        for (int i = 0; i < initialSize; i++)
        {
            GameObject obj = Create();
            pool.Enqueue(obj);
        }
    }

    private GameObject Create()
    {
        Debug.Log("New Fire object was created!");
        GameObject obj = Instantiate(firePrefab, transform);
        obj.SetActive(false);
        return obj;
    }

    public GameObject Get()
    {
        if (pool.Count == 0)
            pool.Enqueue(Create());

        GameObject obj = pool.Dequeue();
        return obj;
    }

    public void Release(GameObject obj)
    {
        obj.SetActive(false);
        pool.Enqueue(obj);
    }
}
