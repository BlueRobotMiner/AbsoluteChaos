using Unity.Netcode;
using UnityEngine;

public class ArmAimController : MonoBehaviour
{
    [Header("Right Arm Joints (Gun Hand)")]
    [SerializeField] Rigidbody2D _armRb;
    [SerializeField] Rigidbody2D _wristRb;
    [SerializeField] Rigidbody2D _handRb;

    [Header("Left Arm Joints (Support Hand)")]
    [SerializeField] Rigidbody2D _armLRb;
    [SerializeField] Rigidbody2D _wristLRb;
    [SerializeField] Rigidbody2D _handLRb;

    [Header("Transforms")]
    [SerializeField] Transform _shoulder;    // right shoulder pivot — used by PlayerController for aim angle
    [SerializeField] Transform _shoulderL;   // left shoulder pivot — IK origin for left arm gun grip
    public Transform Shoulder => _shoulder;

    [Header("Settings")]
    [SerializeField] float _upperArmSpeed = 5f;    // upper arm lerp speed — low = natural physics lag/bend
    [SerializeField] float _handSpeed     = 15f;   // wrist + hand lerp speed — higher = crisp aim tracking

    [Header("Rendering")]
    [SerializeField] SpriteRenderer[] _armRenderers;
    [SerializeField] int _frontOrder = 5;
    [SerializeField] int _backOrder  = -1;

    [Header("Punch Animation")]
    [SerializeField] float _punchFoldDeg  = 80f;
    [SerializeField] float _punchFoldTime = 0.06f;
    [SerializeField] float _punchExtTime  = 0.09f;

    bool      _holdingGun;
    Transform _leftHandGrip;
    float     _serverAimAngle;
    bool      _isPunching;

