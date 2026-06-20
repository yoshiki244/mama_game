using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// マップ画面: 横向き（左→右）のノードマップ（すごろく・仮ローグライク）。
// マス種別: エネミー / イベント / 精神樹 を約5:3:1で配置（精神樹は最低5か所）。固定レイアウト。
// 光るノードをクリックで進み、種別に応じて 戦闘 / お金獲得 / ショップ を行う。全分岐は同じボスへ収束。
public class MapScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root, _nodeLayer, _playerIcon;
    ScrollRect _scroll;
    TextMeshProUGUI _moneyText, _notice, _waveText;
    GameObject _shopOverlay, _clearOverlay;
    const int MaxWave = 3;
    bool _moving;

    enum TileType { Start, Enemy, Event, SpiritTree, Shop, Contract, MidBoss, Boss }

    class Node
    {
        public int col, lane;
        public TileType type;
        public bool cleared;
        public EnemyDef enemy;
        public Vector2 pos;
        public readonly List<Node> next = new List<Node>();
        public Image icon, marker;
        public Button button;
    }

    readonly List<Node> _nodes = new List<Node>();
    Node _current, _engaged;
    float _contentWidth, _halfWidth;

    const int MidColumns = 18;
    const float ColSpacing = 215f;
    const float LaneSpacing = 185f;
    static readonly int[] Lanes = { -1, 0, 1 };
    static readonly Color LineColor = new Color(1f, 0.82f, 0.28f, 0.92f);

    public void Init(ProtoMain main) { _main = main; BuildUI(); BuildMap(); Hide(); }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        _moving = false;
        _main.PlayMapBgm(0);
        RefreshNodes();
    }

    public void Hide() { StopAllCoroutines(); _moving = false; if (_root != null) _root.gameObject.SetActive(false); }

    // ==================== UI ====================

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MapScreen", _main.Canvas.transform);
        var bg = _root.gameObject.AddComponent<Image>();
        bg.sprite = ProtoPixelArt.DungeonMapBackground();
        bg.color = Color.white;
        bg.raycastTarget = false;
        bg.preserveAspect = false;

        ProtoUI.CreatePanel("MapVignette", _root, Vector2.zero, new Vector2(1700, 900), new Color(0, 0, 0, 0.28f)).raycastTarget = false;
        CreateGoldBorder();

        var title = ProtoUI.CreateText("MapTitle", _root, "ダンジョンマップ", 42, new Vector2(0, 404), new Vector2(680, 64), new Color(1f, 0.84f, 0.34f));
        ProtoUI.StyleTitle(title, new Color(1f, 0.84f, 0.34f), 5f);
        title.outlineWidth = 0.28f;
        CreateTitleOrnament(new Vector2(0, 360), 330f);

        _moneyText = ProtoUI.CreateText("Money", _root, "", 24, new Vector2(-545, 405), new Vector2(250, 36), ProtoUI.Gold, TextAlignmentOptions.Left);
        _moneyText.fontStyle = FontStyles.Bold;
        CreateTitleOrnament(new Vector2(-555, 365), 190f);

        // Wave表示（右上）
        _waveText = ProtoUI.CreateText("Wave", _root, "", 26, new Vector2(540, 405), new Vector2(260, 38), new Color(1f, 0.84f, 0.34f), TextAlignmentOptions.Right);
        _waveText.fontStyle = FontStyles.Bold;
        CreateTitleOrnament(new Vector2(560, 365), 190f);

        var viewport = ProtoUI.CreateRect("MapViewport", _root);
        viewport.anchoredPosition = new Vector2(42, 8);
        viewport.sizeDelta = new Vector2(1470, 610);
        viewport.gameObject.AddComponent<RectMask2D>();
        var srImg = viewport.gameObject.AddComponent<Image>(); srImg.color = new Color(0, 0, 0, 0.001f);
        _scroll = viewport.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = true; _scroll.vertical = false; _scroll.viewport = viewport;
        _scroll.scrollSensitivity = 30f; _scroll.movementType = ScrollRect.MovementType.Clamped;

        _nodeLayer = ProtoUI.CreateRect("NodeLayer", viewport);
        _nodeLayer.anchorMin = new Vector2(0.5f, 0.5f); _nodeLayer.anchorMax = new Vector2(0.5f, 0.5f);
        _nodeLayer.pivot = new Vector2(0.5f, 0.5f);
        _nodeLayer.anchoredPosition = Vector2.zero;
        _scroll.content = _nodeLayer;

        _playerIcon = ProtoUI.CreateRect("PlayerIcon", _nodeLayer);
        _playerIcon.sizeDelta = new Vector2(118, 150);
        var pimg = _playerIcon.gameObject.AddComponent<Image>();
        pimg.sprite = ProtoPixelArt.MamaMapPhoto(); pimg.preserveAspect = true; pimg.raycastTarget = false;

        _notice = ProtoUI.CreateText("Notice", _root, "", 19, new Vector2(0, 328), new Vector2(1200, 30), new Color(1f, 0.92f, 0.6f));

        ProtoUI.CreatePanel("BottomBar", _root, new Vector2(0, -424), new Vector2(1700, 56), new Color(0.015f, 0.014f, 0.02f, 0.90f)).raycastTarget = false;
        ProtoUI.CreatePanel("BottomBarLine", _root, new Vector2(0, -395), new Vector2(1700, 2), new Color(0.95f, 0.72f, 0.26f, 0.70f)).raycastTarget = false;
        ProtoUI.CreateButton("MenuBtn", _root, "メニュー", 18, new Vector2(-700, -424), new Vector2(178, 42),
            new Color(0.18f, 0.14f, 0.35f, 0.98f), () => _main.ShowMenu());
        ProtoUI.CreateText("Hint", _root,
            "Bメニュー　移動:クリック　敵:戦闘　騎士:中ボス　？:イベント　樹:神聖樹　店:ショップ　契:契約(HP→マス)",
            17, new Vector2(120, -424), new Vector2(1480, 30), ProtoUI.Gold);
    }

    void CreateGoldBorder()
    {
        var c = new Color(0.92f, 0.68f, 0.25f, 0.78f);
        ProtoUI.CreatePanel("BorderTop", _root, new Vector2(0, 440), new Vector2(1590, 2), c).raycastTarget = false;
        ProtoUI.CreatePanel("BorderBottom", _root, new Vector2(0, -390), new Vector2(1590, 2), c).raycastTarget = false;
        ProtoUI.CreatePanel("BorderLeft", _root, new Vector2(-795, 25), new Vector2(2, 828), c).raycastTarget = false;
        ProtoUI.CreatePanel("BorderRight", _root, new Vector2(795, 25), new Vector2(2, 828), c).raycastTarget = false;

        CreateCorner(new Vector2(-774, 419), 1, 1);
        CreateCorner(new Vector2(774, 419), -1, 1);
        CreateCorner(new Vector2(-774, -369), 1, -1);
        CreateCorner(new Vector2(774, -369), -1, -1);
    }

    void CreateCorner(Vector2 pos, int sx, int sy)
    {
        var c = new Color(1f, 0.78f, 0.28f, 0.82f);
        for (int i = 0; i < 3; i++)
        {
            var h = ProtoUI.CreatePanel("CornerH", _root, pos + new Vector2(sx * i * 12f, 0), new Vector2(32 - i * 6, 2), c);
            h.raycastTarget = false;
            var v = ProtoUI.CreatePanel("CornerV", _root, pos + new Vector2(0, sy * i * 12f), new Vector2(2, 32 - i * 6), c);
            v.raycastTarget = false;
        }
        var dot = ProtoUI.CreatePanel("CornerDot", _root, pos + new Vector2(sx * 24f, sy * 24f), new Vector2(6, 6), c);
        dot.transform.localRotation = Quaternion.Euler(0, 0, 45);
        dot.raycastTarget = false;
    }

    void CreateTitleOrnament(Vector2 pos, float width)
    {
        var c = new Color(1f, 0.77f, 0.24f, 0.82f);
        ProtoUI.CreatePanel("OrnamentLineL", _root, pos + new Vector2(-width * 0.27f, 0), new Vector2(width * 0.38f, 2), c).raycastTarget = false;
        ProtoUI.CreatePanel("OrnamentLineR", _root, pos + new Vector2(width * 0.27f, 0), new Vector2(width * 0.38f, 2), c).raycastTarget = false;
        for (int i = -1; i <= 1; i++)
        {
            var d = ProtoUI.CreatePanel("OrnamentDot", _root, pos + new Vector2(i * 34f, 0), new Vector2(i == 0 ? 13 : 7, i == 0 ? 13 : 7), c);
            d.transform.localRotation = Quaternion.Euler(0, 0, 45);
            d.raycastTarget = false;
        }
    }

    void CreateLegendItem(Vector2 pos, string icon, string label, Color iconColor)
    {
        var i = ProtoUI.CreateText("LegendIcon", _root, icon, 25, pos + new Vector2(-78, 0), new Vector2(44, 30), iconColor);
        i.fontStyle = FontStyles.Bold;
        ProtoUI.CreateText("LegendLabel", _root, label, 18, pos + new Vector2(28, 0), new Vector2(190, 30), ProtoUI.Gold, TextAlignmentOptions.Left);
    }
    // ==================== マップ生成（固定・横向き） ====================

    void BuildMap()
    {
        // 既存ノードUI（線・マーカー・アイコン）を消す（プレイヤーアイコンは残す）
        for (int i = _nodeLayer.childCount - 1; i >= 0; i--)
        {
            var ch = _nodeLayer.GetChild(i);
            if (ch == _playerIcon) continue;
            Destroy(ch.gameObject);
        }
        _nodes.Clear();
        int bossCol = MidColumns + 1;

        // コンテンツ幅を先に確定（ノード座標は中央基準で配置する）
        _contentWidth = (bossCol + 1) * ColSpacing + 105f;
        _halfWidth = _contentWidth / 2f;
        _nodeLayer.sizeDelta = new Vector2(_contentWidth, 600);

        // 開始ノード（左端・中央レーン）
        var start = NewNode(0, 0, TileType.Start);

        // 中間ノード（各列3レーン）＝毎回ランダム配置
        var byCol = new Dictionary<int, List<Node>>();
        byCol[0] = new List<Node> { start };

        // 中ボス列は「5の倍数の列」に固定（その列は全レーン中ボス＝必ず通る）
        var mbCols = new HashSet<int>();
        for (int col = 5; col <= MidColumns; col += 5) mbCols.Add(col);

        var normalNodes = new List<Node>();
        for (int col = 1; col <= MidColumns; col++)
        {
            bool midbossCol = mbCols.Contains(col);
            var list = new List<Node>();
            foreach (int lane in Lanes)
            {
                if (midbossCol) { list.Add(NewNode(col, lane, TileType.MidBoss)); continue; }
                TileType t;
                if (col == 1) t = TileType.Enemy;             // 初手は必ず戦闘
                else if (col >= 3) { float r = Random.value; t = r < 0.60f ? TileType.Enemy : r < 0.82f ? TileType.Event : TileType.SpiritTree; }
                else t = Random.value < 0.70f ? TileType.Enemy : TileType.Event;
                var n = NewNode(col, lane, t);
                normalNodes.Add(n);
                list.Add(n);
            }
            byCol[col] = list;
        }

        // ボス（右端・中央）
        var boss = NewNode(bossCol, 0, TileType.Boss);
        byCol[bossCol] = new List<Node> { boss };

        // エッジ（次の列でレーン差≤1）
        for (int col = 0; col < bossCol; col++)
            foreach (var a in byCol[col])
                foreach (var b in byCol[col + 1])
                    if (Mathf.Abs(a.lane - b.lane) <= 1)
                        a.next.Add(b);

        // 同種隣接禁止：イベント→イベント / 神聖樹→神聖樹 はつなげない（右側を戦闘に変換）
        bool changed = true; int safe = 0;
        while (changed && safe++ < 12)
        {
            changed = false;
            foreach (var a in _nodes)
                foreach (var b in a.next)
                    if (a.type == b.type && (a.type == TileType.Event || a.type == TileType.SpiritTree))
                    { b.type = TileType.Enemy; changed = true; }
        }

        // 店マスを3つ配置（3バトル以降＝col>=4、店どうしは隣接させない）
        var shopCand = normalNodes.FindAll(n => n.col >= 4 && n.type != TileType.MidBoss);
        for (int i = shopCand.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var tmp = shopCand[i]; shopCand[i] = shopCand[j]; shopCand[j] = tmp; }
        int shops = 0;
        foreach (var n in shopCand)
        {
            if (shops >= 3) break;
            if (n.type == TileType.Shop || ConnectedToType(n, TileType.Shop)) continue;
            n.type = TileType.Shop; shops++;
        }

        // 契約マスを2つ配置（3バトル以降＝col>=4、契約どうしは隣接させない・店マスは避ける）
        var conCand = normalNodes.FindAll(n => n.col >= 4 && n.type != TileType.Shop && n.type != TileType.MidBoss);
        for (int i = conCand.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var tmp = conCand[i]; conCand[i] = conCand[j]; conCand[j] = tmp; }
        int contracts = 0;
        foreach (var n in conCand)
        {
            if (contracts >= 2) break;
            if (n.type == TileType.Contract || ConnectedToType(n, TileType.Contract)) continue;
            n.type = TileType.Contract; contracts++;
        }

        // 神聖樹を最低5（col>=3、樹どうしは隣接させない）
        int trees = _nodes.FindAll(n => n.type == TileType.SpiritTree).Count;
        if (trees < 5)
        {
            var evs = normalNodes.FindAll(n => n.type == TileType.Event && n.col >= 3);
            for (int i = evs.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var tmp = evs[i]; evs[i] = evs[j]; evs[j] = tmp; }
            foreach (var n in evs)
            {
                if (trees >= 5) break;
                if (ConnectedToType(n, TileType.SpiritTree)) continue;
                n.type = TileType.SpiritTree; trees++;
            }
        }

        // 種別確定後に敵を割り当て
        foreach (var n in _nodes)
        {
            if (n.type == TileType.Enemy) n.enemy = EnemyForColumn(n.col);
            else if (n.type == TileType.MidBoss) n.enemy = MakeMidBoss(n.col);
            else if (n.type == TileType.Boss) n.enemy = _main.Db != null ? _main.Db.FindEnemy("dragon") : null;
        }

        foreach (var a in _nodes)
            foreach (var b in a.next)
                DrawLine(a, b);
        foreach (var n in _nodes) BuildNodeUI(n);

        _current = start;
        _playerIcon.anchoredPosition = start.pos;
        _playerIcon.SetAsLastSibling();
    }

    Node NewNode(int col, int lane, TileType type)
    {
        var n = new Node
        {
            col = col, lane = lane, type = type,
            cleared = type == TileType.Start,
            pos = new Vector2(col * ColSpacing + 58f - _halfWidth, lane * LaneSpacing),
        };
        _nodes.Add(n);
        return n;
    }

    // nがエッジで指定種別のノードと接続しているか（前後どちらも）
    bool ConnectedToType(Node n, TileType t)
    {
        foreach (var b in n.next) if (b.type == t) return true;
        foreach (var a in _nodes) if (a.next.Contains(n) && a.type == t) return true;
        return false;
    }

    // 中ボス（騎士）を実行時生成。通常敵より頑丈・強力
    EnemyDef MakeMidBoss(int col)
    {
        var e = ScriptableObject.CreateInstance<EnemyDef>();
        e.id = "midboss_knight";
        e.enemyName = "騎士";
        e.spriteKey = EnemySpriteKey.Knight;
        e.baseHP = 220 + col * 12;
        e.minAtk = 12; e.maxAtk = 20;
        e.battleSize = new Vector2(360, 360);
        e.mapSize = new Vector2(90, 90);
        e.levelOffset = 1;
        e.moneyReward = 60;
        e.attacks = new[]
        {
            new EnemyAttackDef { name = "斬撃", mult = 1f, hits = 1, weight = 50 },
            new EnemyAttackDef { name = "連撃", mult = 0.7f, hits = 2, weight = 30 },
            new EnemyAttackDef { name = "強打", mult = 1.6f, hits = 1, weight = 20 },
        };
        return e;
    }

    EnemyDef EnemyForColumn(int col)
    {
        if (_main.Db == null) return null;
        float p = col / (float)MidColumns;
        string id = p < 0.35f ? (Random.value < 0.5f ? "slime" : "bat")
                  : p < 0.7f ? (Random.value < 0.5f ? "bat" : "golem")
                  : "golem";
        return _main.Db.FindEnemy(id) ?? _main.Db.FindEnemy("slime");
    }

    void DrawLine(Node a, Node b)
    {
        var seg = ProtoUI.CreateRect("Line", _nodeLayer);
        Vector2 mid = (a.pos + b.pos) / 2f; Vector2 d = b.pos - a.pos;
        seg.anchoredPosition = mid; seg.sizeDelta = new Vector2(6f, d.magnitude);
        seg.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
        var glow = seg.gameObject.AddComponent<Image>(); glow.color = new Color(1f, 0.72f, 0.18f, 0.20f); glow.raycastTarget = false;
        var core = ProtoUI.CreatePanel("LineCore", seg, Vector2.zero, new Vector2(2.5f, d.magnitude), LineColor);
        core.raycastTarget = false;
    }

    void BuildNodeUI(Node n)
    {
        var markerRt = ProtoUI.CreateRect($"Mk_{n.col}_{n.lane}", _nodeLayer);
        markerRt.anchoredPosition = n.pos; markerRt.sizeDelta = new Vector2(92, 92);
        n.marker = markerRt.gameObject.AddComponent<Image>(); n.marker.color = Color.clear; n.marker.raycastTarget = false;

        if (n.type != TileType.Start) CreateNodeFrame(n);

        var iconRt = ProtoUI.CreateRect($"Nd_{n.col}_{n.lane}", _nodeLayer);
        iconRt.anchoredPosition = n.pos;
        iconRt.sizeDelta = n.type == TileType.Boss ? new Vector2(104, 104) : n.type == TileType.MidBoss ? new Vector2(88, 88) : new Vector2(62, 62);
        n.icon = iconRt.gameObject.AddComponent<Image>(); n.icon.preserveAspect = true;

        switch (n.type)
        {
            case TileType.Start: n.icon.sprite = ProtoPixelArt.MamaMapPhoto(); break;
            case TileType.Enemy: n.icon.sprite = n.enemy != null ? n.enemy.MapSprite() : ProtoPixelArt.Slime(); break;
            case TileType.MidBoss: n.icon.sprite = n.enemy != null ? n.enemy.MapSprite() : ProtoPixelArt.Knight(); break;
            case TileType.Boss:  n.icon.sprite = n.enemy != null ? n.enemy.MapSprite() : ProtoPixelArt.Dragon(); break;
            case TileType.Event:
            {
                var ev = ProtoPixelArt.EventPhoto();
                if (ev != null) { n.icon.sprite = ev; n.icon.color = Color.white; }
                else { n.icon.sprite = null; n.icon.color = new Color(0.82f, 0.58f, 0.18f, 0.96f); AddLabel(iconRt, "?", 34, new Color(1f, 0.92f, 0.58f)); }
                break;
            }
            case TileType.SpiritTree:
            {
                var tree = ProtoPixelArt.TreePhoto();
                if (tree != null) { n.icon.sprite = tree; n.icon.color = Color.white; }
                else { n.icon.sprite = null; n.icon.color = new Color(0.24f, 0.58f, 0.34f, 0.96f); AddLabel(iconRt, "樹", 28, new Color(0.78f, 1f, 0.74f)); }
                break;
            }
            case TileType.Shop:
            {
                var sp = ProtoPixelArt.ShopPhoto();
                if (sp != null) { n.icon.sprite = sp; n.icon.color = Color.white; }
                else { n.icon.sprite = null; n.icon.color = new Color(0.85f, 0.65f, 0.18f, 0.96f); AddLabel(iconRt, "店", 28, new Color(0.20f, 0.12f, 0.02f)); }
                break;
            }
            case TileType.Contract:
            {
                var sp = ProtoPixelArt.ContractPhoto();
                if (sp != null) { n.icon.sprite = sp; n.icon.color = Color.white; }
                else { n.icon.sprite = null; n.icon.color = new Color(0.7f, 0.3f, 0.7f, 0.96f); AddLabel(iconRt, "契", 28, new Color(1f, 0.9f, 1f)); }
                break;
            }
        }

        n.button = iconRt.gameObject.AddComponent<Button>();
        n.button.targetGraphic = n.icon;
        var node = n;
        n.button.onClick.AddListener(() => OnNodeClicked(node));
    }

    void CreateNodeFrame(Node n)
    {
        Vector2 size = n.type == TileType.Boss ? new Vector2(112, 112) : n.type == TileType.MidBoss ? new Vector2(98, 98) : new Vector2(78, 78);
        Color frameColor = n.type == TileType.SpiritTree
            ? new Color(0.38f, 0.72f, 0.28f, 0.92f)
            : new Color(0.86f, 0.55f, 0.14f, 0.94f);
        Color innerColor = n.type == TileType.SpiritTree
            ? new Color(0.86f, 0.92f, 0.80f, 0.32f)   // 樹は明るく薄い背景（木の画像が沈まないように）
            : new Color(0.10f, 0.065f, 0.025f, 0.78f);

        var frame = ProtoUI.CreatePanel($"Frame_{n.col}_{n.lane}", _nodeLayer, n.pos, size, frameColor);
        frame.raycastTarget = false;
        var inner = ProtoUI.CreatePanel("FrameInner", frame.transform, Vector2.zero, size - new Vector2(10, 10), innerColor);
        inner.raycastTarget = false;
        ProtoUI.AddPanelTrim(inner, size - new Vector2(10, 10), new Color(1f, 0.78f, 0.24f, 0.62f), new Color(1f, 1f, 1f, 0.10f));

        Vector2[] corners =
        {
            new Vector2(-size.x * 0.5f + 4, size.y * 0.5f - 4),
            new Vector2(size.x * 0.5f - 4, size.y * 0.5f - 4),
            new Vector2(-size.x * 0.5f + 4, -size.y * 0.5f + 4),
            new Vector2(size.x * 0.5f - 4, -size.y * 0.5f + 4),
        };
        foreach (var p in corners)
        {
            var d = ProtoUI.CreatePanel("FrameDot", frame.transform, p, new Vector2(8, 8), new Color(1f, 0.82f, 0.28f, 0.92f));
            d.transform.localRotation = Quaternion.Euler(0, 0, 45);
            d.raycastTarget = false;
        }
    }

    void AddLabel(RectTransform parent, string text, int size, Color col)
    {
        var t = ProtoUI.CreateText("L", parent, text, size, Vector2.zero, new Vector2(64, 64), col);
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
    }

    // ==================== 進行 ====================

    void RefreshNodes()
    {
        foreach (var n in _nodes)
        {
            bool reachable = !_moving && _current.next.Contains(n) && !n.cleared;
            n.button.interactable = reachable;
            n.marker.color = reachable ? new Color(1f, 0.82f, 0.28f, 0.42f) : Color.clear;

            // 種別の基本色を保ちつつ、クリア済みは暗く
            if (n.type == TileType.Event || n.type == TileType.SpiritTree || n.type == TileType.Shop || n.type == TileType.Contract)
            {
                if (n.icon.sprite != null)
                {
                    // 画像アイコンは色を塗らず、クリア済みのみ暗く
                    n.icon.color = n.cleared ? new Color(0.5f, 0.5f, 0.55f) : Color.white;
                }
                else
                {
                    Color baseC = n.type == TileType.Event ? new Color(0.82f, 0.58f, 0.18f, 0.96f)
                                : n.type == TileType.Shop ? new Color(0.85f, 0.65f, 0.18f, 0.96f)
                                : n.type == TileType.Contract ? new Color(0.7f, 0.3f, 0.7f, 0.96f)
                                : new Color(0.24f, 0.58f, 0.34f, 0.96f);
                    n.icon.color = n.cleared ? baseC * 0.4f : baseC;
                }
            }
            else
            {
                n.icon.color = (n.cleared && n.type != TileType.Start) ? new Color(0.4f, 0.4f, 0.45f) : Color.white;
            }
        }
        _playerIcon.anchoredPosition = _current.pos;
        _playerIcon.SetAsLastSibling();
        _moneyText.text = $"￥{_main.Money}";
        if (_waveText != null) _waveText.text = $"WAVE {_main.Wave} / {MaxWave}";
        ScrollToCurrent();
    }

    void ScrollToCurrent()
    {
        if (_scroll == null) return;
        const float vw = 1470f;
        float scrollable = _contentWidth - vw;
        if (scrollable <= 0f) { Canvas.ForceUpdateCanvases(); _scroll.horizontalNormalizedPosition = 0f; return; }
        // 現在地が左から約400pxの位置に来るようスクロール（左端では先頭表示）
        float fromLeft = _current.pos.x + _halfWidth; // 0=コンテンツ左端
        float normalized = Mathf.Clamp01((fromLeft - 400f) / scrollable);
        Canvas.ForceUpdateCanvases();
        _scroll.horizontalNormalizedPosition = normalized;
    }

    void OnNodeClicked(Node n)
    {
        if (_moving || !_current.next.Contains(n) || n.cleared) return;
        StartCoroutine(MoveTo(n));
    }

    IEnumerator MoveTo(Node n)
    {
        _moving = true; _notice.text = "";
        foreach (var node in _nodes) { node.button.interactable = false; node.marker.color = Color.clear; }

        Vector2 from = _playerIcon.anchoredPosition; float t = 0f; const float dur = 0.35f;
        while (t < dur) { t += Time.deltaTime; _playerIcon.anchoredPosition = Vector2.Lerp(from, n.pos, Mathf.SmoothStep(0, 1, t / dur)); yield return null; }
        _playerIcon.anchoredPosition = n.pos;
        _main.CurrentDepth = n.col; // このマスの深度を記録（報酬/ショップ抽選に使用）

        switch (n.type)
        {
            case TileType.Enemy:
            case TileType.MidBoss:
            case TileType.Boss:
                _engaged = n;
                _main.StartBattle(n.enemy);
                break;
            case TileType.Event:
                _main.AddMoney(_main.Cfg != null ? _main.Cfg.eventMoney : 25);
                n.cleared = true; _current = n; _moving = false;
                _notice.text = $"イベント！お金 +{(_main.Cfg != null ? _main.Cfg.eventMoney : 25)}";
                RefreshNodes();
                break;
            case TileType.SpiritTree:
                _current = n; _moving = false;
                if (!n.cleared) { _main.AwardCells(3); n.cleared = true; }   // 神聖樹で+3マス（確定・1回）
                _main.HealFull();                                            // HP全回復
                _notice.text = "神聖樹！　盤面マス +3　HP全回復";
                RefreshNodes();
                break;
            case TileType.Shop:
                _current = n; _moving = false;
                OpenShop(n);   // 店マス：カード購入
                break;
            case TileType.Contract:
                _current = n; _moving = false;
                OpenContract(n);   // 契約マス：最大HP→マスストック
                break;
        }
    }

    // バトル勝利時（ProtoMain.OnBattleWon から）
    public void OnEnemyDefeated()
    {
        if (_engaged == null) return;
        var node = _engaged; _engaged = null;
        node.cleared = true; _current = node; _moving = false;

        // 盤面マス入手：中ボス=5確定 / 雑魚=10%で1
        if (node.type == TileType.MidBoss)
        {
            _main.AwardCells(5);
            if (_notice != null) _notice.text = "中ボス撃破！　盤面マス +5";
        }
        else if (node.type == TileType.Enemy)
        {
            if (Random.value < 0.1f) { _main.AwardCells(1); if (_notice != null) _notice.text = "盤面マスを 1 入手！"; }
        }

        if (node.type == TileType.Boss)
        {
            if (_main.Wave < MaxWave) { _main.SetWave(_main.Wave + 1); AdvanceWave(); }
            else ShowClear();
        }
    }

    // 次のWaveへ（マップを最初から・敵はWaveに応じて強くなる）
    void AdvanceWave()
    {
        BuildMap();   // 次Waveは新しいランダムマップ
        if (_notice != null) _notice.text = $"WAVE {_main.Wave} 突入！";
        RefreshNodes();
    }


    // ==================== 精神樹ショップ ====================

    void OpenShop(Node treeNode)
    {
        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("Shop", _root);
        _shopOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);

        var title = ProtoUI.CreateText("ST", rt, "ショップ", 40, new Vector2(0, 380), new Vector2(600, 50));
        ProtoUI.StyleTitle(title, new Color(0.6f, 1f, 0.7f), 6f);

        var money = ProtoUI.CreateText("M", rt, "", 24, new Vector2(0, 330), new Vector2(600, 30), ProtoUI.Gold);

        // 盤面マスの所持状況（神聖樹では入場時に+3マス入手済み）
        var cellInfo = ProtoUI.CreateText("CellInfo", rt, "", 20, new Vector2(0, 250), new Vector2(600, 30), new Color(0.6f, 1f, 0.7f));

        System.Action refresh = null;

        // 購入候補
        var offers = _main.Db != null ? _main.Db.RandomCards(_main.Cfg != null ? _main.Cfg.shopOfferCount : 5,
            new HashSet<string>(_main.OwnedCardIds), treeNode.col) : new List<CardDef>();
        var offerButtons = new List<(CardDef card, Button btn, TextMeshProUGUI label, Image frame)>();
        float spacing = 260f; float startX = -(offers.Count - 1) * spacing / 2f;
        for (int i = 0; i < offers.Count; i++)
        {
            var card = offers[i];
            var frame = ProtoUI.CreatePanel($"Off_{card.id}", rt, new Vector2(startX + i * spacing, -10), new Vector2(240, 290),
                new Color(0.66f, 0.55f, 0.34f));
            var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(228, 278), new Color(0.10f, 0.08f, 0.16f));
            inner.raycastTarget = false;
            var nm = ProtoUI.CreateText("N", inner.transform, card.displayName, 18, new Vector2(0, 116), new Vector2(220, 26), Color.white);
            nm.fontStyle = FontStyles.Bold;
            ProtoUI.CreateText("K", inner.transform, $"{(card.kind == CardKind.Attack ? "攻撃" : "スキル")} / {card.Size}マス / マナ{card.ManaCost}",
                13, new Vector2(0, 90), new Vector2(220, 20), new Color(0.8f, 0.85f, 1f));
            var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 20), new Vector2(200, 110), new Color(0.05f, 0.04f, 0.10f));
            art.raycastTarget = false;
            DrawMini(art.transform, card, 13f);
            string eff = card.kind == CardKind.Attack
                ? (card.HasEffect(CardEffectType.BlinkOnUse) ? $"威力{card.power}・点滅" : $"威力 {card.power}")
                : card.description;
            ProtoUI.CreateText("D", inner.transform, eff, 13, new Vector2(0, -90), new Vector2(212, 56), new Color(0.9f, 0.92f, 1f), TextAlignmentOptions.Top).raycastTarget = false;
            var price = ProtoUI.CreateText("P", inner.transform, "", 16, new Vector2(0, -128), new Vector2(220, 24), ProtoUI.Gold);

            var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
            var c = card;
            btn.onClick.AddListener(() => { if (_main.BuyCard(c.id)) refresh(); });
            offerButtons.Add((card, btn, price, frame));
        }

        ProtoUI.CreateButton("Close", rt, "出発する", 22, new Vector2(0, -380), new Vector2(260, 60),
            new Color(0.45f, 0.3f, 0.55f), () => CloseShop(treeNode));

        refresh = () =>
        {
            money.text = $"￥{_main.Money}";
            cellInfo.text = $"マスストック: {_main.CellStock}　／　盤面 {_main.BoardCells}/{ProtoMain.MaxCells}マス";

            int price = _main.Cfg != null ? _main.Cfg.shopBuyPrice : 40;
            foreach (var o in offerButtons)
            {
                bool owned = _main.OwnsCard(o.card.id);
                bool afford = _main.Money >= price;
                o.label.text = owned ? "購入済み" : $"購入 {price}";
                o.btn.interactable = !owned && afford;
                o.frame.color = owned ? new Color(0.3f, 0.3f, 0.32f) : new Color(0.66f, 0.55f, 0.34f);
            }
        };

        refresh();
    }

    void DrawMini(Transform parent, CardDef card, float cs)
    {
        var shape = card.Shape;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var v in shape) { minX = Mathf.Min(minX, v.x); minY = Mathf.Min(minY, v.y); maxX = Mathf.Max(maxX, v.x); maxY = Mathf.Max(maxY, v.y); }
        float gap = 2f, ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.CreatePanel("M", parent, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.color).raycastTarget = false;
    }

    void CloseShop(Node treeNode)
    {
        if (_shopOverlay != null) { Destroy(_shopOverlay); _shopOverlay = null; }
        treeNode.cleared = true;
        RefreshNodes();
    }

    // ==================== 契約マス ====================
    void OpenContract(Node node)
    {
        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("Contract", _root);
        _shopOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.82f);

        var title = ProtoUI.CreateText("CT", rt, "契約", 40, new Vector2(0, 300), new Vector2(600, 50));
        ProtoUI.StyleTitle(title, new Color(0.85f, 0.6f, 1f), 8f);
        ProtoUI.CreateText("CD", rt, "最大HPを10支払うごとに マスストックを1得る", 22, new Vector2(0, 230), new Vector2(900, 30), new Color(0.92f, 0.9f, 1f));

        TextMeshProUGUI info = ProtoUI.CreateText("CInfo", rt, "", 26, new Vector2(0, 120), new Vector2(900, 40), ProtoUI.Gold);

        System.Action refresh = () =>
        {
            info.text = $"最大HP {_main.Stats.MaxHP}　／　マスストック {_main.CellStock}";
        };

        var payBtn = ProtoUI.CreateButton("Pay", rt, "最大HP -10 → マスストック +1", 22, new Vector2(0, 20), new Vector2(460, 70),
            new Color(0.55f, 0.25f, 0.6f, 0.98f), null);
        payBtn.onClick.AddListener(() =>
        {
            if (!_main.ContractTradeHpForCell() && _notice != null) _notice.text = "これ以上は最大HPを払えません";
            refresh();
        });

        ProtoUI.CreateButton("CClose", rt, "出発する", 22, new Vector2(0, -120), new Vector2(280, 60),
            new Color(0.45f, 0.3f, 0.55f), () => CloseShop(node));
        refresh();
    }

    // ==================== クリア ====================

    void ShowClear()
    {
        if (_clearOverlay != null) Destroy(_clearOverlay);
        var rt = ProtoUI.CreateFullScreen("Clear", _root);
        _clearOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);
        var t = ProtoUI.CreateText("CT", rt, "ゲームクリア！", 64, new Vector2(0, 80), new Vector2(900, 100));
        ProtoUI.StyleTitle(t, ProtoUI.Gold, 10f);
        ProtoUI.CreateText("CS", rt, "ボスを撃破した！", 24, new Vector2(0, -10), new Vector2(900, 50), new Color(0.9f, 0.9f, 1f));
        ProtoUI.CreateButton("Replay", rt, "もう一度挑戦", 24, new Vector2(0, -90), new Vector2(280, 64),
            new Color(0.35f, 0.3f, 0.55f), ResetRun);
    }

    public void ResetRun()
    {
        if (_clearOverlay != null) { Destroy(_clearOverlay); _clearOverlay = null; }
        BuildMap();      // 最初から＝マップを再生成（ランダム配置）
        RefreshNodes();
    }

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;
        if (_shopOverlay != null || _clearOverlay != null) return;
        bool menu = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame) menu = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!menu && Input.GetKeyDown(KeyCode.B)) menu = true;
#endif
        if (menu) _main.ShowMenu();
    }
}
