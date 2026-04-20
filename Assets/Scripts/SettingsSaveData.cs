/// <summary>
/// Serializable data class for audio/game settings.
/// Stored separately from player customization so the two concerns don't mix.
/// Saved to settings.json via SettingsSaveManager.
/// </summary>
[System.Serializable]
public class SettingsSaveData
{
    public float musicVolume = 80f;   // 0 – 100
    public float sfxVolume   = 80f;   // 0 – 100
}
