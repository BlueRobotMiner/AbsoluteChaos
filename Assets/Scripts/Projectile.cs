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
    [SerializeField] float _speed  = 20f;
    [SerializeField] int   _damage = 25;

    [Header("Explosive Rounds")]
    [SerializeField] float          _explosionRadius = 2f;
    [SerializeField] int            _explosionDamage = 15;   // AoE damage (in addition to direct hit)
    [SerializeField] ExplosionEffect _explosionEffect;

    [Header("Ricochet")]
    [SerializeField] LayerMask _wallLayer;
    [SerializeField] LayerMask _groundLayer;
    [SerializeField] LayerMask _objectLayer;

    public float Speed => _speed;

    Rigidbody2D   _rb;
    SpriteRenderer _bulletSprite;
    ulong         _ownerClientId;
    PlayerCombat  _ownerCombat;
    int           _activeDamage;
    bool          _explosive;
    bool          _exploding;
    int           _bouncesRemaining;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb           = GetComponent<Rigidbody2D>();
        _bulletSprite = GetComponent<SpriteRenderer>();
        _activeDamage = _damage;
    }

    void OnDisable()
    {
        _rb.velocity        = Vector2.zero;
        _rb.gravityScale    = 0f;
        _rb.simulated       = true;
        _activeDamage       = _damage;
        _explosive          = false;
        _exploding          = false;
        _bouncesRemaining   = 0;
        if (_bulletSprite != null) _bulletSprite.enabled = true;
        _explosionEffect?.ResetEffect();
    }

    void FixedUpdate()
    {
        // Ricochet wall check — server only, cast ahead along velocity
        if (_bouncesRemaining <= 0) return;
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;

        float dist = _rb.velocity.magnitude * Time.fixedDeltaTime * 2f;
        if (dist < 0.001f) return;

        LayerMask bounceMask = _wallLayer | _groundLayer | _objectLayer;
        var hit = Physics2D.Raycast(transform.position, _rb.velocity.normalized, dist, bounceMask);
        if (hit.collider != null)
        {
            _rb.velocity = Vector2.Reflect(_rb.velocity, hit.normal);
            _bouncesRemaining--;
            _ownerCombat?.BroadcastRicochet(_rb.position, _rb.velocity);
        }
    }

    // ── API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerCombat (server-side).
    /// gravityScale: 0 = flat shot, positive = bullet drop.
    /// speedOverride / damageOverride: 0 = use prefab defaults.
    /// explosive: AoE damage ring on hit.
    /// bouncesRemaining: how many times bullet can ricochet off walls.
    /// </summary>
    public void Launch(Vector2 direction, ulong ownerClientId,
                       float gravityScale  = 0f,
                       float speedOverride = 0f,
                       int   damageOverride = 0,
                       bool  explosive     = false,
                       int   bounces       = 0,
                       PlayerCombat ownerCombat = null)
    {
        _ownerClientId    = ownerClientId;
        _ownerCombat      = ownerCombat;
        _rb.gravityScale  = gravityScale;
        _explosive        = explosive;
        _bouncesRemaining = bounces;

        float spd    = speedOverride > 0f ? speedOverride : _speed;
        _rb.velocity = direction.normalized * spd;

        _activeDamage = damageOverride > 0 ? damageOverride : _damage;
    }

    // ── Collision ─────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;
        if (_exploding) return;

        // Killboxes are death zones for players only — bullets fly straight through
        if (col.GetComponent<KillBox>() != null) return;

        PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();

        if (ph != null)
        {
            // Don't damage the shooter
            if (ph.OwnerClientId == _ownerClientId) return;

            ph.TakeDamageServerRpc(_activeDamage);

            if (_explosive)
                DealExplosionDamage(transform.position, ph);

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayHitSFX();
        }
        else
        {
            // Non-player surface: skip if ricochet still has bounces and not explosive
            if (!_explosive && _bouncesRemaining > 0) return;
        }

        if (_explosive)
            TriggerExplosionAndReturn();
        else
        {
            ProjectilePool.Instance?.BroadcastHit(_rb.position);
            ReturnToPool();
        }
    }

    void TriggerExplosionAndReturn()
    {
        _exploding            = true;
        _rb.simulated         = false;
        if (_bulletSprite != null) _bulletSprite.enabled = false;
        ProjectilePool.Instance?.BroadcastExplosion(_rb.position);

        if (_explosionEffect != null)
            _explosionEffect.Explode(ReturnToPool);
        else
            ReturnToPool();
    }

    void DealExplosionDamage(Vector2 origin, PlayerHealth directHit)
    {
        var hits = Physics2D.OverlapCircleAll(origin, _explosionRadius);
        foreach (var hit in hits)
        {
            var victim = hit.GetComponentInParent<PlayerHealth>();
            if (victim == null) continue;
            if (victim == directHit) continue;        // already took direct damage
            if (victim.OwnerClientId == _ownerClientId) continue;
            victim.TakeDamageServerRpc(_explosionDamage);
        }
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
