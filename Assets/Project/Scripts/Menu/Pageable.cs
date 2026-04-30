using UnityEngine;

public abstract class Pageable : MonoBehaviour
{
    [Header("Pageable features")]
    [SerializeField] private CanvasGroup pageGroup;
    [SerializeField] private float transitionDuration = 1f;
    [SerializeField] private bool animatePage = true;

    protected virtual void Awake()
    {
        if (animatePage)
        {
            if (pageGroup == null)
            {
                Debug.LogError("No Canvas Group");
            }
            else
            {
                pageGroup.interactable = false;
                pageGroup.blocksRaycasts = false;
                pageGroup.alpha = 0f;
            }
        }
    }

    protected abstract void OnOpen();
    protected abstract void OnClose();

    public void Open()
    {
        if (animatePage)
            PageTransitioner.Instance.AnimateOpen(pageGroup, transitionDuration, OnOpen);
        else
            OnOpen();
    }

    public void Close()
    {
        if (animatePage)
            PageTransitioner.Instance.AnimateClose(pageGroup, transitionDuration / 2f, OnClose);
        else
            OnClose();
    }
}