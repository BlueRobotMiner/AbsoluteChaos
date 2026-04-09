using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages the card drafting screen. Lives in the CardDraft scene.
/// Server deals random cards to eligible players via ClientRpc.
/// Each player picks one; when all picks are in the server loads the next map.
/// </summary>
public class CardDraftingUI : NetworkBehaviour
{
    const int CardsOffered = 3;

    [Header("Per-Player Draft Panels (index 0-2)")]
    [SerializeField] GameObject[]  _draftPanels;    // root panel per player slot
    [SerializeField] Button[][]    _cardButtons;    // 3 buttons per panel — assign via inspector helper
    [SerializeField] TMP_Text[][]  _cardNameTexts;  // label on each button
    [SerializeField] TMP_Text[]    _waitingLabels;  // shown on panels where player already picked / not eligible

    // Flat serialized button lists for Inspector (Unity can't serialize jagged arrays directly)
    [Header("Card Buttons — Slot 0")]
    [SerializeField] Button[] _slot0Buttons = new Button[CardsOffered];
    [SerializeField] TMP_Text[] _slot0Labels = new TMP_Text[CardsOffered];

    [Header("Card Buttons — Slot 1")]
    [SerializeField] Button[] _slot1Buttons = new Button[CardsOffered];
    [SerializeField] TMP_Text[] _slot1Labels = new TMP_Text[CardsOffered];

    [Header("Card Buttons — Slot 2")]
    [SerializeField] Button[] _slot2Buttons = new Button[CardsOffered];
    [SerializeField] TMP_Text[] _slot2Labels = new TMP_Text[CardsOffered];

    [Header("Waiting Labels (one per slot)")]
    [SerializeField] TMP_Text _waitingLabel0;
    [SerializeField] TMP_Text _waitingLabel1;
    [SerializeField] TMP_Text _waitingLabel2;

    // NetworkList tracks how many picks are done — init in Awake
    NetworkList<int> _pickedSlots;

    // Server-side state
    int[] _eligibleSlots;
    int[][] _dealtCards = new int[3][];   // [playerSlot][cardIndex]

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _pickedSlots = new NetworkList<int>();

