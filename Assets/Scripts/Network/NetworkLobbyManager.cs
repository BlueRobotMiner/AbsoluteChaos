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
    [SerializeField] TMP_Text _codeText;
    [SerializeField] TMP_Text _ipText;
    [SerializeField] TMP_Text _playerCountText;
    [SerializeField] TMP_Text _statusText;

    [Header("Spawning")]
    [SerializeField] GameObject  _playerPrefab;
    [SerializeField] Transform[] _spawnPoints;
    [SerializeField] Vector3     _spawnOffset;   // tune this to align root with visual character center

    // NetworkList must be initialized in Awake — field initializers crash on NGO spawn
    NetworkList<ulong> _connectedClients;

    // Synced join code — server writes, all clients read
    NetworkVariable<FixedString64Bytes> _joinCode = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    bool _gameStarted;
    int  _nextSlot;

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

        NetworkManager.Singleton.OnClientConnectedCallback  += ServerOnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += ServerOnClientDisconnected;

        // Spawn host as slot 0
        _connectedClients.Add(NetworkManager.Singleton.LocalClientId);
        SpawnPlayer(NetworkManager.Singleton.LocalClientId);

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
        _nextSlot = 0;
    }

    // ── Server callbacks ─────────────────────────────────────────────────

    void ServerOnClientConnected(ulong clientId)
    {
        // Skip host — already handled in OnNetworkSpawn
        if (clientId == NetworkManager.Singleton.LocalClientId) return;

        _connectedClients.Add(clientId);
        SpawnPlayer(clientId);

        if (_connectedClients.Count >= 3)
            SetStatusClientRpc("Kill to start!");
    }

    void SpawnPlayer(ulong clientId)
    {
        if (_playerPrefab == null)
        {
            Debug.LogError("[LobbyManager] _playerPrefab is not assigned!");
            return;
        }

        Vector3 raw = _spawnPoints != null && _nextSlot < _spawnPoints.Length
            ? _spawnPoints[_nextSlot].position
            : Vector3.zero;
        Vector3 spawnPos = new Vector3(raw.x + _spawnOffset.x, raw.y + _spawnOffset.y, 0f);

        var go  = Instantiate(_playerPrefab, spawnPos, Quaternion.identity);
        var net = go.GetComponent<NetworkObject>();

        // SpawnAsPlayerObject sets ownership to clientId automatically
        // OnGainedOwnership in PlayerController fires on the client after this completes
        net.SpawnAsPlayerObject(clientId);

        Debug.Log($"[LobbyManager] Spawned player for client {clientId} at slot {_nextSlot} pos {spawnPos}");
        _nextSlot++;
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
