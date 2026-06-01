using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BarAnim : MonoBehaviour
{
    private static readonly List<BarAnim> AllButtons = new List<BarAnim>();

    [Header("Click Source")]
    [SerializeField] private Button clickButton;

    [Header("Highlight")]
    [SerializeField] private RectTransform highlight;
    [SerializeField] private CanvasGroup highlightCanvasGroup;

    [Header("Image Swap")]
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite selectedSprite;

    [Header("Text Color")]
    [SerializeField] private TMP_Text targetText;
    private Color selectedColor = new Color32(14, 139, 253, 255);
    private Color normalColor = Color.white;

    [Header("Select Animation")]
    [SerializeField] private float selectStartScale = 0.9f;
    [SerializeField] private float selectEndScale = 1f;
    [SerializeField] private float selectDuration = 0.12f;

    [Header("Deselect Animation")]
    [SerializeField] private float deselectStartScale = 1f;
    [SerializeField] private float deselectEndScale = 0.5f;
    [SerializeField] private float deselectScaleDuration = 0.08f;
    [SerializeField] private float deselectFadeDuration = 0.12f;

    [Header("State")]
    [SerializeField] private bool selectOnStart = false;

    private Coroutine animRoutine;
    private bool isSelected;

    private void Awake()
    {
        if (!AllButtons.Contains(this))
            AllButtons.Add(this);

        if (highlight != null)
        {
            if (highlightCanvasGroup == null)
                highlightCanvasGroup = highlight.GetComponent<CanvasGroup>();

            if (highlightCanvasGroup == null)
                highlightCanvasGroup = highlight.gameObject.AddComponent<CanvasGroup>();

            highlight.gameObject.SetActive(selectOnStart);
            highlight.localScale = Vector3.one * (selectOnStart ? selectEndScale : deselectEndScale);
            highlightCanvasGroup.alpha = selectOnStart ? 1f : 0f;
        }

        if (targetImage != null)
            targetImage.sprite = selectOnStart ? selectedSprite : normalSprite;

        if (targetText != null)
            targetText.color = selectOnStart ? selectedColor : normalColor;

        isSelected = selectOnStart;

        if (clickButton != null)
        {
            clickButton.onClick.RemoveListener(OnClick);
            clickButton.onClick.AddListener(OnClick);
        }
    }

    private void OnEnable()
    {
        if (!AllButtons.Contains(this))
            AllButtons.Add(this);
    }

    private void OnDisable()
    {
        AllButtons.Remove(this);
    }

    private void OnDestroy()
    {
        AllButtons.Remove(this);

        if (clickButton != null)
            clickButton.onClick.RemoveListener(OnClick);
    }

    private void Start()
    {
        if (selectOnStart)
        {
            for (int i = 0; i < AllButtons.Count; i++)
            {
                if (AllButtons[i] != null && AllButtons[i] != this)
                    AllButtons[i].ForceDeselectImmediate();
            }

            ForceSelectImmediate();
        }
    }

    private void OnClick()
    {
        if (isSelected) return;

        for (int i = 0; i < AllButtons.Count; i++)
        {
            if (AllButtons[i] == null) continue;

            if (AllButtons[i] == this)
                AllButtons[i].SetSelected(true);
            else
                AllButtons[i].SetSelected(false);
        }
    }

    public void SetSelected(bool value)
    {
        if (isSelected == value) return;

        isSelected = value;

        if (targetImage != null)
            targetImage.sprite = value ? selectedSprite : normalSprite;

        if (targetText != null)
            targetText.color = value ? selectedColor : normalColor;

        if (animRoutine != null)
            StopCoroutine(animRoutine);

        animRoutine = StartCoroutine(value ? SelectRoutine() : DeselectRoutine());
    }

    private void ForceSelectImmediate()
    {
        isSelected = true;

        if (targetImage != null)
            targetImage.sprite = selectedSprite;

        if (targetText != null)
            targetText.color = selectedColor;

        if (animRoutine != null)
            StopCoroutine(animRoutine);

        if (highlight != null && highlightCanvasGroup != null)
        {
            highlight.gameObject.SetActive(true);
            highlight.localScale = Vector3.one * selectEndScale;
            highlightCanvasGroup.alpha = 1f;
        }
    }

    private void ForceDeselectImmediate()
    {
        isSelected = false;

        if (targetImage != null)
            targetImage.sprite = normalSprite;

        if (targetText != null)
            targetText.color = normalColor;

        if (animRoutine != null)
            StopCoroutine(animRoutine);

        if (highlight != null && highlightCanvasGroup != null)
        {
            highlight.localScale = Vector3.one * deselectEndScale;
            highlightCanvasGroup.alpha = 0f;
            highlight.gameObject.SetActive(false);
        }
    }

    private IEnumerator SelectRoutine()
    {
        if (highlight == null || highlightCanvasGroup == null) yield break;

        highlight.gameObject.SetActive(true);
        highlightCanvasGroup.alpha = 1f;
        highlight.localScale = Vector3.one * selectStartScale;

        float time = 0f;

        while (time < selectDuration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / selectDuration);
            t = EaseOutBack(t);

            float scale = Mathf.LerpUnclamped(selectStartScale, selectEndScale, t);
            highlight.localScale = Vector3.one * scale;

            yield return null;
        }

        highlight.localScale = Vector3.one * selectEndScale;
        highlightCanvasGroup.alpha = 1f;
        animRoutine = null;
    }

    private IEnumerator DeselectRoutine()
    {
        if (highlight == null || highlightCanvasGroup == null) yield break;

        highlight.gameObject.SetActive(true);
        highlightCanvasGroup.alpha = 1f;
        highlight.localScale = Vector3.one * deselectStartScale;

        float time = 0f;

        while (time < deselectScaleDuration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / deselectScaleDuration);
            t = EaseInCubic(t);

            float scale = Mathf.Lerp(deselectStartScale, deselectEndScale, t);
            highlight.localScale = Vector3.one * scale;

            yield return null;
        }

        highlight.localScale = Vector3.one * deselectEndScale;

        time = 0f;

        while (time < deselectFadeDuration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / deselectFadeDuration);
            highlightCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t);

            yield return null;
        }

        highlightCanvasGroup.alpha = 0f;
        highlight.gameObject.SetActive(false);
        animRoutine = null;
    }

    private float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private float EaseInCubic(float t)
    {
        return t * t * t;
    }
}