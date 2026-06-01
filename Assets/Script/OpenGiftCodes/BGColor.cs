using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

public class ColorSpawner : MonoBehaviour
{
    private static readonly List<ColorSpawner> registeredInstances = new List<ColorSpawner>();
    public static IReadOnlyList<ColorSpawner> RegisteredInstances => registeredInstances;

    public RectTransform previewAnchor;

    public Transform container;
    public RectTransform scrollViewport;

    public TextAsset jsonFile;

    public TextAsset patternJsonFile;

    [Header("Sync with RandomSwitcher")]
    public RandomSwitcher randomSwitcher;

    [Header("Spawn Settings")]
    private int spawnCount = 10;
    private int winnerIndex = 0;
    public Button generateButton;

    public Button skipButton;

    private float scrollDuration = 3f;
    private float scrollSpeed = 0.7f;
    private float itemWidth = 1080f;
    private float itemSpacing = 10f;

    private float slowdownDistance = 300f;
    private float minSpeed = 0.1f;
    private float slowdownCurveStrength = 2f;

    [Header("Material Settings")]
    public Material gradientMaterial;
    public string gradientOverlayName = "GradientOverlay";

    private void OnEnable()
    {
        if (!registeredInstances.Contains(this))
            registeredInstances.Add(this);
    }

    private void OnDisable()
    {
        registeredInstances.Remove(this);
    }

    [Header("Pattern Settings")]
    public RectTransform patternContainer;
    public Sprite[] patternSprites;
    public Material patternMaterial;
    public float basePatternSize = 55f;

    private int patternUpdateCount = 8;
    private float patternChangeDuration = 0.55f;
    public AnimationCurve patternFadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    private float fadeOutScaleMin = 0f;
    private float fadeInScaleStart = 0f;
    private float fadeInScaleEnd = 1f;

    [Header("Background Settings")]
    [SerializeField] private GameObject backgroundContainer;
    [SerializeField] private Material transitionMaterial;
    [SerializeField] private Texture2D noiseTexture;
    [SerializeField] private float transitionDuration = 2f;

    [Serializable]
    public struct TransitionPair
    {
        public string from;
        public string to;

        public TransitionPair(string from, string to)
        {
            this.from = from;
            this.to = to;
        }
    }

    [Serializable]
    public class RollData
    {
        public List<string> slotNames = new List<string>();
        public List<TransitionPair> transitionPairs = new List<TransitionPair>();
    }

    [Serializable]
    public class RollColorData
    {
        public string id;
        public string name;
        public int rarityPermille;

        public RollColorData() { }

        public RollColorData(string id, string name, int rarityPermille)
        {
            this.id = id;
            this.name = name;
            this.rarityPermille = rarityPermille;
        }
    }

    [Serializable]
    public class RollPatternData
    {
        public string id;
        public string name;
        public int rarityPermille;

        public RollPatternData() { }

        public RollPatternData(string id, string name, int rarityPermille)
        {
            this.id = id;
            this.name = name;
            this.rarityPermille = rarityPermille;
        }
    }

    public event Action<RollData> OnRollDataChanged;
    public event Action<List<string>> OnPatternNamesReady;

    public event Action<List<RollColorData>> OnRollColorItemsReady;
    public event Action<RollColorData> OnWinColorItemReady;
    public event Action<List<RollPatternData>> OnPatternItemsReady;
    public event Action<RollPatternData> OnWinPatternItemReady;

    public List<string> LastPatternNames { get; private set; } = new List<string>();
    public List<RollPatternData> LastPatternItems { get; private set; } = new List<RollPatternData>();
    public List<RollColorData> LastColorItems { get; private set; } = new List<RollColorData>();
    public RollColorData CurrentWinColorItem { get; private set; }
    public RollPatternData CurrentWinPatternItem { get; private set; }

    private readonly List<string> slotColorNames = new List<string>();
    private readonly List<TransitionPair> transitionPairs = new List<TransitionPair>();

    private readonly List<Sprite> plannedPatternSprites = new List<Sprite>();
    private readonly List<string> plannedPatternNames = new List<string>();
    private readonly List<PatternRarityItem> plannedPatternData = new List<PatternRarityItem>();

    [Serializable]
    public class ItemColorData
    {
        public string id;
        public string name;
        public int centerColor;
        public int edgeColor;
        public int patternColor;
        public int textColor;
        public int rarityPermille;
        public HexColors hex;

        [Serializable]
        public class HexColors
        {
            public string centerColor;
            public string edgeColor;
            public string patternColor;
            public string textColor;
        }
    }

    [Serializable]
    public class ColorDatabase
    {
        public List<ItemColorData> items;
    }

    [Serializable]
    public class PatternRarityItem
    {
        public string id;
        public string name;
        public int rarityPermille;
    }

    [Serializable]
    public class PatternRarityDatabase
    {
        public List<PatternRarityItem> items;
    }

    private List<ItemColorData> allColors = new List<ItemColorData>();
    private readonly Dictionary<string, ItemColorData> colorByName = new Dictionary<string, ItemColorData>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PatternRarityItem> patternByName = new Dictionary<string, PatternRarityItem>(StringComparer.OrdinalIgnoreCase);

    private System.Random rng = new System.Random();

    private bool isScrolling = false;
    private RectTransform containerRect;

    private readonly List<GameObject> transitionElements = new List<GameObject>();
    private List<GameObject> currentPatternEmojis = new List<GameObject>();
    private readonly Dictionary<Color32, Texture2D> solidColorTextureCache = new Dictionary<Color32, Texture2D>();

