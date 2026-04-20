using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[System.Serializable]
public class ItemSpawnConfig
{
    public CardId     card;
    public GameObject prefab;
    [Tooltip("Max items on map with 1 card stack. Each additional stack adds 1 more.")]
    public int        baseMaxOnMap   = 1;
    [Tooltip("Seconds between spawns with 1 card stack. Each additional stack halves this.")]
    public float      baseSpawnDelay = 25f;
}

/// <summary>
/// Server-authoritative spawner for guns and card-triggered items.
///
/// Gun section: maintains one gun per player (+_extraGuns). Respawns on a long delay.
/// Item section: each ItemSpawnConfig is activated by RegisterCardStack(). More stacks
///               from duplicate cards → more items allowed on map + faster respawn.
/// </summary>
public class ItemSpawner : NetworkBehaviour
{
    public static ItemSpawner Instance { get; private set; }

    [Header("Gun Prefabs & Spawn Points")]
    [SerializeField] GameObject[] _gunPrefabs;
    [SerializeField] Transform[]  _spawnPoints;

    [Header("Gun Count")]
    [Tooltip("Extra guns beyond one-per-player. 0 = exactly one gun per player.")]
    [SerializeField] int _extraGuns = 0;

    [Header("Gun Timing")]
    [SerializeField] float _gunRespawnDelay = 12f;
    [SerializeField] float _pointCooldown   = 1f;

    [Header("Gun Physics")]
    [SerializeField] float _gravityScale = 0.3f;
    [SerializeField] float _killY        = -10f;

    [Header("Card-Triggered Items")]
    [SerializeField] ItemSpawnConfig[] _itemConfigs;

    // ── Per-point tracking (server only) ──────────────────────────────────
    NetworkObject[] _pointItems;
    float[]         _pointCooldowns;

    // ── Gun tracking ──────────────────────────────────────────────────────
    readonly List<NetworkObject> _activeGuns = new();
    bool _gunRespawnPending;

    // ── Item tracking (server only) ───────────────────────────────────────
    class ItemState
    {
        public int                  stacks;
        public float                nextSpawnTime;
        public readonly List<NetworkObject> active = new();

        public int   MaxOnMap(ItemSpawnConfig cfg) => cfg.baseMaxOnMap + (stacks - 1);
        public float Delay   (ItemSpawnConfig cfg) => cfg.baseSpawnDelay / stacks;
    }
    ItemState[] _itemStates;

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

        int n = _spawnPoints != null ? _spawnPoints.Length : 0;
        _pointItems     = new NetworkObject[n];
        _pointCooldowns = new float[n];

        int m = _itemConfigs != null ? _itemConfigs.Length : 0;
        _itemStates = new ItemState[m];
        for (int i = 0; i < m; i++) _itemStates[i] = new ItemState();

