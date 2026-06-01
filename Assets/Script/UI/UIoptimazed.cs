using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UIoptimazed : MonoBehaviour
{
    public event Action<InventoryManager.InventoryEntry> ItemClicked;

    [Header("Refs")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private InventoryManager inventoryManager;

    [Header("Layout")]
    [SerializeField] private Vector2 cellSize = new Vector2(160f, 220f);
    [SerializeField] private Vector2 spacing = new Vector2(10f, 10f);
    [FormerlySerializedAs("paddingTop")]
    [SerializeField] private float topPadding = 0f;
    [FormerlySerializedAs("paddingBottom")]
    [SerializeField] private float bottomEndPadding = 0f;
    [SerializeField] private int extraRows = 3;
    [SerializeField] private int maxPoolItems = 30;

    [Header("Item Paths")]
    [HideInInspector] [SerializeField] private string rootImagePath = "";
    [SerializeField] private string modelPath = "Model";
    [SerializeField] private string numPath = "Num";
    [SerializeField] private string patternPath = "2Dmask/Pattern";

    [Header("Pattern")]
    [SerializeField] private Material patternMaterial;
    [SerializeField] private float basePatternSize = 64f;

    [Header("Model")]
    [SerializeField] private Vector2 modelSize = new Vector2(110f, 110f);
    [SerializeField] private bool preserveAspect = true;
    [FormerlySerializedAs("modelAnchoredPosition")]
    [SerializeField] private Vector2 modelPosition = Vector2.zero;

    [Header("Root Material")]
    [SerializeField] private Material inventoryItemParentMaterial;

    private readonly List<ItemView> pool = new List<ItemView>();
    private readonly List<InventoryManager.InventoryEntry> items = new List<InventoryManager.InventoryEntry>();

    private int columnCount;
    private int visibleRows;
    private int pooledRows;
    private int poolSize;
    private int totalRows;

    private float rowHeight;
    private bool subscribed;
    private float referenceLayoutWidth = -1f;

    private int currentTopRow = -1;
    private int currentBottomRow = -1;

    private sealed class ItemView
    {
        public GameObject gameObject;
        public RectTransform rootRect;
        public CanvasGroup canvasGroup;
        public Image rootImage;
        public Image modelImage;
        public Image numImage;
        public Text legacyText;
        public TMP_Text tmpText;
        public RectTransform patternRoot;
        public Image[] patternImages;
        public Material[] patternMaterials;
        public Material rootMaterial;
        public Button button;
        public int boundDataIndex = -1;
        public int currentRow = -1;
        public int currentColumn = -1;
    }

    private void Awake()
    {
        if (scrollRect == null)
            scrollRect = GetComponent<ScrollRect>();

        if (scrollRect == null)
        {
            Debug.LogError("UIoptimazed: ScrollRect not found.");
            enabled = false;
            return;
        }

        if (viewport == null)
            viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.GetComponent<RectTransform>();

        if (content == null)
            content = scrollRect.content;

        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance;

        if (viewport == null || content == null || itemPrefab == null)
        {
            Debug.LogError("UIoptimazed: Assign viewport, content and itemPrefab.");
            enabled = false;
            return;
        }

        CacheReferenceLayoutWidth();
        DisableConflictingLayoutComponents();
        SetupContentForTopLeftLayout();
    }

    private void OnEnable()
    {
        Subscribe();
        ReloadFromInventory();
    }

    private void Start()
    {
        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance;

        ReloadFromInventory();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled)
            return;

        Rebuild();
    }

    public void ReloadFromInventory()
    {
        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance;

        items.Clear();

        if (inventoryManager != null && inventoryManager.Items != null)
        {
            for (int i = 0; i < inventoryManager.Items.Count; i++)
            {
                InventoryManager.InventoryEntry entry = inventoryManager.Items[i];
                if (entry != null)
                    items.Add(entry);
            }
        }

        if (inventoryManager != null)
            inventoryManager.WarmUiLookupCache(items);

        Rebuild();
    }

    public void Rebuild()
    {
        CacheReferenceLayoutWidth();
        DisableConflictingLayoutComponents();
        CleanupDestroyedPoolItems();
        DestroyForeignChildren();

        if (viewport == null || content == null)
            return;

        float viewportWidth = viewport.rect.width;
        float viewportHeight = viewport.rect.height;
        float layoutWidth = GetReferenceLayoutWidth(viewportWidth);

        rowHeight = cellSize.y + spacing.y;

        float usableWidth = layoutWidth + spacing.x;
        float usableHeight = Mathf.Max(0f, viewportHeight - topPadding);
        float fullCellWidth = cellSize.x + spacing.x;

        columnCount = Mathf.Max(1, Mathf.FloorToInt(usableWidth / Mathf.Max(1f, fullCellWidth)));
        visibleRows = Mathf.Max(1, Mathf.CeilToInt((usableHeight + spacing.y) / Mathf.Max(1f, rowHeight)));

        int desiredRows = visibleRows + extraRows * 2;
        int maxRowsByPool = Mathf.Max(1, maxPoolItems / Mathf.Max(1, columnCount));

        pooledRows = Mathf.Min(desiredRows, maxRowsByPool);
        pooledRows = Mathf.Max(visibleRows + 2, pooledRows);

        totalRows = Mathf.CeilToInt(items.Count / (float)columnCount);
        pooledRows = Mathf.Min(pooledRows, Mathf.Max(1, totalRows));

        poolSize = pooledRows * columnCount;

        Vector2 size = content.sizeDelta;
        size.x = Mathf.Max(layoutWidth, GetGridWidth());
        size.y = topPadding + GetBottomEndPadding() + totalRows * cellSize.y + Mathf.Max(0, totalRows - 1) * spacing.y;
        content.sizeDelta = size;

        ResetScrollToTop();
        EnsurePool();

        currentTopRow = -1;
        currentBottomRow = -1;

        InitializeVisibleRows();
    }

    private void SetupContentForTopLeftLayout()
    {
        if (content == null)
            return;

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
    }

    private void CacheReferenceLayoutWidth()
    {
        if (content == null)
            return;

        if (referenceLayoutWidth <= 0f && content.rect.width > 0f)
            referenceLayoutWidth = content.rect.width;
    }

    private void DisableConflictingLayoutComponents()
    {
        if (content == null)
            return;

        GridLayoutGroup gridLayout = content.GetComponent<GridLayoutGroup>();
        if (gridLayout != null && gridLayout.enabled)
            gridLayout.enabled = false;

        HorizontalLayoutGroup horizontalLayout = content.GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayout != null && horizontalLayout.enabled)
            horizontalLayout.enabled = false;

        VerticalLayoutGroup verticalLayout = content.GetComponent<VerticalLayoutGroup>();
        if (verticalLayout != null && verticalLayout.enabled)
            verticalLayout.enabled = false;

        ContentSizeFitter contentSizeFitter = content.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter != null && contentSizeFitter.enabled)
            contentSizeFitter.enabled = false;

        AutoScrollHeight autoScrollHeight = content.GetComponent<AutoScrollHeight>();
        if (autoScrollHeight != null && autoScrollHeight.enabled)
            autoScrollHeight.enabled = false;
    }

    private void ResetScrollToTop()
    {
        SetupContentForTopLeftLayout();

        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = 1f;
        content.anchoredPosition = Vector2.zero;
    }

    private void Subscribe()
    {
        if (subscribed || scrollRect == null)
            return;

        scrollRect.onValueChanged.AddListener(OnScroll);
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || scrollRect == null)
            return;

        scrollRect.onValueChanged.RemoveListener(OnScroll);
        subscribed = false;
    }

    private void OnScroll(Vector2 value)
    {
        UpdateVisibleRows();
    }

    private void CleanupDestroyedPoolItems()
    {
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            if (pool[i] == null || pool[i].gameObject == null)
                pool.RemoveAt(i);
        }
    }

    private void DestroyForeignChildren()
    {
        if (content == null)
            return;

        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform child = content.GetChild(i);
            if (child == null)
                continue;

            bool belongsToPool = false;

            for (int j = 0; j < pool.Count; j++)
            {
                if (pool[j] != null && pool[j].gameObject == child.gameObject)
                {
                    belongsToPool = true;
                    break;
                }
            }

            if (!belongsToPool)
                Destroy(child.gameObject);
        }
    }

    private void EnsurePool()
    {
        while (pool.Count < poolSize)
            pool.Add(CreateItemView());

        while (pool.Count > poolSize)
        {
            ItemView last = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);

            if (last != null && last.gameObject != null)
                Destroy(last.gameObject);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            bool shouldBeActive = i < poolSize;
            if (pool[i] != null)
            {
                SetViewVisible(pool[i], shouldBeActive);
                if (!shouldBeActive)
                {
                    pool[i].boundDataIndex = -1;
                    pool[i].currentRow = -1;
                    pool[i].currentColumn = -1;
                }
            }
        }
    }

    private ItemView CreateItemView()
    {
        GameObject obj = Instantiate(itemPrefab, content);
        RectTransform rootRect = obj.GetComponent<RectTransform>();

        if (rootRect != null)
        {
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.sizeDelta = cellSize;
        }

        ItemView view = new ItemView
        {
            gameObject = obj,
            rootRect = rootRect,
            canvasGroup = obj.GetComponent<CanvasGroup>() ?? obj.AddComponent<CanvasGroup>()
        };

        Transform root = obj.transform;
        view.rootImage = string.IsNullOrWhiteSpace(rootImagePath)
            ? obj.GetComponent<Image>()
            : FindChild(root, rootImagePath)?.GetComponent<Image>();

        Transform modelRoot = FindChild(root, modelPath);
        Transform numRoot = FindChild(root, numPath);
        view.patternRoot = FindChild(root, patternPath) as RectTransform;

        view.modelImage = modelRoot != null ? modelRoot.GetComponent<Image>() : null;
        view.numImage = numRoot != null ? numRoot.GetComponent<Image>() : null;

        if (numRoot != null)
        {
            view.legacyText = numRoot.GetComponent<Text>();
            if (view.legacyText == null)
                view.legacyText = numRoot.GetComponentInChildren<Text>(true);

            view.tmpText = numRoot.GetComponent<TMP_Text>();
            if (view.tmpText == null)
                view.tmpText = numRoot.GetComponentInChildren<TMP_Text>(true);
        }

        if (view.rootImage != null && inventoryItemParentMaterial != null)
        {
            view.rootMaterial = new Material(inventoryItemParentMaterial);
            view.rootImage.material = view.rootMaterial;
            view.rootImage.color = Color.white;
        }

        view.button = obj.GetComponent<Button>();
        if (view.button != null)
            view.button.onClick.AddListener(() => HandleItemClicked(view));

        InitializePatternCache(view);
        return view;
    }

    private void HandleItemClicked(ItemView view)
    {
        if (view == null)
            return;

        int dataIndex = view.boundDataIndex;
        if (dataIndex < 0 || dataIndex >= items.Count)
            return;

        InventoryManager.InventoryEntry entry = items[dataIndex];
        if (entry == null)
            return;

        ItemClicked?.Invoke(entry);
    }

    private void InitializePatternCache(ItemView view)
    {
        if (view == null || view.patternRoot == null)
            return;

        for (int i = view.patternRoot.childCount - 1; i >= 0; i--)
            Destroy(view.patternRoot.GetChild(i).gameObject);

        view.patternImages = new Image[InventoryPattern.Points.Length];
        view.patternMaterials = new Material[InventoryPattern.Points.Length];

        for (int i = 0; i < InventoryPattern.Points.Length; i++)
        {
            PatternPoint point = InventoryPattern.Points[i];

            GameObject go = new GameObject("Pattern_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(view.patternRoot, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = point.position;
            rt.anchorMax = point.position;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(basePatternSize * point.scale, basePatternSize * point.scale);
            rt.localScale = Vector3.one;

            Image img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;

            Material mat = patternMaterial != null ? new Material(patternMaterial) : null;
            if (mat != null)
                img.material = mat;

            view.patternImages[i] = img;
            view.patternMaterials[i] = mat;
        }
    }

    private void InitializeVisibleRows()
    {
        RebindVisibleWindow(GetTargetTopRow());
    }

    private void UpdateVisibleRows()
    {
        if (columnCount <= 0 || pooledRows <= 0 || rowHeight <= 0f)
            return;

        if (pool.Count == 0)
            return;

        int targetTopRow = GetTargetTopRow();

        if (currentTopRow == -1)
        {
            InitializeVisibleRows();
            return;
        }

        if (targetTopRow == currentTopRow)
            return;

        int deltaRows = targetTopRow - currentTopRow;

        if (Mathf.Abs(deltaRows) >= pooledRows)
        {
            RebindVisibleWindow(targetTopRow);
            return;
        }

        if (deltaRows > 0)
        {
            int newBottomRow = targetTopRow + pooledRows - 1;
            for (int row = currentBottomRow + 1; row <= newBottomRow; row++)
                RebindDataRow(row);
        }
        else
        {
            for (int row = targetTopRow; row < currentTopRow; row++)
                RebindDataRow(row);
        }

        currentTopRow = targetTopRow;
        currentBottomRow = targetTopRow + pooledRows - 1;
    }

    private int GetTargetTopRow()
    {
        float scrollY = Mathf.Max(0f, content.anchoredPosition.y - topPadding);
        int firstVisibleRow = Mathf.FloorToInt(scrollY / Mathf.Max(1f, rowHeight));
        int targetTop = Mathf.Max(0, firstVisibleRow - extraRows);

        int maxTop = Mathf.Max(0, totalRows - pooledRows);
        targetTop = Mathf.Min(targetTop, maxTop);

        return targetTop;
    }

    private void RebindVisibleWindow(int topRow)
    {
        if (pool.Count == 0)
            return;

        currentTopRow = topRow;
        currentBottomRow = topRow + pooledRows - 1;

        for (int dataRow = currentTopRow; dataRow <= currentBottomRow; dataRow++)
            RebindDataRow(dataRow);
    }

    private void RebindDataRow(int dataRow)
    {
        int pooledRow = GetPooledRowIndex(dataRow);

        for (int col = 0; col < columnCount; col++)
        {
            int poolIndex = pooledRow * columnCount + col;
            if (poolIndex < 0 || poolIndex >= pool.Count)
                continue;

            BindViewToRowAndColumn(pool[poolIndex], dataRow, col);
        }
    }

    private int GetPooledRowIndex(int dataRow)
    {
        if (pooledRows <= 0)
            return 0;

        int pooledRow = dataRow % pooledRows;
        if (pooledRow < 0)
            pooledRow += pooledRows;

        return pooledRow;
    }

    private void BindViewToRowAndColumn(ItemView view, int row, int column)
    {
        if (view == null || view.rootRect == null)
            return;

        view.currentRow = row;
        view.currentColumn = column;

        float x = GetHorizontalStartOffset() + column * (cellSize.x + spacing.x);
        float y = -(topPadding + row * (cellSize.y + spacing.y));

        view.rootRect.anchoredPosition = new Vector2(x, y);
        view.rootRect.sizeDelta = cellSize;

        int dataIndex = row * columnCount + column;

        if (row < 0 || row >= totalRows || dataIndex < 0 || dataIndex >= items.Count || items[dataIndex] == null)
        {
            view.boundDataIndex = -1;
            ClearView(view);
            return;
        }

        SetViewVisible(view, true);

        if (view.boundDataIndex != dataIndex)
        {
            Bind(view, items[dataIndex]);
            view.boundDataIndex = dataIndex;
        }
    }

    private void Bind(ItemView view, InventoryManager.InventoryEntry data)
    {
        if (view == null || data == null)
            return;

        InventoryManager.BackgroundItemData bg = inventoryManager != null
            ? inventoryManager.GetBackgroundForUI(data.backgroundId, data.backgroundName)
            : null;

        Sprite modelSprite = inventoryManager != null
            ? inventoryManager.GetModelSpriteForUI(data.modelId, data.modelName)
            : null;

        Sprite patternSprite = inventoryManager != null
            ? inventoryManager.GetPatternSpriteForUI(data.patternName)
            : null;

        string centerHex = bg != null && bg.hex != null ? bg.hex.centerColor : "#FFFFFF";
        string edgeHex = bg != null && bg.hex != null ? bg.hex.edgeColor : "#FFFFFF";
        string patternHex = bg != null && bg.hex != null ? bg.hex.patternColor : "#FFFFFF";

        SetNumber(view, data.inventoryNumber, edgeHex);
        SetRootMaterial(view, centerHex, edgeHex);
        SetModel(view, modelSprite);
        SetPattern(view, patternSprite, patternHex);
    }

    private void SetNumber(ItemView view, int itemNumber, string edgeHex)
    {
        string value = "#" + itemNumber;
        Color numberColor = HexToColor(edgeHex, Color.white);

        if (view.numImage != null)
        {
            view.numImage.enabled = true;
            view.numImage.color = numberColor;
        }

        if (view.legacyText != null)
            view.legacyText.text = value;

        if (view.tmpText != null)
            view.tmpText.text = value;
    }

    private void SetRootMaterial(ItemView view, string centerHex, string edgeHex)
    {
        if (view.rootImage != null)
            view.rootImage.enabled = true;

        if (view.rootMaterial == null)
            return;

        if (view.rootMaterial.HasProperty("_CenterColor"))
            view.rootMaterial.SetColor("_CenterColor", HexToColor(centerHex, Color.white));

        if (view.rootMaterial.HasProperty("_EdgeColor"))
            view.rootMaterial.SetColor("_EdgeColor", HexToColor(edgeHex, Color.white));
    }

    private void SetModel(ItemView view, Sprite modelSprite)
    {
        if (view.modelImage == null)
            return;

        view.modelImage.sprite = modelSprite;
        view.modelImage.enabled = modelSprite != null;
        view.modelImage.preserveAspect = preserveAspect;

        RectTransform rt = view.modelImage.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = modelPosition;
        rt.sizeDelta = modelSize;
        rt.localScale = Vector3.one;
    }

    private void SetPattern(ItemView view, Sprite patternSprite, string patternHex)
    {
        if (view.patternImages == null)
            return;

        Color patternColor = HexToColor(patternHex, Color.white);

        for (int i = 0; i < view.patternImages.Length; i++)
        {
            Image img = view.patternImages[i];
            if (img == null)
                continue;

            PatternPoint point = InventoryPattern.Points[i];
            img.enabled = patternSprite != null;
            img.sprite = patternSprite;

            Material mat = view.patternMaterials != null && i < view.patternMaterials.Length
                ? view.patternMaterials[i]
                : null;

            Color finalColor = new Color(patternColor.r, patternColor.g, patternColor.b, point.opacity);

            if (mat != null && mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", finalColor);
                img.color = Color.white;
            }
            else
            {
                img.color = finalColor;
            }
        }
    }

    private void SetViewVisible(ItemView view, bool visible)
    {
        if (view == null || view.gameObject == null || view.canvasGroup == null)
            return;

        view.canvasGroup.alpha = visible ? 1f : 0f;
        view.canvasGroup.blocksRaycasts = visible;
        view.canvasGroup.interactable = visible;
    }

    private void ClearView(ItemView view)
    {
        if (view == null)
            return;

        SetViewVisible(view, false);

        if (view.rootImage != null)
            view.rootImage.enabled = false;

        if (view.modelImage != null)
        {
            view.modelImage.sprite = null;
            view.modelImage.enabled = false;
        }

        if (view.numImage != null)
            view.numImage.enabled = false;

        if (view.legacyText != null)
            view.legacyText.text = string.Empty;

        if (view.tmpText != null)
            view.tmpText.text = string.Empty;

        if (view.patternImages != null)
        {
            for (int i = 0; i < view.patternImages.Length; i++)
            {
                if (view.patternImages[i] != null)
                {
                    view.patternImages[i].sprite = null;
                    view.patternImages[i].enabled = false;
                }
            }
        }
    }

    private Transform FindChild(Transform root, string path)
    {
        if (root == null)
            return null;

        if (string.IsNullOrWhiteSpace(path))
            return root;

        return root.Find(path);
    }

    private Color HexToColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        string value = hex.Trim();
        if (!value.StartsWith("#", StringComparison.Ordinal))
            value = "#" + value;

        if (ColorUtility.TryParseHtmlString(value, out Color color))
            return color;

        return fallback;
    }

    private float GetGridWidth()
    {
        return columnCount * cellSize.x + Mathf.Max(0, columnCount - 1) * spacing.x;
    }

    private float GetHorizontalStartOffset()
    {
        if (viewport == null)
            return 0f;

        float layoutWidth = GetReferenceLayoutWidth(viewport.rect.width);
        return Mathf.Max(0f, (layoutWidth - GetGridWidth()) * 0.5f);
    }

    private float GetReferenceLayoutWidth(float fallbackWidth)
    {
        if (referenceLayoutWidth > 0f)
            return referenceLayoutWidth;

        if (content != null && content.rect.width > 0f)
            return content.rect.width;

        return Mathf.Max(0f, fallbackWidth);
    }

    private float GetBottomEndPadding()
    {
        return Mathf.Max(0f, bottomEndPadding);
    }

}


