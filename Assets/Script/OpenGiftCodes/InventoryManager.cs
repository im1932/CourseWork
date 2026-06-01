using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;
using TMPro;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }
    public IReadOnlyList<InventoryEntry> Items => inventoryItems;
    public event Action InventoryChanged;

    [Serializable]
    public class InventoryEntry
    {
        public int inventoryNumber;
        public string collectionKey;
        public string collectionName;
        public string giftTypeKey;
        public string giftTypeName;
        public string giftId;

        public string uniqueDropId;
        public string createdAt;

        public string modelId;
        public string modelName;
        public int modelRarityPermille;

        public string backgroundId;
        public string backgroundName;
        public int backgroundRarityPermille;

        public string patternId;
        public string patternName;
        public int patternRarityPermille;
    }

    [Serializable]
    private class InventorySaveData
    {
        public List<InventoryEntry> items = new List<InventoryEntry>();
    }

    [Serializable]
    public class ColorHexData
    {
        public string centerColor;
        public string edgeColor;
        public string patternColor;
        public string textColor;
    }

    [Serializable]
    public class BackgroundItemData
    {
        public string id;
        public string name;
        public int rarityPermille;
        public ColorHexData hex;
    }

    [Serializable]
    public class BackgroundDatabase
    {
        public List<BackgroundItemData> items;
    }

    [Serializable]
    public class ModelCatalogItemData
    {
        public string id;
        public string name;
        public int rarityPermille;
    }

    [Serializable]
    public class ModelCatalogDatabase
    {
        public List<ModelCatalogItemData> items;
    }

    [Serializable]
    public class ModelCatalogBinding
    {
        public string collectionKey;
        public TextAsset jsonFile;
        public SpriteAtlas atlas;
    }

    private struct ReservedRollNumber
    {
        public string collectionKey;
        public int inventoryNumber;
    }

    private const bool IncludeInactiveSources = true;

    [Header("Inventory UI")]
    [SerializeField] private Transform content;
    [SerializeField] private GameObject inventoryItemPrefab;

    [Header("Virtualized UI")]
    [SerializeField] private UIoptimazed virtualizedView;

    [Header("Catalogs")]
    [SerializeField] private TextAsset backgroundJsonFile;
    [SerializeField] private List<ModelCatalogBinding> modelCatalogs = new List<ModelCatalogBinding>();
    [SerializeField] private Sprite[] patternSprites;

    [Header("Pattern")]
    [SerializeField] private Material patternMaterial;
    [SerializeField] private float basePatternSize = 64f;

    [Header("Model")]
    [SerializeField] private Vector2 modelSize = new Vector2(110f, 110f);
    [SerializeField] private bool preserveAspect = true;

    [Header("Inventory Root Material")]
    [SerializeField] private Material inventoryItemParentMaterial;

    [Header("Save")]
    [SerializeField] private string saveKey = DefaultSaveKey;

    private const string inventoryCounterKeyPrefix = "inventory_counter_collection_";
    private const string DefaultSaveKey = "inventory_save_v1";

    private readonly List<InventoryEntry> inventoryItems = new List<InventoryEntry>();
    private readonly Dictionary<string, BackgroundItemData> backgroundsById = new Dictionary<string, BackgroundItemData>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BackgroundItemData> backgroundsByName = new Dictionary<string, BackgroundItemData>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> patternSpriteByName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> modelNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteAtlas> modelAtlasById = new Dictionary<string, SpriteAtlas>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> modelCollectionById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> modelSpriteById = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> missingModelSpriteIdsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<CaseOpeningScroll, ColorSpawner> rollToVisualMap = new Dictionary<CaseOpeningScroll, ColorSpawner>();
    private readonly Dictionary<CaseOpeningScroll, Action<CaseOpeningScroll.RollItemData>> rollHandlers = new Dictionary<CaseOpeningScroll, Action<CaseOpeningScroll.RollItemData>>();
    private readonly Dictionary<CaseOpeningScroll, ReservedRollNumber> reservedNumbersByRoll = new Dictionary<CaseOpeningScroll, ReservedRollNumber>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        LoadBackgroundCatalog();
        LoadModelCatalogs();
        BuildPatternSpriteMap();
        LoadInventory();
        AlbumCollectionProgressStore.RebuildFromInventory(inventoryItems);
        SyncCounterWithInventory();
        RebuildGrid();
    }
