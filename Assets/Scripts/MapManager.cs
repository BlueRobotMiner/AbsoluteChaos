using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in each Map scene. On spawn it repositions all players and resets health.
/// Listens to GameManager delegates to trigger CardDraft after a round ends.
/// </summary>
public class MapManager : NetworkBehaviour
{
    [Header("Spawn Points")]
    [SerializeField] Transform[] _spawnPoints;

    [Header("Timing")]
    [SerializeField] float _roundEndDelay = 2f;   // pause before loading next scene

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        GameManager.Instance.OnRoundEnd  += HandleRoundEnd;
        GameManager.Instance.OnMatchEnd  += HandleMatchEnd;

        // Small delay so all clients finish loading the scene before we move players
        StartCoroutine(RespawnAfterLoad());
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        GameManager.Instance.OnRoundEnd  -= HandleRoundEnd;
        GameManager.Instance.OnMatchEnd  -= HandleMatchEnd;
    }

    // ── Respawn ───────────────────────────────────────────────────────────

    IEnumerator RespawnAfterLoad()
    {
        yield return new WaitForSeconds(0.2f);   // let scene settle
        RespawnAllPlayers();
    }

    void RespawnAllPlayers()
    {
        // Show health bars on ALL clients (SetHealthBarVisible is not a ClientRpc,
        // so we broadcast it explicitly)
        ShowHealthBarsClientRpc();

        // Reset health server-side — NetworkVariable pushes the new value to all clients
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            ph.ResetHealth();

        // includeInactive = true so we find players who were hidden on death
        var players = FindObjectsOfType<PlayerController>(true);

        foreach (var pc in players)
        {
            int slot = pc.GetPlayerSlotPublic();

            // Re-show on all clients before doing anything else
            ShowPlayerClientRpc(pc.GetComponent<NetworkObject>().NetworkObjectId);

            // Re-enable input and shooting on the owning client (plain SetInputEnabled
            // only runs server-side and won't reach Player 2's input gate)
            pc.SetInputEnabledClientRpc(true);
            if (pc.TryGetComponent(out PlayerCombat combat))
                combat.SetShootingEnabledClientRpc(true);

            // Use per-slot spawn point if available, otherwise fall back in order
            Vector2 pos;
            if (GameManager.Instance.HasSavedPositions)
            {
                // Return each player to the spawn point nearest to where they were
                // when the round ended — keeps layout consistent across maps
                pos = slot < _spawnPoints.Length
                    ? (Vector2)_spawnPoints[slot].position
                    : Vector2.zero;
            }
            else
            {
                int fallbackIndex = System.Array.IndexOf(players, pc);
                pos = fallbackIndex < _spawnPoints.Length
                    ? (Vector2)_spawnPoints[fallbackIndex].position
                    : Vector2.zero;
            }

            pc.InitializePositionClientRpc(pos);
        }

        // Positions consumed — clear so we don't reuse stale data next map
        GameManager.Instance.ClearSavedPositions();
    }

    [ClientRpc]
    void ShowPlayerClientRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(networkObjectId, out var netObj))
            netObj.gameObject.SetActive(true);
    }

    [ClientRpc]
    void ShowHealthBarsClientRpc()
    {
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            ph.SetHealthBarVisible(true);
    }

    // ── Round end ─────────────────────────────────────────────────────────

    void HandleRoundEnd(int winnerSlot)
    {
        SavePlayerPositions();
        // Sync scores to all clients so their RoundProgressUI refreshes
        SyncRoundEndClientRpc(GameManager.Instance.PlayerScores, winnerSlot);
        StartCoroutine(LoadCardDraft());
    }

    [ClientRpc]
    void SyncRoundEndClientRpc(int[] scores, int winnerSlot)
    {
        if (IsServer) return;   // server already updated by AddRoundWin
        GameManager.Instance?.ClientSyncRoundEnd(scores, winnerSlot);
    }

    void HandleMatchEnd(int winnerSlot)
    {
        SyncMatchEndClientRpc(GameManager.Instance.PlayerScores, winnerSlot);
        StartCoroutine(LoadResults(winnerSlot));
    }

    [ClientRpc]
    void SyncMatchEndClientRpc(int[] scores, int winnerSlot)
    {
        if (IsServer) return;
        GameManager.Instance?.ClientSyncMatchEnd(scores, winnerSlot);
    }

    void SavePlayerPositions()
    {
        var players = FindObjectsOfType<PlayerController>(true);
        var positions = new Vector2[3];

        foreach (var pc in players)
        {
            int slot = pc.GetPlayerSlotPublic();
            if (slot >= 0 && slot < positions.Length && pc.rb != null)
                positions[slot] = pc.rb.position;
        }

        GameManager.Instance.SavePlayerPositions(positions);
    }

    IEnumerator LoadCardDraft()
    {
        yield return new WaitForSeconds(_roundEndDelay);
        DespawnAllGuns();
        yield return null;   // one frame for despawn RPCs to reach clients before scene swap
        NetworkManager.Singleton.SceneManager.LoadScene("CardDraft", LoadSceneMode.Single);
    }

    void DespawnAllGuns()
    {
        foreach (var gun in FindObjectsOfType<Gun>(true))
            if (gun.IsSpawned) gun.DespawnSelf();
    }

    IEnumerator LoadResults(int winnerSlot)
    {
        yield return new WaitForSeconds(_roundEndDelay);
        NetworkManager.Singleton.SceneManager.LoadScene("Results", LoadSceneMode.Single);
    }
}
