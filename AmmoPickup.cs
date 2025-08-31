using UnityEngine;
using Photon.Pun;

public class AmmoPickup : MonoBehaviourPunCallbacks
{
    public int ammoAmount = 10;
    public float pickupRadius = 2f;

    private bool isPickedUp = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isPickedUp) return;

        Player player = other.GetComponent<Player>();
        if (player == null)
        {
            player = other.GetComponentInParent<Player>();
        }

        // Only allow the local player to add ammo
        PhotonView playerView = other.GetComponentInParent<PhotonView>();
        if (player != null && playerView != null && playerView.IsMine)
        {
            player.AddAmmo(ammoAmount);
            isPickedUp = true;
            photonView.RPC("DisablePickup", RpcTarget.All);
        }
    }

    [PunRPC]
    private void DisablePickup()
    {
        gameObject.SetActive(false);
    }
}

