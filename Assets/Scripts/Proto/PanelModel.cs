using UnityEngine;
using System.Collections.Generic;

// スキルパネルの盤面データ（配置判定・確率算出）。本設では単一・正方形・可変サイズ(5×5〜10×10)。
// 1マス＝出現率の分子。空白マスは通常攻撃に変換される。出現率 = ピースのマス数 / 盤面マス数。
public class PanelModel
{
    public int W { get; private set; }
    public int H { get; private set; }

    // 1回の配置（ピース1個）
    public class Placement
    {
        public int id;
        public CardDef card;
        public List<Vector2Int> cells = new List<Vector2Int>();
    }

    Placement[,] _grid;
    bool[,] _unlocked;   // そのマスが解放済み（配置可能）か
    int _nextId = 1;
    public List<Placement> Placements = new List<Placement>();

    public PanelModel(int w, int h)
    {
        W = w; H = h;
        _grid = new Placement[w, h];
        _unlocked = new bool[w, h];
    }

    // 中央に cols×rows マス（横cols×縦rows）を初期解放
    public void UnlockInitial(int cols, int rows)
    {
        int sx = (W - cols) / 2, sy = (H - rows) / 2;
        for (int x = sx; x < sx + cols && x < W; x++)
            for (int y = sy; y < sy + rows && y < H; y++)
                if (x >= 0 && y >= 0) _unlocked[x, y] = true;
    }

    public bool IsUnlocked(int x, int y) => IsValid(x, y) && _unlocked[x, y];

    public bool Unlock(int x, int y)
    {
        if (!IsValid(x, y) || _unlocked[x, y]) return false;
        _unlocked[x, y] = true;
        return true;
    }

    public int UnlockedCount()
    {
        int n = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_unlocked[x, y]) n++;
        return n;
    }

    public List<Vector2Int> GetUnlockedCells()
    {
        var list = new List<Vector2Int>();
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_unlocked[x, y]) list.Add(new Vector2Int(x, y));
        return list;
    }

    // 盤面拡張（既存配置は座標そのまま保持。範囲外になった配置は除去）
    public void Resize(int newW, int newH)
    {
        var newGrid = new Placement[newW, newH];
        var kept = new List<Placement>();
        foreach (var p in Placements)
        {
            bool fits = true;
            foreach (var c in p.cells)
                if (c.x < 0 || c.y < 0 || c.x >= newW || c.y >= newH) { fits = false; break; }
            if (!fits) continue;
            foreach (var c in p.cells) newGrid[c.x, c.y] = p;
            kept.Add(p);
        }
        W = newW; H = newH;
        _grid = newGrid;
        Placements = kept;
    }

    public bool IsValid(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    public Placement GetAt(int x, int y) => IsValid(x, y) ? _grid[x, y] : null;

    public static Vector2Int Rotate(Vector2Int v, int rot)
    {
        for (int i = 0; i < ((rot % 4) + 4) % 4; i++)
            v = new Vector2Int(v.y, -v.x);
        return v;
    }

    public IEnumerable<Vector2Int> Cells(CardDef c, Vector2Int anchor, int rot)
    {
        foreach (var o in c.Shape)
            yield return anchor + Rotate(o, rot);
    }

    public bool CanPlace(CardDef c, Vector2Int anchor, int rot)
    {
        foreach (var cell in Cells(c, anchor, rot))
        {
            if (!IsValid(cell.x, cell.y)) return false;
            if (!_unlocked[cell.x, cell.y]) return false;
            if (_grid[cell.x, cell.y] != null) return false;
        }
        return true;
    }

    public bool Place(CardDef c, Vector2Int anchor, int rot)
    {
        if (!CanPlace(c, anchor, rot)) return false;
        var p = new Placement { id = _nextId++, card = c };
        foreach (var cell in Cells(c, anchor, rot))
        {
            _grid[cell.x, cell.y] = p;
            p.cells.Add(cell);
        }
        Placements.Add(p);
        return true;
    }

    public bool CanPlaceCells(List<Vector2Int> cells)
    {
        foreach (var c in cells)
        {
            if (!IsValid(c.x, c.y)) return false;
            if (!_unlocked[c.x, c.y]) return false;
            if (_grid[c.x, c.y] != null) return false;
        }
        return true;
    }

    public bool PlaceCells(CardDef card, List<Vector2Int> cells)
    {
        if (!CanPlaceCells(cells)) return false;
        var p = new Placement { id = _nextId++, card = card };
        foreach (var c in cells) { _grid[c.x, c.y] = p; p.cells.Add(c); }
        Placements.Add(p);
        return true;
    }

    // 指定マスのピースをその場で90度回転（盤外は内側へスライド／衝突時は元に戻す）
    public bool RotatePlacementAt(int x, int y)
    {
        var p = GetAt(x, y);
        if (p == null) return false;

        var pivot = p.cells[0];
        var rotated = new List<Vector2Int>();
        foreach (var c in p.cells)
        {
            var rel = c - pivot;
            rotated.Add(pivot + new Vector2Int(rel.y, -rel.x));
        }

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in rotated)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
        }
        int dx = minX < 0 ? -minX : (maxX >= W ? W - 1 - maxX : 0);
        int dy = minY < 0 ? -minY : (maxY >= H ? H - 1 - maxY : 0);
        for (int i = 0; i < rotated.Count; i++) rotated[i] += new Vector2Int(dx, dy);

        var card = p.card;
        var original = new List<Vector2Int>(p.cells);
        RemoveAt(x, y);
        if (PlaceCells(card, rotated)) return true;
        PlaceCells(card, original);
        return false;
    }

    public void RemoveAt(int x, int y)
    {
        var p = GetAt(x, y);
        if (p == null) return;
        foreach (var c in p.cells) _grid[c.x, c.y] = null;
        Placements.Remove(p);
    }

    public int OccupiedCount()
    {
        int n = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_grid[x, y] != null) n++;
        return n;
    }

    // カードごとの占有マス数
    public Dictionary<CardDef, int> CountByCard()
    {
        var d = new Dictionary<CardDef, int>();
        foreach (var p in Placements)
        {
            if (!d.ContainsKey(p.card)) d[p.card] = 0;
            d[p.card] += p.cells.Count;
        }
        return d;
    }

    public int ValidCount() => UnlockedCount();   // 山札母数＝解放済みマス

    // 山札（解放マスのみ。マス＝カード。null＝通常攻撃）。確率サンプリングの母集団。
    public List<CardDef> BuildDeck()
    {
        var deck = new List<CardDef>();
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_unlocked[x, y]) deck.Add(_grid[x, y]?.card);
        return deck;
    }
}
