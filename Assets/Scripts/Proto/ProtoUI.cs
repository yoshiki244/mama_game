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

    // 角丸スプライト（9スライス・実行時生成）。ポップなUIの土台。
    static Sprite _rounded;
    public static Sprite RoundedSprite()
    {
        if (_rounded != null) return _rounded;
        const int N = 48; const float r = 16f, c = (N - 1) / 2f;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - c) - (N / 2f - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y - c) - (N / 2f - r), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f); // 角を滑らかに
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _rounded = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        return _rounded;
    }

    // パネルを角丸化（9スライス）
    public static Image Round(Image img)
    {
        if (img != null) { img.sprite = RoundedSprite(); img.type = Image.Type.Sliced; }
        return img;
    }

    // 立体タイル用ベベルスプライト（上辺ハイライト・下右辺シャドウ・縦グラデを白ベースで焼き込み。tint色がそのまま活きる）
    static Sprite _bevel;
    public static Sprite BevelSprite()
    {
        if (_bevel != null) return _bevel;
        const int N = 32; const float r = 5f; float c = (N - 1) / 2f;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 角丸のアルファ
                float ex = Mathf.Max(Mathf.Abs(x - c) - (N / 2f - r), 0f);
                float ey = Mathf.Max(Mathf.Abs(y - c) - (N / 2f - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(ex * ex + ey * ey) + 0.5f);

                // 縦グラデーション（上が明るい）※テクスチャは下が y=0
                float b = Mathf.Lerp(0.72f, 1.0f, y / (float)(N - 1));
                // 上辺のハイライト
                if (y >= N - 4) b = Mathf.Min(1f, b + 0.18f * (y - (N - 4)) / 3f + 0.08f);
                // 下辺・右辺のシャドウ（落ち影で浮き上がって見える）
                if (y <= 3) b *= 0.52f + 0.12f * y;
                if (x >= N - 4) b *= 0.78f;
                // 左辺はわずかに明るく
                if (x <= 2) b = Mathf.Min(1f, b + 0.05f);

                tex.SetPixel(x, y, new Color(b, b, b, a));
            }
        tex.Apply();
        _bevel = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(6, 6, 6, 6));
        return _bevel;
    }

    // Imageを立体タイル化
    public static Image Bevel(Image img)
    {
        if (img != null) { img.sprite = BevelSprite(); img.type = Image.Type.Sliced; }
        return img;
    }

    // 角丸パネル
    public static Image CreateRoundedPanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        => Round(CreatePanel(name, parent, pos, size, color));

    // 放射状グロー（中心が明るく外へ柔らかく消える光。レアの後光などに）
    static Sprite _radial;
    public static Sprite RadialGlowSprite()
    {
        if (_radial != null) return _radial;
        const int N = 64; float c = (N - 1) / 2f;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _radial = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _radial;
    }

    public static Image CreateGlow(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var img = CreatePanel(name, parent, pos, size, color);
        img.sprite = RadialGlowSprite();
        img.raycastTarget = false;
        return img;
    }

    // カード内面用の上品な縦グラデーション（上が明るい。tint色が活きる白ベース）
    static Sprite _vgrad;
    public static Sprite VGradSprite()
    {
        if (_vgrad != null) return _vgrad;
        const int H = 32;
        var tex = new Texture2D(2, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
        {
            float b = Mathf.Lerp(0.62f, 1.05f, y / (float)(H - 1));   // 下0.62→上1.05（上端がほんのり発光）
            b = Mathf.Min(b, 1f);
            for (int x = 0; x < 2; x++) tex.SetPixel(x, y, new Color(b, b, b, 1f));
        }
        tex.Apply();
        _vgrad = Sprite.Create(tex, new Rect(0, 0, 2, H), new Vector2(0.5f, 0.5f), 100f);
        return _vgrad;
    }

    public static Image VGrad(Image img)
    {
        if (img != null) { img.sprite = VGradSprite(); img.type = Image.Type.Simple; }
        return img;
    }

    // 走査光（シャイン）：カードの上を斜めの光が周期的にスッと走る
    public static void AddShine(Image container, Vector2 size, float alpha = 0.14f)
    {
        if (container == null) return;
        if (container.GetComponent<RectMask2D>() == null) container.gameObject.AddComponent<RectMask2D>();
        var bar = CreatePanel("Shine", container.transform, new Vector2(-size.x, 0), new Vector2(42f, size.y * 1.8f), new Color(1f, 1f, 1f, alpha));
        bar.raycastTarget = false;
        bar.transform.localRotation = Quaternion.Euler(0, 0, 18f);
        var s = container.gameObject.AddComponent<ShineSweep>();
        s.bar = (RectTransform)bar.transform;
        s.width = size.x;
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
        var img = Round(CreatePanel(name, parent, pos, size, bg)); // 角丸
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.12f, 1.05f, 1f);
        colors.pressedColor = new Color(0.78f, 0.75f, 0.70f, 1f);
        colors.disabledColor = new Color(0.42f, 0.42f, 0.45f, 0.65f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        var text = CreateText("Label", img.transform, label, fontSize, Vector2.zero, size);
        text.fontStyle = FontStyles.Bold;
        return btn;
    }

    // 金枠付きボタン（周囲を金色の枠で囲む）
    public static Button CreateGoldButton(string name, Transform parent, string label, float fontSize,
        Vector2 pos, Vector2 size, Color bg, System.Action onClick)
    {
        Round(CreatePanel(name + "Border", parent, pos, size + new Vector2(10f, 10f), new Color(0.85f, 0.72f, 0.4f, 0.95f))).raycastTarget = false;
        return CreateButton(name, parent, label, fontSize, pos, size, bg, onClick);
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
        var shadow = Round(CreatePanel(name + "Shadow", parent, pos + new Vector2(0, -6), size + new Vector2(14, 14), new Color(0, 0, 0, 0.30f)));
        shadow.raycastTarget = false;
        var frame = Round(CreatePanel(name + "Frame", parent, pos, size, border));
        frame.raycastTarget = false;
        var inner = Round(CreatePanel(name, frame.transform, Vector2.zero, size - new Vector2(8, 8), fill));
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

// レアカード用の脈動グロー（色A⇔色Bをゆっくり往復して光らせる）
public class RareGlow : MonoBehaviour
{
    public UnityEngine.UI.Graphic target;
    public Color colA = Color.white, colB = Color.yellow;
    public float speed = 4f;
    void Update()
    {
        if (target == null) return;
        float t = (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f;
        target.color = Color.Lerp(colA, colB, t);
    }
}

// 走査光の駆動（一定間隔で左→右へ光の帯を走らせる）
public class ShineSweep : MonoBehaviour
{
    public RectTransform bar;
    public float width = 240f, interval = 2.4f, dur = 0.8f;
    float _t;
    void Update()
    {
        if (bar == null) return;
        _t += Time.unscaledDeltaTime;
        float cycle = interval + dur;
        float k = _t % cycle;
        if (k < dur)
        {
            if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
            float p = k / dur;
            bar.anchoredPosition = new Vector2(Mathf.Lerp(-width / 2f - 50f, width / 2f + 50f, p), 0);
        }
        else if (bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
    }
}
