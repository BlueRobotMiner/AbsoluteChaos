using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to a hidden child GO on the bullet prefab (the "explosion circle").
/// Awake captures the design-time scale as the target, then zeroes out and deactivates.
/// Explode() animates 0 → target scale, applies knockback at peak (server only),
/// then shrinks back and fires the completion callback so the caller can return to pool.
/// </summary>
public class ExplosionEffect : MonoBehaviour
{
    [SerializeField] float _expandTime     = 0.12f;
    [SerializeField] float _holdTime       = 0.05f;
    [SerializeField] float _shrinkTime     = 0.08f;
    [SerializeField] float _knockbackForce = 8f;
    [SerializeField] LayerMask _knockbackMask;

    Vector3 _targetScale;
    Action  _onComplete;
    float   _timer;

    enum Phase { Idle, Expanding, Holding, Shrinking }
    Phase _phase = Phase.Idle;

    void Awake()
    {
        _targetScale         = transform.localScale;
        transform.localScale = Vector3.zero;
        gameObject.SetActive(false);
    }

    public void Explode(Action onComplete)
    {
        _onComplete          = onComplete;
        _timer               = 0f;
        _phase               = Phase.Expanding;
        transform.localScale = Vector3.zero;
        gameObject.SetActive(true);
    }

    void Update()
    {
        switch (_phase)
        {
            case Phase.Expanding:
                _timer += Time.deltaTime;
                transform.localScale = Vector3.Lerp(Vector3.zero, _targetScale,
                                                    Mathf.Clamp01(_timer / _expandTime));
                if (_timer >= _expandTime)
                {
                    transform.localScale = _targetScale;
                    ApplyKnockback();
                    _timer = 0f;
                    _phase = Phase.Holding;
                }
                break;

            case Phase.Holding:
                _timer += Time.deltaTime;
                if (_timer >= _holdTime) { _timer = 0f; _phase = Phase.Shrinking; }
                break;

            case Phase.Shrinking:
                _timer += Time.deltaTime;
                transform.localScale = Vector3.Lerp(_targetScale, Vector3.zero,
                                                    Mathf.Clamp01(_timer / _shrinkTime));
                if (_timer >= _shrinkTime)
                {
                    transform.localScale = Vector3.zero;
                    gameObject.SetActive(false);
                    _phase = Phase.Idle;
                    _onComplete?.Invoke();
                    _onComplete = null;
                }
                break;
        }
    }

    void ApplyKnockback()
    {
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;

        float radius = _targetScale.x * 0.5f;
        var hits     = Physics2D.OverlapCircleAll(transform.position, radius, _knockbackMask);
        var seen     = new HashSet<Rigidbody2D>();

        foreach (var hit in hits)
        {
            var rb = hit.attachedRigidbody;
            if (rb == null || !seen.Add(rb)) continue;

            Vector2 dir = rb.position - (Vector2)transform.position;
            if (dir == Vector2.zero) dir = Vector2.up;
            rb.AddForce(dir.normalized * _knockbackForce, ForceMode2D.Impulse);
        }
    }

    public void ResetEffect()
    {
        _phase               = Phase.Idle;
        _timer               = 0f;
        _onComplete          = null;
        transform.localScale = Vector3.zero;
        gameObject.SetActive(false);
    }
}
