using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a per-player round progress bar (slider style, 0 → RoundsToWin).
/// Automatically hides trackers for slots with no connected player.
/// Attach to a Canvas GameObject in each Map scene.
/// </summary>
public class RoundProgressUI : MonoBehaviour
{
    [System.Serializable]
    public class PlayerTracker
    {
        public GameObject root;       // parent GO — hide entire tracker if slot unused
        public Slider     slider;     // fill represents wins earned
        public TMP_Text   label;      // "P1", "P2", etc.
        public Image      fill;       // optional — tint to player color
    }

    [Header("Trackers — one per possible player slot (0, 1, 2)")]
    [SerializeField] PlayerTracker[] _trackers;

    [Header("Colors — must match PlayerController._slotColors order")]
    [SerializeField] Color[] _slotColors =
    {
        Color.white,
        new Color(1f, 0.3f, 0.3f, 1f),
        new Color(0.2f, 1f, 0.4f, 1f),
    };

    // ── Unity ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.OnRoundEnd += _ => RefreshAll();
        GameManager.Instance.OnMatchEnd += _ => RefreshAll();

        // Determine how many players are actually connected
        int connected = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ConnectedClientsIds.Count
            : _trackers.Length;

        for (int i = 0; i < _trackers.Length; i++)
        {
            if (_trackers[i].root == null) continue;

            bool active = i < connected;
            _trackers[i].root.SetActive(active);

            if (!active) continue;

            // Configure slider range
            if (_trackers[i].slider != null)
            {
                _trackers[i].slider.minValue = 0;
                _trackers[i].slider.maxValue = GameManager.RoundsToWin;
                _trackers[i].slider.value    = GameManager.Instance.PlayerScores[i];
            }

            // Label
            if (_trackers[i].label != null)
                _trackers[i].label.text = $"P{i + 1}";

            // Tint fill to player color
            if (_trackers[i].fill != null && i < _slotColors.Length)
                _trackers[i].fill.color = _slotColors[i];
        }
    }

    void OnDestroy()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnRoundEnd -= _ => RefreshAll();
        GameManager.Instance.OnMatchEnd -= _ => RefreshAll();
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    void RefreshAll()
    {
        if (GameManager.Instance == null) return;

        for (int i = 0; i < _trackers.Length; i++)
        {
            if (_trackers[i].slider == null) continue;
            _trackers[i].slider.value = GameManager.Instance.PlayerScores[i];
        }
    }
}
