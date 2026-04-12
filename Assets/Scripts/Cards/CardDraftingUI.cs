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
        if (!IsServer) return;

        _eligibleSlots = GameManager.Instance.IsFirstDraft
            ? new[] { 0, 1, 2 }
            : GameManager.Instance.GetLosers(GameManager.Instance.LastRoundWinner);

        _currentIndex = 0;
        DealNextPlayer();
    }

    // ── Server logic ──────────────────────────────────────────────────────

    void DealNextPlayer()
    {
        int draftingSlot = _eligibleSlots[_currentIndex];
        CardData[] offered = CardDatabase.Instance.GetRandomCards(3);
        DealCardsClientRpc(draftingSlot, (int)offered[0].id, (int)offered[1].id, (int)offered[2].id);
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────

    [ClientRpc]
    void DealCardsClientRpc(int draftingSlot, int card0, int card1, int card2)
    {
        // Set up the card visuals — look up full CardData from database
        _cardSlots[0].Setup(GetCardData(card0));
        _cardSlots[1].Setup(GetCardData(card1));
        _cardSlots[2].Setup(GetCardData(card2));

        foreach (var cardSlot in _cardSlots)
            cardSlot.IsHovered = false;

        if (_statusText != null)
            _statusText.text = $"Player {draftingSlot + 1} is choosing...";

        // Find the local player and either put them in draft mode or stand-by
        var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObj == null) return;

        var pc = localPlayerObj.GetComponent<PlayerController>();
        if (pc == null) return;

        int slot = pc.GetPlayerSlotPublic();

        if (slot == draftingSlot)
        {
            // Teleport the active drafter to the spawn point and show them
            if (_draftSpawn != null)
                pc.rb.position = _draftSpawn.position;
            pc.gameObject.SetActive(true);
            pc.SetDraftMode(true, _cardSlots, this);
        }
        else
        {
            // Hide non-drafting players entirely until it's their turn
            pc.gameObject.SetActive(false);
            pc.SetDraftMode(false, null, null);
        }
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

    CardData GetCardData(int id) => CardDatabase.Instance.GetById((CardId)id);
}
