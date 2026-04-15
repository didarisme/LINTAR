using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoNavButtons : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI label;

    private static readonly Color ActiveBg = new Color(0.76f, 0.35f, 0.10f, 0.15f);
    private static readonly Color ActiveText = new Color(0.76f, 0.35f, 0.10f, 1f);
    private static readonly Color IdleBg = new Color(0f, 0f, 0f, 0f);
    private static readonly Color IdleText = new Color(0.35f, 0.35f, 0.42f, 1f);

    public void SetActive(bool isActive)
    {
        background.color = isActive ? ActiveBg  : IdleBg;
        label.color = isActive ? ActiveText : IdleText;
    }
}