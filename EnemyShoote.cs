using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;
using TMPro;

public class EnemyAI : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Enemy Settings")]
    [SerializeField] private float shootInterval = 2f;
    [SerializeField] private float shootSpeed = 10f;
    [SerializeField] private float visualRange = 10f;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private int startingHealth = 100;

    [Header("References")]
    [SerializeField] private Transform bulletSpawnPoint;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private GameObject deathParticlesPrefab;
    [SerializeField] private GameObject enemyVisual;
    [SerializeField] private GameObject missilePrefab;

    [Header("AI Settings")]
    [SerializeField] private LayerMask obstacleMask; // Set this to what your walls/obstacles use in the Inspector

    private float lastShootTime = 0f;
    private List<Transform> players = new List<Transform>();
    private Transform closestPlayer = null;
    private float rotationSpeed = 5f;
    private NavMeshAgent navMeshAgent;
    private int currentHealth;
    private bool isDying = false;
    private bool visualActive = false;

    // --------------- Added (Performance Improvements) ---------------
    [Header("Optimization")]
    [SerializeField] private float playerRefreshInterval = 0.5f; // how often to refresh player list
    [SerializeField] private float pathUpdateInterval = 0.4f;    // how often to update destination
    [SerializeField] private float destinationRepathThreshold = 0.75f; // min distance change before path update

    private float nextPlayerRefreshTime;
    private float nextPathUpdateTime;
    private Vector3 lastDestination = Vector3.positiveInfinity;
    private static readonly List<Transform> sharedPlayerCache = new(); // shared cache to reduce allocations
    private static float nextSharedCacheRefreshTime;
    private const float sharedCacheRefreshInterval = 0.5f;
    // ---------------------------------------------------------------

    private void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        if (navMeshAgent != null)
        {
            navMeshAgent.speed = moveSpeed;
        }

        currentHealth = startingHealth;
        UpdateHealthText();

        if (enemyVisual != null)
        {
            enemyVisual.SetActive(false);
            visualActive = false;
        }

        RefreshPlayerCache(force: true);

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("SetInitialHealth", RpcTarget.AllBuffered, currentHealth);
        }
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        // Refresh shared player cache at interval
        if (Time.time >= nextSharedCacheRefreshTime)
        {
            RefreshSharedPlayerCache();
        }

        // Copy from shared cache to local list occasionally
        if (Time.time >= nextPlayerRefreshTime)
        {
            RefreshPlayerCache();
        }

        closestPlayer = FindClosestVisiblePlayer();

        bool shouldBeActive = (closestPlayer != null);
        if (enemyVisual != null)
        {
            if (shouldBeActive && !enemyVisual.activeSelf)
            {
                enemyVisual.SetActive(true);
                visualActive = true;
            }
            else if (!shouldBeActive && enemyVisual.activeSelf)
            {
                enemyVisual.SetActive(false);
                visualActive = false;
            }
        }

        if (closestPlayer == null)
        {
            if (navMeshAgent != null && navMeshAgent.hasPath)
                navMeshAgent.ResetPath();
            return;
        }

        RotateTowardsClosestPlayer();
        MoveTowardsClosestPlayer();

        if (Time.time > lastShootTime + shootInterval)
        {
            lastShootTime = Time.time;
            photonView.RPC("Shoot", RpcTarget.AllViaServer, bulletSpawnPoint.position, closestPlayer.position);
        }
    }

    // Refresh local list from shared cache
    private void RefreshPlayerCache(bool force = false)
    {
        if (!force && Time.time < nextPlayerRefreshTime) return;
        nextPlayerRefreshTime = Time.time + playerRefreshInterval;

        players.Clear();
        for (int i = 0; i < sharedPlayerCache.Count; i++)
        {
            var t = sharedPlayerCache[i];
            if (t != null)
                players.Add(t);
        }
    }

    // Static shared cache refresh
    private static void RefreshSharedPlayerCache()
    {
        nextSharedCacheRefreshTime = Time.time + sharedCacheRefreshInterval;
        sharedPlayerCache.Clear();
        GameObject[] objs = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < objs.Length; i++)
        {
            sharedPlayerCache.Add(objs[i].transform);
        }
    }

    private Transform FindClosestVisiblePlayer()
    {
        Transform closest = null;
        float closestDistanceSqr = visualRange * visualRange;
        Vector3 myPos = transform.position;

        for (int i = 0; i < players.Count; i++)
        {
            Transform player = players[i];
            if (player == null) continue;

            Vector3 diff = player.position - myPos;
            float distSqr = diff.sqrMagnitude;
            if (distSqr < closestDistanceSqr)
            {
                if (HasLineOfSight(player))
                {
                    closestDistanceSqr = distSqr;
                    closest = player;
                }
            }
        }
        return closest;
    }

    // Improved line-of-sight using Physics.Linecast
    private bool HasLineOfSight(Transform player)
    {
        Vector3 origin = transform.position + Vector3.up * 1.0f;
        Vector3 target = player.position + Vector3.up * 1.0f;
        // Returns true if NO obstacle in between
        return !Physics.Linecast(origin, target, obstacleMask);
    }

    private void RotateTowardsClosestPlayer()
    {
        if (closestPlayer == null)
            return;

        Vector3 direction = closestPlayer.position - transform.position;
        direction.y = 0;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    private void MoveTowardsClosestPlayer()
    {
        if (navMeshAgent == null || closestPlayer == null)
            return;

        // Rate limit path updates and only if player moved enough
        if (Time.time >= nextPathUpdateTime)
        {
            nextPathUpdateTime = Time.time + pathUpdateInterval;

            Vector3 targetPos = closestPlayer.position;
            if ((targetPos - lastDestination).sqrMagnitude >= destinationRepathThreshold * destinationRepathThreshold)
            {
                navMeshAgent.SetDestination(targetPos);
                lastDestination = targetPos;
            }
        }
    }

    [PunRPC]
    private void Shoot(Vector3 spawnPoint, Vector3 targetPosition)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            if (missilePrefab == null) return;
            GameObject missile = PhotonNetwork.Instantiate(missilePrefab.name, spawnPoint, Quaternion.identity);
            if (missile != null)
            {
                Vector3 direction = (targetPosition - spawnPoint).normalized;
                Rigidbody rb = missile.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = direction * shootSpeed;
                }
                // Scale missile damage using EnemyDamageMult
                if (missile.TryGetComponent<HomingMissile>(out var hm))
                    hm.Initialize();
                StartCoroutine(DestroyMissileAfterDelay(missile, 5f));
            }
        }
    }

    [PunRPC]
    public void TakeDamage(int damageAmount)
    {
        currentHealth -= damageAmount;
        UpdateHealthText();

        if (currentHealth <= 0 && !isDying)
        {
            isDying = true;
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
                photonView.RPC("Die", RpcTarget.AllBuffered);
            else
                Die();
        }
    }

    [PunRPC]
    private void Die()
    {
        if (deathParticlesPrefab != null)
        {
            GameObject dp = Instantiate(deathParticlesPrefab, transform.position, Quaternion.identity);
            ParticleSystem ps = dp.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Play();
                float lifetime = ps.main.duration + ps.main.startLifetime.constantMax;
                Destroy(dp, lifetime);
            }
        }
        PhotonNetwork.Destroy(gameObject);
    }

    [PunRPC]
    public void SetInitialHealth(int health)
    {
        currentHealth = health;
        UpdateHealthText();
    }

    private void UpdateHealthText()
    {
        if (healthText != null)
            healthText.text = "Health: " + currentHealth;
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(currentHealth);
            stream.SendNext(visualActive);
        }
        else
        {
            transform.position = (Vector3)stream.ReceiveNext();
            transform.rotation = (Quaternion)stream.ReceiveNext();
            currentHealth = (int)stream.ReceiveNext();
            bool vis = (bool)stream.ReceiveNext();
            if (enemyVisual != null && enemyVisual.activeSelf != vis)
            {
                enemyVisual.SetActive(vis);
            }
            UpdateHealthText();
        }
    }

    private IEnumerator DestroyMissileAfterDelay(GameObject missile, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (missile != null)
        {
            PhotonNetwork.Destroy(missile);
        }
    }
}