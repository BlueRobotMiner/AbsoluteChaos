using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player round-win display using world-space 2D sprite circles (SpriteRenderer).
///
/// Each player row has exactly ONE pre-placed circle GO in the scene (SpriteRenderer,
/// no Collider2D). It starts invisible (renderer disabled).
///
/// Win 1  → enable that circle's SpriteRenderer, tint it to the player's color.
/// Win 2+ → clone the pre-placed circle GO, inherit its world position/scale/sprite,
///           then offset each clone to the right by one circle width + padding.
/// </summary>
public class RoundProgressUI : MonoBehaviour
{
    [System.Serializable]
    public class PlayerTracker
    {
        public GameObject      root;             // hide entire row if slot unused
        public SpriteRenderer  preplacedCircle;  // the one circle GO already in the scene
    }

    [Header("Trackers — one entry per possible player slot (0, 1, 2)")]
    [SerializeField] PlayerTracker[] _trackers;

    [Header("Gap between circles (world units, added on top of the sprite width)")]
    [SerializeField] float _spacing = 0.1f;

    List<GameObject>[] _overflow;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Start()
    {
        // Size overflow lists to match however many trackers are wired up
        _overflow = new List<GameObject>[_trackers.Length];
        for (int i = 0; i < _trackers.Length; i++)
            _overflow[i] = new List<GameObject>();

        if (GameManager.Instance == null) return;

        GameManager.Instance.OnRoundEnd += _ => RefreshAll();
        GameManager.Instance.OnMatchEnd += _ => RefreshAll();

        int connected = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ConnectedClientsIds.Count
            : _trackers.Length;

        for (int i = 0; i < _trackers.Length; i++)
        {
            if (_trackers[i].root == null) continue;
            _trackers[i].root.SetActive(i < connected);

            // Pre-placed circle starts invisible
            if (_trackers[i].preplacedCircle != null)
                _trackers[i].preplacedCircle.enabled = false;
        }

        RefreshAll();
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

        for (int slot = 0; slot < _trackers.Length; slot++)
        {
            var tracker = _trackers[slot];
            if (tracker.preplacedCircle == null) continue;

            // Reset pre-placed circle to invisible
            tracker.preplacedCircle.enabled = false;

            // Destroy previous overflow circles
            foreach (var go in _overflow[slot]) if (go != null) Destroy(go);
            _overflow[slot].Clear();

            int wins = slot < GameManager.Instance.PlayerScores.Length
                       ? GameManager.Instance.PlayerScores[slot] : 0;
            if (wins <= 0) continue;

            Color col = GetSlotColor(slot);

            // Win 1 — enable the pre-placed circle
            tracker.preplacedCircle.enabled = true;
            tracker.preplacedCircle.color   = col;

            if (wins <= 1) continue;

            // Calculate how far right each clone steps:
            // sprite world width = sprite.bounds.size.x * lossyScale.x
            var   src    = tracker.preplacedCircle;
            float width  = src.sprite != null
                           ? src.sprite.bounds.size.x * src.transform.lossyScale.x
                           : src.transform.lossyScale.x;
            float step   = width + _spacing;

            // Win 2+ — clone the pre-placed circle, offset right
            for (int w = 1; w < wins; w++)
            {
                var clone = Instantiate(src.gameObject, src.transform.parent);
                clone.transform.position   = src.transform.position + Vector3.right * step * w;
                clone.transform.localScale = src.transform.localScale;
                clone.transform.rotation   = src.transform.rotation;

                var sr = clone.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.enabled = true;
                    sr.color   = col;
                }

                _overflow[slot].Add(clone);
            }
        }
    }

    Color GetSlotColor(int slot)
    {
        var pc = PlayerController.GetBySlot(slot);
        if (pc != null) return pc.PlayerColor;

        Color[] defaults = { Color.white, new Color(1f, 0.3f, 0.3f), new Color(0.2f, 1f, 0.4f) };
        return slot < defaults.Length ? defaults[slot] : Color.white;
    }
}
