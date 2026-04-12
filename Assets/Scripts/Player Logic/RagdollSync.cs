using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Syncs all child Rigidbody2D rotations from the owner to all other clients.
/// Non-owners run no physics — they just read the streamed rotations.
/// Attach to the root Player GO alongside PlayerController.
/// </summary>
public class RagdollSync : NetworkBehaviour
{
    Rigidbody2D[] _bodies;
    Vector2[]     _positions;
    float[]       _rotations;

    public override void OnNetworkSpawn()
    {
        // Collect every child RB except the root (root is handled by NetworkRigidbody2D)
        var all = GetComponentsInChildren<Rigidbody2D>();
        var list = new System.Collections.Generic.List<Rigidbody2D>();
        foreach (var rb in all)
            if (rb.gameObject != gameObject) list.Add(rb);
        _bodies    = list.ToArray();
        _positions = new Vector2[_bodies.Length];
        _rotations = new float[_bodies.Length];

        if (!IsOwner)
        {
            // Non-owners: kill all physics — positions driven by this script only
            foreach (var rb in _bodies)
            {
                rb.isKinematic = true;
                rb.simulated   = false;
            }
        }
    }

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            // Read current positions + rotations and broadcast to all clients
            for (int i = 0; i < _bodies.Length; i++)
            {
                _positions[i] = _bodies[i].position;
                _rotations[i] = _bodies[i].rotation;
            }

            SubmitStateServerRpc(_positions, _rotations);
        }
    }

    [ServerRpc]
    void SubmitStateServerRpc(Vector2[] positions, float[] rotations)
    {
        ApplyStateClientRpc(positions, rotations);
    }

    [ClientRpc]
    void ApplyStateClientRpc(Vector2[] positions, float[] rotations)
    {
        if (IsOwner) return;

        for (int i = 0; i < _bodies.Length && i < rotations.Length; i++)
        {
            _bodies[i].transform.position = positions[i];
            _bodies[i].transform.rotation = Quaternion.Euler(0f, 0f, rotations[i]);
        }
    }
}
