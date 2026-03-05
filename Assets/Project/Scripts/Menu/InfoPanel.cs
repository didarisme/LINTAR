using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoPanel : MonoBehaviour
{
    [System.Serializable]
    public class InfoSection
    {
        public Button       navButton;   // NavButton_Overview и тд
        public GameObject   content;     // Section_Overview и тд
    }

    [Header("Sections")]
    [SerializeField] private InfoSection[] sections;

    [Header("Colors")]
    [SerializeField] private Color activeColor   = new Color(0.91f, 0.79f, 0.48f, 1f); // акцент
    [SerializeField] private Color inactiveColor = new Color(0.35f, 0.35f, 0.42f, 1f); // серый

    private void Start()
    {
        // Подписываем каждую кнопку
        for (int i = 0; i < sections.Length; i++)
        {
            int index = i; // локальная копия для лямбды
            sections[i].navButton.onClick.AddListener(() => ShowSection(index));
        }

        // По умолчанию открываем первую секцию
        ShowSection(0);
    }

    private void ShowSection(int index)
    {
        for (int i = 0; i < sections.Length; i++)
        {
            bool isActive = i == index;

            // Показываем / скрываем контент
            sections[i].content.SetActive(isActive);

            // Красим текст кнопки
            var tmp = sections[i].navButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.color = isActive ? activeColor : inactiveColor;

            // Красим фон кнопки
            var img = sections[i].navButton.GetComponent<Image>();
            if (img != null)
                img.color = isActive
                    ? new Color(0.91f, 0.79f, 0.48f, 0.07f)
                    : new Color(1f, 1f, 1f, 0f);
        }
    }
}