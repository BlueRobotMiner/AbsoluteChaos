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
    }
}

// ── Card enum (extend in Week 2 with full card data) ─────────────────────────

public enum CardId
{
    SpeedBoost,
    DoubleJump,
    Ricochet,
    ExplosiveShots,
    LowGravity,
    ShieldBurst
}
