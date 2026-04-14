using System.Collections;
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
            // All connected players pick — no more than who's actually in the game
            var slots = new System.Collections.Generic.List<int>();
            for (int i = 0; i < connected; i++) slots.Add(i);
            _eligibleSlots = slots.ToArray();
        }
        else
        {
            // Only losers pick — filter to slots that actually exist
            var losers = GameManager.Instance.GetLosers(GameManager.Instance.LastRoundWinner);
            var valid  = System.Array.FindAll(losers, s => s < connected);
            _eligibleSlots = valid;
        }

        _currentIndex = 0;
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

        CardData[] offered = CardDatabase.Instance.GetRandomCards(3);
        DealCardsClientRpc(draftingSlot, (int)offered[0].id, (int)offered[1].id, (int)offered[2].id);
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
            _statusText.text = $"Player {draftingSlot + 1} is choosing...";

        // Ensure health bars stay hidden every time a drafter is shown
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            ph.SetHealthBarVisible(false);

        // Show the drafter on ALL clients, hide everyone else.
        // This runs identically on every machine so visibility stays in sync.
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
        {
            int slot = pc.GetPlayerSlotPublic();
            pc.gameObject.SetActive(slot == draftingSlot);
        }

        // Drive input mode for the LOCAL player only
        var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObj == null) return;

        var localPC = localPlayerObj.GetComponent<PlayerController>();
        if (localPC == null) return;

        int localSlot = localPC.GetPlayerSlotPublic();

        if (localSlot == draftingSlot)
        {
            StartCoroutine(EnableDraftAfterDelay(localPC));
        }
        else
        {
            localPC.SetDraftMode(false, null, null);
        }
    }

    System.Collections.IEnumerator EnableDraftAfterDelay(PlayerController pc)
    {
        // Disable input while scene settles, give player a moment to see the cards
        pc.SetInputEnabled(false);
        yield return new WaitForSeconds(1f);
        pc.SetInputEnabled(true);
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

        _currentIndex++;

        if (_currentIndex >= _eligibleSlots.Length)
            StartCoroutine(LoadNextMap());
        else
            DealNextPlayer();
    }

    IEnumerator LoadNextMap()
    {
        // Re-show all players before leaving the scene
        ShowAllPlayersClientRpc();
        yield return new WaitForSeconds(1f);
        NetworkManager.Singleton.SceneManager.LoadScene(
            GameManager.Instance.GetNextMap(), LoadSceneMode.Single);
    }

    [ClientRpc]
    void ShowAllPlayersClientRpc()
    {
        var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObj != null)
            localPlayerObj.gameObject.SetActive(true);
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

    CardData GetCardData(int id) => CardDatabase.Instance.GetById((CardId)id);
}
