using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the sequential card draft. One player picks at a time.
/// The drafting player's character physically walks toward cards in world space.
/// Non-drafting players wait at their spawn points.
///
/// Scene setup:
///   - 3 CardSlot GOs placed across the scene (left, center, right)
///   - Draft spawn points placed below/in front of the cards
///   - Attach this script to a NetworkObject in the CardDraft scene
/// </summary>
public class CardDraftingUI : NetworkBehaviour
{
    [Header("Scene References")]
    [SerializeField] CardSlot[] _cardSlots;
    [SerializeField] Transform  _draftSpawn;   // single spawn point — only the active drafter appears here
    [SerializeField] TMP_Text   _statusText;

    // Server-side state
    int[] _eligibleSlots;
    int   _currentIndex;   // which index in _eligibleSlots is picking right now

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Hide all health bars on every client when draft starts
        HideHealthBarsClientRpc();

        if (!IsServer) return;

        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;

        if (GameManager.Instance.IsFirstDraft)
        {
            var slots = new System.Collections.Generic.List<int>();
            for (int i = 0; i < connected; i++) slots.Add(i);
            _eligibleSlots = slots.ToArray();
        }
        else
        {
            var losers = GameManager.Instance.GetLosers(GameManager.Instance.LastRoundWinner);
            var valid  = System.Array.FindAll(losers, s => s < connected);
            _eligibleSlots = valid;
        }

