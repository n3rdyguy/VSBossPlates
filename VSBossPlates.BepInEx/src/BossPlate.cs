using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VampireSurvivors.Objects.Characters;
using Object = UnityEngine.Object;

namespace VSBossPlates;

/// <summary>
/// One health plate: background, fill bar, name and HP numbers.
///
/// The plate is a world-space Canvas, not a screen-space one, and that is the single most
/// consequential decision in the mod. The obvious approach - a screen overlay positioned with
/// Camera.WorldToScreenPoint - does not survive contact with this game, because the game
/// renders through a render texture. CameraExtensions carries GetRtZoomScaling() and
/// GetRenderTextureSize() precisely because of it, so WorldToScreenPoint returns coordinates
/// in render-texture space rather than backbuffer space, and every window resize and camera
/// zoom would need that conversion redone. A world-space canvas is drawn by the gameplay
/// camera in the same pass as the sprites, so it tracks zoom and resolution for nothing.
///
/// The plate is deliberately NOT parented to the enemy. Pooled enemies are deactivated and
/// reused rather than destroyed, so a child plate would be deactivated with its boss, sit
/// dormant in the pool, and reappear over whatever enemy that instance became next.
/// </summary>
internal sealed class BossPlate
{
    // Canvas units. Multiplied by Plugin.PlateScale to reach world units.
    private const float PlateWidth = 200f;
    private const float PlateHeight = 56f;
    private const float BarHeightFraction = 0.42f;
    private const float BarInset = 2f;
    private const float TextPadding = 4f;

    private GameObject _root;
    // Each Unity property access crosses the managed/IL2CPP boundary. Keep the component handle
    // and the last values sent to the UI so an unchanged boss does not repeat native calls every
    // frame merely to write the same state again.
    private Transform _rootTransform;
    private RectTransform _fill;
    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _hpText;
    private bool _visible = true;
    private float _lastFraction = float.NaN;
    private float _lastCurrent = float.NaN;
    private float _lastMax = float.NaN;
    private string _lastHpText;

    /// <summary>A scheduled stage boss rather than a mini-boss. Decides which scale applies -
    /// see Scale.</summary>
    private bool _isMajor;

    private static Sprite _whiteSprite;
    private static TMP_FontAsset _font;
    private static bool _warnedNoFont;

    internal static BossPlate Create(EnemyController enemy, string displayName, bool isMajor)
    {
        BossPlate plate = null;
        try
        {
            Camera cam = ResolveCamera(enemy);

            plate = new BossPlate();
            plate._isMajor = isMajor;
            plate.Build(enemy, displayName, cam);
            return plate._root == null ? null : plate;
        }
        catch (Exception ex)
        {
            // Build can fail after creating the root. Leaving that partial canvas behind leaks a
            // native object every retry, which turned one bad transform into thousands of them.
            if (plate != null) plate.Destroy();
            Plugin.Log.LogWarning("Could not build boss plate: " + ex.Message);
            return null;
        }
    }

    private void Build(EnemyController enemy, string displayName, Camera cam)
    {
        _root = new GameObject("VSBossPlate");
        Canvas canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        // Above the sprites the gameplay camera draws, but well below anything the game's own
        // screen-space UI uses, so a plate can never cover the pause menu.
        canvas.sortingOrder = 500;

        // The enemy's layer is guaranteed to be in the gameplay camera's culling mask; a
        // default-layer canvas is not.
        try { _root.layer = enemy.gameObject.layer; } catch { }

        var rootRect = _root.GetComponent<RectTransform>();
        // Adding Canvas replaces a new GameObject's Transform with a RectTransform. Cache only
        // after that conversion; the old wrapper points at a destroyed native component.
        _rootTransform = rootRect;
        rootRect.sizeDelta = new Vector2(PlateWidth, PlateHeight);
        _rootTransform.localScale = Vector3.one * Scale;
        rootRect.rotation = Quaternion.identity;
        PerformanceStats.RecordStaticTransformWrites(2);

        Sprite white = GetWhiteSprite();

        // Bar background doubles as the border: the fill is inset inside it on all sides.
        GameObject barGo = NewChild(_root.transform, "Bar");
        var barRect = barGo.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, BarHeightFraction);
        barRect.offsetMin = Vector2.zero;
        barRect.offsetMax = Vector2.zero;
        Image barImage = barGo.AddComponent<Image>();
        barImage.sprite = white;
        ((Graphic)barImage).color = new Color(0.05f, 0.05f, 0.06f, 0.85f);
        ((Graphic)barImage).raycastTarget = false;

