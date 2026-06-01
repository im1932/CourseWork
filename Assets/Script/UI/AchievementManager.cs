using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AchievementManager : MonoBehaviour
{
    public static AchievementManager Instance { get; private set; }
    public event Action AchievementsRefreshed;

    public enum AchievementType
    {
        OpenCountTotal = 0,
        OwnCollectionItems = 1,
        AlbumCollectionComplete = 3,
        AlbumOverallPercent = 4,
        BackgroundMatchTotal = 5,
        BackgroundMatchStreak = 6,
        OwnModelRarityExact = 7,
        UpgradeCount = 8,
        SameModelStreak = 9
    }

    [Serializable]
    public sealed class AchievementDefinition
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public AchievementType type;
        public string collectionKey;
        public int targetValue = 1;
    }

    [Serializable]
    private sealed class AchievementState
    {
        public int progress;
        public bool isUnlocked;
        public string unlockedAt;
    }

    [Header("Definitions")]
    [SerializeField] private List<AchievementDefinition> achievements = new List<AchievementDefinition>();

    [Header("Popup")]
    [SerializeField] private GameObject unlockPopup;
    [SerializeField] private TMP_Text unlockTitleText;
    [SerializeField] private TMP_Text unlockDescriptionText;
    [SerializeField] private float popupDuration = 2.5f;
    [SerializeField] private float popupShownY = -285.2f;
    [SerializeField] private float popupHiddenY = -900f;
    [SerializeField] private float popupSpeed = 900f;

    [Header("Summary")]
    [SerializeField] private TMP_Text unlockedAchievementsProgressText;
    [SerializeField] private string unlockedAchievementsProgressFormat = "{0}/{1}";
    [SerializeField] private Image unlockedAchievementsProgressFillImage;

    private readonly Dictionary<string, AchievementState> statesById = new Dictionary<string, AchievementState>(StringComparer.OrdinalIgnoreCase);
    private float hidePopupAt = -1f;
    private RectTransform unlockPopupRect;
    private float popupTargetY;
    private bool popupOpen;
    private bool popupHidePending;
    private InventoryManager subscribedInventoryManager;
    private bool hasRefreshedAfterInventoryReady;
    private int currentAlbumOverallPercent;
    private int currentUnlockedAchievementCount;
    private int totalUpgradeCount;
    private string loadedTrackedCounterScope = string.Empty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadProgress();
        EnsureStates();
        UpdateUnlockedAchievementsProgressText();
        ResolvePopupRect(true);
        if (unlockPopup != null)
            unlockPopup.SetActive(false);
    }

    private void Start()
    {
        TryBindInventoryEvents();
        RefreshInventoryAchievements();
    }

    private void Update()
    {
        TryBindInventoryEvents();
        UpdatePopupPosition();

        bool popupVisible = unlockPopup != null && unlockPopup.activeSelf;
        if (popupVisible && hidePopupAt > 0f && Time.unscaledTime >= hidePopupAt)
        {
            HideUnlockPopup();
            hidePopupAt = -1f;
        }
    }

    private void OnEnable()
    {
        TryBindInventoryEvents();
    }

    private void OnDisable()
    {
        UnbindInventoryEvents();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        UnbindInventoryEvents();
    }

    public int GetProgress(string achievementId)
    {
        string normalizedId = BuildScopedAchievementId(achievementId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return 0;

        if (!statesById.TryGetValue(normalizedId, out AchievementState state) || state == null)
            return 0;

        return state.progress;
    }

    public bool IsUnlocked(string achievementId)
    {
        string normalizedId = BuildScopedAchievementId(achievementId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return false;

        return statesById.TryGetValue(normalizedId, out AchievementState state) &&
               state != null &&
               state.isUnlocked;
    }

    public int CurrentAlbumOverallPercent => currentAlbumOverallPercent;
    public IReadOnlyList<AchievementDefinition> Achievements => achievements;

    public void RegisterUpgrade()
    {
        EnsureTrackedCounterScopeLoaded();
        totalUpgradeCount++;
        SaveTrackedCounters();
        RefreshInventoryAchievements();
    }

    public void RefreshInventoryAchievementsImmediately()
    {
        RefreshInventoryAchievements();
    }

    public string GetProgressDisplay(AchievementDefinition definition)
    {
        if (definition == null)
            return string.Empty;

        int progress = GetProgress(definition.id);
        int target = Mathf.Max(1, definition.targetValue);

        if (definition.type == AchievementType.AlbumOverallPercent)
            return progress.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        return progress.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               "/" +
               target.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void HandleInventoryChanged()
    {
        RefreshInventoryAchievements();
    }

    private void TryBindInventoryEvents()
    {
        InventoryManager currentInventoryManager = InventoryManager.Instance;
        if (ReferenceEquals(subscribedInventoryManager, currentInventoryManager))
        {
            if (!hasRefreshedAfterInventoryReady && currentInventoryManager != null)
            {
                hasRefreshedAfterInventoryReady = true;
                RefreshInventoryAchievements();
            }

            return;
        }

        UnbindInventoryEvents();

        if (currentInventoryManager == null)
            return;

        subscribedInventoryManager = currentInventoryManager;
        subscribedInventoryManager.InventoryChanged += HandleInventoryChanged;
        EnsureStates();

        if (!hasRefreshedAfterInventoryReady)
        {
            hasRefreshedAfterInventoryReady = true;
            RefreshInventoryAchievements();
        }
    }

    private void UnbindInventoryEvents()
    {
        if (subscribedInventoryManager == null)
            return;

        subscribedInventoryManager.InventoryChanged -= HandleInventoryChanged;
        subscribedInventoryManager = null;
    }

    private void RefreshInventoryAchievements()
    {
        EnsureTrackedCounterScopeLoaded();
        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
        {
            currentAlbumOverallPercent = 0;
            RefreshUnlockedAchievementSummary();
            AchievementsRefreshed?.Invoke();
            return;
        }

        IReadOnlyList<InventoryManager.InventoryEntry> items = inventoryManager.Items;
        int totalItems = items != null ? items.Count : 0;
        Dictionary<string, int> countsByCollection = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> uniqueModelIdsByCollection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allOwnedModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int bestSameModelStreak = 0;
        int currentSameModelStreak = 0;
        string previousModelId = string.Empty;

        for (int i = 0; items != null && i < items.Count; i++)
        {
            InventoryManager.InventoryEntry entry = items[i];
            if (entry == null)
                continue;

            string collectionKey = NormalizeKey(entry.collectionKey);
            if (string.IsNullOrWhiteSpace(collectionKey))
                collectionKey = NormalizeKey(entry.giftId);

            string modelId = NormalizeKey(entry.modelId);
            if (!string.IsNullOrWhiteSpace(collectionKey))
            {
                if (!countsByCollection.ContainsKey(collectionKey))
                    countsByCollection[collectionKey] = 0;

                countsByCollection[collectionKey]++;

                if (!uniqueModelIdsByCollection.TryGetValue(collectionKey, out HashSet<string> collectionModels))
                {
                    collectionModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    uniqueModelIdsByCollection[collectionKey] = collectionModels;
                }

                if (!string.IsNullOrWhiteSpace(modelId))
                    collectionModels.Add(modelId);
            }

            if (!string.IsNullOrWhiteSpace(modelId))
                allOwnedModelIds.Add(modelId);

            if (!string.IsNullOrWhiteSpace(modelId) &&
                string.Equals(previousModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                currentSameModelStreak++;
            }
            else
            {
                currentSameModelStreak = string.IsNullOrWhiteSpace(modelId) ? 0 : 1;
            }

            if (currentSameModelStreak > bestSameModelStreak)
                bestSameModelStreak = currentSameModelStreak;

            previousModelId = modelId;
        }

        List<GiftCatalogDatabase.GiftItemRecord> allGiftItems = GiftCatalogDatabase.LoadAllGiftItems();
        Dictionary<string, HashSet<string>> dbModelIdsByCollection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allDbModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < allGiftItems.Count; i++)
        {
            GiftCatalogDatabase.GiftItemRecord row = allGiftItems[i];
            if (row == null)
                continue;

            string collectionKey = NormalizeKey(row.collection_name);
            string modelId = NormalizeKey(row.id);
            if (string.IsNullOrWhiteSpace(collectionKey) || string.IsNullOrWhiteSpace(modelId))
                continue;

            if (!dbModelIdsByCollection.TryGetValue(collectionKey, out HashSet<string> collectionModels))
            {
                collectionModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dbModelIdsByCollection[collectionKey] = collectionModels;
            }

            collectionModels.Add(modelId);
            allDbModelIds.Add(modelId);
        }

        int albumPercent = GetAlbumOverallPercent();
        currentAlbumOverallPercent = albumPercent;

        bool changed = false;

        for (int i = 0; i < achievements.Count; i++)
        {
            AchievementDefinition definition = achievements[i];
            if (definition == null)
                continue;

            switch (definition.type)
            {
                case AchievementType.OpenCountTotal:
                    changed |= SetProgress(definition, totalItems);
                    break;

                case AchievementType.OwnCollectionItems:
                    countsByCollection.TryGetValue(NormalizeKey(definition.collectionKey), out int collectionCount);
                    changed |= SetProgress(definition, collectionCount);
                    break;

                case AchievementType.AlbumCollectionComplete:
                    changed |= SetProgress(definition, IsCollectionCompleted(definition.collectionKey, uniqueModelIdsByCollection, dbModelIdsByCollection) ? 1 : 0);
                    break;

                case AchievementType.AlbumOverallPercent:
                    changed |= SetProgress(definition, albumPercent);
                    break;

                case AchievementType.BackgroundMatchTotal:
                    changed |= SetProgress(definition, CountMatchingBackgrounds(items, definition.collectionKey));
                    break;

                case AchievementType.BackgroundMatchStreak:
                    changed |= SetProgress(definition, GetMatchingBackgroundStreak(items, definition.collectionKey));
                    break;

                case AchievementType.OwnModelRarityExact:
                    changed |= SetProgress(definition, CountItemsWithExactModelRarity(items, definition.targetValue));
                    break;

                case AchievementType.UpgradeCount:
                    changed |= SetProgress(definition, totalUpgradeCount);
                    break;

                case AchievementType.SameModelStreak:
                    changed |= SetProgress(definition, bestSameModelStreak);
                    break;
            }
        }

        if (changed)
            SaveProgress();

        RefreshUnlockedAchievementSummary();
        AchievementsRefreshed?.Invoke();
    }

    private bool SetProgress(AchievementDefinition definition, int rawProgress)
    {
        if (definition == null)
            return false;

        string achievementId = BuildScopedAchievementId(definition.id);
        if (string.IsNullOrWhiteSpace(achievementId))
            return false;

        if (!statesById.TryGetValue(achievementId, out AchievementState state) || state == null)
        {
            state = new AchievementState();
            statesById[achievementId] = state;
        }

        int clampedTarget = Mathf.Max(1, definition.targetValue);
        int nextProgress = Mathf.Clamp(rawProgress, 0, clampedTarget);
        bool changed = nextProgress != state.progress;
        state.progress = nextProgress;

        if (!state.isUnlocked && state.progress >= clampedTarget)
        {
            state.isUnlocked = true;
            state.unlockedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            ShowUnlockPopup(definition);
            changed = true;
        }

        return changed;
    }

    private void EnsureStates()
    {
        for (int i = 0; i < achievements.Count; i++)
        {
            AchievementDefinition definition = achievements[i];
            if (definition == null)
                continue;

            string achievementId = BuildScopedAchievementId(definition.id);
            if (string.IsNullOrWhiteSpace(achievementId))
                continue;

            if (!statesById.ContainsKey(achievementId))
                statesById[achievementId] = new AchievementState();
        }
    }

    private void LoadProgress()
    {
        statesById.Clear();
        List<GiftCatalogDatabase.AchievementProgressRecord> rows = GiftCatalogDatabase.LoadAchievementProgress();
        for (int i = 0; i < rows.Count; i++)
        {
            GiftCatalogDatabase.AchievementProgressRecord row = rows[i];
            if (row == null || string.IsNullOrWhiteSpace(row.achievement_id))
                continue;

            statesById[NormalizeKey(row.achievement_id)] = new AchievementState
            {
                progress = Mathf.Max(0, row.progress),
                isUnlocked = row.is_unlocked != 0,
                unlockedAt = row.unlocked_at ?? string.Empty
            };
        }
    }

    private void SaveProgress()
    {
        List<GiftCatalogDatabase.AchievementProgressRecord> rows = new List<GiftCatalogDatabase.AchievementProgressRecord>(statesById.Count);

        foreach (KeyValuePair<string, AchievementState> pair in statesById)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                continue;

            rows.Add(new GiftCatalogDatabase.AchievementProgressRecord
            {
                achievement_id = pair.Key,
                progress = Mathf.Max(0, pair.Value.progress),
                is_unlocked = pair.Value.isUnlocked ? 1 : 0,
                unlocked_at = pair.Value.unlockedAt ?? string.Empty
            });
        }

        GiftCatalogDatabase.ReplaceAchievementProgress(rows);
    }

    private void EnsureTrackedCounterScopeLoaded()
    {
        string scope = GetCurrentAchievementScope();
        if (string.Equals(loadedTrackedCounterScope, scope, StringComparison.OrdinalIgnoreCase))
            return;

        loadedTrackedCounterScope = scope;
        totalUpgradeCount = PlayerPrefs.GetInt(BuildTrackedCounterKey("upgrade_count"), 0);
    }

    private void SaveTrackedCounters()
    {
        PlayerPrefs.SetInt(BuildTrackedCounterKey("upgrade_count"), Mathf.Max(0, totalUpgradeCount));
        PlayerPrefs.Save();
    }

    private string BuildTrackedCounterKey(string counterName)
    {
        return "achievement_metric::" + GetCurrentAchievementScope() + "::" + NormalizeKey(counterName);
    }

    private void RefreshUnlockedAchievementSummary()
    {
        int unlockedCount = 0;
        int totalCount = achievements != null ? achievements.Count : 0;

        for (int i = 0; i < totalCount; i++)
        {
            AchievementDefinition definition = achievements[i];
            if (definition == null)
                continue;

            if (IsUnlocked(definition.id))
                unlockedCount++;
        }

        currentUnlockedAchievementCount = unlockedCount;
        UpdateUnlockedAchievementsProgressText();
    }

    private void UpdateUnlockedAchievementsProgressText()
    {
        int totalCount = achievements != null ? achievements.Count : 0;
        float fillAmount = totalCount > 0 ? currentUnlockedAchievementCount / (float)totalCount : 0f;

        if (unlockedAchievementsProgressFillImage != null)
            unlockedAchievementsProgressFillImage.fillAmount = Mathf.Clamp01(fillAmount);

        if (unlockedAchievementsProgressText == null)
            return;

        string format = string.IsNullOrWhiteSpace(unlockedAchievementsProgressFormat) ? "{0}/{1}" : unlockedAchievementsProgressFormat;
        unlockedAchievementsProgressText.text = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            format,
            currentUnlockedAchievementCount,
            totalCount);
    }

    private static int GetAlbumOverallPercent()
    {
        AlbumCollectionProgressStore.GetOverallProgress(out int ownedModelCount, out int totalModelCount);
        if (totalModelCount <= 0)
            return 0;

        float percent = ownedModelCount / (float)totalModelCount * 100f;
        return Mathf.Clamp(Mathf.RoundToInt(percent), 0, 100);
    }

    private void ShowUnlockPopup(AchievementDefinition definition)
    {
        if (unlockPopup == null)
            return;

        if (unlockTitleText != null)
            unlockTitleText.text = string.IsNullOrWhiteSpace(definition.title) ? definition.id : definition.title;

        if (unlockDescriptionText != null)
            unlockDescriptionText.text = definition.description ?? string.Empty;

        ResolvePopupRect(false);
        unlockPopup.SetActive(true);
        popupOpen = true;
        popupHidePending = false;
        popupTargetY = popupShownY;
        hidePopupAt = Time.unscaledTime + Mathf.Max(0.5f, popupDuration);
    }

    private void HideUnlockPopup()
    {
        if (unlockPopup != null)
        {
            ResolvePopupRect(false);
            popupOpen = false;
            popupHidePending = true;
            popupTargetY = popupHiddenY;
        }
    }

    private void ResolvePopupRect(bool snapToHiddenState)
    {
        if (unlockPopup == null)
            return;

        if (unlockPopupRect == null)
            unlockPopupRect = unlockPopup.GetComponent<RectTransform>();

        if (unlockPopupRect == null)
            return;

        if (snapToHiddenState)
        {
            Vector2 anchoredPosition = unlockPopupRect.anchoredPosition;
            anchoredPosition.y = popupHiddenY;
            unlockPopupRect.anchoredPosition = anchoredPosition;
            popupTargetY = popupHiddenY;
            popupOpen = false;
            popupHidePending = false;
        }
    }

    private void UpdatePopupPosition()
    {
        if (unlockPopupRect == null)
            return;

        Vector2 anchoredPosition = unlockPopupRect.anchoredPosition;
        anchoredPosition.y = Mathf.MoveTowards(anchoredPosition.y, popupTargetY, popupSpeed * Time.unscaledDeltaTime);
        unlockPopupRect.anchoredPosition = anchoredPosition;

        if (popupHidePending && !popupOpen && Mathf.Abs(anchoredPosition.y - popupHiddenY) <= 0.01f)
        {
            unlockPopup.SetActive(false);
            popupHidePending = false;
        }
    }

    private static bool IsCollectionCompleted(
        string collectionKey,
        Dictionary<string, HashSet<string>> uniqueModelIdsByCollection,
        Dictionary<string, HashSet<string>> dbModelIdsByCollection)
    {
        string normalizedCollectionKey = NormalizeKey(collectionKey);
        if (string.IsNullOrWhiteSpace(normalizedCollectionKey))
            return false;

        string resolvedCollectionKey = GiftCatalogDatabase.ResolveCollectionName(normalizedCollectionKey);
        if (!dbModelIdsByCollection.TryGetValue(resolvedCollectionKey, out HashSet<string> dbModels) || dbModels == null || dbModels.Count == 0)
            return false;

        if (!uniqueModelIdsByCollection.TryGetValue(resolvedCollectionKey, out HashSet<string> ownedModels) || ownedModels == null)
            return false;

        foreach (string modelId in dbModels)
        {
            if (!ownedModels.Contains(modelId))
                return false;
        }

        return true;
    }

    private static int CountMatchingBackgrounds(IReadOnlyList<InventoryManager.InventoryEntry> items, string backgroundName)
    {
        string normalizedBackground = NormalizeKey(backgroundName);
        if (string.IsNullOrWhiteSpace(normalizedBackground))
            normalizedBackground = "black";

        int count = 0;
        for (int i = 0; items != null && i < items.Count; i++)
        {
            InventoryManager.InventoryEntry entry = items[i];
            if (entry != null && IsMatchingBackground(entry.backgroundName, normalizedBackground))
                count++;
        }

        return count;
    }

    private static int GetMatchingBackgroundStreak(IReadOnlyList<InventoryManager.InventoryEntry> items, string backgroundName)
    {
        string normalizedBackground = NormalizeKey(backgroundName);
        if (string.IsNullOrWhiteSpace(normalizedBackground))
            normalizedBackground = "black";

        int bestStreak = 0;
        int currentStreak = 0;

        for (int i = 0; items != null && i < items.Count; i++)
        {
            InventoryManager.InventoryEntry entry = items[i];
            if (entry != null && IsMatchingBackground(entry.backgroundName, normalizedBackground))
            {
                currentStreak++;
                if (currentStreak > bestStreak)
                    bestStreak = currentStreak;
            }
            else
            {
                currentStreak = 0;
            }
        }

        return bestStreak;
    }

    private static int CountItemsWithExactModelRarity(IReadOnlyList<InventoryManager.InventoryEntry> items, int exactPermilleValue)
    {
        int exactPermille = Mathf.Max(1, exactPermilleValue);
        int count = 0;
        for (int i = 0; items != null && i < items.Count; i++)
        {
            InventoryManager.InventoryEntry entry = items[i];
            if (entry != null && entry.modelRarityPermille == exactPermille)
                count++;
        }

        return count;
    }

    private static bool IsMatchingBackground(string backgroundName, string expectedBackgroundName)
    {
        string left = NormalizeKey(backgroundName);
        string right = NormalizeKey(expectedBackgroundName);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private void ResetCurrentScopeAchievements()
    {
        string scopePrefix = GetCurrentAchievementScope() + "::";
        List<string> keysToReset = new List<string>();

        foreach (KeyValuePair<string, AchievementState> pair in statesById)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            if (pair.Key.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase))
                keysToReset.Add(pair.Key);
        }

        for (int i = 0; i < keysToReset.Count; i++)
            statesById.Remove(keysToReset[i]);

        PlayerPrefs.DeleteKey(BuildTrackedCounterKey("upgrade_count"));
        PlayerPrefs.Save();
        totalUpgradeCount = 0;
        loadedTrackedCounterScope = string.Empty;
        EnsureStates();
        SaveProgress();
        RefreshInventoryAchievements();
    }

    private string BuildScopedAchievementId(string achievementId)
    {
        string normalizedId = NormalizeKey(achievementId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return string.Empty;

        string scope = GetCurrentAchievementScope();
        return string.IsNullOrWhiteSpace(scope)
            ? normalizedId
            : scope + "::" + normalizedId;
    }

    private string GetCurrentAchievementScope()
    {
        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager == null)
            return "inventory_save_v1";

        FieldInfo saveKeyField = typeof(InventoryManager).GetField("saveKey", BindingFlags.Instance | BindingFlags.NonPublic);
        if (saveKeyField == null)
            return "inventory_save_v1";

        object value = saveKeyField.GetValue(inventoryManager);
        string saveKey = value as string;
        saveKey = NormalizeKey(saveKey);
        return string.IsNullOrWhiteSpace(saveKey) ? "inventory_save_v1" : saveKey;
    }
}
