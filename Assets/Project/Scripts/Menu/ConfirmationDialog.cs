using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ConfirmationDialog : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI _messageText;
    [SerializeField] private Button _btnYes;
    [SerializeField] private Button _btnNo;

    private Action _onConfirm;

    private void Awake()
    {
        _btnYes.onClick.AddListener(OnYes);
        _btnNo.onClick.AddListener(OnNo);
    }

    public void Show(string simulationName, Action onConfirm)
    {
        _messageText.text = $"Are you sure you want to delete\n\"{simulationName}\"?";
        _onConfirm = onConfirm;
        gameObject.SetActive(true);
    }

    private void OnYes()
    {
        gameObject.SetActive(false);
        _onConfirm?.Invoke();
        _onConfirm = null;
    }

    private void OnNo()
    {
        gameObject.SetActive(false);
        _onConfirm = null;
    }
}