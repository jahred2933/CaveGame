using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using System;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(PhotonView))]
[DisallowMultipleComponent]
public class PlayerController : MonoBehaviourPunCallbacks, IPunObservable
{
    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;
    private PhotonView photonView;

    private bool groundedPlayer;

    [Header("Movement Speeds")]
    [Tooltip("Normal movement speed when walking.")]
    public float walkSpeed = 2f;
    [Tooltip("Movement speed when sprinting.")]
    public float sprintSpeed = 4f;
    [Tooltip("How fast the player accelerates to max speed.")]
    public float acceleration = 40f;
    [Tooltip("How quickly the player slows/stops when no input is given (ground friction).")]
    public float friction = 10f;

    [Header("Jump Settings")]
    [Tooltip("How high the player jumps.")]
    public float jumpHeight = 1f;
    [Tooltip("Gravity force applied when in air (negative). If usePhysicsGravity is true, this is ignored.")]
    public float gravityValue = -9.81f;
    [Tooltip("Use Physics.gravity instead of custom gravityValue.")]
    public bool usePhysicsGravity = false;

    [Header("Stamina Settings")]
    [Tooltip("Maximum stamina for sprinting.")]
    public float maxStamina = 5f;
    [Tooltip("Stamina cost per second while sprinting.")]
    public float staminaDecreasePerSecond = 1.5f;
    [Tooltip("Stamina recovery per second.")]
    public float staminaIncreasePerSecond = 1f;
    [Tooltip("Percent (0-1) of max stamina required before sprint can be used again after hitting zero.")]
    public float staminaRegenThresholdPercent = 0.05f;
    [Tooltip("Assign a UI Slider for the stamina bar (optional).")]
    public Slider staminaBar;

    [Header("Air Control")]
    [Range(0f, 1f)]
    [Tooltip("How much movement control the player has while airborne.")]
    public float airControlPercent = 0.4f;

    [Header("Ground Check Settings")]
    [Tooltip("Transform to check if grounded.")]
    public Transform groundCheck;
    [Tooltip("Radius for ground check sphere.")]
    public float groundCheckRadius = 0.2f;
    [Tooltip("LayerMask for what is considered ground.")]
    public LayerMask groundLayer;

    [Header("Camera Settings")]
    public GameObject selectedCamera;
    [Tooltip("Camera bob frequency.")]
    public float bobFrequency = 8f;
    [Tooltip("Camera bob horizontal amplitude.")]
    public float bobHorizontalAmplitude = 0.05f;
    [Tooltip("Camera bob vertical amplitude.")]
    public float bobVerticalAmplitude = 0.05f;
    [Tooltip("Multiplier for camera bob when sprinting.")]
    public float sprintBobMultiplier = 1.5f;

    // ---- Hidden Advanced Settings (still used) ----
    [HideInInspector] public float coyoteTime = 0.1f;
    [HideInInspector] public float jumpBufferTime = 0.12f;
    [HideInInspector] public float fallGravityMultiplier = 1.5f;
    [HideInInspector] public bool variableJump = true;
    [HideInInspector][Range(0f, 1f)] public float jumpReleaseVelocityMultiplier = 0.5f;

    [HideInInspector] public float minGroundNormalY = 0.45f;
    [HideInInspector] public float slopeSlideSpeed = 5f;

    [HideInInspector] public bool clampHorizontalSpeed = true;
    [HideInInspector] public float staminaRegenDelay = 0.4f;
    [HideInInspector] public float bobSettleTime = 0.15f;
    [HideInInspector] public bool scaleBobBySpeed = true;

    [HideInInspector] public float positionSendThresholdSqr = 0.0004f;
    [HideInInspector] public float velocitySendThreshold = 0.05f;
    [HideInInspector] public float rotationSendThreshold = 0.5f;

    [HideInInspector] public bool debugGroundGizmos = false;
    [HideInInspector] public bool debugStateLogs = false;

