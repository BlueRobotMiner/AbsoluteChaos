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

                // Grow multiplier each tick, capped
                state.multiplier = Mathf.Min(state.multiplier + _multiplierGrowth, _maxMultiplier);

                // Keep knocking them back every tick too
                ApplyKnockback(ph);
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
        ApplyKnockback(ph);
    }

    void OnTriggerExit2D(Collider2D col)
    {
        PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
        if (ph != null)
            _active.Remove(ph);  // multiplier resets on exit
    }

    void ApplyKnockback(PlayerHealth ph)
    {
        // Push toward map center (0,0) from wherever the killbox is
        var pc = ph.GetComponent<PlayerController>();
        if (pc == null || pc.rb == null) return;

        Vector2 direction = ((Vector2)Vector3.zero - pc.rb.position).normalized;
        // Always push horizontally inward + a bit up so they can recover
        direction = new Vector2(direction.x, 0.5f).normalized;
        pc.rb.velocity = Vector2.zero;
        pc.rb.AddForce(direction * _knockbackForce, ForceMode2D.Impulse);
    }
}

class KillBoxState
{
    public float multiplier;
    public float timer;
}
