using UnityEngine;

public class MusicTrigger : MonoBehaviour
{
    public AudioSource musicAudioSource; // Reference to the AudioSource that will play the music

    private bool hasPlayerEntered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !hasPlayerEntered)
        {
            hasPlayerEntered = true;
            if (musicAudioSource != null)
            {
                musicAudioSource.Play();
            }
            else
            {
                Debug.LogError("No AudioSource assigned!");
            }
        }
    }
}

