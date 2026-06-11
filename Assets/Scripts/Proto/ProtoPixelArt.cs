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

    // 銀髪ロング・青目・水色リボン・白ドレス（裾シースルー）・茶靴
    // 22x32の高解像度版: ハイライト(L)・髪の陰(h)・ドレスの皺(w)・瞳の描き分け(E/e)入り
    public static Sprite Mama()
    {
        // 30x38: 黒ドレス版
        // ・黒ドレスで銀髪とのコントラストを確保（同化問題の解消）
        // ・髪は頭→肩→太もも付近まで途切れず流れる超ロング
        // ・前髪はギザギザの不揃いな毛先
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
            { 'H', new Color(0.87f, 0.88f, 0.95f) },        // 髪（銀髪ベース）
            { 'h', new Color(0.66f, 0.68f, 0.80f) },        // 髪の陰
            { 'L', new Color(0.98f, 0.99f, 1f) },           // 髪のハイライト
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

    public static Sprite Slime()
    {
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
