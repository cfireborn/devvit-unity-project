#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Bakes the authored COMPERSION title card hierarchy into a prefab.</summary>
[InitializeOnLoad]
public static class CompersionTitleCardPrefabBuilder
{
    const string PrefabPath = "Assets/UI/CompersionTitleCard.prefab";

    static CompersionTitleCardPrefabBuilder()
    {
        EditorApplication.delayCall += EnsurePrefabExists;
    }

    static void EnsurePrefabExists()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            return;
        Build();
    }

    static readonly Color BackdropColor = new Color32(2, 5, 7, 255);
    static readonly Color UmberAtmosphere = new Color32(42, 23, 21, 255);
    static readonly Color SmokedPanel = new Color32(8, 40, 44, 230);
    static readonly Color DeepSmokedPanel = new Color32(7, 16, 19, 238);
    static readonly Color WarmGoldBorder = new Color32(217, 169, 79, 255);
    static readonly Color WarmGoldHighlight = new Color32(246, 213, 140, 220);
    static readonly Color FlagHeartColor = new Color32(252, 191, 0, 255);
    static readonly Color CreamText = new Color32(233, 221, 191, 235);

    [MenuItem("Compersion/Bake Title Card Prefab")]
    public static void BuildFromMenu() => Build();

    public static void Build()
    {
        var title = LoadSprite("Assets/UI/compersion-title-card/title-com-per-sion@2x.png");
        var pronunciation = LoadSprite("Assets/UI/compersion-title-card/pronunciation@2x.png");
        var partOfSpeech = LoadSprite("Assets/UI/compersion-title-card/part-of-speech@2x.png");
        var definitionLead = LoadSprite("Assets/UI/compersion-title-card/definition-lead@2x.png");
        var definitionBody = LoadSprite("Assets/UI/compersion-title-card/definition-body@2x.png");
        var skin = new Skin
        {
            fullScreenBackdrop = LoadSprite("Assets/UI/compersion-title-card/backdrop-kit/EXPORT-01—Full-Screen-Vector-Backdrop@2x.png"),
            titleBackdrop = LoadSprite("Assets/UI/compersion-title-card/backdrop-kit/EXPORT-02—Title-Backdrop@2x.png"),
            widePanel = LoadSprite("Assets/UI/compersion-title-card/backdrop-kit/EXPORT-03—Content-Panel-Wide@2x.png"),
            compactPanel = LoadSprite("Assets/UI/compersion-title-card/backdrop-kit/EXPORT-04—Compact-Panel@2x.png"),
            continueRibbon = LoadSprite("Assets/UI/compersion-title-card/backdrop-kit/EXPORT-05—Continue-Ribbon@2x.png"),
            balloonAndHouseSilhouette = LoadSprite("Assets/Scene/Balloons/Assets/Balloon_Koi.png"),
            cloudSilhouette = LoadSprite("Assets/Scene/Clouds/Assets/FullClouds/Cloud_Sprites.png")
        };

        var root = new GameObject(
            "CompersionTitleCard",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup),
            typeof(CompersionTitleCardUI));
        var rootRect = root.GetComponent<RectTransform>();
        StretchFull(rootRect);

        var inputBackdrop = root.GetComponent<Image>();
        inputBackdrop.color = BackdropColor;
        inputBackdrop.raycastTarget = true;

        BuildBackdrop(skin, root.transform);
        if (skin.fullScreenBackdrop == null)
            BuildFineFrame(root.transform);

        GameObject titlePanel = BuildSurface(
            "TitleBeat",
            root.transform,
            new Vector2(0.05f, 0.75f),
            new Vector2(0.95f, 0.92f),
            skin.titleBackdrop,
            DeepSmokedPanel);
        BuildArtwork(titlePanel.transform, "TitleArtwork", title, new Vector2(0.045f, 0.38f), new Vector2(0.955f, 0.91f));
        if (skin.titleBackdrop == null || !skin.titleBackdropIncludesFlag)
            BuildPolyamoryFlag(titlePanel.transform, skin.polyamoryFlag);

        GameObject compactPanel = BuildSurface(
            "PronunciationAndNounBeat",
            root.transform,
            new Vector2(0.065f, 0.59f),
            new Vector2(0.935f, 0.695f),
            skin.compactPanel,
            SmokedPanel);
        BuildArtwork(compactPanel.transform, "PronunciationArtwork", pronunciation, new Vector2(0.035f, 0.18f), new Vector2(0.67f, 0.82f));
        BuildDivider(compactPanel.transform, 0.69f);
        BuildArtwork(compactPanel.transform, "NounArtwork", partOfSpeech, new Vector2(0.69f, 0.08f), new Vector2(0.965f, 0.92f));

        GameObject leadPanel = BuildSurface(
            "DefinitionLeadBeat",
            root.transform,
            new Vector2(0.065f, 0.41f),
            new Vector2(0.935f, 0.55f),
            skin.widePanel,
            SmokedPanel);
        BuildArtwork(leadPanel.transform, "DefinitionLeadArtwork", definitionLead, new Vector2(0.035f, 0.16f), new Vector2(0.965f, 0.84f));

        GameObject bodyPanel = BuildSurface(
            "DefinitionBodyBeat",
            root.transform,
            new Vector2(0.055f, 0.19f),
            new Vector2(0.945f, 0.39f),
            skin.widePanel,
            DeepSmokedPanel);
        BuildArtwork(bodyPanel.transform, "DefinitionBodyArtwork", definitionBody, new Vector2(0.035f, 0.08f), new Vector2(0.965f, 0.92f));

        GameObject prompt = BuildContinueRibbon(skin.continueRibbon, root.transform);

        var card = root.GetComponent<CompersionTitleCardUI>();
        var serialized = new SerializedObject(card);
        serialized.FindProperty("rootGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        serialized.FindProperty("revealBeats").arraySize = 5;
        AssignRevealBeat(serialized, 0, titlePanel, 0f);
        AssignRevealBeat(serialized, 1, compactPanel, 0.36f);
        AssignRevealBeat(serialized, 2, leadPanel, 0.72f);
        AssignRevealBeat(serialized, 3, bodyPanel, 1.08f);
        AssignRevealBeat(serialized, 4, prompt, 1.62f);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Saved {PrefabPath}");
    }

    sealed class Skin
    {
        public Sprite fullScreenBackdrop;
        public Sprite titleBackdrop;
        public Sprite widePanel;
        public Sprite compactPanel;
        public Sprite continueRibbon;
        public Sprite polyamoryFlag;
        public bool titleBackdropIncludesFlag;
        public Sprite balloonAndHouseSilhouette;
        public Sprite cloudSilhouette;
    }

    static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

    static void AssignRevealBeat(SerializedObject serialized, int index, GameObject panel, float startTime)
    {
        var beats = serialized.FindProperty("revealBeats");
        var beat = beats.GetArrayElementAtIndex(index);
        beat.FindPropertyRelative("group").objectReferenceValue = panel.GetComponent<CanvasGroup>();
        beat.FindPropertyRelative("startTime").floatValue = startTime;
    }

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    static void BuildBackdrop(Skin skin, Transform parent)
    {
        if (skin.fullScreenBackdrop != null)
        {
            Image authoredBackdrop = CreateImage("AuthoredFullScreenBackdrop", parent, Vector2.zero, Vector2.one, Color.white);
            authoredBackdrop.sprite = skin.fullScreenBackdrop;
            authoredBackdrop.preserveAspect = true;
            var fitter = authoredBackdrop.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = skin.fullScreenBackdrop.rect.width / skin.fullScreenBackdrop.rect.height;
            return;
        }

        CreateImage("UmberWash", parent, new Vector2(0.42f, 0f), Vector2.one, new Color(UmberAtmosphere.r, UmberAtmosphere.g, UmberAtmosphere.b, 0.42f));
        CreateImage("DeepTealWash", parent, Vector2.zero, new Vector2(0.66f, 1f), new Color(0.02f, 0.12f, 0.13f, 0.34f));

        if (skin.balloonAndHouseSilhouette != null)
        {
            Image balloon = CreateImage("BalloonAndHouseSilhouette", parent, new Vector2(0.24f, 0.14f), new Vector2(1.04f, 0.93f), new Color(0.15f, 0.24f, 0.22f, 0.13f));
            balloon.sprite = skin.balloonAndHouseSilhouette;
            balloon.preserveAspect = true;
        }

        if (skin.cloudSilhouette != null)
        {
            BuildCloudSilhouette("CloudSilhouetteLower", parent, skin.cloudSilhouette, new Vector2(-0.12f, 0.08f), new Vector2(0.66f, 0.25f), 0.09f);
            BuildCloudSilhouette("CloudSilhouetteUpper", parent, skin.cloudSilhouette, new Vector2(0.38f, 0.78f), new Vector2(1.12f, 0.92f), 0.055f);
        }
    }

    static void BuildCloudSilhouette(string name, Transform parent, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, float alpha)
    {
        Image cloud = CreateImage(name, parent, anchorMin, anchorMax, new Color(0.54f, 0.65f, 0.59f, alpha));
        cloud.sprite = sprite;
        cloud.preserveAspect = true;
    }

    static void BuildFineFrame(Transform parent)
    {
        GameObject frame = CreateRect("WarmGoldFrame", parent, new Vector2(0.035f, 0.045f), new Vector2(0.965f, 0.955f));
        AddBorder(frame.transform, WarmGoldBorder, 1.5f, inset: 0f);
        AddBorder(frame.transform, new Color(WarmGoldHighlight.r, WarmGoldHighlight.g, WarmGoldHighlight.b, 0.45f), 1f, inset: 7f);
    }

    static GameObject BuildSurface(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Sprite authoredSurface, Color fallbackFill)
    {
        GameObject panel = CreateRect(name, parent, anchorMin, anchorMax, typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        Image image = panel.GetComponent<Image>();
        image.raycastTarget = false;

        if (authoredSurface != null)
        {
            image.sprite = authoredSurface;
            image.color = Color.white;
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

    static void BuildPolyamoryFlag(Transform parent, Sprite authoredFlag)
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

    static GameObject BuildContinueRibbon(Sprite authoredRibbon, Transform parent)
    {
        GameObject root = CreateRect(
            "ContinueBeat",
            parent,
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
#endif
