using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Runtime-built, local-only cinematic presentation for the COMPERSION definition.</summary>
public sealed class CompersionTitleCardUI : MonoBehaviour, IPointerDownHandler
{
    /// <summary>
    /// Optional art surfaces exported from the Figma backdrop kit. Null entries use the
    /// code-native storybook fallback and never prevent the readable title presentation.
    /// </summary>
    [Serializable]
    public sealed class Skin
    {
        public Sprite fullScreenBackdrop;
        public Sprite titleBackdrop;
        public Sprite widePanel;
        public Sprite compactPanel;
        public Sprite continueRibbon;
        public Sprite polyamoryFlag;
        [Tooltip("Enable only when the assigned title backdrop already contains the flag artwork.")]
        public bool titleBackdropIncludesFlag;
        [Header("Fallback atmosphere (ignored when Full Screen Backdrop is assigned)")]
        public Sprite balloonAndHouseSilhouette;
        public Sprite cloudSilhouette;
    }

    sealed class RevealLayer
    {
        public CanvasGroup group;
        public RectTransform rect;
        public Vector2 restingPosition;
        public float startTime;
        public float duration;
    }

    const float RevealDuration = 0.42f;
    const float RevealSettlePixels = 18f;

    static readonly Color BackdropColor = new Color32(2, 5, 7, 255);            // Ink #020507
    static readonly Color UmberAtmosphere = new Color32(42, 23, 21, 255);        // Warm umber #2A1715
    static readonly Color SmokedPanel = new Color32(8, 40, 44, 230);             // Panel teal #08282C / 90%
    static readonly Color DeepSmokedPanel = new Color32(7, 16, 19, 238);         // Midnight teal #071013
    static readonly Color WarmGoldBorder = new Color32(217, 169, 79, 255);       // Primary gold #D9A94F
    static readonly Color WarmGoldHighlight = new Color32(246, 213, 140, 220);  // Content gold #F6D58C
    static readonly Color FlagHeartColor = new Color32(252, 191, 0, 255);         // #FCBF00
    static readonly Color CreamText = new Color32(233, 221, 191, 235);           // Warm cream #E9DDBF

    readonly List<RevealLayer> _layers = new();
    CanvasGroup _rootGroup;
    Coroutine _revealRoutine;
    Action _onDismissed;
    AdminMenu _adminMenu;
    MobileInputManager _mobileInputManager;
    bool _isShowing;
    bool _revealComplete;
    float _acceptKeyboardAfter;
    int _lastAdvanceFrame = -1;

    public bool IsShowing => _isShowing;

