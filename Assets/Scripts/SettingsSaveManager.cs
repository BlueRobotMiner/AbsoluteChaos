using System.IO;
using UnityEngine;

/// <summary>
/// Singleton MonoBehaviour — handles disk I/O for audio/game settings.
/// Separate from SaveLoadManager so player customization and settings stay independent.
/// Persists across scenes via DontDestroyOnLoad.
/// </summary>
public class SettingsSaveManager : MonoBehaviour
{
    static SettingsSaveManager _instance;
    public static SettingsSaveManager Instance
    {
        get
        {
            // Auto-create if nobody placed this in the scene — guarantees Instance is never null
            if (_instance == null)
            {
                var go = new GameObject("[SettingsSaveManager]");
                _instance = go.AddComponent<SettingsSaveManager>();
            }
            return _instance;
        }
    }

    static string SavePath => Path.Combine(Application.persistentDataPath, "settings.json");

    public SettingsSaveData Data { get; private set; } = new SettingsSaveData();

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(Data, true));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SettingsSaveManager] Save failed: {e.Message}");
        }
    }

    void Load()
    {
        try
        {
            if (File.Exists(SavePath))
                Data = JsonUtility.FromJson<SettingsSaveData>(File.ReadAllText(SavePath));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SettingsSaveManager] Load failed: {e.Message}");
            Data = new SettingsSaveData();
        }
    }

    void OnApplicationQuit() => Save();
}
