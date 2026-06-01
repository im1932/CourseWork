using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LazyLoader : MonoBehaviour
{
    private const float EnableValidationDuration = 0.35f;

    [Header("Refs")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private RectTransform viewport;

    [Header("Window")]
    [SerializeField] private bool refreshOnEnable = true;
    [SerializeField] private int initialVisibleCount = 30;
    [SerializeField] private bool includeInitiallyInactiveChildren = true;
    [SerializeField] private bool resetScrollToTopOnRefresh = true;

    private readonly List<RectTransform> itemRects = new List<RectTransform>();
    private Vector2 cellSize = new Vector2(100f, 100f);
    private Vector2 spacing = Vector2.zero;
    private RectOffset padding = new RectOffset();
    private GridLayoutGroup activeGridLayout;
    private GridLayoutGroup.Constraint gridConstraint = GridLayoutGroup.Constraint.Flexible;
    private int gridConstraintCount = 1;
    private TextAnchor childAlignment = TextAnchor.UpperLeft;
    private int columnCount = 1;
    private int totalRows = 0;
    private float rowHeight = 100f;
    private int currentTopRow = int.MinValue;
    private int currentBottomRow = int.MinValue;
    private bool subscribed;
    private bool hasInitialized;
    private bool pendingEnableValidation;
    private float enableValidationUntilTime = -1f;
    private int cachedChildCount = -1;
    private Vector2 cachedViewportSize = new Vector2(-1f, -1f);

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        pendingEnableValidation = refreshOnEnable;
        enableValidationUntilTime = refreshOnEnable
            ? Time.unscaledTime + EnableValidationDuration
            : -1f;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void LateUpdate()
    {
        if (!isActiveAndEnabled)
            return;

        bool shouldValidateAfterEnable =
            pendingEnableValidation ||
            (enableValidationUntilTime >= 0f && Time.unscaledTime <= enableValidationUntilTime);

        if (shouldValidateAfterEnable)
        {
            pendingEnableValidation = false;
            ValidateAfterEnable();
        }

        UpdateVisibleWindow();
    }

    public void Refresh()
    {
        ResolveReferences();
        CacheChildren();
        CacheLayout();
        DisableConflictingLayoutComponents();
        PrepareContentRoot();

        if (resetScrollToTopOnRefresh && scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
            if (contentRoot != null)
                contentRoot.anchoredPosition = Vector2.zero;
        }

        RebuildVirtualLayout();
        currentTopRow = int.MinValue;
        currentBottomRow = int.MinValue;
        UpdateVisibleWindow(force: true);
        hasInitialized = true;
        cachedChildCount = GetContentChildCount();
        cachedViewportSize = viewport != null ? viewport.rect.size : Vector2.zero;
    }

    public void SetupRuntime(
        ScrollRect runtimeScrollRect,
        RectTransform runtimeContentRoot,
        RectTransform runtimeViewport,
        int runtimeInitialVisibleCount,
        bool runtimeIncludeInitiallyInactiveChildren,
        bool runtimeResetScrollToTopOnRefresh)
    {
        scrollRect = runtimeScrollRect;
        contentRoot = runtimeContentRoot;
        viewport = runtimeViewport;
        initialVisibleCount = runtimeInitialVisibleCount;
        includeInitiallyInactiveChildren = runtimeIncludeInitiallyInactiveChildren;
        resetScrollToTopOnRefresh = runtimeResetScrollToTopOnRefresh;
    }

    private void ResolveReferences()
    {
        if (scrollRect == null)
            scrollRect = GetComponentInParent<ScrollRect>(true);

        if (viewport == null && scrollRect != null)
            viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.GetComponent<RectTransform>();

        if (contentRoot == null)
        {
            if (scrollRect != null && scrollRect.content != null)
                contentRoot = ResolveBestContentRoot(scrollRect.content);
            else
                contentRoot = ResolveBestContentRoot(transform as RectTransform);
        }
        else
        {
            contentRoot = ResolveBestContentRoot(contentRoot);
        }
    }

    private static RectTransform ResolveBestContentRoot(RectTransform root)
    {
        if (root == null)
            return null;

        if (HasDirectVisualChildren(root))
            return root;

        if (root.childCount == 1 && root.GetChild(0) is RectTransform onlyChild)
            return onlyChild;

        GridLayoutGroup grid = root.GetComponentInChildren<GridLayoutGroup>(true);
        if (grid != null)
            return grid.GetComponent<RectTransform>();

        return root;
    }

    private static bool HasDirectVisualChildren(RectTransform root)
    {
        if (root == null)
            return false;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            if (child.GetComponent<LayoutGroup>() == null &&
                child.GetComponent<ContentSizeFitter>() == null)
            {
                return true;
            }
        }

        return false;
    }

    private void CacheChildren()
    {
        itemRects.Clear();
        if (contentRoot == null)
            return;

        for (int i = 0; i < contentRoot.childCount; i++)
        {
            RectTransform child = contentRoot.GetChild(i) as RectTransform;
            if (child == null)
                continue;

            if (!includeInitiallyInactiveChildren && !child.gameObject.activeSelf)
                continue;

            itemRects.Add(child);
        }
    }

    private void CacheLayout()
    {
        if (contentRoot == null)
            return;

        activeGridLayout = contentRoot.GetComponent<GridLayoutGroup>();
        if (activeGridLayout != null)
        {
            cellSize = activeGridLayout.cellSize;
            spacing = activeGridLayout.spacing;
            gridConstraint = activeGridLayout.constraint;
            gridConstraintCount = Mathf.Max(1, activeGridLayout.constraintCount);
            childAlignment = activeGridLayout.childAlignment;
            padding = activeGridLayout.padding != null
                ? new RectOffset(activeGridLayout.padding.left, activeGridLayout.padding.right, activeGridLayout.padding.top, activeGridLayout.padding.bottom)
                : new RectOffset();
        }
        else
        {
            if (itemRects.Count > 0 && itemRects[0] != null)
                cellSize = itemRects[0].rect.size;

            spacing = Vector2.zero;
            gridConstraint = GridLayoutGroup.Constraint.Flexible;
            gridConstraintCount = 1;
            childAlignment = TextAnchor.UpperLeft;
            padding = new RectOffset();
        }
    }

    private void DisableConflictingLayoutComponents()
    {
        if (contentRoot == null)
            return;

        DisableIfPresent<GridLayoutGroup>(contentRoot);
        DisableIfPresent<HorizontalLayoutGroup>(contentRoot);
        DisableIfPresent<VerticalLayoutGroup>(contentRoot);
        DisableIfPresent<ContentSizeFitter>(contentRoot);
        DisableIfPresent<AutoScrollHeight>(contentRoot);
    }

    private static void DisableIfPresent<T>(RectTransform root) where T : Behaviour
    {
        if (root == null)
            return;

        T component = root.GetComponent<T>();
        if (component != null && component.enabled)
            component.enabled = false;
    }

    private void PrepareContentRoot()
    {
        if (contentRoot == null)
            return;

        contentRoot.anchorMin = new Vector2(0f, 1f);
        contentRoot.anchorMax = new Vector2(0f, 1f);
        contentRoot.pivot = new Vector2(0f, 1f);
    }

    private void RebuildVirtualLayout()
    {
        if (contentRoot == null || viewport == null)
            return;

        float viewportWidth = viewport.rect.width;
        float fullCellWidth = Mathf.Max(1f, cellSize.x + spacing.x);

        switch (gridConstraint)
        {
            case GridLayoutGroup.Constraint.FixedColumnCount:
                columnCount = Mathf.Max(1, gridConstraintCount);
                break;

            case GridLayoutGroup.Constraint.FixedRowCount:
                int rowCount = Mathf.Max(1, gridConstraintCount);
                columnCount = Mathf.Max(1, Mathf.CeilToInt(itemRects.Count / (float)rowCount));
                break;

            default:
                columnCount = Mathf.Max(1, Mathf.FloorToInt((viewportWidth - padding.left - padding.right + spacing.x) / fullCellWidth));
                break;
        }

        totalRows = Mathf.CeilToInt(itemRects.Count / (float)columnCount);
        rowHeight = Mathf.Max(1f, cellSize.y + spacing.y);

        float gridWidth = columnCount * cellSize.x + Mathf.Max(0, columnCount - 1) * spacing.x;
        float contentHeight = padding.top + padding.bottom + totalRows * cellSize.y + Mathf.Max(0, totalRows - 1) * spacing.y;
        float contentWidth = Mathf.Max(viewportWidth, padding.left + padding.right + gridWidth);
        contentRoot.sizeDelta = new Vector2(contentWidth, Mathf.Max(0f, contentHeight));

        float availableWidth = Mathf.Max(0f, contentWidth - padding.left - padding.right - gridWidth);
        float availableHeight = Mathf.Max(0f, contentRoot.sizeDelta.y - padding.top - padding.bottom - (totalRows * cellSize.y + Mathf.Max(0, totalRows - 1) * spacing.y));

        float startX = padding.left + GetHorizontalAlignmentOffset(childAlignment, availableWidth);
        float startY = -padding.top - GetVerticalAlignmentOffset(childAlignment, availableHeight);

        for (int i = 0; i < itemRects.Count; i++)
        {
            RectTransform itemRect = itemRects[i];
            if (itemRect == null)
                continue;

            int row = i / columnCount;
            int column = i % columnCount;

            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(0f, 1f);
            itemRect.pivot = new Vector2(0f, 1f);
            itemRect.sizeDelta = cellSize;
            itemRect.anchoredPosition = new Vector2(
                startX + column * (cellSize.x + spacing.x),
                startY - row * (cellSize.y + spacing.y));
        }

        Canvas.ForceUpdateCanvases();
    }

    private static float GetHorizontalAlignmentOffset(TextAnchor alignment, float availableSpace)
    {
        switch (alignment)
        {
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter:
                return availableSpace * 0.5f;

            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                return availableSpace;

            default:
                return 0f;
        }
    }

    private static float GetVerticalAlignmentOffset(TextAnchor alignment, float availableSpace)
    {
        switch (alignment)
        {
            case TextAnchor.MiddleLeft:
            case TextAnchor.MiddleCenter:
            case TextAnchor.MiddleRight:
                return availableSpace * 0.5f;

            case TextAnchor.LowerLeft:
            case TextAnchor.LowerCenter:
            case TextAnchor.LowerRight:
                return availableSpace;

            default:
                return 0f;
        }
    }

    private void UpdateVisibleWindow(bool force = false)
    {
        if (contentRoot == null || viewport == null || itemRects.Count == 0)
            return;

        int visibleRowBudget = Mathf.Max(1, Mathf.CeilToInt(initialVisibleCount / (float)Mathf.Max(1, columnCount)));

        float scrollOffsetY = Mathf.Max(0f, contentRoot.anchoredPosition.y - padding.top);
        int topVisibleRow = Mathf.Max(0, Mathf.FloorToInt(scrollOffsetY / rowHeight));
        int topRow = Mathf.Clamp(topVisibleRow, 0, Mathf.Max(0, totalRows - visibleRowBudget));
        int bottomRow = Mathf.Min(totalRows - 1, topRow + visibleRowBudget - 1);

        if (!force && topRow == currentTopRow && bottomRow == currentBottomRow)
            return;

        currentTopRow = topRow;
        currentBottomRow = bottomRow;

        for (int i = 0; i < itemRects.Count; i++)
        {
            RectTransform itemRect = itemRects[i];
            if (itemRect == null)
                continue;

            int row = i / columnCount;
            bool shouldBeActive = row >= currentTopRow && row <= currentBottomRow;

            if (itemRect.gameObject.activeSelf != shouldBeActive)
            {
                itemRect.gameObject.SetActive(shouldBeActive);
            }
        }
    }

    private void Subscribe()
    {
        if (subscribed || scrollRect == null)
            return;

        scrollRect.onValueChanged.AddListener(OnScrollChanged);
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || scrollRect == null)
            return;

        scrollRect.onValueChanged.RemoveListener(OnScrollChanged);
        subscribed = false;
    }

    private void OnScrollChanged(Vector2 _)
    {
        UpdateVisibleWindow();
    }

    private void ValidateAfterEnable()
    {
        Canvas.ForceUpdateCanvases();
        ForceRebuildIfAvailable(viewport);
        ForceRebuildIfAvailable(contentRoot);

        if (!hasInitialized || ShouldDoFullRefresh())
        {
            Refresh();
            return;
        }

        UpdateVisibleWindow(force: true);
        RefreshVisibleGraphics();
    }

    private bool ShouldDoFullRefresh()
    {
        if (!hasInitialized)
            return true;

        ResolveReferences();

        if (contentRoot == null || viewport == null)
            return true;

        int childCount = GetContentChildCount();
        if (childCount != cachedChildCount)
            return true;

        Vector2 viewportSize = viewport.rect.size;
        if (!Mathf.Approximately(viewportSize.x, cachedViewportSize.x) ||
            !Mathf.Approximately(viewportSize.y, cachedViewportSize.y))
        {
            return true;
        }

        return false;
    }

    private int GetContentChildCount()
    {
        if (contentRoot == null)
            return 0;

        int count = 0;
        for (int i = 0; i < contentRoot.childCount; i++)
        {
            RectTransform child = contentRoot.GetChild(i) as RectTransform;
            if (child == null)
                continue;

            count++;
        }

        return count;
    }

    private static void ForceRebuildIfAvailable(RectTransform target)
    {
        if (target == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(target);
    }

    private void RefreshVisibleGraphics()
    {
        for (int i = 0; i < itemRects.Count; i++)
        {
            RectTransform itemRect = itemRects[i];
            if (itemRect == null || !itemRect.gameObject.activeInHierarchy)
                continue;

            Graphic[] graphics = itemRect.GetComponentsInChildren<Graphic>(true);
            for (int graphicIndex = 0; graphicIndex < graphics.Length; graphicIndex++)
            {
                Graphic graphic = graphics[graphicIndex];
                if (graphic == null)
                    continue;

                graphic.SetVerticesDirty();
                graphic.SetMaterialDirty();
            }
        }
    }
}