        _currentIndex = 0;
        StartCoroutine(BeginDraftAfterLoad());
    }

    IEnumerator BeginDraftAfterLoad()
    {
        yield return new WaitForSeconds(0.2f);

        // Dead players have SpriteRenderers disabled from the previous round.
        // Restore renderers only — SetAlive is not used here because it triggers a NV
        // change whose OnValueChanged callback races with HideHealthBarsClientRpc.
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            ph.ShowRenderersForDraft();

        HideHealthBarsClientRpc();

        foreach (var pc in FindObjectsOfType<PlayerController>(true))
            pc.SetInputEnabledClientRpc(false);

        DealNextPlayer();
    }

    [ClientRpc]
    void HideHealthBarsClientRpc()
    {
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            ph.SetHealthBarVisible(false);
    }

    // ── Server logic ──────────────────────────────────────────────────────

    void DealNextPlayer()
    {
        int draftingSlot = _eligibleSlots[_currentIndex];

        // Find the drafting player and move them to the draft spawn point server-side.
        // InitializePositionClientRpc shifts all ragdoll bodies and snapshots to clients.
        PlayerController draftingPC = null;
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
        {
            if (pc.GetPlayerSlotPublic() == draftingSlot)
            {
                draftingPC = pc;
                break;
            }
        }

        if (draftingPC != null && _draftSpawn != null)
            draftingPC.InitializePositionClientRpc((Vector2)_draftSpawn.position);

        CardData[] offered = GetOfferedCards(draftingSlot);
        DealCardsClientRpc(draftingSlot, (int)offered[0].id, (int)offered[1].id, (int)offered[2].id);
    }

    // ── Card selection ────────────────────────────────────────────────────

    /// <summary>
    /// Returns 3 cards for the drafting player.
    /// World-modifier cards (Environment category) already owned by OTHER players are
    /// pushed to the back of the pool so the drafter is less likely to see them.
    /// Non-world-modifier cards and cards the drafter already owns are always offered normally
    /// (stacking is intentional and rewarding for those card types).
    /// </summary>
    CardData[] GetOfferedCards(int draftingSlot)
    {
        // Collect world-modifier card IDs already in OTHER players' stacks
        var otherWorldCards = new HashSet<CardId>();
        int connected = NetworkManager.Singleton.ConnectedClientsIds.Count;
        for (int s = 0; s < connected; s++)
        {
            if (s == draftingSlot) continue;
            foreach (var card in GameManager.Instance.PlayerCardStacks[s])
            {
                var data = CardDatabase.Instance.GetById(card);
                if (data != null && data.category == "Environment")
                    otherWorldCards.Add(card);
            }
        }

        // Draw a larger pool so we have room to filter
        CardData[] pool = CardDatabase.Instance.GetRandomCards(12);

        var preferred = new List<CardData>();   // not a conflicting world modifier
        var fallback  = new List<CardData>();   // world modifier another player already has

        foreach (var card in pool)
        {
            bool conflictsWithOther = card.category == "Environment"
                                   && otherWorldCards.Contains(card.id);

            // Deduplicate within the same offer set
            bool alreadyInList = preferred.Exists(c => c.id == card.id)
                              || fallback.Exists(c => c.id == card.id);
            if (alreadyInList) continue;

            if (conflictsWithOther)
                fallback.Add(card);
            else
                preferred.Add(card);
        }

        // Fill 3 slots: preferred first, fallback only if we run out
        var offered = new List<CardData>(3);
        foreach (var card in preferred)
        {
            if (offered.Count == 3) break;
            offered.Add(card);
        }
        foreach (var card in fallback)
        {
            if (offered.Count == 3) break;
            offered.Add(card);
        }

        // Safety: if the database is very small, pad with whatever is available
        if (offered.Count < 3)
        {
            var extra = CardDatabase.Instance.GetRandomCards(3);
            foreach (var card in extra)
            {
                if (offered.Count == 3) break;
                if (!offered.Exists(c => c.id == card.id))
                    offered.Add(card);
            }
        }

        return offered.ToArray();
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────

    [ClientRpc]
    void DealCardsClientRpc(int draftingSlot, int card0, int card1, int card2)
    {
        // Set up the card visuals
        _cardSlots[0].Setup(GetCardData(card0));
        _cardSlots[1].Setup(GetCardData(card1));
        _cardSlots[2].Setup(GetCardData(card2));

        foreach (var cardSlot in _cardSlots)
            cardSlot.IsHovered = false;

        if (_statusText != null)
        {
            var pc = PlayerController.GetBySlot(draftingSlot);
            string name = pc != null ? pc.PlayerDisplayName : $"Player {draftingSlot + 1}";
            _statusText.text = $"{name} is choosing...";
        }

        // Ensure health bars stay hidden every time a drafter is shown
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            ph.SetHealthBarVisible(false);

        // Hide EVERYONE immediately — the drafter is shown one frame later once
        // any in-flight position RPCs (InitializePositionClientRpc, SnapStateClientRpc)
        // from different NetworkObjects have had a chance to arrive and settle.
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
            pc.gameObject.SetActive(false);

        StartCoroutine(ShowDrafterNextFrame(draftingSlot));
    }

    IEnumerator ShowDrafterNextFrame(int draftingSlot)
    {
        yield return null;   // one frame — lets position sync RPCs settle

        // Show only the drafter, keep everyone else hidden
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
            pc.gameObject.SetActive(pc.GetPlayerSlotPublic() == draftingSlot);

        // Drive input mode for the LOCAL player only
        var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObj == null) yield break;

        var localPC = localPlayerObj.GetComponent<PlayerController>();
        if (localPC == null) yield break;

        int localSlot = localPC.GetPlayerSlotPublic();

        if (localSlot == draftingSlot)
            StartCoroutine(EnableDraftAfterDelay(localPC));
        else
            localPC.SetDraftMode(false, null, null);
    }

    System.Collections.IEnumerator EnableDraftAfterDelay(PlayerController pc)
    {
        // Disable input while scene settles, give player a moment to see the cards
        pc.SetInputEnabled(false);
        yield return new WaitForSeconds(1f);
        pc.SetInputEnabled(true);
        pc.SetInputEnabledServerRpc(true);   // re-enable server-side physics gate for non-host players
        pc.SetDraftMode(true, _cardSlots, this);
    }

    // ── Pick submission — called by PlayerController ───────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void SubmitPickServerRpc(int playerSlot, int cardId)
    {
        // Guard duplicates
        if (_currentIndex >= _eligibleSlots.Length) return;
        if (_eligibleSlots[_currentIndex] != playerSlot) return;

        GameManager.Instance.PlayerCardStacks[playerSlot].Add((CardId)cardId);

        // Push the new card to all clients so their CardHUD stacks stay in sync
        SyncCardPickClientRpc(playerSlot, cardId);

        _currentIndex++;

        if (_currentIndex >= _eligibleSlots.Length)
            StartCoroutine(LoadNextMap());
        else
            DealNextPlayer();
    }

    IEnumerator LoadNextMap()
    {
        yield return new WaitForSeconds(0.5f);
        NetworkManager.Singleton.SceneManager.LoadScene(
            GameManager.Instance.GetNextMap(), LoadSceneMode.Single);
    }

    [ClientRpc]
    void ShowAllPlayersClientRpc()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
        {
            var ph = pc.GetComponent<PlayerHealth>();
            if (ph != null && !ph.IsAlive) continue;
            pc.gameObject.SetActive(true);
        }
    }

    // ── Hover sync ────────────────────────────────────────────────────────

    /// <summary>Called by PlayerController when the hovered card changes. Pass -1 to clear.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void UpdateHoverServerRpc(int cardIndex)
    {
        SyncHoverClientRpc(cardIndex);
    }

    [ClientRpc]
    void SyncHoverClientRpc(int cardIndex)
    {
        for (int i = 0; i < _cardSlots.Length; i++)
            _cardSlots[i].IsHovered = (i == cardIndex);
    }

    /// <summary>
    /// Replicates a confirmed card pick to every client.
    /// The server already has the card added before this fires — clients add it here.
    /// CardHUD.Refresh() is NOT called here; the map scene's CardHUD refreshes on OnRoundEnd.
    /// </summary>
    [ClientRpc]
    void SyncCardPickClientRpc(int playerSlot, int cardId)
    {
        if (IsServer) return;   // server already added it in SubmitPickServerRpc
        GameManager.Instance.PlayerCardStacks[playerSlot].Add((CardId)cardId);
    }

    CardData GetCardData(int id) => CardDatabase.Instance.GetById((CardId)id);
}
