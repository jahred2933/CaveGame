using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    public float updateInterval = 0.5f; // Update interval in seconds

    public TMP_Text fpsText; // Reference to TextMeshPro Text component

    private float accum = 0.0f; // FPS accumulated over the interval
    private int frames = 0; // Frames drawn over the interval
    private float timeLeft; // Left time for current interval

    public void SetFpsText(TMP_Text text)
    {
        fpsText = text;
    }

    private void Start()
    {
        timeLeft = updateInterval;
    }

    private void Update()
    {
        timeLeft -= Time.deltaTime;
        accum += Time.timeScale / Time.deltaTime;
        frames++;

        // Interval ended - update GUI text and start new interval
        if (timeLeft <= 0.0)
        {
            // Display FPS value
            float fps = accum / frames;
            if (fpsText)
            {
                fpsText.text = string.Format("FPS: {0:F2}", fps);
            }

            // Reset values for next interval
            timeLeft = updateInterval;
            accum = 0.0f;
            frames = 0;
        }
    }
}
