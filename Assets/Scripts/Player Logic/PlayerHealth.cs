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
    [SerializeField] UnityEngine.UI.Slider _healthBar;  // world-space slider above head
    [SerializeField] TMPro.TMP_Text        _healthText; // optional label inside the bar
    [SerializeField] GameObject            _healthBarRoot; // the Canvas GO — drag the HealthBarCanvas here

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Tracks player slot (0/1/2) — set by the spawning logic or in Inspector
    [SerializeField] int _playerSlot = 0;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Health.OnValueChanged += OnHealthChanged;

        // Server sets the canonical starting value — all clients see it via OnValueChanged
        if (IsServer) Health.Value = _maxHealth;
        else          UpdateHealthUI(Health.Value);
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

    // ── Elimination ───────────────────────────────────────────────────────

    void Eliminate()
    {
        // Fire the delegate so AudioManager, HUD, etc. can react
        GameManager.Instance?.NotifyPlayerEliminated(_playerSlot);

        // Tell the lobby manager a kill happened (for the first-kill game-start trigger)
        var lobbyManager = FindObjectOfType<NetworkLobbyManager>(true);
        if (lobbyManager != null)
            lobbyManager.NotifyKill();
        else
            Debug.Log("[PlayerHealth] NetworkLobbyManager not found — probably not in Lobby scene.");

        // Disable input and hide the player on every client
        DisableInputClientRpc();
        HidePlayerClientRpc();

        // Check if only one player remains alive
        CheckRoundEnd();
    }

    void CheckRoundEnd()
    {
        // Only active GOs are alive — dead players are hidden (SetActive false)
        // includeInactive = true so we can count total players, but filter by active
        PlayerHealth[] allPlayers = FindObjectsOfType<PlayerHealth>(true);
        int aliveCount = 0;
        int winnerSlot = -1;

        foreach (var ph in allPlayers)
        {
            if (ph.gameObject.activeSelf)
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

    [ClientRpc]
    void HidePlayerClientRpc()
    {
        gameObject.SetActive(false);
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
        if (_healthBar != null)
        {
            _healthBar.minValue = 0;
            _healthBar.maxValue = _maxHealth;
            _healthBar.value    = value;
        }
        if (_healthText != null)
            _healthText.text = $"{value}";
    }

    // ── Public accessor ───────────────────────────────────────────────────

    public int  PlayerSlot                          => _playerSlot;
    public void SetPlayerSlot(int slot)             => _playerSlot = slot;
    public void SetHealthBarVisible(bool visible)   { if (_healthBarRoot != null) _healthBarRoot.SetActive(visible); }
}
