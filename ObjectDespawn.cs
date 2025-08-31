using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DespawnAfterTime : MonoBehaviour
{
    public float despawnTime = 5f; // The amount of time before the object despawns

    private float despawnTimer; // The current time since the object spawned

    private void Start()
    {
        despawnTimer = 0f;
    }

    private void Update()
    {
        despawnTimer += Time.deltaTime;

        if (despawnTimer >= despawnTime)
        {
            Destroy(gameObject);
        }
    }
}
