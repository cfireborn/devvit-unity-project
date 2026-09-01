using UnityEngine;

/// <summary>
/// Moving-platform adapter and allocation-free presentation cache for a runtime ladder.
/// Child-name lookup and creation happen only while the cache warms; steady-state
/// ladder motion updates the root transform without rebuilding identical geometry.
/// </summary>
public class MovingPlatformLadder : MonoBehaviour, IMovingPlatform
{
    public const int MaxMiddleSegments = 64;

    SpriteRenderer _rootRenderer;
    BoxCollider2D _rootCollider;
    SpriteRenderer _bottom;
    SpriteRenderer _top;
    readonly SpriteRenderer[] _middle = new SpriteRenderer[MaxMiddleSegments];
    int _activeMiddleCount;

    bool _geometryValid;
    float _lastHeight;
    float _lastWidth;
    float _lastOverlap;
    Sprite _lastBottomSprite;
    Sprite _lastMiddleSprite;
    Sprite _lastTopSprite;

    public Vector2 GetPosition() => transform.position;
    public BoxCollider2D RootCollider => _rootCollider != null ? _rootCollider : (_rootCollider = GetComponent<BoxCollider2D>());

    void Awake()
    {
        _rootRenderer = GetComponent<SpriteRenderer>();
        _rootCollider = GetComponent<BoxCollider2D>();
        if (_rootRenderer != null)
            _rootRenderer.enabled = false;
    }

    public void SetRootPose(float x, float y)
    {
        Vector3 current = transform.position;
        if (Mathf.Abs(current.x - x) > 0.00001f || Mathf.Abs(current.y - y) > 0.00001f)
            transform.position = new Vector3(x, y, current.z);
        if (transform.localScale != Vector3.one)
            transform.localScale = Vector3.one;
    }

    public bool NeedsGeometryRebuild(float height, float width, float overlap, Sprite bottom, Sprite middle, Sprite top)
    {
        return !_geometryValid || Mathf.Abs(_lastHeight - height) > 0.0001f ||
            Mathf.Abs(_lastWidth - width) > 0.0001f || Mathf.Abs(_lastOverlap - overlap) > 0.0001f ||
            _lastBottomSprite != bottom || _lastMiddleSprite != middle || _lastTopSprite != top;
    }

    public void MarkGeometryRebuilt(float height, float width, float overlap, Sprite bottom, Sprite middle, Sprite top)
    {
        _geometryValid = true;
        _lastHeight = height;
        _lastWidth = width;
        _lastOverlap = overlap;
        _lastBottomSprite = bottom;
        _lastMiddleSprite = middle;
        _lastTopSprite = top;
    }

    public SpriteRenderer GetBottom(Sprite sprite) => GetOrCreatePart(ref _bottom, "Bottom", sprite);
    public SpriteRenderer GetTop(Sprite sprite) => GetOrCreatePart(ref _top, "Top", sprite);

    public SpriteRenderer GetMiddle(int index, Sprite sprite)
    {
        if (index < 0 || index >= MaxMiddleSegments) return null;
        SpriteRenderer renderer = _middle[index];
        if (renderer == null)
        {
            string childName = "Middle_" + index;
            Transform existing = transform.Find(childName);
            if (existing != null)
                renderer = existing.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = CreatePart(childName);
            _middle[index] = renderer;
        }
        renderer.sprite = sprite;
        renderer.gameObject.SetActive(true);
        renderer.enabled = sprite != null;
        return renderer;
    }

    public void SetActiveMiddleCount(int count)
    {
        count = Mathf.Clamp(count, 0, MaxMiddleSegments);
        for (int i = count; i < _activeMiddleCount; i++)
        {
            SpriteRenderer renderer = _middle[i];
            if (renderer != null)
                renderer.gameObject.SetActive(false);
        }
        _activeMiddleCount = count;
    }

    public void SetPresentationActive(bool active)
    {
        if (_rootRenderer == null)
            _rootRenderer = GetComponent<SpriteRenderer>();
        if (_rootRenderer != null)
            _rootRenderer.enabled = false;
        if (RootCollider != null)
            RootCollider.enabled = active;

        SetRendererActive(_bottom, active);
        SetRendererActive(_top, active);
        for (int i = 0; i < _activeMiddleCount; i++)
            SetRendererActive(_middle[i], active);
    }

    static void SetRendererActive(SpriteRenderer renderer, bool active)
    {
        if (renderer != null)
            renderer.enabled = active && renderer.sprite != null && renderer.gameObject.activeSelf;
    }

    SpriteRenderer GetOrCreatePart(ref SpriteRenderer cache, string childName, Sprite sprite)
    {
        if (cache == null)
        {
            Transform existing = transform.Find(childName);
            if (existing != null)
                cache = existing.GetComponent<SpriteRenderer>();
            if (cache == null)
                cache = CreatePart(childName);
        }
        cache.sprite = sprite;
        cache.gameObject.SetActive(true);
        cache.enabled = sprite != null;
        return cache;
    }

    SpriteRenderer CreatePart(string childName)
    {
        GameObject part = new GameObject(childName);
        part.transform.SetParent(transform, false);
        part.transform.localPosition = Vector3.zero;
        part.transform.localScale = Vector3.one;
        part.transform.localRotation = Quaternion.identity;
        return part.AddComponent<SpriteRenderer>();
    }
}
