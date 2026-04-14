using UnityEngine;
using TMPro;

public class InfoNavButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    private static readonly Color ActiveText = new Color(0.76f, 0.35f, 0.10f, 1f);
    private static readonly Color IdleText   = new Color(0.35f, 0.35f, 0.42f, 1f);

    public void SetActiveColor(bool isActive)
    {
        label.color = isActive ? ActiveText : IdleText;
    }
}