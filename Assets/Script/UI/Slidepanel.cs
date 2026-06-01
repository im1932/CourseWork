using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class BottomSheet : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private string panelName = "UpPanel";
    [SerializeField] private Button[] openButtons;
    [SerializeField] private GameObject overlay;
    [SerializeField] private Button overlayButton;
    [SerializeField] private PanelQueueManager panelQueueManager;

    [Header("Motion")]
    [SerializeField] private float hiddenExtraOffset = 32f;
    [SerializeField] private float speed = 900f;
    [SerializeField] private float closeThreshold = 120f;

    private static readonly bool PrewarmPanelOnStart = true;
    private const float PrewarmDelay = 0.35f;

    private RectTransform panel;
    private float targetY;
    private bool open;
    private bool recyclePending;
    private bool prewarmCompleted;
    private Coroutine prewarmCoroutine;
    private Coroutine openCoroutine;

    private float dragStartPointerY;
    private float dragStartPanelY;
    private int cachedPanelId;

    private float HiddenY
    {
        get
        {
            if (panel == null)
                return 0f;

            Canvas.ForceUpdateCanvases();
            return -panel.rect.height - Mathf.Max(0f, hiddenExtraOffset);
        }
    }

    private void Awake()
    {
        RefreshPanel(true);
        ResolvePanelQueueManager();
        BindOpenButtons();

        if (overlayButton != null)
        {
            overlayButton.onClick.RemoveListener(Close);
            overlayButton.onClick.AddListener(Close);
        }

        if (overlay != null)
            overlay.SetActive(false);

        if (panel != null && panel.gameObject.activeSelf)
            ForceClosedState();
    }

    private void Start()
    {
        if (!PrewarmPanelOnStart)
            return;

        prewarmCoroutine = StartCoroutine(PrewarmPanelRoutine());
    }

    private void Update()
    {
        RefreshPanel(false);

        if (panel == null)
            return;

        Vector2 position = panel.anchoredPosition;
        position.y = Mathf.MoveTowards(position.y, targetY, speed * Time.unscaledDeltaTime);
        panel.anchoredPosition = position;

        if (recyclePending && !open && Mathf.Abs(panel.anchoredPosition.y - HiddenY) <= 0.01f)
            RecycleClosedPanel();
    }

    private void BindOpenButtons()
    {
        if (openButtons == null)
            return;

        for (int i = 0; i < openButtons.Length; i++)
        {
            Button button = openButtons[i];
            if (button == null)
                continue;

            button.onClick.RemoveListener(Open);
            button.onClick.AddListener(Open);
        }
    }

    private void ResolvePanelQueueManager()
    {
        if (panelQueueManager == null)
            panelQueueManager = FindObjectOfType<PanelQueueManager>(true);
    }

    private void RecycleClosedPanel()
    {
        recyclePending = false;

        if (panelQueueManager != null)
            panelQueueManager.ReplaceActivePanelWithInactiveCopy();

        RefreshPanel(true);
    }

    private void RefreshPanel(bool snapToState)
    {
        RectTransform found = FindBestPanelByName(panelName);
        if (found == null)
            return;

        bool panelChanged = panel != found || cachedPanelId != found.GetInstanceID();
        if (!panelChanged)
            return;

        panel = found;
        cachedPanelId = found.GetInstanceID();
        RestorePanelVisibility(panel);

        Vector2 position = panel.anchoredPosition;
        float hiddenY = HiddenY;

        if (snapToState || open)
        {
            panel.gameObject.SetActive(true);
            position.y = open ? 0f : hiddenY;
            panel.anchoredPosition = position;
            targetY = position.y;
        }
        else
        {
            if (!open && position.y > 0f)
            {
                position.y = hiddenY;
                panel.anchoredPosition = position;
            }

            targetY = open ? 0f : hiddenY;
        }
    }

    private RectTransform FindBestPanelByName(string targetName)
    {
        RectTransform[] rects = FindObjectsOfType<RectTransform>(true);
        string normalizedTarget = NormalizeName(targetName);

        RectTransform bestActive = null;
        RectTransform bestInactive = null;
        int bestActiveSibling = int.MinValue;
        int bestInactiveSibling = int.MinValue;

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform candidate = rects[i];
            if (NormalizeName(candidate.name) != normalizedTarget)
                continue;

            int sibling = candidate.GetSiblingIndex();

            if (candidate.gameObject.activeInHierarchy)
            {
                if (sibling >= bestActiveSibling)
                {
                    bestActiveSibling = sibling;
                    bestActive = candidate;
                }
            }
            else if (sibling >= bestInactiveSibling)
            {
                bestInactiveSibling = sibling;
                bestInactive = candidate;
            }
        }

        return bestActive != null ? bestActive : bestInactive;
    }

    private string NormalizeName(string objectName)
    {
        string result = objectName.Replace("(Clone)", "").Trim();

        if (result.EndsWith(")"))
        {
            int bracketIndex = result.LastIndexOf('(');
            if (bracketIndex > 0)
            {
                string inside = result.Substring(bracketIndex + 1, result.Length - bracketIndex - 2).Trim();
                bool digits = inside.Length > 0;

                for (int i = 0; i < inside.Length; i++)
                {
                    if (!char.IsDigit(inside[i]))
                    {
                        digits = false;
                        break;
                    }
                }

                if (digits)
                    result = result.Substring(0, bracketIndex).Trim();
            }
        }

        int lastSpace = result.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            string tail = result.Substring(lastSpace + 1).Trim();
            bool digits = tail.Length > 0;

            for (int i = 0; i < tail.Length; i++)
            {
                if (!char.IsDigit(tail[i]))
                {
                    digits = false;
                    break;
                }
            }

            if (digits)
                result = result.Substring(0, lastSpace).Trim();
        }

        return result;
    }

    public void Open()
    {
        if (openCoroutine != null)
        {
            StopCoroutine(openCoroutine);
            openCoroutine = null;
        }

        if (prewarmCoroutine != null)
        {
            StopCoroutine(prewarmCoroutine);
            prewarmCoroutine = null;
        }

        RefreshPanel(false);
        if (panel != null && panel.gameObject.activeSelf)
        {
            recyclePending = false;
            open = true;
            targetY = 0f;

            if (overlay != null)
                overlay.SetActive(true);

            return;
        }

        openCoroutine = StartCoroutine(OpenRoutine());
    }

    public void Close()
    {
        RefreshPanel(false);
        if (panel == null)
            return;

        open = false;
        targetY = HiddenY;
        recyclePending = true;

        if (overlay != null)
            overlay.SetActive(false);
    }

    public void Toggle()
    {
        if (open)
            Close();
        else
            Open();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        RefreshPanel(false);
        if (panel == null)
            return;

        dragStartPointerY = eventData.position.y;
        dragStartPanelY = panel.anchoredPosition.y;
    }

    public void OnDrag(PointerEventData eventData)
    {
        RefreshPanel(false);
        if (panel == null)
            return;

        float deltaY = eventData.position.y - dragStartPointerY;
        float y = dragStartPanelY + deltaY;
        y = Mathf.Clamp(y, HiddenY, 0f);

        Vector2 position = panel.anchoredPosition;
        position.y = y;
        panel.anchoredPosition = position;
        targetY = y;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        RefreshPanel(false);
        if (panel == null)
            return;

        float currentY = panel.anchoredPosition.y;
        float thresholdY = HiddenY + Mathf.Min(closeThreshold, Mathf.Abs(HiddenY) * 0.5f);

        if (currentY <= thresholdY)
            Close();
        else
            Open();
    }

    private IEnumerator PrewarmPanelRoutine()
    {
        if (prewarmCompleted)
            yield break;

        if (PrewarmDelay > 0f)
            yield return new WaitForSecondsRealtime(PrewarmDelay);

        RefreshPanel(true);
        if (panel == null || open)
            yield break;

        GameObject panelObject = panel.gameObject;
        if (panelObject == null || panelObject.activeSelf)
        {
            prewarmCompleted = true;
            yield break;
        }

        panelObject.SetActive(true);
        CanvasGroup canvasGroup = EnsureCanvasGroup(panelObject);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        yield return null;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        SnapPanelToHidden();
        targetY = HiddenY;

        if (!open)
            panelObject.SetActive(false);

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        prewarmCompleted = true;
        prewarmCoroutine = null;
    }

    private IEnumerator OpenRoutine()
    {
        RefreshPanel(false);
        if (panel == null)
            yield break;

        recyclePending = false;

        GameObject panelObject = panel.gameObject;
        CanvasGroup canvasGroup = EnsureCanvasGroup(panelObject);

        panelObject.SetActive(true);
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        yield return null;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        SnapPanelToHidden();

        open = true;
        targetY = 0f;

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        if (overlay != null)
            overlay.SetActive(true);

        openCoroutine = null;
    }

    private void ForceClosedState()
    {
        if (panel == null)
            return;

        open = false;
        recyclePending = false;
        SnapPanelToHidden();
        targetY = HiddenY;
    }

    private void SnapPanelToHidden()
    {
        if (panel == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        Vector2 position = panel.anchoredPosition;
        position.y = HiddenY;
        panel.anchoredPosition = position;
    }

    private CanvasGroup EnsureCanvasGroup(GameObject target)
    {
        CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = target.AddComponent<CanvasGroup>();

        return canvasGroup;
    }

    private void RestorePanelVisibility(RectTransform targetPanel)
    {
        if (targetPanel == null)
            return;

        CanvasGroup canvasGroup = EnsureCanvasGroup(targetPanel.gameObject);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
    }
}