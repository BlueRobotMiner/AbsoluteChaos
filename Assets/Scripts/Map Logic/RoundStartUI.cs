using TMPro;
using UnityEngine;

/// <summary>
/// Plain MonoBehaviour — attach to any GO in the map scene (e.g. directly on the Canvas).
/// MapManager drives this via its own ClientRpcs; no NetworkObject required here.
/// </summary>
public class RoundStartUI : MonoBehaviour
{
    [SerializeField] GameObject _countdownPanel;
    [SerializeField] TMP_Text   _countdownText;

    public void ShowLabel(string label)
    {
        if (_countdownPanel != null) _countdownPanel.SetActive(true);
        if (_countdownText  != null) _countdownText.text = label;
    }

    public void Hide()
    {
        if (_countdownPanel != null) _countdownPanel.SetActive(false);
        if (_countdownText  != null) _countdownText.text = string.Empty;
    }
}
