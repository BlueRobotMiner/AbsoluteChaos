using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Syncs child Rigidbody2D positions + rotations from server to all clients.
/// Server reads physics state and broadcasts; clients interpolate smoothly toward targets.
/// </summary>
public class RagdollSync : NetworkBehaviour
{
    [Header("Bandwidth throttle")]
    [SerializeField] int   _sendEveryNFrames = 4;      // ~12 sends/sec at 50Hz physics
    [SerializeField] float _posThreshold     = 0.03f;  // min position delta before sending
    [SerializeField] float _rotThreshold     = 2f;     // min rotation delta (degrees) before sending

    [Header("Client interpolation")]
    [SerializeField] float _interpSpeed = 25f;         // lerp speed toward received targets

    Rigidbody2D[] _bodies;

    // Server: last-sent values for delta check
    Vector2[] _lastSentPos;
    float[]   _lastSentRot;
    int       _frameCounter;

    // Client: interpolation targets set by ClientRpc
    Vector2[] _targetPositions;
    float[]   _targetRotations;

    public override void OnNetworkSpawn()
    {
        var all  = GetComponentsInChildren<Rigidbody2D>();
        var list = new System.Collections.Generic.List<Rigidbody2D>();
        foreach (var rb in all)
            if (rb.gameObject != gameObject) list.Add(rb);

        _bodies = list.ToArray();
        int n   = _bodies.Length;

        _lastSentPos     = new Vector2[n];
        _lastSentRot     = new float[n];
        _targetPositions = new Vector2[n];
        _targetRotations = new float[n];

        // Zero out initial spin so joints don't explode on spawn
        for (int i = 0; i < n; i++)
        {
            _bodies[i].angularVelocity = 0f;
            _bodies[i].velocity        = Vector2.zero;
            _bodies[i].rotation        = 0f;

            // Seed interpolation targets to current positions so there's no initial snap
            _targetPositions[i] = _bodies[i].position;
            _targetRotations[i] = _bodies[i].rotation;
        }

        if (!IsServer)
        {
            // Clients are pure renderers — kill all child body physics
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

        if (IsServer)
        {
            ServerBroadcast();
        }
        else
        {
            // Smoothly interpolate each limb toward the last received server state
            float t = _interpSpeed * Time.fixedDeltaTime;
            for (int i = 0; i < _bodies.Length; i++)
            {
                _bodies[i].transform.position = Vector2.Lerp(
                    (Vector2)_bodies[i].transform.position, _targetPositions[i], t);

                _bodies[i].transform.rotation = Quaternion.Slerp(
                    _bodies[i].transform.rotation,
                    Quaternion.Euler(0f, 0f, _targetRotations[i]),
                    t);
            }
        }
    }

    void ServerBroadcast()
    {
        _frameCounter++;
        if (_frameCounter % _sendEveryNFrames != 0) return;

        // Check if anything moved enough to bother sending
        bool dirty = false;
        for (int i = 0; i < _bodies.Length; i++)
        {
            if (Vector2.Distance(_bodies[i].position, _lastSentPos[i]) > _posThreshold ||
                Mathf.Abs(Mathf.DeltaAngle(_bodies[i].rotation, _lastSentRot[i])) > _rotThreshold)
            {
                dirty = true;
                break;
            }
        }

        if (!dirty) return;

        // Snapshot current state and send
        var positions = new Vector2[_bodies.Length];
        var rotations = new float[_bodies.Length];
        for (int i = 0; i < _bodies.Length; i++)
        {
            positions[i] = _bodies[i].position;
            rotations[i] = _bodies[i].rotation;
        }

        System.Array.Copy(positions, _lastSentPos, _bodies.Length);
        System.Array.Copy(rotations, _lastSentRot, _bodies.Length);

        ApplyStateClientRpc(positions, rotations);
    }

    [ClientRpc]
    void ApplyStateClientRpc(Vector2[] positions, float[] rotations)
    {
        // Server already runs physics — only clients need to update their targets
        if (IsServer) return;

        for (int i = 0; i < _bodies.Length && i < positions.Length; i++)
        {
            _targetPositions[i] = positions[i];
            _targetRotations[i] = rotations[i];
        }
    }

    // ── Teleport (server → immediate snap on all clients) ─────────────────

    /// <summary>
    /// Server only. Shifts every ragdoll body by the delta between the current root
    /// position and newRootPos, zeroes velocities, then force-broadcasts so clients
    /// snap instantly instead of lerping from the old position.
    /// </summary>
    public void ServerTeleport(Vector2 newRootPos)
    {
        if (!IsServer || _bodies == null) return;

        // Use the first body as the reference root (index 0 is set in OnNetworkSpawn order)
        // Fall back to transform.position if no bodies yet
        Vector2 currentRoot = _bodies.Length > 0
            ? _bodies[0].position
            : (Vector2)transform.position;

        Vector2 delta = newRootPos - currentRoot;

        foreach (var rb in _bodies)
        {
            rb.position        += delta;
            rb.velocity         = Vector2.zero;
            rb.angularVelocity  = 0f;
        }

        // Force an immediate broadcast that snaps clients (bypasses throttle)
        var positions = new Vector2[_bodies.Length];
        var rotations = new float[_bodies.Length];
        for (int i = 0; i < _bodies.Length; i++)
        {
            positions[i] = _bodies[i].position;
            rotations[i] = _bodies[i].rotation;
            _lastSentPos[i] = positions[i];
            _lastSentRot[i] = rotations[i];
        }

        SnapStateClientRpc(positions, rotations);
    }

    /// <summary>Immediately snaps all client limbs to the given positions — no lerp.</summary>
    [ClientRpc]
    void SnapStateClientRpc(Vector2[] positions, float[] rotations)
    {
        if (IsServer) return;

        for (int i = 0; i < _bodies.Length && i < positions.Length; i++)
        {
            _targetPositions[i]          = positions[i];
            _targetRotations[i]          = rotations[i];
            _bodies[i].transform.position = new Vector3(positions[i].x, positions[i].y, 0f);
            _bodies[i].transform.rotation = Quaternion.Euler(0f, 0f, rotations[i]);
        }
    }
}