    // ── Awake ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        DisableColliders(_armRb);
        DisableColliders(_wristRb);
        DisableColliders(_handRb);
        DisableColliders(_armLRb);
        DisableColliders(_wristLRb);
        DisableColliders(_handLRb);
    }

    static void DisableColliders(Rigidbody2D rb)
    {
        if (rb == null) return;
        foreach (var col in rb.GetComponents<Collider2D>())
            col.enabled = false;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetGunRenderer(SpriteRenderer sr) { _holdingGun = sr != null; }

    public void SetLeftGrip(Transform grip) { _leftHandGrip = grip; }

    public void SetServerAimAngle(float angle) { _serverAimAngle = angle; }

    public void TriggerPunch(float aimAngle)
    {
        if (_isPunching) return;
        StartCoroutine(PunchCoroutine(aimAngle));
    }

    System.Collections.IEnumerator PunchCoroutine(float aimAngle)
    {
        _isPunching = true;

        // Snapshot each joint's current angle — fold and extend are relative to these
        float rArmStart   = _armRb    != null ? _armRb.rotation   : aimAngle;
        float rWristStart = _wristRb  != null ? _wristRb.rotation : aimAngle;
        float rHandStart  = _handRb   != null ? _handRb.rotation  : aimAngle;
        float lArmStart   = _armLRb   != null ? _armLRb.rotation  : aimAngle + 180f;
        float lWristStart = _wristLRb != null ? _wristLRb.rotation: aimAngle + 180f;
        float lHandStart  = _handLRb  != null ? _handLRb.rotation : aimAngle + 180f;

        float end = Time.time + _punchFoldTime;
        while (Time.time < end)
        {
            if (_armRb    != null) _armRb.MoveRotation(Mathf.LerpAngle(_armRb.rotation,       rArmStart   - _punchFoldDeg,        25f * Time.fixedDeltaTime));
            if (_wristRb  != null) _wristRb.MoveRotation(Mathf.LerpAngle(_wristRb.rotation,   rWristStart - _punchFoldDeg + 20f,  25f * Time.fixedDeltaTime));
            if (_handRb   != null) _handRb.MoveRotation(Mathf.LerpAngle(_handRb.rotation,     rHandStart  - _punchFoldDeg + 30f,  25f * Time.fixedDeltaTime));
            if (_armLRb   != null) _armLRb.MoveRotation(Mathf.LerpAngle(_armLRb.rotation,     lArmStart   - _punchFoldDeg,        25f * Time.fixedDeltaTime));
            if (_wristLRb != null) _wristLRb.MoveRotation(Mathf.LerpAngle(_wristLRb.rotation, lWristStart - _punchFoldDeg + 20f,  25f * Time.fixedDeltaTime));
            if (_handLRb  != null) _handLRb.MoveRotation(Mathf.LerpAngle(_handLRb.rotation,   lHandStart  - _punchFoldDeg + 30f,  25f * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }

        end = Time.time + _punchExtTime;
        while (Time.time < end)
        {
            if (_armRb    != null) _armRb.MoveRotation(Mathf.LerpAngle(_armRb.rotation,       rArmStart,   35f * Time.fixedDeltaTime));
            if (_wristRb  != null) _wristRb.MoveRotation(Mathf.LerpAngle(_wristRb.rotation,   rWristStart, 35f * Time.fixedDeltaTime));
            if (_handRb   != null) _handRb.MoveRotation(Mathf.LerpAngle(_handRb.rotation,     rHandStart,  35f * Time.fixedDeltaTime));
            if (_armLRb   != null) _armLRb.MoveRotation(Mathf.LerpAngle(_armLRb.rotation,     lArmStart,   35f * Time.fixedDeltaTime));
            if (_wristLRb != null) _wristLRb.MoveRotation(Mathf.LerpAngle(_wristLRb.rotation, lWristStart, 35f * Time.fixedDeltaTime));
            if (_handLRb  != null) _handLRb.MoveRotation(Mathf.LerpAngle(_handLRb.rotation,   lHandStart,  35f * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }

        _isPunching = false;
    }

    // ── FixedUpdate ───────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        if (_shoulder == null || _handRb == null) return;
        if (_isPunching) return;

        float aimAngle    = _serverAimAngle;
        bool  crossedBody = Mathf.Abs(aimAngle) > 90f;

        // ── Right arm ─────────────────────────────────────────────────────────
        // Upper arm uses a slow lerp so it lags behind the wrist/hand — the lag
        // creates a natural elbow bend that inverts automatically when the arm
        // crosses to the other side of the body.

        if (_holdingGun)
        {
            // Wrist and hand snap precisely so the gun tracks the mouse exactly.
            // Upper arm trails at _upperArmSpeed — bend is physics/lag driven.
            if (_armRb   != null) _armRb.MoveRotation(Mathf.LerpAngle(_armRb.rotation, aimAngle, _upperArmSpeed * Time.fixedDeltaTime));
            if (_wristRb != null) _wristRb.MoveRotation(aimAngle);
            _handRb.MoveRotation(aimAngle);
        }
        else
        {
            float t = _handSpeed * Time.fixedDeltaTime;
            if (_armRb   != null) _armRb.MoveRotation(Mathf.LerpAngle(_armRb.rotation,     aimAngle, _upperArmSpeed * Time.fixedDeltaTime));
            if (_wristRb != null) _wristRb.MoveRotation(Mathf.LerpAngle(_wristRb.rotation, aimAngle, t));
            _handRb.MoveRotation(Mathf.LerpAngle(_handRb.rotation, aimAngle, t));
        }

        // ── Left arm ──────────────────────────────────────────────────────────
        // Gun held: aim toward the gun's left grip point using the shoulder pivot.
        // Unarmed: mirror right arm direction via aimAngle + 180°.
        // Same lag pattern — upper arm slow, wrist/hand faster.

        if (_armLRb == null) { UpdateSortOrder(crossedBody); return; }

        {
            float leftAimAngle = aimAngle + 180f;
            float lt = _handSpeed * Time.fixedDeltaTime;
            _armLRb.MoveRotation(Mathf.LerpAngle(_armLRb.rotation,     leftAimAngle, _upperArmSpeed * Time.fixedDeltaTime));
            if (_wristLRb != null) _wristLRb.MoveRotation(Mathf.LerpAngle(_wristLRb.rotation, leftAimAngle, lt));
            if (_handLRb  != null) _handLRb.MoveRotation(Mathf.LerpAngle(_handLRb.rotation,   leftAimAngle, lt));
        }

        UpdateSortOrder(crossedBody);
    }

    void UpdateSortOrder(bool crossedBody)
    {
        int order = crossedBody ? _frontOrder : _backOrder;
        foreach (var sr in _armRenderers)
            if (sr != null) sr.sortingOrder = order;
    }
}
