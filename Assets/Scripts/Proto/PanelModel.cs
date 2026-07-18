using UnityEngine;
using System.Collections.Generic;

// 盤面マスの種別（特殊マス＝地形システム）
public enum CellKind
{
    Normal = 0,
    Power = 1,      // 強化マス：覆った攻撃カードの威力+50%（1マスごと）
    Gold = 2,       // 黄金マス：覆ったカードが手札に出るたびコイン+5
    Resonance = 3,  // 共鳴マス：このマスが絡む隣接シナジーを3倍で数える
    Curse = 4,      // 呪いマス：覆ったカードの出現率3倍、ただし使用時にHP-3
}

// スキルパネルの盤面データ（配置判定・確率算出）。本設では単一・正方形・可変サイズ(5×5〜10×10)。
// 1マス＝出現率の分子。空白マスは通常攻撃に変換される。出現率 = ピースのマス数 / 盤面マス数。
public class PanelModel
{
    // カード出現の底上げ係数。1ピースの重み = CardWeightScale / マス数（空きマス＝通常攻撃は1票）。
    // 大きくするほどカードが出やすく、通常攻撃が減る。（マス数が大きいほど出にくい関係は維持）
    public const float CardWeightScale = 10f;

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

    CellKind[,] _kinds;   // 特殊マスの種別

    // 歪みマス：この戦闘の間だけ封印されるマス（中ボス以上の攻撃で発生。掛かったピースは出現しない）
    public HashSet<Vector2Int> Sealed = new HashSet<Vector2Int>();

    bool CoversSealed(Placement p)
    {
        foreach (var c in p.cells) if (Sealed.Contains(c)) return true;
        return false;
    }

    public PanelModel(int w, int h)
    {
        W = w; H = h;
        _grid = new Placement[w, h];
        _unlocked = new bool[w, h];
        _kinds = new CellKind[w, h];
    }

    // ---- 特殊マス ----
    public CellKind KindAt(int x, int y) => IsValid(x, y) ? _kinds[x, y] : CellKind.Normal;
    public void SetKind(int x, int y, CellKind k) { if (IsValid(x, y)) _kinds[x, y] = k; }

    // 指定カードのピースが指定種別のマスを何個覆っているか（複数配置なら最大値）
    public int MaxKindCover(string cardId, CellKind kind)
    {
        int best = 0;
        foreach (var p in Placements)
        {
            if (p.card == null || p.card.id != cardId) continue;
            int n = 0;
            foreach (var c in p.cells) if (_kinds[c.x, c.y] == kind) n++;
            if (n > best) best = n;
        }
        return best;
    }

    bool CoversKind(Placement p, CellKind kind)
    {
        foreach (var c in p.cells) if (_kinds[c.x, c.y] == kind) return true;
        return false;
    }

