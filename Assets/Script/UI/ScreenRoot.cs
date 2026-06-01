using UnityEngine;
using UnityEngine.UI;

public class ScreenSwitcher : MonoBehaviour
{
    public RectTransform screenRoot;

    public RectTransform mainScreen;
    public RectTransform settingsScreen;
    public RectTransform profileScreen;
    public RectTransform albumScreen;

    public Button mainButton;
    public Button settingsButton;
    public Button profileButton;
    public Button albumButton;

    public float speed = 10f;
    [Header("Performance")]
    public bool deactivateInactiveScreens = true;
    public float transitionCompleteDistance = 40f;
    public float deactivateDelay = 0.12f;
    public bool snapToTargetBeforeDeactivate = true;

    private Vector2 targetPos;
    private RectTransform currentScreen;
    private RectTransform pendingScreen;
    private float transitionSettledAt = -1f;
    private RectTransform[] screens;

    void Start()
    {
        CacheScreens();
        targetPos = screenRoot != null ? screenRoot.anchoredPosition : Vector2.zero;

        if (mainButton != null) mainButton.onClick.AddListener(OpenMain);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (profileButton != null) profileButton.onClick.AddListener(OpenProfile);
        if (albumButton != null) albumButton.onClick.AddListener(OpenAlbum);

        currentScreen = ResolveClosestScreen();
        pendingScreen = currentScreen;

        if (deactivateInactiveScreens)
            ApplyTransitionScreenState(currentScreen, null);
    }

    void Update()
    {
        if (screenRoot == null)
            return;

        screenRoot.anchoredPosition = Vector2.Lerp(
            screenRoot.anchoredPosition,
            targetPos,
            Time.deltaTime * speed
        );

        if (!deactivateInactiveScreens || pendingScreen == null)
            return;

        float distanceToTarget = Vector2.Distance(screenRoot.anchoredPosition, targetPos);
        if (distanceToTarget <= Mathf.Max(1f, transitionCompleteDistance))
        {
            if (transitionSettledAt < 0f)
                transitionSettledAt = Time.unscaledTime;

            if (Time.unscaledTime - transitionSettledAt >= Mathf.Max(0f, deactivateDelay))
            {
                if (snapToTargetBeforeDeactivate)
                    screenRoot.anchoredPosition = targetPos;

                currentScreen = pendingScreen;
                ApplyTransitionScreenState(currentScreen, null);
                transitionSettledAt = -1f;
            }
        }
        else
        {
            transitionSettledAt = -1f;
        }
    }

    void OpenMain()
    {
        OpenScreen(mainScreen);
    }

    void OpenSettings()
    {
        OpenScreen(settingsScreen);
    }

    void OpenProfile()
    {
        OpenScreen(profileScreen);
    }

    void OpenAlbum()
    {
        OpenScreen(albumScreen);
    }

    private void OpenScreen(RectTransform screen)
    {
        if (screen == null)
            return;

        RectTransform fromScreen = ResolveClosestScreen();
        if (fromScreen != null)
            currentScreen = fromScreen;

        targetPos = -screen.anchoredPosition;
        pendingScreen = screen;
        transitionSettledAt = -1f;

        if (!deactivateInactiveScreens)
            return;

        ApplyTransitionScreenState(currentScreen, pendingScreen);
    }

    private RectTransform ResolveClosestScreen()
    {
        RectTransform[] screens = GetScreens();
        RectTransform bestScreen = null;
        float bestDistance = float.MaxValue;
        Vector2 currentPosition = screenRoot != null ? screenRoot.anchoredPosition : Vector2.zero;

        for (int i = 0; i < screens.Length; i++)
        {
            RectTransform screen = screens[i];
            if (screen == null)
                continue;

            float distance = Vector2.Distance(currentPosition, -screen.anchoredPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestScreen = screen;
            }
        }

        return bestScreen != null ? bestScreen : mainScreen;
    }

    private void ApplyTransitionScreenState(RectTransform primaryScreen, RectTransform secondaryScreen)
    {
        int primaryIndex = GetScreenIndex(primaryScreen);
        int secondaryIndex = GetScreenIndex(secondaryScreen);
        int minIndex = Mathf.Min(primaryIndex, secondaryIndex);
        int maxIndex = Mathf.Max(primaryIndex, secondaryIndex);
        RectTransform[] screens = GetScreens();
        for (int i = 0; i < screens.Length; i++)
        {
            RectTransform screen = screens[i];
            if (screen == null)
                continue;

            bool shouldBeActive = false;
            if (primaryIndex >= 0 && secondaryIndex >= 0)
                shouldBeActive = i >= minIndex && i <= maxIndex;
            else
                shouldBeActive = screen == primaryScreen || screen == secondaryScreen;

            if (screen.gameObject.activeSelf != shouldBeActive)
                screen.gameObject.SetActive(shouldBeActive);
        }
    }

    private int GetScreenIndex(RectTransform screen)
    {
        if (screen == null)
            return -1;

        RectTransform[] screens = GetScreens();
        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i] == screen)
                return i;
        }

        return -1;
    }

    private RectTransform[] GetScreens()
    {
        if (screens == null || screens.Length != 4)
        {
            CacheScreens();
        }

        return screens;
    }

    private void CacheScreens()
    {
        screens = new RectTransform[]
        {
            mainScreen,
            settingsScreen,
            albumScreen,
            profileScreen
        };
    }
}