private void OnDisable()
    {
        UnbindAllSources();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static int GetLastSavedInventoryNumber()
    {
        return GetLastSavedInventoryNumber("default");
    }

    public static int GetLastSavedInventoryNumber(string collectionKey)
    {
        string normalizedKey = NormalizeCollectionKey(collectionKey);
        return PlayerPrefs.GetInt(GetInventoryCounterKey(normalizedKey), 0);
    }

    public static int PeekNextInventoryNumber()
    {
        return GetLastSavedInventoryNumber() + 1;
    }

    public static int PeekNextInventoryNumber(string collectionKey)
    {
        return GetLastSavedInventoryNumber(collectionKey) + 1;
    }

    private static void SetLastSavedInventoryNumber(string collectionKey, int value)
    {
        string normalizedKey = NormalizeCollectionKey(collectionKey);
        PlayerPrefs.SetInt(GetInventoryCounterKey(normalizedKey), Mathf.Max(0, value));
        PlayerPrefs.Save();
    }

    private int ReserveNextInventoryNumber(string collectionKey)
    {
        int next = GetLastSavedInventoryNumber(collectionKey) + 1;
        SetLastSavedInventoryNumber(collectionKey, next);
        return next;
    }

    private void SyncCounterWithInventory()
    {
        bool inventoryChanged = false;
        Dictionary<string, int> nextNumberByCollection = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < inventoryItems.Count; i++)
        {
            if (inventoryItems[i] == null)
                continue;

            InventoryEntry entry = inventoryItems[i];
            string resolvedCollectionKey = ResolveCollectionKey(entry.modelId, entry.collectionKey);
            if (!string.Equals(entry.collectionKey, resolvedCollectionKey, StringComparison.OrdinalIgnoreCase))
            {
                entry.collectionKey = resolvedCollectionKey;
                inventoryChanged = true;
            }

            if (string.IsNullOrWhiteSpace(entry.collectionName))
            {
                entry.collectionName = entry.collectionKey;
                inventoryChanged = true;
            }

            entry.giftTypeKey = NormalizeGiftTypeKey(entry.giftTypeKey);

            string normalizedDisplayGiftName = ResolveGiftDisplayName(entry.giftTypeName, entry.giftId, entry.giftTypeKey);
            if (!string.Equals(entry.giftTypeName, normalizedDisplayGiftName, StringComparison.Ordinal))
            {
                entry.giftTypeName = normalizedDisplayGiftName;
                inventoryChanged = true;
            }

            if (string.IsNullOrWhiteSpace(entry.giftId))
            {
                entry.giftId = NormalizeGiftId(entry.giftTypeKey);
                inventoryChanged = true;
            }

            int nextNumber = 1;
            if (nextNumberByCollection.TryGetValue(entry.collectionKey, out int existingNext))
                nextNumber = existingNext;

            if (entry.inventoryNumber != nextNumber)
            {
                entry.inventoryNumber = nextNumber;
                inventoryChanged = true;
            }

            nextNumberByCollection[entry.collectionKey] = nextNumber + 1;
        }

        foreach (var pair in nextNumberByCollection)
        {
            SetLastSavedInventoryNumber(pair.Key, pair.Value - 1);
        }

        if (inventoryChanged)
            SaveInventory();
    }

    public void RefreshSourceBindings()
    {
        UnbindAllSources();
        BindAllSourcesInScene();
    }

    private void BindAllSourcesInScene()
    {
        IReadOnlyList<CaseOpeningScroll> modelRolls = CaseOpeningScroll.RegisteredInstances;
        IReadOnlyList<ColorSpawner> visualRolls = ColorSpawner.RegisteredInstances;

        rollToVisualMap.Clear();
        rollHandlers.Clear();

        for (int i = 0; i < modelRolls.Count; i++)
        {
            CaseOpeningScroll modelRoll = modelRolls[i];
            if (modelRoll == null)
                continue;

            ColorSpawner visualRoll = FindBestVisualRollFor(modelRoll, visualRolls);
            rollToVisualMap[modelRoll] = visualRoll;

            Action<CaseOpeningScroll.RollItemData> handler = (modelItem) =>
            {
                HandleModelReady(modelRoll, modelItem);
            };

            rollHandlers[modelRoll] = handler;
            modelRoll.OnWinItemReady += handler;
        }
    }

    private void UnbindAllSources()
    {
        foreach (var pair in rollHandlers)
        {
            if (pair.Key != null)
                pair.Key.OnWinItemReady -= pair.Value;
        }

        rollHandlers.Clear();
        rollToVisualMap.Clear();
    }

    private ColorSpawner FindBestVisualRollFor(CaseOpeningScroll modelRoll, IReadOnlyList<ColorSpawner> visualRolls)
    {
        if (modelRoll == null || visualRolls == null || visualRolls.Count == 0)
            return null;

        Transform modelTransform = modelRoll.transform;

        for (Transform parent = modelTransform; parent != null; parent = parent.parent)
        {
            for (int i = 0; i < visualRolls.Count; i++)
            {
                if (visualRolls[i] == null)
                    continue;

                if (visualRolls[i].transform.IsChildOf(parent) || visualRolls[i].transform == parent)
                    return visualRolls[i];
            }
        }

        ColorSpawner nearest = null;
        float bestDistance = float.MaxValue;
        Vector3 modelPos = modelTransform.position;

        for (int i = 0; i < visualRolls.Count; i++)
        {
            if (visualRolls[i] == null)
                continue;

            float distance = Vector3.SqrMagnitude(modelPos - visualRolls[i].transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = visualRolls[i];
            }
        }

        return nearest;
    }

    private void HandleModelReady(CaseOpeningScroll modelRoll, CaseOpeningScroll.RollItemData modelItem)
    {
        if (modelRoll == null || modelItem == null)
            return;

        GetOrCreateCurrentNumberForRoll(modelRoll, modelItem);
    }

    public int GetOrCreateCurrentNumberForRoll(CaseOpeningScroll modelRoll)
    {
        return GetOrCreateCurrentNumberForRoll(modelRoll, GetCurrentModelItem(modelRoll));
    }

    public int GetOrCreateCurrentNumberForRoll(CaseOpeningScroll modelRoll, CaseOpeningScroll.RollItemData modelItem)
    {
        if (modelRoll == null)
            return 0;

        string collectionKey = ResolveCollectionKey(modelItem != null ? modelItem.id : "", "");

        if (reservedNumbersByRoll.TryGetValue(modelRoll, out ReservedRollNumber existing) &&
            existing.inventoryNumber > 0 &&
            string.Equals(existing.collectionKey, collectionKey, StringComparison.OrdinalIgnoreCase))
        {
            return existing.inventoryNumber;
        }

        int newNumber = ReserveNextInventoryNumber(collectionKey);
        reservedNumbersByRoll[modelRoll] = new ReservedRollNumber
        {
            collectionKey = collectionKey,
            inventoryNumber = newNumber
        };
        return newNumber;
    }

    public int GetCurrentNumberForRoll(CaseOpeningScroll modelRoll)
    {
        if (modelRoll == null)
            return 0;

        if (reservedNumbersByRoll.TryGetValue(modelRoll, out ReservedRollNumber existing))
            return existing.inventoryNumber;

        return 0;
    }

    public bool SaveCurrentFromRoll(CaseOpeningScroll modelRoll)
    {
        if (modelRoll == null)
            return false;

        if (!rollToVisualMap.ContainsKey(modelRoll))
            RefreshSourceBindings();

        CaseOpeningScroll.RollItemData modelItem = GetCurrentModelItem(modelRoll);
        return TrySaveFromResolvedData(modelRoll, modelItem);
    }

    public bool SaveCurrentFromNearestRoll(Transform sourceTransform)
    {
        if (sourceTransform == null)
            return false;

        IReadOnlyList<CaseOpeningScroll> rolls = CaseOpeningScroll.RegisteredInstances;
        if (rolls == null || rolls.Count == 0)
            return false;

        CaseOpeningScroll nearest = null;
        float bestDistance = float.MaxValue;
        Vector3 pos = sourceTransform.position;

        for (int i = 0; i < rolls.Count; i++)
        {
            if (rolls[i] == null)
                continue;

            float distance = Vector3.SqrMagnitude(pos - rolls[i].transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = rolls[i];
            }
        }

        return SaveCurrentFromRoll(nearest);
    }

    public int SaveCurrentFromAllRolls()
    {
        RefreshSourceBindings();

        int savedCount = 0;
        foreach (var pair in rollToVisualMap)
        {
            if (pair.Key == null)
                continue;

            CaseOpeningScroll.RollItemData modelItem = GetCurrentModelItem(pair.Key);
            if (TrySaveFromResolvedData(pair.Key, modelItem))
                savedCount++;
        }

        return savedCount;
    }

    private bool TrySaveFromResolvedData(CaseOpeningScroll modelRoll, CaseOpeningScroll.RollItemData modelItem)
    {
        if (modelRoll == null)
        {
            return false;
        }

        if (modelItem == null)
        {
            return false;
        }

        if (!rollToVisualMap.TryGetValue(modelRoll, out ColorSpawner visualRoll) || visualRoll == null)
        {
            return false;
        }

        object bg = GetCurrentColorItem(visualRoll);
        object pattern = GetCurrentPatternItem(visualRoll);

        if (bg == null)
        {
            return false;
        }

        if (pattern == null)
        {
            return false;
        }

        string bgId = Safe(GetStringMember(bg, "id"));
        string bgName = Safe(GetStringMember(bg, "name"));
        int bgRarity = GetIntMember(bg, "rarityPermille");

        string patternId = Safe(GetStringMember(pattern, "id"));
        string patternName = Safe(GetStringMember(pattern, "name"));
        int patternRarity = GetIntMember(pattern, "rarityPermille");

        string collectionKey = ResolveCollectionKey(modelItem.id, "");
        string giftTypeKey = GetGiftTypeKey(modelRoll);
        string giftTypeName = GetGiftTypeDisplayName(modelRoll);
        string giftId = GetGiftId(modelRoll);
        int assignedNumber = GetOrCreateCurrentNumberForRoll(modelRoll, modelItem);

        InventoryEntry entry = new InventoryEntry
        {
            inventoryNumber = assignedNumber,
            collectionKey = collectionKey,
            collectionName = collectionKey,
            giftTypeKey = giftTypeKey,
            giftTypeName = giftTypeName,
            giftId = giftId,
            uniqueDropId = Guid.NewGuid().ToString("N"),
            createdAt = DateTime.UtcNow.ToString("o"),

            modelId = Safe(modelItem.id),
            modelName = Safe(modelItem.name),
            modelRarityPermille = modelItem.rarityPermille,

            backgroundId = bgId,
            backgroundName = bgName,
            backgroundRarityPermille = bgRarity,

            patternId = patternId,
            patternName = patternName,
            patternRarityPermille = patternRarity
        };

        inventoryItems.Add(entry);

        SaveInventory();

        if (virtualizedView != null)
            virtualizedView.ReloadFromInventory();
        else
            AddEntryToGrid(entry, entry.inventoryNumber);

        reservedNumbersByRoll.Remove(modelRoll);
        return true;
    }

