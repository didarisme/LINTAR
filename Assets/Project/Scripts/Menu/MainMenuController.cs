using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenuPanel;

    [SerializeField] private Pageable chooseSimPage;
    [SerializeField] private Pageable uploadSimPage;
    [SerializeField] private Pageable settingsPage;
    [SerializeField] private Pageable infoPage;

    private Pageable currentPanel;

    public void OpenChooseSim()
    {
        OpenPage(chooseSimPage);
    }

    public void OpenUploadSim()
    {
        OpenPage(uploadSimPage);
    }

    public void OpenSettings()
    {
        OpenPage(settingsPage);
    }

    public void OpenInfo()
    {
        OpenPage(infoPage);
    }

    public void OnExitBtn()
    {
        Application.Quit();
    }

    private void OpenPage(Pageable newPage)
    {
        if (newPage == currentPanel) return;

        currentPanel?.OnClose();

        currentPanel = newPage;
        currentPanel?.OnOpen();

        SetMainMenuVisible(false);
    }

    public void CloseCurrentPage()
    {
        currentPanel?.OnClose();
        currentPanel = null;

        SetMainMenuVisible(true);
    }

    private void SetMainMenuVisible(bool visible)
    {
        mainMenuPanel.SetActive(visible);
    }
}