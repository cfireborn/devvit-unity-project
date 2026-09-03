using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Local-only cinematic presentation for the COMPERSION definition.</summary>
public sealed class CompersionTitleCardUI : MonoBehaviour, IPointerDownHandler
{
    [Serializable]
    public sealed class RevealBeat
    {
        public CanvasGroup group;
        public float startTime;
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

    [SerializeField] CanvasGroup rootGroup;
    [SerializeField] RevealBeat[] revealBeats;

    readonly List<RevealLayer> _layers = new();
    Coroutine _revealRoutine;
    Action _onDismissed;
    AdminMenu _adminMenu;
    MobileInputManager _mobileInputManager;
    bool _isShowing;
    bool _revealComplete;
    float _acceptKeyboardAfter;
    int _lastAdvanceFrame = -1;

    public bool IsShowing => _isShowing;

    void Awake()
    {
        if (rootGroup == null)
            rootGroup = GetComponent<CanvasGroup>();
        CacheRevealLayers();
    }

    public void Configure(AdminMenu adminMenu, MobileInputManager mobileInputManager)
    {
        _adminMenu = adminMenu;
        _mobileInputManager = mobileInputManager;
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

        rootGroup.alpha = 1f;
        rootGroup.blocksRaycasts = true;
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
        if (rootGroup != null)
            rootGroup.blocksRaycasts = false;
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

    void CacheRevealLayers()
    {
        _layers.Clear();
        if (revealBeats == null) return;

        foreach (RevealBeat beat in revealBeats)
        {
            if (beat?.group == null) continue;
            RectTransform rect = beat.group.GetComponent<RectTransform>();
            _layers.Add(new RevealLayer
            {
                group = beat.group,
                rect = rect,
                restingPosition = rect.anchoredPosition,
                startTime = beat.startTime,
                duration = RevealDuration
            });
        }
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
        rootGroup.blocksRaycasts = false;
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
}
