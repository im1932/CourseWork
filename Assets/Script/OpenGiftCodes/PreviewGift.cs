using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using LottiePlugin.UI;

public class RandomSwitcher : MonoBehaviour
{
    private static readonly List<RandomSwitcher> registeredInstances = new List<RandomSwitcher>();
    public static IReadOnlyList<RandomSwitcher> RegisteredInstances => registeredInstances;

    [System.Serializable]
    public class GiftAnimationBinding
    {
        public string giftId;
        public List<TextAsset> animationJsonFiles = new List<TextAsset>();
    }

    [Header("Preview Prefabs")]
    public Transform SpawnPoint;
    public List<GameObject> Prefabs = new List<GameObject>();

    [Header("lottie Animation")]
    public CaseOpeningScroll giftSource;
    public List<GiftAnimationBinding> giftAnimationBindings = new List<GiftAnimationBinding>();

    [Header("Colors")]
    public TextAsset colorJsonFile;
    public Image colorImage;
    public Image colorImage2;
    public Material gradientMaterial;

    [Header("Pattern Settings")]
    public RectTransform patternContainer;
    public Sprite[] patternSprites;
    public Material patternMaterial;
    public float basePatternSize = 64f;

    [Header("Animation Settings")]
    public float fadeDuration = 0.32f;
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Pattern Animation Settings")]
    public float patternChangeDuration = 0.4f;
    public AnimationCurve patternFadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Range(0f, 2f)] public float fadeOutScaleMin = 0f;
    [Range(0f, 2f)] public float fadeInScaleStart = 0f;
    [Range(0f, 2f)] public float fadeInScaleEnd = 1.0f;

    [Header("Settings")]
    public float switchInterval = 4f;

    [Header("Target Container")]
    public Transform targetContainer;

    private GameObject currentItem;
    private GameObject nextItem;

    private ColorDataList colorData;
    private List<GameObject> currentPatternEmojis = new List<GameObject>();
    private List<GameObject> nextPatternEmojis = new List<GameObject>();

    private int lastItemIndex = -1;
    private int lastColorIndex = -1;
    private int nextColorIndex = -1;

    private bool useFirstColorImage = true;
    private System.Random rng = new System.Random();
    private ColorEntry currentColorEntry;
    private Image currentActiveImage;
    private Sprite currentPatternSprite;
    private Color currentPatternColor;

    private Sprite nextPatternSprite;
    private Color nextPatternColor;
    private bool isLockedForever = false;
    private string currentGiftId = "";
    private readonly Dictionary<string, int> lastAnimationIndexByGift = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

    public struct PatternSnapshot
    {
        public Sprite sprite;
        public Color color;
        public PatternSnapshot(Sprite s, Color c) { sprite = s; color = c; }
    }

    public PatternSnapshot GetCurrentPatternSnapshot()
    {
        if (currentPatternSprite == null && patternSprites != null && patternSprites.Length > 0)
            currentPatternSprite = patternSprites[0];

        return new PatternSnapshot(currentPatternSprite, currentPatternColor);
    }

    public void DisablePatternVisuals()
    {
        ClearPatternEmojis(currentPatternEmojis);
        ClearPatternEmojis(nextPatternEmojis);
        currentPatternEmojis = new List<GameObject>();
        nextPatternEmojis = new List<GameObject>();

        if (patternContainer != null)
            patternContainer.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (!registeredInstances.Contains(this))
            registeredInstances.Add(this);
    }

    void Start()
    {
        RefreshCurrentGiftId();
        LoadColorsFromJson();

        if (colorImage != null && gradientMaterial != null)
        {
            colorImage.material = Instantiate(gradientMaterial);
            colorImage.raycastTarget = false;
        }
        if (colorImage2 != null && gradientMaterial != null)
        {
            colorImage2.material = Instantiate(gradientMaterial);
            colorImage2.raycastTarget = false;
        }

        RemoveCanvasGroupIfAny(colorImage);
        RemoveCanvasGroupIfAny(colorImage2);

        if (Prefabs.Count > 0 && SpawnPoint != null)
        {
            lastItemIndex = rng.Next(Prefabs.Count);
            currentItem = Instantiate(Prefabs[lastItemIndex], SpawnPoint.position, SpawnPoint.rotation, SpawnPoint);
            ApplyAnimationToSpawnedItem(currentItem);
            SetItemAlpha(currentItem, 1f);
        }

        if (colorData != null && colorData.colors != null && colorData.colors.Count > 0)
        {
            lastColorIndex = rng.Next(colorData.colors.Count);
            currentColorEntry = colorData.colors[lastColorIndex];

            ApplyColorToImage(colorImage, lastColorIndex);
            ApplyColorToImage(colorImage2, lastColorIndex);

            if (colorImage != null && colorImage2 != null)
            {
                NormalizeColorHierarchyAndActive(colorImage, colorImage2);
                useFirstColorImage = true;
            }
            else
            {
                currentActiveImage = colorImage != null ? colorImage : colorImage2;
                if (currentActiveImage != null) currentActiveImage.gameObject.SetActive(true);
            }

            CapturePatternSnapshotFromCurrentColor();
            UpdatePatternImmediate();
            UpdateTransitionMaterial();
        }
        else
        {
            currentPatternColor = Color.white;
            currentPatternSprite = (patternSprites != null && patternSprites.Length > 0) ? patternSprites[0] : null;
            UpdatePatternImmediate();
        }
        StartCoroutine(SwitchRoutine());
    }

    private void OnDisable()
    {
        registeredInstances.Remove(this);
    }

    public ColorEntry GetCurrentColorData() => currentColorEntry;
    public GameObject GetCurrentColorObject() => currentActiveImage != null ? currentActiveImage.gameObject : null;

    public void StopForever()
    {
        if (isLockedForever) return;
        isLockedForever = true;

        StopAllCoroutines();

        if (colorImage != null && colorImage2 != null)
        {
            Image active = currentActiveImage != null ? currentActiveImage : colorImage;
            Image inactive = (active == colorImage) ? colorImage2 : colorImage;
            NormalizeColorHierarchyAndActive(active, inactive);
        }

        if (nextItem != null) Destroy(nextItem);
        nextItem = null;
        if (currentItem != null) SetItemAlpha(currentItem, 1f);

        CapturePatternSnapshotFromCurrentColor();

        enabled = false;
    }

    public void ApplySelectedGiftImmediate(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return;

        currentGiftId = giftId;

        if (currentItem != null)
            ApplyAnimationToSpawnedItem(currentItem);

        if (nextItem != null)
            ApplyAnimationToSpawnedItem(nextItem);
    }

    IEnumerator SwitchRoutine()
    {
        while (true)
        {
            if (isLockedForever) yield break;

            yield return new WaitForSeconds(switchInterval);

            if (isLockedForever) yield break;

            yield return StartCoroutine(CrossfadeEverything());
        }
    }

    IEnumerator CrossfadeEverything()
    {
        if (isLockedForever) yield break;
        if (colorData == null || colorData.colors == null || colorData.colors.Count == 0) yield break;

        RefreshCurrentGiftId();

        int newItemIndex = GetRandomIndex(Prefabs.Count, lastItemIndex);
        nextColorIndex = GetRandomIndex(colorData.colors.Count, lastColorIndex);

        Image currentColorImage = useFirstColorImage ? colorImage : colorImage2;
        Image nextColorImage = useFirstColorImage ? colorImage2 : colorImage;

        if (Prefabs.Count > 0 && SpawnPoint != null)
        {
            nextItem = Instantiate(Prefabs[newItemIndex], SpawnPoint.position, SpawnPoint.rotation, SpawnPoint);
            ApplyAnimationToSpawnedItem(nextItem);
            SetItemAlpha(nextItem, 0f);
        }

        if (nextColorImage != null && currentColorImage != null)
        {
            ApplyColorToImage(nextColorImage, nextColorIndex);
            NormalizeColorHierarchyAndActive(nextColorImage, currentColorImage);
        }

        nextPatternColor = HexToColor(colorData.colors[nextColorIndex].hex.patternColor);
        nextPatternSprite = (patternSprites != null && patternSprites.Length > 0)
            ? patternSprites[rng.Next(patternSprites.Length)]
            : null;

        if (patternContainer != null && !patternContainer.gameObject.activeSelf)
            patternContainer.gameObject.SetActive(true);

        nextPatternEmojis = CreatePatternEmojis(nextPatternSprite, nextPatternColor, 0f, fadeInScaleStart);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            if (isLockedForever) yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float curveT = fadeCurve.Evaluate(t);

            if (currentItem != null) SetItemAlpha(currentItem, 1f - curveT);
            if (nextItem != null) SetItemAlpha(nextItem, curveT);

            foreach (var emoji in currentPatternEmojis)
            {
                if (emoji != null)
                {
                    CanvasGroup cg = emoji.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = 1f - curveT;
                    float scaleValue = Mathf.Lerp(1f, fadeOutScaleMin, curveT);
                    emoji.transform.localScale = Vector3.one * scaleValue;
                }
            }
            foreach (var emoji in nextPatternEmojis)
            {
                if (emoji != null)
                {
                    CanvasGroup cg = emoji.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = curveT;
                    float scaleValue = Mathf.Lerp(fadeInScaleStart, fadeInScaleEnd, curveT);
                    emoji.transform.localScale = Vector3.one * scaleValue;
                }
            }

            yield return null;
        }

        if (isLockedForever) yield break;

        if (currentItem != null) Destroy(currentItem);
        currentItem = nextItem;
        nextItem = null;
        SetItemAlpha(currentItem, 1f);

        ClearPatternEmojis(currentPatternEmojis);
        currentPatternEmojis = nextPatternEmojis;
        nextPatternEmojis = new List<GameObject>();

        currentPatternSprite = nextPatternSprite;
        currentPatternColor = nextPatternColor;

        foreach (var emoji in currentPatternEmojis)
        {
            if (emoji != null)
                emoji.transform.localScale = Vector3.one * fadeInScaleEnd;
        }

        currentColorEntry = colorData.colors[nextColorIndex];
        currentActiveImage = nextColorImage;

        useFirstColorImage = !useFirstColorImage;

        UpdateTransitionMaterial();

        lastItemIndex = newItemIndex;
        lastColorIndex = nextColorIndex;
    }

    private void NormalizeColorHierarchyAndActive(Image active, Image inactive)
    {
        if (active == null || inactive == null) return;

        active.gameObject.SetActive(true);
        inactive.gameObject.SetActive(false);

        active.transform.SetAsFirstSibling();
        inactive.transform.SetAsLastSibling();

        currentActiveImage = active;
    }

    private void CapturePatternSnapshotFromCurrentColor()
    {
        if (currentColorEntry != null && currentColorEntry.hex != null)
            currentPatternColor = HexToColor(currentColorEntry.hex.patternColor);
        else
            currentPatternColor = Color.white;

        if (currentPatternSprite == null && patternSprites != null && patternSprites.Length > 0)
            currentPatternSprite = patternSprites[rng.Next(patternSprites.Length)];
    }

    void UpdateTransitionMaterial()
    {
        if (currentColorEntry == null) return;
        if (targetContainer == null) return;

        int childCount = targetContainer.childCount;
        if (childCount == 0) return;

        Transform lastChild = targetContainer.GetChild(childCount - 1);
        Image lastImage = lastChild.GetComponent<Image>();
        if (lastImage == null || lastImage.material == null) return;

        Color edgeColor = HexToColor(currentColorEntry.hex.edgeColor);
        Texture2D edgeTexture = CreateTextureFromColor(edgeColor);
        lastImage.material.SetTexture("_RightTex", edgeTexture);
    }

    private void RemoveCanvasGroupIfAny(Image img)
    {
        if (img == null) return;
        var cg = img.GetComponent<CanvasGroup>();
        if (cg != null) Destroy(cg);
    }

    Texture2D CreateTextureFromColor(Color color)
    {
        Texture2D texture = new Texture2D(256, 256);
        Color[] pixels = new Color[256 * 256];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    void LoadColorsFromJson()
    {
        if (colorJsonFile == null) return;

        try
        {
            string json = colorJsonFile.text;
            if (json.TrimStart().StartsWith("[")) json = "{\"colors\":" + json + "}";
            colorData = JsonUtility.FromJson<ColorDataList>(json);
        }
        catch { colorData = null; }
    }

    void SetItemAlpha(GameObject item, float alpha)
    {
        if (item == null) return;
        CanvasGroup cg = item.GetComponent<CanvasGroup>();
        if (cg == null) cg = item.AddComponent<CanvasGroup>();
        cg.alpha = alpha;
    }

    void ApplyColorToImage(Image image, int colorIndex)
    {
        if (image == null || colorData == null || colorIndex < 0 || colorIndex >= colorData.colors.Count)
            return;

        ColorEntry entry = colorData.colors[colorIndex];

        if (image.material != null)
        {
            Color centerCol = HexToColor(entry.hex.centerColor);
            Color edgeCol = HexToColor(entry.hex.edgeColor);

            image.material.SetColor("_CenterColor", centerCol);
            image.material.SetColor("_EdgeColor", edgeCol);
        }
    }

    void UpdatePatternImmediate()
    {
        if (patternContainer == null) return;
        if (patternSprites == null || patternSprites.Length == 0) return;

        if (currentPatternSprite == null)
            currentPatternSprite = patternSprites[rng.Next(patternSprites.Length)];

        ClearPatternEmojis(currentPatternEmojis);
        currentPatternEmojis = CreatePatternEmojis(currentPatternSprite, currentPatternColor, 1f, fadeInScaleEnd);
    }

    List<GameObject> CreatePatternEmojis(Sprite sprite, Color patternColor, float initialAlpha, float initialScale)
    {
        if (patternContainer == null) return new List<GameObject>();
        if (sprite == null) return new List<GameObject>();

        List<GameObject> emojis = new List<GameObject>();
        PatternPoint[] points = DefaultPattern.Points;

        foreach (var point in points)
        {
            GameObject emojiObj = new GameObject($"PatternEmoji_{point.position.x:F3}_{point.position.y:F3}");
            emojiObj.transform.SetParent(patternContainer, false);

            RectTransform emojiRT = emojiObj.AddComponent<RectTransform>();
            emojiRT.anchorMin = point.position;
            emojiRT.anchorMax = point.position;
            emojiRT.pivot = new Vector2(0.5f, 0.5f);
            emojiRT.anchoredPosition = Vector2.zero;

            float finalSize = basePatternSize * 2f * point.scale;
            emojiRT.sizeDelta = new Vector2(finalSize, finalSize);

            Image emojiImg = emojiObj.AddComponent<Image>();
            emojiImg.sprite = sprite;

            Material patternInstance = new Material(patternMaterial);
            emojiImg.material = patternInstance;

            Color finalColor = new Color(patternColor.r, patternColor.g, patternColor.b, point.opacity);
            patternInstance.SetColor("_Color", finalColor);

            emojiImg.color = Color.white;
            emojiImg.raycastTarget = false;

            CanvasGroup cg = emojiObj.AddComponent<CanvasGroup>();
            cg.alpha = initialAlpha;

            emojiObj.transform.localScale = Vector3.one * initialScale;

            emojis.Add(emojiObj);
        }

        return emojis;
    }

    void ClearPatternEmojis(List<GameObject> emojis)
    {
        foreach (var emoji in emojis)
            if (emoji != null) Destroy(emoji);
        emojis.Clear();
    }

    int GetRandomIndex(int count, int lastIndex)
    {
        if (count <= 1) return 0;

        int index;
        do { index = rng.Next(0, count); }
        while (index == lastIndex);

        return index;
    }

    Color HexToColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.white;
        Color c;
        if (ColorUtility.TryParseHtmlString(hex, out c)) return c;
        return Color.white;
    }

    private void RefreshCurrentGiftId()
    {
        string selectedGiftId = CaseOpeningScroll.GetSelectedGiftId();
        if (!string.IsNullOrWhiteSpace(selectedGiftId))
        {
            currentGiftId = selectedGiftId;
            return;
        }

        if (giftSource == null)
            return;

        string resolvedGiftId = giftSource.GetCurrentGiftId();
        if (!string.IsNullOrWhiteSpace(resolvedGiftId))
            currentGiftId = resolvedGiftId;
    }

    private bool ApplyAnimationToSpawnedItem(GameObject item)
    {
        if (item == null)
            return false;

        if (!IsAnimatedImageRuntimeSupported())
            return false;

        TextAsset animationJson = PickAnimationJsonForCurrentGift();
        if (animationJson == null)
            return false;

        AnimatedImage previewAnimationSource = item.GetComponentInChildren<AnimatedImage>(true);
        if (previewAnimationSource == null)
            return false;

        return TryPlayAnimatedImage(previewAnimationSource, animationJson);
    }

    private static bool IsAnimatedImageRuntimeSupported()
    {
        if (Application.platform != RuntimePlatform.Android)
            return true;

        return System.IntPtr.Size >= 8;
    }

    private static bool TryPlayAnimatedImage(AnimatedImage animatedImage, TextAsset animationJson)
    {
        if (animatedImage == null || animationJson == null)
            return false;

        animatedImage.LoadFromAnimationJson(animationJson.text, 512u, 512u, string.Empty);
        animatedImage.Play();
        return true;
    }

    private TextAsset PickAnimationJsonForCurrentGift()
    {
        GiftAnimationBinding binding = GetAnimationBinding(currentGiftId);
        if (binding == null || binding.animationJsonFiles == null || binding.animationJsonFiles.Count == 0)
            return null;

        List<TextAsset> validFiles = new List<TextAsset>();
        for (int i = 0; i < binding.animationJsonFiles.Count; i++)
        {
            if (binding.animationJsonFiles[i] != null)
                validFiles.Add(binding.animationJsonFiles[i]);
        }

        if (validFiles.Count == 0)
            return null;

        int lastIndex = -1;
        lastAnimationIndexByGift.TryGetValue(currentGiftId ?? "", out lastIndex);
        int chosenIndex = GetRandomIndex(validFiles.Count, lastIndex);
        lastAnimationIndexByGift[currentGiftId ?? ""] = chosenIndex;
        return validFiles[chosenIndex];
    }

    private bool HasAnimationBindingForCurrentGift()
    {
        GiftAnimationBinding binding = GetAnimationBinding(currentGiftId);
        return binding != null && binding.animationJsonFiles != null && binding.animationJsonFiles.Count > 0;
    }

    private GiftAnimationBinding GetAnimationBinding(string giftId)
    {
        if (giftAnimationBindings == null || giftAnimationBindings.Count == 0 || string.IsNullOrWhiteSpace(giftId))
            return null;

        for (int i = 0; i < giftAnimationBindings.Count; i++)
        {
            GiftAnimationBinding binding = giftAnimationBindings[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.giftId))
                continue;

            if (string.Equals(binding.giftId, giftId, System.StringComparison.OrdinalIgnoreCase))
                return binding;
        }

        return null;
    }

    private string NormalizeAnimationName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace('\u00A0', ' ').Trim();
    }

}

[System.Serializable]
public class ColorDataList { public List<ColorEntry> colors; }

[System.Serializable]
public class ColorEntry
{
    public string name;
    public HexColors hex;
    public int rarityPermille;
}

[System.Serializable]
public class HexColors
{
    public string centerColor;
    public string edgeColor;
    public string patternColor;
    public string textColor;
}