        GameObject fillGo = NewChild(barGo.transform, "Fill");
        _fill = fillGo.GetComponent<RectTransform>();
        _fill.anchorMin = new Vector2(0f, 0f);
        _fill.anchorMax = new Vector2(1f, 1f);
        _fill.offsetMin = new Vector2(BarInset, BarInset);
        _fill.offsetMax = new Vector2(-BarInset, -BarInset);
        Image fillImage = fillGo.AddComponent<Image>();
        fillImage.sprite = white;
        ((Graphic)fillImage).color = new Color(0.78f, 0.13f, 0.16f, 1f);
        ((Graphic)fillImage).raycastTarget = false;

        TMP_FontAsset font = GetFont();

        if (Plugin.ShowName)
        {
            // A backing panel, not bare text. White text alone disappears on the pale stone
            // floors, and an outline would mean building a TMP material variant per plate.
            GameObject nameBgGo = NewChild(_root.transform, "NameBg");
            var nameBgRect = nameBgGo.GetComponent<RectTransform>();
            nameBgRect.anchorMin = new Vector2(0f, BarHeightFraction);
            nameBgRect.anchorMax = new Vector2(1f, 1f);
            nameBgRect.offsetMin = Vector2.zero;
            nameBgRect.offsetMax = Vector2.zero;
            Image nameBgImage = nameBgGo.AddComponent<Image>();
            nameBgImage.sprite = white;
            ((Graphic)nameBgImage).color = new Color(0.05f, 0.05f, 0.06f, 0.6f);
            ((Graphic)nameBgImage).raycastTarget = false;

            _nameText = AddText(
                nameBgGo.transform, "Name", displayName, font, 22f,
                new Color(1f, 1f, 1f, 1f), TextAlignmentOptions.Midline,
                TextPadding);
        }

