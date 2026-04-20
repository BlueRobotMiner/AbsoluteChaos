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

    // Alive state — server writes, all clients read. Toggling renderers via NV avoids
    // calling SetActive on the root NetworkObject GO, which causes NGO to destroy it.
    NetworkVariable<bool> _networkAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsAlive => _networkAlive.Value;

    [SerializeField] int _playerSlot = 0;

    // Fragile card: incoming damage is scaled by this (default 1 = no modifier)
    float _incomingDamageMultiplier = 1f;

    // Spawn immunity: set by MapManager at respawn so killbox doesn't hit during physics settle
    float _immunityEndTime;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Health.OnValueChanged        += OnHealthChanged;
        _networkAlive.OnValueChanged += OnAliveChanged;

        if (IsServer) Health.Value = _maxHealth;
        else          UpdateHealthUI(Health.Value);

        // Apply current state immediately — NV callbacks don't fire for the initial value,
        // so late-joining clients need this to see dead players correctly on join
        ApplyAliveState(_networkAlive.Value);
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged        -= OnHealthChanged;
        _networkAlive.OnValueChanged -= OnAliveChanged;
    }

    // ── Damage ────────────────────────────────────────────────────────────

    /// <summary>Called by Projectile on the server when this player is hit.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(int damage)
    {
        if (Health.Value <= 0) return;
        if (Time.time < _immunityEndTime) return;

        int scaled = Mathf.RoundToInt(damage * _incomingDamageMultiplier);
        Health.Value = Mathf.Max(0, Health.Value - scaled);

        if (Health.Value == 0)
            Eliminate();
    }

    [ServerRpc(RequireOwnership = false)]
    public void HealServerRpc(int amount)
    {
        if (Health.Value <= 0) return;
        Health.Value = Mathf.Min(_maxHealth, Health.Value + amount);
    }

    // ── Elimination ───────────────────────────────────────────────────────

    void Eliminate()
    {
        GameManager.Instance?.NotifyPlayerEliminated(_playerSlot);

        var lobbyManager = FindObjectOfType<NetworkLobbyManager>(true);
        if (lobbyManager != null)
            lobbyManager.NotifyKill();
        else
            Debug.Log("[PlayerHealth] NetworkLobbyManager not found — probably not in Lobby scene.");

        _networkAlive.Value = false;   // fires OnAliveChanged on all clients — hides renderers
        DisableInputClientRpc();

        CheckRoundEnd();
    }

    void CheckRoundEnd()
    {
        PlayerHealth[] allPlayers = FindObjectsOfType<PlayerHealth>(true);
        int aliveCount = 0;
        int winnerSlot = -1;

        foreach (var ph in allPlayers)
        {
            if (ph._networkAlive.Value)
            {
                aliveCount++;
                winnerSlot = ph._playerSlot;
            }
        }

        if (aliveCount == 1 && winnerSlot >= 0 && (GameManager.Instance?.RoundsActive ?? false))
            GameManager.Instance?.AddRoundWin(winnerSlot);
    }

    // ── Alive / reset API (server only) ──────────────────────────────────

    /// <summary>Brings this player back to life at the start of each round.</summary>
    public void SetAlive(bool alive)
    {
        if (!IsServer) return;
        _networkAlive.Value = alive;
        // NV OnValueChanged only fires when the value changes — force-broadcast so the
        // health bar always appears even if the player was never dead (NV stays true).
        ForceApplyAliveClientRpc(alive);
    }

    [ClientRpc]
    void ForceApplyAliveClientRpc(bool alive) => ApplyAliveState(alive);

    /// <summary>
    /// Restores SpriteRenderers only — used by CardDraft to make dead players visible
    /// without touching health bars or alive state.
    /// </summary>
    public void ShowRenderersForDraft()
    {
        if (!IsServer) return;
        ShowRenderersForDraftClientRpc();
    }

    [ClientRpc]
    void ShowRenderersForDraftClientRpc()
    {
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
            sr.enabled = true;
    }

    /// <summary>Called at the start of each new round (server only).</summary>
    public void ResetHealth()
    {
        if (!IsServer) return;
        Health.Value = _maxHealth;
    }

    // ── NetworkVariable callback ──────────────────────────────────────────

    void OnAliveChanged(bool prev, bool current) => ApplyAliveState(current);

    void ApplyAliveState(bool alive)
    {
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
            sr.enabled = alive;
        SetHealthBarVisible(alive);
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

    public int  PlayerSlot                        => _playerSlot;
    public void SetPlayerSlot(int slot)           => _playerSlot = slot;
    public void  SetDamageMultiplier(float m)      => _incomingDamageMultiplier = m;
    public float GetCurrentMultiplier()           => _incomingDamageMultiplier;
    public void  ResetBaseStats()                 => _incomingDamageMultiplier = 1f;
    public void  SetSpawnImmunity(float duration)  => _immunityEndTime = Time.time + duration;
    public bool  IsImmune                         => Time.time < _immunityEndTime;
    public void SetHealthBarVisible(bool visible)
    {
        if (_healthBarRoot != null) _healthBarRoot.SetActive(visible);
        // Force-refresh text and bar when showing — OnValueChanged won't fire if
        // health hasn't changed since last map, so the UI would stay stale otherwise
        if (visible) UpdateHealthUI(Health.Value);
    }
}
