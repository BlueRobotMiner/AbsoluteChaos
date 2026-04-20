using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Results screen flow:
///   Host   → sees "Rematch" + "Quit to Menu"
///   Clients → see winner banner + "Quit to Menu", then a rematch popup when host initiates
///
/// Rematch requires ALL non-host clients to accept.
/// Any client declining cancels the rematch and notifies the host.
/// </summary>
public class ResultsManager : NetworkBehaviour
{
    [Header("Win Banner")]
    [SerializeField] TMP_Text _winnerText;

    [Header("Host Buttons (shown to host only)")]
    [SerializeField] Button   _rematchButton;
    [SerializeField] Button   _quitButton;

    [Header("Client Rematch Popup (hidden until host initiates)")]
    [SerializeField] GameObject _rematchPopup;      // the whole popup panel
    [SerializeField] TMP_Text   _popupBodyText;     // "Player 1 wants a rematch!"
    [SerializeField] Button     _acceptButton;
    [SerializeField] Button     _declineButton;

    [Header("Client Quit Button (always visible to clients)")]
    [SerializeField] Button _clientQuitButton;

    [Header("Host status text (shown after initiating rematch)")]
    [SerializeField] TMP_Text _hostStatusText;      // "Waiting for players..." / "X declined."

    [Header("Colors — match PlayerController slot colors")]
    [SerializeField] Color[] _slotColors =
    {
        Color.white,
        new Color(1f, 0.3f, 0.3f, 1f),
        new Color(0.2f, 1f, 0.4f, 1f),
    };

    // Server-side accept tracking
    readonly HashSet<ulong> _accepted = new HashSet<ulong>();
    int _clientsNeeded;

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        PopulateUI();

        if (IsHost)
        {
            // Host sees Rematch + Quit; popup and client quit are hidden
            if (_rematchButton != null)
            {
                _rematchButton.gameObject.SetActive(true);
                _rematchButton.onClick.AddListener(OnHostRematchClicked);
            }
            if (_quitButton != null)
            {
                _quitButton.gameObject.SetActive(true);
                _quitButton.onClick.AddListener(OnQuitClicked);
            }
            if (_rematchPopup != null)   _rematchPopup.SetActive(false);
            if (_clientQuitButton != null) _clientQuitButton.gameObject.SetActive(false);
            if (_hostStatusText != null)  _hostStatusText.text = "";

            // Count non-host clients we'll need to hear from
            _clientsNeeded = NetworkManager.Singleton.ConnectedClientsIds.Count - 1;
        }
        else
        {
            // Clients see their quit button; host controls and popup start hidden
            if (_rematchButton != null) _rematchButton.gameObject.SetActive(false);
            if (_quitButton != null)    _quitButton.gameObject.SetActive(false);
            if (_rematchPopup != null)  _rematchPopup.SetActive(false);

            if (_clientQuitButton != null)
            {
                _clientQuitButton.gameObject.SetActive(true);
                _clientQuitButton.onClick.AddListener(OnQuitClicked);
            }

            if (_acceptButton != null)  _acceptButton.onClick.AddListener(OnAcceptClicked);
            if (_declineButton != null) _declineButton.onClick.AddListener(OnDeclineClicked);
        }
    }

    // ── UI population ─────────────────────────────────────────────────────

    void PopulateUI()
    {
        if (GameManager.Instance == null || _winnerText == null) return;

        int winnerSlot = GameManager.Instance.LastRoundWinner;
        Color col = winnerSlot >= 0 && winnerSlot < _slotColors.Length
            ? _slotColors[winnerSlot] : Color.white;

        _winnerText.text  = winnerSlot >= 0 ? $"Player {winnerSlot + 1} Wins!" : "Match Over!";
        _winnerText.color = col;
    }

    // ── Host: initiate rematch ────────────────────────────────────────────

    void OnHostRematchClicked()
    {
        if (!IsHost) return;

        _rematchButton.interactable = false;

        if (_clientsNeeded <= 0)
        {
            // Solo host — skip waiting
            StartRematch();
            return;
        }

        _accepted.Clear();

        if (_hostStatusText != null)
            _hostStatusText.text = "Waiting for players...";

        int winnerSlot = GameManager.Instance.LastRoundWinner;
        string initiatorName = winnerSlot >= 0 ? $"Player {winnerSlot + 1}" : "The host";
        SendRematchRequestClientRpc(initiatorName);
    }

    // ── ClientRpc: show popup on all non-host clients ─────────────────────

    [ClientRpc]
    void SendRematchRequestClientRpc(string initiatorName)
    {
        if (IsHost) return;

        if (_rematchPopup != null) _rematchPopup.SetActive(true);
        if (_popupBodyText != null) _popupBodyText.text = $"{initiatorName} wants a rematch!";
    }

    // ── Client: accept ────────────────────────────────────────────────────

    void OnAcceptClicked()
    {
        if (_rematchPopup != null) _rematchPopup.SetActive(false);
        RespondRematchServerRpc(NetworkManager.Singleton.LocalClientId, accepted: true);
    }

    // ── Client: decline ───────────────────────────────────────────────────

    void OnDeclineClicked()
    {
        if (_rematchPopup != null) _rematchPopup.SetActive(false);
        RespondRematchServerRpc(NetworkManager.Singleton.LocalClientId, accepted: false);
    }

    // ── ServerRpc: tally responses ────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    void RespondRematchServerRpc(ulong clientId, bool accepted)
    {
        if (accepted)
        {
            _accepted.Add(clientId);

            if (_accepted.Count >= _clientsNeeded)
                StartRematch();   // everyone said yes
        }
        else
        {
            // Find which player slot declined
            int declinerSlot = 0;
            int idx = 0;
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (id == clientId) { declinerSlot = idx; break; }
                idx++;
            }

            if (_hostStatusText != null)
                _hostStatusText.text = $"Player {declinerSlot + 1} declined.";

            if (_rematchButton != null)
                _rematchButton.interactable = true;

            // Tell all clients to hide their popup in case it's still showing
            HidePopupClientRpc();
        }
    }

    [ClientRpc]
    void HidePopupClientRpc()
    {
        if (_rematchPopup != null) _rematchPopup.SetActive(false);
    }

    // ── Start the rematch (server) ────────────────────────────────────────

    void StartRematch()
    {
        GameManager.Instance.ResetForNewMatch();
        // Go straight to CardDraft — all players pick fresh cards, then into maps
        // ResetForNewMatch already sets IsFirstDraft = true so everyone drafts
        NetworkManager.Singleton.SceneManager.LoadScene(
            "CardDraft", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // ── Quit — local only, no network coordination needed ─────────────────

    void OnQuitClicked()
    {
        GameManager.Instance?.ResetForNewMatch();
        NetworkManager.Singleton.Shutdown();
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
}
