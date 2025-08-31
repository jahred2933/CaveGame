using UnityEngine;
using Photon.Pun;

public class Bullet : MonoBehaviour
{
    public float speed = 10f;
    public int damageAmount = 10;

    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        Destroy(gameObject, 3f); // Destroy bullet after 3 seconds
    }

    private void FixedUpdate()
    {
        Vector3 forward = transform.forward;
        rb.MovePosition(rb.position + forward * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
            if (playerHealth != null && playerHealth.photonView.IsMine)
            {
                playerHealth.photonView.RPC("TakeDamage", RpcTarget.All, damageAmount);
            }

            Destroy(gameObject); // Destroy the bullet after hitting the player
        }
    }
}

