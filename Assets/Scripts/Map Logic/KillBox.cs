using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Trigger zone at the map edge. When a player enters it their velocity is reflected
/// back toward the play area and they take damage. Each hit on the same player
/// within a session escalates force and damage.
///
/// _instantKill = true  → outer hard boundary, kills immediately (for players who
///                         tunnel past the borders entirely).
/// _instantKill = false → bouncy danger zone just outside the play borders.
///
/// Projectiles are completely ignored — bullet reflection is handled by the
/// Projectile script's wall-layer raycast (set Ground/Borders/Obstacles there).
/// </summary>
public class KillBox : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] bool _instantKill = false;

    [Header("Bounce (ignored when _instantKill = true)")]
    [SerializeField] float _baseForce       = 20f;   // outward impulse on first hit
    [SerializeField] int   _baseDamage      = 15;
    [SerializeField] float _multiplierGrowth = 0.5f; // added to multiplier each hit
    [SerializeField] float _maxMultiplier   = 5f;

    [Header("Anti-spam")]
    [Tooltip("Seconds before the same player can be bounced again — prevents multi-limb double triggers")]
    [SerializeField] float _hitCooldown = 0.25f;

    // Per-player state: multiplier and last-hit timestamp
    readonly Dictionary<PlayerHealth, PlayerKillState> _state = new();

    // ── Trigger ───────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Ignore bullets — they are handled by Projectile._wallLayer ricochet
        if (col.GetComponent<Projectile>() != null) return;

        PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
        if (ph == null) return;
        if (ph.IsImmune) return;

        if (!_state.TryGetValue(ph, out var ks))
        {
            ks = new PlayerKillState { multiplier = 1f };
            _state[ph] = ks;
        }

        // Per-player cooldown so multiple limbs entering at once only trigger once
        if (Time.time - ks.lastHitTime < _hitCooldown) return;
        ks.lastHitTime = Time.time;

        if (_instantKill)
        {
            ph.TakeDamageServerRpc(9999);
            return;
        }

        // ── Damage ────────────────────────────────────────────────────────
        int damage = Mathf.RoundToInt(_baseDamage * ks.multiplier);
        ph.TakeDamageServerRpc(damage);

        // ── Velocity reflection ───────────────────────────────────────────
        var pc = ph.GetComponent<PlayerController>();
        if (pc?.rb != null)
        {
            // Normal points outward: from killbox center toward the player
            Vector2 normal = ((Vector2)pc.rb.position - (Vector2)transform.position).normalized;
            if (normal == Vector2.zero) normal = Vector2.up;

            // Reflect current velocity so they bounce back the way they came
            Vector2 reflected = Vector2.Reflect(pc.rb.velocity, normal);

            // Add extra outward push that grows each hit
            float force = _baseForce * ks.multiplier;
            pc.rb.velocity = reflected + normal * force;

            // Suppress horizontal input briefly so the bounce isn't immediately cancelled
            pc.ApplyKnockback(Vector2.zero, Mathf.Lerp(0.2f, 0.6f,
                (ks.multiplier - 1f) / Mathf.Max(_maxMultiplier - 1f, 1f)));
        }

        // Escalate for next hit — doesn't reset until round ends or player leaves
        ks.multiplier = Mathf.Min(ks.multiplier + _multiplierGrowth, _maxMultiplier);
    }

    void OnTriggerExit2D(Collider2D col)
    {
        // Reset multiplier when the player fully leaves so a brief graze doesn't
        // permanently escalate — they need to keep hitting it to keep the stack
        if (!NetworkManager.Singleton.IsServer) return;
        var ph = col.GetComponentInParent<PlayerHealth>();
        if (ph != null && _state.TryGetValue(ph, out var ks))
            ks.multiplier = 1f;
    }
}

class PlayerKillState
{
    public float multiplier;
    public float lastHitTime;
}
