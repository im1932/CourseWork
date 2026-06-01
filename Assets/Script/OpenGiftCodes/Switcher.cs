using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections.Generic;

public class PanelQueueManager : MonoBehaviour
{
    [SerializeField] private Transform panelsParent;

    private static readonly string PanelNameKey = "UP Panel";
    private static readonly string UpgradeButtonName = "Upgrade";
    private static readonly string NextButtonName = "Next";
    private static readonly bool FirstUpgradeDoesNothing = true;
    private static readonly bool ActivateFirstPanelOnStart = false;

    private Transform activePanel;
    private UnityAction upgradeAction;
    private UnityAction nextAction;
    private readonly HashSet<int> firstUpgradeHandledPanels = new HashSet<int>();

    private void Awake()
    {
        upgradeAction = OnUpgradePressed;
        nextAction = OnNextPressed;
    }

    private void Start()
    {
        if (panelsParent == null)
            panelsParent = transform;

        var panels = GetPanels();
        if (panels.Count == 0)
            return;

        for (int i = 0; i < panels.Count; i++)
            panels[i].gameObject.SetActive(false);

        activePanel = ActivateFirstPanelOnStart ? panels[0] : null;

        if (ActivateFirstPanelOnStart)
            panels[0].gameObject.SetActive(true);

        SyncActivePanelAndButtons();
    }

    private void Update()
    {
        SyncActivePanelAndButtons();
    }

    private List<Transform> GetPanels()
    {
        List<Transform> result = new List<Transform>();

        if (panelsParent == null)
            return result;

        for (int i = 0; i < panelsParent.childCount; i++)
        {
            Transform child = panelsParent.GetChild(i);

            if (child != null && child.name.Contains(PanelNameKey))
                result.Add(child);
        }

        result.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
        return result;
    }

    private void RebindButtons()
    {
        var panels = GetPanels();

        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i] == null)
                continue;

            bool enable = panels[i] == activePanel && panels[i].gameObject.activeInHierarchy;

            BindButton(panels[i], UpgradeButtonName, enable, upgradeAction);
            BindButton(panels[i], NextButtonName, enable, nextAction);
        }
    }

    private void SyncActivePanelAndButtons()
    {
        var panels = GetPanels();
        if (panels.Count == 0)
            return;

        if (activePanel == null || !activePanel.gameObject.activeInHierarchy)
            activePanel = FindFirstActivePanel(panels);

        RebindButtons();
    }

    private Transform FindFirstActivePanel(List<Transform> panels)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            if (panels[i] != null && panels[i].gameObject.activeInHierarchy)
                return panels[i];
        }

        return null;
    }

    private Transform FindInactiveReplacementSource(List<Transform> panels, Transform currentPanel)
    {
        for (int i = panels.Count - 1; i >= 0; i--)
        {
            Transform panel = panels[i];
            if (panel == null || panel == currentPanel)
                continue;

            if (!panel.gameObject.activeInHierarchy)
                return panel;
        }

        for (int i = panels.Count - 1; i >= 0; i--)
        {
            Transform panel = panels[i];
            if (panel != null && panel != currentPanel)
                return panel;
        }

        return null;
    }

    private void BindButton(Transform panel, string buttonName, bool enable, UnityAction action)
    {
        if (panel == null || string.IsNullOrWhiteSpace(buttonName) || action == null)
            return;

        Button[] buttons = panel.GetComponentsInChildren<Button>(true);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            if (button.name != buttonName)
                continue;

            if (button.onClick == null)
                continue;

            button.onClick.RemoveListener(action);

            if (enable)
                button.onClick.AddListener(action);

            button.interactable = enable;
        }
    }

    private void OnUpgradePressed()
    {
        if (FirstUpgradeDoesNothing)
        {
            if (activePanel == null || !activePanel.gameObject.activeInHierarchy)
                activePanel = FindFirstActivePanel(GetPanels());

            if (activePanel == null)
                return;

            int panelId = activePanel.GetInstanceID();
            if (firstUpgradeHandledPanels.Add(panelId))
                return;
        }

        if (AchievementManager.Instance != null)
            AchievementManager.Instance.RegisterUpgrade();
    }

    private void OnNextPressed()
    {
        var panels = GetPanels();
        if (panels.Count < 2)
            return;

        if (activePanel == null)
        {
            activePanel = panels[0];
            activePanel.gameObject.SetActive(true);
            RebindButtons();
            return;
        }

        Transform currentPanel = activePanel;
        int currentIndex = panels.IndexOf(currentPanel);

        if (currentIndex < 0)
            currentIndex = 0;

        int nextIndex = currentIndex + 1;
        if (nextIndex >= panels.Count)
            nextIndex = 0;

        Transform nextPanel = panels[nextIndex];

        GameObject copy = Instantiate(nextPanel.gameObject, panelsParent);
        copy.name = nextPanel.name;
        copy.transform.SetAsFirstSibling();
        copy.SetActive(false);

        nextPanel.gameObject.SetActive(false);
        nextPanel.gameObject.SetActive(true);
        nextPanel.SetAsFirstSibling();
        currentPanel.gameObject.SetActive(false);

        currentPanel.SetParent(null);
        Destroy(currentPanel.gameObject);

        activePanel = nextPanel;

        RebindButtons();
    }

    public void ReplaceActivePanelWithInactiveCopy()
    {
        var panels = GetPanels();
        if (panels.Count == 0)
            return;

        if (activePanel == null || !activePanel.gameObject.activeInHierarchy)
            activePanel = FindFirstActivePanel(panels);

        if (activePanel == null)
            return;

        Transform currentPanel = activePanel;
        Transform sourcePanel = FindInactiveReplacementSource(panels, currentPanel);
        if (sourcePanel == null)
            sourcePanel = currentPanel;

        GameObject copy = Instantiate(sourcePanel.gameObject, panelsParent);
        copy.name = sourcePanel.name;
        copy.transform.SetAsFirstSibling();
        copy.SetActive(false);

        currentPanel.SetParent(null);
        Destroy(currentPanel.gameObject);

        activePanel = null;
        RebindButtons();
    }
}