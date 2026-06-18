using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// UIをコードから生成するためのヘルパー集
public static class ProtoUI
{
    static TMP_FontAsset _font;
    static bool _fontLoaded;
    public static readonly Color Gold = new Color(0.92f, 0.82f, 0.55f);
    public static readonly Color Ink = new Color(0.025f, 0.027f, 0.04f, 0.88f);
    public static readonly Color Panel = new Color(0.055f, 0.065f, 0.085f, 0.90f);
    public static readonly Color PanelSoft = new Color(0.09f, 0.105f, 0.13f, 0.82f);
    public static readonly Color Border = new Color(0.72f, 0.62f, 0.38f, 0.78f);
    public static readonly Color Cyan = new Color(0.45f, 0.82f, 1f, 0.95f);

    public static TMP_FontAsset Font
    {
        get
        {
            if (!_fontLoaded)
            {
                _fontLoaded = true;

                // ① ゲーム用ドットフォント（DotGothic16）を最優先。実行時にTMPフォント化する
                var ttf = Resources.Load<UnityEngine.Font>("DotGothic16-Regular");
                if (ttf != null)
                {
                    _font = TMP_FontAsset.CreateFontAsset(ttf);
                }
                else
                {
                    // ② フォールバック: Noto Sans JP
                    _font = Resources.Load<TMP_FontAsset>("NotoSansJP-VariableFont_wght SDF");
                }

                if (_font == null)
                    Debug.LogWarning("日本語フォントが Assets/Resources に見つかりません。文字が□になります。");
            }
            return _font;
        }
    }

    public static Canvas CreateCanvas()
    {
        var go = new GameObject("ProtoCanvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    // 画面いっぱいに広がるコンテナ
    public static RectTransform CreateFullScreen(string name, Transform parent)
    {
        var rt = CreateRect(name, parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static Image CreatePanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var rt = CreateRect(name, parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize,
        Vector2 pos, Vector2 size, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = CreateRect(name, parent);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) t.font = Font;
        t.text = text;
        t.fontSize = fontSize;
        t.color = color ?? Color.white;
        t.alignment = align;

        // 視認性のための細い黒縁取り（どんな背景でも文字が沈まない）
        t.outlineWidth = 0.18f;
        t.outlineColor = new Color32(12, 10, 24, 235);
        return t;
    }

    // 見出し用の高級感スタイル（太字＋字間広め＋指定色）
    public static void StyleTitle(TextMeshProUGUI t, Color color, float spacing = 5f)
    {
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = spacing;
        t.color = color;
    }

    public static Button CreateButton(string name, Transform parent, string label, float fontSize,
        Vector2 pos, Vector2 size, Color bg, System.Action onClick)
    {
        var img = CreatePanel(name, parent, pos, size, bg);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.12f, 1.05f, 1f);
        colors.pressedColor = new Color(0.78f, 0.75f, 0.70f, 1f);
        colors.disabledColor = new Color(0.42f, 0.42f, 0.45f, 0.65f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        AddPanelTrim(img, size, Color.Lerp(bg, Gold, 0.38f), new Color(1f, 1f, 1f, 0.08f));
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        var text = CreateText("Label", img.transform, label, fontSize, Vector2.zero, size);
        text.fontStyle = FontStyles.Bold;
        return btn;
    }

    public static void AddPanelTrim(Image panel, Vector2 size, Color border, Color shine)
    {
        var top = CreatePanel("TopTrim", panel.transform, new Vector2(0, size.y * 0.5f - 2f), new Vector2(size.x, 3f), shine);
        top.raycastTarget = false;
        var bottom = CreatePanel("BottomTrim", panel.transform, new Vector2(0, -size.y * 0.5f + 2f), new Vector2(size.x, 2f), border);
        bottom.raycastTarget = false;
    }

    public static Image CreateFramedPanel(string name, Transform parent, Vector2 pos, Vector2 size, Color fill, Color border)
    {
        var shadow = CreatePanel(name + "Shadow", parent, pos + new Vector2(0, -4), size + new Vector2(8, 8), new Color(0, 0, 0, 0.28f));
        shadow.raycastTarget = false;
        var frame = CreatePanel(name + "Frame", parent, pos, size, border);
        frame.raycastTarget = false;
        var inner = CreatePanel(name, frame.transform, Vector2.zero, size - new Vector2(8, 8), fill);
        inner.raycastTarget = false;
        return inner;
    }

    // 数字入力欄（点滅チャレンジの手入力用）
    public static TMP_InputField CreateInputField(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize)
    {
        var bg = CreatePanel(name, parent, pos, size, new Color(0.1f, 0.09f, 0.18f));
        var input = bg.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;

        var areaRt = CreateRect("Text Area", bg.transform);
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(10, 6);
        areaRt.offsetMax = new Vector2(-10, -7);
        areaRt.gameObject.AddComponent<RectMask2D>();

        var text = CreateText("Text", areaRt, "", fontSize, Vector2.zero, Vector2.zero);
        var textRt = (RectTransform)text.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        input.textViewport = areaRt;
        input.textComponent = text;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        return input;
    }

    // ゲージ（HPバー・タイマー用）。fillの幅を割合で操作する
    public static Image CreateGauge(string name, Transform parent, Vector2 pos, Vector2 size,
        Color bgColor, Color fillColor, out Image fill)
    {
        var bg = CreatePanel(name, parent, pos, size, bgColor);
        var fillRt = CreateRect("Fill", bg.transform);
        fillRt.anchorMin = new Vector2(0, 0);
        fillRt.anchorMax = new Vector2(0, 1);
        fillRt.pivot = new Vector2(0, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = new Vector2(size.x, 0);
        fill = fillRt.gameObject.AddComponent<Image>();
        fill.color = fillColor;
        AddPanelTrim(bg, size, Color.Lerp(bgColor, Border, 0.5f), new Color(1f, 1f, 1f, 0.06f));
        return bg;
    }

    public static void SetGauge(Image fill, float ratio, float fullWidth)
    {
        var rt = (RectTransform)fill.transform;
        rt.sizeDelta = new Vector2(fullWidth * Mathf.Clamp01(ratio), 0);
    }
}

// 左右クリック＋ドラッグを受け取るためのハンドラ
public class CellClickHandler : MonoBehaviour, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public System.Action<PointerEventData> onClick;
    public System.Action<PointerEventData> onBeginDrag;
    public System.Action<PointerEventData> onDrag;
    public System.Action<PointerEventData> onEndDrag;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.dragging) return; // ドラッグ後のリリースはクリック扱いしない
        onClick?.Invoke(e);
    }

    public void OnBeginDrag(PointerEventData e) => onBeginDrag?.Invoke(e);
    public void OnDrag(PointerEventData e) => onDrag?.Invoke(e);
    public void OnEndDrag(PointerEventData e) => onEndDrag?.Invoke(e);
}
