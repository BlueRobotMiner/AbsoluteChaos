using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    // 2 non-host connections = 3 total players (host is implicit)
    const int MaxConnections = 2;

    NetworkManager NM => NetworkManager.Singleton;

    /// <summary>
    /// Creates a Relay allocation, configures UnityTransport, and returns the join code.
    /// Call this BEFORE NetworkManager.StartHost().
    /// </summary>
    public async Task<string> CreateRelayAsync()
    {
        await InitServicesAsync();

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        // Use 2-arg constructor — required for Relay SDK 1.x; old 9-arg tutorials overload is wrong
        NM.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(allocation, "dtls"));

        Debug.Log($"[RelayManager] Relay created. Join code: {joinCode}");
        return joinCode;
    }

    /// <summary>
    /// Joins an existing Relay allocation and configures UnityTransport.
    /// Call this BEFORE NetworkManager.StartClient().
    /// </summary>
    public async Task JoinRelayAsync(string joinCode)
    {
        await InitServicesAsync();

        JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);

        NM.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(join, "dtls"));

        Debug.Log($"[RelayManager] Joined relay. Code: {joinCode}");
    }

    // ─────────────────────────────────────────────────────────────────────

    static async Task InitServicesAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
        {
            // Services already up — just ensure we're still signed in
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            return;
        }

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        Debug.Log($"[RelayManager] Unity Services ready. PlayerID: {AuthenticationService.Instance.PlayerId}");
    }
}
