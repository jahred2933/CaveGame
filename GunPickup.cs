using UnityEngine;
using Photon.Pun;

public class PickupItemScript : MonoBehaviourPun
{
    // Assign this in the Inspector to set which object to disable when picked up
    public GameObject objectToDisable;

    // This method disables the pickup and the specified object for the player who picked it up.
    public void Local_DestroyPickup()
    {
        gameObject.SetActive(false);

        // Disable the specified object if assigned
        if (objectToDisable != null)
        {
            objectToDisable.SetActive(false);
        }
    }

    // This RPC disables the pickup and the specified object for all players in the room.
    [PunRPC]
    public void RPC_DestroyPickup()
    {
        gameObject.SetActive(false);

        if (objectToDisable != null)
        {
            objectToDisable.SetActive(false);
        }
    }
}