    // 指定種別の特殊マスの個数
    public int CountKind(CellKind kind)
    {
        int n = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_kinds[x, y] == kind) n++;
        return n;
    }

    // 指定種別の特殊マスをすべて通常マスに戻す（呪いの浄化など）。戻した個数を返す。
    public int ClearKind(CellKind kind)
    {
        int n = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_kinds[x, y] == kind) { _kinds[x, y] = CellKind.Normal; n++; }
        return n;
    }

    // セーブ用：特殊マスの位置と種別を書き出し／復元
    public void CaptureSpecials(List<int> xs, List<int> ys, List<int> ks)
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (_kinds[x, y] != CellKind.Normal) { xs.Add(x); ys.Add(y); ks.Add((int)_kinds[x, y]); }
    }

    // ---- 形状シナジー ----
    // 行コンプリート：解放マスが3つ以上あり、その行の解放マスがすべてピースで埋まっている行の本数
    public int CompletedRows()
    {
        int n = 0;
        for (int y = 0; y < H; y++)
        {
            int unlocked = 0; bool full = true;
            for (int x = 0; x < W; x++)
            {
                if (!_unlocked[x, y]) continue;
                unlocked++;
                if (_grid[x, y] == null) { full = false; break; }
            }
            if (full && unlocked >= 3) n++;
        }
        return n;
    }

    // 列コンプリート：同条件を列で数える
    public int CompletedCols()
    {
        int n = 0;
        for (int x = 0; x < W; x++)
        {
            int unlocked = 0; bool full = true;
            for (int y = 0; y < H; y++)
            {
                if (!_unlocked[x, y]) continue;
                unlocked++;
                if (_grid[x, y] == null) { full = false; break; }
            }
            if (full && unlocked >= 3) n++;
        }
        return n;
    }

    // コア判定：そのピースの上下左右の隣接マス（解放済み）がすべて他のピースで埋まっており、
    // かつ他ピースとの接触マスが3つ以上ある（＝しっかり囲まれている）
    public bool IsCorePlacement(Placement p)
    {
        if (p == null) return false;
        int touching = 0;
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        var seen = new HashSet<Vector2Int>();
        foreach (var c in p.cells)
            foreach (var d in dirs)
            {
                var n = c + d;
                if (p.cells.Contains(n) || !seen.Add(n)) continue;   // 自分自身・重複はスキップ
                if (!IsUnlocked(n.x, n.y)) continue;                  // 盤面外・未解放は「壁」扱いでOK
                var other = _grid[n.x, n.y];
                if (other == null || other == p) return false;        // 露出あり→コアではない
                touching++;
            }
        return touching >= 3;
    }

    // 指定カードのいずれかの配置がコア化しているか
    public bool IsCoreCard(string cardId)
    {
        foreach (var p in Placements)
            if (p.card != null && p.card.id == cardId && IsCorePlacement(p)) return true;
        return false;
    }

    // コア化しているピースの数（表示用）
    public int CoreCount()
    {
        int n = 0;
        foreach (var p in Placements) if (IsCorePlacement(p)) n++;
        return n;
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

    // すべて未解放に戻す
    public void RelockAll()
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                _unlocked[x, y] = false;
    }

    // 未解放マスに掛かっている配置を除去（リセット時に呼ぶ）
    public void RemovePlacementsOnLocked()
    {
        for (int i = Placements.Count - 1; i >= 0; i--)
        {
            var p = Placements[i];
            bool bad = false;
            foreach (var c in p.cells) if (!_unlocked[c.x, c.y]) { bad = true; break; }
            if (bad)
            {
                foreach (var c in p.cells) _grid[c.x, c.y] = null;
                Placements.RemoveAt(i);
            }
        }
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
        var card = p.card;
        var original = new List<Vector2Int>(p.cells);

        // 中心（重心）まわりで90度回転すると、元の位置からあまりずれない
        int sx = 0, sy = 0;
        foreach (var c in p.cells) { sx += c.x; sy += c.y; }
        var pivot = new Vector2Int(Mathf.RoundToInt(sx / (float)p.cells.Count), Mathf.RoundToInt(sy / (float)p.cells.Count));
        var rotated = new List<Vector2Int>();
        foreach (var c in p.cells)
        {
            var rel = c - pivot;
            rotated.Add(pivot + new Vector2Int(rel.y, -rel.x));
        }

        // 元のピースを一旦外す（自分のマスも回転先の候補にできる）
        RemoveAt(x, y);

        // 近い位置から順に、収まるオフセットを探す（ウォールキック。最大4マスまでずらす）
        const int kick = 4;
        List<Vector2Int> bestCells = null; int best = int.MaxValue;
        for (int oy = -kick; oy <= kick; oy++)
            for (int ox = -kick; ox <= kick; ox++)
            {
                int dist = Mathf.Abs(ox) + Mathf.Abs(oy);
                if (dist >= best) continue;
                var cand = new List<Vector2Int>(rotated.Count);
                foreach (var c in rotated) cand.Add(new Vector2Int(c.x + ox, c.y + oy));
                if (CanPlaceCells(cand)) { best = dist; bestCells = cand; }
            }

        if (bestCells != null && PlaceCells(card, bestCells)) return true;
        PlaceCells(card, original);   // どこにも収まらなければ元に戻す
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

    // 出現重み付きエントリ。空きマス=1票（通常攻撃）。
    // 配置カードの1ピース重み = CardWeightScale × マス数^k（k = 1 - 2×HP割合）。
    //   HP満タン(1.0)→k=-1：1/マス数（小型有利）／ HP半分→k=0：フラット ／ 瀕死(0)→k=+1：マス数比例（大型＝強カード有利）
    public List<(CardDef card, float weight)> WeightedEntries(float hpRatio = 1f, int emptyReduce = 0)
    {
        float k = 1f - 2f * Mathf.Clamp01(hpRatio);
        var list = new List<(CardDef, float)>();
        foreach (var p in Placements)
            if (p.cells.Count > 0)
            {
                if (CoversSealed(p)) continue;   // 歪みマスに掛かるピースはこの戦闘中出ない
                float w = CardWeightScale * Mathf.Pow(p.cells.Count, k);
                if (CoversKind(p, CellKind.Curse)) w *= GameBalance.CurseWeightMult;   // 呪いマス：出現率アップ
                list.Add((p.card, w));
            }
        int sealedEmpty = 0;
        foreach (var s in Sealed) if (IsValid(s.x, s.y) && _unlocked[s.x, s.y] && _grid[s.x, s.y] == null) sealedEmpty++;
        int empty = UnlockedCount() - OccupiedCount() - sealedEmpty - Mathf.Max(0, emptyReduce);   // 圧縮カードで空きマスを減らす
        for (int i = 0; i < empty; i++) list.Add((null, 1f)); // 空きマス＝通常攻撃
        return list;
    }

    // 指定カードの出現率（0〜100%）。現在のHP割合・空きマス圧縮を反映
    public float CardAppearRate(string cardId, float hpRatio = 1f, int emptyReduce = 0)
    {
        var entries = WeightedEntries(hpRatio, emptyReduce);
        float total = 0f, mine = 0f;
        foreach (var e in entries)
        {
            total += e.weight;
            if (e.card != null && e.card.id == cardId) mine += e.weight;
        }
        return total > 0f ? mine / total * 100f : 0f;
    }

    // 通常攻撃（空きマス）の出現率（0〜100%）
    public float EmptyAppearRate(float hpRatio = 1f, int emptyReduce = 0)
    {
        var entries = WeightedEntries(hpRatio, emptyReduce);
        float total = 0f, mine = 0f;
        foreach (var e in entries)
        {
            total += e.weight;
            if (e.card == null) mine += e.weight;
        }
        return total > 0f ? mine / total * 100f : 0f;
    }

    // 重みに従ってカードを1枚抽選（null＝通常攻撃）。母集団が空ならnull。
    public CardDef PickWeighted(float hpRatio = 1f, int emptyReduce = 0)
    {
        var entries = WeightedEntries(hpRatio, emptyReduce);
        float total = 0f;
        foreach (var e in entries) total += e.weight;
        if (total <= 0f) return null;
        float r = Random.value * total;
        foreach (var e in entries) { r -= e.weight; if (r <= 0f) return e.card; }
        return entries[entries.Count - 1].card;
    }
}
