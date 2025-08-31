using UnityEngine;
using Photon.Pun;

public class ProjectileDamage : MonoBehaviour
{
    public int damagePerShot = 10;
    public LayerMask damageableLayers;

    public LayerMask enemyLayer;
    public LayerMask bossLayer;
    public LayerMask rockLayer;

    public GameObject hitEffectPrefab;
    public float lifetime = 5f;

    private float timer = 0f;

    void OnEnable()
    {
        // Reset timer when reused via pool to avoid premature return (performance correctness)
        timer = 0f;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            ReturnToPool();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        int layer = collision.gameObject.layer;

        if ((damageableLayers & (1 << layer)) != 0)
        {
            if (hitEffectPrefab != null)
            {
                ContactPoint contact = collision.contacts[0];
                Instantiate(hitEffectPrefab, contact.point, Quaternion.identity);
            }

            PhotonView targetPhotonView = collision.gameObject.GetComponent<PhotonView>();

            if (targetPhotonView != null)
            {
                if ((enemyLayer & (1 << layer)) != 0)
                {
                    targetPhotonView.RPC("TakeDamage", RpcTarget.All, damagePerShot);
                }
                else if ((bossLayer & (1 << layer)) != 0)
                {
                    targetPhotonView.RPC("TakeDamage", RpcTarget.All, damagePerShot);
                }
                else if ((rockLayer & (1 << layer)) != 0)
                {
                    targetPhotonView.RPC("RequestDamage", RpcTarget.MasterClient, damagePerShot);
                }
            }

            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        ProjectilePool.Instance.ReturnProjectile(gameObject);
    }
}