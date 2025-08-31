using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class LevelEndTrigger : MonoBehaviour
{
    public bool requireBossDead = true;
    public bool requireAllPlayersInZone = true;

    int insideCount = 0;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c) c.isTrigger = true;
    }

    void OnEnable()
    {
        GlobalGameEvents.LevelCleared += OnLevelCleared;
    }

    void OnDisable()
    {
        GlobalGameEvents.LevelCleared -= OnLevelCleared;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        insideCount++;
        TryComplete();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        insideCount = Mathf.Max(0, insideCount - 1);
    }

    void TryComplete()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (requireBossDead && FindObjectOfType<BossController>() != null) return;

        if (requireAllPlayersInZone)
        {
            int playersInRoom = PhotonNetwork.CurrentRoom?.PlayerCount ?? 1;
            if (insideCount < playersInRoom) return;
        }

        GlobalGameEvents.EmitLevelCleared();
    }

    // ADDED: Scene reload on level complete
    void OnLevelCleared()
    {
        // Use PhotonNetwork to sync scene load for everyone. Only Master initiates.
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LoadLevel("Main Scene");
        }
    }
}