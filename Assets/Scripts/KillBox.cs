using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to each killbox trigger. Instead of instant death:
/// - Knocks the player back toward the center of the map
/// - Deals damage with a hit multiplier that stacks the longer they stay in
/// - Multiplier resets when the player leaves the killbox
///
/// Tag this GO as "KillBox". Collider2D must be Is Trigger = true.
/// </summary>
public class KillBox : MonoBehaviour
{
    [SerializeField] float _knockbackForce  = 18f;   // how hard the player is pushed back
    [SerializeField] int   _baseDamage      = 8;     // damage on first hit
    [SerializeField] float _tickRate        = 0.4f;  // seconds between damage ticks
    [SerializeField] float _multiplierGrowth = 0.4f; // how much multiplier increases per tick
    [SerializeField] float _maxMultiplier   = 4f;    // damage multiplier cap

    // Each player that is currently inside tracked separately
    // Key: PlayerHealth, Value: their current state
    System.Collections.Generic.Dictionary<PlayerHealth, KillBoxState> _active = new();

    void Update()
    {
        var toRemove = new System.Collections.Generic.List<PlayerHealth>();

        foreach (var kvp in _active)
        {
            var ph    = kvp.Key;
            var state = kvp.Value;

            if (ph == null) { toRemove.Add(ph); continue; }

            state.timer += Time.deltaTime;
            if (state.timer >= _tickRate)
            {
                state.timer = 0f;

                int damage = Mathf.RoundToInt(_baseDamage * state.multiplier);
                ph.TakeDamageServerRpc(damage);

                // Apply knockback scaled to current multiplier — longer exposure = harder push
                ApplyKnockback(ph, state.multiplier);

                // Grow multiplier each tick AFTER applying so first hit uses base values
                state.multiplier = Mathf.Min(state.multiplier + _multiplierGrowth, _maxMultiplier);
            }
        }

        foreach (var ph in toRemove)
            _active.Remove(ph);
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
        if (ph == null || _active.ContainsKey(ph)) return;

        _active[ph] = new KillBoxState { multiplier = 1f, timer = _tickRate }; // hit immediately on entry
        ApplyKnockback(ph, 1f);
    }

    void OnTriggerExit2D(Collider2D col)
    {
        PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
        if (ph != null)
            _active.Remove(ph);  // multiplier resets on exit
    }

    void ApplyKnockback(PlayerHealth ph, float multiplier)
    {
        var pc = ph.GetComponent<PlayerController>();
        if (pc == null || pc.rb == null) return;

        Vector2 toCenter = (Vector2)Vector3.zero - pc.rb.position;

        // Always push inward horizontally. For the vertical component: use the actual
        // direction to center but clamp the Y to at least 1.5 so bottom killboxes
        // always launch players upward rather than just sideways.
        float yDir    = Mathf.Max(toCenter.normalized.y, 1.5f);
        Vector2 dir   = new Vector2(toCenter.x != 0 ? Mathf.Sign(toCenter.x) : 0f, yDir).normalized;

        float force   = _knockbackForce * multiplier;

        // Suppress duration scales with multiplier so high-multiplier hits give
        // the player longer to recover before movement input overrides the impulse
        float suppress = Mathf.Lerp(0.25f, 0.65f, (multiplier - 1f) / Mathf.Max(_maxMultiplier - 1f, 1f));

        pc.ApplyKnockback(dir * force, suppress);
    }
}

class KillBoxState
{
    public float multiplier;
    public float timer;
}
