using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to any GO near a slider. Shows the slider's current value as a percentage.
/// The background panel is just a UI Image on this same GO — set it up in the editor.
///
/// Scene setup example:
///   SliderRow
///   ├── Label          (TMP_Text "Music Volume")
///   ├── MySlider       (Slider component)
///   └── ValueDisplay   ← put this component here
///       ├── Background (Image — gives the little panel behind the number)
///       └── Text       (TMP_Text — wire into _valueText below)
/// </summary>
public class SliderValueDisplay : MonoBehaviour
{
    [SerializeField] Slider    _slider;
    [SerializeField] TMP_Text  _valueText;
    [SerializeField] string    _format = "{0}%";   // change to "{0}" if you don't want the % sign

    void Awake()
    {
        if (_slider != null)
            _slider.onValueChanged.AddListener(UpdateDisplay);
    }

    void OnEnable()
    {
        // Refresh immediately when the panel opens so the text matches the current value
        if (_slider != null) UpdateDisplay(_slider.value);
    }

    void UpdateDisplay(float value)
    {
        if (_valueText != null)
            _valueText.text = string.Format(_format, Mathf.RoundToInt(value));
    }
}
