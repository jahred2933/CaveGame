using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using TMPro;
using System;

public class Player : MonoBehaviourPunCallbacks, IPunObservable
{
    public GameObject objectToActivate;

    [Header("Weapon Settings")]
    public int damagePerShot = 10;
    public float timeBetweenShots = 0.1f;
    public int maxAmmo = 30;

    [Header("FX References")]
    public ParticleSystem fireParticleSystem;
    public ParticleSystem ammoParticleSystem;
    public Transform bulletSpawnPoint;
    public Light flamethrowerLight;

    [Header("Transforms")]
    public Transform localCamera;
    public Transform weaponTransform;

    [Header("Weapon Angle Limits")]
    public float minWeaponAngle = -45f;
    public float maxWeaponAngle = 45f;

    [Header("Smoothing Settings")]
    public float smoothSpeed = 10f;

    [Header("Flamethrower Light Settings")]
    public float lightMaxIntensity = 2f;
    public float lightMinIntensity = 0f;
    public float lightDimSpeed = 5f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI ammoText;
    [Tooltip("Low ammo threshold (percent of max).")]
    [Range(0.01f, 0.5f)]
    public float lowAmmoPercent = 0.2f;
    public Color lowAmmoColor = Color.red;

    [Header("Networking Sync")]
    public float angleSyncThreshold = 0.5f;

    [Header("Performance Tweaks")]
    public float weaponAngleSnapEpsilon = 0.1f;

    [Header("Beam Settings")]
    public float beamRange = 8f;
    public float beamRadius = 0.15f;
    public LayerMask damageLayers;
    public bool beamUseSphereCast = true;
    public bool beamDrawDebugDuringPlay = true;
    public Color beamGizmoColor = new Color(1f, 0.5f, 0f, 0.6f);
    public bool beamShowImpactGizmo = true;

    public bool weaponActivated = false;

    public event Action<int, int> OnAmmoChanged;
    public event Action OnWeaponActivated;
    public event Action OnFired;

    private enum WeaponState { Inactive, Ready, Firing, Empty }
    private WeaponState weaponState = WeaponState.Inactive;

    private int currentAmmo;
    private bool isFiring = false;
    private float weaponVerticalAngle = 0f;

    private float lastFireTime = -999f;

    private Collider playerCollider;
    private Rigidbody rb;

    private bool ammoParticlesPlaying = false;

    private string lastAmmoString = null;
    private int lastAmmoShown = -999;
    private bool lastActivationShown = false;
    private Color originalAmmoColor;

    private const float LIGHT_EPSILON = 0.01f;

    private float lastSentAngle = float.MinValue;

    public bool IsFiring => isFiring;

    private bool lastBeamHitValid = false;
    private Vector3 lastBeamHitPoint = Vector3.zero;

    void Start()
    {
        if (fireParticleSystem != null)
            fireParticleSystem.Stop();

        currentAmmo = maxAmmo;
        weaponActivated = false;
        weaponState = WeaponState.Inactive;

        if (ammoText != null)
        {
            originalAmmoColor = ammoText.color;
            ammoText.gameObject.SetActive(false);
        }

        UpdateAmmoText(true);

        if (flamethrowerLight != null)
        {
            flamethrowerLight.enabled = true;
            flamethrowerLight.intensity = 0f;
        }

        playerCollider = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();

        if (rb != null)
            rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine) return;

