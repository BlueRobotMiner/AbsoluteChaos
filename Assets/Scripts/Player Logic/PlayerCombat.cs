using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles shooting (LMB) and active card activation (RMB, stubbed for Week 2).
/// Server runs authoritative physics projectile via pool.
/// All clients receive a visual-only bullet via ClientRpc so both screens show the shot.
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    [Header("Shooting")]
    [SerializeField] float _fireRate    = 0.2f;
    [SerializeField] float _bulletSpeed = 20f;

    [Header("Punch (no gun)")]
    [SerializeField] float _punchCooldown   = 0.5f;
    [SerializeField] float _punchRange      = 1.2f;
    [SerializeField] int   _punchDamage     = 20;
    [SerializeField] float _punchLunge      = 8f;
    [SerializeField] float _punchUpForce    = 6f;

    float _nextFireTime;
    float _nextPunchTime;
    Gun   _heldGun;
    bool  _shootingEnabled = true;

    // ── Card-effect state (server only) ───────────────────────────────────
    [HideInInspector] public float baseFireRate;
    [HideInInspector] public float ammoMultiplier = 1f;   // set by AmmoStash card; applied on gun pickup
    bool _explosiveRounds;
    int  _ricochetBounces;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _heldGun     = null;
        baseFireRate = _fireRate;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetHeldGun(Gun gun)
    {
        _heldGun = gun;
    }

    /// <summary>
    /// Called each Update to validate the held gun reference is still live.
    /// Catches the case where the gun NetworkObject was despawned without
    /// going through the normal ClearHolder path (e.g. scene transition).
    /// </summary>
    void ValidateHeldGun()
    {
        if (_heldGun != null && !_heldGun.IsSpawned)
            _heldGun = null;
    }

    public void SetShootingEnabled(bool value)
    {
        _shootingEnabled = value;
        // When re-enabling, push both timers forward so the card-pick click
        // doesn't immediately fire or punch
        if (value)
        {
            _nextFireTime  = Time.time + _fireRate;
            _nextPunchTime = Time.time + _punchCooldown;
        }
    }

    /// <summary>
    /// Broadcasts shooting enable/disable to the owning client.
    /// Call this from the server instead of SetShootingEnabled when you need it to reach the client.
    /// </summary>
    [ClientRpc]
    public void SetShootingEnabledClientRpc(bool enabled)
    {
        if (!IsOwner) return;
        SetShootingEnabled(enabled);
    }

    // ── Update ────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsOwner) return;

        ValidateHeldGun();

        if (_shootingEnabled && Input.GetMouseButtonDown(0) && _heldGun == null
            && Time.time >= _nextPunchTime)
        {
            // No gun — throw a punch toward mouse cursor
            Vector2 punchDir = Vector2.right;
            if (Camera.main != null)
            {
                Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                punchDir = (mouseWorld - (Vector2)transform.position).normalized;
            }
            _nextPunchTime = Time.time + _punchCooldown;
            PunchServerRpc(punchDir);
        }

        if (_shootingEnabled && Input.GetMouseButton(0) && _heldGun != null
            && Time.time >= _nextFireTime)
        {
            if (Camera.main == null)
            {
                Debug.LogWarning("[PlayerCombat] Camera.main is null — tag the camera MainCamera.");
                return;
            }

            Transform firePoint = _heldGun.GetFirePoint();
            if (firePoint == null)
            {
                Debug.LogWarning("[PlayerCombat] Gun has no FirePoint assigned on the prefab.");
                return;
            }

            Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 direction  = (mouseWorld - (Vector2)firePoint.position).normalized;

            _nextFireTime = Time.time + _fireRate;
            FireServerRpc(firePoint.position, direction);
        }

        if (Input.GetMouseButtonDown(1))
            ActivateCardServerRpc();

        // G — throw held weapon in aim direction
        if (_heldGun != null && Input.GetKeyDown(KeyCode.G))
        {
            Vector2 throwDir   = Vector2.right;
            Vector2 gunPos     = _heldGun.transform.position;   // client's current gun position
            if (Camera.main != null && _heldGun.GetFirePoint() != null)
            {
                Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                throwDir = (mouseWorld - (Vector2)_heldGun.GetFirePoint().position).normalized;
            }
            // Send local gun position so the server throws from exactly where this client sees it
            ThrowWeaponServerRpc(throwDir, gunPos);
        }
    }

    // ── ServerRpcs ────────────────────────────────────────────────────────

    [ServerRpc]
    void FireServerRpc(Vector2 spawnPos, Vector2 direction)
    {
        // Ammo check — stop firing when empty, but don't auto-throw
        if (_heldGun != null && !_heldGun.UseAmmo()) return;

        float gravity  = _heldGun != null ? _heldGun.BulletGravityScale  : 0f;
        float speedOvr = _heldGun != null ? _heldGun.BulletSpeedOverride : 0f;
        int   dmgOvr   = _heldGun != null ? _heldGun.BulletDamageOverride : 0;

        if (ProjectilePool.Instance != null)
        {
            GameObject proj = ProjectilePool.Instance.GetProjectile();
            proj.transform.position = spawnPos;
            if (proj.TryGetComponent(out Projectile p))
                p.Launch(direction, OwnerClientId, gravity, speedOvr, dmgOvr,
                         _explosiveRounds, _ricochetBounces, this);
        }
        else
        {
            Debug.LogWarning("[PlayerCombat] ProjectilePool not found in scene.");
        }

        // Broadcast visual bullet to every client — same speed + gravity the physics bullet uses
        ShowBulletClientRpc(spawnPos, direction, speedOvr, gravity);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayShootSFX();
    }

    [ServerRpc]
    void ThrowWeaponServerRpc(Vector2 throwDir, Vector2 clientGunPos)
    {
        var guns = FindObjectsOfType<Gun>();
        foreach (var gun in guns)
        {
            if (gun.IsHeldBy(NetworkObject.NetworkObjectId))
            {
                gun.Throw(throwDir, clientGunPos);
                if (gun.IsEmpty()) gun.ScheduleDespawn(5f);
                _heldGun = null;
                return;
            }
        }
    }

    [ServerRpc]
    void PunchServerRpc(Vector2 punchDir)
    {
        var pc = GetComponent<PlayerController>();
        if (pc != null && pc.rb != null)
        {
            // Small upward hop + horizontal dash toward aim — vertical is separate so
            // it doesn't fight gravity on steep aim angles
            float hDir = Mathf.Sign(punchDir.x);
            pc.rb.AddForce(new Vector2(hDir * _punchLunge, _punchUpForce), ForceMode2D.Impulse);
        }

        // Trigger arm wind-up + punch-extend animation on the server
        float aimAngle = Mathf.Atan2(punchDir.y, punchDir.x) * Mathf.Rad2Deg;
        var aim = GetComponent<ArmAimController>();
        if (aim != null) aim.TriggerPunch(aimAngle);

        // Hit any enemy PlayerHealth within punch range
        Vector2 punchOrigin = pc != null && pc.rb != null
            ? pc.rb.position
            : (Vector2)transform.position;

        var hits = Physics2D.OverlapCircleAll(punchOrigin + punchDir * (_punchRange * 0.5f),
                                              _punchRange * 0.5f);
        // Use a set so multi-collider ragdolls only take damage once per punch
        var damaged = new System.Collections.Generic.HashSet<PlayerHealth>();
        foreach (var hit in hits)
        {
            var ph = hit.GetComponentInParent<PlayerHealth>();
            if (ph == null) continue;
            if (ph.NetworkObject == NetworkObject) continue;
            if (damaged.Add(ph))
                ph.TakeDamageServerRpc(_punchDamage);
        }

        if (damaged.Count > 0)
            PlayPunchSFXClientRpc();
    }

    [ClientRpc]
    void PlayPunchSFXClientRpc()
    {
        AudioManager.Instance?.PlayPunchSFX();
    }

    [ServerRpc]
    void ActivateCardServerRpc()
    {
        Debug.Log($"[PlayerCombat] Player {OwnerClientId} activated card (stub).");
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a local visual-only bullet on every client screen.
    /// The server's physics projectile handles actual hit detection.
    /// </summary>
    [ClientRpc]
    void ShowBulletClientRpc(Vector2 spawnPos, Vector2 direction, float speedOverride, float gravityScale)
    {
        // Server already has the real physics bullet from the pool — skip the visual copy
        if (IsServer) return;

        if (ProjectilePool.Instance == null)
        {
            Debug.LogWarning("[PlayerCombat] ProjectilePool not in scene — no visual bullet.");
            return;
        }

        GameObject visual = ProjectilePool.Instance.GetProjectile();
        visual.transform.position = spawnPos;

        // Kill physics — BulletVisual drives movement via transform
        if (visual.TryGetComponent(out Rigidbody2D rb))
        {
            rb.isKinematic = true;
            rb.velocity    = Vector2.zero;
        }

        // Visual-only bullet — disable all colliders; OnTriggerEnter2D returns early on clients anyway
        foreach (var col in visual.GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        // Use the override the server passed; fall back to the prefab's default speed
        float speed = speedOverride > 0f ? speedOverride
                    : visual.TryGetComponent(out Projectile projData) ? projData.Speed
                    : _bulletSpeed;

        if (!visual.TryGetComponent(out BulletVisual bv))
            bv = visual.AddComponent<BulletVisual>();

        // Mirror the exact same speed + gravity the server's physics bullet uses
        bv.Init(direction, speed, gravityScale);
    }

    // ── Ricochet sync ─────────────────────────────────────────────────────

    public void BroadcastRicochet(Vector2 bouncePos, Vector2 newVelocity)
    {
        if (!IsServer) return;
        RicochetClientRpc(bouncePos, newVelocity);
    }

    [ClientRpc]
    void RicochetClientRpc(Vector2 bouncePos, Vector2 newVelocity)
    {
        if (IsServer) return;
        BulletVisual.RedirectNearest(bouncePos, newVelocity);
    }

    // ── Card effect API (server only) ─────────────────────────────────────

    public void SetFireRate(float rate)         => _fireRate = rate;
    public void MultiplyFireRate(float mult)    => _fireRate *= mult;
    public void SetExplosiveRounds(bool value)  => _explosiveRounds = value;
    public void AddRicochetBounces(int count)   => _ricochetBounces += count;

    public void ResetBaseStats()
    {
        _fireRate          = baseFireRate;
        _explosiveRounds   = false;
        _ricochetBounces   = 0;
        ammoMultiplier     = 1f;
    }
}
