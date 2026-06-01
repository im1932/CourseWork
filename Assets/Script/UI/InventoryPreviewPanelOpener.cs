using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class InventoryPreviewPanelOpener : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private UIoptimazed virtualizedInventory;
    [SerializeField] private InventoryGiftPreview inventoryGiftPreview;
    [SerializeField] private RectTransform previewPanel;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject overlay;

    [Header("Animation")]
    [SerializeField] private float hiddenExtraOffset = 32f;
    [SerializeField] private float speed = 900f;
    [SerializeField] private bool hideOnStart = true;
    [SerializeField] private float startupWarmupSeconds = 0.1f;

    private float targetY;
    private bool isOpen;
    private bool isInitialized;
    private bool missingPreviewPanelWarningLogged;
    private Coroutine startupHideCoroutine;

    private float CurrentShownY => 0f;
    private float CurrentHiddenY => GetHiddenY();

    private void Awake()
    {
        ResolveReferences();
        BindCloseButton();
        InitializePanelState();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (virtualizedInventory != null)
            virtualizedInventory.ItemClicked += HandleInventoryItemClicked;
    }

    private void OnDisable()
    {
        if (virtualizedInventory != null)
            virtualizedInventory.ItemClicked -= HandleInventoryItemClicked;

        if (startupHideCoroutine != null)
        {
            StopCoroutine(startupHideCoroutine);
            startupHideCoroutine = null;
        }
    }

    private void Update()
    {
        if (previewPanel == null)
            return;

        Vector2 position = previewPanel.anchoredPosition;
        position.y = Mathf.MoveTowards(position.y, targetY, speed * Time.unscaledDeltaTime);
        previewPanel.anchoredPosition = position;

        if (!isOpen && hideOnStart && Mathf.Abs(position.y - CurrentHiddenY) <= 0.01f && previewPanel.gameObject.activeSelf)
            previewPanel.gameObject.SetActive(false);
    }

    public void Open()
    {
        ResolveReferences();

        if (previewPanel == null)
        {
            LogMissingPreviewPanelWarning();
            return;
        }

        previewPanel.gameObject.SetActive(true);
        isOpen = true;
        targetY = CurrentShownY;

        if (overlay != null)
            overlay.SetActive(true);
    }

    public void Close()
    {
        if (previewPanel == null)
            return;

        isOpen = false;
        targetY = CurrentHiddenY;

        if (overlay != null)
            overlay.SetActive(false);
    }

    public void ShowEntry(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return;

        ResolveReferences();
        Open();

        Canvas.ForceUpdateCanvases();

        if (inventoryGiftPreview != null)
            inventoryGiftPreview.Show(entry);
    }

    private void ResolveReferences()
    {
        if (previewPanel == null && inventoryGiftPreview != null)
            previewPanel = inventoryGiftPreview.transform as RectTransform;

        if (closeButton == null && inventoryGiftPreview != null)
            closeButton = FindByName<Button>(inventoryGiftPreview.transform, "Close");
    }

    private void BindCloseButton()
    {
        if (closeButton == null)
            return;

        closeButton.onClick.RemoveListener(Close);
        closeButton.onClick.AddListener(Close);
    }

    private void InitializePanelState()
    {
        if (isInitialized || previewPanel == null)
            return;

        isInitialized = true;
        isOpen = false;
        targetY = CurrentHiddenY;

        Vector2 position = previewPanel.anchoredPosition;
        position.y = CurrentHiddenY;
        previewPanel.anchoredPosition = position;

        if (hideOnStart)
        {
            previewPanel.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(previewPanel);

            if (startupHideCoroutine != null)
                StopCoroutine(startupHideCoroutine);

            startupHideCoroutine = StartCoroutine(HidePreviewAfterStartupWarmup());
        }

        if (overlay != null)
            overlay.SetActive(false);
    }

    private IEnumerator HidePreviewAfterStartupWarmup()
    {
        yield return null;

        if (startupWarmupSeconds > 0f)
            yield return new WaitForSecondsRealtime(startupWarmupSeconds);

        startupHideCoroutine = null;

        if (previewPanel == null || isOpen || !hideOnStart)
            yield break;

        Vector2 position = previewPanel.anchoredPosition;
        position.y = CurrentHiddenY;
        previewPanel.anchoredPosition = position;
        previewPanel.gameObject.SetActive(false);
    }

    private void HandleInventoryItemClicked(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return;

        ShowEntry(entry);
    }

    private float GetHiddenY()
    {
        if (previewPanel == null)
            return 0f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(previewPanel);
        return -previewPanel.rect.height - Mathf.Max(0f, hiddenExtraOffset);
    }

    private T FindByName<T>(Transform root, string objectName) where T : Component
    {
        if (root == null || string.IsNullOrWhiteSpace(objectName))
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (!string.Equals(transforms[i].name, objectName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            T component = transforms[i].GetComponent<T>();
            if (component != null)
                return component;
        }

        return null;
    }

    private void LogMissingPreviewPanelWarning()
    {
        if (missingPreviewPanelWarningLogged)
            return;

        missingPreviewPanelWarningLogged = true;
        Debug.LogWarning("InventoryPreviewPanelOpener: previewPanel is not assigned.");
    }
}
