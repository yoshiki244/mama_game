using UnityEngine;
using System.Collections.Generic;

// 仮グラフィック用のドット絵をコードから生成する（プリプロ用）
// 本番では Aseprite で描いたPNGに差し替える想定
public static class ProtoPixelArt
{
    // 文字マップからSpriteを作る。'.'は透明
    public static Sprite FromMap(string[] rows, Dictionary<char, Color> palette)
    {
        int h = rows.Length;
        int w = 0;
        foreach (var r in rows) w = Mathf.Max(w, r.Length);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point; // ドット絵をくっきり表示

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char c = x < rows[y].Length ? rows[y][x] : '.';
                Color col;
                if (c == '.') col = Color.clear;
                else if (!palette.TryGetValue(c, out col)) col = Color.magenta; // 未定義文字は目立つ色で警告
                tex.SetPixel(x, h - 1 - y, col); // 行は上から書くので上下反転
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    // 主人公の実画像（Assets/Resources/mama_character.png）を読み込む。一度読んだらキャッシュ
    static Sprite _mamaPhoto;
    static bool _mamaPhotoLoaded;
    public static Sprite MamaPhoto()
    {
        if (!_mamaPhotoLoaded)
        {
            _mamaPhoto = Resources.Load<Sprite>("mama_character");
            _mamaPhotoLoaded = true;
            if (_mamaPhoto == null)
                Debug.LogWarning("mama_character.png が Assets/Resources に見つかりません（Sprite設定を確認）");
        }
        return _mamaPhoto != null ? _mamaPhoto : Mama(); // 無ければ従来のドット絵にフォールバック
    }

    // バトル背景画像（Assets/Resources/battle_bg.png）。無ければコード生成の自然背景にフォールバック
    static Sprite _battleBg;
    static bool _battleBgLoaded;
    public static Sprite BattleBackground()
    {
        if (!_battleBgLoaded)
        {
            _battleBg = Resources.Load<Sprite>("battle_bg");
            _battleBgLoaded = true;
            if (_battleBg == null)
                Debug.LogWarning("battle_bg.png が Assets/Resources に見つかりません（Sprite設定を確認）");
        }
        return _battleBg != null ? _battleBg : NatureBackground();
    }

    // 敵・オブジェクト用の画像（Assets/Resources/<name>.png）。無ければnull（＝コード生成にフォールバック）
    static readonly Dictionary<string, Sprite> _enemyPhotoCache = new Dictionary<string, Sprite>();
    static bool TryPhoto(string name, out Sprite sp)
    {
        if (!_enemyPhotoCache.TryGetValue(name, out sp))
        {
            sp = Resources.Load<Sprite>(name);
            _enemyPhotoCache[name] = sp;
        }
        return sp != null;
    }

    // マップの「樹」用の木画像（Assets/Resources/tree.png）。無ければnull
    public static Sprite TreePhoto() { TryPhoto("tree", out var sp); return sp; }

    // マップの「？」イベント用の画像（Assets/Resources/event_block.png）。無ければnull
    public static Sprite EventPhoto() { TryPhoto("event_block", out var sp); return sp; }

    // 戦闘UI左上の顔アイコン（Assets/Resources/front_mama.png）。無ければ立ち絵
    public static Sprite FrontMama() { return TryPhoto("front_mama", out var sp) ? sp : MamaPhoto(); }

    // 被弾時の差し替え画像（Assets/Resources/damage_mama.png）。無ければ通常立ち絵
    public static Sprite DamageMama() { return TryPhoto("damage_mama", out var sp) ? sp : MamaPhoto(); }

    // HP0で倒れた画像（Assets/Resources/mama_down.png）。無ければ通常立ち絵
    public static Sprite DownMama() { return TryPhoto("mama_down", out var sp) ? sp : MamaPhoto(); }

    // ダンジョンマップ用の主人公画像（Assets/Resources/mama_map.png）。無ければドット絵にフォールバック
    static Sprite _mamaMap;
    static bool _mamaMapLoaded;
    public static Sprite MamaMapPhoto()
    {
        if (!_mamaMapLoaded)
        {
            _mamaMap = Resources.Load<Sprite>("mama_map");
            _mamaMapLoaded = true;
            if (_mamaMap == null)
                Debug.LogWarning("mama_map.png が Assets/Resources に見つかりません（Sprite設定を確認）");
        }
        return _mamaMap != null ? _mamaMap : Mama();
    }

    // 主人公画像の「髪の銀色部分だけ」を指定色に塗り替えた版を作る（仲間の色違い用）
    static readonly Dictionary<int, Sprite> _tintCache = new Dictionary<int, Sprite>();
    public static Sprite MamaPhotoTinted(Color hairColor)
    {
        var baseSprite = MamaPhoto();
        if (baseSprite == null) return Mama();

        // 同じ色なら使い回し
        int key = ((int)(hairColor.r * 255) << 16) | ((int)(hairColor.g * 255) << 8) | (int)(hairColor.b * 255);
        if (_tintCache.TryGetValue(key, out var cached)) return cached;

        var src = baseSprite.texture;
        Color[] pixels;
        try { pixels = src.GetPixels(); }
        catch { return baseSprite; } // 読み取り不可ならそのまま

        Color.RGBToHSV(hairColor, out float targetH, out float targetS, out _);

        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            if (c.a < 0.1f) continue;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // 銀髪＝低彩度かつ中間〜やや明るい明度。白いドレス(明度高)や肌・線(暗/有彩)は除外
            if (s < 0.22f && v > 0.45f && v < 0.90f)
            {
                var nc = Color.HSVToRGB(targetH, Mathf.Max(0.45f, targetS), v); // 明暗(立体感)は維持
                nc.a = c.a;
                pixels[i] = nc;
            }
        }

        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        tex.filterMode = src.filterMode;
        tex.SetPixels(pixels);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        _tintCache[key] = sprite;
        return sprite;
    }

