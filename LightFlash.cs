using UnityEngine;

public class FlashingLight : MonoBehaviour
{
    public float flashInterval = 1.0f; // Time interval for the light to flash (in seconds)
    private Light lightComponent;

    void Start()
    {
        lightComponent = GetComponent<Light>();

        // Start the flashing sequence
        InvokeRepeating("ToggleLight", 0f, flashInterval);
    }

    void ToggleLight()
    {
        // Toggle the light on and off
        lightComponent.enabled = !lightComponent.enabled;
    }
}
