using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Singleton that persists across all scene loads.
/// Owns scores, card stacks, draft state, and map rotation.
/// Plain MonoBehaviour (NOT NetworkBehaviour) to avoid DontDestroyOnLoad + NGO conflict.
/// Networked score display is handled by subscribing to the delegate events below.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────

    public static GameManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialize card stacks
        for (int i = 0; i < PlayerCardStacks.Length; i++)
            PlayerCardStacks[i] = new List<CardId>();
    }

    // ── Scores ───────────────────────────────────────────────────────────

    public int[] PlayerScores { get; } = new int[3];
    public const int RoundsToWin = 5;
    public int LastRoundWinner { get; private set; } = -1;

    // ── Card stacks — survives all scene transitions ──────────────────────

    public List<CardId>[] PlayerCardStacks { get; } = new List<CardId>[3];

    // ── Draft state — read by CardDraftingUI ──────────────────────────────

    /// <summary>True after the lobby first-kill; false after that (losers-only drafts).</summary>
    public bool IsFirstDraft { get; set; } = true;

    /// <summary>
    /// True only while a map scene is active (MapManager sets/clears this).
    /// Prevents lobby kills from counting as round wins.
    /// </summary>
    public bool RoundsActive { get; set; } = false;

    // ── Map ───────────────────────────────────────────────────────────────

    // Single map for Unit 13 draft — expand to rotation post-semester
    public string GetNextMap() => "Map1";

    // ── Delegates (satisfies the delegate technical requirement) ──────────

    /// <summary>Fired on the server when a round ends. int = winner player slot (0-2).</summary>
    public event Action<int> OnRoundEnd;

    /// <summary>Fired on the server when a player reaches RoundsToWin. int = winner slot.</summary>
    public event Action<int> OnMatchEnd;

    /// <summary>Fired on the server each time a player is eliminated during a round.</summary>
    public event Action<int> OnPlayerEliminated;

    // ── Round logic ───────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerHealth (server only) when only one player remains alive.
    /// Increments their score and fires the appropriate delegate.
    /// </summary>
    public void AddRoundWin(int playerSlot)
    {
        PlayerScores[playerSlot]++;
        LastRoundWinner = playerSlot;
        IsFirstDraft    = false;    // all drafts after the first are losers-only

        if (PlayerScores[playerSlot] >= RoundsToWin)
            OnMatchEnd?.Invoke(playerSlot);
        else
            OnRoundEnd?.Invoke(playerSlot);
    }

    /// <summary>Notifies subscribers that a player was eliminated this round.</summary>
    public void NotifyPlayerEliminated(int playerSlot)
    {
        OnPlayerEliminated?.Invoke(playerSlot);
    }

    /// <summary>
    /// Called on non-server clients via ClientRpc to mirror the server's round-end state.
    /// Updates local scores and fires OnRoundEnd so client-side UI (RoundProgressUI, etc.) refreshes.
    /// </summary>
    public void ClientSyncRoundEnd(int[] scores, int winnerSlot)
    {
        for (int i = 0; i < scores.Length && i < PlayerScores.Length; i++)
            PlayerScores[i] = scores[i];
        LastRoundWinner = winnerSlot;
        IsFirstDraft    = false;
        OnRoundEnd?.Invoke(winnerSlot);
    }

    /// <summary>
    /// Called on non-server clients via ClientRpc to mirror a match-end state.
    /// </summary>
    public void ClientSyncMatchEnd(int[] scores, int winnerSlot)
    {
        for (int i = 0; i < scores.Length && i < PlayerScores.Length; i++)
            PlayerScores[i] = scores[i];
        LastRoundWinner = winnerSlot;
        OnMatchEnd?.Invoke(winnerSlot);
    }

    /// <summary>Returns player slots that are NOT the round winner — these players draft next.</summary>
    public int[] GetLosers(int winnerSlot) =>
        Enumerable.Range(0, 3).Where(i => i != winnerSlot).ToArray();

    // ── Match reset ───────────────────────────────────────────────────────

    /// <summary>Resets everything for a full rematch.</summary>
    public void ResetForNewMatch()
    {
        Array.Clear(PlayerScores, 0, PlayerScores.Length);
        foreach (var stack in PlayerCardStacks)
            stack.Clear();
        IsFirstDraft    = true;
        LastRoundWinner = -1;
        RoundsActive    = false;
    }
}

// ── Card enum ────────────────────────────────────────────────────────────────

public enum CardId
{
    // Bullet modifiers
    ExplosiveRounds,    // bullets explode on impact
    Ricochet,           // bullets bounce off walls once
    RapidFire,          // increased fire rate

    // Spawn modifiers
    HealthPackRain,     // health packs start spawning around the map
    AmmoStash,          // ammo pickups start spawning

    // Player stat modifiers
    SpeedBoost,         // move faster
    DoubleJump,         // jump a second time in the air
    Fragile,            // all other players take +50% damage this round

    // Environment modifiers
    LowGravity,         // reduced gravity for everyone
    HeavyGravity,       // increased gravity for everyone
}
