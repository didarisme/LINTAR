using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FireSimControlPanel : MonoBehaviour
{
    [SerializeField] private FireSimulation fireSim;

    [Header("Buttons")]
    [SerializeField] private Button reloadBtn;
    [SerializeField] private Button exitBtn;
    [SerializeField] private Button launchBtn;

    [Header("Input fields")]
    [SerializeField] private TMP_InputField speedField;
    [SerializeField] private TMP_InputField lifetimeField;

    private void Start()
    {
        launchBtn.onClick.AddListener(OnLaunchButton);
        reloadBtn.onClick.AddListener(OnReloadButton);
        exitBtn.onClick.AddListener(OnExitButton);

        speedField.onSubmit.AddListener(SetSpeed);
        lifetimeField.onSubmit.AddListener(SetLifeTime);

        UpdatePlaceHolder(speedField.placeholder as TMP_Text, fireSim.FireSpeed.ToString("F1"));
        UpdatePlaceHolder(lifetimeField.placeholder as TMP_Text, fireSim.Lifetime.ToString());
    }

    private void SetSpeed(string textValue)
    {
        if (float.TryParse(textValue, out float speed))
        {
            speed = fireSim.SetSpeed(speed);
            UpdatePlaceHolder(speedField.placeholder as TMP_Text, speed.ToString());

            speedField.text = "";
        }
    }

    private void SetLifeTime(string textValue)
    {
        if (float.TryParse(textValue, out float lifeTime))
        {
            lifeTime = fireSim.SetLifetime(lifeTime);
            UpdatePlaceHolder(lifetimeField.placeholder as TMP_Text, lifeTime.ToString());

            lifetimeField.text = "";
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