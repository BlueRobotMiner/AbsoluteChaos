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
    [SerializeField] float      _fireRate       = 0.2f;
    [SerializeField] float      _bulletSpeed    = 20f;   // must match Projectile._speed
    [SerializeField] float      _bulletLifetime = 3f;    // must match Projectile._lifetime
    [SerializeField] GameObject _bulletVisualPrefab;     // assign your bullet prefab here

    [Header("Punch (no gun)")]
    [SerializeField] float _punchCooldown   = 0.5f;   // seconds between punches
    [SerializeField] float _punchRange      = 1.2f;   // radius of hit detection sphere
    [SerializeField] int   _punchDamage     = 20;     // HP removed per punch
    [SerializeField] float _punchLunge      = 8f;     // forward impulse on the punching player

    float _nextFireTime;
    float _nextPunchTime;
    Gun   _heldGun;
    bool  _shootingEnabled = true;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Clear stale gun reference from the previous scene — the gun NetworkObject
        // is destroyed on scene unload so the C# reference becomes a dead Unity object
        _heldGun = null;
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
            Vector2 throwDir = Vector2.right;
            if (Camera.main != null && _heldGun.GetFirePoint() != null)
            {
                Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                throwDir = (mouseWorld - (Vector2)_heldGun.GetFirePoint().position).normalized;
            }
            ThrowWeaponServerRpc(throwDir);
        }
    }

    // ── ServerRpcs ────────────────────────────────────────────────────────

    [ServerRpc]
    void FireServerRpc(Vector2 spawnPos, Vector2 direction)
    {
        // Authoritative physics bullet — server only
        if (ProjectilePool.Instance != null)
        {
            GameObject proj = ProjectilePool.Instance.GetProjectile();
            proj.transform.position = spawnPos;
            if (proj.TryGetComponent(out Projectile p))
                p.Launch(direction, OwnerClientId);
        }
        else
        {
            Debug.LogWarning("[PlayerCombat] ProjectilePool not found in scene.");
        }

        // Broadcast visual bullet to every client (including host)
        ShowBulletClientRpc(spawnPos, direction);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayShootSFX();
    }

    [ServerRpc]
    void ThrowWeaponServerRpc(Vector2 throwDir)
    {
        // Find the gun held by this player on the server and throw it
        var guns = FindObjectsOfType<Gun>();
        foreach (var gun in guns)
        {
            if (gun.IsHeldBy(NetworkObject.NetworkObjectId))
            {
                gun.Throw(throwDir);
                _heldGun = null;
                return;
            }
        }
    }

    [ServerRpc]
    void PunchServerRpc(Vector2 punchDir)
    {
        // Lunge the punching player's body toward the mouse
        var pc = GetComponent<PlayerController>();
        if (pc != null && pc.rb != null)
            pc.rb.AddForce(punchDir * _punchLunge, ForceMode2D.Impulse);

        // Hit any enemy PlayerHealth within punch range
        Vector2 punchOrigin = pc != null && pc.rb != null
            ? pc.rb.position
            : (Vector2)transform.position;

        var hits = Physics2D.OverlapCircleAll(punchOrigin + punchDir * (_punchRange * 0.5f),
                                              _punchRange * 0.5f);
        foreach (var hit in hits)
        {
            var ph = hit.GetComponentInParent<PlayerHealth>();
            if (ph == null) continue;
            if (ph.NetworkObject == NetworkObject) continue;  // don't hit self
            ph.TakeDamageServerRpc(_punchDamage);
        }
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
    void ShowBulletClientRpc(Vector2 spawnPos, Vector2 direction)
    {
        // Server already has the real physics bullet from the pool — skip the visual copy
        if (IsServer) return;

        // Use assigned visual prefab, or fall back to the pool's prefab automatically
        if (ProjectilePool.Instance == null)
        {
            Debug.LogWarning("[PlayerCombat] ProjectilePool not in scene — no visual bullet.");
            return;
        }

        // Reuse a pooled bullet for the visual — same object, no extra allocations
        GameObject visual = ProjectilePool.Instance.GetProjectile();
        visual.transform.position = spawnPos;

        // Kill physics — BulletVisual drives movement via transform
        if (visual.TryGetComponent(out Rigidbody2D rb))
        {
            rb.isKinematic = true;
            rb.velocity    = Vector2.zero;
        }

        // Keep trigger collider so OnTriggerEnter2D fires on hit — disable solid colliders only
        foreach (var col in visual.GetComponentsInChildren<Collider2D>())
            if (!col.isTrigger) col.enabled = false;

        // Read speed from the Projectile component so it always matches the server bullet
        float speed = _bulletSpeed;
        if (visual.TryGetComponent(out Projectile projData))
            speed = projData.Speed;

        // BulletVisual drives movement and returns to pool on hit or lifetime expiry
        if (!visual.TryGetComponent(out BulletVisual bv))
            bv = visual.AddComponent<BulletVisual>();

        bv.Init(direction, speed);
    }
}
