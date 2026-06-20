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
    Coroutine _shopMsgCo; // 店員セリフのタイプライター制御
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

        // ▼▼ デバッグ用（一時的）：左上からすぐショップへ。動作確認が終わったら削除する ▼▼
        ProtoUI.CreateGoldButton("DebugShop", _root, "ショップへ(デバッグ)", 16, new Vector2(-690, 330), new Vector2(180, 44),
            new Color(0.5f, 0.3f, 0.15f, 0.98f), () => OpenShop(new Node { col = Mathf.Max(4, _current != null ? _current.col : 4), type = TileType.Shop }));
        ProtoUI.CreateGoldButton("DebugContract", _root, "契約へ(デバッグ)", 16, new Vector2(-690, 278), new Vector2(180, 44),
            new Color(0.4f, 0.15f, 0.2f, 0.98f), () => OpenContract(new Node { col = Mathf.Max(4, _current != null ? _current.col : 4), type = TileType.Contract }));
        ProtoUI.CreateGoldButton("DebugTree", _root, "神聖樹へ(デバッグ)", 16, new Vector2(-690, 226), new Vector2(180, 44),
            new Color(0.18f, 0.4f, 0.22f, 0.98f), () => OpenSpiritTree(new Node { col = Mathf.Max(4, _current != null ? _current.col : 4), type = TileType.SpiritTree }));
        // ▲▲ デバッグ用ここまで ▲▲

        ProtoUI.CreatePanel("BottomBar", _root, new Vector2(0, -424), new Vector2(1700, 56), new Color(0.015f, 0.014f, 0.02f, 0.90f)).raycastTarget = false;
        ProtoUI.CreatePanel("BottomBarLine", _root, new Vector2(0, -395), new Vector2(1700, 2), new Color(0.95f, 0.72f, 0.26f, 0.70f)).raycastTarget = false;
        var gold = new Color(0.9f, 0.78f, 0.42f, 0.95f);
        ProtoUI.CreatePanel("MenuBtnBorder", _root, new Vector2(-705, -424), new Vector2(186, 50), gold).raycastTarget = false;
        ProtoUI.CreateButton("MenuBtn", _root, "メニュー", 18, new Vector2(-705, -424), new Vector2(178, 42),
            new Color(0.18f, 0.14f, 0.35f, 0.98f), () => _main.ShowMenu());
        // ？アイコン（チュートリアル）
        ProtoUI.CreatePanel("HelpBtnBorder", _root, new Vector2(-560, -424), new Vector2(58, 50), gold).raycastTarget = false;
        ProtoUI.CreateButton("HelpBtn", _root, "？", 22, new Vector2(-560, -424), new Vector2(50, 42),
            new Color(0.3f, 0.28f, 0.5f, 0.98f), OpenTutorial);
        // 拡大・縮小ボタン（メニュー↔？と同じ間隔31pxで等間隔配置）
        ProtoUI.CreatePanel("ZoomInBorder", _root, new Vector2(-479, -424), new Vector2(58, 50), gold).raycastTarget = false;
        ProtoUI.CreateButton("ZoomIn", _root, "＋", 24, new Vector2(-479, -424), new Vector2(50, 42),
            new Color(0.22f, 0.34f, 0.28f, 0.98f), () => SetZoom(0.15f));
        ProtoUI.CreatePanel("ZoomOutBorder", _root, new Vector2(-398, -424), new Vector2(58, 50), gold).raycastTarget = false;
        ProtoUI.CreateButton("ZoomOut", _root, "－", 24, new Vector2(-398, -424), new Vector2(50, 42),
            new Color(0.34f, 0.24f, 0.28f, 0.98f), () => SetZoom(-0.15f));
        ProtoUI.CreateText("Hint", _root,
            "Ｂ：メニューを開く　　マスを左クリック：移動　　右クリック：チュートリアル　　＋／－：拡大縮小",
            17, new Vector2(120, -424), new Vector2(940, 30), ProtoUI.Gold, TextAlignmentOptions.Center);
    }

    float _zoom = 1f;
    void SetZoom(float delta)
    {
        _zoom = Mathf.Clamp(_zoom + delta, 0.45f, 1.2f);
        if (_nodeLayer != null) _nodeLayer.localScale = new Vector3(_zoom, _zoom, 1f);
        ScrollToCurrent();
    }

    // 各マスの説明ポップアップ
    void OpenTutorial()
    {
        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("Tutorial", _root);
        _shopOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);

        // 不透明パネル（マップが透けない完全なポップアップ）
        ProtoUI.CreateFramedPanel("TBox", rt, new Vector2(0, 0), new Vector2(820, 760),
            new Color(0.07f, 0.06f, 0.12f, 1f), new Color(0.85f, 0.72f, 0.4f, 0.95f));

        var title = ProtoUI.CreateText("TT", rt, "マスの説明", 40, new Vector2(0, 330), new Vector2(700, 50));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 8f);
        ProtoUI.CreatePanel("TTLine", rt, new Vector2(0, 298), new Vector2(640, 3), new Color(0.85f, 0.72f, 0.4f, 0.9f)).raycastTarget = false;

        (Sprite, string)[] items =
        {
            (ProtoPixelArt.Slime(),        "敵マス：戦闘。倒すと10%で盤面マス+1"),
            (ProtoPixelArt.Knight(),       "中ボス：強敵。倒すと盤面マス+5"),
            (ProtoPixelArt.DragonFront(),  "ボス：各Waveのボス。倒すと次のWaveへ"),
            (ProtoPixelArt.EventPhoto(),   "？マス：イベント。お金を獲得"),
            (ProtoPixelArt.TreePhoto(),    "樹マス：神聖樹。HP全回復＋盤面マス+3か最大HP+10を選択"),
            (ProtoPixelArt.ShopPhoto(),    "店マス：ショップ。お金でカード購入"),
            (ProtoPixelArt.ContractPhoto(),"契約マス：最大HPを10払うごとに盤面マス+1"),
        };
        float y = 230f;
        var contractSp = ProtoPixelArt.ContractPhoto();
        foreach (var it in items)
        {
            // アイコン
            var iconRt = ProtoUI.CreateRect("TIco", rt);
            iconRt.anchoredPosition = new Vector2(-300, y); iconRt.sizeDelta = new Vector2(60, 60);
            var im = iconRt.gameObject.AddComponent<Image>();
            if (it.Item1 != null)
            {
                im.sprite = it.Item1;
                im.preserveAspect = it.Item1 != contractSp;   // 契約はマスに充填
            }
            else im.color = new Color(0, 0, 0, 0);
            im.raycastTarget = false;
            // 説明
            ProtoUI.CreateText("TD", rt, it.Item2, 22, new Vector2(150, y), new Vector2(640, 40), Color.white, TextAlignmentOptions.Left);
            y -= 70f;
        }

        ProtoUI.CreateGoldButton("TClose", rt, "閉じる", 24, new Vector2(0, -330), new Vector2(280, 60),
            new Color(0.45f, 0.3f, 0.55f), () => { Destroy(_shopOverlay); _shopOverlay = null; });
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
            // 中ボス列でも全レーンを中ボスにはせず、1レーンだけ中ボス＝回避ルートを残す
            int midLane = midbossCol ? Lanes[Random.Range(0, Lanes.Length)] : int.MinValue;
            var list = new List<Node>();
            foreach (int lane in Lanes)
            {
                if (midbossCol && lane == midLane) { list.Add(NewNode(col, lane, TileType.MidBoss)); continue; }
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
                if (sp != null) { n.icon.sprite = sp; n.icon.color = Color.white; } // 正方形画像＝店マスと同じ表示
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
                OpenSpiritTree(n);   // 神聖樹：HP全回復＋選択イベント
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
            // 20%で装備をドロップ（未装備のときのみ＝1つだけ所持）
            if (_main.Equipped == EquipKind.None && Random.value < 0.20f)
            {
                var drop = EquipInfo.All[Random.Range(0, EquipInfo.All.Length)];
                _main.SetEquip(drop);
                if (_notice != null) _notice.text = $"中ボス撃破！　盤面マス +5　／　{EquipInfo.Name(drop)} を入手！";
            }
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

        // 背景画像（無ければ暗幕）
        var bgImg = rt.gameObject.AddComponent<Image>();
        var bg = ProtoPixelArt.ShopEvent();
        if (bg != null) { bgImg.sprite = bg; bgImg.color = Color.white; bgImg.preserveAspect = false; }
        else bgImg.color = new Color(0, 0, 0, 0.85f);
        ProtoUI.CreatePanel("ShopVeil", rt, Vector2.zero, new Vector2(1700, 900), new Color(0, 0, 0, 0.42f)).raycastTarget = false;

        var title = ProtoUI.CreateText("ST", rt, "ショップ", 40, new Vector2(0, 380), new Vector2(600, 50));
        ProtoUI.StyleTitle(title, new Color(0.6f, 1f, 0.7f), 6f);

        // 店員のセリフ（順番に表示）
        ProtoUI.CreateFramedPanel("ShopMsgBox", rt, new Vector2(0, 300), new Vector2(900, 58),
            new Color(0.05f, 0.06f, 0.10f, 0.9f), new Color(0.6f, 0.85f, 0.55f, 0.85f));
        var keeper = ProtoUI.CreateText("ShopMsg", rt, "", 22, new Vector2(0, 300), new Vector2(870, 44), new Color(0.92f, 1f, 0.92f));
        System.Action<string> say = (s) =>
        {
            if (_shopMsgCo != null) StopCoroutine(_shopMsgCo);
            _shopMsgCo = StartCoroutine(Typewriter(keeper, s, 40f));
        };
        say("いらっしゃい、旅人さん！　さあ、どれにするんだい？");

        // 所持金（右上・枠付き）
        ProtoUI.CreateFramedPanel("MoneyBox", rt, new Vector2(640, 300), new Vector2(220, 64),
            new Color(0.06f, 0.07f, 0.04f, 0.92f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
        var money = ProtoUI.CreateText("M", rt, "", 30, new Vector2(640, 300), new Vector2(200, 44), ProtoUI.Gold);
        money.fontStyle = FontStyles.Bold;

        System.Action refresh = null;

        // 購入候補
        var offers = _main.Db != null ? _main.Db.RandomCards(_main.Cfg != null ? _main.Cfg.shopOfferCount : 5,
            new HashSet<string>(_main.OwnedCardIds), treeNode.col) : new List<CardDef>();
        ProtoUI.CreateText("SkillTitle", rt, "― スキル ―", 22, new Vector2(0, 235), new Vector2(400, 28), new Color(0.85f, 0.78f, 0.5f));
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
            ProtoUI.CreateText("K", inner.transform, $"{(CardDef.KindLabel(card.Category))} / {card.Size}マス / マナ{card.ManaCost}",
                13, new Vector2(0, 90), new Vector2(220, 20), new Color(0.8f, 0.85f, 1f));
            var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 20), new Vector2(200, 110), new Color(0.05f, 0.04f, 0.10f));
            art.raycastTarget = false;
            DrawMini(art.transform, card, 13f);
            string eff = card.kind == CardKind.Attack
                ? (card.HasEffect(CardEffectType.BlinkOnUse) ? $"威力{card.power}・点滅" : $"威力 {card.power}")
                : card.description;
            ProtoUI.CreateText("D", inner.transform, eff, 13, new Vector2(0, -100), new Vector2(212, 64), new Color(0.9f, 0.92f, 1f), TextAlignmentOptions.Top).raycastTarget = false;
            // 価格はカードの上に枠を作って表示
            float topY = -10 + 290f / 2f + 42f; // カード上端から少し離す
            ProtoUI.CreateFramedPanel($"PriceBox_{card.id}", rt, new Vector2(startX + i * spacing, topY), new Vector2(150, 46),
                new Color(0.06f, 0.07f, 0.04f, 0.95f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
            var price = ProtoUI.CreateText("P", rt, "", 20, new Vector2(startX + i * spacing, topY), new Vector2(140, 34), ProtoUI.Gold);
            price.fontStyle = FontStyles.Bold;

            var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
            var c = card;
            btn.onClick.AddListener(() =>
            {
                int pr = _main.Cfg != null ? _main.Cfg.shopBuyPrice : 40;
                if (!_main.OwnsCard(c.id) && _main.Money < pr) { say("おっと、お金が足りないようだね……"); return; }
                if (_main.BuyCard(c.id)) { say("毎度あり！　いい買い物だ。"); refresh(); }
            });
            offerButtons.Add((card, btn, price, frame));
        }

        // 装備売り場（1つだけ所持可能）
        ProtoUI.CreateText("EqTitle", rt, "― 装備 ―", 22, new Vector2(0, -210), new Vector2(400, 28), new Color(0.85f, 0.78f, 0.5f));
        var equipButtons = new List<(EquipKind kind, Button btn, TextMeshProUGUI label, Image frame)>();
        float eqSpacing = 360f, eqStartX = -(EquipInfo.All.Length - 1) * eqSpacing / 2f;
        for (int i = 0; i < EquipInfo.All.Length; i++)
        {
            var kind = EquipInfo.All[i];
            var frame = ProtoUI.CreatePanel($"Eq_{kind}", rt, new Vector2(eqStartX + i * eqSpacing, -278), new Vector2(330, 96),
                new Color(0.66f, 0.55f, 0.34f));
            var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(318, 84), new Color(0.10f, 0.08f, 0.16f));
            inner.raycastTarget = false;
            var nm = ProtoUI.CreateText("N", inner.transform, EquipInfo.Name(kind), 19, new Vector2(0, 22), new Vector2(310, 26), Color.white);
            nm.fontStyle = FontStyles.Bold;
            ProtoUI.CreateText("D", inner.transform, EquipInfo.Desc(kind), 14, new Vector2(0, -2), new Vector2(310, 22), new Color(0.85f, 0.9f, 1f)).raycastTarget = false;
            var lab = ProtoUI.CreateText("P", inner.transform, "", 16, new Vector2(0, -26), new Vector2(310, 22), ProtoUI.Gold);
            var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
            var k = kind;
            btn.onClick.AddListener(() =>
            {
                if (_main.Equipped != EquipKind.None) { say("装備は1つしか持てないよ。"); return; }
                if (_main.Money < EquipInfo.ShopPrice) { say("おっと、お金が足りないようだね……"); return; }
                if (_main.BuyEquip(k)) { say($"{EquipInfo.Name(k)}、毎度あり！"); refresh(); }
            });
            equipButtons.Add((kind, btn, lab, frame));
        }

        var closeBtn = ProtoUI.CreateGoldButton("Close", rt, "店を出る", 22, new Vector2(0, -380), new Vector2(260, 60),
            new Color(0.45f, 0.3f, 0.55f), null);
        closeBtn.onClick.AddListener(() =>
        {
            closeBtn.interactable = false;
            StartCoroutine(ShopExit(keeper, treeNode));
        });

        refresh = () =>
        {
            money.text = $"￥{_main.Money}";

            int price = _main.Cfg != null ? _main.Cfg.shopBuyPrice : 40;
            foreach (var o in offerButtons)
            {
                bool owned = _main.OwnsCard(o.card.id);
                bool afford = _main.Money >= price;
                o.label.text = owned ? "購入済み" : $"購入 {price}";
                o.btn.interactable = !owned; // 所持金不足でも押せる（押すとセリフで知らせる）
                o.frame.color = owned ? new Color(0.3f, 0.3f, 0.32f)
                    : afford ? new Color(0.66f, 0.55f, 0.34f) : new Color(0.45f, 0.38f, 0.26f);
            }
            bool hasEquip = _main.Equipped != EquipKind.None;
            foreach (var e in equipButtons)
            {
                bool isThis = _main.Equipped == e.kind;
                bool afford = _main.Money >= EquipInfo.ShopPrice;
                e.label.text = isThis ? "装備中" : hasEquip ? "装備枠がいっぱい" : $"購入 {EquipInfo.ShopPrice}";
                e.btn.interactable = !hasEquip;
                e.frame.color = isThis ? new Color(0.4f, 0.5f, 0.34f)
                    : hasEquip ? new Color(0.3f, 0.3f, 0.32f)
                    : afford ? new Color(0.66f, 0.55f, 0.34f) : new Color(0.45f, 0.38f, 0.26f);
            }
        };

        refresh();
    }

    // 店を出るときのセリフ → マップへ
    IEnumerator ShopExit(TextMeshProUGUI keeper, Node node)
    {
        if (_shopMsgCo != null) StopCoroutine(_shopMsgCo);
        yield return Typewriter(keeper, "まいどあり！　またどうぞ！", 40f);
        yield return new WaitForSeconds(0.8f);
        CloseShop(node);
    }

    void DrawMini(Transform parent, CardDef card, float cs)
    {
        var shape = card.Shape;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var v in shape) { minX = Mathf.Min(minX, v.x); minY = Mathf.Min(minY, v.y); maxX = Mathf.Max(maxX, v.x); maxY = Mathf.Max(maxY, v.y); }
        float gap = 2f, ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.CreatePanel("M", parent, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.CategoryColor).raycastTarget = false;
    }

    void CloseShop(Node treeNode)
    {
        if (_shopOverlay != null) { Destroy(_shopOverlay); _shopOverlay = null; }
        treeNode.cleared = true;
        RefreshNodes();
    }

    // ==================== 神聖樹マス ====================
    void OpenSpiritTree(Node node)
    {
        _main.HealFull(); // 立ち寄るとHP全回復

        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("SpiritTree", _root);
        _shopOverlay = rt.gameObject;

        // 背景画像（無ければ暗幕）
        var bgImg = rt.gameObject.AddComponent<Image>();
        var bg = ProtoPixelArt.ShiningTree();
        if (bg != null) { bgImg.sprite = bg; bgImg.color = Color.white; bgImg.preserveAspect = false; }
        else bgImg.color = new Color(0.04f, 0.10f, 0.06f, 0.96f);
        ProtoUI.CreatePanel("STVeil", rt, Vector2.zero, new Vector2(1700, 900), new Color(0, 0, 0, 0.32f)).raycastTarget = false;

        var title = ProtoUI.CreateText("STT", rt, "神聖樹", 44, new Vector2(0, 320), new Vector2(700, 56), new Color(0.7f, 1f, 0.78f));
        ProtoUI.StyleTitle(title, new Color(0.7f, 1f, 0.78f), 8f);

        // 木のセリフ（順番に表示）
        ProtoUI.CreateFramedPanel("STMsgBox", rt, new Vector2(0, 210), new Vector2(1080, 116),
            new Color(0.04f, 0.10f, 0.06f, 0.92f), new Color(0.5f, 0.85f, 0.55f, 0.9f));
        var msg = ProtoUI.CreateText("STMsg", rt, "",
            22, new Vector2(0, 210), new Vector2(1040, 100), new Color(0.92f, 1f, 0.92f));

        var healTxt = ProtoUI.CreateText("STHeal", rt, "HPと状態異常を回復した！", 24, new Vector2(0, 120), new Vector2(900, 34), ProtoUI.Gold);
        healTxt.gameObject.SetActive(false);

        var gold = new Color(0.85f, 0.72f, 0.4f, 0.95f);

        // 選択ボタン（金枠付き）。セリフ表示が終わるまで隠す
        var cellBorder = ProtoUI.CreatePanel("STCellBorder", rt, new Vector2(-250, 10), new Vector2(432, 96), gold);
        cellBorder.raycastTarget = false;
        var cellBtn = ProtoUI.CreateButton("STCell", rt, "盤面マス ＋3", 24, new Vector2(-250, 10), new Vector2(420, 84),
            new Color(0.24f, 0.5f, 0.32f, 0.98f), null);
        var hpBorder = ProtoUI.CreatePanel("STHpBorder", rt, new Vector2(250, 10), new Vector2(432, 96), gold);
        hpBorder.raycastTarget = false;
        var hpBtn = ProtoUI.CreateButton("STHp", rt, "最大HP ＋10", 24, new Vector2(250, 10), new Vector2(420, 84),
            new Color(0.5f, 0.28f, 0.30f, 0.98f), null);

        cellBorder.gameObject.SetActive(false); cellBtn.gameObject.SetActive(false);
        hpBorder.gameObject.SetActive(false); hpBtn.gameObject.SetActive(false);

        System.Action<string> choose = (notice) =>
        {
            cellBtn.interactable = false; hpBtn.interactable = false;
            if (_notice != null) _notice.text = notice;
            StartCoroutine(SpiritTreeFarewell(msg, node));
        };
        cellBtn.onClick.AddListener(() => { _main.AwardCells(3); choose("神聖樹の恵み：盤面マス +3"); });
        hpBtn.onClick.AddListener(() =>
        {
            if (_main.Stats != null) _main.Stats.MaxHP += 10;
            _main.HealFull();
            choose("神聖樹の恵み：最大HP +10");
        });

        // 導入のセリフを順番に表示 → 終わったらHP回復表示とボタンを出す
        StartCoroutine(Typewriter(msg,
            "おお、よくぞ来たな旅人よ。\nその傷、わしが癒やそう……ほれ、もう大丈夫じゃ。\nさらに我が恵み、ひとつだけ授けよう。どちらが望みじゃ？",
            40f, () =>
            {
                healTxt.gameObject.SetActive(true);
                cellBorder.gameObject.SetActive(true); cellBtn.gameObject.SetActive(true);
                hpBorder.gameObject.SetActive(true); hpBtn.gameObject.SetActive(true);
            }));
    }

    // 一文字ずつ表示する演出
    IEnumerator Typewriter(TextMeshProUGUI t, string full, float cps, System.Action onDone = null)
    {
        if (t == null) yield break;
        t.text = full;
        t.ForceMeshUpdate();
        int total = t.textInfo.characterCount;
        t.maxVisibleCharacters = 0;
        float shown = 0f;
        while (shown < total)
        {
            shown += cps * Time.deltaTime;
            t.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }
        t.maxVisibleCharacters = total;
        onDone?.Invoke();
    }

    // 選択後の締めのセリフ → マップへ
    IEnumerator SpiritTreeFarewell(TextMeshProUGUI msg, Node node)
    {
        yield return Typewriter(msg, "汝に神のご加護があらんことを。", 28f);
        yield return new WaitForSeconds(0.9f);
        CloseShop(node);
    }

    // ==================== 契約マス ====================
    void OpenContract(Node node)
    {
        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("Contract", _root);
        _shopOverlay = rt.gameObject;

        // 背景画像（悪魔）。無ければ暗幕
        var bgImg = rt.gameObject.AddComponent<Image>();
        var bg = ProtoPixelArt.EvilEvent();
        if (bg != null) { bgImg.sprite = bg; bgImg.color = Color.white; bgImg.preserveAspect = false; }
        else bgImg.color = new Color(0.10f, 0.02f, 0.05f, 0.96f);
        ProtoUI.CreatePanel("EvilVeil", rt, Vector2.zero, new Vector2(1700, 900), new Color(0, 0, 0, 0.45f)).raycastTarget = false;

        var title = ProtoUI.CreateText("CT", rt, "悪魔の契約", 42, new Vector2(0, 330), new Vector2(700, 52), new Color(0.95f, 0.4f, 0.45f));
        ProtoUI.StyleTitle(title, new Color(0.95f, 0.4f, 0.45f), 8f);

        // 悪魔のセリフ（順番に表示）
        ProtoUI.CreateFramedPanel("EvilMsgBox", rt, new Vector2(0, 225), new Vector2(1080, 120),
            new Color(0.10f, 0.02f, 0.05f, 0.92f), new Color(0.85f, 0.3f, 0.35f, 0.9f));
        var demon = ProtoUI.CreateText("EvilMsg", rt, "", 22, new Vector2(0, 225), new Vector2(1040, 104), new Color(1f, 0.88f, 0.9f));
        demon.lineSpacing = 24f; // 行間を少し広げる
        System.Action<string> say = (s) =>
        {
            if (_shopMsgCo != null) StopCoroutine(_shopMsgCo);
            _shopMsgCo = StartCoroutine(Typewriter(demon, s, 38f));
        };
        say("クククッ……よく来たな、欲深き者よ。\nおまえの「生命」を寄こせ。代わりに更なる力を授けてやろう。");

        TextMeshProUGUI info = ProtoUI.CreateText("CInfo", rt, "", 26, new Vector2(0, 130), new Vector2(900, 40), ProtoUI.Gold);

        System.Action refresh = () =>
        {
            info.text = $"最大HP {_main.Stats.MaxHP}　／　マスストック {_main.CellStock}";
        };

        var payBtn = ProtoUI.CreateGoldButton("Pay", rt, "最大HP -10 → マスストック +1", 22, new Vector2(0, 20), new Vector2(460, 70),
            new Color(0.55f, 0.18f, 0.22f, 0.98f), null);
        payBtn.onClick.AddListener(() =>
        {
            if (_main.ContractTradeHpForCell()) say("くくく、よい契約だ……命を糧に力は増した。");
            else say("もう差し出す命がないとはな。これ以上は無理な相談よ。");
            refresh();
        });

        var closeBtn = ProtoUI.CreateGoldButton("CClose", rt, "立ち去る", 22, new Vector2(0, -380), new Vector2(280, 60),
            new Color(0.45f, 0.2f, 0.25f, 0.98f), null);
        closeBtn.onClick.AddListener(() =>
        {
            closeBtn.interactable = false;
            StartCoroutine(EvilFarewell(demon, node));
        });
        refresh();
    }

    // 悪魔から立ち去るときのセリフ → マップへ
    IEnumerator EvilFarewell(TextMeshProUGUI demon, Node node)
    {
        if (_shopMsgCo != null) StopCoroutine(_shopMsgCo);
        yield return Typewriter(demon, "後悔するなよ。", 38f);
        yield return new WaitForSeconds(0.8f);
        CloseShop(node);
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
        ProtoUI.CreateGoldButton("Replay", rt, "もう一度挑戦", 24, new Vector2(0, -90), new Vector2(280, 64),
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
        bool menu = false, help = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame) menu = true;
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (ms != null && ms.rightButton.wasPressedThisFrame) help = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!menu && Input.GetKeyDown(KeyCode.B)) menu = true;
        if (!help && Input.GetMouseButtonDown(1)) help = true;
#endif
        if (menu) { _main.ShowMenu(); return; }
        if (help) OpenTutorial();   // 右クリックでチュートリアル
    }
}
