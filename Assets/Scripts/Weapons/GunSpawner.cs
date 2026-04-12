using Unity.Netcode;
using UnityEngine;

public class GunSpawner : NetworkBehaviour
{
    [SerializeField] GameObject[] _gunPrefabs;
    [SerializeField] Transform[]  _spawnPoints;
    [SerializeField] float        _gravityScale = 0.3f;

    public override void OnNetworkSpawn()
    {
        // Only the server spawns the gun — NGO replicates it to all clients automatically
        if (!IsServer) return;

        if (_gunPrefabs == null || _gunPrefabs.Length == 0) return;
        if (_spawnPoints == null || _spawnPoints.Length == 0) return;

        int prefabIdx = Random.Range(0, _gunPrefabs.Length);
        int spawnIdx  = Random.Range(0, _spawnPoints.Length);

        Vector3 pos = new Vector3(
            _spawnPoints[spawnIdx].position.x,
            _spawnPoints[spawnIdx].position.y,
            0f);

        var go = Instantiate(_gunPrefabs[prefabIdx], pos, Quaternion.identity);

        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale           = _gravityScale;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        // Spawn as a NetworkObject — server owns it, all clients get it automatically
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();
    }
}
