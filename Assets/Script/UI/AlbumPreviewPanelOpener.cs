using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class AlbumPreviewPanelOpener : MonoBehaviour
{
    private static AlbumPreviewPanelOpener instance;
    private const string SelectedVariantsPrefsKey = "AlbumPreviewPanelOpener.SelectedVariants";
    private static readonly Dictionary<string, List<GiftCatalogDatabase.GiftItemRecord>> cachedCollectionItemsByKey =
        new Dictionary<string, List<GiftCatalogDatabase.GiftItemRecord>>(System.StringComparer.OrdinalIgnoreCase);

    [Header("Panel")]
    [SerializeField] private RectTransform previewPanel;
    [SerializeField] private Button closeButton;
    [SerializeField] private Transform collectionContent;
    [SerializeField] private GameObject collectionItemPrefab;
    [SerializeField] private RectTransform collectionViewport;
    [SerializeField] private GameObject collectedCollectionItemPrefab;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Image collectionProgressFillImage;
    [SerializeField] private TMP_Text collectionProgressPercentText;

    [Header("Owned Variants")]
    [SerializeField] private RectTransform ownedItemsPanel;
    [SerializeField] private Button ownedItemsCloseButton;
    [SerializeField] private Transform ownedItemsContent;
    [SerializeField] private GameObject ownedItemVariantPrefab;
    private static readonly bool HideOwnedItemsPanelOnStart = true;

    [Header("Owned Variants Animation")]
    [SerializeField] private float ownedItemsHiddenExtraOffset = 32f;
    [SerializeField] private float ownedItemsSpeed = 3500f;
    [SerializeField] private float ownedItemsCloseHideDelay = 0.15f;

    [Header("Collection Layout")]
    [SerializeField] private Vector2 collectionCellSize = new Vector2(160f, 220f);
    [SerializeField] private Vector2 collectionSpacing = new Vector2(10f, 10f);
    [SerializeField] private float collectionTopPadding = 0f;
    [SerializeField] private float collectionBottomPadding = 0f;
    [SerializeField] private float collectionBottomOverlayPadding = 160f;

    [Header("Collection Lazy Loading")]
    private static readonly bool LazyLoadCollectionItems = true;
    [SerializeField] private int initialCollectionItemCount = 30;
    private static readonly bool HideCollectionContentWhileRebuilding = true;

    [Header("Collection Opening")]
    private static readonly bool DeferPanelOpenUntilContentReady = true;
    private static readonly bool PreferSmoothScrollOverVirtualization = true;
    [SerializeField] private int collectionOpenBatchSize = 12;
    [SerializeField] private GameObject loadingScreen;

    [Header("Animation")]
    [SerializeField] private float hiddenExtraOffset = 32f;
    [SerializeField] private float speed = 3500f;
    [SerializeField] private float closeHideDelay = 2f;
    private static readonly bool HideOnStart = true;
    private static readonly bool PrewarmPanelOnStart = true;
    [SerializeField] private float prewarmDelay = 0.35f;

    private float targetY;
    private float hideAfterTime = -1f;
    private float ownedItemsTargetY;
    private float ownedItemsHideAfterTime = -1f;
    private bool isOpen;
    private bool isOwnedItemsOpen;
    private RectTransform activePanel;
    private RectTransform closingPanel;
    private RectTransform closingOwnedItemsPanel;
    private Button resolvedCloseButton;
    private bool prewarmCompleted;
    private Coroutine prewarmCoroutine;
    private Coroutine closeGuardCoroutine;
    private Coroutine ownedItemsCloseGuardCoroutine;
    private Coroutine openCollectionViewsCoroutine;
    private readonly HashSet<int> initializedPanelIds = new HashSet<int>();
    private readonly List<GiftCatalogDatabase.GiftItemRecord> currentCollectionItems = new List<GiftCatalogDatabase.GiftItemRecord>();
    private readonly List<GameObject> spawnedCollectionViews = new List<GameObject>();
    private readonly List<GameObject> spawnedOwnedItemViews = new List<GameObject>();
    private readonly List<InventoryManager.InventoryEntry> cachedOwnedCollectionEntries = new List<InventoryManager.InventoryEntry>();
    private readonly List<VirtualizedCollectionSlot> virtualizedCollectionSlots = new List<VirtualizedCollectionSlot>();
    private readonly Dictionary<int, CollectionItemViewRefs> collectionItemViewRefCache = new Dictionary<int, CollectionItemViewRefs>();
    private readonly Dictionary<string, List<InventoryManager.InventoryEntry>> cachedOwnedEntriesByModelId =
        new Dictionary<string, List<InventoryManager.InventoryEntry>>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> selectedInventoryNumberByGiftModelKey = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
    private bool pendingOwnedItemsViewClear;
    private string currentGiftId;
    private string currentResolvedCollectionName;
    private ScrollRect collectionScrollRect;
    private ScrollRect ownedItemsScrollRect;
    private bool collectionScrollListenerBound;
    private Vector2? pendingCollectionScrollRestorePosition;
    private CanvasGroup collectionContentCanvasGroup;
    private bool collectionWindowRefreshRequested;
    private int virtualizedCollectionColumnCount = 1;
    private int virtualizedCollectionPoolRowCount;
    private int virtualizedCollectionTotalRows;
    private int virtualizedCollectionTopRow = int.MinValue;
    private float virtualizedCollectionGridWidth;
    private float virtualizedCollectionRowStep = 1f;
    private bool isUsingVirtualizedCollectionViews;
    private InventoryManager subscribedInventoryManager;
    private bool missingOwnedItemsPanelWarningLogged;
    private bool missingOwnedItemsContentWarningLogged;
    private bool invalidOwnedItemsContentWarningLogged;
    private bool missingCollectionContentWarningLogged;
    private float CurrentHiddenY => GetHiddenYForPanel(activePanel != null ? activePanel : previewPanel, hiddenExtraOffset);

    private float CurrentOwnedItemsHiddenY => GetHiddenYForPanel(ownedItemsPanel, ownedItemsHiddenExtraOffset);

    [System.Serializable]
    private sealed class SavedVariantSelection
    {
        public string key;
        public int inventoryNumber;
    }

    [System.Serializable]
    private sealed class SavedVariantSelectionCollection
    {
        public List<SavedVariantSelection> items = new List<SavedVariantSelection>();
    }

    private sealed class VirtualizedCollectionSlot
    {
        public GameObject view;
        public RectTransform rectTransform;
        public bool usesCollectedPrefab;
        public int boundIndex = -1;
    }

    private sealed class CollectionItemViewRefs
    {
        public Image modelImage;
        public TMP_Text titleText;
        public Text legacyTitleText;
        public TMP_Text idText;
        public Text legacyIdText;
        public TMP_Text rarityText;
        public Text legacyRarityText;
        public Button button;
        public GameObject rootView;
        public GiftCatalogDatabase.GiftItemRecord boundItem;
        public InventoryManager boundInventoryManager;
        public bool boundIsOwned;
        public bool clickListenerBound;
    }

    private void Awake()
    {
        instance = this;
        LoadSelectedVariantSelections();
        ResolvePanelReferences(previewPanel);
        ResolveOwnedItemsReferences();
        BindCloseButton();
        BindOwnedItemsCloseButton();
        RefreshOverallCollectionProgress();
        InitializePanelState(previewPanel);
        InitializeOwnedItemsPanelState();
    }

    private void Start()
    {
        TryBindInventoryEvents();
        RefreshOverallCollectionProgress();

        if (!PrewarmPanelOnStart)
            return;

        prewarmCoroutine = StartCoroutine(PrewarmPanelRoutine());
    }

    private void Update()
    {
        TryBindInventoryEvents();
        AnimateMainPanel();
        AnimateOwnedItemsPanel();

        if (collectionWindowRefreshRequested)
        {
            collectionWindowRefreshRequested = false;
            UpdateVirtualizedCollectionWindow();
        }
    }

    private void OnDisable()
    {
        UnbindInventoryEvents();
        UnbindCollectionScrollRect();

        if (openCollectionViewsCoroutine != null)
        {
            StopCoroutine(openCollectionViewsCoroutine);
            openCollectionViewsCoroutine = null;
        }
    }

    public static void OpenGiftId(string giftId, GameObject sourceButtonObject, GameObject panelOverride = null)
    {
        if (instance == null)
            return;

        instance.OpenForGiftId(giftId, sourceButtonObject, panelOverride);
    }

    public static void NotifyInventorySaved()
    {
        if (instance == null)
            return;

        instance.HandleInventorySavedDirectly();
    }

    public static void RefreshProgressDisplayOnly()
    {
        RefreshProgressDisplayOnly(null);
    }

    public static void RefreshProgressDisplayOnly(GameObject panelOverride)
    {
        RectTransform overridePanel = panelOverride != null ? panelOverride.GetComponent<RectTransform>() : null;
        if (instance != null)
            instance.RefreshProgressDisplayForPanel(overridePanel);

        AchievementManager manager = AchievementManager.Instance;
        if (manager != null)
            manager.RefreshInventoryAchievementsImmediately();
    }

    public void OpenForGiftId(string giftId, GameObject sourceButtonObject, GameObject panelOverride = null)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return;

        RectTransform overridePanel = panelOverride != null ? panelOverride.GetComponent<RectTransform>() : null;
        activePanel = overridePanel != null ? overridePanel : previewPanel;
        if (activePanel == null)
            return;

        currentGiftId = giftId;
        currentResolvedCollectionName = GiftCatalogDatabase.ResolveCollectionName(giftId);
        ResolvePanelReferences(activePanel);
        ResolveOwnedItemsReferences();
        BindCloseButton();
        BindOwnedItemsCloseButton();
        InitializePanelState(activePanel);
        CloseOwnedItemsPanel();

        currentCollectionItems.Clear();
        if (TryGetCollectionItems(giftId, currentResolvedCollectionName, out List<GiftCatalogDatabase.GiftItemRecord> loadedItems))
            currentCollectionItems.AddRange(loadedItems);

        string displayName = ResolveCollectionDisplayName(giftId, sourceButtonObject);
        SetText(titleText, displayName);
        RefreshOverallCollectionProgress();

        if (openCollectionViewsCoroutine != null)
            StopCoroutine(openCollectionViewsCoroutine);

        if (DeferPanelOpenUntilContentReady)
        {
            PreparePanelForDeferredOpen();
            ShowLoadingOverlay(true);
            openCollectionViewsCoroutine = StartCoroutine(PrepareAndOpenCollectionViewsRoutine());
        }
        else
        {
            OpenCurrentPanel();
            openCollectionViewsCoroutine = StartCoroutine(RebuildCollectionViewsNextFrame());
        }
    }

    private void TryBindInventoryEvents()
    {
        InventoryManager currentInventoryManager = InventoryManager.Instance;
        if (ReferenceEquals(subscribedInventoryManager, currentInventoryManager))
            return;

        UnbindInventoryEvents();

        if (currentInventoryManager == null)
            return;

        subscribedInventoryManager = currentInventoryManager;
        subscribedInventoryManager.InventoryChanged += HandleInventoryChanged;
        RefreshOverallCollectionProgress();
    }

    private void UnbindInventoryEvents()
    {
        if (subscribedInventoryManager == null)
            return;

        subscribedInventoryManager.InventoryChanged -= HandleInventoryChanged;
        subscribedInventoryManager = null;
    }

    private void HandleInventoryChanged()
    {
        RefreshOverallCollectionProgress();
    }

    private void HandleInventorySavedDirectly()
    {
        RefreshOverallCollectionProgress();

        if (!isOpen || currentCollectionItems.Count == 0)
            return;

        Vector2 preservedScrollPosition = GetCollectionScrollPosition();
        pendingCollectionScrollRestorePosition = preservedScrollPosition;
        RebuildCollectionViews(true);
        if (!ShouldUseLazyCollectionLoading())
            RestoreCollectionScrollPosition(preservedScrollPosition);
    }

    private void RefreshOverallCollectionProgress()
    {
        AlbumCollectionProgressStore.GetOverallProgress(out int ownedModelCount, out int totalModelCount);
        UpdateCollectionProgressUI(ownedModelCount, totalModelCount);
    }

    private void RefreshProgressDisplayForPanel(RectTransform panelOverride)
    {
        RectTransform panel = panelOverride != null ? panelOverride : (activePanel != null ? activePanel : previewPanel);
        if (panel != null)
        {
            activePanel = panel;
            ResolvePanelReferences(panel);
            InitializePanelState(panel);
        }

        RefreshOverallCollectionProgress();
    }

    public void Close()
    {
        RectTransform panel = activePanel != null ? activePanel : previewPanel;
        if (panel == null)
            return;

        if (!isActiveAndEnabled)
        {
            CloseOwnedItemsPanelImmediate();
            panel.gameObject.SetActive(false);
            closingPanel = null;
            hideAfterTime = -1f;
            isOpen = false;
            targetY = CurrentHiddenY;
            return;
        }

        closingPanel = panel;
        closingPanel.gameObject.SetActive(true);
        isOpen = false;
        targetY = CurrentHiddenY;
        hideAfterTime = Time.unscaledTime + Mathf.Max(0f, closeHideDelay);
        CloseOwnedItemsPanel();

        if (closeGuardCoroutine != null)
            StopCoroutine(closeGuardCoroutine);

        closeGuardCoroutine = StartCoroutine(KeepClosingPanelActive(panel));

    }

    private void OpenCurrentPanel()
    {
        RectTransform panel = activePanel != null ? activePanel : previewPanel;
        if (panel == null)
            return;

        if (prewarmCoroutine != null)
        {
            StopCoroutine(prewarmCoroutine);
            prewarmCoroutine = null;
        }

        closingPanel = null;
        hideAfterTime = -1f;

        if (closeGuardCoroutine != null)
        {
            StopCoroutine(closeGuardCoroutine);
            closeGuardCoroutine = null;
        }

        panel.gameObject.SetActive(true);
        isOpen = true;
        targetY = 0f;
        ShowLoadingOverlay(false);
    }

    private void PreparePanelForDeferredOpen()
    {
        RectTransform panel = activePanel != null ? activePanel : previewPanel;
        if (panel == null)
            return;

        if (prewarmCoroutine != null)
        {
            StopCoroutine(prewarmCoroutine);
            prewarmCoroutine = null;
        }

        closingPanel = null;
        hideAfterTime = -1f;

        if (closeGuardCoroutine != null)
        {
            StopCoroutine(closeGuardCoroutine);
            closeGuardCoroutine = null;
        }

        panel.gameObject.SetActive(true);
        isOpen = false;
        targetY = CurrentHiddenY;
        Vector2 position = panel.anchoredPosition;
        position.y = CurrentHiddenY;
        panel.anchoredPosition = position;
    }

    private void BindCloseButton()
    {
        Button button = closeButton != null ? closeButton : resolvedCloseButton;
        if (button == null)
            return;

        resolvedCloseButton = button;
        button.onClick.RemoveListener(Close);
        button.onClick.AddListener(Close);
    }

    private void InitializePanelState(RectTransform panel)
    {
        if (panel == null)
            return;

        int panelId = panel.GetInstanceID();
        if (initializedPanelIds.Contains(panelId))
            return;

        initializedPanelIds.Add(panelId);
        isOpen = false;
        targetY = GetHiddenYForPanel(panel, hiddenExtraOffset);

        Vector2 position = panel.anchoredPosition;
        position.y = GetHiddenYForPanel(panel, hiddenExtraOffset);
        panel.anchoredPosition = position;

        if (HideOnStart)
            panel.gameObject.SetActive(false);

    }

    private void ResolvePanelReferences(RectTransform panel)
    {
        if (previewPanel == null)
            previewPanel = panel;

        resolvedCloseButton = closeButton;

        if (collectionContent != null)
            collectionContent = ResolveBestCollectionContentRoot(collectionContent);
    }

    private void ResolveOwnedItemsReferences()
    {
        if (ownedItemsContent != null)
            ownedItemsScrollRect = ownedItemsContent.GetComponentInParent<ScrollRect>(true);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private static void SetText(Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private static string FormatRarityLabel(int rarityPermille)
    {
        if (rarityPermille <= 0)
            return string.Empty;

        float percentage = rarityPermille / 10f;
        return percentage.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private static Sprite FindBestImage(GameObject rootObject)
    {
        if (rootObject == null)
            return null;

        Image[] images = rootObject.GetComponentsInChildren<Image>(true);
        Image best = null;
        float bestArea = -1f;

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.sprite == null)
                continue;

            RectTransform rect = image.rectTransform;
            float area = Mathf.Abs(rect.rect.width * rect.rect.height);
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = image;
        }

        return best != null ? best.sprite : null;
    }

    private static string ResolveDisplayName(GameObject rootObject, string fallbackGiftId)
    {
        if (rootObject != null)
        {
            if (!string.IsNullOrWhiteSpace(rootObject.name))
            {
                string cleanedName = CleanDisplayName(rootObject.name);
                if (!IsGenericUiName(cleanedName))
                    return cleanedName;
            }

            Transform parent = rootObject.transform != null ? rootObject.transform.parent : null;
            while (parent != null)
            {
                string parentName = CleanDisplayName(parent.name);
                if (!IsGenericUiName(parentName))
                    return parentName;

                parent = parent.parent;
            }

            TMP_Text tmp = rootObject.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text) && !IsGenericUiName(tmp.text))
                return tmp.text.Trim();

            Text legacy = rootObject.GetComponentInChildren<Text>(true);
            if (legacy != null && !string.IsNullOrWhiteSpace(legacy.text) && !IsGenericUiName(legacy.text))
                return legacy.text.Trim();
        }

        return FormatGiftIdForDisplay(fallbackGiftId);
    }

    private string ResolveCollectionDisplayName(string giftId, GameObject sourceButtonObject)
    {
        string preferredId = !string.IsNullOrWhiteSpace(currentResolvedCollectionName)
            ? currentResolvedCollectionName
            : giftId;

        string displayName = FormatGiftIdForDisplay(preferredId);
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        return ResolveDisplayName(sourceButtonObject, giftId);
    }

    private static string CleanDisplayName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("(Clone)", "").Trim();
    }

    private static bool IsGenericUiName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string trimmed = value.Trim();
        return string.Equals(trimmed, "View", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "Button", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "Image", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "Text", System.StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatGiftIdForDisplay(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return string.Empty;

        string source = giftId.Replace("(Clone)", "").Replace('_', ' ').Trim();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(source.Length + 8);

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];

            if (i > 0)
            {
                char previous = source[i - 1];
                bool addSpaceBeforeUpper = char.IsUpper(current) && !char.IsWhiteSpace(previous) && !char.IsUpper(previous);
                bool addSpaceBetweenDigitAndLetter = char.IsDigit(previous) && char.IsLetter(current);
                bool addSpaceBetweenLetterAndDigit = char.IsLetter(previous) && char.IsDigit(current);

                if ((addSpaceBeforeUpper || addSpaceBetweenDigitAndLetter || addSpaceBetweenLetterAndDigit) &&
                    builder.Length > 0 &&
                    builder[builder.Length - 1] != ' ')
                {
                    builder.Append(' ');
                }
            }

            builder.Append(current);
        }

        return builder.ToString().Trim();
    }

    private void BindOwnedItemsCloseButton()
    {
        if (ownedItemsCloseButton == null)
            return;

        ownedItemsCloseButton.onClick.RemoveListener(CloseOwnedItemsPanel);
        ownedItemsCloseButton.onClick.AddListener(CloseOwnedItemsPanel);
    }

    private void InitializeOwnedItemsPanelState()
    {
        if (ownedItemsPanel == null)
            return;

        Vector2 position = ownedItemsPanel.anchoredPosition;
        position.y = CurrentOwnedItemsHiddenY;
        ownedItemsPanel.anchoredPosition = position;
        ownedItemsTargetY = CurrentOwnedItemsHiddenY;
        isOwnedItemsOpen = false;
        closingOwnedItemsPanel = null;
        ownedItemsHideAfterTime = -1f;

        if (HideOwnedItemsPanelOnStart)
            ownedItemsPanel.gameObject.SetActive(false);
    }

    private void RebuildCollectionViews(bool preservePendingScrollRestore = false)
    {
        ClearCollectionViews(!preservePendingScrollRestore);
        isUsingVirtualizedCollectionViews = false;

        if (collectionItemPrefab == null && collectedCollectionItemPrefab == null)
            return;

        collectionContent = ResolveBestCollectionContentRoot(collectionContent);

        if (collectionContent == null)
        {
            WarnMissingCollectionContent();
            return;
        }

        DisableCollectionLayoutComponents();
        RectTransform contentRect = collectionContent as RectTransform;
        RectTransform viewportRect = collectionViewport;
        collectionScrollRect = ResolveCollectionScrollRect();
        collectionContentCanvasGroup = ResolveCollectionContentCanvasGroup();

        InventoryManager inventoryManager = InventoryManager.Instance;
        RebuildOwnedCollectionEntryCache(inventoryManager);
        if (ShouldUseLazyCollectionLoading())
        {
            UnbindCollectionScrollRect();
            SetCollectionContentVisible(!HideCollectionContentWhileRebuilding);
            BuildLazyWindowCollectionViews(contentRect, viewportRect, inventoryManager);
            return;
        }

        UnbindCollectionScrollRect();
        SetCollectionContentVisible(true);
        int ownedMatchCount = BuildAllCollectionViews(inventoryManager);

        ApplyCollectionGridLayout(contentRect, viewportRect);
    }

    private bool ShouldUseLazyCollectionLoading()
    {
        if (PreferSmoothScrollOverVirtualization)
            return false;

        return LazyLoadCollectionItems &&
               currentCollectionItems.Count > Mathf.Max(1, initialCollectionItemCount) &&
               collectionScrollRect != null;
    }

    private void BuildLazyWindowCollectionViews(RectTransform contentRect, RectTransform viewportRect, InventoryManager inventoryManager)
    {
        if (contentRect == null || viewportRect == null)
            return;

        DisableCollectionRuntimeLazyLoader();
        ConfigureVirtualizedCollectionLayout(contentRect, viewportRect);
        isUsingVirtualizedCollectionViews = true;
        EnsureVirtualizedCollectionPool();

        if (pendingCollectionScrollRestorePosition.HasValue)
        {
            RestoreCollectionScrollPosition(pendingCollectionScrollRestorePosition.Value);
            pendingCollectionScrollRestorePosition = null;
        }
        else if (collectionScrollRect != null)
        {
            collectionScrollRect.StopMovement();
            collectionScrollRect.verticalNormalizedPosition = 1f;
            RectTransform scrollContentRect = GetCollectionScrollContentRect();
            if (scrollContentRect != null)
                scrollContentRect.anchoredPosition = Vector2.zero;
        }

        SetCollectionContentVisible(true);
        BindCollectionScrollRect();
        UpdateVirtualizedCollectionWindow(force: true);
    }

    private void DisableCollectionRuntimeLazyLoader()
    {
        if (collectionContent == null)
            return;

        LazyLoader lazyLoader = collectionContent.GetComponent<LazyLoader>();
        if (lazyLoader == null)
            return;

        lazyLoader.enabled = false;
    }

    private int BuildAllCollectionViews(InventoryManager inventoryManager)
    {
        if (collectionContent == null)
            return 0;

        return BuildCollectionViewRange(inventoryManager, 0, currentCollectionItems.Count, positionImmediately: false);
    }

    private void BindCollectionScrollRect()
    {
        if (collectionScrollRect == null || collectionScrollListenerBound)
            return;

        collectionScrollRect.onValueChanged.AddListener(OnCollectionScrollChanged);
        collectionScrollListenerBound = true;
    }

    private void UnbindCollectionScrollRect()
    {
        if (collectionScrollRect == null || !collectionScrollListenerBound)
            return;

        collectionScrollRect.onValueChanged.RemoveListener(OnCollectionScrollChanged);
        collectionScrollListenerBound = false;
    }

    private void OnCollectionScrollChanged(Vector2 normalizedPosition)
    {
        collectionWindowRefreshRequested = true;
    }

    private void ClearCollectionViews(bool clearPendingScrollRestorePosition = true)
    {
        if (clearPendingScrollRestorePosition)
            pendingCollectionScrollRestorePosition = null;
        collectionWindowRefreshRequested = false;
        ResetVirtualizedCollectionState();

        for (int i = spawnedCollectionViews.Count - 1; i >= 0; i--)
        {
            GameObject view = spawnedCollectionViews[i];
            if (view != null)
            {
                ClearEditorSelectionIfDestroyedObject(view);
                view.SetActive(false);
                Destroy(view);
            }
        }

        spawnedCollectionViews.Clear();
        virtualizedCollectionSlots.Clear();
        collectionItemViewRefCache.Clear();
        cachedOwnedCollectionEntries.Clear();
        cachedOwnedEntriesByModelId.Clear();
    }

    private System.Collections.IEnumerator RebuildCollectionViewsNextFrame()
    {
        yield return null;

        openCollectionViewsCoroutine = null;
        RebuildCollectionViews();
    }

    private System.Collections.IEnumerator PrepareAndOpenCollectionViewsRoutine()
    {
        yield return null;

        yield return StartCoroutine(BuildCollectionViewsBeforeOpenRoutine());

        ShowLoadingOverlay(false);
        OpenCurrentPanel();
        openCollectionViewsCoroutine = null;
    }

    private System.Collections.IEnumerator BuildCollectionViewsBeforeOpenRoutine()
    {
        ClearCollectionViews();
        isUsingVirtualizedCollectionViews = false;

        if (collectionItemPrefab == null && collectedCollectionItemPrefab == null)
            yield break;

        collectionContent = ResolveBestCollectionContentRoot(collectionContent);

        if (collectionContent == null)
        {
            WarnMissingCollectionContent();
            yield break;
        }

        DisableCollectionLayoutComponents();
        RectTransform contentRect = collectionContent as RectTransform;
        RectTransform viewportRect = collectionViewport;
        collectionScrollRect = ResolveCollectionScrollRect();
        collectionContentCanvasGroup = ResolveCollectionContentCanvasGroup();
        UnbindCollectionScrollRect();
        SetCollectionContentVisible(false);

        InventoryManager inventoryManager = InventoryManager.Instance;
        RebuildOwnedCollectionEntryCache(inventoryManager);

        int batchSize = Mathf.Max(1, collectionOpenBatchSize);
        for (int startIndex = 0; startIndex < currentCollectionItems.Count; startIndex += batchSize)
        {
            BuildCollectionViewRange(inventoryManager, startIndex, batchSize, positionImmediately: false);
            yield return null;
        }

        ApplyCollectionGridLayout(contentRect, viewportRect);
        SetCollectionContentVisible(true);

        if (pendingCollectionScrollRestorePosition.HasValue)
        {
            RestoreCollectionScrollPosition(pendingCollectionScrollRestorePosition.Value);
            pendingCollectionScrollRestorePosition = null;
        }
        else if (collectionScrollRect != null)
        {
            collectionScrollRect.StopMovement();
            collectionScrollRect.verticalNormalizedPosition = 1f;
            RectTransform scrollContentRect = GetCollectionScrollContentRect();
            if (scrollContentRect != null)
                scrollContentRect.anchoredPosition = Vector2.zero;
        }
    }

    private void ApplyCollectionItemView(Transform viewRoot, GiftCatalogDatabase.GiftItemRecord item, InventoryManager.InventoryEntry ownedEntry, InventoryManager inventoryManager, bool useCollectedPrefab)
    {
        if (viewRoot == null || item == null)
            return;

        CollectionItemViewRefs viewRefs = GetCollectionItemViewRefs(viewRoot.gameObject);
        int rarityPermille = ownedEntry != null && ownedEntry.modelRarityPermille > 0
            ? ownedEntry.modelRarityPermille
            : item.rarity_permille;
        string rarityLabel = FormatRarityLabel(rarityPermille);

        if (useCollectedPrefab && ownedEntry != null && inventoryManager != null)
        {
            if (isUsingVirtualizedCollectionViews)
                useCollectedPrefab = false;
        }

        if (useCollectedPrefab && ownedEntry != null && inventoryManager != null)
        {
            inventoryManager.ApplyEntryToExternalPrefab(viewRoot.gameObject, ownedEntry);
            SetText(viewRefs != null ? viewRefs.titleText : null, item.id);
            SetText(viewRefs != null ? viewRefs.legacyTitleText : null, item.id);
            SetText(viewRefs != null ? viewRefs.rarityText : null, rarityLabel);
            SetText(viewRefs != null ? viewRefs.legacyRarityText : null, rarityLabel);
            return;
        }

        Sprite modelSprite = inventoryManager != null
            ? inventoryManager.GetModelSpriteForUI(item.id, item.name)
            : null;

        Image modelImage = viewRefs != null ? viewRefs.modelImage : FindCollectionItemImage(viewRoot);
        if (modelImage != null)
        {
            modelImage.sprite = modelSprite;
            modelImage.enabled = modelSprite != null;
            modelImage.preserveAspect = true;
        }

        SetText(viewRefs != null ? viewRefs.titleText : null, item.id);
        SetText(viewRefs != null ? viewRefs.legacyTitleText : null, item.id);
        SetText(viewRefs != null ? viewRefs.idText : null, item.id);
        SetText(viewRefs != null ? viewRefs.legacyIdText : null, item.id);
        SetText(viewRefs != null ? viewRefs.rarityText : null, rarityLabel);
        SetText(viewRefs != null ? viewRefs.legacyRarityText : null, rarityLabel);
    }

    private void BindCollectionItemButton(GameObject view, GiftCatalogDatabase.GiftItemRecord item, InventoryManager inventoryManager, bool isOwned)
    {
        if (view == null || item == null)
            return;

        CollectionItemViewRefs viewRefs = GetCollectionItemViewRefs(view);
        Button button = EnsureItemButton(view);
        if (button == null)
            return;

        button.interactable = isOwned;
        if (viewRefs != null)
        {
            viewRefs.boundItem = item;
            viewRefs.boundInventoryManager = inventoryManager;
            viewRefs.boundIsOwned = isOwned;

            if (!viewRefs.clickListenerBound)
            {
                viewRefs.clickListenerBound = true;
                button.onClick.AddListener(() => HandleCollectionItemButtonClick(view));
            }
        }
    }

    private void OpenOwnedItemsPanel(GiftCatalogDatabase.GiftItemRecord item, InventoryManager inventoryManager)
    {
        if (item == null || inventoryManager == null)
            return;

        ResolveOwnedItemsReferences();
        List<InventoryManager.InventoryEntry> matchingEntries = GetOwnedEntriesForCollectionItem(item);
        if (matchingEntries == null || matchingEntries.Count == 0)
            matchingEntries = inventoryManager.GetEntriesForModel(currentGiftId, item.id);

        if (ownedItemsPanel == null)
        {
            WarnMissingOwnedItemsPanel();
            return;
        }

        if (ownedItemsContent == null)
            WarnMissingOwnedItemsContent();

        if (ownedItemsContent == ownedItemsPanel)
            WarnInvalidOwnedItemsContent();

        ClearOwnedItemViews();

        GameObject prefabToUse = ownedItemVariantPrefab != null
            ? ownedItemVariantPrefab
            : (collectedCollectionItemPrefab != null ? collectedCollectionItemPrefab : collectionItemPrefab);

        if (ownedItemsContent != null && prefabToUse != null)
        {
            for (int i = 0; i < matchingEntries.Count; i++)
            {
                InventoryManager.InventoryEntry entry = matchingEntries[i];
                if (entry == null)
                    continue;

                GameObject view = Instantiate(prefabToUse, ownedItemsContent, false);
                view.name = "OwnedModel_" + item.id + "_" + entry.inventoryNumber;
                view.SetActive(true);
                spawnedOwnedItemViews.Add(view);

                inventoryManager.ApplyEntryToExternalPrefab(view, entry);
                BindOwnedVariantButton(view, item, entry);
            }
        }
        if (ownedItemsContent != null)
        {
            AutoScrollHeight autoScrollHeight = ownedItemsContent.GetComponent<AutoScrollHeight>();
            if (autoScrollHeight != null)
                autoScrollHeight.UpdateHeight();
        }

        RebuildOwnedItemsScrollLayout();

        ShowOwnedItemsPanelContainer();
        Canvas.ForceUpdateCanvases();
    }

    private void WarnMissingCollectionContent()
    {
        if (missingCollectionContentWarningLogged)
            return;

        missingCollectionContentWarningLogged = true;
        Debug.LogWarning("[AlbumPreviewPanelOpener] Collection content was not found. Assign Collection Content in the inspector.", this);
    }

    private void WarnMissingOwnedItemsPanel()
    {
        if (missingOwnedItemsPanelWarningLogged)
            return;

        missingOwnedItemsPanelWarningLogged = true;
        Debug.LogWarning("[AlbumPreviewPanelOpener] Owned items panel is not assigned and could not be auto-found.", this);
    }

    private void WarnMissingOwnedItemsContent()
    {
        if (missingOwnedItemsContentWarningLogged)
            return;

        missingOwnedItemsContentWarningLogged = true;
        Debug.LogWarning("[AlbumPreviewPanelOpener] Owned items panel is assigned, but Owned Items Content was not found. Opening panel without spawned variants.", this);
    }

    private void WarnInvalidOwnedItemsContent()
    {
        if (invalidOwnedItemsContentWarningLogged)
            return;

        invalidOwnedItemsContentWarningLogged = true;
        Debug.LogWarning("[AlbumPreviewPanelOpener] Owned Items Content points to the panel itself. Assign the actual content container.", this);
    }

    private void BindOwnedVariantButton(GameObject view, GiftCatalogDatabase.GiftItemRecord item, InventoryManager.InventoryEntry entry)
    {
        if (view == null || item == null || entry == null)
            return;

        Button button = EnsureItemButton(view);
        if (button == null)
            return;

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => SelectOwnedVariant(item, entry));
    }

    private void HandleCollectionItemButtonClick(GameObject view)
    {
        CollectionItemViewRefs viewRefs = GetCollectionItemViewRefs(view);
        if (viewRefs == null ||
            !viewRefs.boundIsOwned ||
            viewRefs.boundItem == null ||
            viewRefs.boundInventoryManager == null)
        {
            return;
        }

        OpenOwnedItemsPanel(viewRefs.boundItem, viewRefs.boundInventoryManager);
    }

    private void CloseOwnedItemsPanel()
    {
        if (ownedItemsPanel == null)
            return;

        if (!isActiveAndEnabled)
        {
            CloseOwnedItemsPanelImmediate();
            return;
        }

        pendingOwnedItemsViewClear = true;
        closingOwnedItemsPanel = ownedItemsPanel;
        closingOwnedItemsPanel.gameObject.SetActive(true);
        isOwnedItemsOpen = false;
        ownedItemsTargetY = CurrentOwnedItemsHiddenY;
        ownedItemsHideAfterTime = Time.unscaledTime + Mathf.Max(0f, ownedItemsCloseHideDelay);

        if (ownedItemsCloseGuardCoroutine != null)
            StopCoroutine(ownedItemsCloseGuardCoroutine);

        ownedItemsCloseGuardCoroutine = StartCoroutine(KeepOwnedItemsClosingPanelActive(ownedItemsPanel));
    }

    private void CloseOwnedItemsPanelImmediate()
    {
        if (ownedItemsPanel == null)
            return;

        pendingOwnedItemsViewClear = false;
        closingOwnedItemsPanel = null;
        ownedItemsHideAfterTime = -1f;
        isOwnedItemsOpen = false;
        ownedItemsTargetY = CurrentOwnedItemsHiddenY;

        if (ownedItemsCloseGuardCoroutine != null)
        {
            StopCoroutine(ownedItemsCloseGuardCoroutine);
            ownedItemsCloseGuardCoroutine = null;
        }

        Vector2 position = ownedItemsPanel.anchoredPosition;
        position.y = CurrentOwnedItemsHiddenY;
        ownedItemsPanel.anchoredPosition = position;
        ownedItemsPanel.gameObject.SetActive(false);

        ClearOwnedItemViews();
    }

    private void ShowOwnedItemsPanelContainer()
    {
        if (ownedItemsPanel == null)
            return;

        Transform current = ownedItemsPanel;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
                current.gameObject.SetActive(true);

            current = current.parent;
        }

        ownedItemsPanel.gameObject.SetActive(true);
        ownedItemsPanel.SetAsLastSibling();

        if (ownedItemsCloseGuardCoroutine != null)
        {
            StopCoroutine(ownedItemsCloseGuardCoroutine);
            ownedItemsCloseGuardCoroutine = null;
        }

        pendingOwnedItemsViewClear = false;
        closingOwnedItemsPanel = null;
        ownedItemsHideAfterTime = -1f;

        Vector2 anchoredPosition = ownedItemsPanel.anchoredPosition;
        float verticalThreshold = Mathf.Max(400f, ownedItemsPanel.rect.height * 0.5f + 100f);
        if (!isOwnedItemsOpen && Mathf.Abs(anchoredPosition.y - CurrentOwnedItemsHiddenY) > verticalThreshold)
            anchoredPosition.y = CurrentOwnedItemsHiddenY;

        ownedItemsPanel.anchoredPosition = anchoredPosition;
        isOwnedItemsOpen = true;
        ownedItemsTargetY = 0f;
    }

    private InventoryManager.InventoryEntry ResolveOwnedEntryForCollectionItem(GiftCatalogDatabase.GiftItemRecord item, InventoryManager inventoryManager)
    {
        if (item == null || inventoryManager == null)
            return null;

        List<InventoryManager.InventoryEntry> matchingEntries = GetOwnedEntriesForCollectionItem(item);
        if (matchingEntries == null || matchingEntries.Count == 0)
            return null;

        string selectionKey = BuildGiftModelSelectionKey(currentGiftId, item.id);
        if (selectedInventoryNumberByGiftModelKey.TryGetValue(selectionKey, out int selectedInventoryNumber))
        {
            for (int i = 0; i < matchingEntries.Count; i++)
            {
                InventoryManager.InventoryEntry entry = matchingEntries[i];
                if (entry != null && entry.inventoryNumber == selectedInventoryNumber)
                    return entry;
            }

            RemoveSelectedVariantSelection(selectionKey);
        }

        return matchingEntries[0];
    }

    private void RebuildOwnedCollectionEntryCache(InventoryManager inventoryManager)
    {
        cachedOwnedCollectionEntries.Clear();
        cachedOwnedEntriesByModelId.Clear();

        if (inventoryManager != null)
        {
            List<InventoryManager.InventoryEntry> matchingGiftEntries = inventoryManager.GetEntriesForGiftLoose(currentGiftId);
            for (int i = 0; i < matchingGiftEntries.Count; i++)
            {
                InventoryManager.InventoryEntry entry = matchingGiftEntries[i];
                string modelId = NormalizeModelId(entry != null ? entry.modelId : string.Empty);
                if (string.IsNullOrWhiteSpace(modelId))
                    continue;

                if (!cachedOwnedEntriesByModelId.TryGetValue(modelId, out List<InventoryManager.InventoryEntry> entries))
                {
                    entries = new List<InventoryManager.InventoryEntry>();
                    cachedOwnedEntriesByModelId[modelId] = entries;
                }

                entries.Add(entry);
            }
        }

        for (int i = 0; i < currentCollectionItems.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord item = currentCollectionItems[i];
            InventoryManager.InventoryEntry ownedEntry = ResolveOwnedEntryForCollectionItem(item, inventoryManager);
            cachedOwnedCollectionEntries.Add(ownedEntry);
        }
    }

    private void UpdateCollectionProgressUI(int ownedCount, int totalCount)
    {
        int safeOwnedCount = Mathf.Max(0, ownedCount);
        int safeTotalCount = Mathf.Max(0, totalCount);
        float fill = safeTotalCount > 0 ? safeOwnedCount / (float)safeTotalCount : 0f;
        float percent = fill * 100f;

        if (collectionProgressFillImage != null)
            collectionProgressFillImage.fillAmount = Mathf.Clamp01(fill);

        if (collectionProgressPercentText != null)
        {
            collectionProgressPercentText.text =
                percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

    }

    private InventoryManager.InventoryEntry GetCachedOwnedCollectionEntry(int index, GiftCatalogDatabase.GiftItemRecord item, InventoryManager inventoryManager)
    {
        if (index >= 0 && index < cachedOwnedCollectionEntries.Count)
            return cachedOwnedCollectionEntries[index];

        return ResolveOwnedEntryForCollectionItem(item, inventoryManager);
    }

    private List<InventoryManager.InventoryEntry> GetOwnedEntriesForCollectionItem(GiftCatalogDatabase.GiftItemRecord item)
    {
        string modelId = NormalizeModelId(item != null ? item.id : string.Empty);
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        cachedOwnedEntriesByModelId.TryGetValue(modelId, out List<InventoryManager.InventoryEntry> matchingEntries);
        return matchingEntries;
    }

    private static string NormalizeModelId(string modelId)
    {
        return string.IsNullOrWhiteSpace(modelId) ? string.Empty : modelId.Trim();
    }

    private void SelectOwnedVariant(GiftCatalogDatabase.GiftItemRecord item, InventoryManager.InventoryEntry entry)
    {
        if (item == null || entry == null)
            return;

        SaveSelectedVariantSelection(BuildGiftModelSelectionKey(currentGiftId, item.id), entry.inventoryNumber);
        CloseOwnedItemsPanel();

        if (!TryRefreshSelectedCollectionItemView(item, entry))
        {
            Vector2 preservedScrollPosition = GetCollectionScrollPosition();
            pendingCollectionScrollRestorePosition = preservedScrollPosition;
            RebuildCollectionViews(true);
            if (!ShouldUseLazyCollectionLoading())
                RestoreCollectionScrollPosition(preservedScrollPosition);
        }
    }

    private static string BuildGiftModelSelectionKey(string giftId, string modelId)
    {
        return (string.IsNullOrWhiteSpace(giftId) ? "default" : giftId.Trim()) + "|" +
               (string.IsNullOrWhiteSpace(modelId) ? "" : modelId.Trim());
    }

    private Vector2 GetCollectionScrollPosition()
    {
        RectTransform contentRect = GetCollectionScrollContentRect();
        return contentRect != null ? contentRect.anchoredPosition : Vector2.zero;
    }

    private void RestoreCollectionScrollPosition(Vector2 anchoredPosition)
    {
        RectTransform contentRect = GetCollectionScrollContentRect();
        if (contentRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        contentRect.anchoredPosition = anchoredPosition;
    }

    private bool TryRefreshSelectedCollectionItemView(GiftCatalogDatabase.GiftItemRecord item, InventoryManager.InventoryEntry selectedEntry)
    {
        if (item == null || selectedEntry == null)
            return false;

        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
            return false;

        int itemIndex = FindCollectionItemIndex(item);
        if (itemIndex < 0)
            return false;

        while (cachedOwnedCollectionEntries.Count <= itemIndex)
            cachedOwnedCollectionEntries.Add(null);

        cachedOwnedCollectionEntries[itemIndex] = selectedEntry;

        if (TryRefreshVirtualizedCollectionItemView(itemIndex, item, selectedEntry, inventoryManager))
            return true;

        if (itemIndex < 0 || itemIndex >= spawnedCollectionViews.Count)
            return false;

        GameObject view = spawnedCollectionViews[itemIndex];
        if (view == null)
            return false;

        RefreshCollectionItemView(view, item, selectedEntry, inventoryManager);
        return true;
    }

    private int FindCollectionItemIndex(GiftCatalogDatabase.GiftItemRecord item)
    {
        if (item == null)
            return -1;

        for (int i = 0; i < currentCollectionItems.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord currentItem = currentCollectionItems[i];
            if (currentItem == null)
                continue;

            if (string.Equals(currentItem.id, item.id, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private bool TryRefreshVirtualizedCollectionItemView(
        int itemIndex,
        GiftCatalogDatabase.GiftItemRecord item,
        InventoryManager.InventoryEntry selectedEntry,
        InventoryManager inventoryManager)
    {
        bool refreshed = false;

        for (int i = 0; i < virtualizedCollectionSlots.Count; i++)
        {
            VirtualizedCollectionSlot slot = virtualizedCollectionSlots[i];
            if (slot == null || slot.boundIndex != itemIndex)
                continue;

            if (slot.view == null)
                continue;

            RefreshCollectionItemView(slot.view, item, selectedEntry, inventoryManager);
            slot.view.SetActive(true);
            refreshed = true;
        }

        return refreshed;
    }

    private void RefreshCollectionItemView(
        GameObject view,
        GiftCatalogDatabase.GiftItemRecord item,
        InventoryManager.InventoryEntry ownedEntry,
        InventoryManager inventoryManager)
    {
        if (view == null || item == null)
            return;

        bool useCollectedPrefab = ownedEntry != null && collectedCollectionItemPrefab != null;
        ApplyCollectionItemView(view.transform, item, ownedEntry, inventoryManager, useCollectedPrefab);
        BindCollectionItemButton(view, item, inventoryManager, ownedEntry != null);
    }

    private int BuildCollectionViewRange(InventoryManager inventoryManager, int startIndex, int count, bool positionImmediately)
    {
        if (collectionContent == null || count <= 0)
            return 0;

        int ownedMatchCount = 0;
        int endExclusive = Mathf.Min(currentCollectionItems.Count, startIndex + count);

        for (int i = startIndex; i < endExclusive; i++)
        {
            GiftCatalogDatabase.GiftItemRecord item = currentCollectionItems[i];
            if (item == null)
                continue;

            InventoryManager.InventoryEntry ownedEntry = GetCachedOwnedCollectionEntry(i, item, inventoryManager);

            if (ownedEntry != null)
            {
                ownedMatchCount++;
            }

            bool useCollectedPrefab = ownedEntry != null && collectedCollectionItemPrefab != null;
            GameObject prefabToUse = useCollectedPrefab
                ? collectedCollectionItemPrefab
                : collectionItemPrefab;
            if (prefabToUse == null)
                continue;

            GameObject view = Instantiate(prefabToUse, collectionContent, false);
            view.name = string.IsNullOrWhiteSpace(item.name) ? "AlbumItem_" + i : item.name;
            view.SetActive(true);
            spawnedCollectionViews.Add(view);

            if (positionImmediately)
                PositionCollectionItemView(view.GetComponent<RectTransform>(), i);

            ApplyCollectionItemView(view.transform, item, ownedEntry, inventoryManager, useCollectedPrefab);
            BindCollectionItemButton(view, item, inventoryManager, ownedEntry != null);
        }

        return ownedMatchCount;
    }

    private void PositionCollectionItemView(RectTransform itemRect, int itemIndex)
    {
        if (itemRect == null)
            return;

        int columnCount = Mathf.Max(1, virtualizedCollectionColumnCount);
        int row = itemIndex / columnCount;
        int column = itemIndex % columnCount;
        float startX = -virtualizedCollectionGridWidth * 0.5f;

        itemRect.anchorMin = new Vector2(0.5f, 1f);
        itemRect.anchorMax = new Vector2(0.5f, 1f);
        itemRect.pivot = new Vector2(0f, 1f);
        itemRect.sizeDelta = collectionCellSize;
        itemRect.anchoredPosition = new Vector2(
            startX + column * (collectionCellSize.x + collectionSpacing.x),
            -(collectionTopPadding + row * (collectionCellSize.y + collectionSpacing.y)));
        itemRect.localScale = Vector3.one;
    }

    private void ConfigureVirtualizedCollectionLayout(RectTransform contentRect, RectTransform viewportRect)
    {
        if (contentRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        if (viewportRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewportRect);

        float fullCellWidth = Mathf.Max(1f, collectionCellSize.x + collectionSpacing.x);
        RectTransform layoutArea = ResolveCollectionLayoutArea(contentRect, viewportRect);
        float layoutWidth = layoutArea != null ? layoutArea.rect.width : contentRect.rect.width;
        if (layoutWidth <= 0f)
            layoutWidth = contentRect.rect.width;

        virtualizedCollectionColumnCount = Mathf.Max(1, Mathf.FloorToInt((layoutWidth + collectionSpacing.x) / fullCellWidth));
        virtualizedCollectionTotalRows = Mathf.CeilToInt(currentCollectionItems.Count / (float)virtualizedCollectionColumnCount);
        virtualizedCollectionGridWidth = virtualizedCollectionColumnCount * collectionCellSize.x +
                                         Mathf.Max(0, virtualizedCollectionColumnCount - 1) * collectionSpacing.x;
        virtualizedCollectionRowStep = Mathf.Max(1f, collectionCellSize.y + collectionSpacing.y);

        int visibleRowsFromViewport = 1;
        if (viewportRect != null)
        {
            float availableHeight = Mathf.Max(collectionCellSize.y, viewportRect.rect.height - collectionTopPadding - GetEffectiveCollectionBottomPadding());
            visibleRowsFromViewport = Mathf.Max(1, Mathf.CeilToInt((availableHeight + collectionSpacing.y) / virtualizedCollectionRowStep));
        }
        else
        {
            visibleRowsFromViewport = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(1, Mathf.Min(initialCollectionItemCount, virtualizedCollectionColumnCount * 2)) / (float)virtualizedCollectionColumnCount));
        }

        int bufferedRows = 2;

        virtualizedCollectionPoolRowCount = Mathf.Min(
            Mathf.Max(1, virtualizedCollectionTotalRows),
            visibleRowsFromViewport + bufferedRows);

        float contentHeight = collectionTopPadding +
                              GetEffectiveCollectionBottomPadding() +
                              virtualizedCollectionTotalRows * collectionCellSize.y +
                              Mathf.Max(0, virtualizedCollectionTotalRows - 1) * collectionSpacing.y;

        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = new Vector2(0f, 0f);
        contentRect.sizeDelta = new Vector2(virtualizedCollectionGridWidth, Mathf.Max(0f, contentHeight));
        SyncCollectionScrollContainerSize(contentRect);
    }

    private void EnsureVirtualizedCollectionPool()
    {
        if (collectionContent == null)
            return;

        int requiredCount = Mathf.Min(
            currentCollectionItems.Count,
            Mathf.Max(1, virtualizedCollectionPoolRowCount) * Mathf.Max(1, virtualizedCollectionColumnCount));

        while (virtualizedCollectionSlots.Count > requiredCount)
        {
            int lastIndex = virtualizedCollectionSlots.Count - 1;
            VirtualizedCollectionSlot slot = virtualizedCollectionSlots[lastIndex];
            if (slot != null && slot.view != null)
            {
                spawnedCollectionViews.Remove(slot.view);
                ForgetCollectionItemViewRefs(slot.view);
                ClearEditorSelectionIfDestroyedObject(slot.view);
                slot.view.SetActive(false);
                Destroy(slot.view);
            }

            virtualizedCollectionSlots.RemoveAt(lastIndex);
        }

        GameObject defaultPrefab = collectionItemPrefab != null ? collectionItemPrefab : collectedCollectionItemPrefab;
        if (defaultPrefab == null)
            return;

        while (virtualizedCollectionSlots.Count < requiredCount)
        {
            GameObject view = Instantiate(defaultPrefab, collectionContent, false);
            view.name = "AlbumLazyItem_" + virtualizedCollectionSlots.Count;
            view.SetActive(false);

            RectTransform rectTransform = view.GetComponent<RectTransform>();
            VirtualizedCollectionSlot slot = new VirtualizedCollectionSlot
            {
                view = view,
                rectTransform = rectTransform,
                usesCollectedPrefab = false,
                boundIndex = -1
            };

            virtualizedCollectionSlots.Add(slot);
            spawnedCollectionViews.Add(view);
        }
    }

    private void UpdateVirtualizedCollectionWindow(bool force = false)
    {
        if (collectionContent == null || virtualizedCollectionSlots.Count == 0 || currentCollectionItems.Count == 0)
            return;

        RectTransform contentRect = collectionContent as RectTransform;
        RectTransform scrollContentRect = GetCollectionScrollContentRect();
        RectTransform scrollOffsetRect = scrollContentRect != null ? scrollContentRect : contentRect;
        if (contentRect == null || scrollOffsetRect == null)
            return;

        int maxTopRow = Mathf.Max(0, virtualizedCollectionTotalRows - virtualizedCollectionPoolRowCount);
        float scrollOffsetY = Mathf.Max(0f, scrollOffsetRect.anchoredPosition.y - collectionTopPadding);
        int topVisibleRow = Mathf.Max(0, Mathf.FloorToInt(scrollOffsetY / Mathf.Max(1f, virtualizedCollectionRowStep)));
        int topRow = Mathf.Clamp(topVisibleRow, 0, maxTopRow);

        if (!force && topRow == virtualizedCollectionTopRow)
            return;

        virtualizedCollectionTopRow = topRow;

        InventoryManager inventoryManager = InventoryManager.Instance;
        int firstDataIndex = topRow * virtualizedCollectionColumnCount;

        for (int slotIndex = 0; slotIndex < virtualizedCollectionSlots.Count; slotIndex++)
        {
            int dataIndex = firstDataIndex + slotIndex;
            UpdateVirtualizedCollectionSlot(slotIndex, dataIndex, inventoryManager);
        }
    }

    private void UpdateVirtualizedCollectionSlot(int slotIndex, int dataIndex, InventoryManager inventoryManager)
    {
        if (slotIndex < 0 || slotIndex >= virtualizedCollectionSlots.Count)
            return;

        VirtualizedCollectionSlot slot = virtualizedCollectionSlots[slotIndex];
        if (slot == null)
            return;

        if (dataIndex < 0 || dataIndex >= currentCollectionItems.Count)
        {
            if (slot.view != null)
                slot.view.SetActive(false);

            slot.boundIndex = -1;
            return;
        }

        GiftCatalogDatabase.GiftItemRecord item = currentCollectionItems[dataIndex];
        InventoryManager.InventoryEntry ownedEntry = GetCachedOwnedCollectionEntry(dataIndex, item, inventoryManager);
        if (slot == null || slot.view == null || slot.rectTransform == null)
            return;

        int row = dataIndex / virtualizedCollectionColumnCount;
        int column = dataIndex % virtualizedCollectionColumnCount;
        float startX = -virtualizedCollectionGridWidth * 0.5f;

        slot.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        slot.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        slot.rectTransform.pivot = new Vector2(0f, 1f);
        slot.rectTransform.sizeDelta = collectionCellSize;
        slot.rectTransform.anchoredPosition = new Vector2(
            startX + column * (collectionCellSize.x + collectionSpacing.x),
            -(collectionTopPadding + row * (collectionCellSize.y + collectionSpacing.y)));
        slot.rectTransform.localScale = Vector3.one;

        ApplyCollectionItemView(slot.view.transform, item, ownedEntry, inventoryManager, useCollectedPrefab: false);
        BindCollectionItemButton(slot.view, item, inventoryManager, ownedEntry != null);
        slot.view.name = string.IsNullOrWhiteSpace(item.name) ? "AlbumItem_" + dataIndex : item.name;
        slot.view.SetActive(true);
        slot.boundIndex = dataIndex;
    }

    private void ResetVirtualizedCollectionState()
    {
        virtualizedCollectionColumnCount = 1;
        virtualizedCollectionPoolRowCount = 0;
        virtualizedCollectionTotalRows = 0;
        virtualizedCollectionTopRow = int.MinValue;
        virtualizedCollectionGridWidth = 0f;
        virtualizedCollectionRowStep = 1f;
    }

    private static bool TryGetCollectionItems(
        string requestedGiftId,
        string resolvedCollectionName,
        out List<GiftCatalogDatabase.GiftItemRecord> items)
    {
        if (TryGetCachedCollectionItems(requestedGiftId, out items) ||
            TryGetCachedCollectionItems(resolvedCollectionName, out items))
        {
            return items != null;
        }

        string loadKey = !string.IsNullOrWhiteSpace(resolvedCollectionName)
            ? resolvedCollectionName
            : requestedGiftId;

        if (!GiftCatalogDatabase.TryLoadGiftItems(loadKey, out List<GiftCatalogDatabase.GiftItemRecord> loadedItems) ||
            loadedItems == null)
        {
            items = null;
            return false;
        }

        List<GiftCatalogDatabase.GiftItemRecord> sortedItems = new List<GiftCatalogDatabase.GiftItemRecord>(loadedItems);
        sortedItems.Sort(CompareCollectionItemsByRarestFirst);
        CacheCollectionItems(requestedGiftId, sortedItems);
        CacheCollectionItems(resolvedCollectionName, sortedItems);
        items = sortedItems;
        return true;
    }

    private static bool TryGetCachedCollectionItems(string collectionKey, out List<GiftCatalogDatabase.GiftItemRecord> items)
    {
        if (string.IsNullOrWhiteSpace(collectionKey))
        {
            items = null;
            return false;
        }

        return cachedCollectionItemsByKey.TryGetValue(collectionKey.Trim(), out items);
    }

    private static void CacheCollectionItems(string collectionKey, List<GiftCatalogDatabase.GiftItemRecord> items)
    {
        if (string.IsNullOrWhiteSpace(collectionKey) || items == null)
            return;

        cachedCollectionItemsByKey[collectionKey.Trim()] = items;
    }

    private void LoadSelectedVariantSelections()
    {
        selectedInventoryNumberByGiftModelKey.Clear();

        if (!PlayerPrefs.HasKey(SelectedVariantsPrefsKey))
            return;

        string json = PlayerPrefs.GetString(SelectedVariantsPrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return;

        SavedVariantSelectionCollection savedSelections = JsonUtility.FromJson<SavedVariantSelectionCollection>(json);
        if (savedSelections == null || savedSelections.items == null)
            return;

        for (int i = 0; i < savedSelections.items.Count; i++)
        {
            SavedVariantSelection item = savedSelections.items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.key))
                continue;

            selectedInventoryNumberByGiftModelKey[item.key] = item.inventoryNumber;
        }
    }

    private void SaveSelectedVariantSelection(string selectionKey, int inventoryNumber)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
            return;

        selectedInventoryNumberByGiftModelKey[selectionKey] = inventoryNumber;
        PersistSelectedVariantSelections();
    }

    private void RemoveSelectedVariantSelection(string selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
            return;

        if (!selectedInventoryNumberByGiftModelKey.Remove(selectionKey))
            return;

        PersistSelectedVariantSelections();
    }

    private void PersistSelectedVariantSelections()
    {
        SavedVariantSelectionCollection savedSelections = new SavedVariantSelectionCollection();

        foreach (KeyValuePair<string, int> pair in selectedInventoryNumberByGiftModelKey)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            savedSelections.items.Add(new SavedVariantSelection
            {
                key = pair.Key,
                inventoryNumber = pair.Value
            });
        }

        string json = JsonUtility.ToJson(savedSelections);
        PlayerPrefs.SetString(SelectedVariantsPrefsKey, json);
        PlayerPrefs.Save();
    }

    private void ClearOwnedItemViews()
    {
        for (int i = spawnedOwnedItemViews.Count - 1; i >= 0; i--)
        {
            GameObject view = spawnedOwnedItemViews[i];
            if (view != null)
            {
                ForgetCollectionItemViewRefs(view);
                ClearEditorSelectionIfDestroyedObject(view);
                Destroy(view);
            }
        }

        spawnedOwnedItemViews.Clear();
        RebuildOwnedItemsScrollLayout();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void ClearEditorSelectionIfDestroyedObject(GameObject view)
    {
#if UNITY_EDITOR
        if (view == null)
            return;

        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
            return;

        Transform current = selectedObject.transform;
        while (current != null)
        {
            if (current.gameObject == view)
            {
                Selection.activeGameObject = null;
                return;
            }

            current = current.parent;
        }
#endif
    }

    private static Image FindCollectionItemImage(Transform root)
    {
        if (root == null)
            return null;

        Image namedImage = FindByName<Image>(root, "Model", "Gift", "PreviewImage", "Preview", "Image");
        if (namedImage != null && namedImage.transform != root)
            return namedImage;

        Image[] images = root.GetComponentsInChildren<Image>(true);
        Image best = null;
        float bestArea = -1f;

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.transform == root)
                continue;

            RectTransform rect = image.rectTransform;
            float area = Mathf.Abs(rect.rect.width * rect.rect.height);
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = image;
        }

        return best;
    }

    private Button EnsureItemButton(GameObject view)
    {
        if (view == null)
            return null;

        CollectionItemViewRefs viewRefs = GetCollectionItemViewRefs(view);
        if (viewRefs != null && viewRefs.button != null)
            return viewRefs.button;

        Button button = view.GetComponent<Button>();
        if (button == null)
            button = view.GetComponentInChildren<Button>(true);

        if (button == null)
            button = view.AddComponent<Button>();

        if (button.targetGraphic == null)
        {
            Graphic graphic = view.GetComponent<Graphic>();
            if (graphic == null)
                graphic = viewRefs != null ? viewRefs.modelImage : FindCollectionItemImage(view.transform);

            if (graphic == null)
                graphic = view.GetComponentInChildren<Graphic>(true);

            button.targetGraphic = graphic;
        }

        if (viewRefs != null)
            viewRefs.button = button;

        return button;
    }

    private CollectionItemViewRefs GetCollectionItemViewRefs(GameObject view)
    {
        if (view == null)
            return null;

        int instanceId = view.GetInstanceID();
        if (collectionItemViewRefCache.TryGetValue(instanceId, out CollectionItemViewRefs cachedRefs) && cachedRefs != null)
            return cachedRefs;

        Transform root = view.transform;
        CollectionItemViewRefs viewRefs = new CollectionItemViewRefs
        {
            modelImage = FindCollectionItemImage(root),
            titleText = FindByName<TMP_Text>(root, "Title", "Name", "Label", "Modeltxt"),
            legacyTitleText = FindByName<Text>(root, "Title", "Name", "Label", "Modeltxt"),
            idText = FindByName<TMP_Text>(root, "Id", "IdText", "Num"),
            legacyIdText = FindByName<Text>(root, "Id", "IdText", "Num"),
            rarityText = FindNestedComponentByContainerName<TMP_Text>(root, "Rarity"),
            legacyRarityText = FindNestedComponentByContainerName<Text>(root, "Rarity"),
            rootView = view
        };

        collectionItemViewRefCache[instanceId] = viewRefs;
        return viewRefs;
    }

    private void ForgetCollectionItemViewRefs(GameObject view)
    {
        if (view == null)
            return;

        collectionItemViewRefCache.Remove(view.GetInstanceID());
    }

    private static int CompareCollectionItemsByRarestFirst(GiftCatalogDatabase.GiftItemRecord left, GiftCatalogDatabase.GiftItemRecord right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        int rarityComparison = left.rarity_permille.CompareTo(right.rarity_permille);
        if (rarityComparison != 0)
            return rarityComparison;

        return string.Compare(left.id, right.id, System.StringComparison.OrdinalIgnoreCase);
    }

    private static Transform ResolveBestCollectionContentRoot(Transform root)
    {
        if (root == null)
            return null;

        Transform directGrid = root.Find("Grid");
        if (directGrid != null)
            return directGrid;

        Transform directContent = root.Find("Content");
        if (directContent != null)
            return directContent;

        Transform directItems = root.Find("Items");
        if (directItems != null)
            return directItems;

        Transform directList = root.Find("List");
        if (directList != null)
            return directList;

        return root;
    }

    private void RebuildOwnedItemsScrollLayout()
    {
        if (!(ownedItemsContent is RectTransform contentRect))
            return;

        ResolveOwnedItemsReferences();
        DisableLayoutComponents(ownedItemsContent);

        RectTransform viewportRect = ownedItemsScrollRect != null ? ownedItemsScrollRect.viewport : null;

        if (ownedItemsScrollRect != null)
        {
            if (ownedItemsScrollRect.content == null || ownedItemsScrollRect.content != contentRect)
                ownedItemsScrollRect.content = contentRect;

            if (viewportRect != null && ownedItemsScrollRect.viewport == null)
                ownedItemsScrollRect.viewport = viewportRect;
        }

        ApplyOwnedItemsGridLayout(contentRect, viewportRect);
    }

    private void ApplyOwnedItemsGridLayout(RectTransform contentRect, RectTransform viewportRect)
    {
        if (contentRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        if (viewportRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewportRect);

        GetOwnedItemsLayoutSettings(
            contentRect,
            out Vector2 cellSize,
            out Vector2 spacing,
            out float topPadding,
            out float bottomPadding);

        float fullCellWidth = cellSize.x + spacing.x;
        float layoutWidth = viewportRect != null && viewportRect.rect.width > 0f
            ? viewportRect.rect.width
            : contentRect.rect.width;

        if (layoutWidth <= 0f && contentRect.parent is RectTransform parentRect)
            layoutWidth = parentRect.rect.width;

        if (layoutWidth <= 0f)
            layoutWidth = cellSize.x;

        int columnCount = Mathf.Max(1, Mathf.FloorToInt((layoutWidth + spacing.x) / Mathf.Max(1f, fullCellWidth)));
        int totalRows = Mathf.Max(1, Mathf.CeilToInt(spawnedOwnedItemViews.Count / (float)columnCount));
        float gridWidth = columnCount * cellSize.x + Mathf.Max(0, columnCount - 1) * spacing.x;
        float contentHeight = topPadding +
                              bottomPadding +
                              totalRows * cellSize.y +
                              Mathf.Max(0, totalRows - 1) * spacing.y;

        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(gridWidth, Mathf.Max(0f, contentHeight));

        float startX = -gridWidth * 0.5f;

        for (int i = 0; i < spawnedOwnedItemViews.Count; i++)
        {
            GameObject view = spawnedOwnedItemViews[i];
            if (view == null)
                continue;

            RectTransform itemRect = view.GetComponent<RectTransform>();
            if (itemRect == null)
                continue;

            int row = i / columnCount;
            int column = i % columnCount;

            itemRect.anchorMin = new Vector2(0.5f, 1f);
            itemRect.anchorMax = new Vector2(0.5f, 1f);
            itemRect.pivot = new Vector2(0f, 1f);
            itemRect.sizeDelta = cellSize;
            itemRect.anchoredPosition = new Vector2(
                startX + column * (cellSize.x + spacing.x),
                -(topPadding + row * (cellSize.y + spacing.y)));
            itemRect.localScale = Vector3.one;
        }

        if (ownedItemsScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            ownedItemsScrollRect.StopMovement();
            ownedItemsScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void GetOwnedItemsLayoutSettings(
        RectTransform contentRect,
        out Vector2 cellSize,
        out Vector2 spacing,
        out float topPadding,
        out float bottomPadding)
    {
        cellSize = collectionCellSize;
        spacing = collectionSpacing;
        topPadding = collectionTopPadding;
        bottomPadding = collectionBottomPadding;

        if (contentRect == null)
            return;

        GridLayoutGroup gridLayout = contentRect.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
            return;

        if (gridLayout.cellSize.x > 0f && gridLayout.cellSize.y > 0f)
            cellSize = gridLayout.cellSize;

        spacing = gridLayout.spacing;
        topPadding = gridLayout.padding.top;
        bottomPadding = gridLayout.padding.bottom;
    }

    private ScrollRect ResolveCollectionScrollRect()
    {
        if (collectionContent == null)
            return null;

        return collectionContent.GetComponentInParent<ScrollRect>(true);
    }

    private CanvasGroup ResolveCollectionContentCanvasGroup()
    {
        if (!(collectionContent is RectTransform contentRect))
            return null;

        CanvasGroup canvasGroup = contentRect.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = contentRect.gameObject.AddComponent<CanvasGroup>();

        return canvasGroup;
    }

    private void SetCollectionContentVisible(bool isVisible)
    {
        if (collectionContentCanvasGroup == null)
            return;

        collectionContentCanvasGroup.alpha = isVisible ? 1f : 0f;
        collectionContentCanvasGroup.interactable = isVisible;
        collectionContentCanvasGroup.blocksRaycasts = isVisible;
    }

    private void ShowLoadingOverlay(bool isVisible)
    {
        GameObject overlay = loadingScreen;
        if (overlay == null)
            return;

        overlay.SetActive(isVisible);

        CanvasGroup canvasGroup = overlay.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = isVisible ? 1f : 0f;
            canvasGroup.interactable = isVisible;
            canvasGroup.blocksRaycasts = isVisible;
        }
    }

    private void DisableCollectionLayoutComponents()
    {
        if (collectionContent == null)
            return;

        DisableLayoutComponents(collectionContent);
    }

    private static void DisableLayoutComponents(Transform target)
    {
        if (target == null)
            return;

        Behaviour gridLayout = target.GetComponent("GridLayoutGroup") as Behaviour;
        if (gridLayout != null && gridLayout.enabled)
            gridLayout.enabled = false;

        Behaviour horizontalLayout = target.GetComponent("HorizontalLayoutGroup") as Behaviour;
        if (horizontalLayout != null && horizontalLayout.enabled)
            horizontalLayout.enabled = false;

        Behaviour verticalLayout = target.GetComponent("VerticalLayoutGroup") as Behaviour;
        if (verticalLayout != null && verticalLayout.enabled)
            verticalLayout.enabled = false;

        Behaviour contentSizeFitter = target.GetComponent("ContentSizeFitter") as Behaviour;
        if (contentSizeFitter != null && contentSizeFitter.enabled)
            contentSizeFitter.enabled = false;

        Behaviour autoScrollHeight = target.GetComponent("AutoScrollHeight") as Behaviour;
        if (autoScrollHeight != null && autoScrollHeight.enabled)
            autoScrollHeight.enabled = false;
    }

    private void ApplyCollectionGridLayout(RectTransform contentRect, RectTransform viewportRect)
    {
        ApplyGridLayoutToViews(contentRect, viewportRect, spawnedCollectionViews);
    }

    private void ApplyGridLayoutToViews(RectTransform contentRect, RectTransform viewportRect, List<GameObject> views)
    {
        if (contentRect == null || views == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        if (viewportRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewportRect);

        float fullCellWidth = collectionCellSize.x + collectionSpacing.x;
        RectTransform layoutArea = ResolveCollectionLayoutArea(contentRect, viewportRect);
        float layoutWidth = layoutArea != null ? layoutArea.rect.width : contentRect.rect.width;
        if (layoutWidth <= 0f)
            layoutWidth = contentRect.rect.width;

        int columnCount = Mathf.Max(1, Mathf.FloorToInt((layoutWidth + collectionSpacing.x) / Mathf.Max(1f, fullCellWidth)));
        int totalItemCount = ShouldUseLazyCollectionLoading()
            ? Mathf.Max(views.Count, currentCollectionItems.Count)
            : views.Count;
        int totalRows = Mathf.CeilToInt(totalItemCount / (float)columnCount);
        float gridWidth = columnCount * collectionCellSize.x + Mathf.Max(0, columnCount - 1) * collectionSpacing.x;
        float contentHeight = collectionTopPadding +
                              GetEffectiveCollectionBottomPadding() +
                              totalRows * collectionCellSize.y +
                              Mathf.Max(0, totalRows - 1) * collectionSpacing.y;

        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = new Vector2(0f, 0f);
        contentRect.sizeDelta = new Vector2(gridWidth, Mathf.Max(0f, contentHeight));
        SyncCollectionScrollContainerSize(contentRect);

        float startX = -gridWidth * 0.5f;

        for (int i = 0; i < views.Count; i++)
        {
            GameObject view = views[i];
            if (view == null)
                continue;

            RectTransform itemRect = view.GetComponent<RectTransform>();
            if (itemRect == null)
                continue;

            int row = i / columnCount;
            int column = i % columnCount;

            itemRect.anchorMin = new Vector2(0.5f, 1f);
            itemRect.anchorMax = new Vector2(0.5f, 1f);
            itemRect.pivot = new Vector2(0f, 1f);
            itemRect.sizeDelta = collectionCellSize;
            itemRect.anchoredPosition = new Vector2(
                startX + column * (collectionCellSize.x + collectionSpacing.x),
                -(collectionTopPadding + row * (collectionCellSize.y + collectionSpacing.y)));
            itemRect.localScale = Vector3.one;
        }
    }

    private RectTransform ResolveCollectionLayoutArea(RectTransform contentRect, RectTransform viewportRect)
    {
        if (viewportRect != null && viewportRect.rect.width > 0f)
            return viewportRect;

        RectTransform parentRect = contentRect != null ? contentRect.parent as RectTransform : null;
        if (parentRect != null && parentRect.rect.width > 0f)
            return parentRect;

        return contentRect;
    }

    private float GetEffectiveCollectionBottomPadding()
    {
        return Mathf.Max(0f, collectionBottomPadding) + Mathf.Max(0f, collectionBottomOverlayPadding);
    }

    private RectTransform GetCollectionScrollContentRect()
    {
        if (collectionScrollRect != null && collectionScrollRect.content != null)
            return collectionScrollRect.content;

        return collectionContent as RectTransform;
    }

    private void SyncCollectionScrollContainerSize(RectTransform layoutContentRect)
    {
        if (layoutContentRect == null)
            return;

        RectTransform scrollContentRect = GetCollectionScrollContentRect();
        if (scrollContentRect == null || scrollContentRect == layoutContentRect)
            return;

        Canvas.ForceUpdateCanvases();

        scrollContentRect.anchorMin = new Vector2(0.5f, 1f);
        scrollContentRect.anchorMax = new Vector2(0.5f, 1f);
        scrollContentRect.pivot = new Vector2(0.5f, 1f);
        scrollContentRect.anchoredPosition = Vector2.zero;

        layoutContentRect.anchorMin = new Vector2(0.5f, 1f);
        layoutContentRect.anchorMax = new Vector2(0.5f, 1f);
        layoutContentRect.pivot = new Vector2(0.5f, 1f);
        layoutContentRect.anchoredPosition = Vector2.zero;

        Vector2 size = scrollContentRect.sizeDelta;
        size.x = Mathf.Max(size.x, layoutContentRect.sizeDelta.x);
        size.y = layoutContentRect.sizeDelta.y;
        scrollContentRect.sizeDelta = size;

        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContentRect);
    }

    private static T FindByName<T>(Transform root, params string[] objectNames) where T : Component
    {
        if (root == null || objectNames == null || objectNames.Length == 0)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        T best = null;
        int bestDepth = int.MaxValue;
        bool bestActive = false;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
                continue;

            if (!NameMatches(candidate.name, objectNames))
                continue;

            T component = candidate.GetComponent<T>();
            if (component == null)
                continue;

            int depth = GetDepthRelativeToRoot(root, candidate);
            bool isActive = candidate.gameObject.activeInHierarchy;

            if (best == null ||
                depth < bestDepth ||
                (depth == bestDepth && isActive && !bestActive))
            {
                best = component;
                bestDepth = depth;
                bestActive = isActive;
            }
        }

        return best;
    }

    private static Transform FindTransformByName(Transform root, params string[] objectNames)
    {
        if (root == null || objectNames == null || objectNames.Length == 0)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        Transform best = null;
        int bestDepth = int.MaxValue;
        bool bestActive = false;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
                continue;

            if (!NameMatches(candidate.name, objectNames))
                continue;

            int depth = GetDepthRelativeToRoot(root, candidate);
            bool isActive = candidate.gameObject.activeInHierarchy;

            if (best == null ||
                depth < bestDepth ||
                (depth == bestDepth && isActive && !bestActive))
            {
                best = candidate;
                bestDepth = depth;
                bestActive = isActive;
            }
        }

        return best;
    }

    private static T FindNestedComponentByContainerName<T>(Transform root, params string[] objectNames) where T : Component
    {
        Transform container = FindTransformByName(root, objectNames);
        if (container == null)
            return null;

        T component = container.GetComponent<T>();
        if (component != null)
            return component;

        return container.GetComponentInChildren<T>(true);
    }

    private static bool NameMatches(string candidateName, string[] objectNames)
    {
        if (string.IsNullOrWhiteSpace(candidateName))
            return false;

        string normalizedCandidate = NormalizeObjectName(candidateName);
        for (int i = 0; i < objectNames.Length; i++)
        {
            string expectedName = objectNames[i];
            if (string.IsNullOrWhiteSpace(expectedName))
                continue;

            if (string.Equals(candidateName, expectedName, System.StringComparison.OrdinalIgnoreCase))
                return true;

            string normalizedExpected = NormalizeObjectName(expectedName);
            if (string.IsNullOrEmpty(normalizedExpected))
                continue;

            if (string.Equals(normalizedCandidate, normalizedExpected, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalizedCandidate.IndexOf(normalizedExpected, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedExpected.IndexOf(normalizedCandidate, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string NormalizeObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static int GetDepthRelativeToRoot(Transform root, Transform candidate)
    {
        int depth = 0;
        Transform current = candidate;

        while (current != null && current != root)
        {
            depth++;
            current = current.parent;
        }

        return depth;
    }

    private static float GetHiddenYForPanel(RectTransform panel, float hiddenExtraOffset)
    {
        if (panel == null)
            return 0f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        return -panel.rect.height - Mathf.Max(0f, hiddenExtraOffset);
    }

    private System.Collections.IEnumerator PrewarmPanelRoutine()
    {
        if (prewarmCompleted)
            yield break;

        if (prewarmDelay > 0f)
            yield return new WaitForSecondsRealtime(prewarmDelay);

        ResolvePanelReferences(previewPanel);
        InitializePanelState(previewPanel);

        if (previewPanel == null || isOpen)
            yield break;

        GameObject panelObject = previewPanel.gameObject;
        if (panelObject == null || panelObject.activeSelf)
        {
            prewarmCompleted = true;
            prewarmCoroutine = null;
            yield break;
        }

        Vector2 originalPosition = previewPanel.anchoredPosition;
        float originalTargetY = targetY;

        panelObject.SetActive(true);

        Vector2 hiddenPosition = previewPanel.anchoredPosition;
        hiddenPosition.y = GetHiddenYForPanel(previewPanel, hiddenExtraOffset);
        previewPanel.anchoredPosition = hiddenPosition;
        targetY = GetHiddenYForPanel(previewPanel, hiddenExtraOffset);

        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return null;

        if (!isOpen)
            panelObject.SetActive(false);

        previewPanel.anchoredPosition = originalPosition;
        targetY = originalTargetY;

        prewarmCompleted = true;
        prewarmCoroutine = null;
    }

    private System.Collections.IEnumerator KeepClosingPanelActive(RectTransform panel)
    {
        if (panel == null)
            yield break;

        while (panel != null &&
               closingPanel == panel &&
               !isOpen &&
               (hideAfterTime < 0f || Time.unscaledTime < hideAfterTime))
        {
            if (!panel.gameObject.activeSelf)
                panel.gameObject.SetActive(true);

            yield return null;
        }

        if (closeGuardCoroutine != null)
            closeGuardCoroutine = null;
    }

    private void AnimateMainPanel()
    {
        RectTransform panel = isOpen
            ? (activePanel != null ? activePanel : previewPanel)
            : closingPanel;

        if (panel == null)
            return;

        Vector2 position = panel.anchoredPosition;
        position.y = Mathf.MoveTowards(position.y, targetY, speed * Time.unscaledDeltaTime);
        panel.anchoredPosition = position;

        if (!isOpen &&
            closingPanel != null &&
            HideOnStart &&
            Mathf.Abs(position.y - GetHiddenYForPanel(panel, hiddenExtraOffset)) <= 0.01f &&
            (hideAfterTime < 0f || Time.unscaledTime >= hideAfterTime) &&
            panel.gameObject.activeSelf)
        {
            panel.gameObject.SetActive(false);

            if (activePanel == panel)
                activePanel = null;

            closingPanel = null;
            hideAfterTime = -1f;
        }
    }

    private void AnimateOwnedItemsPanel()
    {
        RectTransform panel = isOwnedItemsOpen ? ownedItemsPanel : closingOwnedItemsPanel;
        if (panel == null)
            return;

        Vector2 position = panel.anchoredPosition;
        position.y = Mathf.MoveTowards(position.y, ownedItemsTargetY, ownedItemsSpeed * Time.unscaledDeltaTime);
        panel.anchoredPosition = position;

        if (!isOwnedItemsOpen &&
            closingOwnedItemsPanel != null &&
            HideOwnedItemsPanelOnStart &&
            Mathf.Abs(position.y - GetHiddenYForPanel(panel, ownedItemsHiddenExtraOffset)) <= 0.01f &&
            (ownedItemsHideAfterTime < 0f || Time.unscaledTime >= ownedItemsHideAfterTime) &&
            panel.gameObject.activeSelf)
        {
            if (pendingOwnedItemsViewClear)
            {
                ClearOwnedItemViews();
                pendingOwnedItemsViewClear = false;
            }

            panel.gameObject.SetActive(false);
            closingOwnedItemsPanel = null;
            ownedItemsHideAfterTime = -1f;
        }
    }

    private System.Collections.IEnumerator KeepOwnedItemsClosingPanelActive(RectTransform panel)
    {
        if (panel == null)
            yield break;

        while (panel != null &&
               closingOwnedItemsPanel == panel &&
               !isOwnedItemsOpen &&
               (ownedItemsHideAfterTime < 0f || Time.unscaledTime < ownedItemsHideAfterTime))
        {
            if (!panel.gameObject.activeSelf)
                panel.gameObject.SetActive(true);

            yield return null;
        }

        if (ownedItemsCloseGuardCoroutine != null)
            ownedItemsCloseGuardCoroutine = null;
    }

}
