using UnityEngine;
using System.Collections.Generic;

// 10x10スキルパネルの盤面データ（GDD SYS-02: 盤面管理・配置判定・確率算出）
// 1マス=出現率1%。空白マスは通常攻撃に変換される。
public class PanelModel
{
    public int W { get; private set; }
    public int H { get; private set; }

    // 1回の配置（ピース1個）を表す
    public class Placement
    {
        public int id;
        public ProtoSkill skill;
        public List<Vector2Int> cells = new List<Vector2Int>();
    }

    Placement[,] _grid;
    int _nextId = 1;
    public List<Placement> Placements = new List<Placement>();

    public PanelModel(int w, int h)
    {
        W = w;
        H = h;
        _grid = new Placement[w, h];
    }

    public Placement GetAt(int x, int y) => _grid[x, y];

    // 90度回転をrot回適用
    public static Vector2Int Rotate(Vector2Int v, int rot)
    {
        for (int i = 0; i < ((rot % 4) + 4) % 4; i++)
            v = new Vector2Int(v.y, -v.x);
        return v;
    }

    public IEnumerable<Vector2Int> Cells(ProtoSkill s, Vector2Int anchor, int rot)
    {
        foreach (var o in s.shape)
            yield return anchor + Rotate(o, rot);
    }

    public bool CanPlace(ProtoSkill s, Vector2Int anchor, int rot)
    {
        foreach (var c in Cells(s, anchor, rot))
        {
            if (c.x < 0 || c.y < 0 || c.x >= W || c.y >= H) return false;
            if (_grid[c.x, c.y] != null) return false;
        }
        return true;
    }

    public bool Place(ProtoSkill s, Vector2Int anchor, int rot)
    {
        if (!CanPlace(s, anchor, rot)) return false;

        var p = new Placement { id = _nextId++, skill = s };
        foreach (var c in Cells(s, anchor, rot))
        {
            _grid[c.x, c.y] = p;
            p.cells.Add(c);
        }
        Placements.Add(p);
        return true;
    }

    // 任意のマス集合が配置可能か（ドラッグ移動用）
    public bool CanPlaceCells(List<Vector2Int> cells)
    {
        foreach (var c in cells)
        {
            if (c.x < 0 || c.y < 0 || c.x >= W || c.y >= H) return false;
            if (_grid[c.x, c.y] != null) return false;
        }
        return true;
    }

    // 任意のマス集合にそのまま配置（ドラッグ移動用）
    public bool PlaceCells(ProtoSkill s, List<Vector2Int> cells)
    {
        if (!CanPlaceCells(cells)) return false;
        var p = new Placement { id = _nextId++, skill = s };
        foreach (var c in cells)
        {
            _grid[c.x, c.y] = p;
            p.cells.Add(c);
        }
        Placements.Add(p);
        return true;
    }

    // 指定マスにあるピースをその場で90度回転。
    // 盤外にはみ出す場合は内側へ自動スライド。他ピースと衝突する場合のみ失敗（元に戻す）
    public bool RotatePlacementAt(int x, int y)
    {
        var p = _grid[x, y];
        if (p == null) return false;

        var pivot = p.cells[0];
        var rotated = new List<Vector2Int>();
        foreach (var c in p.cells)
        {
            var rel = c - pivot;
            rotated.Add(pivot + new Vector2Int(rel.y, -rel.x));
        }

        // 盤外にはみ出した分を内側へスライド
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in rotated)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
        }
        int dx = minX < 0 ? -minX : (maxX >= W ? W - 1 - maxX : 0);
        int dy = minY < 0 ? -minY : (maxY >= H ? H - 1 - maxY : 0);
        for (int i = 0; i < rotated.Count; i++) rotated[i] += new Vector2Int(dx, dy);

        var skill = p.skill;
        var original = new List<Vector2Int>(p.cells);
        RemoveAt(x, y);

        if (PlaceCells(skill, rotated)) return true;
        PlaceCells(skill, original); // 他ピースと衝突した場合は復元
        return false;
    }

    // 指定マスにあるピースを丸ごと撤去
    public void RemoveAt(int x, int y)
    {
        var p = _grid[x, y];
        if (p == null) return;
        foreach (var c in p.cells)
            _grid[c.x, c.y] = null;
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

    // スキルごとの占有マス数（=出現率%）
    public Dictionary<ProtoSkill, int> CountBySkill()
    {
        var d = new Dictionary<ProtoSkill, int>();
        foreach (var p in Placements)
        {
            if (!d.ContainsKey(p.skill)) d[p.skill] = 0;
            d[p.skill] += p.cells.Count;
        }
        return d;
    }

    // 山札を生成: 100マス=100枚（GDD推奨の引き切り方式）。null=通常攻撃
    public List<ProtoSkill> BuildDeck()
    {
        var deck = new List<ProtoSkill>();
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                deck.Add(_grid[x, y]?.skill);
        return deck;
    }
}
