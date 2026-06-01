using System;
using System.Collections;
using System.Collections.Generic;
using LottiePlugin.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.U2D;

public class CaseOpeningScroll : MonoBehaviour
{
    private static readonly List<CaseOpeningScroll> registeredInstances = new List<CaseOpeningScroll>();
    public static IReadOnlyList<CaseOpeningScroll> RegisteredInstances => registeredInstances;

    [Serializable]
    private class GiftSourceBinding
    {
        public string giftId;
        public Button selectButton;
        public GameObject panelToOpen;
        public TextAsset jsonFile;
        public SpriteAtlas atlas;
        public TextAsset[] animationJsonFiles;
    }

    [Serializable]
    private class InventoryPreviewItemRef
    {
        public string id;
        public string name;
    }

    [Serializable]
    private class InventoryPreviewItemDb
    {
        public List<InventoryPreviewItemRef> items;
    }

    private static string globalSelectedGiftId;
    private bool winAlreadySent;

    [Serializable]
    public class ItemJson
    {
        public string id;
        public string name;
        public int rarityPermille;
    }

    [Serializable]
    public class JsonDb
    {
        public List<ItemJson> items;
    }

    [Serializable]
    public class RollItemData
    {
        public string id;
        public string name;
        public int rarityPermille;

        public RollItemData() { }

        public RollItemData(string id, string name, int rarityPermille)
        {
            this.id = id;
            this.name = name;
            this.rarityPermille = rarityPermille;
        }
    }

    private struct Entry
    {
        public string id;
        public string name;
        public int w;

        public Entry(string id, string name, int w)
        {
            this.id = id;
            this.name = name;
            this.w = w;
        }
    }

    private sealed class UiItem
    {
        public GameObject go;
        public RectTransform rt;
        public Image img;
        public CanvasGroup cg;
        public bool pooled;
    }

    public event Action<List<RollItemData>> OnRollItemsReady;
    public event Action<RollItemData> OnWinItemReady;

    public event Action<List<string>> OnRollNamesReady;
    public event Action<string> OnWinItemNameReady;

    public IReadOnlyList<RollItemData> LastRollItems => lastRollItems;
    public RollItemData CurrentWinItemData => currentWinItemData;

    public IReadOnlyList<string> LastRollNames => lastRollNames;
    public string CurrentWinItemName => currentWinItemData != null ? currentWinItemData.name : "";

    private readonly List<RollItemData> lastRollItems = new List<RollItemData>(64);
    private readonly List<string> lastRollNames = new List<string>(64);

    private bool isScrolling;
    private bool continueFadeForIndex1;

    private readonly List<GameObject> spawnedItems = new List<GameObject>(128);
    private readonly List<RectTransform> spawnedRT = new List<RectTransform>(128);
    private readonly List<CanvasGroup> spawnedCG = new List<CanvasGroup>(128);
    private readonly List<RollItemData> spawnedData = new List<RollItemData>(128);

    private int winItemIndex;
    private float currentSpeedMultiplier = 1f;
    private float speedVelocity;
    private RollItemData currentWinItemData;

    [Header("Scroll Settings")]
    [SerializeField] private RectTransform itemsContainer;
    [SerializeField] private RectTransform scrollViewport;

    [HideInInspector] [SerializeField] private TextAsset jsonFile;
    [HideInInspector] [SerializeField] private SpriteAtlas atlas;

    [Header("Gift Sources")]
    [SerializeField] private Transform giftButtonsRoot;
    [SerializeField] private GiftSourceBinding[] giftSources;

    [Header("Animated End Items")]
    [SerializeField] private AnimatedImage winAnimatedPrefab;
    [SerializeField] private AnimatedImage penultimateAnimatedPrefab;

    [Header("Spawn UI")]
    [SerializeField] private float itemWidth = 150f;
    [SerializeField] private float itemSpacing = 10f;

    [HideInInspector] [SerializeField] private RectTransform winItem;
    [SerializeField] private Transform existingItemParent;
    private GameObject movedExistingItem;

    private float scrollDuration = 6.49f;
    private float scrollSpeed = 0.92f;
    private int totalItems = 34;
    private float centerOffset = 18f;

    private float fadeStartDistance = 400f;
    private float fadeEndDistance = 800f;
    private float fadeCurveStrength = 2f;
    private float index1FadeDuration = 1f;
    private float index1FadeSpeed = 5f;

    private float slowdownDistance = 500f;
    private float minSpeed = 0.121f;
    private float slowdownCurveStrength = 1f;

    private float arcHeight = 15f;
    private float arcRadius = 500f;
    private float minScale = 0.75f;
    private float maxScale = 1f;

    [Header("UI")]
    [SerializeField] private Button openButton;
    [SerializeField] private Button skipButton;
    private Button nextButton;

    private int prewarmFrames = 2;
    private float maxDt = 0.0333f;

    private float uiSwapDuration = 0.4f;
    private string infoObjectName = "Info";
    private string giftInfoObjectName = "GiftInfo";

    private readonly Dictionary<string, ItemJson> itemsByName = new Dictionary<string, ItemJson>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ItemJson> itemsById = new Dictionary<string, ItemJson>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> availableEntries = new List<Entry>(256);
    private long totalWeightAvailable;

    private Coroutine scrollCoroutine;
    private Coroutine fadeCoroutine;
    private Coroutine uiSwapCoroutine;
    private bool skipRequested;
    private Vector2 cachedEndPos;

    private readonly Dictionary<GameObject, UiItem> uiMap = new Dictionary<GameObject, UiItem>(256);
    private readonly Stack<UiItem> pool = new Stack<UiItem>(256);

    private CanvasGroup containerCg;
    private CanvasGroup infoCanvasGroup;
    private CanvasGroup giftInfoCanvasGroup;
    private bool sourcesInitialized;
    private string currentGiftId;
    private string currentCollectionName;

    private const string AnimatedSlotChildName = "__AnimatedGiftItem";
    private const string NextButtonObjectName = "Next";
    private static readonly Dictionary<Button, UnityAction> registeredGiftButtonActions = new Dictionary<Button, UnityAction>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterSceneGiftSourceButtonsOnLoad()
    {
        registeredGiftButtonActions.Clear();

        CaseOpeningScroll[] allScrolls = Resources.FindObjectsOfTypeAll<CaseOpeningScroll>();
        if (allScrolls == null || allScrolls.Length == 0)
            return;

        for (int i = 0; i < allScrolls.Length; i++)
        {
            CaseOpeningScroll scroll = allScrolls[i];
            if (scroll == null || !scroll.gameObject.scene.IsValid())
                continue;

            scroll.RegisterGiftSourceButtons();
        }
    }

