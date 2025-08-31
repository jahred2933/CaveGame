using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;
using Photon.Realtime;

// NOTE (#18): Ensure a PhotonTransformView or equivalent component handles position/rotation sync for remote clients,
// since this script disables the NavMeshAgent on non-owned instances.

public class EnemyBehavior : MonoBehaviourPun, IPunObservable
{
    public float wanderRadius = 5.0f;
    public float wanderTimer = 5.0f;
    public float detectionRadius = 10.0f;
    public float fleeDistance = 15.0f;
    public float fleeSpeed = 7.0f;
    public LayerMask playerLayer;
    public float minDistanceToOtherDeer = 2.0f;
    public float checkInterval = 0.5f;
    public int maxWanderAttempts = 5;

    public float fleeRepathInterval = 1.0f;
    public int playerBufferSize = 8;
    public int deerBufferSize = 8;

    private NavMeshAgent navMeshAgent;
    private Transform target;
    private bool isScared;
    private float timer;
    private Vector3 wanderDestination;
    private Animator enemyAnimator;
    private Coroutine periodicUpdateCoroutine;

    // Animation sync variables
    private float syncedVelocity;
    private bool syncedIsScared;

    private float baseSpeed;
    private float nextFleeRepathTime;

    private Collider[] playerColliders;
    private Collider[] deerColliders;

    private int deerLayerMask;

    private const float destinationEpsilon = 0.05f;

    private NavMeshHit navHit;
    private NavMeshHit fallbackNavHit;

    // Added: cache WaitForSeconds to avoid allocation each loop if interval stays same
    private WaitForSeconds cachedCheckWait;
    private float lastCachedInterval = -1f;

    void Start()
    {
        enemyAnimator = GetComponent<Animator>();
        navMeshAgent = GetComponent<NavMeshAgent>();

        playerColliders = new Collider[playerBufferSize];
        deerColliders = new Collider[deerBufferSize];

        deerLayerMask = LayerMask.GetMask("Deer");

        if (!photonView.IsMine)
        {
            if (navMeshAgent != null)
                navMeshAgent.enabled = false;
            return;
        }

        baseSpeed = navMeshAgent.speed;

        timer = 0f;
        wanderDestination = ChooseRandomDestination();
        navMeshAgent.SetDestination(wanderDestination);
        navMeshAgent.isStopped = false;

        UpdateCachedWait();
        periodicUpdateCoroutine = StartCoroutine(PeriodicUpdate());
    }

    void Update()
    {
        if (enemyAnimator != null)
        {
            float velMag = 0f;
            if (navMeshAgent != null && navMeshAgent.enabled)
                velMag = navMeshAgent.velocity.magnitude;
            UpdateAnimator(velMag, isScared);
        }

        if (!photonView.IsMine)
            return;

        if (!isScared)
        {
            Wander();
        }

        // If interval changed at runtime, refresh cached WaitForSeconds
        if (Mathf.Abs(checkInterval - lastCachedInterval) > 0.0001f)
            UpdateCachedWait();
    }

    private void UpdateCachedWait()
    {
        lastCachedInterval = checkInterval;
        cachedCheckWait = new WaitForSeconds(checkInterval);
    }

    IEnumerator PeriodicUpdate()
    {
        while (true)
        {
            // Use cached wait object
            yield return cachedCheckWait;

            if (!photonView.IsMine)
                continue;

            bool playerDetected = DetectPlayer();

            if (playerDetected)
            {
                if (!isScared)
                {
                    isScared = true;
                    FleeFromPlayer();
                }
                else if (Time.time >= nextFleeRepathTime)
                {
                    FleeFromPlayer();
                }
            }
            else
            {
                target = null;
                if (isScared)
                {
                    isScared = false;
                    navMeshAgent.speed = baseSpeed;
                }
            }
        }
    }

