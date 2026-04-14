using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FireSimControlPanel : MonoBehaviour
{
    [SerializeField] private FireSimulation fireSim;
    [SerializeField] private BurntZoneRenderer burntRenderer;

    [Header("Buttons")]
    [SerializeField] private Button reloadBtn;
    [SerializeField] private Button exitBtn;
    [SerializeField] private Button launchBtn;

    [Header("Input fields")]
    [SerializeField] private TMP_InputField speedField;
    [SerializeField] private TMP_InputField lifetimeField;
    [SerializeField] private TMP_InputField offsetField;
    [SerializeField] private TMP_InputField burntSizeField;
    [SerializeField] private TMP_InputField burntOffsetField;

    private void Start()
    {
        launchBtn.onClick.AddListener(OnLaunchButton);
        reloadBtn.onClick.AddListener(OnReloadButton);
        exitBtn.onClick.AddListener(OnExitButton);

        speedField.onSubmit.AddListener(SetSpeed);
        lifetimeField.onSubmit.AddListener(SetLifeTime);
        offsetField.onSubmit.AddListener(SetOffset);

        burntSizeField.onSubmit.AddListener(SetBurntSize);
        burntOffsetField.onSubmit.AddListener(SetBurntOffset);

        UpdatePlaceHolder(speedField.placeholder as TMP_Text, fireSim.FireSpeed.ToString("F1") + " m/s");
        UpdatePlaceHolder(lifetimeField.placeholder as TMP_Text, fireSim.Lifetime.ToString("F1") + " s");
        UpdatePlaceHolder(offsetField.placeholder as TMP_Text, fireSim.YOffset.ToString("F1") + " m");
        UpdatePlaceHolder(burntSizeField.placeholder as TMP_Text, burntRenderer.MeshSize.ToString("F1") + " m");
        UpdatePlaceHolder(burntOffsetField.placeholder as TMP_Text, burntRenderer.YOffset.ToString("F1") + " m");
    }

    private void SetSpeed(string textValue)
    {
        if (float.TryParse(textValue, out float speed))
        {
            speed = fireSim.SetSpeed(speed);
            UpdatePlaceHolder(speedField.placeholder as TMP_Text, speed.ToString("F1") + " m/s" );

            speedField.text = "";
        }
    }

    private void SetLifeTime(string textValue)
    {
        if (float.TryParse(textValue, out float lifeTime))
        {
            lifeTime = fireSim.SetLifetime(lifeTime);
            UpdatePlaceHolder(lifetimeField.placeholder as TMP_Text, lifeTime.ToString("F1") + " s");

            lifetimeField.text = "";
        }
    }

    private void SetOffset(string textValue)
    {
        if (float.TryParse(textValue, out float offset))
        {
            offset = fireSim.SetYOffset(offset);
            UpdatePlaceHolder(offsetField.placeholder as TMP_Text, offset.ToString("F1") + " m");

            offsetField.text = "";
        }
    }

    private void SetBurntSize(string textValue)
    {
        if (float.TryParse(textValue, out float size))
        {
            burntRenderer.SetSize(size);
            UpdatePlaceHolder(burntSizeField.placeholder as TMP_Text, size.ToString("F1"));

            burntSizeField.text = "";
        }
    }

    private void SetBurntOffset(string textValue)
    {
        if (float.TryParse(textValue, out float offset))
        {
            burntRenderer.SetYOffset(offset);
            UpdatePlaceHolder(burntOffsetField.placeholder as TMP_Text, offset.ToString("F2") + " m");

            burntOffsetField.text = "";
        }
    }

    private void UpdatePlaceHolder(TMP_Text placeholder, string textValue)
    {
        placeholder.text = textValue;
    }

    private void OnLaunchButton()
    {
        fireSim.StartSimulation();
        launchBtn.interactable = false;
    }

    private void OnReloadButton()
    {
        reloadBtn.interactable = false;
        exitBtn.interactable = false;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnExitButton()
    {
        reloadBtn.interactable = false;
        exitBtn.interactable = false;

        SceneManager.LoadScene(0);
    }
}