    // NEW HIDDEN FIELD: toggle for legacy grounded downforce (disabled by default to improve uphill retention)
    [HideInInspector] public bool useGroundedDownforce = false;

    // Events
    public event Action<bool> OnGroundedChanged;
    public event Action OnJump;
    public event Action OnLand;
    public event Action OnSprintStart;
    public event Action OnSprintEnd;
    public event Action<float, float> OnStaminaChanged;

    // Internal State
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float lerpSpeed = 10f;

    private float stamina;
    private bool staminaLocked;

    private bool isSprinting;
    private Vector3 lastInputDirection = Vector3.zero;
    private bool lastMoving = false;
    private bool lastSprinting = false;
    private bool jumpRequest;
    private Vector3 groundNormal = Vector3.up;

    private bool movementLocked = false;

    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private bool jumpConsumed;

    private float bobTimer = 0f;
    private Vector3 cameraInitialLocalPos;
    private Vector3 cameraBobTargetPos;
    private float cameraBobSmoothTime = 0.08f;
    private Vector3 cameraBobVelocity = Vector3.zero;
    private Vector3 bobCurrentOffset = Vector3.zero;

    private float lastStaminaUseTime = -999f;

    private Vector3 lastSentPosition;
    private Vector3 lastSentVelocity;
    private Quaternion lastSentRotation;
    private bool firstSend = true;

    private static readonly Collider[] groundHits = new Collider[8];

    private float walkSpeedSqr;
    private float sprintSpeedSqr;

    private const byte FlagPosition = 1 << 0;
    private const byte FlagVelocity = 1 << 1;
    private const byte FlagRotation = 1 << 2;

    public bool IsGrounded => groundedPlayer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        photonView = GetComponent<PhotonView>();

        targetPosition = transform.position;
        targetRotation = transform.rotation;

        stamina = maxStamina;
        staminaLocked = false;

        if (selectedCamera != null)
        {
            cameraInitialLocalPos = selectedCamera.transform.localPosition;
            cameraBobTargetPos = cameraInitialLocalPos;
        }

        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;

