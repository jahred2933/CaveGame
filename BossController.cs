using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using TMPro;
using UnityEngine.AI;
using MimicSpace;

[DisallowMultipleComponent]
public class BossController : MonoBehaviourPunCallbacks, IPunObservable
{
    // ----------------- Nested Data Structs -----------------
    [Serializable]
    public class ShieldSettings
    {
        public GameObject rockPrefab;
        public GameObject beamPrefab;
        public int numberOfRocks = 4;
        public float spawnRangeModifier = 2f;
        public float raiseDuration = 1f;
        public Vector2 raiseDelayRange = new Vector2(0.2f, 1f);
        public float beamHeight = 6f;
        public LayerMask groundLayer;
    }

    [Serializable]
    public class MissileSettings
    {
        public GameObject missilePrefab;
        public int numberOfMissiles = 3;
        public float spawnRadius = 5f;
        public float interval = 10f;
    }

    [Serializable]
    public class ChargeSettings
    {
        public float cooldown = 3.5f;
        public float speedMultiplier = 5f;
        public float chargeAcceleration = 175f;
        public float normalAcceleration = 40f;
        public float duration = 1.5f;
        public float overshootDistance = 2f;
        public float postChargeResetDelay = 1.2f;
        public float collisionPushbackDistance = 0.3f;
        public float arriveThreshold = 0.4f;
    }

    public enum BossState { Idle, Protection, Chasing }

    // ----------------- Serialized Fields -----------------
    [Header("General")]
    [SerializeField] private string gameSceneName = "Main Scene";
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Transform damageFeedbackTarget;
    [SerializeField] private List<GameObject> detectionObjects = new();
    [SerializeField] private List<BossSpawnOrderEntry> bossSpawnOrderEntries = new();

    [Header("Stats")]
    [SerializeField] private int startingHealth = 100;
    [SerializeField] private float healRatePerSecond = 5f;
    [SerializeField] private float aggroDistance = 10f;

    [Header("Phase Durations")]
    [SerializeField] private float chasingPhaseDuration = 10f;

    [Header("Movement / Rotation")]
    [SerializeField] private float turningSpeedDegPerSec = 180f;
    [SerializeField] private float navStoppingDistance = 5f;
    [SerializeField] private float rotationYawOffset = 0f;

    [Header("Damage")]
    [SerializeField] private int proximityDamage = 10;
    [SerializeField] private float proximityDamageInterval = 1f; // Also drives attack trigger cadence.

    [Header("Settings Groups")]
    [SerializeField] private ShieldSettings shield = new();
    [SerializeField] private MissileSettings missiles = new();
    [SerializeField] private ChargeSettings charge = new();

    [Header("Audio")]
    // protectionBreakAudioSource removed previously.
    [SerializeField] private AudioSource chargeWarningAudioSource;

    [Header("Animation")]
    [SerializeField] private Animator animator; // Walk(bool), Charge(bool), Attack(trigger), Angry(trigger)
    [SerializeField] private float angryDuration = 2f; // Failsafe if no animation event fires.

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    // ----------------- Runtime State -----------------
    private BossState state = BossState.Idle;
    private bool bossStarted;
    private int currentHealth;
    private GameObject currentTargetPlayer;
    private readonly List<GameObject> activeShieldRocks = new();
    private readonly Dictionary<GameObject, float> lastProximityDamageTime = new();
    private List<Mimic> cachedMimics = new();

    // Timers
    private float phaseTimer;
    private float missileTimer;
    private float chargeTimer;
    private float mimicProtectionRefreshTimer;

    // Charge
    private bool isCharging;
    private Coroutine chargeRoutine;
    private Vector3 chargeTargetPos = Vector3.zero;
    private float nextChargeRepathTime = 0f;
    private const float chargeRepathInterval = 0.2f;

    // Coroutines
    private Coroutine stateMachineRoutine;
    private Coroutine protectionPhaseRoutine;
    private Coroutine chasingPhaseRoutine;
    private Coroutine damageFlashRoutine;
    private Coroutine angryCoroutine;

    // Angry phase control
    private bool inAngry;
    private bool angryFinishedFlag; // NEW (change 1)

    // Components
    private NavMeshAgent agent;
    private Rigidbody rb;

    // Movement caches
    private float baseSpeed;
    private float baseAcceleration;
    private bool baseAutoBraking;
    private float baseStoppingDistance;

    // Damage feedback
    private Quaternion originalFeedbackRotation;

    // Destination smoothing
    private float lastChaseUpdateTime;
    private Vector3 lastPlayerPosition = Vector3.positiveInfinity;
    private readonly float chaseUpdateInterval = 0.2f;
    private readonly float destinationUpdateThreshold = 0.5f;

    // Cached yields
    private static readonly WaitForSeconds WaitQuarter = new(0.25f);
    private static readonly WaitForSeconds WaitOnePointFive = new(1.5f);

    // Player caching
    private static readonly List<GameObject> cachedPlayers = new();
    private static float nextPlayerCacheTime;
    private const float playerCacheInterval = 0.5f;

