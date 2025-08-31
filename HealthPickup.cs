using System.Collections;
using UnityEngine;
using Photon.Pun;

public class HealthPickup : MonoBehaviourPunCallbacks, IPunObservable
{
    public int healAmount = 50;
    public float respawnTime = 10f;
    public GameObject pickupEffect;
    public float effectDuration = 3f;

    private bool isPickedUp = false;
    private Renderer pickupRenderer;
    private Collider pickupCollider;
    private ParticleSystem pickupParticles;

    void Start()
    {
        pickupRenderer = GetComponent<Renderer>();
        pickupCollider = GetComponent<Collider>();

        if (pickupEffect != null)
        {
            pickupParticles = pickupEffect.GetComponent<ParticleSystem>();
            pickupEffect.SetActive(false);
        }
        else
        {
            Debug.LogError("Pickup effect is not assigned.");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isPickedUp) return;

        if (other.CompareTag("Player"))
        {
            PhotonView otherPV = other.GetComponent<PhotonView>();
            if (otherPV == null)
                otherPV = other.GetComponentInParent<PhotonView>();

            if (otherPV != null && otherPV.IsMine)
            {
                if (other.TryGetComponent<PlayerHealth>(out var playerHealth))
                {
                    playerHealth.Heal(healAmount);
                }
                photonView.RPC("Pickup", RpcTarget.All);
            }
        }
    }

    [PunRPC]
    void Pickup()
    {
        if (isPickedUp) return;

        isPickedUp = true;
        if (pickupRenderer != null) pickupRenderer.enabled = false;
        if (pickupCollider != null) pickupCollider.enabled = false;

        if (pickupEffect != null)
        {
            StartCoroutine(PlayEffectAndRespawn());
        }
        else
        {
            StartCoroutine(RespawnAfterDelay());
        }
    }

    IEnumerator PlayEffectAndRespawn()
    {
        pickupEffect.SetActive(true);
        if (pickupParticles != null)
        {
            pickupParticles.Play();
            yield return new WaitForSeconds(effectDuration);
            pickupParticles.Stop();
        }
        pickupEffect.SetActive(false);
        float remaining = Mathf.Max(0f, respawnTime - effectDuration);
        yield return new WaitForSeconds(remaining);
        ResetPickup();
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnTime);
        ResetPickup();
    }

    void ResetPickup()
    {
        isPickedUp = false;
        if (pickupRenderer != null) pickupRenderer.enabled = true;
        if (pickupCollider != null) pickupCollider.enabled = true;
    }

    // Fixed: Use !stream.IsWriting instead of stream.IsReading (robustness)
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(isPickedUp);
        }
        else
        {
            bool newVal = (bool)stream.ReceiveNext();
            if (newVal != isPickedUp)
            {
                isPickedUp = newVal;
                if (isPickedUp)
                {
                    if (pickupRenderer != null) pickupRenderer.enabled = false;
                    if (pickupCollider != null) pickupCollider.enabled = false;
                }
                else
                {
                    if (pickupRenderer != null) pickupRenderer.enabled = true;
                    if (pickupCollider != null) pickupCollider.enabled = true;
                }
            }
        }
    }
}