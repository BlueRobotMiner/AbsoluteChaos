using UnityEngine;

/// <summary>
/// Behaviour on each pooled projectile prefab.
/// Server-authoritative: only runs hit detection on the server.
/// Automatically returns to pool after lifetime or on hit.
/// Required on prefab: Rigidbody2D, Collider2D (Is Trigger = true).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] float _speed    = 20f;
    [SerializeField] int   _damage   = 25;
    [SerializeField] float _lifetime = 3f;

    public float Speed    => _speed;
    public float Lifetime => _lifetime;

    Rigidbody2D _rb;
    ulong       _ownerClientId;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        // Auto-return to pool after lifetime expires
        CancelInvoke(nameof(ReturnToPool));
        Invoke(nameof(ReturnToPool), _lifetime);
    }

    void OnDisable()
    {
        CancelInvoke(nameof(ReturnToPool));
        _rb.velocity = Vector2.zero;
    }

    // ── API ───────────────────────────────────────────────────────────────

    /// <summary>Called by PlayerCombat (server-side) to set velocity and owner.</summary>
    public void Launch(Vector2 direction, ulong ownerClientId)
    {
        _ownerClientId = ownerClientId;
        _rb.velocity   = direction.normalized * _speed;
    }

    // ── Collision ─────────────────────────────────────────────────────────

    // Only process hits on the server — authority sits there
    void OnTriggerEnter2D(Collider2D col)
    {
        // Projectiles are not NetworkObjects — authority lives on server only
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;

        PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();

        if (ph != null)
        {
            // Don't damage the shooter
            if (ph.OwnerClientId == _ownerClientId) return;

            ph.TakeDamageServerRpc(_damage);

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayHitSFX();
        }

        // Tell all clients to remove the nearest visual bullet at this position
        ProjectilePool.Instance?.BroadcastHit(_rb.position);

        // Return server bullet to pool
        ReturnToPool();
    }

    // ── Pool return ───────────────────────────────────────────────────────

    void ReturnToPool()
    {
        if (ProjectilePool.Instance != null)
            ProjectilePool.Instance.ReturnProjectile(gameObject);
        else
            gameObject.SetActive(false);
    }
}