        if (other.CompareTag("PickupItem"))
        {
            photonView.RPC(nameof(RPC_ActivateWeapon), RpcTarget.AllBuffered);

            var pickup = other.GetComponent<PickupItemScript>();
            if (pickup != null)
            {
                pickup.Local_DestroyPickup();
            }
        }
    }

    [PunRPC]
    void RPC_ActivateWeapon()
    {
        ApplyWeaponActivation();
    }

    public void ActivateWeapon()
    {
        ApplyWeaponActivation();
    }

    private void ApplyWeaponActivation()
    {
        if (weaponState == WeaponState.Inactive)
        {
            weaponState = currentAmmo > 0 ? WeaponState.Ready : WeaponState.Empty;
            weaponActivated = true;
            UpdateAmmoText(true);
            OnWeaponActivated?.Invoke();

            if (objectToActivate != null)
                objectToActivate.SetActive(true);

            if (weaponTransform != null && !weaponTransform.gameObject.activeSelf)
                weaponTransform.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            if (weaponActivated)
            {
                HandleFiringInput();
                ProcessAutoFire();
            }

            if (localCamera != null && weaponTransform != null)
            {
                float cameraPitch = localCamera.localEulerAngles.x;
                if (cameraPitch > 180f) cameraPitch -= 360f;
                float targetAngle = Mathf.Clamp(cameraPitch, minWeaponAngle, maxWeaponAngle);

                float angleDelta = Mathf.Abs(Mathf.DeltaAngle(weaponVerticalAngle, targetAngle));
                if (angleDelta < weaponAngleSnapEpsilon)
                {
                    weaponVerticalAngle = targetAngle;
                    weaponTransform.localRotation = Quaternion.Euler(weaponVerticalAngle, 0, 0);
                }
                else
                {
                    weaponVerticalAngle = Mathf.LerpAngle(weaponVerticalAngle, targetAngle, Time.deltaTime * smoothSpeed);
                    weaponTransform.localRotation = Quaternion.Slerp(
                        weaponTransform.localRotation,
                        Quaternion.Euler(weaponVerticalAngle, 0, 0),
                        Time.deltaTime * smoothSpeed
                    );
                }
            }
        }

        if (flamethrowerLight != null)
        {
            float target = isFiring ? lightMaxIntensity : lightMinIntensity;
            flamethrowerLight.intensity = Mathf.Lerp(flamethrowerLight.intensity, target, Time.deltaTime * lightDimSpeed);
            if (flamethrowerLight.intensity < LIGHT_EPSILON)
                flamethrowerLight.intensity = 0f;
        }
    }

    private void HandleFiringInput()
    {
        if (Input.GetButtonDown("Fire1") && CanStartFiring())
        {
            StartFiringCycle();
            AttemptFire();
        }

        if (Input.GetButtonUp("Fire1"))
        {
            StopFiringCycle();
        }
    }

    private bool CanStartFiring()
    {
        return currentAmmo > 0 && !isFiring && weaponState != WeaponState.Inactive;
    }

    private void StartFiringCycle()
    {
        isFiring = true;
        if (weaponState == WeaponState.Ready)
            weaponState = WeaponState.Firing;
    }

    private void StopFiringCycle()
    {
        if (!isFiring) return;
        isFiring = false;
        if (weaponState == WeaponState.Firing)
            weaponState = (currentAmmo > 0) ? WeaponState.Ready : WeaponState.Empty;

        if (photonView.IsMine)
            photonView.RPC(nameof(RPC_StopFireParticles), RpcTarget.All);
    }

    private void ProcessAutoFire()
    {
        if (!isFiring) return;
        if (Time.time >= lastFireTime + timeBetweenShots)
        {
            AttemptFire();
        }
    }

    private void AttemptFire()
    {
        if (currentAmmo <= 0)
        {
            weaponState = WeaponState.Empty;
            StopFiringCycle();
            return;
        }
        if (!weaponActivated || weaponState == WeaponState.Inactive)
            return;

        if (ShootSuccessful())
        {
            lastFireTime = Time.time;
            OnFired?.Invoke();
        }
        else
        {
            StopFiringCycle();
        }
    }

    private bool ShootSuccessful()
    {
        if (bulletSpawnPoint == null)
        {
            Debug.LogWarning("[Weapon] BulletSpawnPoint is not assigned!");
            return false;
        }

        Vector3 origin = bulletSpawnPoint.position;
        Vector3 direction = bulletSpawnPoint.forward;
        float range = Mathf.Max(0.01f, beamRange);

        RaycastHit hit;
        bool gotHit;

        if (beamUseSphereCast && beamRadius > 0f)
        {
            gotHit = Physics.SphereCast(origin, beamRadius, direction, out hit, range, damageLayers, QueryTriggerInteraction.Ignore);
        }
        else
        {
            gotHit = Physics.Raycast(origin, direction, out hit, range, damageLayers, QueryTriggerInteraction.Ignore);
        }

        if (beamDrawDebugDuringPlay)
        {
            Color c = gotHit ? Color.red : Color.yellow;
            Debug.DrawLine(origin, gotHit ? hit.point : origin + direction * range, c, timeBetweenShots * 1.5f);
        }

        lastBeamHitValid = gotHit;
        if (gotHit)
        {
            lastBeamHitPoint = hit.point;
            ApplyBeamDamage(hit.collider);
        }

        currentAmmo--;
        if (currentAmmo <= 0)
        {
            currentAmmo = 0;
            weaponState = WeaponState.Empty;
        }
        UpdateAmmoText();

        if (photonView.IsMine)
            photonView.RPC(nameof(RPC_PlayFireParticles), RpcTarget.All);

        return true;
    }

    private void ApplyBeamDamage(Collider targetCollider)
    {
        if (targetCollider == null) return;
        if (playerCollider != null && targetCollider == playerCollider)
            return;

        PhotonView targetPV = targetCollider.GetComponent<PhotonView>();
        if (targetPV == null)
            targetPV = targetCollider.GetComponentInParent<PhotonView>();
        if (targetPV == null)
            return;

        RockHealth rockHealth = targetCollider.GetComponent<RockHealth>() ?? targetCollider.GetComponentInParent<RockHealth>();
        if (rockHealth != null)
        {
            targetPV.RPC("RequestDamage", targetPV.Owner, damagePerShot);
            return;
        }

        BossController boss = targetCollider.GetComponent<BossController>() ?? targetCollider.GetComponentInParent<BossController>();
        if (boss != null)
        {
            targetPV.RPC("TakeDamage", targetPV.Owner, damagePerShot);
            return;
        }

        EnemyAI enemy = targetCollider.GetComponent<EnemyAI>() ?? targetCollider.GetComponentInParent<EnemyAI>();
        if (enemy != null)
        {
            targetPV.RPC("TakeDamage", targetPV.Owner, damagePerShot);
            return;
        }

        HomingMissile missile = targetCollider.GetComponent<HomingMissile>() ?? targetCollider.GetComponentInParent<HomingMissile>();
        if (missile != null)
        {
            targetPV.RPC("TakeDamage", targetPV.Owner, damagePerShot);
            return;
        }
    }

    private void UpdateAmmoText(bool force = false)
    {
        if (ammoText != null)
        {
            bool shouldShow = weaponActivated;
            if (shouldShow != lastActivationShown)
            {
                ammoText.gameObject.SetActive(shouldShow);
                lastActivationShown = shouldShow;
            }

            if (shouldShow)
            {
                if (force || currentAmmo != lastAmmoShown)
                {
                    string newString = $"Ammo: {currentAmmo}";
                    if (newString != lastAmmoString)
                    {
                        ammoText.text = newString;
                        lastAmmoString = newString;
                        lastAmmoShown = currentAmmo;
                    }

                    float lowThreshold = maxAmmo * lowAmmoPercent;
                    ammoText.color = (currentAmmo <= lowThreshold) ? lowAmmoColor : originalAmmoColor;
                }
            }
        }

        // Simplified particle control:
        if (ammoParticleSystem != null)
        {
            if (currentAmmo > 0)
            {
                if (!ammoParticlesPlaying)
                {
                    ammoParticleSystem.Play();
                    ammoParticlesPlaying = true;
                }
            }
            else // currentAmmo == 0
            {
                if (ammoParticlesPlaying)
                {
                    var main = ammoParticleSystem.main;
                    main.playOnAwake = false; // Turn off Play On Awake after depletion.
                    ammoParticleSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    ammoParticlesPlaying = false;
                }
            }
        }

        OnAmmoChanged?.Invoke(currentAmmo, maxAmmo);
    }

    public void AddAmmo(int amount)
    {
        int prev = currentAmmo;
        currentAmmo = Mathf.Min(currentAmmo + amount, maxAmmo);

        if (prev != currentAmmo)
        {
            if (weaponState == WeaponState.Empty && currentAmmo > 0 && weaponActivated)
                weaponState = isFiring ? WeaponState.Firing : WeaponState.Ready;

            UpdateAmmoText();

            if (photonView.IsMine && ammoParticleSystem != null && currentAmmo > 0)
                photonView.RPC(nameof(RPC_PlayAmmoParticles), RpcTarget.All);
        }
    }

    private void StopFiringCycleExternalIfDeactivated()
    {
        if (!weaponActivated && isFiring)
            StopFiringCycle();
    }

    [PunRPC]
    private void RPC_PlayAmmoParticles()
    {
        if (ammoParticleSystem != null && currentAmmo > 0 && !ammoParticleSystem.isPlaying)
        {
            ammoParticleSystem.Play();
            ammoParticlesPlaying = true;
        }
    }

    [PunRPC]
    private void RPC_PlayFireParticles()
    {
        if (fireParticleSystem != null && !fireParticleSystem.isPlaying)
            fireParticleSystem.Play();
    }

    [PunRPC]
    private void RPC_StopFireParticles()
    {
        if (fireParticleSystem != null && fireParticleSystem.isPlaying)
            fireParticleSystem.Stop();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentAmmo);
            stream.SendNext(weaponActivated);
            stream.SendNext(isFiring);

            bool angleChanged = Mathf.Abs(Mathf.DeltaAngle(weaponVerticalAngle, lastSentAngle)) > angleSyncThreshold;
            stream.SendNext(angleChanged);
            if (angleChanged)
            {
                stream.SendNext(weaponVerticalAngle);
                lastSentAngle = weaponVerticalAngle;
            }

            stream.SendNext((byte)weaponState);
        }
        else
        {
            currentAmmo = (int)stream.ReceiveNext();
            weaponActivated = (bool)stream.ReceiveNext();
            isFiring = (bool)stream.ReceiveNext();

            bool angleChanged = (bool)stream.ReceiveNext();
            if (angleChanged)
            {
                weaponVerticalAngle = (float)stream.ReceiveNext();
                if (weaponTransform != null)
                    weaponTransform.localRotation = Quaternion.Euler(weaponVerticalAngle, 0, 0);
                lastSentAngle = weaponVerticalAngle;
            }

            weaponState = (WeaponState)stream.ReceiveNext();

            UpdateAmmoText();

            if (isFiring)
                RPC_PlayFireParticles();
            else
                RPC_StopFireParticles();
        }
    }

    private void OnDrawGizmos()
    {
        Transform spawn = bulletSpawnPoint != null ? bulletSpawnPoint : transform;
        if (spawn == null) return;

        Gizmos.color = beamGizmoColor;

        Vector3 origin = spawn.position;
        Vector3 dir = spawn.forward;
        float range = Mathf.Max(0.01f, beamRange);

        Gizmos.DrawLine(origin, origin + dir * range);

        if (beamUseSphereCast && beamRadius > 0f)
        {
            int segments = 16;
            float r = beamRadius;
            Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;
            Vector3 up = Vector3.Cross(right, dir).normalized;

            DrawCircle(origin, dir, up, right, r, segments);
            DrawCircle(origin + dir * range, dir, up, right, r, segments);
        }

        if (Application.isPlaying && beamShowImpactGizmo && lastBeamHitValid)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(lastBeamHitPoint, 0.07f);
        }
    }

    private void DrawCircle(Vector3 center, Vector3 dir, Vector3 up, Vector3 right, float radius, int segments)
    {
        Vector3 prev = center + right * radius;
        float angleStep = 360f / segments;
        for (int i = 1; i <= segments; i++)
        {
            float angle = angleStep * i * Mathf.Deg2Rad;
            Vector3 p = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}