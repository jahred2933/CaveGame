using Photon.Pun;
using UnityEngine;

public class HomingMissile : MonoBehaviourPunCallbacks, IPunInstantiateMagicCallback
{
    public float speed = 10f;
    public float rotateSpeed = 5f;
    public LayerMask targetMask;
    public float detectionRange = 10f;
    public int damageAmount = 10;
    public float targetSearchInterval = 1f;

    public int maxHealth = 20;
    private int currentHealth;

    [Header("Points")]
    [SerializeField] private int pointsOnKill = 5; // small reward for destroying missiles

    private Transform target;
    private float targetSearchTimer;

    private static readonly Collider[] targetBuffer = new Collider[16];

    private int baseDamage;

    void Awake()
    {
        baseDamage = damageAmount;
    }

    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        Initialize();
    }

    void OnEnable()
    {
        if (currentHealth <= 0)
            currentHealth = maxHealth;
        if (target == null)
        {
            targetSearchTimer = 0f;
        }
    }

    public void Initialize()
    {
        currentHealth = maxHealth;
        targetSearchTimer = targetSearchInterval;

        // Scale missile damage by current enemy damage multiplier
        float mult = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.EnemyDamageMult : 1f;
        damageAmount = Mathf.Max(1, Mathf.RoundToInt(baseDamage * mult));

        FindTargetNonAlloc();
    }

    void Update()
    {
        if (!photonView.IsMine)
            return;

        targetSearchTimer -= Time.deltaTime;
        if (target == null || targetSearchTimer <= 0f)
        {
            FindTargetNonAlloc();
            targetSearchTimer = targetSearchInterval;
        }

        if (target != null)
        {
            MoveTowardsTarget();
        }
    }

    void MoveTowardsTarget()
    {
        Vector3 direction = (target.position - transform.position).normalized;
        float angleDiff = Vector3.Angle(transform.forward, direction);

        if (angleDiff > 1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), rotateSpeed * Time.deltaTime);
        }

        transform.Translate(Vector3.forward * speed * Time.deltaTime, Space.Self);
    }

    void FindTargetNonAlloc()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRange, targetBuffer, targetMask);
        float closestDistSqr = float.MaxValue;
        Transform closest = null;

        for (int i = 0; i < count; i++)
        {
            Collider c = targetBuffer[i];
            if (c == null) continue;
            float dSqr = (c.transform.position - transform.position).sqrMagnitude;
            if (dSqr < closestDistSqr)
            {
                closestDistSqr = dSqr;
                closest = c.transform;
            }
        }
        target = closest;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine)
            return;

        if (other.CompareTag("Player"))
        {
            PhotonView targetPV = other.GetComponent<PhotonView>() ?? other.GetComponentInParent<PhotonView>();
            if (targetPV != null)
            {
                targetPV.RPC("TakeDamage", targetPV.Owner, damageAmount);
            }

            PhotonNetwork.Destroy(gameObject);
        }
    }

    [PunRPC]
    public void TakeDamage(int damage, PhotonMessageInfo info)
    {
        if (!photonView.IsMine)
            return;

        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            // Award a small amount of points to the attacker
            int killerActor = (info.Sender != null) ? info.Sender.ActorNumber : -1;
            GlobalGameEvents.EmitEnemyKilled(killerActor, pointsOnKill);

            PhotonNetwork.Destroy(gameObject);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}