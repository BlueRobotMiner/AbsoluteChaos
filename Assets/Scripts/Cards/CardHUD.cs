using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Map-scene HUD showing each player's collected card icons.
///
/// ICON ROWS (main HUD canvas):
///   Each PlayerCardPanel has a nameLabel and an iconAnchor.
///   Card icons are plain Image GOs spawned as siblings of the anchor.
///   Icon 0 sits at the anchor's anchoredPosition; each additional icon
///   steps LEFT by iconSpacing pixels.
///
/// TOOLTIP CARD (separate canvas, higher Sort Order):
///   One card panel (background + icon + name + description) hidden by default.
///   _tooltipCard is its root RectTransform — the designer positions it in the
///   editor at the spot for Player 1 / Icon 0.  That position is stored as
///   _tooltipBasePos in Awake and used as the origin for all offsets:
///
///       tooltipX = baseX  -  (iconIndex  * panel.iconSpacing)
///       tooltipY = baseY  -  (playerSlot * _playerRowSpacing)
///
///   Hover detection runs in Update() — no per-icon component needed.
///   After _hoverDelay seconds on the same icon the card appears.
///   Sliding to an adjacent icon while the card is visible immediately
///   moves and repopulates it (no second wait).
/// </summary>
public class CardHUD : MonoBehaviour
{
    [System.Serializable]
    public class PlayerCardPanel
    {
        public GameObject    root;
        public TMP_Text      nameLabel;     // colored to slot color
        public RectTransform iconAnchor;    // defines where icon 0 sits
        public float         iconSpacing = 48f;
    }

    [Header("Panels — one per possible player slot (0, 1, 2)")]
    [SerializeField] PlayerCardPanel[] _panels;

    [Header("Tooltip Card (separate canvas)")]
    [SerializeField] RectTransform _tooltipCard;     // root / background of the card panel
    [SerializeField] Image         _tooltipIcon;
    [SerializeField] TMP_Text      _tooltipName;
    [SerializeField] TMP_Text      _tooltipDesc;
    [SerializeField] float         _hoverDelay       = 0.4f;
    [SerializeField] float         _playerRowSpacing = 80f;  // pixels DOWN per player slot

    [Header("Colors — match PlayerController slot colors")]
    [SerializeField] Color[] _slotColors =
    {
        Color.white,
        new Color(1f, 0.4f, 0.4f, 1f),
        new Color(0.3f, 1f, 0.5f, 1f),
    };

    // Spawned icons per slot — List[slot][iconIndex]
    readonly List<RectTransform>[] _icons = new List<RectTransform>[3];

    // Base tooltip position captured from editor placement
    Vector2 _tooltipBasePos;

    // Hover tracking
    int   _hoveredSlot  = -1;
    int   _hoveredIndex = -1;
    float _hoverTimer;
    bool  _tooltipVisible;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        for (int i = 0; i < _icons.Length; i++)
            _icons[i] = new List<RectTransform>();

        // Store the editor-placed position as the origin for all tooltip offsets
        if (_tooltipCard != null)
        {
            _tooltipBasePos = _tooltipCard.anchoredPosition;
            _tooltipCard.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        // Ensure tooltip is hidden at runtime regardless of editor scene state
        if (_tooltipCard != null) _tooltipCard.gameObject.SetActive(false);

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

    // ── Hover detection ────────────────────────────────────────────────────

    void Update()
    {
        if (GameManager.Instance == null) return;

        int  foundSlot  = -1;
        int  foundIndex = -1;

        // Check every spawned icon across all slots
        for (int i = 0; i < _icons.Length && foundSlot < 0; i++)
        {
            for (int j = 0; j < _icons[i].Count; j++)
            {
                var rt = _icons[i][j];
                if (rt == null) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, Camera.main))
                {
                    foundSlot  = i;
                    foundIndex = j;
                    break;
                }
            }
        }

