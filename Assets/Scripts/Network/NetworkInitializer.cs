using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single entry point for all network connect/host logic.
/// UIManager calls the four public async methods; this class handles transport
/// configuration and starts NGO. Steam integration will slot in here post-semester.
/// </summary>
public class NetworkInitializer : MonoBehaviour
{
    public static NetworkInitializer Instance { get; private set; }

    [SerializeField] RelayManager _relay;
    [SerializeField] string _lobbyScene = "Lobby";

    // UIManager subscribes to these to update the UI
    public event Action<string> OnNetworkError;
    public event Action         OnConnecting;

    /// <summary>
    /// Stored after host setup so NetworkLobbyManager can read it after the scene loads.
    /// Empty string = local game (no code to display).
    /// </summary>
    public string PendingJoinCode { get; private set; } = string.Empty;

    bool _busy;

    void Awake()
    {
        Instance = this;
    }

    // ── Host ─────────────────────────────────────────────────────────────

    /// <summary>Uses Unity Relay — generates a join code other players enter.</summary>
    public async void HostOnlineAsync()
    {
        if (_busy) return;
        _busy = true;
        OnConnecting?.Invoke();

        try
        {
            PendingJoinCode = await _relay.CreateRelayAsync();

            NetworkManager.Singleton.StartHost();

            // LoadScene must be called AFTER StartHost(); requires "Enable Scene Management" on NetworkManager
            NetworkManager.Singleton.SceneManager.LoadScene(_lobbyScene, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkInitializer] HostOnline failed: {e.Message}");
            OnNetworkError?.Invoke(e.Message);
            _busy = false;
        }
    }

    /// <summary>Binds to all interfaces on port 7777 — players on the same LAN connect via IP.</summary>
    public void HostLocalAsync()
    {
        if (_busy) return;
        _busy = true;
        OnConnecting?.Invoke();

        try
        {
            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetConnectionData("0.0.0.0", 7777);

            // Empty string signals NetworkLobbyManager to hide the code text
            PendingJoinCode = string.Empty;

            NetworkManager.Singleton.StartHost();
            NetworkManager.Singleton.SceneManager.LoadScene(_lobbyScene, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkInitializer] HostLocal failed: {e.Message}");
            OnNetworkError?.Invoke(e.Message);
            _busy = false;
        }
    }

    // ── Join ─────────────────────────────────────────────────────────────

    /// <summary>Joins a public game via a relay join code (e.g. "ABC123").</summary>
    public async void JoinOnlineAsync(string joinCode)
    {
        if (_busy) return;
        _busy = true;
        OnConnecting?.Invoke();

        try
        {
            await _relay.JoinRelayAsync(joinCode);
            NetworkManager.Singleton.StartClient();
            // Server loads scene for all clients — we do nothing here
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkInitializer] JoinOnline failed: {e.Message}");
            OnNetworkError?.Invoke(e.Message);
            _busy = false;
        }
    }

    /// <summary>Connects directly to a host by IP address on port 7777.</summary>
    public void JoinLocalAsync(string ip)
    {
        if (_busy) return;
        _busy = true;
        OnConnecting?.Invoke();

        try
        {
            NetworkManager.Singleton.GetComponent<UnityTransport>()
                .SetConnectionData(ip, 7777);

            NetworkManager.Singleton.StartClient();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkInitializer] JoinLocal failed: {e.Message}");
            OnNetworkError?.Invoke(e.Message);
            _busy = false;
        }
    }

    // ── Disconnect ───────────────────────────────────────────────────────

    public void Disconnect()
    {
        NetworkManager.Singleton.Shutdown();
        _busy = false;
        SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    void Start()
    {
        NetworkManager.Singleton.OnTransportFailure += HandleTransportFailure;
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
    }

    void HandleTransportFailure()
    {
        Debug.LogError("[NetworkInitializer] Transport failure.");
        OnNetworkError?.Invoke("Connection lost.");
        Disconnect();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static string GetLocalIP()
    {
        try
        {
            IPAddress[] addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
            string ipv4 = addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .FirstOrDefault();
            return ipv4 ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