    // Shield / beam management
    private class BeamData
    {
        public GameObject beamObj;
        public Light light;
        public Transform rockTransform;
    }
    private readonly List<BeamData> activeBeams = new();
    private float lastShieldCleanTime;
    private const float shieldCleanInterval = 0.5f;

    // Rock raising
    private class RaisingRock
    {
        public GameObject rock;
        public Vector3 startPos;
        public Vector3 targetPos;
        public float duration;
        public float delay;
        public float elapsed;
        public float spinInitial;
        public float startYRot;
        public RockSpin spin;
        public bool started;
    }
    private readonly List<RaisingRock> raisingRocks = new();

    // Network delta compression
    private int lastSentHealth = int.MinValue;
    private BossState lastSentState = (BossState)(-1);
    private bool lastSentCharging = false;
    private Quaternion lastSentRotation = Quaternion.identity;

    // Animation networking
    private bool lastAnimWalk;
    private bool lastAnimCharge;
    private bool pendingAttackTrigger;
    private bool pendingAngryTrigger;
    private bool sentAnimWalkCached;
    private bool sentAnimChargeCached;

    // Flags for network bit packing
    private const byte NetFlag_Health   = 1 << 0;
    private const byte NetFlag_State    = 1 << 1;
    private const byte NetFlag_Charging = 1 << 2;
    private const byte NetFlag_Rotation = 1 << 3;
    private const byte NetFlag_Anim     = 1 << 4;

    // Cleanup guard
    private bool disposed = false;

    // ----------------- Unity Lifecycle -----------------
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        if (!agent)
            Debug.LogError("[BossController] Missing NavMeshAgent.");

        if (!damageFeedbackTarget)
            damageFeedbackTarget = transform;

