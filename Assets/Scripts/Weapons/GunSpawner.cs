using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative gun pool manager.
/// Spawns up to _maxGuns NetworkObject guns at random spawn points.
/// Monitors for guns that fall below _killY and auto-respawns them after a delay.
/// </summary>
public class GunSpawner : NetworkBehaviour
{
    public static GunSpawner Instance { get; private set; }

    [Header("Prefabs & Spawn Points")]
    [SerializeField] GameObject[] _gunPrefabs;
    [SerializeField] Transform[]  _spawnPoints;

    [Header("Pool Settings")]
    [SerializeField] int   _maxGuns      = 1;      // guns alive at the same time
    [SerializeField] float _respawnDelay = 3f;     // seconds before a replacement drops

    [Header("Physics")]
    [SerializeField] float _gravityScale = 0.3f;
    [SerializeField] float _killY        = -10f;   // Y threshold — gun below this is removed

    readonly List<Gun> _activeGuns = new();
    bool _respawnPending;

    // ── Singleton ─────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        for (int i = _activeGuns.Count; i < _maxGuns; i++)
            SpawnGun();
    }

    // ── Server update — watch for out-of-world guns ───────────────────────

    void Update()
    {
        if (!IsServer) return;

        for (int i = _activeGuns.Count - 1; i >= 0; i--)
        {
            var gun = _activeGuns[i];

            // Gun was despawned externally or destroyed
            if (gun == null || !gun.IsSpawned)
            {
                _activeGuns.RemoveAt(i);
                TryQueueRespawn();
                continue;
            }

            // Gun fell out of the world
            if (gun.transform.position.y < _killY)
            {
                _activeGuns.RemoveAt(i);
                gun.DespawnSelf();
                TryQueueRespawn();
            }
        }
    }

    // ── Spawn ─────────────────────────────────────────────────────────────

    void SpawnGun()
    {
        if (!IsServer) return;
        if (_gunPrefabs == null || _gunPrefabs.Length == 0) return;
        if (_spawnPoints == null || _spawnPoints.Length == 0) return;
        if (_activeGuns.Count >= _maxGuns) return;

        int prefabIdx = Random.Range(0, _gunPrefabs.Length);
        int spawnIdx  = Random.Range(0, _spawnPoints.Length);
        Vector3 pos   = new Vector3(_spawnPoints[spawnIdx].position.x,
                                    _spawnPoints[spawnIdx].position.y, 0f);

        var go = Instantiate(_gunPrefabs[prefabIdx], pos, Quaternion.identity);

        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale           = _gravityScale;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        var netObj = go.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();

        var gun = go.GetComponent<Gun>();
        if (gun != null)
            _activeGuns.Add(gun);
    }

    // ── Respawn queue — prevents double-spawn from rapid events ──────────

    void TryQueueRespawn()
    {
        if (_respawnPending) return;
        _respawnPending = true;
        StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(_respawnDelay);
        _respawnPending = false;

        while (_activeGuns.Count < _maxGuns)
            SpawnGun();
    }
}
