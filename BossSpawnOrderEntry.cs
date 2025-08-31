using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using static UnityEngine.EventSystems.EventTrigger;

[System.Serializable]
public class BossSpawnOrderEntry
{
    [Range(0f, 1f)]
    [Tooltip("When boss health fraction falls below this, spawn the prefab.")]
    public float activationThreshold;
    [Tooltip("Prefab to spawn (must live in Resources).")]
    public GameObject spawnPrefab;
    [Tooltip("Name of the GameObject in scene to use as spawn point.")]
    public string spawnPointName;
    [HideInInspector] public Transform spawnPoint;
    [HideInInspector] public bool spawned;
    [HideInInspector] public GameObject spawnedInstance;

    public void Initialize()
    {
        spawned = false;
        spawnedInstance = null;
        spawnPoint = null;
        if (!string.IsNullOrEmpty(spawnPointName))
        {
            var go = GameObject.Find(spawnPointName);
            if (go != null)
                spawnPoint = go.transform;
            else
                Debug.LogWarning($"BossController: spawn point '{spawnPointName}' not found");
        }
    }
    public void Despawn(float bossHealthPercentage) {
        if (spawned && bossHealthPercentage > activationThreshold) {
            if (spawnedInstance != null) {
                PhotonNetwork.Destroy(spawnedInstance);
                spawnedInstance = null;
            }
            spawned = false;
        }
    }
    public void Spawn(float bossHealthPercentage) {
        if (!spawned && bossHealthPercentage <= activationThreshold) {
            if (spawnPrefab != null && spawnPoint != null) {
                GameObject spawnedObj = PhotonNetwork.Instantiate(
                    spawnPrefab.name,
                    spawnPoint.position,
                    spawnPoint.rotation
                );
                spawned = true;
                spawnedInstance = spawnedObj;
            }
        }
    }
}