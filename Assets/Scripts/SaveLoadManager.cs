using System.IO;
using UnityEngine;

/// <summary>
/// Singleton MonoBehaviour — handles all disk I/O for player save data.
/// Persists across scenes via DontDestroyOnLoad.
/// Uses JsonUtility + Application.persistentDataPath (course resource pattern).
/// </summary>
public class SaveLoadManager : MonoBehaviour
{
    public static SaveLoadManager Instance { get; private set; }

    static string SavePath => Path.Combine(Application.persistentDataPath, "playersave.json");

    /// <summary>The in-memory copy of the save data. Always valid — defaults if no file exists.</summary>
    public PlayerSaveData Data { get; private set; } = new PlayerSaveData();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);   // destroy only this component, NOT the whole GO
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Writes the current Data to disk as JSON.</summary>
    public void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(Data, true);
            File.WriteAllText(SavePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveLoadManager] Save failed: {e.Message}");
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────

    void Load()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                Data = JsonUtility.FromJson<PlayerSaveData>(json);
            }
            // No file yet — keep default-constructed Data (first launch)
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveLoadManager] Load failed (corrupt save?): {e.Message}");
            Data = new PlayerSaveData();   // reset to safe defaults
        }
    }

    void OnApplicationQuit() => Save();   // safety flush on exit
}