    private void Awake()
    {
        if (!registeredInstances.Contains(this))
            registeredInstances.Add(this);

        if (itemsContainer != null)
        {
            containerCg = itemsContainer.GetComponent<CanvasGroup>();
            if (containerCg == null) containerCg = itemsContainer.gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(infoObjectName) || string.Equals(infoObjectName, "Title", StringComparison.Ordinal))
            infoObjectName = "Info";

        if (string.IsNullOrWhiteSpace(giftInfoObjectName))
            giftInfoObjectName = "GiftInfo";
    }

    private void Start()
    {
        AutoResolveMissingGiftSourceButtons();

        if (!string.IsNullOrWhiteSpace(globalSelectedGiftId))
        {
            ApplyGiftSelection(globalSelectedGiftId);
        }
        else
        {
            RefreshRollSource();
        }

        sourcesInitialized = true;

        InitializeUiSwapState();
        ResolveUiButtons();
        ShowSkipButton();
        if (openButton != null) openButton.onClick.AddListener(StartScroll);
        if (skipButton != null) skipButton.onClick.AddListener(RequestSkip);
        RegisterGiftSourceButtons();
    }

    private void OnDestroy()
    {
        registeredInstances.Remove(this);
    }

    private void RegisterGiftSourceButtons()
    {
        if (giftSources == null)
            return;

        for (int i = 0; i < giftSources.Length; i++)
        {
            GiftSourceBinding binding = giftSources[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.giftId))
                continue;

            if (binding.selectButton == null)
                binding.selectButton = ResolveSelectButton(binding);

            if (binding.selectButton == null)
                continue;

            string capturedGiftId = binding.giftId;
            UnityAction action = () =>
            {
                HandleGiftSourceButtonClick(capturedGiftId);
            };

            RegisterGiftSourceButton(binding.selectButton, action);

            Button[] childButtons = binding.selectButton.GetComponentsInChildren<Button>(true);
            for (int buttonIndex = 0; buttonIndex < childButtons.Length; buttonIndex++)
            {
                Button childButton = childButtons[buttonIndex];
                if (childButton == null || childButton == binding.selectButton)
                    continue;

                Button parentButton = binding.selectButton;
                UnityAction childAction = () =>
                {
                    if (parentButton != null)
                        parentButton.onClick.Invoke();
                };

                RegisterGiftSourceButton(childButton, childAction);
            }
        }
    }

    private static void RegisterGiftSourceButton(Button button, UnityAction action)
    {
        if (button == null || action == null)
            return;

        UnityAction existingAction;
        if (registeredGiftButtonActions.TryGetValue(button, out existingAction) && existingAction != null)
            button.onClick.RemoveListener(existingAction);

        registeredGiftButtonActions[button] = action;
        button.onClick.AddListener(action);
    }

    public void AutoFillGiftSourceButtons()
    {
        AutoFillGiftSourceButtonsInternal(assignOnlyMissing: false);
    }

    public void AutoResolveMissingGiftSourceButtons()
    {
        AutoFillGiftSourceButtonsInternal(assignOnlyMissing: true);
    }

    private void AutoFillGiftSourceButtonsInternal(bool assignOnlyMissing)
    {
        if (giftSources == null || giftSources.Length == 0)
            return;

        Button[] orderedButtons = GetOrderedGiftButtons();
        int buttonIndex = 0;

        for (int i = 0; i < giftSources.Length; i++)
        {
            GiftSourceBinding binding = giftSources[i];
            if (binding == null)
                continue;

            if (assignOnlyMissing && binding.selectButton != null)
                continue;

            while (buttonIndex < orderedButtons.Length && orderedButtons[buttonIndex] == null)
                buttonIndex++;

            if (buttonIndex >= orderedButtons.Length)
                break;

            binding.selectButton = orderedButtons[buttonIndex];
            buttonIndex++;
        }
    }

    private Button ResolveSelectButton(GiftSourceBinding binding)
    {
        Button[] orderedButtons = GetOrderedGiftButtons();
        if (giftSources == null || orderedButtons.Length == 0)
            return null;

        for (int i = 0; i < giftSources.Length && i < orderedButtons.Length; i++)
        {
            if (giftSources[i] == binding)
                return orderedButtons[i];
        }

        return null;
    }

    private Button[] GetOrderedGiftButtons()
    {
        if (giftButtonsRoot == null)
            return Array.Empty<Button>();

        List<Button> orderedButtons = new List<Button>(giftButtonsRoot.childCount);
        for (int i = 0; i < giftButtonsRoot.childCount; i++)
        {
            Transform child = giftButtonsRoot.GetChild(i);
            if (child == null)
                continue;

            Button button = child.GetComponent<Button>();
            if (button == null)
                button = child.GetComponentInChildren<Button>(true);

            if (button != null)
                orderedButtons.Add(button);
        }

        return orderedButtons.ToArray();
    }

    private static void HandleGiftSourceButtonClick(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return;

        ApplySelectedGiftToAll(giftId);
    }

    public void SelectGift(string giftId)
    {
        ApplySelectedGiftToAll(giftId);
    }

    private static void ApplySelectedGiftToAll(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return;

        globalSelectedGiftId = giftId;

        if (registeredInstances.Count > 0)
        {
            for (int i = 0; i < registeredInstances.Count; i++)
            {
                CaseOpeningScroll scroll = registeredInstances[i];
                if (scroll == null)
                    continue;

                scroll.ApplyGiftSelection(giftId);
            }
        }

        IReadOnlyList<RandomSwitcher> allSwitchers = RandomSwitcher.RegisteredInstances;
        for (int i = 0; i < allSwitchers.Count; i++)
        {
            RandomSwitcher switcher = allSwitchers[i];
            if (switcher == null)
                continue;

            switcher.ApplySelectedGiftImmediate(giftId);
        }
    }

    public static string GetSelectedGiftId()
    {
        return globalSelectedGiftId;
    }

    public static string GetGiftDisplayNameForId(string giftId)
    {
        return FormatGiftDisplayName(giftId);
    }

    public static TextAsset FindAnimationJsonForInventoryItem(string collectionKey, string giftId, string modelId, string modelName)
    {
        if (registeredInstances.Count == 0)
            return null;

        string normalizedCollectionKey = NormalizeBindingValueStatic(collectionKey);
        string normalizedGiftId = NormalizeBindingValueStatic(giftId);
        string normalizedModelId = NormalizeBindingValueStatic(modelId);
        string normalizedModelName = NormalizeBindingValueStatic(modelName);

        if (!string.IsNullOrWhiteSpace(normalizedGiftId))
        {
            for (int i = 0; i < registeredInstances.Count; i++)
            {
                CaseOpeningScroll scroll = registeredInstances[i];
                if (scroll == null)
                    continue;

                TextAsset animationJson = scroll.FindAnimationJsonForInventoryItemInternal(normalizedCollectionKey, normalizedGiftId, normalizedModelId, normalizedModelName, true, false);
                if (animationJson != null)
                    return animationJson;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedCollectionKey))
        {
            for (int i = 0; i < registeredInstances.Count; i++)
            {
                CaseOpeningScroll scroll = registeredInstances[i];
                if (scroll == null)
                    continue;

                TextAsset animationJson = scroll.FindAnimationJsonForInventoryItemInternal(normalizedCollectionKey, normalizedGiftId, normalizedModelId, normalizedModelName, false, true);
                if (animationJson != null)
                    return animationJson;
            }
        }

        for (int i = 0; i < registeredInstances.Count; i++)
        {
            CaseOpeningScroll scroll = registeredInstances[i];
            if (scroll == null)
                continue;

            TextAsset animationJson = scroll.FindAnimationJsonForInventoryItemInternal(normalizedCollectionKey, normalizedGiftId, normalizedModelId, normalizedModelName, false, false);
            if (animationJson != null)
                return animationJson;
        }

        return null;
    }

    private void ApplyGiftSelection(string giftId)
    {
        if (!TryGetGiftSource(giftId, out GiftSourceBinding binding))
        {
            return;
        }

        currentGiftId = giftId;
        currentCollectionName = ResolveCollectionName(binding);
        jsonFile = binding.jsonFile;
        atlas = binding.atlas;

        if (sourcesInitialized || isActiveAndEnabled)
            RefreshRollSource();
    }

    public string GetCurrentGiftId()
    {
        return currentGiftId;
    }

    public string GetCurrentGiftDisplayName()
    {
        GiftSourceBinding binding = GetCurrentGiftSourceBinding();
        if (binding != null && binding.selectButton != null && !string.IsNullOrWhiteSpace(binding.selectButton.name))
            return FormatGiftDisplayName(binding.selectButton.name);

        if (!string.IsNullOrWhiteSpace(currentGiftId))
            return FormatGiftDisplayName(currentGiftId);

        return string.Empty;
    }

    private void RefreshRollSource()
    {
        LoadItemsFromJson();
        BuildAvailableEntries();
        ValidateJsonAgainstAtlas();
    }

    private bool TryGetGiftSource(string giftId, out GiftSourceBinding binding)
    {
        binding = null;

        if (giftSources == null || giftSources.Length == 0 || string.IsNullOrWhiteSpace(giftId))
            return false;

        for (int i = 0; i < giftSources.Length; i++)
        {
            GiftSourceBinding candidate = giftSources[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.giftId))
                continue;

            if (!string.Equals(candidate.giftId, giftId, StringComparison.OrdinalIgnoreCase))
                continue;

            binding = candidate;
            return true;
        }

        return false;
    }

    public void StartScroll()
    {
        if (isScrolling) return;
        if (itemsContainer == null || scrollViewport == null) return;

        skipRequested = false;
        continueFadeForIndex1 = false;
        currentSpeedMultiplier = 1f;
        speedVelocity = 0f;
        currentWinItemData = null;
        winAlreadySent = false;
        GenerateItemsPooled();

        if (winItemIndex >= 0 && winItemIndex < spawnedData.Count)
        {
            currentWinItemData = spawnedData[winItemIndex];
            if (currentWinItemData != null && !winAlreadySent)
            {
                OnWinItemReady?.Invoke(CloneItemData(currentWinItemData));
                OnWinItemNameReady?.Invoke(currentWinItemData.name);
                winAlreadySent = true;
            }
        }

        if (lastRollItems.Count > 0)
            OnRollItemsReady?.Invoke(lastRollItems);

        if (lastRollNames.Count > 0)
            OnRollNamesReady?.Invoke(lastRollNames);

        BeginUiSwapToRollState();
        ShowSkipButton();
        if (openButton != null) openButton.interactable = false;
        if (skipButton != null) skipButton.interactable = true;

        if (scrollCoroutine != null) StopCoroutine(scrollCoroutine);
        scrollCoroutine = StartCoroutine(ScrollRoutine());
    }

    private void InitializeUiSwapState()
    {
        Transform root = transform;

        Transform infoTransform = FindInfoTransform(root);
        if (infoTransform != null && !infoTransform.gameObject.activeSelf)
            infoTransform.gameObject.SetActive(true);
        infoCanvasGroup = GetOrAddCanvasGroup(infoTransform);

        Transform giftInfoTransform = FindChildByName(root, giftInfoObjectName);
        if (giftInfoTransform != null && !giftInfoTransform.gameObject.activeSelf)
            giftInfoTransform.gameObject.SetActive(true);
        giftInfoCanvasGroup = GetOrAddCanvasGroup(giftInfoTransform);

        SetCanvasGroupState(infoCanvasGroup, 1f, false, false);
        SetCanvasGroupState(giftInfoCanvasGroup, 0f, false, false);
    }

    private void BeginUiSwapToRollState()
    {
        if (uiSwapCoroutine != null)
            StopCoroutine(uiSwapCoroutine);

        uiSwapCoroutine = StartCoroutine(UiSwapRoutine());
    }

    private IEnumerator UiSwapRoutine()
    {
        CanvasGroup[] fadeOut = { infoCanvasGroup };
        CanvasGroup[] fadeIn = { giftInfoCanvasGroup };

        for (int i = 0; i < fadeOut.Length; i++)
        {
            CanvasGroup cg = fadeOut[i];
            if (cg == null) continue;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        for (int i = 0; i < fadeIn.Length; i++)
        {
            CanvasGroup cg = fadeIn[i];
            if (cg == null) continue;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        float duration = Mathf.Max(0.0001f, uiSwapDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            SetCanvasGroupAlpha(infoCanvasGroup, 1f - t);
            SetCanvasGroupAlpha(giftInfoCanvasGroup, t);
            yield return null;
        }

        SetCanvasGroupState(infoCanvasGroup, 0f, false, false);
        SetCanvasGroupState(giftInfoCanvasGroup, 1f, false, false);

        uiSwapCoroutine = null;
    }

    private Transform FindInfoTransform(Transform root)
    {
        Transform infoTransform = FindChildByName(root, infoObjectName);
        if (infoTransform != null)
            return infoTransform;

        return FindChildByName(root, "Title");
    }

    private Transform FindChildByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate != null && string.Equals(candidate.name, targetName, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    private CanvasGroup GetOrAddCanvasGroup(Transform target)
    {
        if (target == null)
            return null;

        CanvasGroup cg = target.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = target.gameObject.AddComponent<CanvasGroup>();

        return cg;
    }

    private void SetCanvasGroupState(CanvasGroup cg, float alpha, bool interactable, bool blocksRaycasts)
    {
        if (cg == null)
            return;

        cg.alpha = alpha;
        cg.interactable = interactable;
        cg.blocksRaycasts = blocksRaycasts;
    }

    private void SetCanvasGroupAlpha(CanvasGroup cg, float alpha)
    {
        if (cg == null)
            return;

        cg.alpha = alpha;
    }

    private void RequestSkip()
    {
        skipRequested = true;
        if (!isScrolling) return;

        if (scrollCoroutine != null)
        {
            StopCoroutine(scrollCoroutine);
            scrollCoroutine = null;
        }

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        continueFadeForIndex1 = false;

        if (itemsContainer != null)
        {
            itemsContainer.anchoredPosition = cachedEndPos;
            UpdateItemsArcCached();
            ForceFinalAlphasOnSkipCached();
        }

        isScrolling = false;
        ShowNextButton();

        if (openButton != null) openButton.interactable = true;
        if (skipButton != null) skipButton.interactable = true;

        if (winItemIndex >= 0 && winItemIndex < spawnedItems.Count)
            OnWinItem(spawnedItems[winItemIndex], winItemIndex);
    }

    private void ForceFinalAlphasOnSkipCached()
    {
        for (int i = 0; i < spawnedCG.Count; i++)
        {
            var cg = spawnedCG[i];
            if (cg == null) continue;
            cg.alpha = (i == winItemIndex) ? 1f : 0f;
        }
    }

    public float GetChancePercentByName(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return -1f;
        if (totalWeightAvailable <= 0) return -1f;

        for (int i = 0; i < availableEntries.Count; i++)
        {
            if (string.Equals(availableEntries[i].name, spriteName, StringComparison.OrdinalIgnoreCase))
                return availableEntries[i].w / (float)totalWeightAvailable * 100f;
        }

        return -1f;
    }

    public int GetRarityPermilleByName(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
            return -1;

        if (itemsByName.TryGetValue(spriteName, out var item) && item != null)
            return item.rarityPermille;

        return -1;
    }

    public string GetIdByName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return "";
        if (itemsByName.TryGetValue(itemName, out var item) && item != null)
            return item.id ?? "";
        return "";
    }

    public RollItemData GetCurrentWinItemClone()
    {
        return CloneItemData(currentWinItemData);
    }

    private IEnumerator ScrollRoutine()
    {
        isScrolling = true;
        skipRequested = false;
        continueFadeForIndex1 = false;
        currentSpeedMultiplier = 1f;
        speedVelocity = 0f;

        if (containerCg != null) containerCg.alpha = 1f;

        if (spawnedItems.Count == 0)
        {
            isScrolling = false;
            ShowSkipButton();
            if (openButton != null) openButton.interactable = true;
            if (skipButton != null) skipButton.interactable = true;
            yield break;
        }

        winItemIndex = Mathf.Clamp(winItemIndex, 0, spawnedItems.Count - 1);

        float viewportWidth = scrollViewport.rect.width;
        float startX = -viewportWidth * 0.18f;
        float targetX = -(-viewportWidth / 2f + itemWidth / 2f) - centerOffset;

        Vector2 startPos = new Vector2(startX, 0f);
        Vector2 endPos = new Vector2(targetX, 0f);
        cachedEndPos = endPos;

        itemsContainer.anchoredPosition = startPos;
        UpdateItemsFadeCached();
        UpdateItemsArcCached();
        Canvas.ForceUpdateCanvases();

        int frames = Mathf.Clamp(prewarmFrames, 0, 6);
        for (int i = 0; i < frames; i++)
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        float actualDuration = scrollDuration / Mathf.Max(0.0001f, scrollSpeed);
        float elapsed = 0f;
        float viewportCenterX = viewportWidth * 0.5f;

        while (elapsed < actualDuration)
        {
            if (skipRequested) break;

            float distanceToCenter = GetItem1DistanceToCenter(viewportCenterX);
            float targetSpeedMul = 1f;

            if (distanceToCenter < slowdownDistance)
            {
                float slowdownT = Mathf.Clamp01(distanceToCenter / Mathf.Max(0.0001f, slowdownDistance));
                float smoothT = Mathf.Pow(slowdownT, slowdownCurveStrength);
                targetSpeedMul = Mathf.Lerp(minSpeed, 1f, smoothT);
            }

            float dt = Time.unscaledDeltaTime;
            if (dt < 0f) dt = 0f;
            if (dt > maxDt) dt = maxDt;

            currentSpeedMultiplier = Mathf.SmoothDamp(currentSpeedMultiplier, targetSpeedMul, ref speedVelocity, 0.25f, Mathf.Infinity, dt);

            elapsed += dt * currentSpeedMultiplier;
            float t = Mathf.Clamp01(elapsed / actualDuration);

            itemsContainer.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

            UpdateItemsFadeCached();
            UpdateItemsArcCached();
            yield return null;
        }

        itemsContainer.anchoredPosition = endPos;
        UpdateItemsFadeCached();
        UpdateItemsArcCached();

        isScrolling = false;

        continueFadeForIndex1 = true;
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(ContinueFadeForIndex1());
        ShowNextButton();

        if (openButton != null) openButton.interactable = true;
        if (skipButton != null) skipButton.interactable = true;
    }

    private void ResolveUiButtons()
    {
        if (nextButton != null)
            return;

        Transform nextTransform = FindChildByName(transform, NextButtonObjectName);
        if (nextTransform != null)
            nextButton = nextTransform.GetComponent<Button>();
    }

    private void ShowSkipButton()
    {
        if (skipButton != null)
            skipButton.gameObject.SetActive(true);

        if (nextButton != null)
            nextButton.gameObject.SetActive(false);
    }

    private void ShowNextButton()
    {
        if (skipButton != null)
            skipButton.gameObject.SetActive(false);

        if (nextButton != null)
            nextButton.gameObject.SetActive(true);
    }

    private int ClampPermille(int v)
    {
        if (v <= 0) return 0;
        if (v > 1000000) return 1000000;
        return v;
    }

    private Entry? PickEntryWeighted()
    {
        if (availableEntries.Count == 0 || totalWeightAvailable <= 0) return null;

        double r = UnityEngine.Random.value * totalWeightAvailable;
        long acc = 0;

        for (int i = 0; i < availableEntries.Count; i++)
        {
            acc += availableEntries[i].w;
            if (r < acc) return availableEntries[i];
        }

        return availableEntries[availableEntries.Count - 1];
    }

    private UiItem GetPooledItem()
    {
        UiItem ui;
        if (pool.Count > 0)
        {
            ui = pool.Pop();
            ui.pooled = false;
            ui.go.SetActive(true);
            return ui;
        }

        GameObject go = new GameObject("Item", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        go.transform.SetParent(itemsContainer, false);

        ui = new UiItem
        {
            go = go,
            rt = go.GetComponent<RectTransform>(),
            img = go.GetComponent<Image>(),
            cg = go.GetComponent<CanvasGroup>(),
            pooled = false
        };

        ui.rt.sizeDelta = new Vector2(itemWidth, itemWidth);
        ui.img.preserveAspect = true;
        ui.img.raycastTarget = false;
        uiMap[go] = ui;
        return ui;
    }

    private void ReturnToPool(GameObject go)
    {
        if (go == null) return;
        if (!uiMap.TryGetValue(go, out var ui)) return;
        if (ui.pooled) return;

        ui.pooled = true;
        ui.go.SetActive(false);
        ui.go.name = "Item";
        ui.img.sprite = null;
        ui.cg.alpha = 1f;
        ui.rt.localScale = Vector3.one;
        ui.rt.anchoredPosition = Vector2.zero;
        pool.Push(ui);
    }

    private void ReparentKeepScreenPosition(RectTransform rt, Transform newParent)
    {
        if (rt == null || newParent == null) return;

        Canvas canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector3 worldCenter = rt.TransformPoint(rt.rect.center);

        rt.SetParent(newParent, true);

        RectTransform newParentRect = newParent as RectTransform;
        if (newParentRect == null) return;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(newParentRect, screenPoint, cam, out Vector2 localPoint);

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = localPoint;
    }

    private void GenerateItemsPooled()
    {
        RectTransform resolvedWin = winItem;
        Entry? winningEntryNullable = PickEntryWeighted();
        RollItemData weightedWinData = null;
        Sprite weightedWinSprite = null;

        if (winningEntryNullable.HasValue)
        {
            Entry winningEntry = winningEntryNullable.Value;
            if (!string.IsNullOrWhiteSpace(winningEntry.name))
            {
                weightedWinSprite = atlas != null ? atlas.GetSprite(winningEntry.name) : null;
                if (weightedWinSprite != null)
                    weightedWinData = new RollItemData(winningEntry.id, winningEntry.name, winningEntry.w);
            }
        }

        if (resolvedWin == null && itemsContainer != null && itemsContainer.childCount > 0)
            resolvedWin = itemsContainer.GetChild(0) as RectTransform;

        for (int i = 0; i < spawnedItems.Count; i++)
        {
            var it = spawnedItems[i];
            if (it == null) continue;
            if (movedExistingItem != null && it == movedExistingItem) continue;
            if (resolvedWin != null && it == resolvedWin.gameObject) continue;

            ReturnToPool(it);
        }

        spawnedItems.Clear();
        spawnedRT.Clear();
        spawnedCG.Clear();
        spawnedData.Clear();
        movedExistingItem = null;
        lastRollItems.Clear();
        lastRollNames.Clear();

        if (resolvedWin != null)
        {
            resolvedWin.SetParent(itemsContainer, false);
            resolvedWin.SetAsFirstSibling();
            resolvedWin.sizeDelta = new Vector2(itemWidth, itemWidth);

            if (weightedWinData != null)
            {
                Image preselectedWinImage = resolvedWin.GetComponent<Image>();
                if (preselectedWinImage != null)
                {
                    preselectedWinImage.sprite = weightedWinSprite;
                }

                resolvedWin.gameObject.name = weightedWinData.name;
            }
        }

        int normalCount = Mathf.Max(0, totalItems - 1);
        float step = itemWidth + itemSpacing;

        for (int i = 0; i < normalCount; i++)
        {
            Entry? pickedNullable = PickEntryWeighted();
            if (!pickedNullable.HasValue) continue;

            Entry picked = pickedNullable.Value;
            if (string.IsNullOrWhiteSpace(picked.name)) continue;

            Sprite s = atlas != null ? atlas.GetSprite(picked.name) : null;
            if (s == null) continue;

            UiItem ui = GetPooledItem();

            ui.go.name = picked.name;
            ui.img.sprite = s;

            ui.rt.SetParent(itemsContainer, false);
            ui.rt.SetAsLastSibling();
            ui.rt.sizeDelta = new Vector2(itemWidth, itemWidth);
            ui.rt.anchoredPosition = new Vector2(i * step, 0f);
            ui.cg.alpha = 1f;
            ui.rt.localScale = Vector3.one;

            RollItemData data = new RollItemData(picked.id, picked.name, picked.w);

            spawnedItems.Add(ui.go);
            spawnedRT.Add(ui.rt);
            spawnedCG.Add(ui.cg);
            spawnedData.Add(data);
            lastRollItems.Add(CloneItemData(data));
            lastRollNames.Add(data.name);
        }

        if (resolvedWin != null)
        {
            resolvedWin.anchoredPosition = new Vector2(normalCount * step, 0f);
            resolvedWin.SetAsFirstSibling();
            CanvasGroup winCg = resolvedWin.GetComponent<CanvasGroup>();
            if (winCg == null) winCg = resolvedWin.gameObject.AddComponent<CanvasGroup>();
            winCg.alpha = 1f;

            Image winImg = resolvedWin.GetComponent<Image>();
            RollItemData winData = null;

            if (winImg != null && winImg.sprite != null)
            {
                string winName = winImg.sprite.name;
                resolvedWin.gameObject.name = winName;
                if (weightedWinData != null && string.Equals(weightedWinData.name, winName, StringComparison.OrdinalIgnoreCase))
                    winData = CloneItemData(weightedWinData);
                else
                    winData = GetItemDataByName(winName);
            }

            if (winData == null)
            {
                if (weightedWinData != null)
                    winData = CloneItemData(weightedWinData);
                else if (currentWinItemData != null && !string.IsNullOrWhiteSpace(currentWinItemData.name))
                    winData = CloneItemData(currentWinItemData);
                else
                    winData = GetItemDataByName(SanitizeObjectName(resolvedWin.gameObject.name));
            }

            if (winData == null)
                winData = new RollItemData("", SanitizeObjectName(resolvedWin.gameObject.name), 0);

            spawnedItems.Insert(0, resolvedWin.gameObject);
            spawnedRT.Insert(0, resolvedWin);
            spawnedCG.Insert(0, winCg);
            spawnedData.Insert(0, winData);

            winItemIndex = 0;
        }
        else
        {
            winItemIndex = 0;
        }

        if (existingItemParent != null && existingItemParent.childCount > 0)
        {
            movedExistingItem = existingItemParent.GetChild(0).gameObject;

            RectTransform rt = movedExistingItem.GetComponent<RectTransform>();
            if (rt != null)
            {
                ReparentKeepScreenPosition(rt, itemsContainer);
                movedExistingItem.transform.SetAsLastSibling();

                rt.sizeDelta = new Vector2(itemWidth, itemWidth);
                rt.localRotation = Quaternion.Euler(-180f, 0f, 0f);

                CanvasGroup cg = movedExistingItem.GetComponent<CanvasGroup>();
                if (cg == null) cg = movedExistingItem.AddComponent<CanvasGroup>();
                cg.alpha = 1f;

                spawnedItems.Add(movedExistingItem);
                spawnedRT.Add(rt);
                spawnedCG.Add(cg);
                spawnedData.Add(GetItemDataByName(movedExistingItem.name));
            }
        }

        ConfigureAnimatedEndItems();
    }

    private void LoadItemsFromJson()
    {
        itemsByName.Clear();
        itemsById.Clear();
        TryLoadItemsFromDatabase();
    }

    private void BuildAvailableEntries()
    {
        availableEntries.Clear();
        totalWeightAvailable = 0;
        if (atlas == null) return;

        foreach (var kv in itemsByName)
        {
            ItemJson item = kv.Value;
            if (item == null) continue;

            string n = item.name;
            int w = ClampPermille(item.rarityPermille);
            if (string.IsNullOrWhiteSpace(n) || w <= 0) continue;

            Sprite s = atlas.GetSprite(n);
            if (s == null) continue;

            availableEntries.Add(new Entry(item.id, item.name, w));
            totalWeightAvailable += w;
        }
    }

    private void ValidateJsonAgainstAtlas()
    {
        if (itemsByName.Count == 0) return;
        if (atlas == null) return;

        foreach (var kv in itemsByName)
        {
            string n = kv.Key;
        }
    }

    private float GetItem1DistanceToCenter(float viewportCenterX)
    {
        if (spawnedRT.Count <= 1) return float.MaxValue;

        RectTransform item1 = spawnedRT[1];
        if (item1 == null) return float.MaxValue;

        float item1ScreenX = itemsContainer.anchoredPosition.x + item1.anchoredPosition.x + itemWidth * 0.5f;
        return Mathf.Abs(item1ScreenX - viewportCenterX);
    }

    private IEnumerator ContinueFadeForIndex1()
    {
        if (spawnedCG.Count <= 1) yield break;

        CanvasGroup cg = spawnedCG[1];
        if (cg == null) yield break;

        float startAlpha = cg.alpha;
        float elapsed = 0f;
        float realDuration = index1FadeDuration / Mathf.Max(0.0001f, index1FadeSpeed);

        while (elapsed < realDuration)
        {
            if (!continueFadeForIndex1 || skipRequested) yield break;

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / realDuration);
            float smoothT = Mathf.Pow(t, fadeCurveStrength);
            cg.alpha = Mathf.Lerp(startAlpha, 0f, smoothT);
            yield return null;
        }

        cg.alpha = 0f;
        continueFadeForIndex1 = false;
    }

    private void UpdateItemsArcCached()
    {
        float centerX = scrollViewport.rect.width * 0.5f;

        for (int i = 0; i < spawnedRT.Count; i++)
        {
            RectTransform item = spawnedRT[i];
            if (item == null) continue;

            float screenX = itemsContainer.anchoredPosition.x + item.anchoredPosition.x;
            float dist = Mathf.Abs(screenX - centerX);

            float t = Mathf.Clamp01(dist / Mathf.Max(0.0001f, arcRadius));
            float smoothT = t * t;

            float scale = Mathf.Lerp(maxScale, minScale, smoothT);
            float y = -Mathf.Abs(arcHeight) * (1f - smoothT);

            item.anchoredPosition = new Vector2(item.anchoredPosition.x, y);
            item.localScale = Vector3.one * scale;
        }
    }

    private void UpdateItemsFadeCached()
    {
        float viewportWidth = scrollViewport.rect.width;
        float centerX = viewportWidth * 0.5f;

        for (int i = 0; i < spawnedRT.Count; i++)
        {
            CanvasGroup cg = spawnedCG[i];
            if (cg == null) continue;

            if (i == winItemIndex)
            {
                cg.alpha = 1f;
                continue;
            }

            if (i == 1 && continueFadeForIndex1) continue;

            RectTransform item = spawnedRT[i];
            if (item == null) continue;

            float itemScreenX = itemsContainer.anchoredPosition.x + item.anchoredPosition.x + itemWidth * 0.5f;
            float distanceFromCenter = Mathf.Abs(itemScreenX - centerX);

            float alpha = 1f;

            if (distanceFromCenter > fadeStartDistance)
            {
                float fadeZoneDistance = distanceFromCenter - fadeStartDistance;
                float fadeZoneLength = fadeEndDistance - fadeStartDistance;
                float fadeProgress = Mathf.Clamp01(fadeZoneDistance / Mathf.Max(0.0001f, fadeZoneLength));
                float smoothFade = Mathf.Pow(fadeProgress, fadeCurveStrength);
                alpha = 1f - smoothFade;
            }

            cg.alpha = alpha;
        }
    }

    private RollItemData GetItemDataByName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return new RollItemData("", "", 0);

        if (itemsByName.TryGetValue(itemName, out var item) && item != null)
            return new RollItemData(item.id, item.name, item.rarityPermille);

        return new RollItemData("", itemName, 0);
    }

    private RollItemData CloneItemData(RollItemData source)
    {
        if (source == null) return null;
        return new RollItemData(source.id, source.name, source.rarityPermille);
    }

    private List<RollItemData> CloneItemList(List<RollItemData> source)
    {
        List<RollItemData> result = new List<RollItemData>(source.Count);
        for (int i = 0; i < source.Count; i++)
            result.Add(CloneItemData(source[i]));
        return result;
    }

    private void OnWinItem(GameObject item, int itemIndex)
    {
        if (item == null) return;
        if (winAlreadySent) return;

        RollItemData data = null;
        if (itemIndex >= 0 && itemIndex < spawnedData.Count)
            data = spawnedData[itemIndex];

        if (data == null)
            data = GetItemDataByName(item.name);

        currentWinItemData = CloneItemData(data);

        OnWinItemReady?.Invoke(CloneItemData(currentWinItemData));
        OnWinItemNameReady?.Invoke(currentWinItemData.name);

        winAlreadySent = true;
    }

    private void ConfigureAnimatedEndItems()
    {
        if (winItemIndex >= 0 && winItemIndex < spawnedItems.Count)
            SetupAnimatedItemSlot(spawnedItems[winItemIndex], spawnedData[winItemIndex], winAnimatedPrefab);

        if (spawnedItems.Count > 1)
        {
            CleanupAnimatedSlot(spawnedItems[1]);
            Image secondaryImage = spawnedItems[1] != null ? spawnedItems[1].GetComponent<Image>() : null;
            if (secondaryImage != null)
                secondaryImage.enabled = true;
        }
    }

    private void CleanupAnimatedEndItems()
    {
        if (winItemIndex >= 0 && winItemIndex < spawnedItems.Count)
        {
            CleanupAnimatedSlot(spawnedItems[winItemIndex]);
            Image winImage = spawnedItems[winItemIndex] != null ? spawnedItems[winItemIndex].GetComponent<Image>() : null;
            if (winImage != null)
                winImage.enabled = true;
        }

        if (spawnedItems.Count > 1)
        {
            CleanupAnimatedSlot(spawnedItems[1]);
            Image secondaryImage = spawnedItems[1] != null ? spawnedItems[1].GetComponent<Image>() : null;
            if (secondaryImage != null)
                secondaryImage.enabled = true;
        }
    }

    private void SetupAnimatedItemSlot(GameObject host, RollItemData itemData, AnimatedImage animatedPrefab)
    {
        if (host == null)
            return;

        CleanupAnimatedSlot(host);

        Image hostImage = host.GetComponent<Image>();
        if (hostImage != null)
            hostImage.enabled = true;

        TextAsset animationJson = PickAnimationJsonForItem(itemData);
        if (animatedPrefab == null || animationJson == null)
            return;

        AnimatedImage animatedInstance = Instantiate(animatedPrefab, host.transform, false);
        animatedInstance.gameObject.name = AnimatedSlotChildName;
        StretchRectTransform(animatedInstance.GetComponent<RectTransform>());

        if (!TryPlayAnimatedImage(animatedInstance, animationJson))
        {
            Destroy(animatedInstance.gameObject);
            return;
        }

        if (hostImage != null)
            hostImage.enabled = false;
    }

    private void CleanupAnimatedSlot(GameObject host)
    {
        if (host == null)
            return;

        for (int i = host.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = host.transform.GetChild(i);
            if (child != null && string.Equals(child.name, AnimatedSlotChildName, StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }

    private void StretchRectTransform(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.Euler(-180f, 0f, 0f);
    }

    private TextAsset PickAnimationJsonForItem(RollItemData itemData)
    {
        GiftSourceBinding giftSource = GetCurrentGiftSourceBinding();
        if (giftSource == null || giftSource.animationJsonFiles == null || giftSource.animationJsonFiles.Length == 0 || itemData == null)
            return null;

        ItemJson itemJson = GetItemJsonById(itemData.id);
        if (itemJson == null)
            return null;

        return PickAnimationJsonByTargets(giftSource.animationJsonFiles, itemJson.id, itemJson.name);
    }

    private GiftSourceBinding GetCurrentGiftSourceBinding()
    {
        if (string.IsNullOrWhiteSpace(currentGiftId))
            return null;

        TryGetGiftSource(currentGiftId, out GiftSourceBinding binding);
        return binding;
    }

    private TextAsset FindAnimationJsonForInventoryItemInternal(string collectionKey, string giftId, string modelId, string modelName, bool requireGiftIdMatch, bool requireCollectionMatch)
    {
        if (giftSources == null || giftSources.Length == 0)
            return null;

        for (int i = 0; i < giftSources.Length; i++)
        {
            GiftSourceBinding binding = giftSources[i];
            if (binding == null)
                continue;

            if (!BindingMatchesInventoryItem(binding, collectionKey, giftId, modelId, modelName, requireGiftIdMatch, requireCollectionMatch))
                continue;

            TextAsset animationJson = PickAnimationJsonForInventoryBinding(binding, modelId, modelName);
            if (animationJson != null)
                return animationJson;
        }

        return null;
    }

    private bool BindingMatchesInventoryItem(GiftSourceBinding binding, string collectionKey, string giftId, string modelId, string modelName, bool requireGiftIdMatch, bool requireCollectionMatch)
    {
        if (binding == null)
            return false;

        string normalizedGiftId = NormalizeBindingValue(giftId);
        string bindingGiftId = NormalizeBindingValue(binding.giftId);
        bool giftIdMatches = !string.IsNullOrWhiteSpace(normalizedGiftId) &&
                             string.Equals(bindingGiftId, normalizedGiftId, StringComparison.OrdinalIgnoreCase);

        string normalizedCollectionKey = NormalizeBindingValue(collectionKey);
        string atlasName = binding.atlas != null ? NormalizeBindingValue(binding.atlas.name) : string.Empty;
        bool collectionMatches = !string.IsNullOrWhiteSpace(normalizedCollectionKey) &&
                                 (string.Equals(bindingGiftId, normalizedCollectionKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(atlasName, normalizedCollectionKey, StringComparison.OrdinalIgnoreCase));

        if (requireGiftIdMatch)
            return giftIdMatches;

        if (requireCollectionMatch)
            return collectionMatches;

        if (!string.IsNullOrWhiteSpace(normalizedCollectionKey) && !collectionMatches)
            return false;

        if (giftIdMatches)
            return true;

        string normalizedModelName = NormalizeBindingValue(modelName);
        if (binding.atlas != null && !string.IsNullOrWhiteSpace(normalizedModelName))
        {
            Sprite sprite = binding.atlas.GetSprite(normalizedModelName);
            if (sprite != null)
                return true;
        }

        if (collectionMatches)
            return true;

        InventoryPreviewItemRef itemRef = FindInventoryPreviewItem(binding, modelId, modelName);
        return itemRef != null;
    }

    private TextAsset PickAnimationJsonForInventoryBinding(GiftSourceBinding binding, string modelId, string modelName)
    {
        if (binding == null || binding.animationJsonFiles == null || binding.animationJsonFiles.Length == 0)
            return null;

        InventoryPreviewItemRef itemRef = FindInventoryPreviewItem(binding, modelId, modelName);
        string targetId = itemRef != null ? itemRef.id : modelId;
        string targetName = itemRef != null ? itemRef.name : modelName;

        return PickAnimationJsonByTargets(binding.animationJsonFiles, targetId, targetName);
    }

    private TextAsset PickAnimationJsonByTargets(TextAsset[] animationFiles, string targetId, string targetName)
    {
        if (animationFiles == null || animationFiles.Length == 0)
            return null;

        string normalizedTargetId = NormalizeBindingValue(targetId);
        string normalizedTargetName = NormalizeBindingValue(targetName);

        List<TextAsset> idMatches = new List<TextAsset>();
        List<TextAsset> nameMatches = new List<TextAsset>();

        for (int i = 0; i < animationFiles.Length; i++)
        {
            TextAsset candidate = animationFiles[i];
            if (candidate == null)
                continue;

            string fileName = NormalizeBindingValue(candidate.name);
            if (!string.IsNullOrWhiteSpace(normalizedTargetId) &&
                string.Equals(fileName, normalizedTargetId, StringComparison.OrdinalIgnoreCase))
            {
                idMatches.Add(candidate);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedTargetName) &&
                string.Equals(fileName, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
            {
                nameMatches.Add(candidate);
            }
        }

        if (idMatches.Count > 0)
            return idMatches[UnityEngine.Random.Range(0, idMatches.Count)];

        if (nameMatches.Count > 0)
            return nameMatches[UnityEngine.Random.Range(0, nameMatches.Count)];

        return null;
    }

    private InventoryPreviewItemRef FindInventoryPreviewItem(TextAsset json, string itemId, string itemName)
    {
        if (json == null || string.IsNullOrWhiteSpace(json.text))
            return null;

        string jsonText = json.text.Trim();
        if (string.IsNullOrEmpty(jsonText))
            return null;

        try
        {
            if (jsonText.StartsWith("[", StringComparison.Ordinal))
                jsonText = "{\"items\":" + jsonText + "}";

            InventoryPreviewItemDb db = JsonUtility.FromJson<InventoryPreviewItemDb>(jsonText);
            if (db == null || db.items == null)
                return null;

            string normalizedItemId = NormalizeBindingValue(itemId);
            string normalizedItemName = NormalizeBindingValue(itemName);

            for (int i = 0; i < db.items.Count; i++)
            {
                InventoryPreviewItemRef candidate = db.items[i];
                if (candidate == null)
                    continue;

                string candidateId = NormalizeBindingValue(candidate.id);
                string candidateName = NormalizeBindingValue(candidate.name);

                if (!string.IsNullOrWhiteSpace(normalizedItemId) &&
                    string.Equals(candidateId, normalizedItemId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (!string.IsNullOrWhiteSpace(normalizedItemName) &&
                    string.Equals(candidateName, normalizedItemName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private bool TryLoadItemsFromDatabase()
    {
        GiftSourceBinding binding = GetCurrentGiftSourceBinding();
        string collectionName = ResolveDatabaseCollectionName(binding);

        if (string.IsNullOrWhiteSpace(collectionName))
            return false;

        if (!GiftCatalogDatabase.TryLoadGiftItems(collectionName, out List<GiftCatalogDatabase.GiftItemRecord> rows))
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord row = rows[i];
            if (row == null)
                continue;
            if (atlas != null && atlas.GetSprite(row.name) == null)
                continue;

            ItemJson item = new ItemJson
            {
                id = row.id,
                name = row.name,
                rarityPermille = row.rarity_permille
            };

            if (!string.IsNullOrWhiteSpace(item.id))
                itemsById[item.id] = item;
            if (!string.IsNullOrWhiteSpace(item.name))
                itemsByName[item.name] = item;
        }
        return itemsByName.Count > 0;
    }

    private string ResolveDatabaseCollectionName(GiftSourceBinding binding)
    {
        List<string> candidates = new List<string>();
        AddCollectionCandidate(candidates, currentCollectionName);
        AddCollectionCandidate(candidates, ResolveCollectionName(binding));
        AddCollectionCandidate(candidates, binding != null ? binding.giftId : string.Empty);
        AddCollectionCandidate(candidates, currentGiftId);

        for (int i = 0; i < candidates.Count; i++)
        {
            if (!GiftCatalogDatabase.TryLoadGiftItems(candidates[i], out List<GiftCatalogDatabase.GiftItemRecord> rows))
                continue;

            if (binding == null || binding.atlas == null || HasAtlasMatches(binding.atlas, rows))
                return candidates[i];
        }

        if (binding == null || binding.atlas == null)
            return string.Empty;

        List<GiftCatalogDatabase.GiftItemRecord> allRows = GiftCatalogDatabase.LoadAllGiftItems();
        Dictionary<string, int> scoreByCollection = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allRows.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord row = allRows[i];
            if (row == null || string.IsNullOrWhiteSpace(row.collection_name) || string.IsNullOrWhiteSpace(row.name))
                continue;

            if (binding.atlas.GetSprite(row.name) == null)
                continue;

            if (!scoreByCollection.TryGetValue(row.collection_name, out int score))
                score = 0;

            scoreByCollection[row.collection_name] = score + 1;
        }

        string bestCollection = string.Empty;
        int bestScore = 0;
        foreach (KeyValuePair<string, int> pair in scoreByCollection)
        {
            if (pair.Value <= bestScore)
                continue;

            bestCollection = pair.Key;
            bestScore = pair.Value;
        }

        return bestCollection;
    }

    private static void AddCollectionCandidate(List<string> candidates, string value)
    {
        string normalized = NormalizeBindingValueStatic(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], normalized, StringComparison.OrdinalIgnoreCase))
                return;
        }

        candidates.Add(normalized);
    }

    private static bool HasAtlasMatches(SpriteAtlas atlas, List<GiftCatalogDatabase.GiftItemRecord> rows)
    {
        if (atlas == null || rows == null || rows.Count == 0)
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord row = rows[i];
            if (row == null || string.IsNullOrWhiteSpace(row.name))
                continue;

            if (atlas.GetSprite(row.name) != null)
                return true;
        }

        return false;
    }

    private static bool TryPlayAnimatedImage(AnimatedImage animatedImage, TextAsset animationJson)
    {
        if (animatedImage == null || animationJson == null)
            return false;

        animatedImage.LoadFromAnimationJson(animationJson.text, 512u, 512u, string.Empty);
        animatedImage.Play();
        return true;
    }

    private InventoryPreviewItemRef FindInventoryPreviewItem(GiftSourceBinding binding, string itemId, string itemName)
    {
        string collectionName = ResolveCollectionName(binding);
        GiftCatalogDatabase.GiftItemRecord row = GiftCatalogDatabase.FindGiftItem(collectionName, itemId, itemName);
        if (row == null)
            return null;

        if (binding != null && binding.atlas != null && binding.atlas.GetSprite(row.name) == null)
        {
            return null;
        }

        return new InventoryPreviewItemRef
        {
            id = row.id,
            name = row.name
        };
    }

    private string ResolveCollectionName(GiftSourceBinding binding)
    {
        if (binding == null)
            return string.Empty;

        if (binding.atlas != null && !string.IsNullOrWhiteSpace(binding.atlas.name))
            return NormalizeBindingValue(binding.atlas.name);

        if (!string.IsNullOrWhiteSpace(binding.giftId))
            return NormalizeBindingValue(binding.giftId);

        return string.Empty;
    }

    private static string FormatGiftDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        string source = rawName.Replace("(Clone)", "").Replace('_', ' ').Replace('-', ' ').Trim();
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

            if (char.IsWhiteSpace(current))
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                    builder.Append(' ');

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString().Trim();
    }

    private static string SanitizeObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("(Clone)", "").Trim();
    }

    private ItemJson GetItemJsonById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        itemsById.TryGetValue(itemId, out ItemJson item);
        return item;
    }

    private string NormalizeBindingValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace('\u00A0', ' ').Trim();
    }

    private static string NormalizeBindingValueStatic(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace('\u00A0', ' ').Trim();
    }

}
