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
        var go = PhotonNetwork.Instantiate(playerPrefab.name, spawnPoint.position, spawnPoint.rotation);
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

    // Lazy-load local profile so it's never null after joining a room
    public PlayerProfile GetLocalProfile()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return null;
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!profiles.TryGetValue(actor, out var prof) || prof == null)
        {
            string id = PhotonNetwork.LocalPlayer.UserId ?? SystemInfo.deviceUniqueIdentifier;
            prof = SaveSystem.Load(id);
            prof.playerId = id;
            prof.playerName = PhotonNetwork.NickName;
            profiles[actor] = prof;
            SaveSystem.Save(prof);
        }
        return prof;
    }

    PlayerProfile GetProfile(int actor)
    {
        if (profiles.TryGetValue(actor, out var p)) return p;
        var pl = FindPhotonPlayer(actor);
        string id = pl?.UserId ?? (SystemInfo.deviceUniqueIdentifier + "_" + actor);
        var prof = SaveSystem.Load(id);
        prof.playerName = pl != null ? pl.NickName : prof.playerName;
        profiles[actor] = prof;
        return prof;
    }

    Photon.Realtime.Player FindPhotonPlayer(int actor)
    {
        foreach (var p in PhotonNetwork.PlayerList)
            if (p.ActorNumber == actor) return p;
        return null;
    }

    // Gate respawn (called by PlayerHealth on lethal)
    public bool TryConsumeLifeOnDeath(int actorNumber)
    {
        if (!permadeath) return true; // allow respawn always

        var prof = GetProfile(actorNumber);
        if (prof.extraLives > 0)
        {
            prof.extraLives -= 1;
            SaveSystem.Save(prof);
            if (verboseLogging) Debug.Log($"{logPrefix} Personal life consumed for actor {actorNumber}. Remaining: {prof.extraLives}");
            return true;
        }

        if (verboseLogging) Debug.Log($"{logPrefix} No lives left for actor {actorNumber}. No respawn.");
        return false;
    }

    void OnLevelCleared()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int hostLevel = GetLocalProfile().playerLevel;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            var prof = GetProfile(p.ActorNumber);
            int gap = Mathf.Max(0, hostLevel - prof.playerLevel);
            int bonus = Mathf.Clamp(Mathf.CeilToInt(gap * underdogPerGap), 0, maxUnderdogGainPerClear);
            prof.playerLevel = Mathf.Max(0, prof.playerLevel + 1 + bonus);
            SaveSystem.Save(prof);
        }

        runLevelEnv = Mathf.Max(1, runLevelEnv + 1);

        // Master restores each player via their own PhotonView (no PV on this object needed)
        RestoreAllPlayers();
    }

    // Master calls this locally and sends RPC to each player's owner
    void RestoreAllPlayers()
    {
        var all = GameObject.FindGameObjectsWithTag("Player");
        foreach (var go in all)
        {
            var ph = go.GetComponent<PlayerHealth>() ?? go.GetComponentInChildren<PlayerHealth>(true);
            if (ph != null && ph.photonView != null)
            {
                ph.photonView.RPC("FullRestoreAtStart", ph.photonView.Owner, null);
            }
        }
    }

    void OnPlayerDied(int actor)
    {
        // handled by TryConsumeLifeOnDeath at the time of death
    }

    public bool TryPurchaseUpgrade(string key, out string msg)
    {
        msg = "";
        var prof = GetLocalProfile();
        if (prof == null) { msg = "No profile"; return false; }

        int owned = prof.Get(key);
        int cost = CalcCost(owned);
        var pts = PointsManager.Instance != null ? PointsManager.Instance.LocalSpendablePoints : 0;
        if (pts < cost) { msg = $"Need {cost}"; return false; }

        if (PointsManager.Instance != null) PointsManager.Instance.SetLocalSpendablePoints(pts - cost);
        prof.Add(key, 1);
        if (key == "ExtraLife")
            prof.extraLives += 1;

        SaveSystem.Save(prof);

        // apply now if player exists, or wait until the player appears
        EnsureApplyNowOrWhenReady(prof);

        msg = "OK";
        return true;
    }

    public int CalcCost(int owned) => Mathf.CeilToInt(100f * Mathf.Pow(1.25f, owned));

    // Uses cached player, then robust search across hierarchy
    GameObject FindLocalPlayerObject()
    {
        // Use cached if valid and still ours
        if (localPlayerCached != null)
        {
            var cachedPv = localPlayerCached.GetComponentInChildren<PhotonView>(true) ?? localPlayerCached.GetComponentInParent<PhotonView>(true) ?? localPlayerCached.GetComponent<PhotonView>();
            if (cachedPv != null && cachedPv.IsMine)
                return localPlayerCached;

            // If cache got invalid (scene reload / ownership change), clear it
            if (cachedPv == null || !cachedPv.IsMine) localPlayerCached = null;
        }

        var all = GameObject.FindGameObjectsWithTag("Player");
        foreach (var go in all)
        {
            var pv = go.GetComponentInChildren<PhotonView>(true) ?? go.GetComponentInParent<PhotonView>(true) ?? go.GetComponent<PhotonView>();
            if (pv != null && pv.IsMine)
            {
                localPlayerCached = go; // refresh cache
                return go;
            }
        }
        return null;
    }

    // ensure upgrades are applied even if purchase happened before spawn finished
    void EnsureApplyNowOrWhenReady(PlayerProfile prof)
    {
        var local = FindLocalPlayerObject();
        if (local != null)
        {
            ApplyUpgradesToPlayer(local, prof);
            return;
        }

        if (pendingApplyCo != null) return; // already waiting
        pendingApplyCo = StartCoroutine(WaitForLocalPlayerAndApply(prof, 8f));
    }

    System.Collections.IEnumerator WaitForLocalPlayerAndApply(PlayerProfile prof, float timeoutSeconds)
    {
        if (verboseLogging) Debug.Log($"{logPrefix} Waiting for local player to appear to apply upgrades...");
        float t = 0f;
        while (t < timeoutSeconds)
        {
            var local = FindLocalPlayerObject();
            if (local != null)
            {
                if (verboseLogging) Debug.Log($"{logPrefix} Local player found after {t:0.00}s. Applying upgrades.");
                ApplyUpgradesToPlayer(local, prof);
                pendingApplyCo = null;
                yield break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (verboseLogging) Debug.LogWarning($"{logPrefix} Timed out waiting for local player. Upgrades will apply on next spawn.");
        pendingApplyCo = null;
    }

    public void ApplyUpgradesToPlayer(GameObject player, PlayerProfile prof)
    {
        if (player == null || prof == null) return;

        // Health
        var health = player.GetComponent<PlayerHealth>() ?? player.GetComponentInChildren<PlayerHealth>(true);
        if (health != null)
        {
            int mh = prof.Get("MaxHealth");
            health.maxHealth = Mathf.Max(1, health.maxHealth + mh * 10); // +10 HP per rank
            int hr = prof.Get("HealthRegen");
            health.regenRate = Mathf.Max(0.2f, health.regenRate * Mathf.Pow(0.92f, hr)); // ~8% faster per rank

            // Refresh health and UI for the owner
            if (health.photonView != null && health.photonView.IsMine)
                health.photonView.RPC(nameof(PlayerHealth.FullRestore), health.photonView.Owner);
        }

        // Weapon
        var gun = player.GetComponent<Player>() ?? player.GetComponentInChildren<Player>(true);
        if (gun != null)
        {
            int dmg = prof.Get("Damage");
            // +0.75 dmg per rank (rounded to int if needed)
            gun.damagePerShot = Mathf.Max(1, Mathf.RoundToInt(gun.damagePerShot + dmg * 0.75f));
        }

        // Movement/Stamina
        var ctrl = player.GetComponent<PlayerController>() ?? player.GetComponentInChildren<PlayerController>(true);
        if (ctrl != null)
        {
            int spd = prof.Get("MoveSpeed");
            float spdMult = 1f + spd * 0.01f; // +1% per rank
            ctrl.walkSpeed *= spdMult;
            ctrl.sprintSpeed *= spdMult;

            int stam = prof.Get("Stamina");
            ctrl.maxStamina = Mathf.Max(1f, ctrl.maxStamina + stam * 5f);

            int sRegen = prof.Get("StaminaRegen");
            ctrl.staminaIncreasePerSecond = Mathf.Max(0.1f, ctrl.staminaIncreasePerSecond * Mathf.Pow(1.08f, sRegen)); // +8% per rank
        }

        if (verboseLogging) Debug.Log($"{logPrefix} Upgrades applied to local player.");
    }
}