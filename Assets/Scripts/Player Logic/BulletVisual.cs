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

    const float MaxLifetime = 8f;   // fallback: return to pool if server hit RPC never arrives

    Vector2 _velocity;
    float   _gravityScale;
    bool    _exploding;
    float   _spawnTime;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void OnEnable()  { _active.Add(this); _exploding = false; _velocity = Vector2.zero; _spawnTime = Time.time; }
    void OnDisable() => _active.Remove(this);

    // ── API ───────────────────────────────────────────────────────────────

    public void Init(Vector2 direction, float speed, float gravityScale = 0f)
    {
        _velocity     = direction.normalized * speed;
        _gravityScale = gravityScale;
        // Lifetime is handled by Projectile.OnEnable's Invoke — returns to pool automatically
    }

    /// <summary>
    /// Called when the server physics bullet bounces — redirects the nearest visual bullet
    /// so P2's screen matches the server's bounce trajectory.
    /// </summary>
    public static void RedirectNearest(Vector2 bouncePos, Vector2 newVelocity)
    {
        BulletVisual closest = null;
        float minDist = float.MaxValue;
        foreach (var bv in _active)
        {
            float d = Vector2.Distance(bv.transform.position, bouncePos);
            if (d < minDist) { minDist = d; closest = bv; }
        }
        if (closest != null) closest._velocity = newVelocity;
    }

    /// <summary>
    /// Called by ProjectilePool.NotifyExplosionClientRpc — finds the nearest visual bullet,
    /// stops its movement, and plays its child ExplosionEffect before returning to pool.
    /// </summary>
    public static void ExplodeNearest(Vector2 pos)
    {
        BulletVisual closest = null;
        float minDist = float.MaxValue;
        foreach (var bv in _active)
        {
            if (bv._exploding) continue;
            float d = Vector2.Distance(bv.transform.position, pos);
            if (d < minDist) { minDist = d; closest = bv; }
        }
        if (closest == null) return;

        closest._exploding = true;
        closest._velocity  = Vector2.zero;

        var effect = closest.GetComponentInChildren<ExplosionEffect>(true);
        if (effect != null)
        {
            effect.Explode(() =>
            {
                if (ProjectilePool.Instance != null)
                    ProjectilePool.Instance.ReturnProjectile(closest.gameObject);
                else
                    closest.gameObject.SetActive(false);
            });
        }
        else
        {
            if (ProjectilePool.Instance != null)
                ProjectilePool.Instance.ReturnProjectile(closest.gameObject);
            else
                closest.gameObject.SetActive(false);
        }
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
        if (Time.time - _spawnTime > MaxLifetime)
        {
            if (ProjectilePool.Instance != null) ProjectilePool.Instance.ReturnProjectile(gameObject);
            else gameObject.SetActive(false);
            return;
        }

        // Mirror Rigidbody2D gravity so visual bullet arcs match the server physics bullet
        _velocity += (Vector2)Physics2D.gravity * _gravityScale * Time.deltaTime;
        transform.Translate(_velocity * Time.deltaTime, Space.World);
    }
}
