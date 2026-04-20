using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in each Map scene. On spawn it repositions all players and resets health.
/// Listens to GameManager delegates to trigger CardDraft after a round ends.
/// Applies and resets all card effects each round.
/// </summary>
public class MapManager : NetworkBehaviour
{
    [Header("Spawn Points")]
    [SerializeField] Transform[] _spawnPoints;

    [Header("Timing")]
    [SerializeField] float _roundEndDelay   = 2f;
    [SerializeField] float _spawnDropHeight = 0f;

    [Header("Card Settings")]
    [SerializeField] float _ammoMultiplier = 1.2f;   // AmmoStash card: multiply gun mag size by this

    [Header("Card Spawn Prefabs")]

    [Header("Countdown")]
    [SerializeField] RoundStartUI _roundStartUI;
    [SerializeField] int   _countdownFrom      = 3;
    [SerializeField] float _fightDisplayTime   = 0.8f;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.RoundsActive = true;

        if (!IsServer) return;

        GameManager.Instance.OnRoundEnd  += HandleRoundEnd;
        GameManager.Instance.OnMatchEnd  += HandleMatchEnd;

        StartCoroutine(RespawnAfterLoad());
    }

    public override void OnNetworkDespawn()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.RoundsActive = false;

        if (!IsServer) return;
        GameManager.Instance.OnRoundEnd  -= HandleRoundEnd;
        GameManager.Instance.OnMatchEnd  -= HandleMatchEnd;

        // Restore global physics to defaults so they don't leak into other scenes
        Physics2D.gravity = new Vector2(0f, -9.81f);
    }

    // ── Respawn ───────────────────────────────────────────────────────────

    IEnumerator RespawnAfterLoad()
    {
        yield return new WaitForSeconds(0.2f);

        // Player GOs may be SetActive(false) from draft-scene hiding — re-enable on all clients
        ReactivateAllPlayersClientRpc();
        yield return null;

        // Place all players and lock input — they're visible but can't move yet
        RespawnAllPlayers(inputEnabled: false);
        ApplyAllCardEffects();

        // Start countdown; when it finishes, release input and spawn guns
        StartCoroutine(CountdownCoroutine());
    }

    IEnumerator CountdownCoroutine()
    {
        PlayCountdownSFXClientRpc();   // play once at the very start of the countdown
        for (int i = _countdownFrom; i >= 1; i--)
        {
            ShowCountdownClientRpc(i.ToString());
            yield return new WaitForSeconds(1f);
        }
        ShowCountdownClientRpc("FIGHT!");
        yield return new WaitForSeconds(_fightDisplayTime);
        HideCountdownClientRpc();
        OnCountdownComplete();
    }

    [ClientRpc]
    void ReactivateAllPlayersClientRpc()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
            pc.gameObject.SetActive(true);
    }

    [ClientRpc]
    void PlayCountdownSFXClientRpc()
    {
        AudioManager.Instance?.PlayCountdownSFX();
    }

    [ClientRpc]
    void ShowCountdownClientRpc(string label)
    {
        _roundStartUI?.ShowLabel(label);
    }

    [ClientRpc]
    void HideCountdownClientRpc()
    {
        _roundStartUI?.Hide();
    }

    void OnCountdownComplete()
    {
        // Enable input on all players
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
        {
            pc.SetInputEnabledClientRpc(true);
            if (pc.TryGetComponent(out PlayerCombat combat))
                combat.SetShootingEnabledClientRpc(true);
        }

        // Now spawn guns (card-triggered items like health packs were already spawned in ApplyAllCardEffects)
        if (ItemSpawner.Instance != null)
            ItemSpawner.Instance.SpawnInitialGuns();
    }

    void RespawnAllPlayers(bool inputEnabled = true)
    {
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
        {
            ph.SetAlive(true);         // fires OnAliveChanged on all clients — shows renderers + health bar
            ph.ResetHealth();
            ph.SetSpawnImmunity(2f);
        }

        foreach (var pc in FindObjectsOfType<PlayerController>(true))
        {
            int slot = pc.GetPlayerSlotPublic();

            pc.SetInputEnabledClientRpc(inputEnabled);
            if (pc.TryGetComponent(out PlayerCombat combat))
                combat.SetShootingEnabledClientRpc(inputEnabled);

            Vector2 pos;
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                pos = Vector2.up * _spawnDropHeight;
            else
            {
                int spawnIndex = slot % _spawnPoints.Length;
                pos = (Vector2)_spawnPoints[spawnIndex].position + Vector2.up * _spawnDropHeight;
            }

            pc.InitializePositionClientRpc(pos);
        }
    }

    // ── Card effects ──────────────────────────────────────────────────────

    void ApplyAllCardEffects()
    {
        if (!IsServer || GameManager.Instance == null) return;

        // 1. Reset all per-player stats, global physics, and item spawn stacks
        ItemSpawner.Instance?.ClearCardStacks();
        Physics2D.gravity = new Vector2(0f, -9.81f);
        foreach (var pc in FindObjectsOfType<PlayerController>(true))  pc.ResetBaseStats();
        foreach (var pc in FindObjectsOfType<PlayerController>(true))  pc.GetComponent<PlayerCombat>()?.ResetBaseStats();
        foreach (var pc in FindObjectsOfType<PlayerController>(true))  pc.GetComponent<PlayerHealth>()?.ResetBaseStats();

        // 2. Apply each player's full card stack
        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        for (int slot = 0; slot < connected; slot++)
        {
            foreach (var card in GameManager.Instance.PlayerCardStacks[slot])
                ApplyCardForSlot(slot, card);
        }

        // 3. Gravity is server-only physics — no client sync needed (clients are pure renderers)
    }

    void ApplyCardForSlot(int slot, CardId card)
    {
        var pc     = PlayerController.GetBySlot(slot);
        var combat = pc != null ? pc.GetComponent<PlayerCombat>()    : null;
        var health = pc != null ? pc.GetComponent<PlayerHealth>()     : null;

        switch (card)
        {
            // ── Bullet modifiers ─────────────────────────────────────────
            case CardId.ExplosiveRounds:
                combat?.SetExplosiveRounds(true);
                break;

            case CardId.Ricochet:
                combat?.AddRicochetBounces(3);
                break;

            case CardId.RapidFire:
                // Multiplicative: each copy in the stack halves the cooldown again
                combat?.MultiplyFireRate(0.5f);
                break;

            // ── Spawn modifiers ──────────────────────────────────────────
            // Each player who owns this card spawns one instance per round;
            // two players owning the same card → two packs/pickups on the map.
            case CardId.HealthPackRain:
                ItemSpawner.Instance?.RegisterCardStack(CardId.HealthPackRain);
                break;

            case CardId.AmmoStash:
                if (combat != null) combat.ammoMultiplier = _ammoMultiplier;
                break;

            // ── Player stat modifiers ────────────────────────────────────
            case CardId.SpeedBoost:
                // Multiplicative: second copy → 1.5 × 1.5 = 2.25×
                if (pc != null) pc.speed *= 1.5f;
                break;

            case CardId.DoubleJump:
                // Additive: each copy adds one extra jump
                if (pc != null) pc.SetMaxJumps(pc.GetMaxJumps() + 1);
                break;

            case CardId.Fragile:
                // All OTHER players take 50% more damage this round
                int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
                for (int other = 0; other < playerCount; other++)
                {
                    if (other == slot) continue;
                    var otherHealth = PlayerController.GetBySlot(other)
                                          ?.GetComponent<PlayerHealth>();
                    if (otherHealth != null)
                        otherHealth.SetDamageMultiplier(otherHealth.GetCurrentMultiplier() * 1.5f);
                }
                break;

            // ── Environment modifiers ────────────────────────────────────
            case CardId.LowGravity:
                Physics2D.gravity = new Vector2(0f, Physics2D.gravity.y * 0.5f);
                break;

            case CardId.HeavyGravity:
                Physics2D.gravity = new Vector2(0f, Physics2D.gravity.y * 1.5f);
                break;
        }
    }

    // ── Round end ─────────────────────────────────────────────────────────

    void HandleRoundEnd(int winnerSlot)
    {
        SyncRoundEndClientRpc(GameManager.Instance.PlayerScores, winnerSlot);
        StartCoroutine(LoadCardDraft());
    }

    [ClientRpc]
    void SyncRoundEndClientRpc(int[] scores, int winnerSlot)
    {
        if (IsServer) return;
        GameManager.Instance?.ClientSyncRoundEnd(scores, winnerSlot);
    }

    void HandleMatchEnd(int winnerSlot)
    {
        SyncMatchEndClientRpc(GameManager.Instance.PlayerScores, winnerSlot);
        StartCoroutine(LoadResults(winnerSlot));
    }

    [ClientRpc]
    void SyncMatchEndClientRpc(int[] scores, int winnerSlot)
    {
        if (IsServer) return;
        GameManager.Instance?.ClientSyncMatchEnd(scores, winnerSlot);
    }

    IEnumerator LoadCardDraft()
    {
        yield return new WaitForSeconds(_roundEndDelay);
        DespawnAllGuns();
        yield return null;
        NetworkManager.Singleton.SceneManager.LoadScene("CardDraft", LoadSceneMode.Single);
    }

    void DespawnAllGuns()
    {
        foreach (var gun in FindObjectsOfType<Gun>(true))
            if (gun.IsSpawned) gun.DespawnSelf();
    }

    IEnumerator LoadResults(int winnerSlot)
    {
        yield return new WaitForSeconds(_roundEndDelay);
        DespawnAllGuns();
        DespawnAllPlayers();
        yield return null;
        NetworkManager.Singleton.SceneManager.LoadScene("Results", LoadSceneMode.Single);
    }

    void DespawnAllPlayers()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
        {
            if (pc.TryGetComponent(out NetworkObject no) && no.IsSpawned)
                no.Despawn(true);
        }
    }

}
