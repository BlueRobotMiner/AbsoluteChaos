using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Object Pool pattern — reuses a fixed set of projectile GameObjects instead of
/// Instantiate/Destroy per shot. Satisfies the "Additional Design Pattern" requirement.
/// Place this on a persistent GameObject in the Map scenes (or on the NetworkManager GO).
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────

    public static ProjectilePool Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────

    [SerializeField] GameObject _projectilePrefab;
    [SerializeField] int        _poolSize = 20;

    // ── Pool ─────────────────────────────────────────────────────────────

    readonly Queue<GameObject> _pool = new Queue<GameObject>();

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
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

    /// <summary>
    /// Returns an inactive projectile from the pool, activating it.
    /// If the pool is empty a new instance is created as an overflow fallback.
    /// </summary>
    public GameObject GetProjectile()
    {
        if (_pool.Count > 0)
        {
            GameObject obj = _pool.Dequeue();
            obj.SetActive(true);
            return obj;
        }

        // Overflow — create a new one rather than silently failing
        Debug.LogWarning("[ProjectilePool] Pool exhausted — instantiating overflow projectile.");
        return Instantiate(_projectilePrefab);
    }

    /// <summary>
    /// Returns a projectile to the pool. Called by Projectile itself on expiry or hit.
    /// </summary>
    public void ReturnProjectile(GameObject proj)
    {
        proj.SetActive(false);
        proj.transform.SetParent(transform);
        _pool.Enqueue(proj);
    }
}
