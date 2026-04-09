using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using TMPro;
using UnityEngine;

/// <summary>
/// Lives in the Lobby scene. Tracks connected players, shows the relay join code
/// (public games only), and triggers the CardDraft scene on the first kill.
/// </summary>
public class NetworkLobbyManager : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_Text _codeText;        // relay join code — shown for public games only
    [SerializeField] TMP_Text _ipText;          // local IP        — shown for local games only
    [SerializeField] TMP_Text _playerCountText; // "Players: 2 / 3"
    [SerializeField] TMP_Text _statusText;      // "Waiting for players..." / "Kill to start!"

    // NetworkList must be initialized in Awake — field initializers crash on NGO spawn
    NetworkList<ulong> _connectedClients;

    // Synced join code — server writes, all clients read
    NetworkVariable<FixedString64Bytes> _joinCode = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    bool _gameStarted;

    // ── Unity ────────────────────────────────────────────────────────────

    void Awake()
    {
        _connectedClients = new NetworkList<ulong>();
    }

    // ── NGO ──────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _connectedClients.OnListChanged += OnPlayerListChanged;
        _joinCode.OnValueChanged        += OnJoinCodeChanged;

        // Refresh UI with current values (important for late-joining clients)
        RefreshPlayerCount();
        RefreshJoinCode(_joinCode.Value);

        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback    += ServerOnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback   += ServerOnClientDisconnected;

        // Host counts as client 0
        _connectedClients.Add(NetworkManager.Singleton.LocalClientId);

        // Read the join code stored by NetworkInitializer before scene loaded.
        // Public game = relay code shown; local game = empty string = text hidden.
        if (NetworkInitializer.Instance != null)
            SetJoinCode(NetworkInitializer.Instance.PendingJoinCode);
    }

    public override void OnNetworkDespawn()
    {
        _connectedClients.OnListChanged -= OnPlayerListChanged;
        _joinCode.OnValueChanged        -= OnJoinCodeChanged;

        if (!IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback  -= ServerOnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= ServerOnClientDisconnected;
    }

    // ── Server callbacks ─────────────────────────────────────────────────

    void ServerOnClientConnected(ulong clientId)
    {
        _connectedClients.Add(clientId);

        if (_connectedClients.Count >= 3)
            SetStatusClientRpc("Kill to start!");
    }

    void ServerOnClientDisconnected(ulong clientId)
    {
        for (int i = 0; i < _connectedClients.Count; i++)
        {
            if (_connectedClients[i] == clientId)
            {
                _connectedClients.RemoveAt(i);
                break;
            }
        }
    }

    // ── Kill trigger ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerHealth on the server when a player is eliminated.
    /// Once all 3 players are connected the first kill moves everyone to CardDraft.
    /// </summary>
    public void NotifyKill()
    {
        if (!IsServer || _gameStarted) return;
        if (_connectedClients.Count < 3) return;

        _gameStarted = true;
        StartCoroutine(TransitionToCardDraft());
    }

    IEnumerator TransitionToCardDraft()
    {
        // Brief pause so players can see the kill before the scene changes
        yield return new WaitForSeconds(1.5f);

        if (GameManager.Instance != null)
            GameManager.Instance.IsFirstDraft = true;

        NetworkManager.Singleton.SceneManager.LoadScene("CardDraft", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Called by NetworkInitializer (server only) after relay join code is obtained.
    /// Pass empty string for local games — the join code text will be hidden.
    /// </summary>
    public void SetJoinCode(string code)
    {
        if (!IsServer) return;
        _joinCode.Value = new FixedString64Bytes(code);
    }

    // ── NetworkVariable / NetworkList callbacks ───────────────────────────

    void OnPlayerListChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshPlayerCount();
    }

    void OnJoinCodeChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        RefreshJoinCode(current);
    }

    // ── UI helpers ───────────────────────────────────────────────────────

    void RefreshPlayerCount()
    {
        if (_playerCountText != null)
            _playerCountText.text = $"Players: {_connectedClients.Count} / 3";

        if (_statusText != null)
            _statusText.text = _connectedClients.Count < 3 ? "Waiting for players..." : "Kill to start!";
    }

    void RefreshJoinCode(FixedString64Bytes code)
    {
        string codeStr = code.ToString();
        bool isPublicGame = !string.IsNullOrEmpty(codeStr);

        if (_codeText != null)
        {
            _codeText.gameObject.SetActive(isPublicGame);
            if (isPublicGame)
                _codeText.text = $"Here's your game code!\nCode: {codeStr}";
        }

        if (_ipText != null)
        {
            _ipText.gameObject.SetActive(!isPublicGame);
            if (!isPublicGame)
                _ipText.text = $"Here's your game IP!\nIP: {NetworkInitializer.GetLocalIP()}";
        }
    }

    [ClientRpc]
    void SetStatusClientRpc(string message)
    {
        if (_statusText != null)
            _statusText.text = message;
    }
}