        if (FindObjectOfType<MapManager>() == null)
            FillGuns();
    }

    /// <summary>Called by MapManager after the countdown so guns drop at round start.</summary>
    public void SpawnInitialGuns()
    {
        if (!IsServer) return;
        FillGuns();
    }

    // ── Card stack API (called by MapManager) ─────────────────────────────

    /// <summary>
    /// Registers one card stack for a player. Each call increases the max items on
    /// the map and halves the spawn interval for that item type.
    /// Call ClearCardStacks() at the start of ApplyAllCardEffects to reset first.
    /// </summary>
    public void RegisterCardStack(CardId card)
    {
        if (!IsServer || _itemConfigs == null) return;
        for (int i = 0; i < _itemConfigs.Length; i++)
        {
            if (_itemConfigs[i].card != card) continue;
            _itemStates[i].stacks++;
            // Reset timer so first spawn doesn't wait a full cycle after registering
            if (_itemStates[i].stacks == 1)
                _itemStates[i].nextSpawnTime = Time.time + _itemConfigs[i].baseSpawnDelay;
            return;
        }
    }

    /// <summary>Resets all card stacks to zero. Call before re-applying cards each round.</summary>
    public void ClearCardStacks()
    {
        if (_itemStates == null) return;
        foreach (var state in _itemStates)
        {
            state.stacks        = 0;
            state.nextSpawnTime = 0f;
        }
    }

    // ── Server Update ─────────────────────────────────────────────────────

    void Update()
    {
        if (!IsServer || _pointItems == null) return;

        TickGuns();
        TickItems();
    }

    void TickGuns()
    {
        bool gunLost = false;

        for (int i = 0; i < _pointItems.Length; i++)
        {
            var item = _pointItems[i];
            if (item == null) continue;

            bool despawned = !item.IsSpawned;
            bool fell      = !despawned && item.transform.position.y < _killY;
            if (!despawned && !fell) continue;

            if (fell) item.Despawn(true);

            bool wasGun = _activeGuns.Remove(item);
            _pointItems[i]     = null;
            _pointCooldowns[i] = Time.time + _pointCooldown;
            if (wasGun) gunLost = true;
        }

        for (int i = _activeGuns.Count - 1; i >= 0; i--)
        {
            var gun = _activeGuns[i];
            if (gun == null || !gun.IsSpawned) { _activeGuns.RemoveAt(i); gunLost = true; }
        }

        if (gunLost) TryQueueGunRespawn();
    }

    void TickItems()
    {
        if (_itemConfigs == null) return;

        for (int i = 0; i < _itemConfigs.Length; i++)
        {
            var cfg   = _itemConfigs[i];
            var state = _itemStates[i];

            if (state.stacks == 0) continue;

            // Clean up despawned entries
            state.active.RemoveAll(no => no == null || !no.IsSpawned);

            if (state.active.Count >= state.MaxOnMap(cfg)) continue;
            if (Time.time < state.nextSpawnTime) continue;
            if (cfg.prefab == null) continue;

            var netObj = SpawnItem(cfg.prefab);
            if (netObj != null)
            {
                state.active.Add(netObj);
                state.nextSpawnTime = Time.time + state.Delay(cfg);
            }
        }
    }

    // ── Gun fill ──────────────────────────────────────────────────────────

    int GunCap() => Mathf.Max(1,
        (NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1)
        + _extraGuns);

    void FillGuns()
    {
        int cap = GunCap();
        while (_activeGuns.Count < cap)
            if (!SpawnGun()) break;
    }

    bool SpawnGun()
    {
        if (_gunPrefabs == null || _gunPrefabs.Length == 0) return false;
        int pointIdx = GetAvailablePoint();
        if (pointIdx < 0) return false;

        var netObj = SpawnAtPoint(_gunPrefabs[Random.Range(0, _gunPrefabs.Length)], pointIdx);
        if (netObj == null) return false;

        var rb = netObj.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.gravityScale = _gravityScale; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; }

        _activeGuns.Add(netObj);
        return true;
    }

    void TryQueueGunRespawn()
    {
        if (_gunRespawnPending || _activeGuns.Count >= GunCap()) return;
        _gunRespawnPending = true;
        StartCoroutine(GunRespawnCoroutine());
    }

    IEnumerator GunRespawnCoroutine()
    {
        yield return new WaitForSeconds(_gunRespawnDelay);
        _gunRespawnPending = false;
        FillGuns();
    }

    // ── One-shot item spawn (legacy / direct calls) ───────────────────────

    public NetworkObject SpawnItem(GameObject prefab)
    {
        if (!IsServer || prefab == null) return null;
        int idx = GetAvailablePoint();
        if (idx < 0) return null;
        return SpawnAtPoint(prefab, idx);
    }

    public NetworkObject SpawnItemAt(GameObject prefab, int spawnPointIndex)
    {
        if (!IsServer || prefab == null) return null;
        if (_spawnPoints == null || spawnPointIndex < 0 || spawnPointIndex >= _spawnPoints.Length) return null;
        if (IsPointOccupied(spawnPointIndex)) return null;
        return SpawnAtPoint(prefab, spawnPointIndex);
    }

    // ── Pickup notification ───────────────────────────────────────────────

    public void NotifyItemPickedUp(NetworkObject item)
    {
        if (!IsServer) return;
        FreePoint(item);
        if (_itemStates != null)
            foreach (var state in _itemStates)
                state.active.Remove(item);
    }

    // ── Core spawn ────────────────────────────────────────────────────────

    NetworkObject SpawnAtPoint(GameObject prefab, int pointIdx)
    {
        Vector3 pos = new(_spawnPoints[pointIdx].position.x, _spawnPoints[pointIdx].position.y, 0f);
        var go     = Instantiate(prefab, pos, Quaternion.identity);
        var netObj = go.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogWarning($"[ItemSpawner] {prefab.name} has no NetworkObject.");
            Destroy(go);
            return null;
        }

        netObj.Spawn();
        _pointItems[pointIdx]     = netObj;
        _pointCooldowns[pointIdx] = 0f;
        return netObj;
    }

    // ── Point helpers ─────────────────────────────────────────────────────

    int GetAvailablePoint()
    {
        if (_spawnPoints == null) return -1;
        var free = new List<int>();
        for (int i = 0; i < _spawnPoints.Length; i++)
            if (!IsPointOccupied(i) && Time.time >= _pointCooldowns[i])
                free.Add(i);
        return free.Count == 0 ? -1 : free[Random.Range(0, free.Count)];
    }

    bool IsPointOccupied(int idx) =>
        _pointItems != null && _pointItems[idx] != null && _pointItems[idx].IsSpawned;

    void FreePoint(NetworkObject item)
    {
        if (_pointItems == null) return;
        for (int i = 0; i < _pointItems.Length; i++)
        {
            if (_pointItems[i] != item) continue;
            _pointItems[i]     = null;
            _pointCooldowns[i] = Time.time + _pointCooldown;
            return;
        }
    }

    public int SpawnPointCount => _spawnPoints != null ? _spawnPoints.Length : 0;
}
