using UnityEngine;

public class InfoPanel : Pageable
{
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private GameObject[] pages;

    private int activePage = 0;

    public override void OnOpen()
    {
        OpenPage(0);
        infoPanel.SetActive(true);
    }

    public override void OnClose()
    {
        infoPanel.SetActive(false);
    }

    public void OpenPage(int pageIndex)
    {
        if (pages == null || pages.Length == 0) return;

        pages[activePage].SetActive(false);

        activePage = pageIndex;
        pages[activePage].SetActive(true);
    }
}