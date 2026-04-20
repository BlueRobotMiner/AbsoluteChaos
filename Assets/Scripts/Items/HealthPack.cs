using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawned by the HealthPackRain card via ItemSpawner.SpawnItem().
/// Falls with physics, freezes on landing, picked up via trigger collider.
/// Prefab requirements: NetworkObject, Rigidbody2D, two BoxCollider2Ds
///   (one solid for ground landing, one Is Trigger for pickup detection).
/// </summary>
public class HealthPack : NetworkBehaviour
{
    [SerializeField] int _healAmount = 20;

    Rigidbody2D _rb;
    bool        _landed;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();

        // Ignore collisions between the solid collider and all player body parts
        // so the pack doesn't get knocked around when a player walks into it
        Collider2D solidCol = null;
        foreach (var col in GetComponents<Collider2D>())
        {
            if (!col.isTrigger) { solidCol = col; break; }
        }

        if (solidCol != null)
        {
            foreach (var pc in FindObjectsOfType<PlayerController>(true))
                foreach (var col in pc.GetComponentsInChildren<Collider2D>(true))
                    Physics2D.IgnoreCollision(solidCol, col, true);
        }
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (_landed) return;
        _landed = true;

        if (_rb != null)
        {
            _rb.velocity        = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.isKinematic     = true;
        }
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer) return;

        var ph = col.GetComponentInParent<PlayerHealth>();
        if (ph == null) return;

        ph.HealServerRpc(_healAmount);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayHealthPickupSFX();

        ItemSpawner.Instance?.NotifyItemPickedUp(NetworkObject);
        NetworkObject.Despawn(true);
    }
}
