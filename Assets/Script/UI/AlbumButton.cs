using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class AlbumButton : MonoBehaviour
{
    [Serializable]
    public class AlbumSourceBinding
    {
        public string giftId;
        public Button selectButton;
        public GameObject panelToOpen;
    }

    [Header("Album Sources")]
    [SerializeField] private AlbumSourceBinding[] albumSources;

    private readonly System.Collections.Generic.Dictionary<Button, UnityAction> registeredActions = new System.Collections.Generic.Dictionary<Button, UnityAction>();

    public AlbumSourceBinding[] AlbumSources => albumSources;

    private void Awake()
    {
        RegisterButtons();
    }

    private void OnEnable()
    {
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    public void RegisterButtons()
    {
        if (albumSources == null)
            return;

        for (int i = 0; i < albumSources.Length; i++)
        {
            AlbumSourceBinding binding = albumSources[i];
            if (binding == null || binding.selectButton == null || string.IsNullOrWhiteSpace(binding.giftId))
                continue;

            Button capturedButton = binding.selectButton;
            string capturedGiftId = binding.giftId;
            GameObject capturedPanel = binding.panelToOpen;

            if (registeredActions.TryGetValue(capturedButton, out UnityAction existingAction) && existingAction != null)
                capturedButton.onClick.RemoveListener(existingAction);

            UnityAction action = () => AlbumPreviewPanelOpener.OpenGiftId(capturedGiftId, capturedButton.gameObject, capturedPanel);
            registeredActions[capturedButton] = action;
            capturedButton.onClick.AddListener(action);
        }
    }

    public void UnregisterButtons()
    {
        foreach (System.Collections.Generic.KeyValuePair<Button, UnityAction> pair in registeredActions)
        {
            if (pair.Key != null && pair.Value != null)
                pair.Key.onClick.RemoveListener(pair.Value);
        }

        registeredActions.Clear();
    }
}
