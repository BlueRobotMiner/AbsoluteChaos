using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardSlot : MonoBehaviour
{
    [SerializeField] Image    _iconImage;    // Icon child
    [SerializeField] TMP_Text _nameText;     // Name child
    [SerializeField] TMP_Text _descText;     // Description child

    [Header("World Target")]
    [SerializeField] Transform _worldTarget; // empty GO in the scene at ground level below this card

    [Header("Hover")]
    [SerializeField] float _hoverScale = 1.15f;
    [SerializeField] float _scaleSpeed = 8f;

    public CardId   CardId     { get; private set; }
    public bool     IsHovered  { get; set; }
    public Transform WorldTarget => _worldTarget;

    Vector3 _baseScale;

    void Awake() => _baseScale = transform.localScale;

    public void Setup(CardData data)
    {
        if (data == null) return;
        CardId = data.id;
        if (_nameText  != null) _nameText.text  = data.displayName;
        if (_descText  != null) _descText.text  = data.description;
        if (_iconImage != null && data.icon != null)
            _iconImage.sprite = data.icon;
    }

    void Update()
    {
        Vector3 target = IsHovered ? _baseScale * _hoverScale : _baseScale;
        transform.localScale = Vector3.Lerp(transform.localScale, target, _scaleSpeed * Time.deltaTime);
    }
}
