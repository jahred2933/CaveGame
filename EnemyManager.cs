using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;
using System.Collections;

[System.Serializable]
public class EnemySpawnPointData
{
    public Transform spawnPoint;
    public GameObject enemyPrefab;
    public bool spawnAtStart = true;
    public bool continuousSpawn = false;
    public float initialSpawnDelay = 2f;
    public float spawnInterval = 5f;
    public int totalEnemies = 1;

    // Internal guards to prevent duplicate routine starts
    [HideInInspector] public bool startedSpawnOnce;
    [HideInInspector] public bool startedContinuous;

    // NEW: cache the spawn point's name so we can rebind across scene reloads
    [HideInInspector] public string cachedSpawnPointName;
}

public class EnemyManager : MonoBehaviourPunCallbacks
{
    [Header("Which scene to run in")]
    public string gameSceneName = "Main Scene";

    [Header("All Spawn Points")]
    public EnemySpawnPointData[] spawnPoints;

    void Awake()
    {
        // Cache names for existing spawnPoint references so we can find them next scene
        CacheSpawnPointNames();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        // Ensure any continuous routines stop when scene unloads or this is disabled
        StopAllCoroutines();
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        // Wait a frame or two so scene objects exist before starting
        StartCoroutine(DelayedTryStartSpawning());
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != gameSceneName) return;

        // Clean restart when the scene (re)loads
        StopAllCoroutines();
        ResetSpawnFlags();
        // Rebind spawn points if references became null after reload
        RebindSpawnPoints();
        StartCoroutine(DelayedTryStartSpawning());
    }

    IEnumerator DelayedTryStartSpawning()
    {
        // Wait end of frame to ensure scene instances (spawn points) are present
        yield return null;
        yield return null; // cheap insurance for network/scene init order
        TryStartSpawning();
    }

    void CacheSpawnPointNames()
    {
        if (spawnPoints == null) return;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            var d = spawnPoints[i];
            if (d.spawnPoint != null)
                d.cachedSpawnPointName = d.spawnPoint.name;
        }
    }

    void RebindSpawnPoints()
    {
        if (spawnPoints == null) return;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            var d = spawnPoints[i];
            if (d.spawnPoint == null)
            {
                // Prefer unique name rebind (fast and simple)
                if (!string.IsNullOrEmpty(d.cachedSpawnPointName))
                {
                    var go = GameObject.Find(d.cachedSpawnPointName);
                    if (go != null)
                    {
                        d.spawnPoint = go.transform;
#if UNITY_EDITOR
                        Debug.Log($"[EnemyManager] Rebound spawnPoint '{d.cachedSpawnPointName}'.");
#endif
                        continue;
                    }
                }
                // Optional: tag-based fallback if you decide to tag them (commented to keep behavior unchanged)
                // var tagged = GameObject.FindWithTag("EnemySpawn");
                // if (tagged) d.spawnPoint = tagged.transform;
            }
            else
            {
                // Keep name cache fresh in case designer renamed the transform
                d.cachedSpawnPointName = d.spawnPoint.name;
            }
        }
    }

    void ResetSpawnFlags()
    {
        if (spawnPoints == null) return;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            spawnPoints[i].startedSpawnOnce = false;
            spawnPoints[i].startedContinuous = false;
        }
    }

    void TryStartSpawning()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!PhotonNetwork.InRoom) return;

        Debug.Log("[EnemyManager] Starting spawn routines");
        foreach (var data in spawnPoints)
        {
            if (data.spawnPoint == null || data.enemyPrefab == null)
            {
                Debug.LogWarning("[EnemyManager] Missing spawnPoint or prefab");
                continue;
            }

            if (data.spawnAtStart && !data.startedSpawnOnce)
            {
                data.startedSpawnOnce = true;
                StartCoroutine(SpawnOnce(data));
            }
            if (data.continuousSpawn && !data.startedContinuous)
            {
                data.startedContinuous = true;
                StartCoroutine(SpawnContinuously(data));
            }
        }
    }

    IEnumerator SpawnOnce(EnemySpawnPointData d)
    {
        yield return new WaitForSeconds(d.initialSpawnDelay);
        for (int i = 0; i < d.totalEnemies; i++)
        {
            Spawn(d);
            yield return new WaitForSeconds(0.2f);
        }
    }

    IEnumerator SpawnContinuously(EnemySpawnPointData d)
    {
        yield return new WaitForSeconds(d.initialSpawnDelay);
        var wait = new WaitForSeconds(d.spawnInterval); // cached wait object
        while (true)
        {
            Spawn(d);
            yield return wait;
        }
    }

    void Spawn(EnemySpawnPointData d)
    {
        if (d.spawnPoint == null || d.enemyPrefab == null)
        {
            Debug.LogWarning("[EnemyManager] Missing spawnPoint or prefab");
            return;
        }

        string prefabName = d.enemyPrefab.name;
        PhotonNetwork.Instantiate(
            prefabName,
            d.spawnPoint.position,
            d.spawnPoint.rotation
        );
    }
}