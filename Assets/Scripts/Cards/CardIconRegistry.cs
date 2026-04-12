using System;
using UnityEngine;

/// <summary>
/// Maps CardId to a Sprite and tint color in the Inspector.
/// Attach to the GameManager GO so it persists across all scenes.
/// CardDatabase.GetRandomCards() pulls icons from here at runtime.
/// </summary>
public class CardIconRegistry : MonoBehaviour
{
    public static CardIconRegistry Instance { get; private set; }

    [SerializeField] CardIconEntry[] _entries;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public Sprite GetIcon(CardId id)
    {
        foreach (var entry in _entries)
            if (entry.id == id) return entry.icon;
        return null;
    }

    public Color GetColor(CardId id)
    {
        foreach (var entry in _entries)
            if (entry.id == id) return entry.tint;
        return Color.white;
    }
}

[Serializable]
public class CardIconEntry
{
    public CardId id;
    public Sprite icon;
    public Color  tint = Color.white;
}
