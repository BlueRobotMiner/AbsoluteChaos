using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles shooting (LMB) and active card activation (RMB, stubbed for Week 2).
/// Only the owning client reads input; actual projectile spawn runs on the server via ServerRpc.
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    [Header("Shooting")]
    [SerializeField] float     _fireRate  = 0.2f;   // seconds between shots
    [SerializeField] Transform _firePoint;           // muzzle position / direction

    float _nextFireTime;

    // ── Update ────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsOwner) return;

        if (Input.GetMouseButton(0) && Time.time >= _nextFireTime)
        {
            _nextFireTime = Time.time + _fireRate;
            FireServerRpc(_firePoint.position, _firePoint.right);
        }

        // RMB — active card stub (Week 2)
        if (Input.GetMouseButtonDown(1))
            ActivateCardServerRpc();
    }

    // ── ServerRpcs ────────────────────────────────────────────────────────

    [ServerRpc]
    void FireServerRpc(Vector3 spawnPos, Vector3 direction)
    {
        if (ProjectilePool.Instance == null) return;

        GameObject proj = ProjectilePool.Instance.GetProjectile();
        proj.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);

        Projectile p = proj.GetComponent<Projectile>();
        if (p != null)
            p.Launch(direction, OwnerClientId);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayShootSFX();
    }

    [ServerRpc]
    void ActivateCardServerRpc()
    {
        // Card activation logic — implemented in Week 2
        Debug.Log($"[PlayerCombat] Player {OwnerClientId} activated card (stub).");
    }
}