    public void Initialize(
        Sprite title,
        Sprite pronunciation,
        Sprite partOfSpeech,
        Sprite definitionLead,
        Sprite definitionBody,
        Skin skin,
        AdminMenu adminMenu,
        MobileInputManager mobileInputManager)
    {
        if (_rootGroup != null) return;

        _adminMenu = adminMenu;
        _mobileInputManager = mobileInputManager;
        skin ??= new Skin();

        Image inputBackdrop = GetComponent<Image>();
        inputBackdrop.color = BackdropColor;
        inputBackdrop.raycastTarget = true;
        _rootGroup = gameObject.AddComponent<CanvasGroup>();

        BuildBackdrop(skin);
        // EXPORT-01 already owns the rounded inset frame. Keep the code-native
        // frame only for the no-art fallback so authored and fallback borders
        // never double up.
        if (skin.fullScreenBackdrop == null)
            BuildFineFrame();

        GameObject titlePanel = BuildSurface(
            "TitleBeat",
            transform,
            new Vector2(0.05f, 0.75f),
            new Vector2(0.95f, 0.92f),
            skin.titleBackdrop,
            DeepSmokedPanel);
        BuildArtwork(titlePanel.transform, "TitleArtwork", title, new Vector2(0.045f, 0.38f), new Vector2(0.955f, 0.91f));
        // A stale "includes flag" checkbox must not remove the flag when its
        // associated authored title backdrop is later cleared in the Inspector.
        if (skin.titleBackdrop == null || !skin.titleBackdropIncludesFlag)
            BuildPolyamoryFlag(titlePanel.transform, skin.polyamoryFlag);
        AddRevealLayer(titlePanel, startTime: 0f);

        GameObject compactPanel = BuildSurface(
            "PronunciationAndNounBeat",
            transform,
            new Vector2(0.065f, 0.59f),
            new Vector2(0.935f, 0.695f),
            skin.compactPanel,
            SmokedPanel);
        BuildArtwork(compactPanel.transform, "PronunciationArtwork", pronunciation, new Vector2(0.035f, 0.18f), new Vector2(0.67f, 0.82f));
        BuildDivider(compactPanel.transform, 0.69f);
        BuildArtwork(compactPanel.transform, "NounArtwork", partOfSpeech, new Vector2(0.69f, 0.08f), new Vector2(0.965f, 0.92f));
        AddRevealLayer(compactPanel, startTime: 0.36f);

        GameObject leadPanel = BuildSurface(
            "DefinitionLeadBeat",
            transform,
            new Vector2(0.065f, 0.41f),
            new Vector2(0.935f, 0.55f),
            skin.widePanel,
            SmokedPanel);
        BuildArtwork(leadPanel.transform, "DefinitionLeadArtwork", definitionLead, new Vector2(0.035f, 0.16f), new Vector2(0.965f, 0.84f));
        AddRevealLayer(leadPanel, startTime: 0.72f);

        GameObject bodyPanel = BuildSurface(
            "DefinitionBodyBeat",
            transform,
            new Vector2(0.055f, 0.19f),
            new Vector2(0.945f, 0.39f),
            skin.widePanel,
            DeepSmokedPanel);
        BuildArtwork(bodyPanel.transform, "DefinitionBodyArtwork", definitionBody, new Vector2(0.035f, 0.08f), new Vector2(0.965f, 0.92f));
        AddRevealLayer(bodyPanel, startTime: 1.08f);

        GameObject prompt = BuildContinueRibbon(skin.continueRibbon);
        AddRevealLayer(prompt, startTime: 1.62f);
    }

    public void Show(Action onDismissed)
    {
        Cancel();
        _onDismissed = onDismissed;
        _isShowing = true;
        _revealComplete = false;
        _acceptKeyboardAfter = Time.unscaledTime + 0.15f;
        _lastAdvanceFrame = -1;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        _rootGroup.alpha = 1f;
        _rootGroup.blocksRaycasts = true;
        foreach (RevealLayer layer in _layers)
            SetLayerProgress(layer, 0f, beforeStart: true);
        _revealRoutine = StartCoroutine(RevealSequence());
    }

