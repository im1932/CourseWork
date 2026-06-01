using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

public class InventoryGiftPreview : MonoBehaviour
{
    [Serializable]
    private class PatternRarityJsonItem
    {
        public string id;
        public string name;
        public int rarityPermille;
    }

    [Serializable]
    private class PatternRarityJsonDatabase
    {
        public List<PatternRarityJsonItem> items;
    }

    private sealed class AnimationBindingCacheItem
    {
        public string giftId;
        public string collectionName;
        public SpriteAtlas atlas;
        public TextAsset[] animationJsonFiles;
    }

    private sealed class InventoryPreviewAnimationItemRef
    {
        public string id;
        public string name;
    }

    [Header("Data")]
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private ColorSpawner raritySource;

    [Header("Main Texts")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text numberText;

    [Header("GiftInfo Texts")]
    [SerializeField] private TMP_Text modelInfoText;
    [SerializeField] private TMP_Text symbolInfoText;
    [SerializeField] private TMP_Text backgroundInfoText;
    [SerializeField] private TMP_Text modelPercentText;
    [SerializeField] private TMP_Text symbolPercentText;
    [SerializeField] private TMP_Text backgroundPercentText;

    [Header("Preview Refs")]
    [SerializeField] private Image colorContainerImage;
    [SerializeField] private RectTransform patternRoot;
    [SerializeField] private Image patternImage;
    [SerializeField] private GameObject giftRoot;

    [Header("Preview Style")]
    [SerializeField] private Material rootMaterialTemplate;
    [SerializeField] private Material patternMaterial;
    [SerializeField] private float basePatternSize = 64f;

    private readonly List<Image> patternImages = new List<Image>();
    private readonly List<Material> patternMaterials = new List<Material>();
    private readonly List<AnimationBindingCacheItem> cachedAnimationBindings = new List<AnimationBindingCacheItem>();

    private Material runtimeRootMaterial;
    private readonly Dictionary<int, Dictionary<string, int>> patternRarityCacheByTextAssetId = new Dictionary<int, Dictionary<string, int>>();

    private void Awake()
    {
        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance;

        if (patternImage == null && patternRoot != null)
            patternImage = patternRoot.GetComponent<Image>();

        SetupRootMaterial();
        EnsurePatternCache();
        RebuildAnimationBindingCache();
    }

    private void OnEnable()
    {
        if (patternImage == null && patternRoot != null)
            patternImage = patternRoot.GetComponent<Image>();

        if (runtimeRootMaterial == null)
            SetupRootMaterial();

        EnsurePatternCache();
        if (cachedAnimationBindings.Count == 0)
            RebuildAnimationBindingCache();
    }

    public void Show(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return;

        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance;

        if (raritySource == null)
            raritySource = FindObjectOfType<ColorSpawner>(true);

        ApplyEntry(entry);
    }

    private void ApplyEntry(InventoryManager.InventoryEntry entry)
    {
        if (runtimeRootMaterial == null)
            SetupRootMaterial();

        InventoryManager.BackgroundItemData background = inventoryManager != null
            ? inventoryManager.GetBackgroundForUI(entry.backgroundId, entry.backgroundName)
            : null;

        Sprite patternSprite = inventoryManager != null
            ? inventoryManager.GetPatternSpriteForUI(entry.patternName)
            : null;

        string centerHex = background != null && background.hex != null ? background.hex.centerColor : "#FFFFFF";
        string edgeHex = background != null && background.hex != null ? background.hex.edgeColor : "#FFFFFF";
        string patternHex = background != null && background.hex != null ? background.hex.patternColor : "#FFFFFF";

        string modelName = string.IsNullOrWhiteSpace(entry.modelName) ? entry.modelId : entry.modelName;
        string patternName = string.IsNullOrWhiteSpace(entry.patternName) ? entry.patternId : entry.patternName;
        string backgroundName = string.IsNullOrWhiteSpace(entry.backgroundName) ? entry.backgroundId : entry.backgroundName;
        string giftName = !string.IsNullOrWhiteSpace(entry.giftTypeName)
            ? entry.giftTypeName
            : CaseOpeningScroll.GetGiftDisplayNameForId(entry.giftId);

        SetText(nameText, modelName);
        SetText(numberText, BuildGiftAndNumberText(giftName, entry.inventoryNumber));
        SetText(modelInfoText, modelName);
        SetText(symbolInfoText, patternName);
        SetText(backgroundInfoText, backgroundName);
        SetText(modelPercentText, ResolveModelPercentText(entry));
        SetText(symbolPercentText, ResolvePatternPercentText(entry));
        SetText(backgroundPercentText, ResolveBackgroundPercentText(entry));

        ApplyRootMaterial(centerHex, edgeHex);
        ApplyPattern(patternSprite, patternHex);
        ApplyAnimatedGift(entry);
    }

    private string BuildGiftAndNumberText(string giftName, int inventoryNumber)
    {
        string number = "#" + inventoryNumber;
        if (string.IsNullOrWhiteSpace(giftName))
            return number;

        return giftName + " " + number;
    }

    private string FormatPercent(int rarityPermille)
    {
        if (rarityPermille <= 0)
            return string.Empty;

        float value = rarityPermille / 10f;
        return value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private string ResolveModelPercentText(InventoryManager.InventoryEntry entry)
    {
        return FormatPercent(FindModelRarityPermille(entry));
    }

    private string ResolveBackgroundPercentText(InventoryManager.InventoryEntry entry)
    {
        int rarityPermille = 0;
        if (inventoryManager != null && entry != null)
        {
            InventoryManager.BackgroundItemData background = inventoryManager.GetBackgroundForUI(entry.backgroundId, entry.backgroundName);
            if (background != null)
                rarityPermille = background.rarityPermille;
        }

        return FormatPercent(rarityPermille);
    }

    private string ResolvePatternPercentText(InventoryManager.InventoryEntry entry)
    {
        return FormatPercent(FindPatternRarityPermille(entry));
    }

    private int FindModelRarityPermille(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return 0;

        if (entry.modelRarityPermille > 0)
            return entry.modelRarityPermille;

        List<string> collectionKeys = new List<string>();
        AddCollectionLookupKey(collectionKeys, entry.collectionKey);
        AddCollectionLookupKey(collectionKeys, entry.collectionName);
        AddCollectionLookupKey(collectionKeys, entry.giftId);
        AddCollectionLookupKey(collectionKeys, entry.giftTypeKey);
        AddCollectionLookupKey(collectionKeys, entry.giftTypeName);

        for (int i = 0; i < collectionKeys.Count; i++)
        {
            if (!GiftCatalogDatabase.TryLoadGiftItems(collectionKeys[i], out List<GiftCatalogDatabase.GiftItemRecord> items) || items == null)
                continue;

            for (int j = 0; j < items.Count; j++)
            {
                GiftCatalogDatabase.GiftItemRecord item = items[j];
                if (item == null)
                    continue;

                bool idMatch = !string.IsNullOrWhiteSpace(entry.modelId) &&
                               string.Equals(item.id, entry.modelId, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = !string.IsNullOrWhiteSpace(entry.modelName) &&
                                 string.Equals(item.name, entry.modelName, StringComparison.OrdinalIgnoreCase);

                if (idMatch || nameMatch)
                    return item.rarity_permille;
            }
        }

        return 0;
    }

    private int FindPatternRarityPermille(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return 0;

        if (entry.patternRarityPermille > 0)
            return entry.patternRarityPermille;

        List<string> lookupValues = BuildPatternLookupValues(entry);
        if (lookupValues.Count == 0)
            return 0;

        if (raritySource != null)
        {
            int rarityPermille = FindPatternRarityPermilleInSource(raritySource, lookupValues);
            if (rarityPermille > 0)
                return rarityPermille;
        }

        ColorSpawner[] sources = FindObjectsOfType<ColorSpawner>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            ColorSpawner source = sources[i];
            if (source == null)
                continue;

            int rarityPermille = FindPatternRarityPermilleInSource(source, lookupValues);
            if (rarityPermille > 0)
            {
                raritySource = source;
                return rarityPermille;
            }
        }

        return 0;
    }

    private List<string> BuildPatternLookupValues(InventoryManager.InventoryEntry entry)
    {
        List<string> lookupValues = new List<string>();
        AddLookupValue(lookupValues, entry.patternName);
        AddLookupValue(lookupValues, entry.patternId);

        if (inventoryManager != null)
        {
            Sprite patternSprite = inventoryManager.GetPatternSpriteForUI(entry.patternName);
            if (patternSprite != null)
                AddLookupValue(lookupValues, patternSprite.name);
        }

        return lookupValues;
    }

    private int FindPatternRarityPermilleInSource(ColorSpawner source, List<string> lookupValues)
    {
        if (source == null || lookupValues == null || lookupValues.Count == 0)
            return 0;

        Dictionary<string, int> rarityByLookup = GetPatternRarityLookup(source.patternJsonFile);
        if (rarityByLookup == null || rarityByLookup.Count == 0)
            return 0;

        for (int i = 0; i < lookupValues.Count; i++)
        {
            string lookupValue = lookupValues[i];
            if (string.IsNullOrWhiteSpace(lookupValue))
                continue;

            if (rarityByLookup.TryGetValue(lookupValue, out int rarityPermille) && rarityPermille > 0)
                return rarityPermille;
        }

        return 0;
    }

    private Dictionary<string, int> GetPatternRarityLookup(TextAsset patternJsonFile)
    {
        if (patternJsonFile == null)
            return null;

        int instanceId = patternJsonFile.GetInstanceID();
        if (patternRarityCacheByTextAssetId.TryGetValue(instanceId, out Dictionary<string, int> cachedLookup))
            return cachedLookup;

        Dictionary<string, int> lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string json = patternJsonFile.text != null ? patternJsonFile.text.Trim() : string.Empty;

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (json.StartsWith("[", StringComparison.Ordinal))
                    json = "{\"items\":" + json + "}";

                PatternRarityJsonDatabase database = JsonUtility.FromJson<PatternRarityJsonDatabase>(json);
                if (database != null && database.items != null)
                {
                    for (int i = 0; i < database.items.Count; i++)
                    {
                        PatternRarityJsonItem item = database.items[i];
                        if (item == null || item.rarityPermille <= 0)
                            continue;

                        AddPatternRarityLookupValue(lookup, item.id, item.rarityPermille);
                        AddPatternRarityLookupValue(lookup, item.name, item.rarityPermille);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        patternRarityCacheByTextAssetId[instanceId] = lookup;
        return lookup;
    }

    private void AddPatternRarityLookupValue(Dictionary<string, int> lookup, string key, int rarityPermille)
    {
        if (lookup == null || string.IsNullOrWhiteSpace(key) || rarityPermille <= 0)
            return;

        string normalizedKey = key.Trim();
        if (!lookup.ContainsKey(normalizedKey))
            lookup[normalizedKey] = rarityPermille;
    }

    private void AddCollectionLookupKey(List<string> keys, string value)
    {
        if (keys == null || string.IsNullOrWhiteSpace(value))
            return;

        string normalized = value.Trim();
        if (keys.Contains(normalized))
            return;

        keys.Add(normalized);

        string resolved = GiftCatalogDatabase.ResolveCollectionName(normalized);
        if (!string.IsNullOrWhiteSpace(resolved) && !keys.Contains(resolved))
            keys.Add(resolved);
    }

    private void AddLookupValue(List<string> values, string value)
    {
        if (values == null || string.IsNullOrWhiteSpace(value))
            return;

        string normalized = value.Trim();
        if (!values.Contains(normalized))
            values.Add(normalized);
    }

    private void SetText(TMP_Text text, string value)
    {
        if (text == null)
            return;

        text.text = value ?? string.Empty;
    }

    private void SetupRootMaterial()
    {
        if (colorContainerImage == null)
            return;

        Material template = rootMaterialTemplate;
        if (template == null && colorContainerImage.material != null)
            template = colorContainerImage.material;

        if (template == null)
            return;

        runtimeRootMaterial = new Material(template);
        colorContainerImage.material = runtimeRootMaterial;
        colorContainerImage.color = Color.white;
    }

    private void ApplyRootMaterial(string centerHex, string edgeHex)
    {
        if (colorContainerImage != null)
            colorContainerImage.enabled = true;

        if (runtimeRootMaterial == null)
            return;

        if (runtimeRootMaterial.HasProperty("_CenterColor"))
            runtimeRootMaterial.SetColor("_CenterColor", HexToColor(centerHex, Color.white));

        if (runtimeRootMaterial.HasProperty("_EdgeColor"))
            runtimeRootMaterial.SetColor("_EdgeColor", HexToColor(edgeHex, Color.white));
    }

    private void EnsurePatternCache()
    {
        if (patternRoot == null || patternImages.Count > 0)
            return;

        if (patternImage != null)
            patternImage.enabled = false;

        for (int i = patternRoot.childCount - 1; i >= 0; i--)
            Destroy(patternRoot.GetChild(i).gameObject);

        for (int i = 0; i < InventoryPattern.Points.Length; i++)
        {
            PatternPoint point = InventoryPattern.Points[i];

            GameObject item = new GameObject("Pattern_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            item.transform.SetParent(patternRoot, false);

            RectTransform rectTransform = item.GetComponent<RectTransform>();
            rectTransform.anchorMin = point.position;
            rectTransform.anchorMax = point.position;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(basePatternSize * point.scale, basePatternSize * point.scale);
            rectTransform.localScale = Vector3.one;

            Image image = item.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;

            Material material = patternMaterial != null ? new Material(patternMaterial) : null;
            if (material != null)
                image.material = material;

            patternImages.Add(image);
            patternMaterials.Add(material);
        }
    }

    private void ApplyPattern(Sprite patternSprite, string patternHex)
    {
        EnsurePatternCache();
        Color patternColor = HexToColor(patternHex, Color.white);

        for (int i = 0; i < patternImages.Count; i++)
        {
            Image image = patternImages[i];
            if (image == null)
                continue;

            PatternPoint point = InventoryPattern.Points[i];
            image.sprite = patternSprite;
            image.enabled = patternSprite != null;

            Material material = i < patternMaterials.Count ? patternMaterials[i] : null;
            Color finalColor = new Color(patternColor.r, patternColor.g, patternColor.b, point.opacity);

            if (material != null && material.HasProperty("_Color"))
            {
                material.SetColor("_Color", finalColor);
                image.color = Color.white;
            }
            else
            {
                image.color = finalColor;
            }
        }
    }

    private void ApplyAnimatedGift(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return;

        GameObject animationRoot = giftRoot;
        if (animationRoot == null)
            return;

        if (!IsAnimatedImageRuntimeSupported())
            return;

        Type animatedImageType = GetAnimatedImageType();
        if (animatedImageType == null)
            return;

        Component animatedImage = animationRoot.GetComponentInChildren(animatedImageType, true);
        if (animatedImage == null)
            return;

        TextAsset animationJson = FindAnimationJsonForEntry(entry);
        if (animationJson == null)
            return;

        if (!TryPlayAnimatedGift(animatedImage, animationJson))
            return;
    }

    private static Type GetAnimatedImageType()
    {
        Type directType =
            Type.GetType("LottiePlugin.UI.AnimatedImage") ??
            Type.GetType("LottiePlugin.UI.AnimatedImage, Assembly-CSharp");

        if (directType != null)
            return directType;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type resolvedType = assemblies[i].GetType("LottiePlugin.UI.AnimatedImage", false);
            if (resolvedType != null)
                return resolvedType;
        }

        return null;
    }

    private void RebuildAnimationBindingCache()
    {
        cachedAnimationBindings.Clear();

        IReadOnlyList<CaseOpeningScroll> sources = CaseOpeningScroll.RegisteredInstances;
        if (sources == null || sources.Count == 0)
            return;

        for (int i = 0; i < sources.Count; i++)
            CacheAnimationBindingsFromScroll(sources[i]);
    }

    private void CacheAnimationBindingsFromScroll(CaseOpeningScroll scroll)
    {
        if (scroll == null)
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo giftSourcesField = scroll.GetType().GetField("giftSources", flags);
        if (giftSourcesField == null)
            return;

        Array giftSources = giftSourcesField.GetValue(scroll) as Array;
        if (giftSources == null)
            return;

        for (int i = 0; i < giftSources.Length; i++)
        {
            object binding = giftSources.GetValue(i);
            if (binding == null)
                continue;

            AnimationBindingCacheItem cacheItem = BuildAnimationBindingCacheItem(binding);
            if (cacheItem == null || cacheItem.animationJsonFiles == null || cacheItem.animationJsonFiles.Length == 0)
                continue;

            cachedAnimationBindings.Add(cacheItem);
        }
    }

    private AnimationBindingCacheItem BuildAnimationBindingCacheItem(object binding)
    {
        if (binding == null)
            return null;

        Type bindingType = binding.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        FieldInfo giftIdField = bindingType.GetField("giftId", flags);
        FieldInfo atlasField = bindingType.GetField("atlas", flags);
        FieldInfo animationField = bindingType.GetField("animationJsonFiles", flags);

        string giftId = giftIdField != null ? NormalizeBindingValue(giftIdField.GetValue(binding) as string) : string.Empty;
        SpriteAtlas atlas = atlasField != null ? atlasField.GetValue(binding) as SpriteAtlas : null;
        TextAsset[] animationJsonFiles = animationField != null ? animationField.GetValue(binding) as TextAsset[] : null;

        if ((animationJsonFiles == null || animationJsonFiles.Length == 0) &&
            string.IsNullOrWhiteSpace(giftId) &&
            atlas == null)
        {
            return null;
        }

        return new AnimationBindingCacheItem
        {
            giftId = giftId,
            collectionName = ResolveCollectionName(giftId, atlas),
            atlas = atlas,
            animationJsonFiles = animationJsonFiles
        };
    }

    private TextAsset FindAnimationJsonForEntry(InventoryManager.InventoryEntry entry)
    {
        if (entry == null)
            return null;

        if (cachedAnimationBindings.Count == 0)
            RebuildAnimationBindingCache();

        if (cachedAnimationBindings.Count == 0)
            return null;

        string collectionKey = NormalizeBindingValue(entry.collectionKey);
        string giftId = NormalizeBindingValue(entry.giftId);
        string modelId = NormalizeBindingValue(entry.modelId);
        string modelName = NormalizeBindingValue(entry.modelName);

        TextAsset animationJson = FindAnimationJsonForEntry(entry, cachedAnimationBindings, giftId, collectionKey, modelId, modelName, requireGiftIdMatch: true, requireCollectionMatch: false);
        if (animationJson != null)
            return animationJson;

        animationJson = FindAnimationJsonForEntry(entry, cachedAnimationBindings, giftId, collectionKey, modelId, modelName, requireGiftIdMatch: false, requireCollectionMatch: true);
        if (animationJson != null)
            return animationJson;

        return FindAnimationJsonForEntry(entry, cachedAnimationBindings, giftId, collectionKey, modelId, modelName, requireGiftIdMatch: false, requireCollectionMatch: false);
    }

    private TextAsset FindAnimationJsonForEntry(
        InventoryManager.InventoryEntry entry,
        List<AnimationBindingCacheItem> bindings,
        string giftId,
        string collectionKey,
        string modelId,
        string modelName,
        bool requireGiftIdMatch,
        bool requireCollectionMatch)
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            AnimationBindingCacheItem binding = bindings[i];
            if (!BindingMatchesInventoryEntry(binding, entry, giftId, collectionKey, modelId, modelName, requireGiftIdMatch, requireCollectionMatch))
                continue;

            TextAsset animationJson = PickAnimationJsonForBinding(binding, modelId, modelName);
            if (animationJson != null)
                return animationJson;
        }

        return null;
    }

    private bool BindingMatchesInventoryEntry(
        AnimationBindingCacheItem binding,
        InventoryManager.InventoryEntry entry,
        string giftId,
        string collectionKey,
        string modelId,
        string modelName,
        bool requireGiftIdMatch,
        bool requireCollectionMatch)
    {
        if (binding == null)
            return false;

        bool giftIdMatches = !string.IsNullOrWhiteSpace(giftId) &&
                             string.Equals(binding.giftId, giftId, StringComparison.OrdinalIgnoreCase);

        bool collectionMatches = !string.IsNullOrWhiteSpace(collectionKey) &&
                                 string.Equals(binding.collectionName, collectionKey, StringComparison.OrdinalIgnoreCase);

        if (requireGiftIdMatch)
            return giftIdMatches;

        if (requireCollectionMatch)
            return collectionMatches;

        if (!string.IsNullOrWhiteSpace(collectionKey) && !collectionMatches)
            return false;

        if (giftIdMatches || collectionMatches)
            return true;

        if (binding.atlas != null && !string.IsNullOrWhiteSpace(modelName))
        {
            Sprite sprite = binding.atlas.GetSprite(modelName);
            if (sprite != null)
                return true;
        }

        InventoryPreviewAnimationItemRef itemRef = FindInventoryPreviewAnimationItem(binding, entry != null ? entry.modelId : modelId, entry != null ? entry.modelName : modelName);
        return itemRef != null;
    }

    private TextAsset PickAnimationJsonForBinding(AnimationBindingCacheItem binding, string modelId, string modelName)
    {
        if (binding == null || binding.animationJsonFiles == null || binding.animationJsonFiles.Length == 0)
            return null;

        InventoryPreviewAnimationItemRef itemRef = FindInventoryPreviewAnimationItem(binding, modelId, modelName);
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

    private InventoryPreviewAnimationItemRef FindInventoryPreviewAnimationItem(AnimationBindingCacheItem binding, string itemId, string itemName)
    {
        if (binding == null)
            return null;

        GiftCatalogDatabase.GiftItemRecord row = GiftCatalogDatabase.FindGiftItem(binding.collectionName, itemId, itemName);
        if (row == null)
            return null;

        if (binding.atlas != null && binding.atlas.GetSprite(row.name) == null)
            return null;

        return new InventoryPreviewAnimationItemRef
        {
            id = row.id,
            name = row.name
        };
    }

    private string ResolveCollectionName(string giftId, SpriteAtlas atlas)
    {
        if (atlas != null && !string.IsNullOrWhiteSpace(atlas.name))
            return NormalizeBindingValue(atlas.name);

        return NormalizeBindingValue(giftId);
    }

    private string NormalizeBindingValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace('\u00A0', ' ').Trim();
    }

    private static bool IsAnimatedImageRuntimeSupported()
    {
        if (Application.platform != RuntimePlatform.Android)
            return true;

        return System.IntPtr.Size >= 8;
    }

    private bool TryPlayAnimatedGift(Component animatedImage, TextAsset animationJson)
    {
        if (animatedImage == null || animationJson == null)
            return false;

        Type type = animatedImage.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        uint textureWidth = 512;
        uint textureHeight = 512;

        FieldInfo widthField = type.GetField("_textureWidth", flags);
        if (widthField != null && widthField.FieldType == typeof(uint))
            textureWidth = (uint)widthField.GetValue(animatedImage);

        FieldInfo heightField = type.GetField("_textureHeight", flags);
        if (heightField != null && heightField.FieldType == typeof(uint))
            textureHeight = (uint)heightField.GetValue(animatedImage);

        if (textureWidth == 0)
            textureWidth = 512;

        if (textureHeight == 0)
            textureHeight = 512;

        MethodInfo loadMethod = type.GetMethod("LoadFromAnimationJson", flags, null, new[] { typeof(string), typeof(uint), typeof(uint), typeof(string) }, null);
        if (loadMethod != null)
        {
            loadMethod.Invoke(animatedImage, new object[] { animationJson.text, textureWidth, textureHeight, string.Empty });
            InvokeAnimatedImageMethod(animatedImage, "Play");
            return true;
        }

        return false;
    }

    private void InvokeAnimatedImageMethod(Component animatedImage, string methodName)
    {
        if (animatedImage == null || string.IsNullOrWhiteSpace(methodName))
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo method = animatedImage.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
        if (method != null)
            method.Invoke(animatedImage, null);
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
}