        walkSpeedSqr = walkSpeed * walkSpeed;
        sprintSpeedSqr = sprintSpeed * sprintSpeed;
    }

    void Start()
    {
        if (!photonView.IsMine)
            rb.isKinematic = true;

        if (photonView.IsMine && selectedCamera != null)
            selectedCamera.SetActive(true);

        if (staminaBar != null)
        {
            staminaBar.maxValue = maxStamina;
            staminaBar.value = stamina;
        }
    }

    void Update()
    {
        if (!photonView.IsMine)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * lerpSpeed);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * lerpSpeed);
            return;
        }

        if (movementLocked)
        {
            HandleCameraBob(false, false);
            return;
        }

        UpdateGroundedState();
        ReadInput();
        UpdateStamina();
        HandleJumpBuffering();
        HandleCameraBob(lastMoving, isSprinting && lastMoving && groundedPlayer);
    }

    void FixedUpdate()
    {
        if (!photonView.IsMine)
            return;

        if (movementLocked)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        ApplyMovement();
        ApplyJump();
        ApplyGravity();
        ClampHorizontalIfNeeded();
    }

    private void ReadInput()
    {
        float x = 0f, z = 0f;
        if (Input.GetKey(KeyCode.A)) x -= 1f;
        if (Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.W)) z += 1f;
        if (Input.GetKey(KeyCode.S)) z -= 1f;

        Vector3 inputDirection = new Vector3(x, 0, z);
        bool moving = inputDirection.sqrMagnitude > 0.0001f;
        if (moving) inputDirection.Normalize();

        bool sprintKey = Input.GetKey(KeyCode.LeftShift);

        isSprinting = false;
        float staminaRegenThreshold = maxStamina * staminaRegenThresholdPercent;

        if (staminaLocked)
        {
            if (stamina >= staminaRegenThreshold)
                staminaLocked = false;
        }

        if (!staminaLocked && sprintKey && stamina > 0f && moving && groundedPlayer)
        {
            isSprinting = true;
            stamina -= staminaDecreasePerSecond * Time.deltaTime;
            lastStaminaUseTime = Time.time;
            if (stamina <= 0f)
            {
                stamina = 0f;
                isSprinting = false;
                staminaLocked = true;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = Time.time;

        if (isSprinting && !lastSprinting)
            OnSprintStart?.Invoke();
        else if (!isSprinting && lastSprinting)
            OnSprintEnd?.Invoke();

        lastInputDirection = inputDirection;
        lastMoving = moving;
        lastSprinting = isSprinting;

        UpdateStaminaUI();
    }

    private void UpdateGroundedState()
    {
        bool wasGrounded = groundedPlayer;
        groundedPlayer = false;
        groundNormal = Vector3.up;

        if (groundCheck)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(groundCheck.position, groundCheckRadius, groundHits, groundLayer, QueryTriggerInteraction.Ignore);
            if (hitCount > 0)
            {
                if (Physics.Raycast(groundCheck.position, Vector3.down, out RaycastHit hit, groundCheckRadius + 0.3f, groundLayer, QueryTriggerInteraction.Ignore))
                {
                    groundNormal = hit.normal;
                    groundedPlayer = groundNormal.y >= minGroundNormalY;
                }
                else
                {
                    groundedPlayer = true;
                }
            }
        }
        else
        {
            groundedPlayer = Physics.CheckSphere(
                transform.position + Vector3.down * (capsuleCollider.bounds.extents.y + 0.1f),
                groundCheckRadius,
                groundLayer
            );
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit2, capsuleCollider.bounds.extents.y + 0.3f, groundLayer, QueryTriggerInteraction.Ignore))
                groundNormal = hit2.normal;
        }

        if (groundedPlayer)
        {
            lastGroundedTime = Time.time;
            jumpConsumed = false;
        }

        if (groundedPlayer != wasGrounded)
        {
            OnGroundedChanged?.Invoke(groundedPlayer);
            if (groundedPlayer)
                OnLand?.Invoke();
        }
    }

    private void UpdateStamina()
    {
        if (!isSprinting)
        {
            if (Time.time >= lastStaminaUseTime + staminaRegenDelay)
            {
                if (stamina < maxStamina)
                {
                    stamina += staminaIncreasePerSecond * Time.deltaTime;
                    if (stamina > maxStamina) stamina = maxStamina;
                }
            }
        }

        stamina = Mathf.Clamp(stamina, 0f, maxStamina);
        OnStaminaChanged?.Invoke(stamina, maxStamina);
    }

    private void UpdateStaminaUI()
    {
        if (staminaBar != null)
            staminaBar.value = stamina;
    }

    private void HandleJumpBuffering()
    {
        bool canUseCoyote = (Time.time - lastGroundedTime) <= coyoteTime;
        bool hasBufferedJump = (Time.time - lastJumpPressedTime) <= jumpBufferTime;

        if (hasBufferedJump && !jumpConsumed && canUseCoyote)
        {
            jumpRequest = true;
            jumpConsumed = true;
        }

        if (variableJump && Input.GetKeyUp(KeyCode.Space))
        {
            if (rb.linearVelocity.y > 0f)
            {
                Vector3 v = rb.linearVelocity;
                v.y *= jumpReleaseVelocityMultiplier;
                rb.linearVelocity = v;
            }
        }
    }

    private void ApplyMovement()
    {
        float targetSpeed = isSprinting ? sprintSpeed : walkSpeed;

        Vector3 moveDir = Vector3.zero;
        if (lastMoving)
        {
            moveDir = transform.TransformDirection(lastInputDirection);
            moveDir = Vector3.ProjectOnPlane(moveDir, groundNormal).normalized;
        }

        if (groundedPlayer)
        {
            if (lastMoving)
            {
                // NEW: Slope-aligned velocity correction (preserves speed up ramps)
                Vector3 currentPlaneVel = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);
                Vector3 desiredPlaneVel = moveDir * targetSpeed;
                Vector3 delta = desiredPlaneVel - currentPlaneVel;

                float maxDelta = acceleration * Time.fixedDeltaTime;
                if (delta.magnitude > maxDelta)
                    delta = delta.normalized * maxDelta;

                rb.AddForce(delta, ForceMode.VelocityChange);
            }
            else
            {
                // Friction now acts on plane velocity instead of purely horizontal XZ
                Vector3 planeVel = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);
                Vector3 frictionForce = -planeVel * friction;
                rb.AddForce(frictionForce, ForceMode.Acceleration);

                if (planeVel.magnitude < 0.05f)
                {
                    // Zero out only the plane component; preserve vertical (e.g., if standing on moving platform)
                    Vector3 vel = rb.linearVelocity;
                    Vector3 planeComponent = Vector3.ProjectOnPlane(vel, groundNormal);
                    rb.linearVelocity = vel - planeComponent;
                }
            }

            if (groundNormal.y < minGroundNormalY)
            {
                Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                rb.AddForce(slideDir * slopeSlideSpeed, ForceMode.Acceleration);
            }
        }
        else
        {
            if (lastMoving)
            {
                Vector3 desiredDir = transform.TransformDirection(lastInputDirection);
                Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
                float currentMag = currentHorizontal.magnitude;
                Vector3 desiredHorizontal = desiredDir * currentMag;

                Vector3 blended = Vector3.Lerp(currentHorizontal, desiredHorizontal, airControlPercent * Time.fixedDeltaTime * 5f);
                rb.linearVelocity = new Vector3(blended.x, rb.linearVelocity.y, blended.z);
            }
        }
    }

    private void ApplyJump()
    {
        if (jumpRequest)
        {
            float g = usePhysicsGravity ? Physics.gravity.y : gravityValue;
            float jumpVelocity = Mathf.Sqrt(2f * jumpHeight * -g);
            Vector3 vel = rb.linearVelocity;
            if (vel.y < 0f) vel.y = 0f;
            vel.y = jumpVelocity;
            rb.linearVelocity = vel;

            jumpRequest = false;
            OnJump?.Invoke();
            if (debugStateLogs) Debug.Log("[PlayerController] Jump executed.");
        }
    }

    private void ApplyGravity()
    {
        float g = usePhysicsGravity ? Physics.gravity.y : gravityValue;
        if (!groundedPlayer)
        {
            float multiplier = (rb.linearVelocity.y < 0f) ? fallGravityMultiplier : 1f;
            rb.AddForce(Vector3.up * g * multiplier, ForceMode.Acceleration);
        }
        else
        {
            // CHANGED: Removed unconditional grounded downforce unless explicitly enabled
            if (useGroundedDownforce)
                rb.AddForce(Vector3.up * g * 0.2f, ForceMode.Acceleration);
        }
    }

    private void ClampHorizontalIfNeeded()
    {
        if (!clampHorizontalSpeed) return;
        if (!groundedPlayer) return; // do NOT clamp while airborne

        Vector3 vel = rb.linearVelocity;
        float max = isSprinting ? sprintSpeed : walkSpeed;

        // CHANGED: Clamp plane (slope) speed rather than raw horizontal XZ
        Vector3 planeVel = Vector3.ProjectOnPlane(vel, groundNormal);
        if (planeVel.sqrMagnitude > max * max)
        {
            Vector3 excess = planeVel - planeVel.normalized * max;
            rb.linearVelocity = vel - excess;
        }
    }

    private void HandleCameraBob(bool moving, bool sprinting)
    {
        if (selectedCamera == null)
            return;

        if (moving && groundedPlayer)
        {
            float speedFactor = 1f;
            if (scaleBobBySpeed)
            {
                float horizontalSpeed = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).magnitude;
                float maxSpeed = sprinting ? sprintSpeed : walkSpeed;
                speedFactor = maxSpeed > 0.01f ? Mathf.Clamp01(horizontalSpeed / maxSpeed) : 0f;
            }

            float freq = bobFrequency * (sprinting ? sprintBobMultiplier : 1f);
            float horizAmp = bobHorizontalAmplitude * (sprinting ? sprintBobMultiplier : 1f) * speedFactor;
            float vertAmp = bobVerticalAmplitude * (sprinting ? sprintBobMultiplier : 1f) * speedFactor;

            float angle = bobTimer * freq;
            float horizontalBob = Mathf.Sin(angle) * horizAmp;
            float verticalBob = Mathf.Abs(Mathf.Cos(angle)) * vertAmp;

            Vector3 targetOffset = new Vector3(horizontalBob, verticalBob, 0f);
            bobCurrentOffset = Vector3.SmoothDamp(bobCurrentOffset, targetOffset, ref cameraBobVelocity, cameraBobSmoothTime);
            bobTimer += Time.deltaTime;
        }
        else
        {
            bobCurrentOffset = Vector3.SmoothDamp(bobCurrentOffset, Vector3.zero, ref cameraBobVelocity, bobSettleTime);
            bobTimer = 0f;
        }

        cameraBobTargetPos = cameraInitialLocalPos + bobCurrentOffset;
        selectedCamera.transform.localPosition = cameraBobTargetPos;
    }

    public void SetMovementLock(bool locked)
    {
        movementLocked = locked;
        if (locked)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            rb.angularVelocity = Vector3.zero;
        }
    }

    public bool IsMovementLocked()
    {
        return movementLocked;
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (rb == null) return;

        if (stream.IsWriting)
        {
            Vector3 pos = transform.position;
            Vector3 vel = rb.linearVelocity;
            Quaternion rot = transform.rotation;

            bool sendPos = firstSend || (pos - lastSentPosition).sqrMagnitude > positionSendThresholdSqr;
            bool sendVel = firstSend || (vel - lastSentVelocity).magnitude > velocitySendThreshold;
            bool sendRot = firstSend || Quaternion.Angle(rot, lastSentRotation) > rotationSendThreshold;

            byte flags = 0;
            if (sendPos) flags |= FlagPosition;
            if (sendVel) flags |= FlagVelocity;
            if (sendRot) flags |= FlagRotation;

            stream.SendNext(flags);
            if (sendPos) stream.SendNext(pos);
            if (sendVel) stream.SendNext(vel);
            if (sendRot) stream.SendNext(rot);

            if (sendPos) lastSentPosition = pos;
            if (sendVel) lastSentVelocity = vel;
            if (sendRot) lastSentRotation = rot;
            firstSend = false;
        }
        else
        {
            byte flags = (byte)stream.ReceiveNext();
            if ((flags & FlagPosition) != 0) targetPosition = (Vector3)stream.ReceiveNext();
            if ((flags & FlagVelocity) != 0)
            {
                Vector3 v = (Vector3)stream.ReceiveNext();
                if (!rb.isKinematic) rb.linearVelocity = v;
            }
            if ((flags & FlagRotation) != 0) targetRotation = (Quaternion)stream.ReceiveNext();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugGroundGizmos) return;
        Gizmos.color = Color.yellow;
        if (groundCheck != null)
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        else
            Gizmos.DrawWireSphere(transform.position + Vector3.down * (capsuleCollider ? capsuleCollider.bounds.extents.y + 0.1f : 0.5f), groundCheckRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + groundNormal.normalized);
    }

    private void OnValidate()
    {
        if (sprintSpeed < walkSpeed)
            sprintSpeed = walkSpeed;
        staminaRegenThresholdPercent = Mathf.Clamp(staminaRegenThresholdPercent, 0f, 1f);
        minGroundNormalY = Mathf.Clamp(minGroundNormalY, 0f, 1f);
    }
}