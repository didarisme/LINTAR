using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UploadSimulation : Pageable
{
    [Header("Buttons")]
    [SerializeField] private Button uploadPanelBtn;
    [SerializeField] private Button uploadAnotherBtn;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button btnGoToSims;

    [Space]
    [SerializeField] private GameObject postUploadButtons;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI fileNameText;

    [Header("Colors")]
    [SerializeField] private Color defaultColor = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private Color selectedColor = new Color(0.4f, 0.6f, 1f);
    [SerializeField] private Color successColor = new Color(0.3f, 0.8f, 0.4f);
    [SerializeField] private Color warningColor = new Color(0.9f, 0.7f, 0.2f);

    private string _targetFolder;
    private string[] _pendingFilePaths;

    private bool _initialized = false;

    protected override void Awake()
    {
        base.Awake();

        Initialize();
        ResetUI();
    }

    protected override void OnOpen()
    {
    }

    protected override void OnClose()
    {
        _pendingFilePaths = null;
        ResetUI();
    }

    private void Initialize()
    {
        if (_initialized) return;

        // Android writable folder
        _targetFolder = Path.Combine(Application.persistentDataPath, "FireScenarios");

        if (!Directory.Exists(_targetFolder))
            Directory.CreateDirectory(_targetFolder);

        uploadPanelBtn.onClick.AddListener(OpenFileDialog);
        continueButton.onClick.AddListener(OnContinuePressed);
        uploadAnotherBtn.onClick.AddListener(OnUploadAnother);
        btnGoToSims.onClick.AddListener(OnGoToSims);

        RequestStoragePermission();

        _initialized = true;
    }

    private void RequestStoragePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.ExternalStorageRead))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.ExternalStorageRead);
        }

#endif
    }

    private void ResetUI()
    {
        continueButton.gameObject.SetActive(true);
        continueButton.interactable = false;

        postUploadButtons.SetActive(false);

        SetDefault();
    }

    private void OpenFileDialog()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        NativeFilePicker.PickMultipleFiles((paths) =>
        {
            if (paths == null || paths.Length == 0)
                return;

            _pendingFilePaths = paths;

            string label = paths.Length == 1
                ? Path.GetFileName(paths[0])
                : $"{paths.Length} files selected";

            SetStatus("Selected:", label, selectedColor);

            continueButton.interactable = true;

        }, new string[] { "text/plain" });

#endif
    }

    private void OnContinuePressed()
    {
        if (_pendingFilePaths == null || _pendingFilePaths.Length == 0)
            return;

        int uploaded = 0;
        int skipped = 0;

        foreach (string sourcePath in _pendingFilePaths)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath))
                    continue;

                string fileName = Path.GetFileName(sourcePath);
                string destinationPath = Path.Combine(_targetFolder, fileName);

                // skip duplicates
                if (File.Exists(destinationPath))
                {
                    skipped++;
                    continue;
                }

                File.Copy(sourcePath, destinationPath);

                uploaded++;

                Debug.Log($"Copied: {destinationPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Copy failed: {e.Message}");
            }
        }

        _pendingFilePaths = null;

        if (uploaded == 0 && skipped > 0)
        {
            SetStatus(
                "All files already exist:",
                $"{skipped} skipped",
                warningColor
            );
        }
        else if (skipped > 0)
        {
            SetStatus(
                "Uploaded:",
                $"{uploaded} files ({skipped} skipped)",
                successColor
            );
        }
        else
        {
            SetStatus(
                "Uploaded:",
                $"{uploaded} file(s)",
                successColor
            );
        }

        continueButton.gameObject.SetActive(false);
        postUploadButtons.SetActive(true);
    }

    private void OnUploadAnother()
    {
        ResetUI();
    }

    private void OnGoToSims()
    {
        Debug.Log("Go to simulations page");
    }

    private void SetDefault()
    {
        if (labelText != null)
        {
            labelText.text = "Click to select simulation file";
            labelText.color = defaultColor;
        }

        if (fileNameText != null)
        {
            fileNameText.text = "Supported format: .txt";
            fileNameText.color = defaultColor;
        }
    }

    private void SetStatus(string label, string file, Color color)
    {
        if (labelText != null)
        {
            labelText.text = label;
            labelText.color = color;
        }

        if (fileNameText != null)
        {
            fileNameText.text = file;
            fileNameText.color = color;
        }
    }
}