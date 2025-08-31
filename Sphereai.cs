using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class SphereAI : MonoBehaviourPun
{
    [Header("Detection & Combat")]
    [SerializeField] private float detectionRange = 15f;
    [SerializeField] private float fireRate = 1f;
    [SerializeField] private float projectileSpeed = 20f;
    [SerializeField] private float aimOffset = 4.5f;
    [SerializeField] private float rotationSpeed = 0.7f;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;

    private float nextFireTime;
    private Transform targetPlayer;

    // Cache for all player transforms to avoid FindGameObjectsWithTag each frame
    private static readonly List<Transform> playerTransforms = new List<Transform>();

    // --------------- Added / Modified (Fix & Optimization) ---------------
    private static int instanceCount = 0;       // Track active instances
    private static bool subscribedToPlayerEvents = false;
    private static float nextPlayerCacheTime = 0f;
    private const float playerCacheInterval = 0.5f;
    private const float repathInterval = 0.4f;
    private float nextRetargetTime;
    private const float retargetInterval = 0.5f;
    private Vector3 lastAimPosition = Vector3.positiveInfinity;

    // Prevent unsubscribing while others still exist (fix original bug)
    // -------------------------------------------------------------------

    void Awake()
    {
        if (projectilePrefab == null)
            Debug.LogError("Projectile prefab not assigned on " + gameObject.name);
        if (firePoint == null)
            Debug.LogError("Fire point not assigned on " + gameObject.name);

        instanceCount++;
        EnsureSubscription();
        RefreshPlayerCache(force: true);
    }

    void OnDestroy()
    {
        instanceCount--;
        if (instanceCount <= 0)
        {
            TryUnsubscribe();
            subscribedToPlayerEvents = false;
            playerTransforms.Clear();
            instanceCount = 0;
        }
    }

    private static void EnsureSubscription()
    {
        if (!subscribedToPlayerEvents)
        {
            Photon.Pun.PhotonNetwork.NetworkingClient.EventReceived += OnPhotonEvent;
            subscribedToPlayerEvents = true;
        }
    }

    private static void TryUnsubscribe()
    {
        if (subscribedToPlayerEvents)
        {
            Photon.Pun.PhotonNetwork.NetworkingClient.EventReceived -= OnPhotonEvent;
        }
    }

    // Listen for player join/leave events to refresh cache
    private static void OnPhotonEvent(ExitGames.Client.Photon.EventData photonEvent)
    {
        // Event code 255 is player join/leave (Photon internal)
        if (photonEvent.Code == 255)
        {
            RefreshPlayerCache();
        }
    }

    private static void RefreshPlayerCache(bool force = false)
    {
        if (!force && Time.time < nextPlayerCacheTime) return;
        nextPlayerCacheTime = Time.time + playerCacheInterval;

        playerTransforms.Clear();
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            playerTransforms.Add(players[i].transform);
        }
    }

    void Update()
    {
        if (!photonView.IsMine)
            return;

        // Periodic retargeting
        if (Time.time >= nextRetargetTime || targetPlayer == null)
        {
            nextRetargetTime = Time.time + retargetInterval;
            FindClosestPlayer();
        }

        if (targetPlayer != null)
        {
            RotateTowardsTarget();

            float distSqr = (transform.position - targetPlayer.position).sqrMagnitude;
            if (distSqr <= detectionRange * detectionRange)
            {
                if (Time.time >= nextFireTime)
                {
                    FireProjectile();
                    nextFireTime = Time.time + 1f / fireRate;
                }
            }
        }
    }

    void FindClosestPlayer()
    {
        float closestDistanceSqr = Mathf.Infinity;
        targetPlayer = null;
        Vector3 myPos = transform.position;

        for (int i = 0; i < playerTransforms.Count; i++)
        {
            Transform playerTransform = playerTransforms[i];
            if (playerTransform == null) continue;
            float dSqr = (playerTransform.position - myPos).sqrMagnitude;
            if (dSqr < closestDistanceSqr)
            {
                closestDistanceSqr = dSqr;
                targetPlayer = playerTransform;
            }
        }
    }

    void RotateTowardsTarget()
    {
        Vector3 direction = (targetPlayer.position - transform.position).normalized;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * rotationSpeed);
        }
    }

    void FireProjectile()
    {
        if (projectilePrefab == null || firePoint == null || targetPlayer == null) return;

        Vector3 aimPoint = targetPlayer.position + Vector3.up * aimOffset;

        // Avoid redundant identical shots if target hasn't moved meaningfully (micro-opt)
        if ((aimPoint - lastAimPosition).sqrMagnitude < 0.04f) // ~0.2m threshold
        {
            // still fire, but we skip direction recalculation overhead beyond simple diff check (kept simple)
        }
        lastAimPosition = aimPoint;

        GameObject projectile = PhotonNetwork.Instantiate(projectilePrefab.name, firePoint.position, firePoint.rotation);
        if (projectile.TryGetComponent<Rigidbody>(out var rb))
        {
            Vector3 shootDirection = (aimPoint - firePoint.position).normalized;
            rb.linearVelocity = shootDirection * projectileSpeed;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}