        // Wire jagged arrays from flat inspector fields
        _cardButtons  = new[] { _slot0Buttons,  _slot1Buttons,  _slot2Buttons  };
        _cardNameTexts = new[] { _slot0Labels, _slot1Labels, _slot2Labels };
        _waitingLabels = new[] { _waitingLabel0, _waitingLabel1, _waitingLabel2 };
    }

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _pickedSlots.OnListChanged += OnPickedSlotsChanged;

        // Hide all panels until server deals cards
        for (int i = 0; i < 3; i++)
        {
            if (_draftPanels != null && i < _draftPanels.Length && _draftPanels[i] != null)
                _draftPanels[i].SetActive(false);
        }

        if (!IsServer) return;

        // Determine who drafts
        _eligibleSlots = GameManager.Instance.IsFirstDraft
            ? new[] { 0, 1, 2 }
            : GameManager.Instance.GetLosers(GameManager.Instance.LastRoundWinner);

        // Deal random cards to each eligible player and send via ClientRpc
        var allIds = System.Enum.GetValues(typeof(CardId)).Cast<int>().ToArray();

        foreach (int slot in _eligibleSlots)
        {
            int[] offered = DrawUniqueCards(allIds, CardsOffered);
            _dealtCards[slot] = offered;
            DealCardsClientRpc(slot, offered[0], offered[1], offered[2]);
        }

        // Non-eligible player (round winner) sees a waiting message
        for (int i = 0; i < 3; i++)
        {
            if (!_eligibleSlots.Contains(i))
                ShowWaitingClientRpc(i, "You won this round!\nWaiting for others...");
        }
    }

    public override void OnNetworkDespawn()
    {
        _pickedSlots.OnListChanged -= OnPickedSlotsChanged;
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────

    [ClientRpc]
    void DealCardsClientRpc(int slot, int card0, int card1, int card2)
    {
        if (_draftPanels == null || slot >= _draftPanels.Length || _draftPanels[slot] == null)
            return;

        _draftPanels[slot].SetActive(true);

        int[] cards = { card0, card1, card2 };
        for (int i = 0; i < CardsOffered; i++)
        {
            if (_cardNameTexts[slot] != null && i < _cardNameTexts[slot].Length)
                _cardNameTexts[slot][i].text = ((CardId)cards[i]).ToString();

            if (_cardButtons[slot] != null && i < _cardButtons[slot].Length)
            {
                int capturedI    = i;
                int capturedSlot = slot;
                int capturedCard = cards[i];
                _cardButtons[slot][i].interactable = true;
                _cardButtons[slot][i].onClick.RemoveAllListeners();
                _cardButtons[slot][i].onClick.AddListener(
                    () => OnCardButtonClicked(capturedSlot, capturedCard));
            }
        }

        if (_waitingLabels[slot] != null)
            _waitingLabels[slot].gameObject.SetActive(false);
    }

    [ClientRpc]
    void ShowWaitingClientRpc(int slot, string message)
    {
        if (_draftPanels != null && slot < _draftPanels.Length && _draftPanels[slot] != null)
            _draftPanels[slot].SetActive(true);

        if (_waitingLabels != null && slot < _waitingLabels.Length && _waitingLabels[slot] != null)
        {
            _waitingLabels[slot].gameObject.SetActive(true);
            _waitingLabels[slot].text = message;
        }

        // Disable card buttons for this slot
        if (_cardButtons != null && slot < _cardButtons.Length && _cardButtons[slot] != null)
            foreach (var btn in _cardButtons[slot])
                if (btn != null) btn.interactable = false;
    }

    // ── Pick logic ────────────────────────────────────────────────────────

    void OnCardButtonClicked(int slot, int cardId)
    {
        // Disable all buttons for this slot immediately to prevent double-pick
        if (_cardButtons[slot] != null)
            foreach (var btn in _cardButtons[slot])
                if (btn != null) btn.interactable = false;

        SubmitPickServerRpc(slot, cardId);
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitPickServerRpc(int playerSlot, int cardId)
    {
        // Guard against duplicate submission
        if (_pickedSlots.Contains(playerSlot)) return;

        GameManager.Instance.PlayerCardStacks[playerSlot].Add((CardId)cardId);
        _pickedSlots.Add(playerSlot);

        Debug.Log($"[CardDraft] Player {playerSlot} picked {(CardId)cardId}. " +
                  $"Picks in: {_pickedSlots.Count} / {_eligibleSlots.Length}");

        if (_pickedSlots.Count >= _eligibleSlots.Length)
            StartCoroutine(LoadNextMap());
    }

    System.Collections.IEnumerator LoadNextMap()
    {
        yield return new WaitForSeconds(0.5f);
        string nextMap = GameManager.Instance.GetNextMap();
        NetworkManager.Singleton.SceneManager.LoadScene(nextMap, LoadSceneMode.Single);
    }

    // ── NetworkList callback ───────────────────────────────────────────────

    void OnPickedSlotsChanged(NetworkListEvent<int> changeEvent)
    {
        // Update waiting labels on all clients as picks come in
        int slot = changeEvent.Value;
        if (_waitingLabels != null && slot < _waitingLabels.Length && _waitingLabels[slot] != null)
        {
            _waitingLabels[slot].gameObject.SetActive(true);
            _waitingLabels[slot].text = "Picked!";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static int[] DrawUniqueCards(int[] pool, int count)
    {
        // Fisher-Yates partial shuffle to pick unique cards
        int[] copy = (int[])pool.Clone();
        for (int i = 0; i < count; i++)
        {
            int j = UnityEngine.Random.Range(i, copy.Length);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        int[] result = new int[count];
        System.Array.Copy(copy, result, count);
        return result;
    }
}
