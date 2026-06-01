using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class AchievementListView : MonoBehaviour
{
    private sealed class ItemViewRefs
    {
        public GameObject rootObject;
        public TMP_Text titleText;
        public TMP_Text descriptionText;
        public TMP_Text progressText;
        public GameObject unlockedStateObject;
        public GameObject lockedStateObject;
    }

    [Header("Refs")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform content;
    [SerializeField] private AchievementManager achievementManager;

    private readonly List<ItemViewRefs> itemViews = new List<ItemViewRefs>();

    private void Awake()
    {
        ResolveReferences();
        RebuildItemCache();
    }

    private void OnEnable()
    {
        ResolveReferences();
        RebuildItemCache();

        if (Application.isPlaying)
        {
            Subscribe();
            RefreshView();
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
            RefreshView();
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            Unsubscribe();
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
            Unsubscribe();
    }

    public void RefreshView()
    {
        ResolveReferences();
        RebuildItemCache();

        if (achievementManager == null || content == null)
            return;

        IReadOnlyList<AchievementManager.AchievementDefinition> definitions = achievementManager.Achievements;
        int visibleCount = Mathf.Min(definitions.Count, itemViews.Count);

        for (int i = 0; i < visibleCount; i++)
        {
            AchievementManager.AchievementDefinition definition = definitions[i];
            ItemViewRefs itemView = itemViews[i];
            if (definition == null || itemView == null)
                continue;

            if (itemView.rootObject != null)
                itemView.rootObject.SetActive(true);

            BindItem(
                itemView,
                string.IsNullOrWhiteSpace(definition.title) ? definition.id : definition.title,
                definition.description,
                achievementManager.GetProgressDisplay(definition),
                achievementManager.IsUnlocked(definition.id));
        }

    }

    private void ResolveReferences()
    {
        if (scrollRect == null)
            scrollRect = GetComponent<ScrollRect>();

        if (content == null)
            content = scrollRect != null ? scrollRect.content : FindContentTransform();

        if (achievementManager == null)
            achievementManager = AchievementManager.Instance;
    }

    private RectTransform FindContentTransform()
    {
        Transform namedContent = FindTransformByName(transform, "Grid", "Content", "Items", "List");
        return namedContent as RectTransform;
    }

    private void Subscribe()
    {
        if (achievementManager != null)
            achievementManager.AchievementsRefreshed -= HandleAchievementsRefreshed;

        if (achievementManager != null)
            achievementManager.AchievementsRefreshed += HandleAchievementsRefreshed;
    }

    private void Unsubscribe()
    {
        if (achievementManager != null)
            achievementManager.AchievementsRefreshed -= HandleAchievementsRefreshed;
    }

    private void HandleAchievementsRefreshed()
    {
        RefreshView();
    }

    private void RebuildItemCache()
    {
        itemViews.Clear();
        if (content == null)
            return;

        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);
            if (child == null)
                continue;

            itemViews.Add(BuildItemViewRefs(child.gameObject));
        }
    }

    private static ItemViewRefs BuildItemViewRefs(GameObject rootObject)
    {
        ItemViewRefs refs = new ItemViewRefs();
        refs.rootObject = rootObject;
        AutoResolveReferences(refs);
        return refs;
    }

    private static void BindItem(ItemViewRefs itemView, string title, string description, string progress, bool isUnlocked)
    {
        if (itemView == null || itemView.rootObject == null)
            return;

        AutoResolveReferences(itemView);
        SetText(itemView.titleText, title);
        SetText(itemView.descriptionText, description);
        SetText(itemView.progressText, progress);

        if (itemView.unlockedStateObject != null)
            itemView.unlockedStateObject.SetActive(isUnlocked);

        if (itemView.lockedStateObject != null)
            itemView.lockedStateObject.SetActive(!isUnlocked);
    }

    private static void AutoResolveReferences(ItemViewRefs itemView)
    {
        if (itemView == null || itemView.rootObject == null)
            return;

        if (itemView.titleText != null && itemView.descriptionText != null && itemView.progressText != null)
            return;

        TMP_Text[] texts = itemView.rootObject.GetComponentsInChildren<TMP_Text>(true);
        if (texts != null && texts.Length > 0)
        {
            if (itemView.titleText == null)
                itemView.titleText = FindTextByName(texts, "Title", "TitleText", "Name", "Modeltxt");

            if (itemView.descriptionText == null)
                itemView.descriptionText = FindTextByName(texts, "Description", "DescriptionText", "Desc", "Body");

            if (itemView.progressText == null)
                itemView.progressText = FindTextByName(texts, "Progress", "ProgressText", "Count", "Num", "Value");

            if (texts.Length >= 3)
            {
                if (itemView.titleText == null)
                    itemView.titleText = texts[0];

                if (itemView.descriptionText == null)
                    itemView.descriptionText = texts[1];

                if (itemView.progressText == null)
                    itemView.progressText = texts[2];
            }
        }

        if (itemView.unlockedStateObject == null)
            itemView.unlockedStateObject = FindObjectByName(itemView.rootObject.transform, "Unlocked", "UnlockedState", "Complete", "Done");

        if (itemView.lockedStateObject == null)
            itemView.lockedStateObject = FindObjectByName(itemView.rootObject.transform, "Locked", "LockedState", "Incomplete", "Todo");
    }

    private static TMP_Text FindTextByName(TMP_Text[] texts, params string[] names)
    {
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text candidate = texts[i];
            if (candidate == null)
                continue;

            for (int j = 0; j < names.Length; j++)
            {
                if (string.Equals(candidate.name, names[j], System.StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        return null;
    }

    private static GameObject FindObjectByName(Transform root, params string[] names)
    {
        Transform found = FindTransformByName(root, names);
        return found != null ? found.gameObject : null;
    }

    private static Transform FindTransformByName(Transform root, params string[] names)
    {
        if (root == null || names == null || names.Length == 0)
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null)
                continue;

            for (int j = 0; j < names.Length; j++)
            {
                if (string.Equals(child.name, names[j], System.StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }

        return null;
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }
}
