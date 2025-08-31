using UnityEngine;

[ExecuteAlways]
[DefaultExecutionOrder(1000)]
public class BossHeadLook : MonoBehaviour
{
    [Header("Bones")]
    public Transform neckBone;
    public Transform headBone;          // should be child of neckBone

    [Header("Target Tag")]
    public string targetTag = "Player"; // EXACT tag (case‑sensitive)

    [Header("Limits (deg)")]
    public float maxYaw = 70f;          // left/right
    public float maxPitchUp = 40f;      // up
    public float maxPitchDown = 50f;    // down

    [Header("Offsets (deg)")]
    public float yawOffset;
    public float pitchOffset;
    public float rollOffset;

    [Header("Smoothing")]
    public bool smooth = false;
    public float smoothTime = 0.08f;    // used if smooth = true

    [Header("Debug")]
    public bool debugLog;

    Transform _target;
    Quaternion _restLocalRot;
    bool _captured;

    float _yawVel, _pitchVel;
    float _curYaw, _curPitch;
    float _reacquireTimer;
    const float REACQUIRE_INTERVAL = 0.25f;

    void OnEnable()
    {
        if (headBone)
        {
            _restLocalRot = headBone.localRotation;
            _captured = true;
            _curYaw = 0f;
            _curPitch = 0f;
        }
    }

    void LateUpdate()
    {
        if (!neckBone || !headBone || !_captured) return;

        // reacquire target periodically or if lost
        _reacquireTimer -= Application.isPlaying ? Time.deltaTime : 0.016f;
        if (_target == null || _reacquireTimer <= 0f)
        {
            _target = FindClosest();
            _reacquireTimer = REACQUIRE_INTERVAL;
        }
        if (_target == null) return;

        Vector3 dir = _target.position - neckBone.position;
        if (dir.sqrMagnitude < 0.000001f) return;
        dir.Normalize();

        // direction in neck local space
        Vector3 local = neckBone.InverseTransformDirection(dir);

        // raw yaw/pitch from local direction
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg; // up +

        // clamp
        yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        pitch = Mathf.Clamp(pitch, -maxPitchDown, maxPitchUp);

        // smoothing (play mode only)
        if (Application.isPlaying && smooth)
        {
            _curYaw = SmoothDampAngle(_curYaw, yaw, ref _yawVel, smoothTime);
            _curPitch = SmoothDampAngle(_curPitch, pitch, ref _pitchVel, smoothTime);
        }
        else
        {
            _curYaw = yaw;
            _curPitch = pitch;
        }

        // apply offsets
        float finalYaw = _curYaw + yawOffset;
        float finalPitch = _curPitch + pitchOffset;

        // build final local rotation
        Quaternion lookLocal = Quaternion.Euler(-finalPitch, finalYaw, rollOffset);
        headBone.localRotation = lookLocal * _restLocalRot;
    }

    Transform FindClosest()
    {
        GameObject[] objs;
        try { objs = GameObject.FindGameObjectsWithTag(targetTag); }
        catch
        {
            if (debugLog) Debug.LogWarning("BossHeadLook: Tag '" + targetTag + "' not defined.");
            return null;
        }

        if (objs.Length == 0)
        {
            if (debugLog) Debug.LogWarning("BossHeadLook: No objects with tag '" + targetTag + "' found.");
            return null;
        }

        Transform best = null;
        float bestDist = float.PositiveInfinity;
        Vector3 origin = neckBone.position;

        for (int i = 0; i < objs.Length; i++)
        {
            var t = objs[i].transform;
            float d = (t.position - origin).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    // Custom (no Mathf.SmoothDampAngle GC)
    float SmoothDampAngle(float current, float target, ref float velocity, float smoothTime)
    {
        // simple critically damped
        float delta = Mathf.DeltaAngle(current, target);
        float omega = 2f / Mathf.Max(0.0001f, smoothTime);
        float x = omega * Time.deltaTime;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = delta;
        float temp = (velocity + omega * change) * Time.deltaTime;
        velocity = (velocity - omega * temp) * exp;
        float result = current + change + temp;
        return result;
    }
}