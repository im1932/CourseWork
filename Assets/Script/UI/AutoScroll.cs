using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(RectTransform))]
public class AutoScrollHeight : MonoBehaviour
{
    [SerializeField] private int extraRows = 1;
    [SerializeField] private float extraBottomSpace = 0f;

    private GridLayoutGroup grid;
    private RectTransform rectTransform;

    private void Awake()
    {
        grid = GetComponent<GridLayoutGroup>();
        rectTransform = GetComponent<RectTransform>();
        UpdateHeight();
    }

    private void OnEnable()
    {
        UpdateHeight();
    }

    private void OnTransformChildrenChanged()
    {
        UpdateHeight();
    }

    public void UpdateHeight()
    {
        if (grid == null)
            grid = GetComponent<GridLayoutGroup>();

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        int childCount = transform.childCount;

        int columns = Mathf.Max(1, GetColumnCount());
        int rows = Mathf.CeilToInt((float)childCount / columns);
        rows = Mathf.Max(1, rows + extraRows);

        float height =
            grid.padding.top +
            grid.padding.bottom +
            rows * grid.cellSize.y +
            Mathf.Max(0, rows - 1) * grid.spacing.y +
            extraBottomSpace;

        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
    }

    private int GetColumnCount()
    {
        float width = rectTransform.rect.width;
        float usableWidth = width - grid.padding.left - grid.padding.right + grid.spacing.x;
        float cellWidth = grid.cellSize.x + grid.spacing.x;

        if (cellWidth <= 0f)
            return 1;

        return Mathf.Max(1, Mathf.FloorToInt(usableWidth / cellWidth));
    }
}