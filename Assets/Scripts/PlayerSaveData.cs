using UnityEngine;

/// <summary>
/// Plain serializable data class for player customization settings.
/// Stored as JSON at Application.persistentDataPath via SaveLoadManager.
/// </summary>
[System.Serializable]
public class PlayerSaveData
{
    public string playerName    = "Player";
    public int    headTypeIndex = 0;        // 0 = circle, 1 = diamond, 2 = polygon
    public float  colorR        = 1f;
    public float  colorG        = 1f;
    public float  colorB        = 1f;

    public Color ToColor() => new Color(colorR, colorG, colorB);

    public void FromColor(Color c)
    {
        colorR = c.r;
        colorG = c.g;
        colorB = c.b;
    }
}
