using TMPro;
using UnityEngine;

public class CaseWinItemUI : MonoBehaviour
{
    [SerializeField] private CaseOpeningScroll caseOpeningScroll;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text counterText;

    private void Start()
    {
        if (caseOpeningScroll != null)
            caseOpeningScroll.OnWinItemReady += Handle;
    }

    private void OnDestroy()
    {
        if (caseOpeningScroll != null)
            caseOpeningScroll.OnWinItemReady -= Handle;
    }

    private void Handle(CaseOpeningScroll.RollItemData itemData)
    {
        if (itemData == null)
            return;

        if (nameText != null)
            nameText.text = SanitizeName(itemData.name);

        int number = 0;

        if (InventoryManager.Instance != null)
            number = InventoryManager.Instance.GetOrCreateCurrentNumberForRoll(caseOpeningScroll, itemData);

        if (number <= 0)
            number = InventoryManager.PeekNextInventoryNumber();

        if (counterText != null)
        {
            string collectionName = caseOpeningScroll != null ? SanitizeName(caseOpeningScroll.GetCurrentGiftDisplayName()) : string.Empty;
            counterText.text = string.IsNullOrWhiteSpace(collectionName)
                ? "#" + number
                : collectionName + " #" + number;
        }
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("(Clone)", "").Trim();
    }
}
