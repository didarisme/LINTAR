using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoPanel : Pageable
{
    [SerializeField] private InfoPage[] infoPages;

    [Header("Colors")]
    [SerializeField] private Color defaultColor = Color.gray;
    [SerializeField] private Color selectedColor = Color.orange;

    private int _currentIndex = -1;

    protected override void Awake()
    {
        base.Awake();

        ConfigureButtons();
    }

    protected override void OnOpen()
    {
        OpenPage(0);
    }

    protected override void OnClose()
    {
        if (_currentIndex != -1)
        {
            infoPages[_currentIndex].page.Close();
        }

        _currentIndex = -1;
    }

    private void ConfigureButtons()
    {   
        for (int i = 0; i < infoPages.Length; i++)
        {
            int index = i;

            infoPages[i].pageButton.onClick.AddListener(() =>
            {
                OpenPage(index);
            });

            SetButtonColor(i, defaultColor);
        }
    }

    private void OpenPage(int index)
    {
        if (index == _currentIndex)
            return;

        if (_currentIndex != -1)
        {
            infoPages[_currentIndex].page.Close();
            SetButtonColor(_currentIndex, defaultColor);
        }

        _currentIndex = index;
        infoPages[index].page.Open();

        SetButtonColor(index, selectedColor);
    }

    private void SetButtonColor(int index, Color color)
    {
        infoPages[index].btnText.color = color;
    }

    [System.Serializable]
    private struct InfoPage
    {
        public Pageable page;
        public Button pageButton;
        public TextMeshProUGUI btnText;
    }
}