    /// <summary>Hide without invoking the story continuation.</summary>
    public void Cancel()
    {
        if (_revealRoutine != null)
        {
            StopCoroutine(_revealRoutine);
            _revealRoutine = null;
        }

        _onDismissed = null;
        _isShowing = false;
        _revealComplete = false;
        if (_rootGroup != null)
            _rootGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_isShowing || IsAdminOpen() || Time.unscaledTime < _acceptKeyboardAfter) return;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            HandleAdvanceRequest();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!_isShowing) return;
        if (_mobileInputManager != null && _mobileInputManager.TryHandleAdminCornerTap(eventData.position)) return;
        if (IsAdminOpen()) return;
        HandleAdvanceRequest();
    }

    void HandleAdvanceRequest()
    {
        // A browser/simulator may report multiple pointers, or pointer + Space,
        // in one frame. Treat them as one intent so reveal and dismiss stay distinct.
        if (_lastAdvanceFrame == Time.frameCount) return;
        _lastAdvanceFrame = Time.frameCount;

        if (!_revealComplete)
        {
            FinishRevealImmediately();
            return;
        }

        Action completion = _onDismissed;
        _onDismissed = null;
        _isShowing = false;
        _rootGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
        completion?.Invoke();
    }

    bool IsAdminOpen() => _adminMenu != null && _adminMenu.IsOpen;

    IEnumerator RevealSequence()
    {
        float elapsed = 0f;
        float endTime = 0f;
        foreach (RevealLayer layer in _layers)
            endTime = Mathf.Max(endTime, layer.startTime + layer.duration);

        while (elapsed < endTime)
        {
            elapsed = Mathf.Min(elapsed + Time.unscaledDeltaTime, endTime);
            foreach (RevealLayer layer in _layers)
            {
                if (elapsed < layer.startTime)
                {
                    SetLayerProgress(layer, 0f, beforeStart: true);
                    continue;
                }

                float progress = Mathf.Clamp01((elapsed - layer.startTime) / layer.duration);
                SetLayerProgress(layer, progress, beforeStart: false);
            }
            if (elapsed >= endTime)
                break;
            yield return null;
        }

        _revealRoutine = null;
        FinishRevealImmediately();
    }

    static void SetLayerProgress(RevealLayer layer, float progress, bool beforeStart)
    {
        float eased = beforeStart ? 0f : 1f - Mathf.Pow(1f - progress, 3f);
        layer.group.alpha = eased;
        layer.rect.anchoredPosition = layer.restingPosition + Vector2.up * (RevealSettlePixels * (1f - eased));
    }

    void FinishRevealImmediately()
    {
        if (_revealRoutine != null)
        {
            StopCoroutine(_revealRoutine);
            _revealRoutine = null;
        }

        foreach (RevealLayer layer in _layers)
        {
            layer.group.alpha = 1f;
            layer.rect.anchoredPosition = layer.restingPosition;
        }
        _revealComplete = true;
    }

    void AddRevealLayer(GameObject root, float startTime)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        _layers.Add(new RevealLayer
        {
            group = root.GetComponent<CanvasGroup>(),
            rect = rect,
            restingPosition = rect.anchoredPosition,
            startTime = startTime,
            duration = RevealDuration
        });
    }

    void BuildBackdrop(Skin skin)
    {
        if (skin.fullScreenBackdrop != null)
        {
            Image authoredBackdrop = CreateImage("AuthoredFullScreenBackdrop", transform, Vector2.zero, Vector2.one, Color.white);
            authoredBackdrop.sprite = skin.fullScreenBackdrop;
            authoredBackdrop.preserveAspect = true;
            var fitter = authoredBackdrop.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = skin.fullScreenBackdrop.rect.width / skin.fullScreenBackdrop.rect.height;
            return;
        }

        CreateImage("UmberWash", transform, new Vector2(0.42f, 0f), Vector2.one, new Color(UmberAtmosphere.r, UmberAtmosphere.g, UmberAtmosphere.b, 0.42f));
        CreateImage("DeepTealWash", transform, Vector2.zero, new Vector2(0.66f, 1f), new Color(0.02f, 0.12f, 0.13f, 0.34f));

        if (skin.balloonAndHouseSilhouette != null)
        {
            Image balloon = CreateImage("BalloonAndHouseSilhouette", transform, new Vector2(0.24f, 0.14f), new Vector2(1.04f, 0.93f), new Color(0.15f, 0.24f, 0.22f, 0.13f));
            balloon.sprite = skin.balloonAndHouseSilhouette;
            balloon.preserveAspect = true;
        }

        if (skin.cloudSilhouette != null)
        {
            BuildCloudSilhouette("CloudSilhouetteLower", skin.cloudSilhouette, new Vector2(-0.12f, 0.08f), new Vector2(0.66f, 0.25f), 0.09f);
            BuildCloudSilhouette("CloudSilhouetteUpper", skin.cloudSilhouette, new Vector2(0.38f, 0.78f), new Vector2(1.12f, 0.92f), 0.055f);
        }
    }

    void BuildCloudSilhouette(string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, float alpha)
    {
        Image cloud = CreateImage(name, transform, anchorMin, anchorMax, new Color(0.54f, 0.65f, 0.59f, alpha));
        cloud.sprite = sprite;
        cloud.preserveAspect = true;
    }

    void BuildFineFrame()
    {
        GameObject frame = CreateRect("WarmGoldFrame", transform, new Vector2(0.035f, 0.045f), new Vector2(0.965f, 0.955f));
        AddBorder(frame.transform, WarmGoldBorder, 1.5f, inset: 0f);
        AddBorder(frame.transform, new Color(WarmGoldHighlight.r, WarmGoldHighlight.g, WarmGoldHighlight.b, 0.45f), 1f, inset: 7f);
    }

    GameObject BuildSurface(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Sprite authoredSurface, Color fallbackFill)
    {
        GameObject panel = CreateRect(name, parent, anchorMin, anchorMax, typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        Image image = panel.GetComponent<Image>();
        image.raycastTarget = false;

        if (authoredSurface != null)
        {
            image.sprite = authoredSurface;
            image.color = Color.white;
            // The exported framed panels carry Sprite borders so their strokes,
            // corner radii, and border bleed survive small layout changes. The
            // borderless continue ornament stays a simple aspect-preserved image.
            bool hasSliceBorder = authoredSurface.border.sqrMagnitude > 0f;
            image.type = hasSliceBorder ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = !hasSliceBorder;
        }
        else
        {
            image.color = fallbackFill;
            AddBorder(panel.transform, WarmGoldBorder, 2f, inset: 0f);
            AddBorder(panel.transform, new Color(WarmGoldHighlight.r, WarmGoldHighlight.g, WarmGoldHighlight.b, 0.52f), 1f, inset: 7f);
        }

        return panel;
    }

    static void BuildArtwork(Transform parent, string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, float artworkScale = 1f)
    {
        Image artwork = CreateImage(name, parent, anchorMin, anchorMax, Color.white);
        artwork.sprite = sprite;
        artwork.preserveAspect = true;
        artwork.rectTransform.localScale = Vector3.one * artworkScale;
    }

    static void BuildDivider(Transform parent, float anchorX)
    {
        Image divider = CreateImage("WarmGoldDivider", parent, new Vector2(anchorX, 0.2f), new Vector2(anchorX, 0.8f), new Color(WarmGoldHighlight.r, WarmGoldHighlight.g, WarmGoldHighlight.b, 0.5f));
        divider.rectTransform.sizeDelta = new Vector2(1f, 0f);
    }

    void BuildPolyamoryFlag(Transform parent, Sprite authoredFlag)
    {
        GameObject outer = CreateRect("PolyamoryFlag", parent, new Vector2(0.41f, 0.025f), new Vector2(0.59f, 0.345f), typeof(CanvasRenderer), typeof(Image));
        Image outerImage = outer.GetComponent<Image>();
        outerImage.color = WarmGoldBorder;
        outerImage.raycastTarget = false;

        GameObject inner = CreateRect("FlagInner", outer.transform, Vector2.zero, Vector2.one, typeof(CanvasRenderer), typeof(Image));
        SetOffsets(inner.GetComponent<RectTransform>(), 2f);
        Image innerImage = inner.GetComponent<Image>();
        innerImage.color = new Color32(8, 22, 24, 255);
        innerImage.raycastTarget = false;

        if (authoredFlag != null)
        {
            innerImage.sprite = authoredFlag;
            innerImage.color = Color.white;
            innerImage.preserveAspect = true;
            return;
        }

        inner.AddComponent<RectMask2D>();
        CreateImage("CyanStripe", inner.transform, new Vector2(0f, 0.666f), Vector2.one, new Color32(0, 159, 227, 255));
        CreateImage("MagentaStripe", inner.transform, new Vector2(0f, 0.333f), new Vector2(1f, 0.666f), new Color32(229, 0, 81, 255));
        CreateImage("VioletStripe", inner.transform, Vector2.zero, new Vector2(1f, 0.333f), new Color32(52, 12, 70, 255));

        Image chevron = CreateImage("WhiteChevron", inner.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Color.white);
        chevron.rectTransform.sizeDelta = new Vector2(56f, 56f);
        chevron.rectTransform.anchoredPosition = new Vector2(-7f, 0f);
        chevron.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        GameObject heartObject = CreateRect("GoldHeart", inner.transform, new Vector2(0.015f, 0.08f), new Vector2(0.24f, 0.92f), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI heart = heartObject.GetComponent<TextMeshProUGUI>();
        heart.text = "♥";
        heart.color = FlagHeartColor;
        heart.alignment = TextAlignmentOptions.Center;
        heart.enableAutoSizing = true;
        heart.fontSizeMin = 8f;
        heart.fontSizeMax = 28f;
        heart.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            heart.font = TMP_Settings.defaultFontAsset;
    }

    GameObject BuildContinueRibbon(Sprite authoredRibbon)
    {
        GameObject root = CreateRect(
            "ContinueBeat",
            transform,
            new Vector2(0.05f, 0.055f),
            new Vector2(0.95f, 0.17f),
            typeof(CanvasGroup));
        BuildSurface(
            "RibbonSurface",
            root.transform,
            new Vector2(0f, 0.61f),
            new Vector2(1f, 0.9f),
            authoredRibbon,
            new Color(UmberAtmosphere.r, UmberAtmosphere.g, UmberAtmosphere.b, 0.9f));

        GameObject promptObject = CreateRect("ContinuePrompt", root.transform, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.46f), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI prompt = promptObject.GetComponent<TextMeshProUGUI>();
        prompt.text = "Tap or press Space to continue";
        prompt.fontSize = 18f;
        prompt.enableAutoSizing = true;
        prompt.fontSizeMin = 11f;
        prompt.fontSizeMax = 18f;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.color = CreamText;
        prompt.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            prompt.font = TMP_Settings.defaultFontAsset;
        return root;
    }

    static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, params Type[] extraComponents)
    {
        var componentTypes = new Type[extraComponents.Length + 1];
        componentTypes[0] = typeof(RectTransform);
        Array.Copy(extraComponents, 0, componentTypes, 1, extraComponents.Length);
        var root = new GameObject(name, componentTypes);
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return root;
    }

    static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject root = CreateRect(name, parent, anchorMin, anchorMax, typeof(CanvasRenderer), typeof(Image));
        Image image = root.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void AddBorder(Transform parent, Color color, float thickness, float inset)
    {
        Image top = CreateImage("BorderTop", parent, new Vector2(0f, 1f), Vector2.one, color);
        top.rectTransform.sizeDelta = new Vector2(-inset * 2f, thickness);
        top.rectTransform.anchoredPosition = new Vector2(0f, -inset);

        Image bottom = CreateImage("BorderBottom", parent, Vector2.zero, new Vector2(1f, 0f), color);
        bottom.rectTransform.sizeDelta = new Vector2(-inset * 2f, thickness);
        bottom.rectTransform.anchoredPosition = new Vector2(0f, inset);

        Image left = CreateImage("BorderLeft", parent, Vector2.zero, new Vector2(0f, 1f), color);
        left.rectTransform.sizeDelta = new Vector2(thickness, -inset * 2f);
        left.rectTransform.anchoredPosition = new Vector2(inset, 0f);

        Image right = CreateImage("BorderRight", parent, new Vector2(1f, 0f), Vector2.one, color);
        right.rectTransform.sizeDelta = new Vector2(thickness, -inset * 2f);
        right.rectTransform.anchoredPosition = new Vector2(-inset, 0f);
    }

    static void SetOffsets(RectTransform rect, float inset)
    {
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