        if (foundSlot >= 0)
        {
            // Moved to a new icon
            if (foundSlot != _hoveredSlot || foundIndex != _hoveredIndex)
            {
                _hoveredSlot  = foundSlot;
                _hoveredIndex = foundIndex;
                _hoverTimer   = 0f;

                // If tooltip already showing, immediately reposition + repopulate
                if (_tooltipVisible)
                    PlaceTooltip(_hoveredSlot, _hoveredIndex);
            }

            // Count up — show once threshold is crossed
            _hoverTimer += Time.deltaTime;
            if (!_tooltipVisible && _hoverTimer >= _hoverDelay)
                PlaceTooltip(_hoveredSlot, _hoveredIndex);
        }
        else
        {
            // No icon hovered
            if (_hoveredSlot >= 0 || _tooltipVisible)
            {
                _hoveredSlot   = -1;
                _hoveredIndex  = -1;
                _hoverTimer    = 0f;
                _tooltipVisible = false;
                if (_tooltipCard != null) _tooltipCard.gameObject.SetActive(false);
            }
        }
    }

    // ── Tooltip placement ──────────────────────────────────────────────────

    void PlaceTooltip(int slot, int iconIndex)
    {
        if (_tooltipCard == null) return;

        var stack = GameManager.Instance?.PlayerCardStacks[slot];
        if (stack == null || iconIndex >= stack.Count) return;

        var data = CardDatabase.Instance?.GetById(stack[iconIndex]);
        if (data == null) return;

        // Populate content
        if (_tooltipIcon != null) _tooltipIcon.sprite = data.icon;
        if (_tooltipName != null) _tooltipName.text   = data.displayName;
        if (_tooltipDesc != null) _tooltipDesc.text   = data.description;

        // Position: step left per icon index, step down per player slot
        float spacing = slot < _panels.Length ? _panels[slot].iconSpacing : 48f;
        _tooltipCard.anchoredPosition = new Vector2(
            _tooltipBasePos.x - iconIndex * spacing,
            _tooltipBasePos.y - slot      * _playerRowSpacing);

        _tooltipCard.gameObject.SetActive(true);
        _tooltipVisible = true;
    }

    // ── Icon row builder ───────────────────────────────────────────────────

    public void Refresh()
    {
        if (GameManager.Instance == null) return;

        int connected = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ConnectedClientsIds.Count
            : _panels.Length;

        // Hide tooltip whenever the stack changes
        _tooltipVisible = false;
        if (_tooltipCard != null) _tooltipCard.gameObject.SetActive(false);

        for (int i = 0; i < _panels.Length; i++)
        {
            var panel = _panels[i];
            if (panel.root == null) continue;

            bool active = i < connected;
            panel.root.SetActive(active);
            if (!active) continue;

            if (panel.nameLabel != null)
            {
                var pc = PlayerController.GetBySlot(i);
                panel.nameLabel.text  = pc != null ? pc.PlayerDisplayName : $"Player {i + 1}";
                panel.nameLabel.color = pc != null ? pc.PlayerColor
                                      : (i < _slotColors.Length ? _slotColors[i] : Color.white);
            }

            // Destroy old icons
            foreach (var rt in _icons[i])
                if (rt != null) Destroy(rt.gameObject);
            _icons[i].Clear();

            if (panel.iconAnchor == null) continue;

            var stack = GameManager.Instance.PlayerCardStacks[i];
            if (stack == null || stack.Count == 0) continue;

            Vector2 anchorPos = panel.iconAnchor.anchoredPosition;

            for (int j = 0; j < stack.Count; j++)
            {
                // Pull the sprite straight from CardIconRegistry — same source the draft scene uses
                Sprite icon = CardIconRegistry.Instance?.GetIcon(stack[j]);

                var go = new GameObject($"CardIcon_P{i}_{j}", typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(panel.iconAnchor.parent, false);
                rt.anchorMin        = panel.iconAnchor.anchorMin;
                rt.anchorMax        = panel.iconAnchor.anchorMax;
                rt.sizeDelta        = panel.iconAnchor.sizeDelta;
                rt.anchoredPosition = anchorPos + Vector2.left * (j * panel.iconSpacing);

                go.GetComponent<Image>().sprite = icon;

                _icons[i].Add(rt);
            }
        }
    }
}