    bool DetectPlayer()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, playerColliders, playerLayer);
        for (int i = 0; i < count; i++)
        {
            Collider hit = playerColliders[i];
            if (hit != null && hit.CompareTag("Player"))
            {
                target = hit.transform;
                return true;
            }
        }
        return false;
    }

    void FleeFromPlayer()
    {
        if (target == null)
            return;

        Vector3 fleeDirection = (transform.position - target.position).normalized;
        fleeDirection.y = 0f;
        Vector3 fleePosition = transform.position + fleeDirection * fleeDistance;
        navMeshAgent.SetDestination(fleePosition);
        navMeshAgent.speed = fleeSpeed;
        nextFleeRepathTime = Time.time + fleeRepathInterval;
    }

    void Wander()
    {
        if (navMeshAgent.isStopped)
            return;

        timer += Time.deltaTime;

        float stoppingThreshold = navMeshAgent.stoppingDistance + destinationEpsilon;
        float stoppingThresholdSqr = stoppingThreshold * stoppingThreshold;
        float distSqr = (transform.position - wanderDestination).sqrMagnitude;

        if (timer >= wanderTimer || distSqr <= stoppingThresholdSqr)
        {
            wanderDestination = ChooseRandomDestination();
            navMeshAgent.SetDestination(wanderDestination);
            navMeshAgent.isStopped = false;
            timer = 0f;
        }
    }

    Vector3 ChooseRandomDestination()
    {
        int halfAttempts = Mathf.Max(1, maxWanderAttempts / 2);

        for (int attempt = 0; attempt < maxWanderAttempts; attempt++)
        {
            Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
            randomDirection.y = 0f;
            randomDirection += transform.position;

            int deerCount = Physics.OverlapSphereNonAlloc(randomDirection, minDistanceToOtherDeer, deerColliders, deerLayerMask);
            if (deerCount == 0)
            {
                if (NavMesh.SamplePosition(randomDirection, out navHit, wanderRadius, NavMesh.AllAreas))
                {
                    return navHit.position;
                }
            }

            if (attempt >= halfAttempts && attempt > 0)
                break;
        }

        Vector3 fallbackDirection = Random.insideUnitSphere * wanderRadius;
        fallbackDirection.y = 0f;
        fallbackDirection += transform.position;

        NavMesh.SamplePosition(fallbackDirection, out fallbackNavHit, wanderRadius, NavMesh.AllAreas);
        return fallbackNavHit.position;
    }

    [PunRPC]
    void SetDestination(Vector3 destination)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;
        navMeshAgent.SetDestination(destination);
    }

    [PunRPC]
    void SetAgentActive(bool active)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;
        navMeshAgent.isStopped = !active;
    }

    [PunRPC]
    void SetSpeed(float speed)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;
        navMeshAgent.speed = speed;
        if (!isScared)
            baseSpeed = speed;
    }

    [PunRPC]
    void SetScared(bool scared)
    {
        isScared = scared;
        if (!isScared && navMeshAgent != null && navMeshAgent.enabled)
        {
            navMeshAgent.speed = baseSpeed;
        }
    }

    void UpdateAnimator(float velocityMagnitude, bool scared)
    {
        if (photonView.IsMine)
        {
            enemyAnimator.SetBool("IsWalking", velocityMagnitude > 0.1f);
            enemyAnimator.SetBool("IsScared", scared);
        }
        else
        {
            enemyAnimator.SetBool("IsWalking", syncedVelocity > 0.1f);
            enemyAnimator.SetBool("IsScared", syncedIsScared);
        }
    }

    // Added delta-check flags to reduce unnecessary network sends
    private float lastSentVel = -1f;
    private bool lastSentScared = false;

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            float vel = (navMeshAgent != null && navMeshAgent.enabled) ? navMeshAgent.velocity.magnitude : 0f;
            bool scared = isScared;
            byte flags = 0;
            if (Mathf.Abs(vel - lastSentVel) > 0.05f) flags |= 1;
            if (scared != lastSentScared) flags |= 2;
            stream.SendNext(flags);
            if ((flags & 1) != 0)
            {
                stream.SendNext(vel);
                lastSentVel = vel;
            }
            if ((flags & 2) != 0)
            {
                stream.SendNext(scared);
                lastSentScared = scared;
            }
        }
        else
        {
            byte flags = (byte)stream.ReceiveNext();
            if ((flags & 1) != 0)
                syncedVelocity = (float)stream.ReceiveNext();
            if ((flags & 2) != 0)
                syncedIsScared = (bool)stream.ReceiveNext();
        }
    }

    void OnDestroy()
    {
        if (periodicUpdateCoroutine != null)
        {
            StopCoroutine(periodicUpdateCoroutine);
        }
    }
}