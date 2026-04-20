using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Object Pool pattern — reuses a fixed set of projectile GameObjects.
/// Also acts as the network relay for broadcasting bullet hit positions to all clients,
/// since Projectile is not a NetworkBehaviour and can't call ClientRpcs directly.
/// Add a NetworkObject component to this GO in the scene.
/// </summary>
public class ProjectilePool : NetworkBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────

    public static ProjectilePool Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────

    [SerializeField] GameObject _projectilePrefab;
    public GameObject ProjectilePrefab => _projectilePrefab;
    [SerializeField] int _poolSize = 20;

    // ── Pool ─────────────────────────────────────────────────────────────

    readonly Queue<GameObject> _pool = new Queue<GameObject>();

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        WarmUp();
    }

    void WarmUp()
    {
        for (int i = 0; i < _poolSize; i++)
        {
            GameObject obj = Instantiate(_projectilePrefab, transform);
            obj.SetActive(false);
            _pool.Enqueue(obj);
        }
    }

    // ── Pool API ──────────────────────────────────────────────────────────

    public GameObject GetProjectile()
    {
        if (_pool.Count > 0)
        {
            GameObject obj = _pool.Dequeue();
            obj.SetActive(true);
            return obj;
        }
        Debug.LogWarning("[ProjectilePool] Pool exhausted — instantiating overflow.");
        return Instantiate(_projectilePrefab);
    }

    public void ReturnProjectile(GameObject proj)
    {
        proj.SetActive(false);
        proj.transform.SetParent(transform);
        _pool.Enqueue(proj);
    }

    // ── Hit sync ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Projectile (server) when a bullet hits something.
    /// Tells all non-server clients to remove the nearest visual bullet at that position.
    /// </summary>
    public void BroadcastHit(Vector2 hitPos)
    {
        if (IsServer)
            NotifyHitClientRpc(hitPos);
    }

    [ClientRpc]
    void NotifyHitClientRpc(Vector2 hitPos)
    {
        if (IsServer) return;   // server already handled it via the real projectile
        BulletVisual.ReturnNearest(hitPos);
        AudioManager.Instance?.PlayHitSFX();
    }

    // ── Explosion sync ────────────────────────────────────────────────────

    public void BroadcastExplosion(Vector2 pos)
    {
        if (IsServer)
            NotifyExplosionClientRpc(pos);
    }

    [ClientRpc]
    void NotifyExplosionClientRpc(Vector2 pos)
    {
        if (IsServer) return;
        BulletVisual.ExplodeNearest(pos);
        AudioManager.Instance?.PlayHitSFX();
    }
}
