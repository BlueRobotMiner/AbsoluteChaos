using UnityEngine;

public class CardData
{
    public CardId id;
    public string displayName;
    public string description;
    public string category;
    public string abbreviation;  // 2-letter symbol shown on HUD e.g. "EX", "RF", "SB"
    public Sprite icon;          // set at runtime by CardIconRegistry — not stored in DB
}