private object GetCurrentColorItem(ColorSpawner visualRoll)
{
    if (visualRoll == null)
        return null;

    if (visualRoll.LastColorItems != null && visualRoll.LastColorItems.Count > 0)
        return visualRoll.LastColorItems[0];

    if (visualRoll.CurrentWinColorItem != null)
        return visualRoll.CurrentWinColorItem;

    return null;
}

private object GetCurrentPatternItem(ColorSpawner visualRoll)
{
    if (visualRoll == null)
        return null;

    if (visualRoll.CurrentWinPatternItem != null)
        return visualRoll.CurrentWinPatternItem;

    if (visualRoll.LastPatternItems != null && visualRoll.LastPatternItems.Count > 0)
        return visualRoll.LastPatternItems[visualRoll.LastPatternItems.Count - 1];

    return null;
}

    private string GetStringMember(object target, string memberName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return "";

        Type type = target.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            object value = property.GetValue(target, null);
            return value != null ? value.ToString() : "";
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            object value = field.GetValue(target);
            return value != null ? value.ToString() : "";
        }

        return "";
    }

    private int GetIntMember(object target, string memberName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return 0;

        Type type = target.GetType();

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
        {
            object value = property.GetValue(target, null);
            if (value is int intValue)
                return intValue;

            if (value != null && int.TryParse(value.ToString(), out int parsedProperty))
                return parsedProperty;
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            object value = field.GetValue(target);
            if (value is int intValue)
                return intValue;

            if (value != null && int.TryParse(value.ToString(), out int parsedField))
                return parsedField;
        }

        return 0;
    }

    private CaseOpeningScroll.RollItemData GetCurrentModelItem(CaseOpeningScroll modelRoll)
    {
        if (modelRoll == null)
            return null;

        Type type = modelRoll.GetType();

        string[] propertyNames = new string[]
        {
            "CurrentWinItemData",
            "CurrentWinItem",
            "CurrentItem",
            "WinItem",
            "SelectedItem",
            "LastWinItem",
            "CurrentRollItem"
        };

        for (int i = 0; i < propertyNames.Length; i++)
        {
            PropertyInfo property = type.GetProperty(propertyNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && typeof(CaseOpeningScroll.RollItemData).IsAssignableFrom(property.PropertyType))
            {
                object value = property.GetValue(modelRoll, null);
                if (value is CaseOpeningScroll.RollItemData data)
                    return data;
            }
        }

        string[] fieldNames = new string[]
        {
            "currentWinItemData",
            "currentWinItem",
            "currentItem",
            "winItem",
            "selectedItem",
            "lastWinItem",
            "currentRollItem"
        };

        for (int i = 0; i < fieldNames.Length; i++)
        {
            FieldInfo field = type.GetField(fieldNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && typeof(CaseOpeningScroll.RollItemData).IsAssignableFrom(field.FieldType))
            {
                object value = field.GetValue(modelRoll);
                if (value is CaseOpeningScroll.RollItemData data)
                    return data;
            }
        }

        return null;
    }

    private void LoadBackgroundCatalog()
    {
        backgroundsById.Clear();
        backgroundsByName.Clear();

        if (backgroundJsonFile == null)
            return;

        string json = backgroundJsonFile.text != null ? backgroundJsonFile.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            if (json.StartsWith("["))
                json = "{\"items\":" + json + "}";

            BackgroundDatabase db = JsonUtility.FromJson<BackgroundDatabase>(json);
            if (db == null || db.items == null)
                return;

            for (int i = 0; i < db.items.Count; i++)
            {
                BackgroundItemData item = db.items[i];
                if (item == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(item.id))
                    backgroundsById[item.id] = item;

                if (!string.IsNullOrWhiteSpace(item.name))
                    backgroundsByName[item.name] = item;
            }
        }
        catch (Exception)
        {
        }
    }

    private void BuildPatternSpriteMap()
    {
        patternSpriteByName.Clear();

        if (patternSprites == null)
            return;

        for (int i = 0; i < patternSprites.Length; i++)
        {
            Sprite s = patternSprites[i];
            if (s == null)
                continue;

            if (!string.IsNullOrWhiteSpace(s.name))
                patternSpriteByName[s.name] = s;
        }
    }

    private void LoadModelCatalogs()
    {
        modelNameById.Clear();
        modelAtlasById.Clear();
        modelCollectionById.Clear();
        modelSpriteById.Clear();
        missingModelSpriteIdsLogged.Clear();

        if (modelCatalogs == null)
            return;

        for (int i = 0; i < modelCatalogs.Count; i++)
        {
            ModelCatalogBinding binding = modelCatalogs[i];
            if (binding == null)
                continue;

            string collectionKey = NormalizeCollectionKey(binding.collectionKey);
            List<ModelCatalogItemData> items = LoadModelCatalogItems(collectionKey, binding);
            for (int j = 0; j < items.Count; j++)
            {
                ModelCatalogItemData item = items[j];
                if (item == null || string.IsNullOrWhiteSpace(item.id) || string.IsNullOrWhiteSpace(item.name))
                    continue;

                modelNameById[item.id] = item.name;
                modelCollectionById[item.id] = collectionKey;
                if (binding.atlas != null)
                    modelAtlasById[item.id] = binding.atlas;
            }
        }
    }

    private void LoadInventory()
    {
        inventoryItems.Clear();
        if (!GiftCatalogDatabase.HasInventoryRows(GetActiveSaveKey()))
            return;

        List<GiftCatalogDatabase.InventoryRecord> rows = GiftCatalogDatabase.LoadInventory(GetActiveSaveKey());

        for (int i = 0; i < rows.Count; i++)
        {
            GiftCatalogDatabase.InventoryRecord row = rows[i];
            if (row == null)
                continue;

            inventoryItems.Add(new InventoryEntry
            {
                inventoryNumber = row.inventory_number,
                collectionKey = ResolveCollectionKey(row.model_id, ""),
                collectionName = ResolveCollectionKey(row.model_id, ""),
                giftTypeKey = NormalizeGiftTypeKey(row.gift_type_name),
                giftTypeName = ResolveGiftDisplayName(row.gift_type_name, row.gift_id, row.gift_type_name),
                giftId = NormalizeGiftId(row.gift_id),
                uniqueDropId = Guid.NewGuid().ToString("N"),
                createdAt = Safe(row.created_at),
                modelId = Safe(row.model_id),
                modelName = Safe(row.model_name),
                modelRarityPermille = row.model_rarity_permille,
                backgroundId = string.Empty,
                backgroundName = Safe(row.background_name),
                patternId = string.Empty,
                patternName = Safe(row.pattern_name)
            });
        }
    }

    private void SaveInventory()
    {
        List<GiftCatalogDatabase.InventoryRecord> rows = new List<GiftCatalogDatabase.InventoryRecord>(inventoryItems.Count);
        for (int i = 0; i < inventoryItems.Count; i++)
        {
            InventoryEntry entry = inventoryItems[i];
            if (entry == null)
                continue;

            rows.Add(new GiftCatalogDatabase.InventoryRecord
            {
                inventory_number = entry.inventoryNumber,
                gift_id = Safe(entry.giftId),
                gift_type_name = Safe(entry.giftTypeName),
                model_id = Safe(entry.modelId),
                model_name = Safe(entry.modelName),
                model_rarity_permille = entry.modelRarityPermille,
                background_name = Safe(entry.backgroundName),
                pattern_name = Safe(entry.patternName),
                created_at = Safe(entry.createdAt)
            });
        }

        GiftCatalogDatabase.ReplaceInventory(GetActiveSaveKey(), rows);
        AlbumCollectionProgressStore.RebuildFromInventory(inventoryItems);
        if (AchievementManager.Instance != null)
            AchievementManager.Instance.RefreshInventoryAchievementsImmediately();
        AlbumPreviewPanelOpener.NotifyInventorySaved();
        NotifyInventoryChanged();
    }

    public void RebuildGrid()
    {
        if (virtualizedView != null)
        {
            virtualizedView.ReloadFromInventory();
            return;
        }

        if (content == null || inventoryItemPrefab == null)
            return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        for (int i = 0; i < inventoryItems.Count; i++)
            AddEntryToGrid(inventoryItems[i], inventoryItems[i].inventoryNumber);
    }

    private void AddEntryToGrid(InventoryEntry entry, int number)
    {
        if (content == null || inventoryItemPrefab == null || entry == null)
            return;

        GameObject go = Instantiate(inventoryItemPrefab, content);
        go.name = "InventoryItem_" + number;

        ApplyEntryToPrefab(go, entry, number);

        AutoScrollHeight grid = content.GetComponent<AutoScrollHeight>();
        if (grid != null)
            grid.UpdateHeight();
    }

    private void ApplyEntryToPrefab(GameObject itemGO, InventoryEntry entry, int number)
    {
        Transform patternRoot = itemGO.transform.Find("2Dmask/Pattern");
        Transform modelRoot = itemGO.transform.Find("Model");
        Transform numRoot = itemGO.transform.Find("Num");

        Image rootImage = itemGO.GetComponent<Image>();
        Image modelImage = modelRoot != null ? modelRoot.GetComponent<Image>() : null;
        Image numImage = numRoot != null ? numRoot.GetComponent<Image>() : null;

        Text numberText = null;
        TMP_Text numberTMP = null;

        if (numRoot != null)
        {
            numberText = numRoot.GetComponent<Text>();
            if (numberText == null)
                numberText = numRoot.GetComponentInChildren<Text>(true);

            numberTMP = numRoot.GetComponent<TMP_Text>();
            if (numberTMP == null)
                numberTMP = numRoot.GetComponentInChildren<TMP_Text>(true);
        }

        RectTransform patternContainer = patternRoot as RectTransform;

        Sprite modelSprite = GetModelSprite(entry.modelId);
        Sprite patternSprite = GetPatternSprite(entry.patternName);
        BackgroundItemData bg = GetBackground(entry.backgroundId, entry.backgroundName);

        string centerHex = bg != null && bg.hex != null ? bg.hex.centerColor : "#FFFFFF";
        string edgeHex = bg != null && bg.hex != null ? bg.hex.edgeColor : "#FFFFFF";
        string patternHex = bg != null && bg.hex != null ? bg.hex.patternColor : "#FFFFFF";

        SetNumber(number, numberText, numberTMP, numImage, edgeHex);
        SetRootMaterial(rootImage, centerHex, edgeHex);
        SetModel(modelImage, modelSprite);
        SetPattern(patternContainer, patternSprite, patternHex);
    }

    private void SetNumber(int itemNumber, Text numberText, TMP_Text numberTMP, Image numImage, string edgeHex)
    {
        string value = "#" + itemNumber;
        Color numberColor = HexToColor(edgeHex, Color.white);

        if (numImage != null)
            numImage.color = numberColor;

        if (numberText != null)
        {
            numberText.gameObject.SetActive(true);
            numberText.text = value;
        }

        if (numberTMP != null)
        {
            numberTMP.gameObject.SetActive(true);
            numberTMP.text = value;
        }
    }

    private void SetRootMaterial(Image rootImage, string centerHex, string edgeHex)
    {
        if (rootImage == null || inventoryItemParentMaterial == null)
            return;

        Color center = HexToColor(centerHex, Color.white);
        Color edge = HexToColor(edgeHex, Color.white);

        Material runtimeMat = new Material(inventoryItemParentMaterial);
        rootImage.material = runtimeMat;
        rootImage.color = Color.white;

        if (runtimeMat.HasProperty("_CenterColor"))
            runtimeMat.SetColor("_CenterColor", center);

        if (runtimeMat.HasProperty("_EdgeColor"))
            runtimeMat.SetColor("_EdgeColor", edge);
    }

    private void SetModel(Image modelImage, Sprite modelSprite)
    {
        if (modelImage == null)
            return;

        modelImage.sprite = modelSprite;
        modelImage.preserveAspect = preserveAspect;
        modelImage.enabled = modelSprite != null;

        RectTransform rt = modelImage.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = modelSize;
        rt.localScale = Vector3.one;
    }

    private void SetPattern(RectTransform patternContainer, Sprite patternSprite, string patternHex)
    {
        if (patternContainer == null)
            return;

        for (int i = patternContainer.childCount - 1; i >= 0; i--)
            Destroy(patternContainer.GetChild(i).gameObject);

        if (patternSprite == null)
            return;

        Color patternColor = HexToColor(patternHex, Color.white);

        for (int i = 0; i < InventoryPattern.Points.Length; i++)
        {
            PatternPoint point = InventoryPattern.Points[i];

            GameObject go = new GameObject("Pattern_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(patternContainer, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = point.position;
            rt.anchorMax = point.position;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            float size = basePatternSize * point.scale;
            rt.sizeDelta = new Vector2(size, size);
            rt.localScale = Vector3.one;

            Image img = go.GetComponent<Image>();
            img.sprite = patternSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;

            if (patternMaterial != null)
            {
                Material mat = new Material(patternMaterial);
                img.material = mat;

                Color finalColor = new Color(patternColor.r, patternColor.g, patternColor.b, point.opacity);

                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", finalColor);
                else
                    img.color = finalColor;
            }
            else
            {
                img.color = new Color(patternColor.r, patternColor.g, patternColor.b, point.opacity);
            }

            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 1f;
        }
    }

    private Sprite GetModelSprite(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        if (modelSpriteById.TryGetValue(modelId, out Sprite cachedSprite))
            return cachedSprite;

        string resolvedModelName = ResolveModelName(modelId);

        if (modelAtlasById.TryGetValue(modelId, out SpriteAtlas atlasById) &&
            atlasById != null)
        {
            Sprite sprite = null;
            if (!string.IsNullOrWhiteSpace(resolvedModelName))
                sprite = atlasById.GetSprite(resolvedModelName);

            if (sprite == null)
                sprite = atlasById.GetSprite(modelId);

            modelSpriteById[modelId] = sprite;
            if (sprite == null)
                missingModelSpriteIdsLogged.Add(modelId);
            return sprite;
        }

        modelSpriteById[modelId] = null;
        missingModelSpriteIdsLogged.Add(modelId);
        return null;
    }

    private string ResolveModelName(string modelId)
    {
        if (!string.IsNullOrWhiteSpace(modelId) &&
            modelNameById.TryGetValue(modelId, out string modelNameFromId) &&
            !string.IsNullOrWhiteSpace(modelNameFromId))
        {
            return modelNameFromId;
        }

        return string.Empty;
    }

    private Sprite GetPatternSprite(string patternName)
    {
        if (string.IsNullOrWhiteSpace(patternName))
            return null;

        if (patternSpriteByName.TryGetValue(patternName, out Sprite sprite))
            return sprite;

        return null;
    }

    private BackgroundItemData GetBackground(string backgroundId, string backgroundName)
    {
        if (!string.IsNullOrWhiteSpace(backgroundId) && backgroundsById.TryGetValue(backgroundId, out BackgroundItemData byId))
            return byId;

        if (!string.IsNullOrWhiteSpace(backgroundName) && backgroundsByName.TryGetValue(backgroundName, out BackgroundItemData byName))
            return byName;

        return null;
    }

    public Sprite GetModelSpriteForUI(string modelId, string modelName)
    {
        return GetModelSprite(modelId);
    }
public Sprite GetPatternSpriteForUI(string patternName)
    {
        return GetPatternSprite(patternName);
    }

    public BackgroundItemData GetBackgroundForUI(string backgroundId, string backgroundName)
    {
        return GetBackground(backgroundId, backgroundName);
    }

    public void WarmUiLookupCache(IReadOnlyList<InventoryEntry> entries)
    {
        if (entries == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            InventoryEntry entry = entries[i];
            if (entry == null)
                continue;

            GetModelSprite(entry.modelId);
        }
    }

    public string GetGiftIdForUI(InventoryEntry entry)
    {
        if (entry == null)
            return "default";

        if (!string.IsNullOrWhiteSpace(entry.giftId))
            return NormalizeGiftId(entry.giftId);

        return NormalizeGiftId(entry.giftTypeKey);
    }

    public List<InventoryEntry> GetEntriesForModel(string giftId, string modelId)
    {
        List<InventoryEntry> matches = new List<InventoryEntry>();
        if (string.IsNullOrWhiteSpace(modelId))
            return matches;

        string normalizedGiftId = NormalizeGiftId(giftId);
        string normalizedModelId = Safe(modelId);
        string resolvedGiftCollectionName = GiftCatalogDatabase.ResolveCollectionName(normalizedGiftId);

        for (int i = 0; i < inventoryItems.Count; i++)
        {
            InventoryEntry entry = inventoryItems[i];
            if (entry == null)
                continue;

            if (!string.Equals(Safe(entry.modelId), normalizedModelId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!EntryMatchesGift(entry, normalizedGiftId, resolvedGiftCollectionName))
                continue;

            matches.Add(entry);
        }

        matches.Sort(CompareInventoryEntriesByNewestFirst);
        return matches;
    }

    public List<InventoryEntry> GetEntriesForGiftLoose(string giftId)
    {
        List<InventoryEntry> matches = new List<InventoryEntry>();
        string normalizedGiftId = NormalizeGiftId(giftId);
        string resolvedGiftCollectionName = GiftCatalogDatabase.ResolveCollectionName(normalizedGiftId);

        for (int i = 0; i < inventoryItems.Count; i++)
        {
            InventoryEntry entry = inventoryItems[i];
            if (entry == null)
                continue;

            if (!EntryMatchesGift(entry, normalizedGiftId, resolvedGiftCollectionName))
                continue;

            matches.Add(entry);
        }

        matches.Sort(CompareInventoryEntriesByNewestFirst);
        return matches;
    }

    public List<InventoryEntry> GetEntriesForModelAcrossCollections(string modelId)
    {
        List<InventoryEntry> matches = new List<InventoryEntry>();
        if (string.IsNullOrWhiteSpace(modelId))
            return matches;

        string normalizedModelId = Safe(modelId);
        for (int i = 0; i < inventoryItems.Count; i++)
        {
            InventoryEntry entry = inventoryItems[i];
            if (entry == null)
                continue;

            if (string.Equals(Safe(entry.modelId), normalizedModelId, StringComparison.OrdinalIgnoreCase))
                matches.Add(entry);
        }

        matches.Sort(CompareInventoryEntriesByNewestFirst);
        return matches;
    }

    public bool TryBuildModelOwnershipDiagnostic(string giftId, string modelId, out string message)
    {
        message = string.Empty;

        List<InventoryEntry> looseMatches = GetEntriesForModelAcrossCollections(modelId);
        if (looseMatches.Count == 0)
            return false;

        string resolvedGiftCollectionName = GiftCatalogDatabase.ResolveCollectionName(giftId);
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.Append("[AlbumDebug] No owned match for gift '")
            .Append(giftId)
            .Append("' (resolved='")
            .Append(resolvedGiftCollectionName)
            .Append("') and model '")
            .Append(modelId)
            .Append("'. Found same model in inventory under: ");

        for (int i = 0; i < looseMatches.Count; i++)
        {
            InventoryEntry entry = looseMatches[i];
            if (entry == null)
                continue;

            if (i > 0)
                builder.Append(" | ");

            builder.Append("giftId='")
                .Append(GetGiftIdForUI(entry))
                .Append("', collectionKey='")
                .Append(ResolveCollectionKey(entry.modelId, entry.collectionKey))
                .Append("', #")
                .Append(entry.inventoryNumber);
        }

        message = builder.ToString();
        return true;
    }

    public bool TryBuildCollectionOwnershipDiagnostic(string giftId, IList<GiftCatalogDatabase.GiftItemRecord> collectionItems, out string message)
    {
        message = string.Empty;

        List<InventoryEntry> matchingGiftEntries = GetEntriesForGiftLoose(giftId);
        if (matchingGiftEntries.Count == 0)
            return false;

        HashSet<string> collectionModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (collectionItems != null)
        {
            for (int i = 0; i < collectionItems.Count; i++)
            {
                GiftCatalogDatabase.GiftItemRecord item = collectionItems[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;

                collectionModelIds.Add(Safe(item.id));
            }
        }

        HashSet<string> matchedModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> inventoryModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> unmatchedEntries = new List<string>();

        for (int i = 0; i < matchingGiftEntries.Count; i++)
        {
            InventoryEntry entry = matchingGiftEntries[i];
            if (entry == null)
                continue;

            string entryModelId = Safe(entry.modelId);
            if (string.IsNullOrWhiteSpace(entryModelId))
                continue;

            inventoryModelIds.Add(entryModelId);
            if (collectionModelIds.Contains(entryModelId))
            {
                matchedModelIds.Add(entryModelId);
                continue;
            }

            unmatchedEntries.Add(
                "modelId='" + entryModelId +
                "', giftId='" + GetGiftIdForUI(entry) +
                "', collectionKey='" + ResolveCollectionKey(entry.modelId, entry.collectionKey) +
                "', #" + entry.inventoryNumber);
        }

        if (unmatchedEntries.Count == 0 && collectionModelIds.Count > 0)
            return false;

        string resolvedGiftCollectionName = GiftCatalogDatabase.ResolveCollectionName(giftId);
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.Append("[AlbumDebug] Collection '")
            .Append(giftId)
            .Append("' (resolved='")
            .Append(resolvedGiftCollectionName)
            .Append("') loaded ")
            .Append(collectionModelIds.Count)
            .Append(" model ids from gift_items and found ")
            .Append(matchingGiftEntries.Count)
            .Append(" inventory entries (")
            .Append(inventoryModelIds.Count)
            .Append(" unique model ids). Matched unique ids: ")
            .Append(matchedModelIds.Count)
            .Append(".");

        if (collectionModelIds.Count == 0)
        {
            builder.Append(" gift_items returned no models for this collection.");
        }

        if (unmatchedEntries.Count > 0)
        {
            builder.Append(" Inventory entries missing from this collection DB: ");
            for (int i = 0; i < unmatchedEntries.Count; i++)
            {
                if (i > 0)
                    builder.Append(" | ");

                builder.Append(unmatchedEntries[i]);
            }
        }

        message = builder.ToString();
        return true;
    }

    public InventoryEntry GetLatestEntryForModel(string giftId, string modelId)
    {
        List<InventoryEntry> matches = GetEntriesForModel(giftId, modelId);
        return matches.Count > 0 ? matches[0] : null;
    }

    public InventoryEntry GetLatestEntryForGift(string giftId)
    {
        string normalizedGiftId = NormalizeGiftId(giftId);
        InventoryEntry latestEntry = null;

        for (int i = 0; i < inventoryItems.Count; i++)
        {
            InventoryEntry entry = inventoryItems[i];
            if (entry == null)
                continue;

            if (!string.Equals(GetGiftIdForUI(entry), normalizedGiftId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (latestEntry == null || CompareInventoryEntriesByNewestFirst(entry, latestEntry) < 0)
                latestEntry = entry;
        }

        return latestEntry;
    }

    public void ApplyEntryToExternalPrefab(GameObject itemGO, InventoryEntry entry)
    {
        if (itemGO == null || entry == null)
            return;

        ApplyEntryToPrefab(itemGO, entry, entry.inventoryNumber);
    }

    public void ClearInventory()
    {
        HashSet<string> collectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < inventoryItems.Count; i++)
        {
            InventoryEntry entry = inventoryItems[i];
            if (entry == null)
                continue;

            collectionKeys.Add(ResolveCollectionKey(entry.modelId, entry.collectionKey));
        }

        inventoryItems.Clear();
        reservedNumbersByRoll.Clear();
        GiftCatalogDatabase.ClearInventory(GetActiveSaveKey());
        AlbumCollectionProgressStore.ClearAll();
        PlayerPrefs.DeleteKey(GetActiveSaveKey());

        foreach (string collectionKey in collectionKeys)
            PlayerPrefs.DeleteKey(GetInventoryCounterKey(collectionKey));

        PlayerPrefs.Save();

        if (virtualizedView != null)
            virtualizedView.ReloadFromInventory();
        else
            RebuildGrid();

        NotifyInventoryChanged();
    }

    private string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value;
    }

    private void NotifyInventoryChanged()
    {
        InventoryChanged?.Invoke();
    }

    private string GetActiveSaveKey()
    {
        return NormalizeInventoryScopeKey(saveKey);
    }

    private static string NormalizeInventoryScopeKey(string scope)
    {
        return string.IsNullOrWhiteSpace(scope) ? DefaultSaveKey : scope.Trim();
    }

    private static string GetInventoryCounterKey(string collectionKey)
    {
        return inventoryCounterKeyPrefix + NormalizeCollectionKey(collectionKey);
    }

    private static int CompareInventoryEntriesByNewestFirst(InventoryEntry left, InventoryEntry right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        int numberComparison = right.inventoryNumber.CompareTo(left.inventoryNumber);
        if (numberComparison != 0)
            return numberComparison;

        return string.Compare(right.createdAt, left.createdAt, StringComparison.OrdinalIgnoreCase);
    }

    private bool EntryMatchesGift(InventoryEntry entry, string normalizedGiftId, string resolvedGiftCollectionName)
    {
        if (entry == null)
            return false;

        string entryGiftId = GetGiftIdForUI(entry);
        string entryCollectionKey = ResolveCollectionKey(entry.modelId, entry.collectionKey);

        bool exactGiftMatch = string.Equals(entryGiftId, normalizedGiftId, StringComparison.OrdinalIgnoreCase);
        bool aliasGiftMatch = GiftCatalogDatabase.CollectionNamesMatch(entryGiftId, normalizedGiftId);
        bool collectionKeyMatch = GiftCatalogDatabase.CollectionNamesMatch(entryCollectionKey, normalizedGiftId);
        bool resolvedCollectionMatch =
            !string.IsNullOrWhiteSpace(resolvedGiftCollectionName) &&
            string.Equals(entryCollectionKey, resolvedGiftCollectionName, StringComparison.OrdinalIgnoreCase);

        return exactGiftMatch || aliasGiftMatch || collectionKeyMatch || resolvedCollectionMatch;
    }

    private string ResolveCollectionKey(string modelId, string fallbackCollectionKey)
    {
        if (!string.IsNullOrWhiteSpace(modelId) &&
            modelCollectionById.TryGetValue(modelId, out string collectionKey) &&
            !string.IsNullOrWhiteSpace(collectionKey))
        {
            return collectionKey;
        }

        return NormalizeCollectionKey(fallbackCollectionKey);
    }

    private static string NormalizeCollectionKey(string collectionKey)
    {
        if (string.IsNullOrWhiteSpace(collectionKey))
            return "default";

        return collectionKey.Trim();
    }

    private string GetGiftId(CaseOpeningScroll modelRoll)
    {
        if (modelRoll == null)
            return "default";

        string currentGiftId = NormalizeGiftId(modelRoll.GetCurrentGiftId());
        if (!string.Equals(currentGiftId, "default", StringComparison.OrdinalIgnoreCase))
            return currentGiftId;

        return NormalizeGiftId(GetGiftTypeKey(modelRoll));
    }

    private static string NormalizeGiftId(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return "default";

        return giftId.Trim();
    }

    private static string NormalizeGiftTypeKey(string giftTypeKey)
    {
        if (string.IsNullOrWhiteSpace(giftTypeKey))
            return "default";

        string normalized = giftTypeKey.Trim();
        normalized = normalized.Replace("(Clone)", "").Trim();

        if (normalized.StartsWith("UP Panel(", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(")", StringComparison.Ordinal))
            normalized = normalized.Substring(9, normalized.Length - 10).Trim();

        return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
    }

    private string GetGiftTypeKey(CaseOpeningScroll modelRoll)
    {
        if (modelRoll == null)
            return "default";

        for (Transform current = modelRoll.transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf("UP Panel", StringComparison.OrdinalIgnoreCase) >= 0)
                return NormalizeGiftTypeKey(current.name);
        }

        return NormalizeGiftTypeKey(modelRoll.name);
    }

    private string GetGiftTypeDisplayName(CaseOpeningScroll modelRoll)
    {
        if (modelRoll == null)
            return "default";

        string displayName = modelRoll.GetCurrentGiftDisplayName();
        return ResolveGiftDisplayName(displayName, modelRoll.GetCurrentGiftId(), GetGiftTypeKey(modelRoll));
    }

    private string ResolveGiftDisplayName(string currentName, string giftId, string fallbackGiftTypeKey)
    {
        string trimmedCurrentName = Safe(currentName).Trim();
        if (!string.IsNullOrWhiteSpace(trimmedCurrentName) &&
            trimmedCurrentName.IndexOf("UP Panel", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return NormalizeGiftTypeKey(trimmedCurrentName);
        }

        if (!string.IsNullOrWhiteSpace(giftId))
        {
            string fromGiftId = CaseOpeningScroll.GetGiftDisplayNameForId(giftId);
            if (!string.IsNullOrWhiteSpace(fromGiftId))
                return fromGiftId;
        }

        return NormalizeGiftTypeKey(fallbackGiftTypeKey);
    }

    private Color HexToColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        string value = hex.Trim();
        if (!value.StartsWith("#"))
            value = "#" + value;

        if (ColorUtility.TryParseHtmlString(value, out Color c))
            return c;

        return fallback;
    }

    private List<ModelCatalogItemData> LoadModelCatalogItems(string collectionKey, ModelCatalogBinding binding)
    {
        List<ModelCatalogItemData> items = new List<ModelCatalogItemData>();
        List<string> collectionNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(collectionKey))
            collectionNames.Add(collectionKey);

        string atlasName = binding != null && binding.atlas != null ? binding.atlas.name : string.Empty;
        if (!string.IsNullOrWhiteSpace(atlasName) &&
            !collectionNames.Exists(name => string.Equals(name, atlasName, StringComparison.OrdinalIgnoreCase)))
        {
            collectionNames.Add(atlasName);
        }

        for (int i = 0; i < collectionNames.Count; i++)
        {
            if (!GiftCatalogDatabase.TryLoadGiftItems(collectionNames[i], out List<GiftCatalogDatabase.GiftItemRecord> dbItems))
                continue;

            for (int j = 0; j < dbItems.Count; j++)
            {
                GiftCatalogDatabase.GiftItemRecord row = dbItems[j];
                if (row == null)
                    continue;
                items.Add(new ModelCatalogItemData
                {
                    id = row.id,
                    name = row.name,
                    rarityPermille = row.rarity_permille
                });
            }

            if (items.Count > 0)
                return items;
        }

        return items;
    }
}

