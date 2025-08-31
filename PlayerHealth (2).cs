using UnityEngine;
using TMPro;
using Photon.Pun;
using UnityEngine.UI;
using System;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviourPunCallbacks, IPunObservable
{
    public int maxHealth = 100;
    [HideInInspector] public int currentHealth;
    public float regenRate = 2f;          // interval seconds between +1 heal
    public float regenDelay = 3f;
    public TMP_Text healthText;

    [Header("Damage Overlay Settings")]
    public Image damageOverlay;

    [Range(0, 255)] public int overlayDamageAlphaAdd = 32;
    [Range(0, 255)] public int overlayDamageAlphaMax = 32;
    public float overlayFadeOutSpeed = 3.5f;
    [Range(0, 255)] public int overlayLowHealthAlpha = 179;
    [Range(1, 100)] public int lowHealthThresholdPercent = 20;
    [Range(0, 255)] public int overlayPulseRange = 51;
    public float overlayPulseSpeed = 3.5f;

    [Header("Respawn")]
    [Tooltip("Delay before respawning after death (only if the player has lives).")]
    public float respawnDelaySeconds = 3f;

    [Header("Respawn Invincibility")]
    [Tooltip("Seconds of invincibility after a mid-level respawn.")]
    public float respawnInvincibilitySeconds = 2f;

    private float overlayAlphaAccumulator = 0f;
    private float regenTimer;
    private bool isGameOver = false;
    private float pulseTime = 0f;

    // Removed: cached respawn points (no longer used)

    // NEW: track death point + invincibility
    private Vector3 lastDeathPosition;
    private bool hasDeathPosition = false;
    private bool isInvincible = false;
    private float invincibleTimer = 0f;

    public event Action<int, int> OnHealthChanged;

    public float HealthPercent => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;

    private void Start()
    {
        if (photonView.IsMine)
        {
            currentHealth = maxHealth;
            UpdateHealthUI();
            if (damageOverlay != null) SetOverlayAlpha(0);

            // Removed: RespawnPoint caching
        }
    }

    [PunRPC]
    public void TakeDamage(int damageAmount)
    {
        if (photonView.IsMine && !isGameOver)
        {
            if (damageAmount <= 0) return;
            if (isInvincible) return; // NEW: ignore damage while invincible

            // Use Owner.ActorNumber (PUN2)
            GlobalGameEvents.EmitPlayerTookDamage(photonView.Owner.ActorNumber, damageAmount);

            currentHealth -= damageAmount;
            if (currentHealth < 0) currentHealth = 0;

            if (damageOverlay != null)
            {
                overlayAlphaAccumulator += overlayDamageAlphaAdd;
                overlayAlphaAccumulator = Mathf.Clamp(overlayAlphaAccumulator, 0, overlayDamageAlphaMax);
            }

            UpdateHealthUI();

            if (currentHealth <= 0)
            {
                GlobalGameEvents.EmitPlayerDied(photonView.Owner.ActorNumber);

                // Remember where we died (for same-spot respawn if allowed)
                hasDeathPosition = true;
                lastDeathPosition = transform.position;

                // Consume a life if available; otherwise, stay down until level finish (or level reset in solo by your level manager).
                bool canRespawn = PlayerSpawner.Instance == null || PlayerSpawner.Instance.TryConsumeLifeOnDeath(photonView.Owner.ActorNumber);
                if (canRespawn)
                {
                    // Wait a few seconds, then respawn where we died (not at a respawn point).
                    StartCoroutine(RespawnAfterDelay());
                }
                else
                {
                    // No lives left: wait for level finish to be revived at the starting spawn via FullRestoreAtStart.
                    isGameOver = true;
                }
            }

            regenTimer = regenDelay;
        }
    }

    [PunRPC]
    public void Heal(int healAmount)
    {
        if (photonView.IsMine && !isGameOver)
        {
            if (healAmount <= 0) return;
            int previous = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + healAmount);
            if (currentHealth != previous)
                UpdateHealthUI();
        }
    }

    // Call this on level completion to fully restore and move the player to the starting spawn (Launcher/PlayerSpawner start).
    // This should be invoked for ALL players (dead or alive).
    [PunRPC]
    public void FullRestoreAtStart()
    {
        if (!photonView.IsMine) return;

        currentHealth = maxHealth;
        isGameOver = false;
        UpdateHealthUI();
        if (damageOverlay != null) SetOverlayAlpha(0);
        overlayAlphaAccumulator = 0f;
        pulseTime = 0f;
        regenTimer = regenDelay;

        // Reset death spot and invincibility (fresh level start)
        hasDeathPosition = false;
        isInvincible = false;
        invincibleTimer = 0f;

        // Preferred: use the defined starting spawn from your PlayerSpawner/Launcher.
        Transform sp = (PlayerSpawner.Instance != null) ? PlayerSpawner.Instance.spawnPoint : null;

        // No RespawnPoint fallback anymore
        if (sp != null)
        {
            transform.position = sp.position;
            transform.rotation = sp.rotation;
        }
    }

    [PunRPC]
    public void FullRestore()
    {
        if (!photonView.IsMine) return;
        currentHealth = maxHealth;
        isGameOver = false;
        UpdateHealthUI();
        if (damageOverlay != null) SetOverlayAlpha(0);
        overlayAlphaAccumulator = 0f;
        pulseTime = 0f;
        regenTimer = regenDelay;

        // No teleport here; this is a heal-only restore.
        // Reset death spot (this is a full restore, not a mid-death respawn)
        hasDeathPosition = false;
        isInvincible = false;
        invincibleTimer = 0f;
    }

    public bool IsAlive() => currentHealth > 0 && !isGameOver;

    private void Update()
    {
        if (photonView.IsMine && !isGameOver)
        {
            // Invincibility timer tick
            if (isInvincible)
            {
                invincibleTimer -= Time.deltaTime;
                if (invincibleTimer <= 0f)
                {
                    isInvincible = false;
                    invincibleTimer = 0f;
                }
            }

            if (damageOverlay != null && currentHealth > 0)
            {
                overlayAlphaAccumulator = Mathf.MoveTowards(overlayAlphaAccumulator, 0f, overlayFadeOutSpeed * 255f * Time.deltaTime);

                float healthPercent = HealthPercent * 100f;
                float pulseAlpha = 0f;

                if (healthPercent <= lowHealthThresholdPercent)
                {
                    pulseTime += Time.deltaTime * overlayPulseSpeed;
                    float pulse = (Mathf.Sin(pulseTime * Mathf.PI * 2f) + 1f) * 0.5f;
                    int baseAlpha = overlayLowHealthAlpha;
                    int halfRange = overlayPulseRange / 2;
                    int pulseMin = Mathf.Clamp(baseAlpha - halfRange, 0, 255);
                    int pulseMax = Mathf.Clamp(baseAlpha + halfRange, 0, 255);
                    pulseAlpha = Mathf.Lerp(pulseMin, pulseMax, pulse);
                }
                else
                {
                    pulseTime = 0f;
                }

                float finalAlpha = Mathf.Max(overlayAlphaAccumulator, pulseAlpha);
                SetOverlayAlpha((int)finalAlpha);
            }

            if (currentHealth < maxHealth && currentHealth > 0)
            {
                regenTimer -= Time.deltaTime;
                if (regenTimer <= 0f)
                {
                    photonView.RPC(nameof(Heal), RpcTarget.All, 1);
                    regenTimer = regenRate;
                }
            }
        }
    }

    private System.Collections.IEnumerator RespawnAfterDelay()
    {
        float delay = Mathf.Max(0f, respawnDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (photonView.IsMine && !isGameOver)
            RespawnPlayer();
    }

    private void SetOverlayAlpha(int alpha255)
    {
        if (damageOverlay != null)
        {
            Color c = damageOverlay.color;
            c.a = Mathf.Clamp01(alpha255 / 255f);
            damageOverlay.color = c;
        }
    }

    private void RespawnPlayer()
    {
        // Mid-level respawn: always at the exact death spot if recorded.
        if (hasDeathPosition)
        {
            transform.position = lastDeathPosition;
            // keep current rotation as-is (or optionally preserve at-death rotation if you want)
        }
        // No RespawnPoint fallback anymore.

        currentHealth = maxHealth;
        UpdateHealthUI();

        if (damageOverlay != null) SetOverlayAlpha(0);
        overlayAlphaAccumulator = 0f;
        pulseTime = 0f;
        regenTimer = regenDelay;

        // Start invincibility window after respawn
        StartInvincibility(respawnInvincibilitySeconds);

        // Clear death point after using it
        hasDeathPosition = false;
    }

    private void StartInvincibility(float seconds)
    {
        if (seconds <= 0f)
        {
            isInvincible = false;
            invincibleTimer = 0f;
            return;
        }
        isInvincible = true;
        invincibleTimer = seconds;
    }

    // Removed: FindNearestSpawnPoint and all RespawnPoint usage

    private void UpdateHealthUI()
    {
        if (healthText != null)
            healthText.text = "Health: " + currentHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
            stream.SendNext(currentHealth);
        else
        {
            int received = (int)stream.ReceiveNext();
            if (received != currentHealth)
            {
                currentHealth = received;
                UpdateHealthUI();
            }
        }
    }
}