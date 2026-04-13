using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual-only bullet on non-server clients.
/// Tracks all active instances so ProjectilePool.NotifyHitClientRpc can
/// remove the nearest one when the server reports a hit.
/// </summary>
public class BulletVisual : MonoBehaviour
{
    // All currently live visual bullets — used by ReturnNearest
    static readonly List<BulletVisual> _active = new List<BulletVisual>();

    Vector2 _direction;
    float   _speed;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void OnEnable()  => _active.Add(this);
    void OnDisable() => _active.Remove(this);

    // ── API ───────────────────────────────────────────────────────────────

    public void Init(Vector2 direction, float speed)
    {
        _direction = direction;
        _speed     = speed;
        // Lifetime is handled by Projectile.OnEnable's Invoke — returns to pool automatically
    }

    /// <summary>
    /// Called by ProjectilePool.NotifyHitClientRpc — finds the active visual bullet
    /// closest to hitPos and returns it to the pool, matching the server's bullet removal.
    /// </summary>
    public static void ReturnNearest(Vector2 hitPos)
    {
        BulletVisual closest  = null;
        float        minDist  = float.MaxValue;

        foreach (var bv in _active)
        {
            float d = Vector2.Distance(bv.transform.position, hitPos);
            if (d < minDist) { minDist = d; closest = bv; }
        }

        if (closest == null) return;

        if (ProjectilePool.Instance != null)
            ProjectilePool.Instance.ReturnProjectile(closest.gameObject);
        else
            Destroy(closest.gameObject);
    }

    // ── Movement ──────────────────────────────────────────────────────────

    void Update()
    {
        transform.Translate(_direction * _speed * Time.deltaTime, Space.World);
    }
}
