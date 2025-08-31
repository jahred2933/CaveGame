using UnityEngine;

public class PickupItemScript : MonoBehaviour
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
}