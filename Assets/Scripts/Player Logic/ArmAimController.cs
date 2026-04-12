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
    [SerializeField] Transform _shoulder;

    [Header("Settings")]
    [SerializeField] float _handSpeed  = 15f;  // right hand lerp speed
    [SerializeField] float _leftSpeed  = 3f;   // left arm follow speed — loose and lazy

    [Header("Rendering")]
    [SerializeField] SpriteRenderer[] _armRenderers;
    [SerializeField] int _frontOrder = 5;
    [SerializeField] int _backOrder  = -1;

    SpriteRenderer _gunRenderer;
    bool           _holdingGun;

    public void SetGunRenderer(SpriteRenderer sr)
    {
        _gunRenderer = sr;
        _holdingGun  = sr != null;
    }

    void FixedUpdate()
    {
        if (Camera.main == null || _shoulder == null || _handRb == null) return;

        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 toMouse    = mouseWorld - (Vector2)_shoulder.position;
        float   aimAngle   = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

        bool crossedBody = Mathf.Abs(aimAngle) > 90f;

        // Hand always snaps to mouse precisely
        _handRb.MoveRotation(Mathf.LerpAngle(
            _handRb.rotation, aimAngle, _handSpeed * Time.fixedDeltaTime));

        if (_holdingGun)
        {
            // Arm and wrist snap directly to aim angle — no lerp, always points at mouse
            if (_armRb   != null) _armRb.MoveRotation(aimAngle);
            if (_wristRb != null) _wristRb.MoveRotation(aimAngle);
        }

        // Left arm follows mouse loosely — always active, lags behind for a natural feel
        if (_armLRb   != null) _armLRb.MoveRotation(Mathf.LerpAngle(_armLRb.rotation,   aimAngle, _leftSpeed * Time.fixedDeltaTime));
        if (_wristLRb != null) _wristLRb.MoveRotation(Mathf.LerpAngle(_wristLRb.rotation, aimAngle, _leftSpeed * Time.fixedDeltaTime));
        if (_handLRb  != null) _handLRb.MoveRotation(Mathf.LerpAngle(_handLRb.rotation,  aimAngle, _leftSpeed * Time.fixedDeltaTime));

        // Sort order + gun flip
        int order = crossedBody ? _frontOrder : _backOrder;
        foreach (var sr in _armRenderers)
            if (sr != null) sr.sortingOrder = order;

        if (_gunRenderer != null)
            _gunRenderer.flipY = crossedBody;
    }
}