        originalFeedbackRotation = damageFeedbackTarget.localRotation;
        SetAllDetectionObjects(false);
        CacheMimics(); // FIX: ensure this method exists
        CachePlayersForced();
    }

    private void Start()
    {
        // Scale boss health and proximity damage by run multipliers if available
        if (PhotonNetwork.IsMasterClient && PlayerSpawner.Instance != null)
        {
            startingHealth = Mathf.Max(1, Mathf.RoundToInt(startingHealth * PlayerSpawner.Instance.EnemyHealthMult));
            proximityDamage = Mathf.Max(1, Mathf.RoundToInt(proximityDamage * PlayerSpawner.Instance.EnemyDamageMult)); // ensure area damage scales
        }

        InitializeHealth(startingHealth);
        SetupAgentDefaults();

        if (PhotonNetwork.IsMasterClient)
        {
            InitializeSpawnOrderEntries();
            SelectNearestPlayer();
            StartBossActivationWatcher();
        }
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (Time.time >= nextPlayerCacheTime)
            CachePlayers();

        MaintainCurrentTarget();

        if (!inAngry && state == BossState.Chasing)
            UpdateManualRotation();

        UpdateBeams();
        UpdateRaisingRocks();

        if (Time.time >= lastShieldCleanTime)
        {
            CleanShieldRocks();
            lastShieldCleanTime = Time.time + shieldCleanInterval;
        }

        if (!inAngry && isCharging && currentTargetPlayer)
        {
            if (Time.time >= nextChargeRepathTime)
            {
                float sqr = (currentTargetPlayer.transform.position - chargeTargetPos).sqrMagnitude;
                if (sqr > 4f)
                    UpdateChargeDestination();
                nextChargeRepathTime = Time.time + chargeRepathInterval;
            }
            float distSqr = (transform.position - currentTargetPlayer.transform.position).sqrMagnitude;
            if (distSqr > aggroDistance * aggroDistance * 1.5f)
            {
                if (debugLogs) Debug.Log("[BossController] Charge aborted (target left aggro).");
                EndChargeIfActive();
            }
        }

        UpdateContinuousAnimationParams();
    }

    private void OnDisable()
    {
        StopAllManagedCoroutines();
        disposed = true;
    }

    private void OnDestroy()
    {
        if (!disposed)
        {
            StopAllManagedCoroutines();
            disposed = true;
        }
    }

    // ----------------- Player Cache -----------------
    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) => CachePlayersForced();
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        CachePlayersForced();
        if (currentTargetPlayer == null)
            SelectNearestPlayer();
    }

    private void CachePlayers()
    {
        nextPlayerCacheTime = Time.time + playerCacheInterval;
        cachedPlayers.Clear();
        var players = GameObject.FindGameObjectsWithTag("Player");
        cachedPlayers.AddRange(players);
    }
    private void CachePlayersForced()
    {
        nextPlayerCacheTime = Time.time;
        CachePlayers();
    }

    private void MaintainCurrentTarget()
    {
        if (currentTargetPlayer == null || !currentTargetPlayer.activeInHierarchy)
        {
            SelectNearestPlayer();
            return;
        }
        float sqr = (transform.position - currentTargetPlayer.transform.position).sqrMagnitude;
        if (sqr > aggroDistance * aggroDistance * 1.2f)
            SelectNearestPlayer();
    }

    private void SelectNearestPlayer()
    {
        GameObject closest = null;
        float bestSqr = float.MaxValue;
        foreach (var p in cachedPlayers)
        {
            if (!p) continue;
            float dSqr = (p.transform.position - transform.position).sqrMagnitude;
            if (dSqr < bestSqr)
            {
                bestSqr = dSqr;
                closest = p;
            }
        }
        currentTargetPlayer = closest;
    }

    // ----------------- Initialization -----------------
    private void InitializeHealth(int value)
    {
        currentHealth = Mathf.Clamp(value, 0, startingHealth);
        UpdateHealthUI();
        UpdateHealthDrivenSpawns();
    }

    private void SetupAgentDefaults()
    {
        if (!agent) return;

        baseSpeed = agent.speed;
        baseAcceleration = agent.acceleration;
        baseAutoBraking = agent.autoBraking;
        baseStoppingDistance = agent.stoppingDistance;

        if (agent.acceleration != charge.normalAcceleration)
            agent.acceleration = charge.normalAcceleration;
        if (!agent.autoBraking)
            agent.autoBraking = true;
        agent.updateRotation = false;
        if (Math.Abs(agent.stoppingDistance - navStoppingDistance) > 0.001f)
            agent.stoppingDistance = navStoppingDistance;
    }

    private void InitializeSpawnOrderEntries()
    {
        bossSpawnOrderEntries?.ForEach(e => e.Initialize());
    }

    private void CacheMimics() // FIX: added back
    {
        cachedMimics.Clear();
        var found = GetComponentsInChildren<Mimic>(true);
        cachedMimics.AddRange(found);
    }

    // ----------------- State Machine -----------------
    private void StartBossActivationWatcher()
    {
        if (stateMachineRoutine != null) StopCoroutine(stateMachineRoutine);
        stateMachineRoutine = StartCoroutine(ActivationWatcher());
    }

    private IEnumerator ActivationWatcher()
    {
        while (!bossStarted && currentHealth > 0)
        {
            if (currentTargetPlayer)
            {
                float sqr = (transform.position - currentTargetPlayer.transform.position).sqrMagnitude;
                if (sqr <= aggroDistance * aggroDistance)
                {
                    bossStarted = true;
                    TransitionToState(BossState.Protection);
                    yield break;
                }
            }
            SetAllDetectionObjects(false);
            yield return WaitQuarter;
        }
    }

    private void TransitionToState(BossState newState)
    {
        if (state == newState) return;
        ExitState(state);
        state = newState;
        if (debugLogs) Debug.Log($"[BossController] Transition -> {state}");
        EnterState(newState);
    }

    private void EnterState(BossState s)
    {
        phaseTimer = 0f;
        chargeTimer = 0f;
        mimicProtectionRefreshTimer = 0f;

        switch (s)
        {
            case BossState.Protection:
                StartProtectionPhase();
                break;
            case BossState.Chasing:
                StartChasingPhase();
                break;
            case BossState.Idle:
            default:
                SetAllDetectionObjects(false);
                break;
        }
    }

    private void ExitState(BossState s)
    {
        switch (s)
        {
            case BossState.Protection:
                CleanupProtectionPhase();
                break;
            case BossState.Chasing:
                EndChargeIfActive();
                if (agent)
                {
                    if (Math.Abs(agent.acceleration - charge.normalAcceleration) > 0.01f)
                        agent.acceleration = charge.normalAcceleration;
                    if (!agent.autoBraking)
                        agent.autoBraking = true;
                    if (Math.Abs(agent.stoppingDistance - baseStoppingDistance) > 0.01f)
                        agent.stoppingDistance = baseStoppingDistance;
                }
                break;
        }
    }

    // ----------------- Protection Phase -----------------
    private void StartProtectionPhase()
    {
        if (protectionPhaseRoutine != null) StopCoroutine(protectionPhaseRoutine);
        if (agent) agent.isStopped = true;
        FreezePosition();
        SetAllDetectionObjects(false);
        protectionPhaseRoutine = StartCoroutine(ProtectionPhaseLoop());
    }

    private IEnumerator ProtectionPhaseLoop()
    {
        bool rocksSpawned = false;

        yield return WaitOnePointFive;
        SetMimicsProtected(true);

        while (currentHealth > 0 && state == BossState.Protection)
        {
            if (!rocksSpawned)
            {
                SpawnShieldRocks();
                rocksSpawned = true;
            }

            HealOverTime();

            missileTimer += Time.deltaTime;
            if (missileTimer >= missiles.interval)
            {
                SpawnMissiles();
                missileTimer = 0f;
            }

            mimicProtectionRefreshTimer += Time.deltaTime;
            if (mimicProtectionRefreshTimer >= 1f)
            {
                SetMimicsProtected(true);
                mimicProtectionRefreshTimer = 0f;
            }

            UpdateDetectionVisibility();

            if (!ShieldActive() && !isCharging)
                break;

            yield return null;
        }

        if (state == BossState.Protection)
            BreakShieldAndAdvance();
        protectionPhaseRoutine = null;
    }

    private void BreakShieldAndAdvance()
    {
        SetMimicsProtected(false);
        TriggerAngry();
        if (angryCoroutine != null) StopCoroutine(angryCoroutine);
        angryCoroutine = StartCoroutine(AngryThenChase());
        TransitionToState(BossState.Idle);
    }

    private IEnumerator AngryThenChase()
    {
        inAngry = true;
        angryFinishedFlag = false; // NEW reset (change 2)
        if (agent) agent.isStopped = true;
        FreezePosition();

        float timer = 0f;
        // Wait until animation event flips angryFinishedFlag OR failsafe timer expires.
        while (!angryFinishedFlag && timer < angryDuration)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        UnfreezeAll();
        if (agent) agent.isStopped = false;
        inAngry = false;
        TransitionToState(BossState.Chasing);
        angryCoroutine = null;
    }

    // Called via Animation Event at end of Angry clip.
    public void AngryAnimationFinished() // NEW (change 3)
    {
        if (!PhotonNetwork.IsMasterClient) return; // only master drives logic
        angryFinishedFlag = true;
    }

    private void CleanupProtectionPhase()
    {
        DestroyShieldRocks();
        activeShieldRocks.Clear();
    }

    private bool ShieldActive() => activeShieldRocks.Count > 0;

    private void CleanShieldRocks()
    {
        for (int i = activeShieldRocks.Count - 1; i >= 0; i--)
            if (activeShieldRocks[i] == null)
                activeShieldRocks.RemoveAt(i);
    }

    private void SpawnShieldRocks()
    {
        CleanShieldRocks();
        if (activeShieldRocks.Count >= shield.numberOfRocks) return;

        float angleStep = 360f / shield.numberOfRocks;
        float radius = shield.spawnRangeModifier * 2f;

        for (int i = 0; i < shield.numberOfRocks; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 tentativePos = transform.position + dir * radius;

            if (Physics.Raycast(tentativePos + Vector3.up * 50f, Vector3.down, out var hit, 100f, shield.groundLayer))
            {
                Vector3 rockPos = hit.point;
                GameObject rock = PhotonNetwork.Instantiate(shield.rockPrefab.name, rockPos, Quaternion.identity);
                var spin = rock.AddComponent<RockSpin>();
                spin.spinSpeed = UnityEngine.Random.Range(10f, 50f);

                float delay = UnityEngine.Random.Range(shield.raiseDelayRange.x, shield.raiseDelayRange.y);
                raisingRocks.Add(new RaisingRock
                {
                    rock = rock,
                    startPos = rock.transform.position,
                    targetPos = rockPos,
                    duration = shield.raiseDuration,
                    delay = delay,
                    elapsed = 0f,
                    spin = spin,
                    spinInitial = spin.spinSpeed,
                    startYRot = rock.transform.eulerAngles.y
                });

                GameObject beam = PhotonNetwork.Instantiate(shield.beamPrefab.name,
                    rock.transform.position + Vector3.up * shield.beamHeight, Quaternion.identity);
                beam.transform.SetParent(rock.transform);
                var beamData = new BeamData
                {
                    beamObj = beam,
                    light = beam.GetComponentInChildren<Light>(),
                    rockTransform = rock.transform
                };
                activeBeams.Add(beamData);

                activeShieldRocks.Add(rock);
            }
        }
    }

    private void DestroyShieldRocks()
    {
        foreach (var rock in activeShieldRocks)
        {
            if (!rock) continue;
            foreach (Transform child in rock.transform)
            {
                var beamObj = child.gameObject;
                var light = beamObj.GetComponentInChildren<Light>();
                if (beamObj != null)
                {
                    StartCoroutine(DeextendAndDestroyBeam(beamObj, light, 2f));
                }
                else
                {
                    PhotonNetwork.Destroy(child.gameObject);
                }
            }
            PhotonNetwork.Destroy(rock);
        }
        activeShieldRocks.Clear();

        for (int i = activeBeams.Count - 1; i >= 0; i--)
            if (activeBeams[i].beamObj == null || activeBeams[i].rockTransform == null)
                activeBeams.RemoveAt(i);
    }

    // ----------- BEAM DEEXTEND COROUTINE (NEW) -----------
    private IEnumerator DeextendAndDestroyBeam(GameObject beamObj, Light light, float duration)
    {
        float t = 0f;
        Vector3 startScale = beamObj.transform.localScale;
        float startRange = light ? light.range : 1f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            beamObj.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, frac);
            if (light)
                light.range = Mathf.Lerp(startRange, 0f, frac);
            yield return null;
        }
        PhotonNetwork.Destroy(beamObj);
    }
    // -----------------------------------------------------

    // ----------------- Chasing Phase -----------------
    private void StartChasingPhase()
    {
        if (chasingPhaseRoutine != null) StopCoroutine(chasingPhaseRoutine);

        UnfreezeAll();
        if (agent)
        {
            if (agent.isStopped) agent.isStopped = false;
            if (Math.Abs(agent.acceleration - charge.normalAcceleration) > 0.01f)
                agent.acceleration = charge.normalAcceleration;
            if (!agent.autoBraking)
                agent.autoBraking = true;
            if (Math.Abs(agent.stoppingDistance - baseStoppingDistance) > 0.01f)
                agent.stoppingDistance = baseStoppingDistance;
        }
        if (chasingPhaseDuration <= 0f)
            chasingPhaseDuration = 10f;

        chasingPhaseRoutine = StartCoroutine(ChasingPhaseLoop());
    }

    private IEnumerator ChasingPhaseLoop()
    {
        float chaseUpdateAccumulator = 0f;

        while (currentHealth > 0 && phaseTimer < chasingPhaseDuration && state == BossState.Chasing)
        {
            if (inAngry) { yield return null; continue; }

            float dt = Time.deltaTime;
            phaseTimer += dt;

            missileTimer += dt;
            if (missileTimer >= missiles.interval)
            {
                SpawnMissiles();
                missileTimer = 0f;
            }

            chargeTimer += dt;
            if (!isCharging && !inAngry && chargeTimer >= charge.cooldown)
            {
                chargeTimer = 0f;
                StartCharge();
            }

            if (!isCharging)
            {
                chaseUpdateAccumulator += dt;
                if (chaseUpdateAccumulator >= chaseUpdateInterval)
                {
                    chaseUpdateAccumulator = 0f;
                    UpdateChaseDestination();
                }
            }

            UpdateDetectionVisibility();
            yield return null;
        }

        if (currentHealth > 0 && state == BossState.Chasing && !inAngry)
            TransitionToState(BossState.Protection);

        chasingPhaseRoutine = null;
    }

    // ----------------- Charge Logic -----------------
    private void StartCharge()
    {
        if (inAngry) return;
        if (chargeRoutine != null) StopCoroutine(chargeRoutine);
        chargeRoutine = StartCoroutine(ChargeSequence());
    }

    private IEnumerator ChargeSequence()
    {
        if (!agent || !currentTargetPlayer || inAngry)
            yield break;

        isCharging = true;
        Play(chargeWarningAudioSource);

        agent.speed = baseSpeed * charge.speedMultiplier;
        agent.acceleration = charge.chargeAcceleration;
        agent.autoBraking = false;
        agent.stoppingDistance = 0.05f;

        UpdateChargeDestination();
        nextChargeRepathTime = Time.time + chargeRepathInterval;

        float t = 0f;
        while (t < charge.duration && isCharging && !inAngry)
        {
            t += Time.deltaTime;
            if (!currentTargetPlayer) break;

            float arriveSqr = (transform.position - chargeTargetPos).sqrMagnitude;
            if (arriveSqr <= charge.arriveThreshold * charge.arriveThreshold)
                break;

            yield return null;
        }

        if (agent)
        {
            agent.speed = baseSpeed;
            agent.acceleration = charge.normalAcceleration;
        }

        yield return new WaitForSeconds(charge.postChargeResetDelay);

        if (agent)
        {
            agent.autoBraking = baseAutoBraking;
            agent.stoppingDistance = baseStoppingDistance;
        }

        isCharging = false;
        chargeTargetPos = Vector3.zero;
        chargeRoutine = null;
    }

    private void UpdateChargeDestination()
    {
        if (!agent || !currentTargetPlayer) return;

        Vector3 toPlayer = (currentTargetPlayer.transform.position - transform.position);
        if (toPlayer.sqrMagnitude < 0.01f) toPlayer = transform.forward;
        toPlayer.Normalize();
        chargeTargetPos = currentTargetPlayer.transform.position + toPlayer * charge.overshootDistance;
        agent.SetDestination(chargeTargetPos);
    }

    private void EndChargeIfActive()
    {
        if (chargeRoutine != null)
            StopCoroutine(chargeRoutine);
        isCharging = false;
        chargeTargetPos = Vector3.zero;
        if (agent)
        {
            agent.speed = baseSpeed;
            agent.acceleration = charge.normalAcceleration;
            agent.autoBraking = baseAutoBraking;
            agent.stoppingDistance = baseStoppingDistance;
        }
        chargeRoutine = null;
    }

    // ----------------- AI / Chasing Updates -----------------
    private void UpdateChaseDestination()
    {
        if (!agent || inAngry) return;

        if (!currentTargetPlayer)
        {
            SelectNearestPlayer();
            lastPlayerPosition = Vector3.positiveInfinity;
            return;
        }

        float distSqr = (transform.position - currentTargetPlayer.transform.position).sqrMagnitude;
        if (distSqr > aggroDistance * aggroDistance)
        {
            if (!agent.isStopped) agent.isStopped = true;
            return;
        }

        if (Time.time - lastChaseUpdateTime > chaseUpdateInterval ||
            (currentTargetPlayer.transform.position - lastPlayerPosition).sqrMagnitude > destinationUpdateThreshold * destinationUpdateThreshold)
        {
            if (agent.isStopped) agent.isStopped = false;
            agent.SetDestination(currentTargetPlayer.transform.position);
            lastPlayerPosition = currentTargetPlayer.transform.position;
            lastChaseUpdateTime = Time.time;
        }
    }

    private void UpdateManualRotation()
    {
        if (inAngry) return;
        if (!agent) return;
        if (!currentTargetPlayer)
        {
            SelectNearestPlayer();
            if (!currentTargetPlayer) return;
        }

        Vector3 toPlayer = currentTargetPlayer.transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        if (Mathf.Abs(rotationYawOffset) > 0.01f)
            targetRot *= Quaternion.Euler(0f, rotationYawOffset, 0f);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            turningSpeedDegPerSec * Time.deltaTime
        );
    }

    // ----------------- Damage & Healing -----------------
    [PunRPC]
    public void TakeDamage(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (state == BossState.Protection && ShieldActive()) return;
        if (amount <= 0) return;

        int newHealth = Mathf.Max(0, currentHealth - amount);
        if (newHealth == currentHealth) return;

        currentHealth = newHealth;
        UpdateHealthUI();
        UpdateHealthDrivenSpawns();
        PlayDamageFlash();

        if (currentHealth <= 0)
            photonView.RPC(nameof(RPC_Die), RpcTarget.AllBuffered);
    }

    private void HealOverTime()
    {
        if (currentHealth >= startingHealth) return;

        float healFloat = healRatePerSecond * Time.deltaTime;
        int delta = Mathf.FloorToInt(healFloat);
        if (delta <= 0) return;

        currentHealth = Mathf.Min(startingHealth, currentHealth + delta);
        UpdateHealthUI();
        UpdateHealthDrivenSpawns();
    }

    private void UpdateHealthDrivenSpawns()
    {
        float frac = (float)currentHealth / startingHealth;
        if (bossSpawnOrderEntries != null)
        {
            foreach (var entry in bossSpawnOrderEntries)
            {
                entry.Spawn(frac);
                entry.Despawn(frac);
            }
        }
    }

    private void UpdateHealthUI()
    {
        if (healthText)
            healthText.text = $"Health: {currentHealth}";
    }

    [PunRPC]
    private void RPC_Die()
    {
        StopAllManagedCoroutines();
        DestroyShieldRocks();
        SetMimicsProtected(false);
        GlobalGameEvents.EmitBossDefeated();
        PhotonNetwork.Destroy(gameObject);
    }

    // ----------------- Feedback -----------------
    private void PlayDamageFlash()
    {
        if (damageFlashRoutine != null)
            StopCoroutine(damageFlashRoutine);
        damageFlashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private IEnumerator DamageFlashRoutine()
    {
        if (!damageFeedbackTarget) yield break;
        var rend = damageFeedbackTarget.GetComponent<Renderer>();
        if (!rend) yield break;
        Material mat = rend.material;
        if (!mat || !mat.HasProperty("_EmissiveColor")) yield break;

        const float duration = 0.15f;
        float half = duration / 2f;

        Quaternion rotated = originalFeedbackRotation * Quaternion.Euler(-15f, 0f, 0f);

        Color baseColor = new(1.0f, 0.349f, 0f);
        Color emissiveBase = baseColor * 0.00f;
        Color emissiveFlash = baseColor * 0.10f;

        mat.SetColor("_EmissiveColor", emissiveBase);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float segmentT = t < half ? t / half : (t - half) / half;
            segmentT = Mathf.SmoothStep(0f, 1f, segmentT);

            if (t < half)
            {
                damageFeedbackTarget.localRotation =
                    Quaternion.Slerp(originalFeedbackRotation, rotated, segmentT);
                mat.SetColor("_EmissiveColor", Color.Lerp(emissiveBase, emissiveFlash, segmentT));
            }
            else
            {
                damageFeedbackTarget.localRotation =
                    Quaternion.Slerp(rotated, originalFeedbackRotation, segmentT);
                mat.SetColor("_EmissiveColor", Color.Lerp(emissiveFlash, emissiveBase, segmentT));
            }
            yield return null;
        }

        damageFeedbackTarget.localRotation = originalFeedbackRotation;
        mat.SetColor("_EmissiveColor", emissiveBase);
        damageFlashRoutine = null;
    }

    // ----------------- Missiles -----------------
    private void SpawnMissiles()
    {
        if (!missiles.missilePrefab) return;
        float angleStep = 360f / Mathf.Max(1, missiles.numberOfMissiles);
        for (int i = 0; i < missiles.numberOfMissiles; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 spawnPos = transform.position + dir * missiles.spawnRadius;
            GameObject m = PhotonNetwork.Instantiate(missiles.missilePrefab.name, spawnPos, Quaternion.LookRotation(dir));
            if (m.TryGetComponent<Collider>(out var mCol) && TryGetComponent<Collider>(out var bossCol))
                Physics.IgnoreCollision(mCol, bossCol);
        }
        if (debugLogs) Debug.Log("[BossController] Missiles spawned.");
    }

    // ----------------- Mimics -----------------
    private void SetMimicsProtected(bool value)
    {
        CacheMimics();
        foreach (var mimic in cachedMimics)
            if (mimic != null)
                mimic.SetProtectionMode(value);
    }

    // ----------------- Detection -----------------
    private void UpdateDetectionVisibility()
    {
        if (!currentTargetPlayer)
        {
            SelectNearestPlayer();
            if (!currentTargetPlayer)
            {
                SetAllDetectionObjects(false);
                return;
            }
        }
        float distSqr = (transform.position - currentTargetPlayer.transform.position).sqrMagnitude;
        SetAllDetectionObjects(distSqr <= aggroDistance * aggroDistance);
    }

    private void SetAllDetectionObjects(bool active)
    {
        foreach (var obj in detectionObjects)
            if (obj && obj.activeSelf != active)
                obj.SetActive(active);
    }

    // ----------------- Utility -----------------
    private void FreezePosition()
    {
        if (rb) rb.constraints = RigidbodyConstraints.FreezePosition;
    }

    private void UnfreezeAll()
    {
        if (rb) rb.constraints = RigidbodyConstraints.None;
    }

    private void StopAllManagedCoroutines()
    {
        if (stateMachineRoutine != null) StopCoroutine(stateMachineRoutine);
        if (protectionPhaseRoutine != null) StopCoroutine(protectionPhaseRoutine);
        if (chasingPhaseRoutine != null) StopCoroutine(chasingPhaseRoutine);
        if (chargeRoutine != null) StopCoroutine(chargeRoutine);
        if (damageFlashRoutine != null) StopCoroutine(damageFlashRoutine);
        if (angryCoroutine != null) StopCoroutine(angryCoroutine);
        stateMachineRoutine = null;
        protectionPhaseRoutine = null;
        chasingPhaseRoutine = null;
        chargeRoutine = null;
        damageFlashRoutine = null;
        angryCoroutine = null;
    }

    private void Play(AudioSource source)
    {
        if (source && !source.isPlaying) source.Play();
    }

    // ----------------- Centralized Rock Raising -----------------
    private void UpdateRaisingRocks()
    {
        if (raisingRocks.Count == 0) return;

        for (int i = raisingRocks.Count - 1; i >= 0; i--)
        {
            var rr = raisingRocks[i];
            if (rr.rock == null)
            {
                raisingRocks.RemoveAt(i);
                continue;
            }

            rr.elapsed += Time.deltaTime;
            if (rr.elapsed < rr.delay) continue;

            float t = (rr.elapsed - rr.delay) / rr.duration;
            t = Mathf.Clamp01(t);

            rr.rock.transform.position = Vector3.Lerp(rr.startPos, rr.targetPos, t);

            if (rr.spin)
            {
                rr.spin.spinSpeed = Mathf.Lerp(rr.spinInitial, 0f, t);
                float yRot = Mathf.Lerp(rr.startYRot, rr.startYRot + 360f, t);
                var eul = rr.rock.transform.eulerAngles;
                rr.rock.transform.eulerAngles = new Vector3(eul.x, yRot, eul.z);
            }

            if (t >= 1f)
                raisingRocks.RemoveAt(i);
        }
    }

    // ----------------- Beam Tracking -----------------
    private void UpdateBeams()
    {
        if (activeBeams.Count == 0) return;

        Vector3 bossPos = transform.position;
        for (int i = activeBeams.Count - 1; i >= 0; i--)
        {
            var b = activeBeams[i];
            if (b.beamObj == null || b.rockTransform == null)
            {
                activeBeams.RemoveAt(i);
                continue;
            }

            Vector3 dir = (bossPos - b.beamObj.transform.position);
            float sqr = dir.sqrMagnitude;
            if (sqr > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
                b.beamObj.transform.rotation = Quaternion.Slerp(b.beamObj.transform.rotation, target, Time.deltaTime * 15f);
            }

            if (b.light)
            {
                float dist = Mathf.Sqrt(sqr);
                if (Mathf.Abs(b.light.range - dist) > 0.1f)
                    b.light.range = dist;
            }
        }
    }

    // ----------------- Animation Helpers -----------------
    private void UpdateContinuousAnimationParams()
    {
        if (!animator) return;

        bool walk = (state == BossState.Chasing) && !isCharging && !inAngry;
        if (walk != lastAnimWalk)
        {
            animator.SetBool("Walk", walk);
            lastAnimWalk = walk;
        }
        if (isCharging != lastAnimCharge)
        {
            animator.SetBool("Charge", isCharging);
            lastAnimCharge = isCharging;
        }
    }

    private void TriggerAttack()
    {
        if (!animator) return;
        animator.SetTrigger("Attack");
        pendingAttackTrigger = true;
    }

    private void TriggerAngry()
    {
        if (!animator) return;
        animator.SetTrigger("Angry");
        pendingAngryTrigger = true;
    }

    // ----------------- Photon Sync -----------------
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            byte flags = 0;

            if (currentHealth != lastSentHealth) flags |= NetFlag_Health;
            if (state != lastSentState) flags |= NetFlag_State;
            if (isCharging != lastSentCharging) flags |= NetFlag_Charging;

            float angleDelta = Quaternion.Angle(transform.rotation, lastSentRotation);
            if (angleDelta > 1f) flags |= NetFlag_Rotation;

            bool animChanged = pendingAttackTrigger || pendingAngryTrigger;
            if (animChanged || lastAnimWalk != sentAnimWalkCached || lastAnimCharge != sentAnimChargeCached)
                flags |= NetFlag_Anim;

            stream.SendNext(flags);

            if ((flags & NetFlag_Health) != 0)
            {
                stream.SendNext(currentHealth);
                lastSentHealth = currentHealth;
            }
            if ((flags & NetFlag_State) != 0)
            {
                stream.SendNext((int)state);
                lastSentState = state;
            }
            if ((flags & NetFlag_Charging) != 0)
            {
                stream.SendNext(isCharging);
                lastSentCharging = isCharging;
            }
            if ((flags & NetFlag_Rotation) != 0)
            {
                stream.SendNext(transform.rotation);
                lastSentRotation = transform.rotation;
            }
            if ((flags & NetFlag_Anim) != 0)
            {
                byte animBits = 0;
                if (lastAnimWalk) animBits |= 1 << 0;
                if (lastAnimCharge) animBits |= 1 << 1;
                if (pendingAttackTrigger) animBits |= 1 << 2;
                if (pendingAngryTrigger) animBits |= 1 << 3;

                stream.SendNext(animBits);

                sentAnimWalkCached = lastAnimWalk;
                sentAnimChargeCached = lastAnimCharge;

                pendingAttackTrigger = false;
                pendingAngryTrigger = false;
            }
        }
        else
        {
            byte flags = (byte)stream.ReceiveNext();

            if ((flags & NetFlag_Health) != 0)
            {
                int h = (int)stream.ReceiveNext();
                if (h != currentHealth)
                {
                    currentHealth = h;
                    UpdateHealthUI();
                    UpdateHealthDrivenSpawns();
                }
            }
            if ((flags & NetFlag_State) != 0)
            {
                BossState newState = (BossState)(int)stream.ReceiveNext();
                if (state != newState)
                    state = newState;
            }
            if ((flags & NetFlag_Charging) != 0)
            {
                isCharging = (bool)stream.ReceiveNext();
            }
            if ((flags & NetFlag_Rotation) != 0)
            {
                Quaternion remoteRot = (Quaternion)stream.ReceiveNext();
                if (!PhotonNetwork.IsMasterClient)
                    transform.rotation = remoteRot;
            }
            if ((flags & NetFlag_Anim) != 0)
            {
                byte animBits = (byte)stream.ReceiveNext();
                bool walk = (animBits & (1 << 0)) != 0;
                bool chargeB = (animBits & (1 << 1)) != 0;
                bool attackTrig = (animBits & (1 << 2)) != 0;
                bool angryTrig = (animBits & (1 << 3)) != 0;

                if (animator)
                {
                    animator.SetBool("Walk", walk);
                    animator.SetBool("Charge", chargeB);
                    if (attackTrig) animator.SetTrigger("Attack");
                    if (angryTrig) animator.SetTrigger("Angry");
                }
            }
        }
    }

    // ----------------- Triggers (Damage / Attack Integration) -----------------
    private void OnTriggerStay(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (inAngry) return;
        if (!other.CompareTag("Player")) return;

        if (!lastProximityDamageTime.TryGetValue(other.gameObject, out float lastTime))
            lastTime = -Mathf.Infinity;

        if (Time.time - lastTime >= proximityDamageInterval)
        {
            var healthComp = other.GetComponent<PlayerHealth>();
            if (healthComp != null)
            {
                healthComp.photonView.RPC("TakeDamage", RpcTarget.AllBuffered, proximityDamage);
                lastProximityDamageTime[other.gameObject] = Time.time;
                TriggerAttack();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (lastProximityDamageTime.ContainsKey(other.gameObject))
            lastProximityDamageTime.Remove(other.gameObject);
    }

    // ----------------- Gizmos -----------------
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, aggroDistance);
        if (chargeTargetPos != Vector3.zero)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(chargeTargetPos, 0.2f);
            Gizmos.DrawLine(transform.position, chargeTargetPos);
        }
    }

    // ----------------- LEGACY (stubs kept) -----------------
    private IEnumerator RaiseRock(GameObject rock, Vector3 targetPos, float duration, float delay) { yield break; }
    private IEnumerator TrackBeamTowardsBoss(GameObject beam) { yield break; }
}

public class RockSpin : MonoBehaviour
{
    public float spinSpeed = 90f;
    private void Update()
    {
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }
}