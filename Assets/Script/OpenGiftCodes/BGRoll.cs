using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BGRoll : MonoBehaviour
{
    [Header("Source")]
    public ColorSpawner spawner;

    [Header("UI")]
    public RectTransform content;
    public VerticalLayoutGroup layout;
    public Button rollButton;

    [Header("Skip")]
    public Button skipButton;

    [Header("Visible Rows")]
    public int visibleRows = 3;

    [Header("Roll")]
    public int steps = 20;
    public float stepDuration = 0.05f;

    private float itemHeight;
    private float currentY;
    private bool rolling;

    private readonly List<string> names = new List<string>();
    private readonly List<string> percents = new List<string>();
    private int headIndex = 0;

    private bool pendingRoll = false;

    private Coroutine rollCoroutine;
    private int rollTargetHead = 0;
    private bool missingSpawnerWarningLogged;

    private void Awake()
    {
        if (content == null)
        {
            Debug.LogError("[BGRoll] content is NULL (assign in inspector).", this);
            return;
        }

        if (!layout) layout = content.GetComponent<VerticalLayoutGroup>();
        if (rollButton) rollButton.onClick.AddListener(StartRoll);
        if (skipButton) skipButton.onClick.AddListener(SkipRoll);

        RebuildLayout();
        EnsureRows();
        ApplyVisibleNames();
        ResetContentPosition();

        SetSkipInteractable(false);
    }

    private void OnEnable()
    {
        if (spawner != null)
            spawner.OnRollDataChanged += HandleRollDataChanged;
        else
            LogMissingSpawnerWarning();
    }

    private void OnDisable()
    {
        if (spawner != null)
            spawner.OnRollDataChanged -= HandleRollDataChanged;
    }

    private void HandleRollDataChanged(ColorSpawner.RollData data)
    {
        names.Clear();
        percents.Clear();

        if (data != null && data.slotNames != null)
        {
            for (int i = 0; i < data.slotNames.Count; i++)
            {
                var n = data.slotNames[i];
                if (string.IsNullOrWhiteSpace(n)) continue;

                names.Add(n);

                float p = spawner != null ? spawner.GetChancePercentByName(n) : -1f;
                percents.Add(p < 0f ? "-" : p.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%");
            }
        }

        headIndex = 0;

        EnsureRows();
        ApplyVisibleNames();
        ResetContentPosition();

        if (pendingRoll)
        {
            pendingRoll = false;
            StartRoll();
        }
    }

    public void StartRoll()
    {
        if (rolling) return;

        if (names.Count == 0)
        {
            pendingRoll = true;
            if (spawner == null)
                LogMissingSpawnerWarning();
            return;
        }

        RebuildLayout();
        EnsureRows();
        ApplyVisibleNames();
        ResetContentPosition();

        rollTargetHead = Mod(headIndex + steps, names.Count);

        SetSkipInteractable(true);

        if (rollCoroutine != null) StopCoroutine(rollCoroutine);
        rollCoroutine = StartCoroutine(Roll());
    }

    public void SkipRoll()
    {
        if (!rolling) return;

        if (rollCoroutine != null)
        {
            StopCoroutine(rollCoroutine);
            rollCoroutine = null;
        }

        headIndex = rollTargetHead;

        ApplyVisibleNames();
        ResetContentPosition();

        rolling = false;
        SetSkipInteractable(false);
    }

    private void SetSkipInteractable(bool v)
    {
    }

    private void ResetContentPosition()
    {
        currentY = 0f;
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, currentY);
    }

    private IEnumerator Roll()
    {
        rolling = true;

        for (int i = 0; i < steps; i++)
        {
            yield return MoveOne();
            Recycle();
        }

        headIndex = rollTargetHead;

        ApplyVisibleNames();
        ResetContentPosition();

        rolling = false;
        SetSkipInteractable(false);
    }

    private IEnumerator MoveOne()
    {
        float start = currentY;
        float end = currentY + itemHeight;

        float t = 0f;
        float dur = Mathf.Max(0.0001f, stepDuration);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float y = Mathf.Lerp(start, end, k);

            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
            yield return null;
        }

        currentY = end;
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, currentY);
    }

    private void Recycle()
    {
        if (content.childCount <= 0) return;
        if (names.Count == 0) return;

        if (layout) layout.enabled = false;

        RectTransform first = content.GetChild(0) as RectTransform;
        first.SetSiblingIndex(content.childCount - 1);

        headIndex = Mod(headIndex + 1, names.Count);

        int bottomIndex = Mod(headIndex + visibleRows - 1, names.Count);
        SetRowTexts(first, names[bottomIndex], percents.Count == names.Count ? percents[bottomIndex] : "-");

        currentY -= itemHeight;
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, currentY);

        if (layout) layout.enabled = true;
    }

    private void ApplyVisibleNames()
    {
        EnsureRows();
        if (names.Count == 0) return;

        for (int i = 0; i < visibleRows; i++)
        {
            int idx = Mod(headIndex + i, names.Count);
            string p = (percents.Count == names.Count) ? percents[idx] : "-";
            SetRowTexts(content.GetChild(i), names[idx], p);
        }
    }

    private void EnsureRows()
    {
        if (content == null) return;
        if (content.childCount == 0) return;

        while (content.childCount < visibleRows)
        {
            var clone = Instantiate(content.GetChild(0).gameObject, content);
            clone.name = content.GetChild(0).name + "_Clone";
        }

        RebuildLayout();
    }

    private void RebuildLayout()
    {
        if (content == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        if (content.childCount > 0)
        {
            RectTransform first = content.GetChild(0) as RectTransform;
            float spacing = layout != null ? layout.spacing : 0f;
            itemHeight = first.rect.height + spacing;
        }
    }

    private void SetRowTexts(Transform row, string nameValue, string percentValue)
    {
        if (!row) return;

        var tmps = row.GetComponentsInChildren<TMP_Text>(true);
        if (tmps != null && tmps.Length > 0)
        {
            tmps[0].text = nameValue;
            if (tmps.Length > 1) tmps[1].text = percentValue;
            return;
        }

        var txts = row.GetComponentsInChildren<Text>(true);
        if (txts != null && txts.Length > 0)
        {
            txts[0].text = nameValue;
            if (txts.Length > 1) txts[1].text = percentValue;
            return;
        }
    }

    private int Mod(int a, int m)
    {
        if (m <= 0) return 0;
        int r = a % m;
        return r < 0 ? r + m : r;
    }

    private void LogMissingSpawnerWarning()
    {
        if (missingSpawnerWarningLogged)
            return;

        missingSpawnerWarningLogged = true;
        Debug.LogWarning("[BGRoll] Spawner not found. Assign 'spawner' in inspector if this roll should animate.", this);
    }
}
