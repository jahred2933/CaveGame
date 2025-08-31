using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class PlayerSpawner : MonoBehaviourPunCallbacks
{
    public static PlayerSpawner Instance { get; private set; }

    [Header("Must match your Build Settings entry exactly")]
    public string gameSceneName = "Main Scene";

    [Header("Assign in Inspector (must be in Resources/)")]
    public GameObject playerPrefab;
    public Transform spawnPoint;
    [Header("Spawn Options")]
    public float spawnRadius = 5f; // Added: tweakable radius for random spawn

    [Tooltip("Print informational logs. Turn off for a cleaner console.")]
    public bool verboseLogging = true;

    private bool hasSpawned = false;
    private const string logPrefix = "[Spawner]";
    private bool isSpawning = false;

    [Header("Run / Scaling")]
    [Tooltip("If ON, lives are required to respawn mid-level. If OFF, respawn always.")]
    public bool permadeath = true;

    [InspectorName("Run Level")]
    [Tooltip("Difficulty scaling level (L) used in formulas. Host sets it.")]
    [Min(1)]
    public int runLevelEnv = 1;

    [InspectorName("Enemy HP Scaling (Amount per Level)")]
    [Tooltip("How much enemy HP increases per run level. Used in scaling formula.")]
    [Min(0f)]
    public float enemyHealthK = 0.05f;

    [InspectorName("Enemy HP Scaling (Curve Power)")]
    [Tooltip("Exponent/curve for enemy HP scaling. 1 = linear, >1 = ramps up faster.")]
    [Min(0f)]
    public float enemyHealthPow = 1.10f;

    [InspectorName("Enemy Damage Scaling (Amount per Level)")]
    [Tooltip("How much enemy damage increases per run level. Used in scaling formula.")]
    [Min(0f)]
    public float enemyDamageK = 0.04f;

    [InspectorName("Enemy Damage Scaling (Curve Power)")]
    [Tooltip("Exponent/curve for enemy HP scaling. 1 = linear, >1 = ramps up faster.")]
    [Min(0f)]
    public float enemyDamagePow = 1.15f;

    [InspectorName("Points: K")]
    [Tooltip("Points scale: final = base * (1 + K * L^Pow).")]
    [Min(0f)]
    public float pointsK = 0.10f;

    [InspectorName("Points: Pow")]
    [Tooltip("Curve shape for points scaling. 1 = linear, >1 grows faster.")]
    [Min(0f)]
    public float pointsPow = 0.90f;

    [Header("Underdog Progression")]
    [InspectorName("Max Underdog Gain / Clear")]
    [Tooltip("Max bonus levels granted to lower-level players per clear.")]
    [Min(0)]
    public int maxUnderdogGainPerClear = 5;

    [InspectorName("Underdog per Level Gap")]
    [Tooltip("Bonus per level gap. Total bonus = ceil(gap * this), capped by Max Underdog Gain.")]
    [Min(0f)]
    public float underdogPerGap = 0.25f; // ceil(gap*0.25)

    private readonly Dictionary<int, PlayerProfile> profiles = new(); // actor -> profile

    public float EnemyHealthMult => 1f + enemyHealthK * Mathf.Pow(Mathf.Max(1, runLevelEnv), enemyHealthPow);
    public float EnemyDamageMult => 1f + enemyDamageK * Mathf.Pow(Mathf.Max(1, runLevelEnv), enemyDamagePow);
    public float PointsMult => 1f + pointsK * Mathf.Pow(Mathf.Max(1, runLevelEnv), pointsPow);

    // Cache local player object + apply-wait coroutine
    private GameObject localPlayerCached;
    private Coroutine pendingApplyCo;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        DontDestroyOnLoad(gameObject);

        if (playerPrefab == null)
            Debug.LogError($"{logPrefix} playerPrefab is NOT assigned. Assign a prefab located in a Resources folder.");
        if (spawnPoint == null)
            Debug.LogError($"{logPrefix} spawnPoint is NOT assigned. Cannot spawn.");

        var testLoad = playerPrefab != null ? Resources.Load<GameObject>(playerPrefab.name) : null;
        if (playerPrefab != null && testLoad == null)
            Debug.LogWarning($"{logPrefix} Prefab '{playerPrefab.name}' not found via Resources.Load. Ensure it's under a Resources folder.");

        GlobalGameEvents.LevelCleared += OnLevelCleared;
        GlobalGameEvents.PlayerDied += OnPlayerDied;
    }

    void OnDestroy()
    {
        GlobalGameEvents.LevelCleared -= OnLevelCleared;
        GlobalGameEvents.PlayerDied -= OnPlayerDied;
    }

    void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    public override void OnJoinedRoom()
    {
        var localId = PhotonNetwork.LocalPlayer?.UserId ?? SystemInfo.deviceUniqueIdentifier;
        var prof = SaveSystem.Load(localId);
        prof.playerId = localId;
        prof.playerName = PhotonNetwork.NickName;
        profiles[PhotonNetwork.LocalPlayer.ActorNumber] = prof;
        SaveSystem.Save(prof);

        if (PhotonNetwork.IsMasterClient)
        {
            int hostLevel = prof.playerLevel;
            int highest = hostLevel;
            foreach (var p in PhotonNetwork.PlayerList)
            {
                var pid = p.UserId ?? SystemInfo.deviceUniqueIdentifier + "_" + p.ActorNumber;
                var pr = SaveSystem.Load(pid);
                highest = Mathf.Max(highest, pr.playerLevel);
                profiles[p.ActorNumber] = pr;
            }
            runLevelEnv = Mathf.Max(1, highest);
        }

        TrySpawn();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Fresh scene: clear cached player, reset spawn state, and rebind spawnPoint if needed
        localPlayerCached = null;
        hasSpawned = false;
        RefreshSpawnPoint();
        TrySpawn();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        OnJoinedRoom();
    }

    public void ForceSpawnIfPossible() => TrySpawn();

    void TrySpawn()
    {
        if (!this) return;
        if (isSpawning) { if (verboseLogging) Debug.Log($"{logPrefix} Spawn already in progress, skipping."); return; }
        if (hasSpawned) { if (verboseLogging) Debug.Log($"{logPrefix} Already spawned, skipping."); return; }
        if (playerPrefab == null || spawnPoint == null) return;
        if (!PhotonNetwork.InRoom) { if (verboseLogging) Debug.Log($"{logPrefix} Not in room yet, skipping."); return; }

        string activeSceneName = SceneManager.GetActiveScene().name;
        if (activeSceneName != gameSceneName) { if (verboseLogging) Debug.Log($"{logPrefix} Wrong scene ({activeSceneName}), waiting for '{gameSceneName}'."); return; }

        isSpawning = true;
        // Changed: spawn at a random position around spawnPoint within spawnRadius
        Vector2 rand = Random.insideUnitCircle * spawnRadius;
        Vector3 spawnPos = spawnPoint.position + new Vector3(rand.x, 0, rand.y);
        var go = PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnPoint.rotation);
        isSpawning = false;
        hasSpawned = true;
        localPlayerCached = go; // cache our freshly spawned local player
        if (verboseLogging) Debug.Log($"{logPrefix} Spawn complete.");

        ApplyUpgradesToPlayer(go, GetLocalProfile());
    }

    // Re-acquire spawn point after scene loads if the serialized reference died with the previous scene
    void RefreshSpawnPoint()
    {
        // if still valid, keep the Inspector-assigned reference
        if (spawnPoint != null && spawnPoint) return;

        // fallback: tag-based
        var tagged = GameObject.FindWithTag("PlayerSpawn");
        if (tagged != null)
        {
            spawnPoint = tagged.transform;
            if (verboseLogging) Debug.Log($"{logPrefix} Rebound spawnPoint via tag 'PlayerSpawn'.");
            return;
        }

        // fallback: common names
        var byName = GameObject.Find("PlayerSpawn") ?? GameObject.Find("SpawnPoint") ?? GameObject.Find("Player Spawn");
        if (byName != null)
        {
            spawnPoint = byName.transform;
            if (verboseLogging) Debug.Log($"{logPrefix} Rebound spawnPoint via name '{byName.name}'.");
            return;
        }

        if (verboseLogging) Debug.LogWarning($"{logPrefix} No spawnPoint found. Assign a Transform or tag one as 'PlayerSpawn' in the scene.");
    }

    // ... (rest of the script unchanged)
}