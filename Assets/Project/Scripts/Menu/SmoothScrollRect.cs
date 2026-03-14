using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SmoothScrollRect : ScrollRect
{
    [Header("Smooth Scroll")]
    [SerializeField] private float smoothSpeed      = 12f;
    [SerializeField] private float scrollMultiplier  = 3f;

    private Vector2 _targetPosition;
    private bool    _isDragging;

    protected override void OnEnable()
    {
        base.OnEnable();
        _targetPosition = normalizedPosition;
    }

    /// <summary>
    /// Мгновенно сбросить скролл к заданной позиции (без плавной анимации).
    /// </summary>
    public void SetPositionImmediate(Vector2 normalized)
    {
        _targetPosition    = normalized;
        normalizedPosition = normalized;
        velocity           = Vector2.zero;
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        _isDragging = true;
        base.OnBeginDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        _isDragging = false;
        _targetPosition = normalizedPosition;
        base.OnEndDrag(eventData);
    }

    public override void OnScroll(PointerEventData eventData)
    {
        if (!IsActive()) return;

        float delta = eventData.scrollDelta.y * scrollMultiplier;

        if (vertical)
        {
            float contentHeight = content.rect.height - viewport.rect.height;
            if (contentHeight > 0f)
                _targetPosition.y = Mathf.Clamp01(_targetPosition.y + delta / contentHeight);
        }

        if (horizontal)
        {
            float contentWidth = content.rect.width - viewport.rect.width;
            if (contentWidth > 0f)
                _targetPosition.x = Mathf.Clamp01(_targetPosition.x - delta / contentWidth);
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        if (_isDragging) return;

        normalizedPosition = Vector2.Lerp(normalizedPosition, _targetPosition, Time.unscaledDeltaTime * smoothSpeed);
    }

    protected override void SetContentAnchoredPosition(Vector2 position)
    {
        base.SetContentAnchoredPosition(position);

        if (_isDragging)
            _targetPosition = normalizedPosition;
    }
}