    private float currentSpeedMultiplier = 1f;
    private float speedVelocity = 0f;

    private readonly List<GameObject> spawnedItems = new List<GameObject>();
    private readonly List<ItemColorData> selectedColors = new List<ItemColorData>();

    private readonly Dictionary<string, int> patternWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private Coroutine scrollCoroutine;
    private Coroutine patternCoroutine;
    private Coroutine backgroundCoroutine;
    private bool skipRequested;

    void Start()
    {
        LoadColorsFromFile();
        LoadPatternRaritiesFromFile();
        DebugPatternNameMismatches();

        containerRect = container != null ? container.GetComponent<RectTransform>() : null;

        if (scrollViewport == null && container != null)
            scrollViewport = container.parent?.GetComponent<RectTransform>();

        if (generateButton != null)
            generateButton.onClick.AddListener(StartRoll);

        if (skipButton != null)
            skipButton.onClick.AddListener(RequestSkip);
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<Color32, Texture2D> pair in solidColorTextureCache)
        {
            if (pair.Value != null)
                Destroy(pair.Value);
        }

        solidColorTextureCache.Clear();
    }

    private void StartRoll()
    {
        if (scrollCoroutine != null) StopCoroutine(scrollCoroutine);
        scrollCoroutine = StartCoroutine(ScrollRoutine());
    }

    private void RequestSkip()
    {
        skipRequested = true;

        if (!isScrolling) return;

        if (backgroundCoroutine != null)
        {
            StopCoroutine(backgroundCoroutine);
            backgroundCoroutine = null;
        }

        if (patternCoroutine != null)
        {
            StopCoroutine(patternCoroutine);
            patternCoroutine = null;
        }

        ForceBackgroundToEnd();
        ApplyFinalPatternImmediate();
    }

    void LoadColorsFromFile()
    {
        allColors.Clear();
        colorByName.Clear();

        if (jsonFile == null)
        {
            return;
        }

        string jsonText = jsonFile.text.Trim();

        try
        {
            if (jsonText.StartsWith("["))
            {
                string wrappedJson = "{\"items\":" + jsonText + "}";
                ColorDatabase db = JsonUtility.FromJson<ColorDatabase>(wrappedJson);
                if (db != null && db.items != null) allColors = db.items;
            }
            else if (jsonText.StartsWith("{"))
            {
                ColorDatabase db = JsonUtility.FromJson<ColorDatabase>(jsonText);
                if (db != null && db.items != null) allColors = db.items;
            }
        }
        catch (Exception)
        {
        }

        if (allColors == null) allColors = new List<ItemColorData>();

        for (int i = 0; i < allColors.Count; i++)
        {
            var item = allColors[i];
            if (item == null) continue;
            if (string.IsNullOrWhiteSpace(item.name)) continue;
            colorByName[item.name] = item;
        }

    }

    private void LoadPatternRaritiesFromFile()
    {
        patternWeights.Clear();
        patternByName.Clear();

        if (patternJsonFile == null)
        {
            return;
        }

        string jsonText = patternJsonFile.text.Trim();

        try
        {
            PatternRarityDatabase db = null;

            if (jsonText.StartsWith("["))
            {
                string wrapped = "{\"items\":" + jsonText + "}";
                db = JsonUtility.FromJson<PatternRarityDatabase>(wrapped);
            }
            else if (jsonText.StartsWith("{"))
            {
                db = JsonUtility.FromJson<PatternRarityDatabase>(jsonText);
            }

            if (db != null && db.items != null)
            {
                for (int i = 0; i < db.items.Count; i++)
                {
                    var it = db.items[i];
                    if (it == null) continue;
                    if (string.IsNullOrWhiteSpace(it.name)) continue;

                    int w = ClampPermilleWeight(it.rarityPermille);
                    patternWeights[it.name] = w;
                    patternByName[it.name] = it;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void DebugPatternNameMismatches()
    {
        HashSet<string> spriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (patternSprites != null)
        {
            for (int i = 0; i < patternSprites.Length; i++)
            {
                var s = patternSprites[i];
                if (s == null) continue;
                if (!string.IsNullOrWhiteSpace(s.name))
                    spriteNames.Add(s.name);
            }
        }

        List<string> missingInJson = new List<string>();
        foreach (var n in spriteNames)
            if (!patternWeights.ContainsKey(n))
                missingInJson.Add(n);

        List<string> missingInSprites = new List<string>();
        foreach (var kv in patternWeights)
            if (!spriteNames.Contains(kv.Key))
                missingInSprites.Add(kv.Key);

    }

    private IEnumerator ScrollRoutine()
    {
        if (isScrolling || allColors.Count == 0 || containerRect == null || container == null)
            yield break;

        isScrolling = true;
        skipRequested = false;

        CurrentWinColorItem = null;
        CurrentWinPatternItem = null;
        LastColorItems.Clear();
        LastPatternItems.Clear();

        currentSpeedMultiplier = 1f;
        speedVelocity = 0f;

        if (generateButton != null) generateButton.interactable = false;
        if (skipButton != null) skipButton.interactable = true;

        Sprite startPatternSprite = null;
        Color startPatternColor = Color.white;

        if (randomSwitcher != null)
        {
            var snap = randomSwitcher.GetCurrentPatternSnapshot();
            startPatternSprite = snap.sprite;
            startPatternColor = snap.color;

            randomSwitcher.StopForever();
            randomSwitcher.DisablePatternVisuals();
        }

        ClearGridContainer();

        PreparePatternPlan(startPatternSprite);

        if (startPatternSprite != null)
            ShowPatternImmediate(startPatternSprite, startPatternColor);

        GenerateNonWinnerSlots_HColor();

        CreateTransitionElements();

        yield return null;

        Canvas.ForceUpdateCanvases();

        ApplyEdgeColorToLastTransition();

        GameObject winnerGO = TransferFirstHierarchyColorImageToGridEnd();
        if (winnerGO != null)
            spawnedItems.Add(winnerGO);

        BuildAndEmitRollData();

        AlignWinnerToPreviewAnchor(winnerGO);

        ApplyFirstGridChildEdgeToTransition0_LeftTex();

        ApplyGradientToWinner_CentralUnchanged();

        backgroundCoroutine = StartCoroutine(BackgroundTransitionRoutine());
        patternCoroutine = StartCoroutine(PatternUpdateRoutine());

        float viewportWidth = scrollViewport != null ? scrollViewport.rect.width : 800f;
        float totalItemWidth = itemWidth + itemSpacing;
        float winnerItemX = winnerIndex * totalItemWidth;
        float targetX = -(winnerItemX - viewportWidth / 2f + itemWidth / 2f);

        Vector2 startPos = containerRect.anchoredPosition;
        Vector2 endPos = new Vector2(targetX, startPos.y);

        if (skipRequested)
        {
            if (backgroundCoroutine != null) { StopCoroutine(backgroundCoroutine); backgroundCoroutine = null; }
            if (patternCoroutine != null) { StopCoroutine(patternCoroutine); patternCoroutine = null; }
            ForceBackgroundToEnd();
            ApplyFinalPatternImmediate();
            containerRect.anchoredPosition = endPos;
        }
        else
        {
            float actualDuration = Mathf.Max(0.0001f, scrollDuration / Mathf.Max(0.0001f, scrollSpeed));
            float elapsed = 0f;
            float viewportCenterX = viewportWidth * 0.5f;

            while (elapsed < actualDuration)
            {
                if (skipRequested)
                {
                    if (backgroundCoroutine != null) { StopCoroutine(backgroundCoroutine); backgroundCoroutine = null; }
                    if (patternCoroutine != null) { StopCoroutine(patternCoroutine); patternCoroutine = null; }
                    ForceBackgroundToEnd();
                    ApplyFinalPatternImmediate();
                    containerRect.anchoredPosition = endPos;
                    break;
                }

                float winnerScreenX = containerRect.anchoredPosition.x + winnerItemX + itemWidth * 0.5f;
                float distanceToCenter = Mathf.Abs(winnerScreenX - viewportCenterX);

                float targetSpeed = 1f;

                if (distanceToCenter < slowdownDistance)
                {
                    float slowdownT = Mathf.Clamp01(distanceToCenter / Mathf.Max(1f, slowdownDistance));
                    float smoothT = Mathf.Pow(slowdownT, slowdownCurveStrength);
                    targetSpeed = Mathf.Lerp(minSpeed, 1f, smoothT);
                }

                currentSpeedMultiplier = Mathf.SmoothDamp(currentSpeedMultiplier, targetSpeed, ref speedVelocity, 0.25f);

                elapsed += Time.deltaTime * currentSpeedMultiplier;
                float t = Mathf.Clamp01(elapsed / actualDuration);

                containerRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                yield return null;
            }

            if (!skipRequested)
                containerRect.anchoredPosition = endPos;
        }

        EmitFinalWinData();
        isScrolling = false;

        if (generateButton != null) generateButton.interactable = true;
        if (skipButton != null) skipButton.interactable = true;
    }

    private void EmitFinalWinData()
    {
        string winColorName = GetWinnerName();
        CurrentWinColorItem = GetColorRollDataByName(winColorName);

        if (plannedPatternData != null && plannedPatternData.Count > 0)
        {
            PatternRarityItem lastPattern = plannedPatternData[plannedPatternData.Count - 1];
            if (lastPattern != null)
                CurrentWinPatternItem = new RollPatternData(lastPattern.id, lastPattern.name, lastPattern.rarityPermille);
        }

        if (CurrentWinColorItem != null)
            OnWinColorItemReady?.Invoke(CloneColorItem(CurrentWinColorItem));

        if (CurrentWinPatternItem != null)
            OnWinPatternItemReady?.Invoke(ClonePatternItem(CurrentWinPatternItem));

        InventoryManager inventory = InventoryManager.Instance;

        if (inventory != null)
            inventory.SaveCurrentFromNearestRoll(transform);
    }

    private void ForceBackgroundToEnd()
    {
        for (int i = 0; i < transitionElements.Count; i++)
        {
            GameObject transitionGO = transitionElements[i];
            if (transitionGO == null) continue;
            Image img = transitionGO.GetComponent<Image>();
            if (img != null && img.material != null)
                img.material.SetFloat("_Threshold", 1f);
        }
    }

    private void ApplyFinalPatternImmediate()
    {
        if (patternContainer == null || patternMaterial == null) return;
        if (selectedColors == null || selectedColors.Count == 0) return;
        if (plannedPatternSprites == null || plannedPatternSprites.Count == 0) return;

        int lastIndex = Mathf.Max(0, selectedColors.Count - 1);
        int i = Mathf.Max(0, patternUpdateCount - 1);

        int colorIndex = lastIndex - (i * 2);
        colorIndex = Mathf.Clamp(colorIndex, 0, lastIndex);

        int spritePlanIndex = Mathf.Clamp(patternUpdateCount, 1, plannedPatternSprites.Count - 1);
        Sprite finalSprite = plannedPatternSprites[spritePlanIndex];
        if (finalSprite == null) finalSprite = plannedPatternSprites[plannedPatternSprites.Count - 1];

        Color patternColor = HexToColor(selectedColors[colorIndex].hex.patternColor);
        if (patternColor == Color.white)
            patternColor = HexToColor(selectedColors[0].hex.patternColor);

        if (!patternContainer.gameObject.activeSelf)
            patternContainer.gameObject.SetActive(true);

        ClearPatternEmojis(currentPatternEmojis);
        currentPatternEmojis = CreatePatternEmojis(finalSprite, patternColor, 1f);

        for (int k = 0; k < currentPatternEmojis.Count; k++)
            if (currentPatternEmojis[k] != null)
                currentPatternEmojis[k].transform.localScale = Vector3.one * fadeInScaleEnd;
    }

    private void ClearGridContainer()
    {
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);

        spawnedItems.Clear();
        selectedColors.Clear();

        slotColorNames.Clear();
        transitionPairs.Clear();

        plannedPatternSprites.Clear();
        plannedPatternNames.Clear();
        plannedPatternData.Clear();
        LastPatternNames.Clear();
        LastPatternItems.Clear();
        LastColorItems.Clear();

        for (int i = 0; i < transitionElements.Count; i++)
            if (transitionElements[i] != null) Destroy(transitionElements[i]);
        transitionElements.Clear();

        ClearPatternEmojis(currentPatternEmojis);
    }

    private int ClampPermilleWeight(int rarityPermille)
    {
        if (rarityPermille <= 0) return 0;
        if (rarityPermille > 1000000) return 1000000;
        return rarityPermille;
    }

    private ItemColorData PickRandomColorByRarity()
    {
        if (allColors == null || allColors.Count == 0) return null;

        long total = 0;
        for (int i = 0; i < allColors.Count; i++)
        {
            var c = allColors[i];
            if (c == null) continue;
            total += ClampPermilleWeight(c.rarityPermille);
        }

        if (total <= 0)
            return allColors[rng.Next(allColors.Count)];

        double r = rng.NextDouble() * total;
        long acc = 0;

        for (int i = 0; i < allColors.Count; i++)
        {
            var c = allColors[i];
            if (c == null) continue;

            int w = ClampPermilleWeight(c.rarityPermille);
            if (w <= 0) continue;

            acc += w;
            if (r < acc)
                return c;
        }

        return allColors[rng.Next(allColors.Count)];
    }

    public float GetChancePercentByName(string colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName)) return -1f;
        if (allColors == null || allColors.Count == 0) return -1f;

        long total = 0;
        int w = 0;

        for (int i = 0; i < allColors.Count; i++)
        {
            var c = allColors[i];
            if (c == null) continue;

            int ww = ClampPermilleWeight(c.rarityPermille);
            total += ww;

            if (w == 0 && string.Equals(c.name, colorName, StringComparison.OrdinalIgnoreCase))
                w = ww;
        }

        if (total <= 0 || w <= 0) return -1f;
        return w / (float)total * 100f;
    }

    private Sprite PickPatternSpriteByRarity(out PatternRarityItem pickedData)
    {
        pickedData = null;

        if (patternSprites == null || patternSprites.Length == 0) return null;

        long total = 0;
        for (int i = 0; i < patternSprites.Length; i++)
        {
            var s = patternSprites[i];
            if (s == null) continue;

            int w = 0;
            if (!string.IsNullOrWhiteSpace(s.name) && patternWeights.TryGetValue(s.name, out var ww))
                w = ClampPermilleWeight(ww);

            total += w;
        }

        if (total <= 0)
        {
            int guard = 0;
            while (guard++ < 1000)
            {
                var s = patternSprites[rng.Next(patternSprites.Length)];
                if (s != null)
                {
                    patternByName.TryGetValue(s.name, out pickedData);
                    return s;
                }
            }
            return null;
        }

        double r = rng.NextDouble() * total;
        long acc = 0;

        for (int i = 0; i < patternSprites.Length; i++)
        {
            var s = patternSprites[i];
            if (s == null) continue;

            int w = 0;
            if (!string.IsNullOrWhiteSpace(s.name) && patternWeights.TryGetValue(s.name, out var ww))
                w = ClampPermilleWeight(ww);

            if (w <= 0) continue;

            acc += w;
            if (r < acc)
            {
                patternByName.TryGetValue(s.name, out pickedData);
                return s;
            }
        }

        Sprite fallback = patternSprites[rng.Next(patternSprites.Length)];
        if (fallback != null)
            patternByName.TryGetValue(fallback.name, out pickedData);
        return fallback;
    }

    private void GenerateNonWinnerSlots_HColor()
    {
        int need = Mathf.Max(0, spawnCount - 1);

        for (int i = 0; i < need; i++)
        {
            ItemColorData randomColor = PickRandomColorByRarity();
            if (randomColor == null) continue;

            selectedColors.Add(randomColor);
            LastColorItems.Add(new RollColorData(randomColor.id, randomColor.name, randomColor.rarityPermille));
        }

        for (int i = 0; i < selectedColors.Count; i++)
        {
            GameObject slot = CreateColorSlot_HColor(selectedColors[i]);
            slot.transform.SetParent(container, false);
            slot.transform.SetAsLastSibling();
            spawnedItems.Add(slot);
        }
    }

    private string GetWinnerName()
    {
        if (randomSwitcher == null) return "UNKNOWN";

        ColorEntry ce = randomSwitcher.GetCurrentColorData();
        if (ce != null && !string.IsNullOrWhiteSpace(ce.name))
            return ce.name;

        return "UNKNOWN";
    }

    private void BuildAndEmitRollData()
    {
        slotColorNames.Clear();
        transitionPairs.Clear();

        string winnerName = GetWinnerName();

        int needRandom = Mathf.Max(0, spawnCount - 1);

        for (int i = 0; i < needRandom; i++)
        {
            string n = "";

            if (i < selectedColors.Count && selectedColors[i] != null)
                n = selectedColors[i].name;

            if (string.IsNullOrWhiteSpace(n))
                n = "UNKNOWN";

            slotColorNames.Add(n);
        }

        slotColorNames.Add(winnerName);

        if (slotColorNames.Count > spawnCount)
            slotColorNames.RemoveRange(spawnCount, slotColorNames.Count - spawnCount);
        if (slotColorNames.Count == spawnCount)
            slotColorNames[spawnCount - 1] = winnerName;

        for (int i = 0; i < slotColorNames.Count - 1; i++)
            transitionPairs.Add(new TransitionPair(slotColorNames[i], slotColorNames[i + 1]));

        RollData data = new RollData
        {
            slotNames = new List<string>(slotColorNames),
            transitionPairs = new List<TransitionPair>(transitionPairs)
        };

        OnRollDataChanged?.Invoke(data);

        RollColorData winnerColor = GetColorRollDataByName(winnerName);
        if (winnerColor != null)
            LastColorItems.Add(CloneColorItem(winnerColor));

        OnRollColorItemsReady?.Invoke(CloneColorItemList(LastColorItems));

    }

    public void EmitRollDataNow()
    {
        if (selectedColors.Count < Mathf.Max(0, spawnCount - 1) && allColors != null && allColors.Count > 0)
        {
            selectedColors.Clear();
            LastColorItems.Clear();

            int need = Mathf.Max(0, spawnCount - 1);
            for (int i = 0; i < need; i++)
            {
                ItemColorData randomColor = PickRandomColorByRarity();
                if (randomColor == null) continue;

                selectedColors.Add(randomColor);
                LastColorItems.Add(new RollColorData(randomColor.id, randomColor.name, randomColor.rarityPermille));
            }
        }

        BuildAndEmitRollData();
    }

    public void EmitPatternNamesNow()
    {
        if (LastPatternNames != null && LastPatternNames.Count > 0)
            OnPatternNamesReady?.Invoke(new List<string>(LastPatternNames));

        if (LastPatternItems != null && LastPatternItems.Count > 0)
            OnPatternItemsReady?.Invoke(ClonePatternItemList(LastPatternItems));
    }

    public float GetPatternChancePercentByName(string patternName)
    {
        if (string.IsNullOrWhiteSpace(patternName)) return -1f;

        long total = 0;
        int w = 0;

        if (patternSprites == null || patternSprites.Length == 0) return -1f;

        for (int i = 0; i < patternSprites.Length; i++)
        {
            var s = patternSprites[i];
            if (s == null) continue;

            int ww = 0;
            if (!string.IsNullOrWhiteSpace(s.name) && patternWeights.TryGetValue(s.name, out var raw))
                ww = ClampPermilleWeight(raw);

            total += ww;

            if (w == 0 && string.Equals(s.name, patternName, StringComparison.OrdinalIgnoreCase))
                w = ww;
        }

        if (total <= 0 || w <= 0) return -1f;
        return w / (float)total * 100f;
    }

    public string GetColorIdByName(string colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName)) return "";
        if (colorByName.TryGetValue(colorName, out var item) && item != null)
            return item.id ?? "";
        return "";
    }

    public string GetPatternIdByName(string patternName)
    {
        if (string.IsNullOrWhiteSpace(patternName)) return "";
        if (patternByName.TryGetValue(patternName, out var item) && item != null)
            return item.id ?? "";
        return "";
    }

    private GameObject CreateColorSlot_HColor(ItemColorData colorData)
    {
        GameObject go = new GameObject("Slot_" + colorData.name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.localScale = Vector3.one;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(itemWidth, itemWidth);

        Image img = go.GetComponent<Image>();
        img.color = Color.white;

        Color h = HexToColor(colorData.hex.edgeColor);

        if (gradientMaterial != null)
        {
            Material mat = new Material(gradientMaterial);
            img.material = mat;
            mat.SetColor("_CenterColor", h);
            mat.SetColor("_EdgeColor", h);
        }
        else
        {
            img.color = h;
        }

        return go;
    }

    private GameObject TransferFirstHierarchyColorImageToGridEnd()
    {
        if (randomSwitcher == null) return null;
        if (randomSwitcher.colorImage == null || randomSwitcher.colorImage2 == null) return null;

        Transform t1 = randomSwitcher.colorImage.transform;
        Transform t2 = randomSwitcher.colorImage2.transform;

        Transform first = (t1.GetSiblingIndex() <= t2.GetSiblingIndex()) ? t1 : t2;

        first.SetParent(container, false);
        first.SetAsLastSibling();

        Canvas.ForceUpdateCanvases();
        return first.gameObject;
    }

    private void AlignWinnerToPreviewAnchor(GameObject winnerGO)
    {
        if (winnerGO == null) return;
        if (previewAnchor == null) return;
        if (scrollViewport == null) return;

        RectTransform winnerRT = winnerGO.transform as RectTransform;
        if (winnerRT == null) return;

        Canvas.ForceUpdateCanvases();

        Vector2 winnerWorldCenter = winnerRT.TransformPoint(winnerRT.rect.center);
        Vector2 winnerLocalInViewport = scrollViewport.InverseTransformPoint(winnerWorldCenter);

        Vector2 anchorWorldCenter = previewAnchor.TransformPoint(previewAnchor.rect.center);
        Vector2 anchorLocalInViewport = scrollViewport.InverseTransformPoint(anchorWorldCenter);

        Vector2 delta = anchorLocalInViewport - winnerLocalInViewport;
        containerRect.anchoredPosition += new Vector2(delta.x, 0f);
    }

    private void CreateTransitionElements()
    {
        for (int i = 0; i < transitionElements.Count; i++)
            if (transitionElements[i] != null) Destroy(transitionElements[i]);
        transitionElements.Clear();

        if (backgroundContainer == null || transitionMaterial == null) return;

        List<Image> imagesList = new List<Image>();
        for (int i = 0; i < backgroundContainer.transform.childCount; i++)
        {
            Image img = backgroundContainer.transform.GetChild(i).GetComponent<Image>();
            if (img != null) imagesList.Add(img);
        }

        if (imagesList.Count < 2) return;

        Image[] images = imagesList.ToArray();

        for (int i = 0; i < images.Length - 1; i++)
        {
            GameObject transitionGO = new GameObject("Transition_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            transitionGO.transform.SetParent(backgroundContainer.transform, false);

            RectTransform rt = transitionGO.GetComponent<RectTransform>();
            RectTransform leftRT = images[i].GetComponent<RectTransform>();
            RectTransform rightRT = images[i + 1].GetComponent<RectTransform>();

            Vector2 posLeft = leftRT.anchoredPosition;
            Vector2 posRight = rightRT.anchoredPosition;

            rt.anchoredPosition = (posLeft + posRight) / 2f;
            rt.sizeDelta = new Vector2(Mathf.Abs(posRight.x - posLeft.x), leftRT.sizeDelta.y);

            rt.anchorMin = leftRT.anchorMin;
            rt.anchorMax = leftRT.anchorMax;
            rt.pivot = leftRT.pivot;

            Image img = transitionGO.GetComponent<Image>();
            Material mat = new Material(transitionMaterial);

            mat.SetTexture("_LeftTex", GetTextureFromSlotImage(images[i]));
            mat.SetTexture("_RightTex", GetTextureFromSlotImage(images[i + 1]));

            if (noiseTexture != null) mat.SetTexture("_NoiseTex", noiseTexture);
            mat.SetFloat("_Threshold", 0f);

            img.material = mat;
            transitionElements.Add(transitionGO);
        }

        for (int i = 1; i < images.Length; i++)
            Destroy(images[i].gameObject);
    }

    private Texture2D GetTextureFromSlotImage(Image image)
    {
        if (image == null) return null;

        Color c = image.color;

        if (image.material != null)
        {
            bool isWinnerSlot = false;
            if (container != null && winnerIndex >= 0 && winnerIndex < container.childCount)
                isWinnerSlot = (image.transform == container.GetChild(winnerIndex));

            if (isWinnerSlot)
            {
                if (image.material.HasProperty("_CenterColor"))
                    c = image.material.GetColor("_CenterColor");
            }
            else
            {
                if (image.material.HasProperty("_EdgeColor"))
                    c = image.material.GetColor("_EdgeColor");
                else if (image.material.HasProperty("_CenterColor"))
                    c = image.material.GetColor("_CenterColor");
            }
        }

        return CreateTextureFromColor(c);
    }

    private void ApplyEdgeColorToLastTransition()
    {
        if (randomSwitcher == null) return;

        ColorEntry currentColor = randomSwitcher.GetCurrentColorData();
        if (currentColor == null) return;

        if (transitionElements.Count == 0) return;

        Image lastImage = transitionElements[transitionElements.Count - 1]?.GetComponent<Image>();
        if (lastImage == null || lastImage.material == null) return;

        Color edgeColor = HexToColor(currentColor.hex.edgeColor);
        lastImage.material.SetTexture("_RightTex", CreateTextureFromColor(edgeColor));
    }

    private void ApplyFirstGridChildEdgeToTransition0_LeftTex()
    {
        if (transitionElements.Count == 0) return;
        if (container == null || container.childCount == 0) return;

        Image trImg = transitionElements[0]?.GetComponent<Image>();
        if (trImg == null || trImg.material == null) return;

        Image firstImg = container.GetChild(0).GetComponent<Image>();
        if (firstImg == null) return;

        Color edge = firstImg.color;

        if (firstImg.material != null && firstImg.material.HasProperty("_EdgeColor"))
            edge = firstImg.material.GetColor("_EdgeColor");
        else if (firstImg.material != null && firstImg.material.HasProperty("_CenterColor"))
            edge = firstImg.material.GetColor("_CenterColor");

        trImg.material.SetTexture("_LeftTex", CreateTextureFromColor(edge));
    }

    private void ApplyGradientToWinner_CentralUnchanged()
    {
        if (spawnedItems.Count == 0) return;
        if (winnerIndex < 0 || winnerIndex >= spawnedItems.Count) return;
        if (selectedColors.Count == 0) return;

        ItemColorData firstRolled = selectedColors[0];

        Color center = HexToColor(firstRolled.hex.centerColor);
        Color edge = HexToColor(firstRolled.hex.edgeColor);

        GameObject winnerSlot = spawnedItems[winnerIndex];

        Transform old = winnerSlot.transform.Find(gradientOverlayName);
        if (old != null) Destroy(old.gameObject);

        Image winnerImg = winnerSlot.GetComponent<Image>();
        if (winnerImg == null) return;

        if (winnerImg.material == null)
        {
            if (gradientMaterial == null) return;
            winnerImg.material = new Material(gradientMaterial);
        }

        if (winnerImg.material.HasProperty("_CenterColor"))
            winnerImg.material.SetColor("_CenterColor", center);

        if (winnerImg.material.HasProperty("_EdgeColor"))
            winnerImg.material.SetColor("_EdgeColor", edge);

        winnerImg.color = Color.white;
    }

    private void PreparePatternPlan(Sprite firstSprite)
    {
        plannedPatternSprites.Clear();
        plannedPatternNames.Clear();
        plannedPatternData.Clear();

        if (firstSprite != null)
        {
            plannedPatternSprites.Add(firstSprite);
            plannedPatternNames.Add(firstSprite.name);

            PatternRarityItem firstData = GetPatternDataByName(firstSprite.name);
            if (firstData != null)
                plannedPatternData.Add(firstData);
            else
                plannedPatternData.Add(new PatternRarityItem { id = "", name = firstSprite.name, rarityPermille = 0 });
        }

        if (patternSprites != null && patternSprites.Length > 0)
        {
            for (int i = 0; i < patternUpdateCount; i++)
            {
                PatternRarityItem pickedData;
                Sprite s = PickPatternSpriteByRarity(out pickedData);
                if (s == null) continue;

                plannedPatternSprites.Add(s);
                plannedPatternNames.Add(s.name);

                if (pickedData != null)
                    plannedPatternData.Add(pickedData);
                else
                    plannedPatternData.Add(new PatternRarityItem { id = "", name = s.name, rarityPermille = 0 });
            }
        }

        LastPatternNames = new List<string>(plannedPatternNames);
        LastPatternItems = new List<RollPatternData>();

        for (int i = 0; i < plannedPatternData.Count; i++)
        {
            PatternRarityItem p = plannedPatternData[i];
            if (p == null) continue;
            LastPatternItems.Add(new RollPatternData(p.id, p.name, p.rarityPermille));
        }

        if (LastPatternNames.Count > 0)
            OnPatternNamesReady?.Invoke(new List<string>(LastPatternNames));

        if (LastPatternItems.Count > 0)
            OnPatternItemsReady?.Invoke(ClonePatternItemList(LastPatternItems));
    }

    private void ShowPatternImmediate(Sprite sprite, Color color)
    {
        if (patternContainer == null || patternMaterial == null) return;
        if (sprite == null) return;

        if (!patternContainer.gameObject.activeSelf)
            patternContainer.gameObject.SetActive(true);

        ClearPatternEmojis(currentPatternEmojis);

        currentPatternEmojis = CreatePatternEmojis(sprite, color, 1f);

        for (int i = 0; i < currentPatternEmojis.Count; i++)
            if (currentPatternEmojis[i] != null)
                currentPatternEmojis[i].transform.localScale = Vector3.one * fadeInScaleEnd;
    }

    private IEnumerator PatternUpdateRoutine()
    {
        if (patternContainer == null || patternMaterial == null) yield break;
        if (selectedColors.Count == 0) yield break;

        if (plannedPatternSprites == null || plannedPatternSprites.Count <= 1)
            yield break;

        float intervalDuration = scrollDuration / Mathf.Max(1, patternUpdateCount);
        int lastIndex = Mathf.Max(0, selectedColors.Count - 1);

        for (int i = 0; i < patternUpdateCount; i++)
        {
            if (skipRequested) yield break;

            int colorIndex = lastIndex - (i * 2);
            colorIndex = Mathf.Clamp(colorIndex, 0, lastIndex);

            int spritePlanIndex = Mathf.Clamp(i + 1, 1, plannedPatternSprites.Count - 1);
            Sprite nextSprite = plannedPatternSprites[spritePlanIndex];

            yield return StartCoroutine(UpdatePatternWithAnimation(colorIndex, nextSprite));

            if (i < patternUpdateCount - 1)
            {
                float wait = Mathf.Max(0f, intervalDuration - patternChangeDuration);
                float t = 0f;
                while (t < wait)
                {
                    if (skipRequested) yield break;
                    t += Time.deltaTime;
                    yield return null;
                }
            }
        }
    }

    private IEnumerator UpdatePatternWithAnimation(int updateIndex, Sprite newSprite)
    {
        updateIndex = Mathf.Clamp(updateIndex, 0, selectedColors.Count - 1);

        Color patternColor = HexToColor(selectedColors[updateIndex].hex.patternColor);
        if (patternColor == Color.white)
            patternColor = HexToColor(selectedColors[0].hex.patternColor);

        if (newSprite == null) yield break;

        List<GameObject> newPatternEmojis = CreatePatternEmojis(newSprite, patternColor, 0f);

        float elapsed = 0f;

        while (elapsed < patternChangeDuration)
        {
            if (skipRequested)
            {
                ClearPatternEmojis(currentPatternEmojis);
                for (int i = 0; i < newPatternEmojis.Count; i++)
                {
                    var emoji = newPatternEmojis[i];
                    if (emoji == null) continue;
                    CanvasGroup cg = emoji.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = 1f;
                    emoji.transform.localScale = Vector3.one * fadeInScaleEnd;
                }
                currentPatternEmojis = newPatternEmojis;
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, patternChangeDuration));
            float curveT = patternFadeCurve.Evaluate(t);

            for (int i = 0; i < currentPatternEmojis.Count; i++)
            {
                var emoji = currentPatternEmojis[i];
                if (emoji == null) continue;

                CanvasGroup cg = emoji.GetComponent<CanvasGroup>();
                if (cg == null) cg = emoji.AddComponent<CanvasGroup>();
                cg.alpha = 1f - curveT;

                float scaleValue = Mathf.Lerp(1f, fadeOutScaleMin, curveT);
                emoji.transform.localScale = Vector3.one * scaleValue;
            }

            for (int i = 0; i < newPatternEmojis.Count; i++)
            {
                var emoji = newPatternEmojis[i];
                if (emoji == null) continue;

                CanvasGroup cg = emoji.GetComponent<CanvasGroup>();
                if (cg == null) cg = emoji.AddComponent<CanvasGroup>();
                cg.alpha = curveT;

                float scaleValue = Mathf.Lerp(fadeInScaleStart, fadeInScaleEnd, curveT);
                emoji.transform.localScale = Vector3.one * scaleValue;
            }

            yield return null;
        }

        ClearPatternEmojis(currentPatternEmojis);

        for (int i = 0; i < newPatternEmojis.Count; i++)
        {
            var emoji = newPatternEmojis[i];
            if (emoji == null) continue;

            CanvasGroup cg = emoji.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;

            emoji.transform.localScale = Vector3.one * fadeInScaleEnd;
        }

        currentPatternEmojis = newPatternEmojis;
    }

    private List<GameObject> CreatePatternEmojis(Sprite sprite, Color patternColor, float initialAlpha)
    {
        if (patternContainer == null || sprite == null) return new List<GameObject>();

        List<GameObject> emojis = new List<GameObject>();
        PatternPoint[] points = DefaultPattern.Points;

        foreach (var point in points)
        {
            GameObject emojiObj = new GameObject("PatternEmoji_" + point.position.x.ToString("F3") + "_" + point.position.y.ToString("F3"));
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

            emojis.Add(emojiObj);
        }

        return emojis;
    }

    private void ClearPatternEmojis(List<GameObject> emojis)
    {
        for (int i = 0; i < emojis.Count; i++)
            if (emojis[i] != null) Destroy(emojis[i]);
        emojis.Clear();
    }

    private IEnumerator BackgroundTransitionRoutine()
    {
        if (transitionElements.Count == 0) yield break;

        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            if (skipRequested) yield break;

            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, transitionDuration));

            for (int i = 0; i < transitionElements.Count; i++)
            {
                GameObject transitionGO = transitionElements[i];
                if (transitionGO == null) continue;
                Image img = transitionGO.GetComponent<Image>();
                if (img != null && img.material != null)
                    img.material.SetFloat("_Threshold", progress);
            }

            yield return null;
        }

        for (int i = 0; i < transitionElements.Count; i++)
        {
            GameObject transitionGO = transitionElements[i];
            if (transitionGO == null) continue;
            Image img = transitionGO.GetComponent<Image>();
            if (img != null && img.material != null)
                img.material.SetFloat("_Threshold", 1f);
        }
    }

    private Texture2D CreateTextureFromColor(Color color)
    {
        Color32 color32 = (Color32)color;
        if (solidColorTextureCache.TryGetValue(color32, out Texture2D cachedTexture) && cachedTexture != null)
            return cachedTexture;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        solidColorTextureCache[color32] = texture;
        return texture;
    }

    private Color HexToColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.white;
        hex = hex.Trim();
        if (hex[0] != '#') hex = "#" + hex;
        Color c;
        if (ColorUtility.TryParseHtmlString(hex, out c)) return c;
        return Color.white;
    }

    private ItemColorData GetColorDataByName(string colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName)) return null;
        colorByName.TryGetValue(colorName, out var item);
        return item;
    }

    private PatternRarityItem GetPatternDataByName(string patternName)
    {
        if (string.IsNullOrWhiteSpace(patternName)) return null;
        patternByName.TryGetValue(patternName, out var item);
        return item;
    }

    private RollColorData GetColorRollDataByName(string colorName)
    {
        ItemColorData item = GetColorDataByName(colorName);
        if (item == null) return new RollColorData("", colorName, 0);
        return new RollColorData(item.id, item.name, item.rarityPermille);
    }

    private RollColorData CloneColorItem(RollColorData item)
    {
        if (item == null) return null;
        return new RollColorData(item.id, item.name, item.rarityPermille);
    }

    private RollPatternData ClonePatternItem(RollPatternData item)
    {
        if (item == null) return null;
        return new RollPatternData(item.id, item.name, item.rarityPermille);
    }

    private List<RollColorData> CloneColorItemList(List<RollColorData> items)
    {
        List<RollColorData> result = new List<RollColorData>(items.Count);
        for (int i = 0; i < items.Count; i++)
            result.Add(CloneColorItem(items[i]));
        return result;
    }

    private List<RollPatternData> ClonePatternItemList(List<RollPatternData> items)
    {
        List<RollPatternData> result = new List<RollPatternData>(items.Count);
        for (int i = 0; i < items.Count; i++)
            result.Add(ClonePatternItem(items[i]));
        return result;
    }
    
}
