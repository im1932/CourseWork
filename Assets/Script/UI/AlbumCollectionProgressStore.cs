using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AlbumCollectionProgressStore
{
    private const string RegistryKey = "album_progress_registry_v1";
    private const string CollectionKeyPrefix = "album_progress_collection_v1::";
    private const string OverallOwnedCountKey = "album_progress_overall_owned_count_v1";
    private const string OverallTotalCountKey = "album_progress_overall_total_count_v1";
    private const string InventoryScopeKey = "album_progress_inventory_scope_v1";

    [System.Serializable]
    private sealed class StoredCollectionProgress
    {
        public List<string> modelIds = new List<string>();
    }

    public static void RebuildFromInventory(IReadOnlyList<InventoryManager.InventoryEntry> items)
    {
        Dictionary<string, HashSet<string>> modelIdsByGift =
            new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
        HashSet<string> allOwnedModelIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        for (int i = 0; items != null && i < items.Count; i++)
        {
            InventoryManager.InventoryEntry entry = items[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.modelId))
                continue;

            string normalizedModelId = entry.modelId.Trim();
            allOwnedModelIds.Add(normalizedModelId);

            string giftKey = NormalizeGiftKey(entry.giftId);
            if (string.IsNullOrWhiteSpace(giftKey))
                continue;

            if (!modelIdsByGift.TryGetValue(giftKey, out HashSet<string> modelIds))
            {
                modelIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                modelIdsByGift[giftKey] = modelIds;
            }

            modelIds.Add(normalizedModelId);
        }

        SaveAll(modelIdsByGift);
        SaveOverallProgressSnapshot(allOwnedModelIds.Count, GetOrCacheTotalDatabaseModelCount(), ResolveInventoryScope());
    }

    public static int GetCollectedCount(string giftId, IReadOnlyList<GiftCatalogDatabase.GiftItemRecord> collectionItems)
    {
        if (collectionItems == null || collectionItems.Count == 0)
            return 0;

        HashSet<string> ownedModelIds = LoadStoredModelIds(giftId);
        if (ownedModelIds.Count == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < collectionItems.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord item = collectionItems[i];
            if (item == null || string.IsNullOrWhiteSpace(item.id))
                continue;

            if (ownedModelIds.Contains(item.id.Trim()))
                count++;
        }

        return count;
    }

    public static void GetOverallProgress(out int ownedModelCount, out int totalModelCount)
    {
        int cachedOwnedCount = PlayerPrefs.GetInt(OverallOwnedCountKey, -1);
        int cachedTotalCount = PlayerPrefs.GetInt(OverallTotalCountKey, -1);
        if (cachedOwnedCount >= 0 && cachedTotalCount > 0)
        {
            ownedModelCount = cachedOwnedCount;
            totalModelCount = cachedTotalCount;
            return;
        }

        HashSet<string> allOwnedModelIds = LoadAllOwnedModelIds();
        totalModelCount = GetOrCacheTotalDatabaseModelCount();
        ownedModelCount = allOwnedModelIds.Count;
        SaveOverallProgressSnapshot(ownedModelCount, totalModelCount, ResolveInventoryScope());
    }

    public static void ClearAll()
    {
        HashSet<string> registry = LoadRegistry();
        foreach (string collectionKey in registry)
        {
            if (string.IsNullOrWhiteSpace(collectionKey))
                continue;

            PlayerPrefs.DeleteKey(BuildCollectionPlayerPrefsKey(collectionKey));
        }

        PlayerPrefs.DeleteKey(RegistryKey);
        PlayerPrefs.DeleteKey(OverallOwnedCountKey);
        PlayerPrefs.DeleteKey(OverallTotalCountKey);
        PlayerPrefs.DeleteKey(InventoryScopeKey);
        PlayerPrefs.Save();
    }

    private static void SaveAll(Dictionary<string, HashSet<string>> modelIdsByCollection)
    {
        ClearAllWithoutSaving();

        List<string> registryEntries = new List<string>();
        foreach (KeyValuePair<string, HashSet<string>> pair in modelIdsByCollection)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Count == 0)
                continue;

            StoredCollectionProgress data = new StoredCollectionProgress();
            foreach (string modelId in pair.Value)
            {
                if (!string.IsNullOrWhiteSpace(modelId))
                    data.modelIds.Add(modelId);
            }

            if (data.modelIds.Count == 0)
                continue;

            PlayerPrefs.SetString(
                BuildCollectionPlayerPrefsKey(pair.Key),
                JsonUtility.ToJson(data));
            registryEntries.Add(pair.Key);
        }

        PlayerPrefs.SetString(RegistryKey, string.Join("\n", registryEntries.ToArray()));
        PlayerPrefs.Save();
    }

    private static HashSet<string> LoadStoredModelIds(string giftId)
    {
        string giftKey = NormalizeGiftKey(giftId);
        if (string.IsNullOrWhiteSpace(giftKey))
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        string raw = PlayerPrefs.GetString(BuildCollectionPlayerPrefsKey(giftKey), string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            string legacyCollectionKey = ResolveLegacyCollectionKey(giftId);
            if (!string.IsNullOrWhiteSpace(legacyCollectionKey))
                raw = PlayerPrefs.GetString(BuildCollectionPlayerPrefsKey(legacyCollectionKey), string.Empty);
        }

        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        StoredCollectionProgress data = JsonUtility.FromJson<StoredCollectionProgress>(raw);
        HashSet<string> result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (data == null || data.modelIds == null)
            return result;

        for (int i = 0; i < data.modelIds.Count; i++)
        {
            string modelId = data.modelIds[i];
            if (!string.IsNullOrWhiteSpace(modelId))
                result.Add(modelId.Trim());
        }

        return result;
    }

    private static HashSet<string> LoadRegistry()
    {
        HashSet<string> result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        string raw = PlayerPrefs.GetString(RegistryKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        string[] parts = raw.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (!string.IsNullOrWhiteSpace(part))
                result.Add(part.Trim());
        }

        return result;
    }

    private static HashSet<string> LoadAllStoredModelIds()
    {
        HashSet<string> result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        HashSet<string> registry = LoadRegistry();

        foreach (string giftKey in registry)
        {
            if (string.IsNullOrWhiteSpace(giftKey))
                continue;

            string raw = PlayerPrefs.GetString(BuildCollectionPlayerPrefsKey(giftKey), string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            StoredCollectionProgress data = JsonUtility.FromJson<StoredCollectionProgress>(raw);
            if (data == null || data.modelIds == null)
                continue;

            for (int i = 0; i < data.modelIds.Count; i++)
            {
                string modelId = data.modelIds[i];
                if (!string.IsNullOrWhiteSpace(modelId))
                    result.Add(modelId.Trim());
            }
        }

        return result;
    }

    private static HashSet<string> LoadAllOwnedModelIds()
    {
        HashSet<string> loadedFromStore = LoadAllStoredModelIds();
        if (loadedFromStore.Count > 0)
            return loadedFromStore;

        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager != null && inventoryManager.Items != null)
        {
            HashSet<string> loadedFromInventory = CollectOwnedModelIds(inventoryManager.Items);
            if (loadedFromInventory.Count > 0)
                return loadedFromInventory;
        }

        List<GiftCatalogDatabase.InventoryRecord> inventoryRows = GiftCatalogDatabase.LoadInventory(ResolveInventoryScope());
        return CollectOwnedModelIds(inventoryRows);
    }

    private static HashSet<string> CollectOwnedModelIds(IReadOnlyList<InventoryManager.InventoryEntry> items)
    {
        HashSet<string> result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; items != null && i < items.Count; i++)
        {
            InventoryManager.InventoryEntry entry = items[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.modelId))
                continue;

            result.Add(entry.modelId.Trim());
        }

        return result;
    }

    private static HashSet<string> CollectOwnedModelIds(IReadOnlyList<GiftCatalogDatabase.InventoryRecord> rows)
    {
        HashSet<string> result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; rows != null && i < rows.Count; i++)
        {
            GiftCatalogDatabase.InventoryRecord row = rows[i];
            if (row == null || string.IsNullOrWhiteSpace(row.model_id))
                continue;

            result.Add(row.model_id.Trim());
        }

        return result;
    }

    private static HashSet<string> LoadAllDatabaseModelIds()
    {
        HashSet<string> result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        List<GiftCatalogDatabase.GiftItemRecord> allItems = GiftCatalogDatabase.LoadAllGiftItems();

        for (int i = 0; i < allItems.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord item = allItems[i];
            if (item == null || string.IsNullOrWhiteSpace(item.id))
                continue;

            result.Add(item.id.Trim());
        }

        return result;
    }

    private static int GetOrCacheTotalDatabaseModelCount()
    {
        int cachedTotalCount = PlayerPrefs.GetInt(OverallTotalCountKey, -1);
        if (cachedTotalCount > 0)
            return cachedTotalCount;

        int totalCount = LoadAllDatabaseModelIds().Count;
        PlayerPrefs.SetInt(OverallTotalCountKey, Mathf.Max(0, totalCount));
        PlayerPrefs.Save();
        return totalCount;
    }

    private static void ClearAllWithoutSaving()
    {
        HashSet<string> registry = LoadRegistry();
        foreach (string collectionKey in registry)
        {
            if (!string.IsNullOrWhiteSpace(collectionKey))
                PlayerPrefs.DeleteKey(BuildCollectionPlayerPrefsKey(collectionKey));
        }

        PlayerPrefs.DeleteKey(RegistryKey);
        PlayerPrefs.DeleteKey(OverallOwnedCountKey);
        PlayerPrefs.DeleteKey(OverallTotalCountKey);
        PlayerPrefs.DeleteKey(InventoryScopeKey);
    }

    private static string BuildCollectionPlayerPrefsKey(string collectionKey)
    {
        return CollectionKeyPrefix + collectionKey;
    }

    private static string NormalizeGiftKey(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return string.Empty;

        return giftId.Trim();
    }

    private static string ResolveLegacyCollectionKey(string giftId)
    {
        if (string.IsNullOrWhiteSpace(giftId))
            return string.Empty;

        return GiftCatalogDatabase.ResolveCollectionName(giftId.Trim());
    }

    private static string ResolveInventoryScope()
    {
        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
            return PlayerPrefs.GetString(InventoryScopeKey, "inventory_save_v1");

        FieldInfo saveKeyField = typeof(InventoryManager).GetField("saveKey", BindingFlags.Instance | BindingFlags.NonPublic);
        if (saveKeyField == null)
            return PlayerPrefs.GetString(InventoryScopeKey, "inventory_save_v1");

        string scope = saveKeyField.GetValue(inventoryManager) as string;
        return string.IsNullOrWhiteSpace(scope)
            ? PlayerPrefs.GetString(InventoryScopeKey, "inventory_save_v1")
            : scope.Trim();
    }

    private static void SaveOverallProgressSnapshot(int ownedModelCount, int totalModelCount, string inventoryScope)
    {
        PlayerPrefs.SetInt(OverallOwnedCountKey, Mathf.Max(0, ownedModelCount));
        PlayerPrefs.SetInt(OverallTotalCountKey, Mathf.Max(0, totalModelCount));
        PlayerPrefs.SetString(
            InventoryScopeKey,
            string.IsNullOrWhiteSpace(inventoryScope) ? "inventory_save_v1" : inventoryScope.Trim());
        PlayerPrefs.Save();
    }
}
