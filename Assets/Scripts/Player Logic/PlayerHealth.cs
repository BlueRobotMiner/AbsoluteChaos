using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks a player's health (server-authoritative via NetworkVariable).
/// Handles damage from projectiles and instant elimination from killboxes.
/// Notifies GameManager and NetworkLobbyManager via delegates/direct calls.
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] int _maxHealth = 100;

    [Header("UI")]
    [SerializeField] TMPro.TMP_Text _healthText;   // optional on-player health label

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Tracks player slot (0/1/2) — set by the spawning logic or in Inspector
    [SerializeField] int _playerSlot = 0;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Health.OnValueChanged += OnHealthChanged;
        UpdateHealthUI(Health.Value);
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged -= OnHealthChanged;
    }

    // ── Damage ────────────────────────────────────────────────────────────

    /// <summary>Called by Projectile on the server when this player is hit.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(int damage)
    {
        if (Health.Value <= 0) return;   // already eliminated

        Health.Value = Mathf.Max(0, Health.Value - damage);

        if (Health.Value == 0)
            Eliminate();
    }

    /// <summary>Instant kill — called when player enters a killbox trigger.</summary>
    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer) return;
        if (col.CompareTag("KillBox") && Health.Value > 0)
        {
            Health.Value = 0;
            Eliminate();
        }
    }

    // ── Elimination ───────────────────────────────────────────────────────

    void Eliminate()
    {
        // Fire the delegate so AudioManager, HUD, etc. can react
        GameManager.Instance?.NotifyPlayerEliminated(_playerSlot);

        // Tell the lobby manager a kill happened (for the first-kill game-start trigger)
        var lobbyManager = FindObjectOfType<NetworkLobbyManager>();
        if (lobbyManager != null)
            lobbyManager.NotifyKill();

        // Disable input on the owning client
        DisableInputClientRpc();

        // Check if only one player remains alive
        CheckRoundEnd();
    }

    void CheckRoundEnd()
    {
        // Count living players — any with Health.Value > 0
        PlayerHealth[] allPlayers = FindObjectsOfType<PlayerHealth>();
        int aliveCount  = 0;
        int winnerSlot  = -1;

        foreach (var ph in allPlayers)
        {
            if (ph.Health.Value > 0)
            {
                aliveCount++;
                winnerSlot = ph._playerSlot;
            }
        }

        if (aliveCount == 1 && winnerSlot >= 0)
            GameManager.Instance?.AddRoundWin(winnerSlot);
    }

    // ── Reset ─────────────────────────────────────────────────────────────

    /// <summary>Called at the start of each new round (server only).</summary>
    public void ResetHealth()
    {
        if (!IsServer) return;
        Health.Value = _maxHealth;
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────

    [ClientRpc]
    void DisableInputClientRpc()
    {
        if (!IsOwner) return;
        GetComponent<PlayerController>()?.SetInputEnabled(false);
    }

    // ── UI ────────────────────────────────────────────────────────────────

    void OnHealthChanged(int previous, int current)
    {
        UpdateHealthUI(current);

        if (AudioManager.Instance != null && current < previous)
            AudioManager.Instance.PlayHitSFX();
    }

    void UpdateHealthUI(int value)
    {
        if (_healthText != null)
            _healthText.text = $"HP: {value}";
    }

    // ── Public accessor ───────────────────────────────────────────────────

    public int PlayerSlot => _playerSlot;
    public void SetPlayerSlot(int slot) => _playerSlot = slot;
}