        if (Plugin.ShowNumbers)
        {
            _hpText = AddText(
                barGo.transform, "Hp", "", font, 16f,
                new Color(1f, 1f, 1f, 0.95f), TextAlignmentOptions.Midline,
                TextPadding);
        }
    }

    /// <summary>
    /// Camera resolution is deliberately lazy and defensive. EnemyController caches its own
    /// MainCamera, which is the cheapest handle available, but nothing in the interop
    /// assemblies says whether it is populated by the time a spawn hook fires - the
    /// assemblies carry no method bodies. Camera.main is the fallback rather than the first
    /// choice because it is a tagged lookup.
    /// </summary>
    private static Camera ResolveCamera(EnemyController enemy)
    {
        try
        {
            Camera cam = enemy.MainCamera;
            if ((Object)(object)cam != (Object)null) return cam;
        }
        catch { }

        return Camera.main;
    }

    internal void Refresh(float fraction, float current, float max)
    {
        if (_root == null) return;

        // Width is driven by the anchor rather than Image.fillAmount so the bar does not
        // depend on a sprite being sliced or on Image.type surviving a null sprite.
        if (_fill != null && fraction != _lastFraction)
        {
            _lastFraction = fraction;
            Vector2 anchorMax = _fill.anchorMax;
            anchorMax.x = fraction;
            _fill.anchorMax = anchorMax;
            PerformanceStats.RecordFillWrites(1);
        }

        if (_hpText != null && (current != _lastCurrent || max != _lastMax))
        {
            _lastCurrent = current;
            _lastMax = max;

            PerformanceStats.RecordHpFormat();
            string text = FormatPair(current, max);
            if (text != _lastHpText)
            {
                _lastHpText = text;
                ((TMP_Text)_hpText).text = text;
                PerformanceStats.RecordHpTextWrite();
            }
        }
    }

    /// <summary>
    /// Sits on top of the sprite's own bounds rather than at a fixed height above the
    /// transform origin, because bosses differ enormously in size and a fixed offset either
    /// overlaps the small ones or floats far above the large ones.
    /// </summary>
    internal void PositionAbove(EnemyController enemy)
    {
        if (_root == null) return;

        Vector3 basePos;
        try { basePos = enemy.transform.position; }
        catch { return; }

        float top = basePos.y;
        try
        {
            SpriteRenderer renderer = enemy.EnemyRenderer;
            if ((Object)(object)renderer != (Object)null && renderer.enabled)
            {
                top = renderer.bounds.max.y;
            }
        }
        catch { }

        float scale = Scale;
        float halfPlate = PlateHeight * 0.5f * scale;
        _rootTransform.position = new Vector3(
            basePos.x,
            top + Plugin.VerticalOffset + halfPlate,
            basePos.z);
        PerformanceStats.RecordPositionWrite();
    }

    internal void SetVisible(bool visible)
    {
        if (_root == null || _visible == visible) return;
        _visible = visible;
        _root.SetActive(visible);
    }

    internal void Destroy()
    {
        if (_root == null) return;
        try { Object.Destroy(_root); }
        catch { }
        _root = null;
        _rootTransform = null;
        _fill = null;
        _nameText = null;
        _hpText = null;
    }

    /// <summary>
    /// Invariant culture throughout. On a Danish system the default formatter renders 1.8M as
    /// "1,8M", which reads as a thousands separator to most of the mod's audience and is what
    /// the first in-game screenshot showed. Numbers on a health bar are not prose; they should
    /// look the same everywhere.
    /// </summary>
    /// <summary>
    /// Both halves share a unit, chosen from the maximum.
    ///
    /// Formatting each number independently produced "393k / 393.2k", where the two sides use
    /// different precision and the boss looks damaged when it is untouched. The pair is one
    /// quantity read twice; it should be scaled once.
    /// </summary>
    /// <summary>
    /// Read every frame rather than captured at build time, so changing the setting takes
    /// effect on plates already on screen instead of only on the next boss.
    /// </summary>
    private float Scale => _isMajor ? Plugin.PlateScale : Plugin.MiniBossPlateScale;

    private static string FormatPair(float current, float max)
    {
        var inv = CultureInfo.InvariantCulture;
        current = Mathf.Max(0f, current);
        max = Mathf.Max(0f, max);

        float divisor = 1f;
        string suffix = "";
        string format = "0";

        if (max >= 1000000f)
        {
            divisor = 1000000f;
            suffix = "M";
            format = "0.0";
        }
        else if (max >= 10000f)
        {
            divisor = 1000f;
            suffix = "k";
            format = "0.0";
        }

        return (current / divisor).ToString(format, inv) + suffix +
               " / " +
               (max / divisor).ToString(format, inv) + suffix;
    }

    private static GameObject NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    /// <summary>
    /// Fills its parent, inset by <paramref name="padding"/>, and shrinks itself to fit.
    ///
    /// The first version pinned the text to the bar rect with wrapping off and overflow
    /// allowed, which meant "393k / 393.2k" simply spilled out past both ends of the bar. A
    /// health plate is a fixed box by nature: the text has to give, not the box. TMP auto-sizing
    /// does that for nothing, and the floor stops it shrinking into illegibility - if the text
    /// cannot fit at the minimum it will overflow, which is at least visible as a problem.
    /// </summary>
    private static TextMeshProUGUI AddText(
        Transform parent, string name, string text, TMP_FontAsset font, float size,
        Color color, TextAlignmentOptions align, float padding)
    {
        if (font == null) return null;

        GameObject go = NewChild(parent, name);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        ((TMP_Text)tmp).font = font;
        ((TMP_Text)tmp).text = text ?? "";
        ((TMP_Text)tmp).alignment = align;
        ((TMP_Text)tmp).enableWordWrapping = false;
        ((TMP_Text)tmp).overflowMode = TextOverflowModes.Overflow;
        ((TMP_Text)tmp).richText = false;
        ((TMP_Text)tmp).enableAutoSizing = true;
        ((TMP_Text)tmp).fontSizeMax = size;
        ((TMP_Text)tmp).fontSizeMin = size * 0.45f;
        ((TMP_Text)tmp).fontSize = size;
        ((Graphic)tmp).color = color;
        ((Graphic)tmp).raycastTarget = false;
        return tmp;
    }

    /// <summary>
    /// Borrows a font from whatever TMP text is already on screen rather than loading one.
    /// This is how the Evolution Helper mod does it, and it has the useful side effect that
    /// the plate is drawn in the game's own font instead of an Arial fallback.
    /// </summary>
    private static TMP_FontAsset GetFont()
    {
        if (_font != null) return _font;

        try
        {
            TextMeshProUGUI any = Object.FindObjectOfType<TextMeshProUGUI>();
            if ((Object)(object)any != (Object)null) _font = ((TMP_Text)any).font;
        }
        catch { }

        if (_font == null && !_warnedNoFont)
        {
            _warnedNoFont = true;
            Plugin.Log.LogWarning(
                "No TextMeshPro font found in the scene - plates will draw the bar only.");
        }

        return _font;
    }

    /// <summary>
    /// A 1x1 white sprite shared by every plate. Image renders without a sprite, but the
    /// behaviour depends on the UI default material being present, and an explicit sprite
    /// costs four bytes and removes the question.
    /// </summary>
    private static Sprite GetWhiteSprite()
    {
        if ((Object)(object)_whiteSprite != (Object)null) return _whiteSprite;

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        _whiteSprite = Sprite.Create(
            texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return _whiteSprite;
    }
}