    // バトル用MAMA（30x38）: 銀髪ロング・黒ドレス・シースルー裾・右腕を突き出す構え
    public static Sprite Mama()
        => Mama(new Color(0.87f, 0.88f, 0.95f), new Color(0.66f, 0.68f, 0.80f), new Color(0.98f, 0.99f, 1f));

    // 髪色を指定できる版（仲間キャラの色違い生成用）
    public static Sprite Mama(Color hair, Color hairShadow, Color hairLight)
    {
        var rows = new string[]
        {
            "...........HHHHHHHH...........",
            ".........HHHHHHHHHHHH.........",
            "........HHHHHHHHHHHHHH........",
            "........HHHLHHHHHHLHHH........",
            ".......hHHHHHHHHHHHHHHh.......",
            ".......hHHHSHHHHSHHHHHh.......",
            ".......hHHSSSHHSSSHHHHh.......",
            ".......hHSSSSSSSSSSSHHh.......",
            ".......hHSkkSSSSkkSSHHh.......",
            ".......hHSeESSSSeESSHHh.......",
            ".......hHSCSSSSSSCSSHHh.......",
            ".......hHSSSSMMSSSSSHHh.......",
            ".......hHHSSSSSSSSSHHHh.......",
            "......hHHHSSSSSSSHHHHh........",
            "......hHHHHSSSSSSHHHHh........",
            ".....hHHHHHWWWWWHHHHHHh.......",
            ".....hHHSSWWWWWWWHHHHhSSSS....",
            ".....hHHSSWWWWWWWWHhSSSSSSS...",
            ".....hHHSSWWwWWwWWHHHHh.......",
            ".....hHHSSWWWWWHHHHHHHh.......",
            ".....hHHSSwWWWwHHHHHHHh.......",
            "......hHSSSWWWWWWHHHHh........",
            "..........WWWWWWWW............",
            "..........VWWWWWWV............",
            ".........VVWWWWWWVV...........",
            ".........VVwWWWWwVV...........",
            "........VVVWWWWWVVV...........",
            "........VVVVVVVVVVV...........",
            "........VVVVVVVVVVVV..........",
            "........VVVVVVVVVVVV..........",
            "........SSS......SSS..........",
            "........SSS......SSS..........",
            ".........SS.......SS..........",
            ".........SS.......SS..........",
            ".........SS.......SS..........",
            "........BBB......BBB..........",
            "........bBB......bBB..........",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'H', hair },                                  // 髪（ベース）
            { 'h', hairShadow },                            // 髪の陰
            { 'L', hairLight },                             // 髪のハイライト
            { 'S', new Color(1f, 0.88f, 0.78f) },           // 肌
            { 'E', new Color(0.45f, 0.68f, 1f) },           // 瞳（青・明）
            { 'e', new Color(0.18f, 0.32f, 0.65f) },        // 瞳（青・暗＝深み）
            { 'k', new Color(0.32f, 0.28f, 0.36f) },        // まつ毛（柔らかい目元）
            { 'C', new Color(1f, 0.72f, 0.72f) },           // 頬の赤らみ
            { 'M', new Color(0.9f, 0.55f, 0.55f) },         // 口（微笑み）
            { 'W', new Color(0.13f, 0.12f, 0.18f) },        // ドレス（黒）
            { 'w', new Color(0.26f, 0.24f, 0.34f) },        // ドレスの皺・光沢
            { 'V', new Color(0.2f, 0.18f, 0.3f, 0.7f) },    // 裾シースルー（黒レース）
            { 'v', new Color(0.25f, 0.22f, 0.36f, 0.4f) },  // 裾シースルー（より透ける縁）
            { 'T', new Color(0.8f, 0.65f, 0.6f, 0.95f) },   // 透けて見える脚
            { 'B', new Color(0.72f, 0.52f, 0.34f) },        // 靴（茶）
            { 'b', new Color(0.5f, 0.35f, 0.22f) },         // 靴の陰
        };
        return FromMap(rows, palette);
    }

    // 自然あふれる背景（空・太陽・雲・丘・木・草原・花）をピクセルアートで生成
    public static Sprite NatureBackground()
    {
        int w = 160, h = 90;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var rng = new System.Random(7); // 毎回同じ風景になるよう固定シード

        int horizon = 34; // 地平線（テクスチャ座標は下が0）

        // 空と草原のベース
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c;
                if (y >= horizon)
                {
                    float t = (y - horizon) / (float)(h - horizon);
                    c = Color.Lerp(new Color(0.80f, 0.92f, 0.97f), new Color(0.45f, 0.72f, 0.95f), t);
                }
                else
                {
                    float t = y / (float)horizon;
                    c = Color.Lerp(new Color(0.20f, 0.42f, 0.24f), new Color(0.36f, 0.66f, 0.38f), t);
                    if (rng.NextDouble() < 0.08) c *= 0.88f; // 草の質感（まだら）
                }
                tex.SetPixel(x, y, c);
            }
        }

        // 遠景の丘（うねる稜線）
        for (int x = 0; x < w; x++)
        {
            int hillTop = horizon + 4 + (int)(5f * Mathf.Sin(x * 0.07f) + 3f * Mathf.Sin(x * 0.023f + 2f));
            for (int y = horizon; y < hillTop; y++)
                SafeSet(tex, x, y, new Color(0.30f, 0.55f, 0.34f));
        }

        // 太陽
        DrawCircle(tex, 132, 78, 7, new Color(1f, 0.95f, 0.7f));
        DrawCircle(tex, 132, 78, 5, new Color(1f, 0.99f, 0.85f));

        // 雲
        DrawCircle(tex, 28, 74, 5, new Color(1f, 1f, 1f, 0.9f));
        DrawCircle(tex, 36, 76, 6, new Color(1f, 1f, 1f, 0.9f));
        DrawCircle(tex, 44, 74, 5, new Color(1f, 1f, 1f, 0.9f));
        DrawCircle(tex, 88, 82, 4, new Color(1f, 1f, 1f, 0.85f));
        DrawCircle(tex, 95, 83, 5, new Color(1f, 1f, 1f, 0.85f));

        // 木（幹＋こんもりした葉）
        int[] treeXs = { 12, 40, 120, 148 };
        foreach (var tx in treeXs)
        {
            for (int y = horizon - 2; y < horizon + 9; y++)
                for (int x = tx - 1; x <= tx + 1; x++)
                    SafeSet(tex, x, y, new Color(0.42f, 0.30f, 0.20f));
            DrawCircle(tex, tx, horizon + 14, 9, new Color(0.18f, 0.46f, 0.22f));
            DrawCircle(tex, tx - 5, horizon + 10, 6, new Color(0.24f, 0.55f, 0.28f));
            DrawCircle(tex, tx + 5, horizon + 10, 6, new Color(0.21f, 0.50f, 0.25f));
        }

        // 草原に花を散らす
        for (int i = 0; i < 50; i++)
        {
            int fx = rng.Next(0, w);
            int fy = rng.Next(2, horizon - 4);
            Color fc = rng.Next(3) switch
            {
                0 => new Color(1f, 0.75f, 0.85f),  // ピンク
                1 => new Color(1f, 0.98f, 0.6f),   // 黄色
                _ => new Color(1f, 1f, 1f),        // 白
            };
            tex.SetPixel(fx, fy, fc);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    public static Sprite DungeonMapBackground()
    {
        int w = 160, h = 90;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var rng = new System.Random(31);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float center = Mathf.Abs(x - w * 0.5f) / (w * 0.5f);
                float depth = y / (float)h;
                var c = Color.Lerp(new Color(0.015f, 0.018f, 0.035f), new Color(0.025f, 0.055f, 0.09f), depth);
                c *= Mathf.Lerp(1.08f, 0.42f, center);
                if (rng.NextDouble() < 0.12) c *= rng.NextDouble() < 0.5 ? 0.72f : 1.18f;
                tex.SetPixel(x, y, c);
            }
        }

        // stone floor perspective
        for (int y = 0; y < 28; y++)
        {
            int yy = y;
            float t = y / 28f;
            var floor = Color.Lerp(new Color(0.03f, 0.04f, 0.055f), new Color(0.15f, 0.12f, 0.085f), t);
            for (int x = 0; x < w; x++)
            {
                float lane = Mathf.Abs(x - w * 0.5f) / (w * 0.5f);
                var c = floor * Mathf.Lerp(1.15f, 0.5f, lane);
                if ((x + y * 3) % 17 == 0) c *= 1.35f;
                tex.SetPixel(x, yy, c);
            }
        }
        for (int y = 4; y < 28; y += 5)
            for (int x = 14; x < w - 14; x++)
                SafeSet(tex, x, y, new Color(0.42f, 0.30f, 0.13f, 0.35f));
        for (int x = 25; x < w - 20; x += 15)
            for (int y = 0; y < 28; y++)
                SafeSet(tex, x + (28 - y) / 5, y, new Color(0.04f, 0.035f, 0.03f, 0.35f));

        // arches and side pillars
        for (int i = 0; i < 6; i++)
        {
            int left = 8 + i * 18;
            int right = w - 9 - i * 18;
            DrawPillar(tex, left, 25, 58, rng);
            DrawPillar(tex, right, 25, 58, rng);
            DrawArch(tex, left + 6, 66, 10);
            DrawArch(tex, right - 6, 66, 10);
        }

        // distant blue flames
        for (int i = 0; i < 12; i++)
        {
            int x = 16 + i * 12 + rng.Next(-2, 3);
            int y = 42 + rng.Next(0, 22);
            DrawCircle(tex, x, y, 2, new Color(0.12f, 0.28f, 0.78f, 0.42f));
            SafeSet(tex, x, y, new Color(0.75f, 0.92f, 1f, 0.8f));
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    static void DrawPillar(Texture2D tex, int cx, int y0, int height, System.Random rng)
    {
        for (int y = y0; y < y0 + height && y < tex.height; y++)
        {
            for (int x = cx - 3; x <= cx + 3; x++)
            {
                var c = new Color(0.07f, 0.065f, 0.08f);
                if (x == cx - 3 || x == cx + 3) c = new Color(0.02f, 0.018f, 0.024f);
                if (rng.NextDouble() < 0.18) c *= 1.55f;
                SafeSet(tex, x, y, c);
            }
        }
        for (int x = cx - 5; x <= cx + 5; x++)
        {
            SafeSet(tex, x, y0, new Color(0.36f, 0.24f, 0.09f, 0.42f));
            SafeSet(tex, x, y0 + height - 1, new Color(0.36f, 0.24f, 0.09f, 0.42f));
        }
    }

    static void DrawArch(Texture2D tex, int cx, int cy, int r)
    {
        for (int a = 0; a <= 180; a += 3)
        {
            float rad = a * Mathf.Deg2Rad;
            int x = cx + Mathf.RoundToInt(Mathf.Cos(rad) * r);
            int y = cy + Mathf.RoundToInt(Mathf.Sin(rad) * r);
            SafeSet(tex, x, y, new Color(0.30f, 0.20f, 0.08f, 0.35f));
            SafeSet(tex, x, y - 1, new Color(0.02f, 0.018f, 0.025f, 0.55f));
        }
    }

    static void DrawCircle(Texture2D tex, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                    SafeSet(tex, cx + dx, cy + dy, c);
    }

    static void SafeSet(Texture2D tex, int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) return;
        if (c.a < 1f)
        {
            var bg = tex.GetPixel(x, y);
            c = Color.Lerp(bg, new Color(c.r, c.g, c.b, 1f), c.a);
        }
        tex.SetPixel(x, y, c);
    }

    // ボスドラゴン（バトル用・横向き）: 緑の竜が二本足で立ち、片翼を立てた姿（左を向く）
    public static Sprite Dragon()
    {
        if (TryPhoto("dragon", out var photo)) return photo;
        var rows = new string[]
        {
            ".....HH...................dddd............",
            "....dGGd...............ddWWWWWdd..........",
            "...dGGGGd............ddWWWWWWWWWdd........",
            "..dGGEGGd..........dmWWWWWWWWWWWWdd.......",
            "..dGGGGGd........dmWWWWWWWWWWmWWWWWd......",
            "..dGGGGGGd......dmWWWWWWWmWWWWWWmWWd......",
            "...dGGGGGd....dmGWWWWWmWWWWWWmWWWdd.......",
            "...dGGGGGGd..dGGGWWWmWWWWmWWWdd...........",
            "....dGGGGGd.dGGGGGmWWmWWWdd...............",
            ".....dGGGGddGGGGGGGGGGGd..................",
            "......dGGGGGGGGGGGGGGGGGd.................",
            "......dGGGGGGGGGGGGGGGGGGd................",
            ".....dGGGGLLLLLLLGGGGGGGGGd...............",
            ".....dGGGLLLLLLLLLGGGGGGGGGd..............",
            ".....dGGGLLLLLLLLLGGGGGGGGGGdd............",
            ".....dGGGGLLLLLLLGGGGGGGGGGGGGdd..........",
            "......dGGGGLLLLLGGGGGGGdGGGGGGGGdd........",
            "......dGGGGGGGGGGGGGGd...ddGGGGGGGd.......",
            ".......dGGGGGGGGGGGGd......ddGGGGGd.......",
            ".......dGGGGGGGGGGGd.........dGGGGd.......",
            "........dGGGdGGGGGd...........ddGd........",
            "........dGGd.dGGGGd.............d.........",
            ".......dGGd..dGGGGd.......................",
            "......dGGd...dGGGd........................",
            ".....dGGGd..dGGGGd........................",
            ".....dddd...dddddd........................",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'G', new Color(0.42f, 0.62f, 0.40f) },  // 体（緑）
            { 'd', new Color(0.16f, 0.30f, 0.18f) },  // 輪郭・陰（濃い緑）
            { 'L', new Color(0.64f, 0.78f, 0.50f) },  // 腹（明るい黄緑）
            { 'W', new Color(0.86f, 0.88f, 0.62f) },  // 翼膜（淡い黄緑）
            { 'm', new Color(0.30f, 0.50f, 0.30f) },  // 翼の骨・縁
            { 'H', new Color(0.95f, 0.82f, 0.42f) },  // 角（黄）
            { 'E', new Color(0.10f, 0.20f, 0.12f) },  // 目
        };
        return FromMap(rows, palette);
    }

    // ボスドラゴン（マップ用・正面向き）: こちらを向いて翼を左右に広げた姿
    public static Sprite DragonFront()
    {
        if (TryPhoto("dragon_mini", out var photo)) return photo;
        var rows = new string[]
        {
            ".....H.........H.....",
            ".....dd.......dd.....",
            "....dGGdddddddGGd....",
            "...dGGGEGGGGEGGGd....",
            "...dGGGGGGGGGGGGd....",
            "..WdGGGGGGGGGGGGdW...",
            ".WWdGGGGGGGGGGGGdWW..",
            "WWWdGGGGLLLLGGGGdWWW.",
            "WWWdGGGLLLLLLGGGdWWW.",
            ".WWdGGGLLLLLLGGGGdW..",
            "..dGGGGLLLLLLGGGGd...",
            "..dGGGGGGGGGGGGGGd...",
            "...dGGGGGGGGGGGGd....",
            "...dGGdGGGGGGdGGd....",
            "...dGd..dGGd..dGd....",
            "..dGGd..dGGd..dGGd...",
            "..dddd..dddd..dddd...",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'G', new Color(0.42f, 0.62f, 0.40f) },
            { 'd', new Color(0.16f, 0.30f, 0.18f) },
            { 'L', new Color(0.64f, 0.78f, 0.50f) },
            { 'W', new Color(0.86f, 0.88f, 0.62f) },
            { 'H', new Color(0.95f, 0.82f, 0.42f) },
            { 'E', new Color(0.10f, 0.20f, 0.12f) },
        };
        return FromMap(rows, palette);
    }

    // 中ボスの鬼: 赤い肌・黒い荒髪・金の角・ニカッと笑う白い歯の大口・豹柄パンツ・黒い金棒
    public static Sprite Oni()
    {
        var rows = new string[]
        {
            "......KKKKKKKKKK...........",
            "....KKKKKKKKKKKKKK.........",
            "...KKYKKKKKKKKYKKK.........",
            "...KYYKKKKKKKKYYKK.........",
            "..KKKKKKKKKKKKKKKK.........",
            "..KKRRRRRRRRRRRRKK.........",
            ".KKRRKKKRRRRKKKRRKK........",
            ".KKRWWPRRRRRRPWWRKK........",
            "..KRRRRRRrrRRRRRRK.........",
            "..KRRWWWWWWWWWWRRK.........",
            "..KRWWWWWWWWWWWWRK.........",
            "..KRWWWWWWWWWWWWRK.........",
            "..KRRWWWWWWWWWWRRK.........",
            "...RRRrrrrrrrrRRR..........",
            "...RRRRRRRRRRRRRR.....cc...",
            "..RRRRRRRRRRRRRRRR...cNcc..",
            ".RRRRRRRRRRRRRRRRRR..cccc..",
            ".RRrRRRRRRRRRRRRrRR..cNcc..",
            ".RRrRRRRRRRRRRRRrRRR.cccc..",
            ".RRr.RRRRRRRRRR.rRRRRcNcc..",
            ".RRr.RRRRRRRRRR...RRRcccc..",
            ".....RRRRRRRRRR......ccc...",
            "....YYYYYYYYYYYY......cc...",
            "....YBYYBYYBYYBY......cc...",
            "....YYBYYBYYBYYY......cc...",
            "....YYYYYYYYYYYY...........",
            "....RRRR....RRRR...........",
            "....RRRR....RRRR...........",
            "....RRRR....RRRR...........",
            "...RRRRR....RRRRR..........",
            "...rRRRR....rRRRR..........",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'K', new Color(0.13f, 0.11f, 0.12f) },  // 荒れた黒髪・眉
            { 'Y', new Color(0.95f, 0.78f, 0.20f) },  // 角・豹柄パンツの地（金/黄）
            { 'B', new Color(0.14f, 0.11f, 0.09f) },  // 豹柄の黒斑点
            { 'R', new Color(0.85f, 0.22f, 0.15f) },  // 赤い肌（鮮烈な赤）
            { 'r', new Color(0.58f, 0.13f, 0.09f) },  // 肌の陰
            { 'W', new Color(0.97f, 0.96f, 0.92f) },  // ニカッと並ぶ白い歯・白目
            { 'P', new Color(0.10f, 0.08f, 0.08f) },  // 鋭い瞳
            { 'c', new Color(0.16f, 0.14f, 0.14f) },  // 黒い金棒
            { 'N', new Color(0.42f, 0.40f, 0.38f) },  // 金棒のトゲ（鋲）
        };
        return FromMap(rows, palette);
    }

    // マップ歩行用のMAMAミニスプライト（14x18）
    // dir: 0=正面（下向き） 1=背面（上向き） 2=左向き ※右向きは左をX反転して使う
    // frame: 0=足をそろえる 1=足を開く（交互に切り替えて歩行アニメに）
    public static Sprite MapMama(int dir, int frame)
        => MapMama(dir, frame, new Color(0.87f, 0.88f, 0.95f), new Color(0.66f, 0.68f, 0.80f));

    // 髪色を指定できる版（仲間キャラの色違い生成用）
    public static Sprite MapMama(int dir, int frame, Color hair, Color hairShadow)
    {
        string[] body;
        string[] legs;

        if (dir == 0) // 正面: 顔→首→襟付き黒ドレス→シースルー裾 の段差をはっきり
        {
            body = new[]
            {
                "......HHHHHH......",
                "....HHHHHHHHHH....",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HHSSSSSSSSHH...",
                "...HHSEESSEESHH...",
                "...HHSSSSSSSSHH...",
                "...HHSCSSSSCSHH...",
                "....HHSSSSSSHH....",
                ".......SSSS.......",
                "....HHWWWWWWHH....",
                "...HHWWWWWWWWHH...",
                "...HHWWWwwWWWHH...",
                "...hHWWWWWWWWHh...",
                "...hHWWWWWWWWHh...",
                "....hVWWWWWWVh....",
                "....VVWWWWWWVV....",
                "...VVVVVVVVVVVV...",
            };
        }
        else if (dir == 1) // 背面: 背中いっぱいの銀髪が裾近くまで流れる
        {
            body = new[]
            {
                "......HHHHHH......",
                "....HHHHHHHHHH....",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "....HHHHHHHHHH....",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...hHHHHHHHHHHh...",
                "...hHWHHHHHHWHh...",
                "...hHWHHHHHHWHh...",
                "....hVWHHHHWVh....",
                "....VVWWHHWWVV....",
                "...VVVVVVVVVVVV...",
            };
        }
        else // 左向き: 横顔＋首＋髪は後ろへ
        {
            body = new[]
            {
                "......HHHHHH......",
                "....HHHHHHHHHH....",
                "...HHHHHHHHHHHH...",
                "...HHHHHHHHHHHH...",
                "...HSSSSSHHHHHH...",
                "...HSEESSHHHHHH...",
                "...HSSSSSHHHHHH...",
                "...HSCSSSHHHHHH...",
                "....HSSSSHHHHH....",
                ".......SSS.HHH....",
                "....HWWWWWWHHH....",
                "...HHWWWWWWHHHH...",
                "...HHWWwwWWHHHh...",
                "...hHWWWWWWHHh....",
                "...hHWWWWWWHh.....",
                "....hVWWWWVh......",
                "....VVWWWWVV......",
                "...VVVVVVVVVV.....",
            };
        }

        if (dir == 2) // 横向きの足
        {
            legs = frame == 0
                ? new[] { ".......SSS........", ".......SSS........", ".......BBB........", ".................." }
                : new[] { "......SS..S.......", "......SS..S.......", "......BB..B.......", ".................." };
        }
        else // 正面・背面の足
        {
            legs = frame == 0
                ? new[] { "......SS..SS......", "......SS..SS......", "......BB..BB......", ".................." }
                : new[] { ".....SS....SS.....", ".....SS....SS.....", ".....BB....BB.....", ".................." };
        }

        var rows = new string[body.Length + legs.Length];
        body.CopyTo(rows, 0);
        legs.CopyTo(rows, body.Length);

        var palette = new Dictionary<char, Color>
        {
            { 'H', hair },                                  // 髪
            { 'h', hairShadow },                            // 髪の陰
            { 'S', new Color(1f, 0.88f, 0.78f) },           // 肌
            { 'E', new Color(0.35f, 0.6f, 0.95f) },         // 目
            { 'C', new Color(1f, 0.72f, 0.72f) },           // 頬の赤らみ
            { 'W', new Color(0.13f, 0.12f, 0.18f) },        // ドレス（黒）
            { 'w', new Color(0.26f, 0.24f, 0.34f) },        // ドレスの光沢
            { 'V', new Color(0.2f, 0.18f, 0.3f, 0.7f) },    // 裾シースルー（黒レース）
            { 'B', new Color(0.72f, 0.52f, 0.34f) },        // 靴
        };
        return FromMap(rows, palette);
    }

    // 見下ろし型フィールド（ポケモン風）: 市松模様の地面＋木/岩（上から見た図）
    // tree[tx,ty] = true のタイルに木（テーマ2では岩）を描く
    // theme: 0=草原 1=風雨の森（暗い緑） 2=嵐の山頂（岩場）
    // path:  trueのタイルに石の階段を描く（山頂の登り道用）
    public static Sprite TopDownField(int tilesX, int tilesY, bool[,] tree, int seed, int theme = 0, bool[,] path = null)
    {
        const int px = 8; // 1タイル=8ピクセル
        int w = tilesX * px, h = tilesY * px;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var rng = new System.Random(seed);

        // テーマごとの配色
        Color baseA, baseB, treeDark, treeMid, treeLight;
        bool flowers;
        switch (theme)
        {
            case 1: // 風雨の森: 暗く湿った緑
                baseA = new Color(0.28f, 0.43f, 0.27f); baseB = new Color(0.25f, 0.39f, 0.24f);
                treeDark = new Color(0.08f, 0.26f, 0.12f); treeMid = new Color(0.12f, 0.33f, 0.16f); treeLight = new Color(0.18f, 0.40f, 0.22f);
                flowers = false;
                break;
            case 2: // 嵐の山頂: 岩場（木の代わりに岩）
                baseA = new Color(0.44f, 0.44f, 0.49f); baseB = new Color(0.40f, 0.40f, 0.45f);
                treeDark = new Color(0.26f, 0.26f, 0.30f); treeMid = new Color(0.50f, 0.50f, 0.55f); treeLight = new Color(0.62f, 0.62f, 0.68f);
                flowers = false;
                break;
            default: // 草原
                baseA = new Color(0.46f, 0.73f, 0.42f); baseB = new Color(0.42f, 0.69f, 0.38f);
                treeDark = new Color(0.16f, 0.42f, 0.20f); treeMid = new Color(0.22f, 0.52f, 0.26f); treeLight = new Color(0.34f, 0.64f, 0.36f);
                flowers = true;
                break;
        }

        // 地面タイル（2色の市松模様＋まだらノイズ）
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                var baseCol = ((tx + ty) % 2 == 0) ? baseA : baseB;
                for (int y = 0; y < px; y++)
                {
                    for (int x = 0; x < px; x++)
                    {
                        var c = baseCol;
                        double r = rng.NextDouble();
                        if (r < 0.06) c *= 0.92f;
                        else if (r > 0.97) c *= 1.06f;
                        tex.SetPixel(tx * px + x, h - 1 - (ty * px + y), c);
                    }
                }
                // 草の刻み（ちょんちょんと生えてる感じ）
                if (rng.NextDouble() < 0.5)
                    tex.SetPixel(tx * px + rng.Next(1, 7), h - 1 - (ty * px + rng.Next(4, 7)), baseCol * 0.82f);
            }
        }

        // 石の階段（横線で段差を表現）
        if (path != null)
        {
            var stone = new Color(0.66f, 0.65f, 0.70f);
            var stoneEdge = new Color(0.45f, 0.44f, 0.50f);
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    if (!path[tx, ty]) continue;
                    for (int y = 0; y < px; y++)
                        for (int x = 0; x < px; x++)
                            tex.SetPixel(tx * px + x, h - 1 - (ty * px + y), (y % 4 == 0) ? stoneEdge : stone);
                }
            }
        }

        // 花を散らす（草原テーマだけ。木のないタイルに）
        if (flowers)
        {
            for (int i = 0; i < tilesX * tilesY / 6; i++)
            {
                int tx = rng.Next(tilesX), ty = rng.Next(tilesY);
                if (tree[tx, ty]) continue;
                Color fc = rng.Next(3) switch
                {
                    0 => new Color(1f, 0.8f, 0.85f),
                    1 => new Color(1f, 0.95f, 0.6f),
                    _ => Color.white,
                };
                tex.SetPixel(tx * px + rng.Next(2, 6), h - 1 - (ty * px + rng.Next(2, 6)), fc);
            }
        }

        // 木 or 岩（テーマの配色で描く）
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (!tree[tx, ty]) continue;
                int cx = tx * px + px / 2;
                int cy = h - 1 - (ty * px + px / 2);
                DrawCircle(tex, cx, cy, 3, treeDark);
                DrawCircle(tex, cx - 1, cy + 1, 2, treeMid);
                tex.SetPixel(cx - 1, cy + 2, treeLight);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    // 紫のコウモリ（大きな翼・金色の目・牙）
    public static Sprite Bat()
    {
        if (TryPhoto("enemy_bat", out var photo)) return photo;
        var rows = new string[]
        {
            ".WW................WW.",
            ".WWWW............WWWW.",
            ".WWWWWW........WWWWWW.",
            "..WWWWWWW....WWWWWWW..",
            "..WWWWWWWBBBBWWWWWWW..",
            "...WWWWWBBBBBBWWWWW...",
            "....WWWBEBBBBEBWWW....",
            ".....WWBBBBBBBBWW.....",
            "......WBBFBBFBBW......",
            ".......BBBBBBBB.......",
            "........BB..BB........",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'W', new Color(0.45f, 0.30f, 0.60f) },  // 翼
            { 'B', new Color(0.28f, 0.18f, 0.38f) },  // 体
            { 'E', new Color(1f, 0.85f, 0.3f) },      // 目（金）
            { 'F', new Color(0.95f, 0.95f, 0.9f) },   // 牙
        };
        return FromMap(rows, palette);
    }

    // 岩のゴーレム（光る青い目・体のヒビ）
    public static Sprite Golem()
    {
        if (TryPhoto("enemy_golem", out var photo)) return photo;
        var rows = new string[]
        {
            "......GGGGGGGGG.......",
            ".....GGGGGGGGGGG......",
            "....GGLGGGGGGGGGG.....",
            "....GGGEEGGGGEEGG.....",
            "....GGGEEGGGGEEGG.....",
            ".....GGGGGggGGGG......",
            "..GGGGGGGGGGGGGGGG....",
            ".GGGGGGGGGGGGGGGGGG...",
            "GGGgGGGGGGGGGGGGgGGG..",
            "GGGg.GGGGGGGGGGG.gGGG.",
            "GGg..GGGGcGGGGGG..gGG.",
            "GGg..GGGGcGGGGGG..gGG.",
            "GGg..GGGGGGGGGGG..gGG.",
            ".gg..GGGGGGGGGGG..gg..",
            "......GGGGgGGGG.......",
            ".....GGGGGgGGGGG......",
            ".....GGGG...GGGG......",
            ".....GGGG...GGGG......",
            "....gGGGg...gGGGg.....",
            "....ggggg...ggggg.....",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'G', new Color(0.55f, 0.55f, 0.60f) },  // 岩
            { 'g', new Color(0.38f, 0.38f, 0.44f) },  // 岩の陰
            { 'L', new Color(0.72f, 0.72f, 0.78f) },  // ハイライト
            { 'E', new Color(0.4f, 1f, 1f) },         // 光る目（シアン）
            { 'c', new Color(0.25f, 0.25f, 0.3f) },   // ヒビ
        };
        return FromMap(rows, palette);
    }

    public static Sprite Slime()
    {
        if (TryPhoto("enemy_slime", out var photo)) return photo;
        var rows = new string[]
        {
            "......GGGGGG......",
            "....GGLLGGGGGG....",
            "...GGLLGGGGGGGG...",
            "..GGLGGGGGGGGGGg..",
            "..GGEEGGGGGGEEGg..",
            ".GGGeEGGGGGGeEGGg.",
            ".GGGGGGGGGGGGGGGg.",
            ".GGGGGGMMMMGGGGGg.",
            ".GGGGGGGGGGGGGGgg.",
            "..GGGGGGGGGGGGgg..",
            "..gGGGGGGGGGGggg..",
            "...ggggggggggg....",
        };
        var palette = new Dictionary<char, Color>
        {
            { 'G', new Color(0.4f, 0.85f, 0.45f) },   // 体（緑）
            { 'g', new Color(0.25f, 0.6f, 0.3f) },    // 体の陰（下側）
            { 'L', new Color(0.75f, 1f, 0.78f) },     // ハイライト（ぷるぷる感）
            { 'E', new Color(0.95f, 0.98f, 0.95f) },  // 白目
            { 'e', new Color(0.1f, 0.3f, 0.15f) },    // 瞳
            { 'M', new Color(0.15f, 0.45f, 0.2f) },   // 口
        };
        return FromMap(rows, palette);
    }
}
