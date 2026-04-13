using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Displays each player's active card stack on the local screen.
/// Place on a Screen Space Canvas in every Map and Lobby scene.
/// Refreshes automatically after each draft via GameManager delegates.
/// </summary>
public class CardHUD : MonoBehaviour
{
    [System.Serializable]
    public class PlayerCardPanel
    {
        public GameObject root;       // parent GO — hide if slot unused
        public TMP_Text   nameLabel;  // "P1", "P2", "P3"
        public TMP_Text   cardList;   // one card per line
    }

    [Header("Panels — one per slot (left side = P1, right side = P2, etc.)")]
    [SerializeField] PlayerCardPanel[] _panels;

    [Header("Colors")]
    [SerializeField] Color[] _slotColors =
    {
        Color.white,
        new Color(1f, 0.4f, 0.4f, 1f),
        new Color(0.3f, 1f, 0.5f, 1f),
    };

    // ── Unity ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundEnd += _ => Refresh();
            GameManager.Instance.OnMatchEnd += _ => Refresh();
        }

        Refresh();
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundEnd -= _ => Refresh();
            GameManager.Instance.OnMatchEnd -= _ => Refresh();
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    /// <summary>Call this after any card is picked to update all panels.</summary>
    public void Refresh()
    {
        if (GameManager.Instance == null) return;

        int connected = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ConnectedClientsIds.Count
            : _panels.Length;

        for (int i = 0; i < _panels.Length; i++)
        {
            var panel = _panels[i];
            if (panel.root == null) continue;

            bool active = i < connected;
            panel.root.SetActive(active);
            if (!active) continue;

            // Player label with slot color
            if (panel.nameLabel != null)
            {
                panel.nameLabel.text  = $"P{i + 1}";
                panel.nameLabel.color = i < _slotColors.Length ? _slotColors[i] : Color.white;
            }

            // Card list — one per line, or "No cards" if stack is empty
            if (panel.cardList != null)
            {
                var stack = GameManager.Instance.PlayerCardStacks[i];
                if (stack == null || stack.Count == 0)
                {
                    panel.cardList.text = "<color=#888888>No cards</color>";
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var card in stack)
                        sb.AppendLine(CardDisplayName(card));
                    panel.cardList.text = sb.ToString().TrimEnd();
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static string CardDisplayName(CardId id) => id switch
    {
        CardId.ExplosiveRounds => "Explosive Rounds",
        CardId.Ricochet        => "Ricochet",
        CardId.RapidFire       => "Rapid Fire",
        CardId.HealthPackRain  => "Health Pack Rain",
        CardId.AmmoStash       => "Ammo Stash",
        CardId.SpeedBoost      => "Speed Boost",
        CardId.DoubleJump      => "Double Jump",
        CardId.Fragile         => "Fragile",
        CardId.LowGravity      => "Low Gravity",
        CardId.HeavyGravity    => "Heavy Gravity",
        _                      => id.ToString(),
    };
}
