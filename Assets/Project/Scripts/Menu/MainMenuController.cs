using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private Pageable mainMenuPage;

    [SerializeField] private Pageable chooseSimPage;
    [SerializeField] private Pageable uploadSimPage;
    [SerializeField] private Pageable settingsPage;
    [SerializeField] private Pageable infoPage;

    private Pageable currentPanel;

    private void Start()
    {
        mainMenuPage.Open();
    }

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

        currentPanel?.Close();

        currentPanel = newPage;
        currentPanel?.Open();

        mainMenuPage.Close();
    }

    public void CloseCurrentPage()
    {
        currentPanel?.Close();
        currentPanel = null;

        mainMenuPage.Open();
    }
}