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

    bool _debugMode;   // デバッグモード（エディタのみ切替可。任意マスへワープ等）

    // マップ生成専用の乱数（シードから再現可能。戦闘等のグローバル乱数とは分離）
    System.Random _rng;
    int R(int max) => _rng != null ? _rng.Next(max) : Random.Range(0, max);
    float R01() => _rng != null ? (float)_rng.NextDouble() : Random.value;
    readonly List<EnemyDef> _runtimeEnemies = new List<EnemyDef>(); // 実行時生成した中ボス（リーク防止用）

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

        // 初回ガイド（3ページ）。一度見たら出さない
        if (PlayerPrefs.GetInt("guide_seen", 0) == 0) ShowFirstGuide();
    }

    // ==================== 初回ガイド ====================
    GameObject _guideGO;
    void ShowFirstGuide()
    {
        (string title, string body)[] pages =
        {
            ("ようこそ、Project M へ！",
             "マスを進んで3つのWaveを踏破するローグライクです。\n\n" +
             "【敵】戦闘　【樹】神聖樹＝回復と恵み\n【店】カード/装備の購入　【？】ランダムイベント\n\n" +
             "敵を倒して「ストックマス」を集め、盤面を広げましょう。"),
            ("このゲームの核心：スキルビルド",
             "手に入れたカード（ピース）はメニュー→ビルドで盤面に配置します。\n\n" +
             "◆ 盤面に置いたカードだけが戦闘の手札に出ます\n" +
             "◆ 大きいピースほど強力ですが、手札に出る確率は低い！\n" +
             "◆ HPが減るほど、大きいカードが出やすくなります\n" +
             "◆ 同じ種別（色）のピースを隣接させると「シナジー」で強化！"),
            ("バトルのコツ",
             "敵の頭上には「次の行動」が予告されます。\n大ダメージ予告の前に防御カードを構えましょう。\n\n" +
             "マナの範囲でカードを使い、ターン終了で敵の番。\n" +
             "点滅・ゲージ・スロットなどのミニゲームで会心を狙え！\n\n" +
             "それでは──良い旅を！"),
        };

        if (_guideGO != null) Destroy(_guideGO);
        var ov = ProtoUI.CreateFullScreen("FirstGuide", _root);
        _guideGO = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.88f);
        ProtoUI.CreateFramedPanel("GBox", ov, new Vector2(0, 30), new Vector2(940, 560),
            new Color(0.07f, 0.06f, 0.12f, 0.99f), new Color(0.85f, 0.72f, 0.4f, 0.95f));
        var title = ProtoUI.CreateText("GT", ov, "", 34, new Vector2(0, 230), new Vector2(860, 48), ProtoUI.Gold);
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 5f);
        var body = ProtoUI.CreateText("GB", ov, "", 22, new Vector2(0, 30), new Vector2(840, 330), new Color(0.94f, 0.94f, 1f));
        var pageT = ProtoUI.CreateText("GP", ov, "", 16, new Vector2(0, -180), new Vector2(200, 24), new Color(0.7f, 0.72f, 0.85f));

        int page = 0;
        Button nextBtn = null;
        System.Action render = null;
        render = () =>
        {
            title.text = pages[page].title;
            body.text = pages[page].body;
            pageT.text = $"{page + 1} / {pages.Length}";
            nextBtn.GetComponentInChildren<TextMeshProUGUI>().text = page < pages.Length - 1 ? "次へ" : "はじめる！";
        };
        nextBtn = ProtoUI.CreateGoldButton("GNext", ov, "次へ", 24, new Vector2(0, -230), new Vector2(280, 64),
            new Color(0.3f, 0.42f, 0.55f, 0.98f), () =>
            {
                if (page < pages.Length - 1) { page++; render(); }
                else { PlayerPrefs.SetInt("guide_seen", 1); PlayerPrefs.Save(); Destroy(_guideGO); _guideGO = null; }
            });
        render();
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

#if UNITY_EDITOR
        // ▼▼ デバッグモード（エディタのみ・ビルドには含まれない） ▼▼
        // 切替ボタン：プレイ⇔デバッグ。デバッグ中はショップ等の直行ボタン＋任意マスへのワープが有効
        var modeBtn = ProtoUI.CreateGoldButton("DebugToggle", _root, "モード：プレイ", 16, new Vector2(-690, 310), new Vector2(180, 44),
            new Color(0.25f, 0.25f, 0.35f, 0.98f), null);
        var modeLabel = modeBtn.GetComponentInChildren<TextMeshProUGUI>();
        var modeImg = (Image)modeBtn.targetGraphic;

        // デバッグ用ボタン群（コンテナごと表示切替）
        var dbgRoot = ProtoUI.CreateRect("DebugRoot", _root);
        dbgRoot.anchoredPosition = Vector2.zero;
        ProtoUI.CreateGoldButton("DebugShop", dbgRoot, "ショップへ", 16, new Vector2(-690, 256), new Vector2(180, 44),
            new Color(0.5f, 0.3f, 0.15f, 0.98f), () => OpenShop(new Node { col = Mathf.Max(4, _current != null ? _current.col : 4), type = TileType.Shop }, debugFree: true));
        ProtoUI.CreateGoldButton("DebugContract", dbgRoot, "契約へ", 16, new Vector2(-690, 202), new Vector2(180, 44),
            new Color(0.4f, 0.15f, 0.2f, 0.98f), () => OpenContract(new Node { col = Mathf.Max(4, _current != null ? _current.col : 4), type = TileType.Contract }));
        ProtoUI.CreateGoldButton("DebugTree", dbgRoot, "神聖樹へ", 16, new Vector2(-690, 148), new Vector2(180, 44),
            new Color(0.18f, 0.4f, 0.22f, 0.98f), () => OpenSpiritTree(new Node { col = Mathf.Max(4, _current != null ? _current.col : 4), type = TileType.SpiritTree }));
        ProtoUI.CreateText("DebugHint", dbgRoot, "任意のマスをクリックでワープ", 13, new Vector2(-690, 112), new Vector2(200, 20), new Color(1f, 0.8f, 0.5f));
        dbgRoot.gameObject.SetActive(false);

        modeBtn.onClick.AddListener(() =>
        {
            _debugMode = !_debugMode;
            dbgRoot.gameObject.SetActive(_debugMode);
            modeLabel.text = _debugMode ? "モード：デバッグ" : "モード：プレイ";
            modeImg.color = _debugMode ? new Color(0.6f, 0.35f, 0.15f, 0.98f) : new Color(0.25f, 0.25f, 0.35f, 0.98f);
            RefreshNodes();
        });
        // ▲▲ デバッグ用ここまで ▲▲
#endif

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
            (ProtoPixelArt.Slime(),        "雑魚敵マス：戦闘。倒すと確率でストックマス+1"),
            (ProtoPixelArt.Knight(),       "中ボスマス：戦闘。倒すとストックマス+5"),
            (ProtoPixelArt.DragonFront(),  "ボスマス：各Waveのボス。倒すと次のWaveへ"),
            (ProtoPixelArt.EventPhoto(),   "？マス：イベント。お金を獲得"),
            (ProtoPixelArt.TreePhoto(),    "神聖樹マス：イベント発生。"),
            (ProtoPixelArt.ShopPhoto(),    "ショップマス：お金でカードまたは装備を購入"),
            (ProtoPixelArt.ContractPhoto(),"悪魔の契約マス：イベント発生。"),
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
        _rng = new System.Random(_main.MapSeed);   // シードから決定的に生成（セーブで同じマップを再現）

        // 実行時生成した中ボス定義を破棄（リーク防止）
        foreach (var e in _runtimeEnemies) if (e != null) Destroy(e);
        _runtimeEnemies.Clear();

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
            int midLane = midbossCol ? Lanes[R(Lanes.Length)] : int.MinValue;
            var list = new List<Node>();
            foreach (int lane in Lanes)
            {
                if (midbossCol && lane == midLane) { list.Add(NewNode(col, lane, TileType.MidBoss)); continue; }
                TileType t;
                if (col == 1) t = TileType.Enemy;             // 初手は必ず戦闘
                else if (col >= 3) { float r = R01(); t = r < 0.60f ? TileType.Enemy : r < 0.82f ? TileType.Event : TileType.SpiritTree; }
                else t = R01() < 0.70f ? TileType.Enemy : TileType.Event;
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
        for (int i = shopCand.Count - 1; i > 0; i--) { int j = R(i + 1); var tmp = shopCand[i]; shopCand[i] = shopCand[j]; shopCand[j] = tmp; }
        int shops = 0;
        foreach (var n in shopCand)
        {
            if (shops >= 3) break;
            if (n.type == TileType.Shop || ConnectedToType(n, TileType.Shop)) continue;
            n.type = TileType.Shop; shops++;
        }

        // 契約マスを2つ配置（3バトル以降＝col>=4、契約どうしは隣接させない・店マスは避ける）
        var conCand = normalNodes.FindAll(n => n.col >= 4 && n.type != TileType.Shop && n.type != TileType.MidBoss);
        for (int i = conCand.Count - 1; i > 0; i--) { int j = R(i + 1); var tmp = conCand[i]; conCand[i] = conCand[j]; conCand[j] = tmp; }
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
            for (int i = evs.Count - 1; i > 0; i--) { int j = R(i + 1); var tmp = evs[i]; evs[i] = evs[j]; evs[j] = tmp; }
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
            else if (n.type == TileType.Boss) n.enemy = BossForWave(_main.Wave);
        }

        foreach (var a in _nodes)
            foreach (var b in a.next)
                DrawLine(a, b);
        foreach (var n in _nodes) BuildNodeUI(n);

        _current = start;
        _playerIcon.anchoredPosition = start.pos;
        _playerIcon.SetAsLastSibling();
    }

    // ---- セーブ/ロード：踏破状況 ----
    public void CaptureRun(List<int> cleared, out int curNode)
    {
        cleared?.Clear();
        for (int i = 0; i < _nodes.Count; i++)
            if (_nodes[i].cleared && _nodes[i].type != TileType.Start) cleared?.Add(i);
        curNode = _current != null ? _nodes.IndexOf(_current) : -1;
    }

    // セーブから復元：同じシードでマップを再生成し、踏破状況と現在地を反映
    public void RestoreRun(List<int> cleared, int curNode)
    {
        BuildMap();
        if (cleared != null)
            foreach (var idx in cleared)
                if (idx >= 0 && idx < _nodes.Count) _nodes[idx].cleared = true;
        if (curNode >= 0 && curNode < _nodes.Count)
        {
            _current = _nodes[curNode];
            _playerIcon.anchoredPosition = _current.pos;
        }
        RefreshNodes();
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

    // 中ボスを実行時生成（3種からランダム）。通常敵より頑丈・強力
    EnemyDef MakeMidBoss(int col)
    {
        var e = ScriptableObject.CreateInstance<EnemyDef>();
        _runtimeEnemies.Add(e);   // マップ再生成時に破棄（リーク防止）
        e.levelOffset = 1;
        e.moneyReward = 60;
        int hp = 220 + col * 12;
        switch (R(3))
        {
            case 0:
                e.id = "midboss_knight"; e.enemyName = "黒騎士"; e.spriteKey = EnemySpriteKey.Knight;
                e.baseHP = hp; e.minAtk = 12; e.maxAtk = 20;
                e.battleSize = new Vector2(360, 360); e.mapSize = new Vector2(90, 90);
                e.attacks = new[]
                {
                    new EnemyAttackDef { name = "斬撃", mult = 1f, hits = 1, weight = 50 },
                    new EnemyAttackDef { name = "連撃", mult = 0.7f, hits = 2, weight = 30 },
                    new EnemyAttackDef { name = "強打", mult = 1.6f, hits = 1, weight = 20 },
                };
                break;
            case 1:
                e.id = "midboss_oni"; e.enemyName = "赤鬼"; e.spriteKey = EnemySpriteKey.Oni;
                e.baseHP = hp + 60; e.minAtk = 14; e.maxAtk = 22;
                e.battleSize = new Vector2(400, 440); e.mapSize = new Vector2(94, 100);
                e.attacks = new[]
                {
                    new EnemyAttackDef { name = "金棒たたき", mult = 1f, hits = 1, weight = 45 },
                    new EnemyAttackDef { name = "鬼の乱打", mult = 0.6f, hits = 3, weight = 25 },
                    new EnemyAttackDef { name = "怒りの一撃", mult = 2f, hits = 1, weight = 18 },
                    new EnemyAttackDef { name = "睨みつけ", mult = 0f, hits = 0, weight = 12, act = EnemyActKind.PowerUp, amount = 3 },
                };
                break;
            default:
                e.id = "midboss_golem"; e.enemyName = "石の巨兵"; e.spriteKey = EnemySpriteKey.Golem;
                e.baseHP = hp + 120; e.minAtk = 11; e.maxAtk = 18;
                e.battleSize = new Vector2(440, 400); e.mapSize = new Vector2(92, 88);
                e.attacks = new[]
                {
                    new EnemyAttackDef { name = "鉄拳", mult = 1f, hits = 1, weight = 46 },
                    new EnemyAttackDef { name = "踏みつけ", mult = 1.8f, hits = 1, weight = 24 },
                    new EnemyAttackDef { name = "岩石連打", mult = 0.65f, hits = 2, weight = 18 },
                    new EnemyAttackDef { name = "エネルギー充填", mult = 0f, hits = 0, weight = 12, act = EnemyActKind.Charge },
                };
                break;
        }
        return e;
    }

    // Wave別のボス（全3体）
    EnemyDef BossForWave(int wave)
    {
        if (_main.Db == null) return null;
        string id = wave <= 1 ? "boss_goblinking" : wave == 2 ? "boss_guardian" : "dragon";
        return _main.Db.FindEnemy(id) ?? _main.Db.FindEnemy("dragon");
    }

    // 深度に応じて雑魚敵を抽選（全10種を段階的に）
    static readonly string[] EnemyEarly = { "slime", "bat", "mushroom", "goblin", "wolf" };
    static readonly string[] EnemyMid = { "ghost", "skeleton", "harpy", "golem", "mudgolem" };
    static readonly string[] EnemyLate = { "lizardman", "sandworm", "golem", "mudgolem", "oni" };
    EnemyDef EnemyForColumn(int col)
    {
        if (_main.Db == null) return null;
        float p = col / (float)MidColumns;
        var pool = p < 0.35f ? EnemyEarly : p < 0.7f ? EnemyMid : EnemyLate;
        string id = pool[R(pool.Length)];
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
            // デバッグモード中は未踏破の全マスへワープ可能
            bool reachable = _debugMode
                ? (!_moving && !n.cleared && n != _current && n.type != TileType.Start)
                : (!_moving && _current.next.Contains(n) && !n.cleared);
            n.button.interactable = reachable;
            n.marker.color = !reachable ? Color.clear
                : _debugMode && !_current.next.Contains(n) ? new Color(0.3f, 0.85f, 1f, 0.42f)   // ワープ先＝水色
                : new Color(1f, 0.82f, 0.28f, 0.42f);

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
        if (_moving || n.cleared) return;
        if (!_debugMode && !_current.next.Contains(n)) return;   // デバッグ中は隣接チェックを無視してワープ
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
                _current = n; _moving = false;
                OpenRandomEvent(n);   // 選択肢つきランダムイベント
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
                OpenContract(n);   // 契約マス：最大HP→ストックマス
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
            if (_notice != null) _notice.text = "中ボス撃破！　ストックマス +5";
            // 20%で装備をドロップ
            if (Random.value < 0.20f)
            {
                var drop = EquipInfo.All[Random.Range(0, EquipInfo.All.Length)];
                if (_main.Equipped == EquipKind.None)
                {
                    _main.SetEquip(drop);
                    if (_notice != null) _notice.text = $"中ボス撃破！　ストックマス +5　／　{EquipInfo.Name(drop)} を入手！";
                }
                else
                {
                    // すでに装備中＝持ち替え確認
                    ShowEquipSwapConfirm(drop, () => { _main.SetEquip(drop); if (_notice != null) _notice.text = $"{EquipInfo.Name(drop)} に持ち替えた！"; });
                }
            }
        }
        else if (node.type == TileType.Enemy)
        {
            if (Random.value < 0.1f) { _main.AwardCells(1); if (_notice != null) _notice.text = "ストックマスを 1 入手！"; }
        }

        if (node.type == TileType.Boss)
        {
            if (_main.Wave < MaxWave) { _main.SetWave(_main.Wave + 1); AdvanceWave(); }
            else ShowClear();
        }
        _main.AutoSaveRun();   // 戦闘勝利のたびオートセーブ
    }

    // 次のWaveへ（マップを最初から・敵はWaveに応じて強くなる）
    void AdvanceWave()
    {
        _main.NewMapSeed();   // 次Waveは新しいシードでランダムマップ
        BuildMap();
        if (_notice != null) _notice.text = $"WAVE {_main.Wave} 突入！";
        RefreshNodes();
    }


    // ==================== 精神樹ショップ ====================

    void OpenShop(Node treeNode, bool debugFree = false)
    {
        _main.PlayShopBgm();   // ショップBGM（退店でマップ曲へ戻る）
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
        bool closing = false;   // 「店を出る」押下後の購入を防ぐ

        // 購入候補（入店するたびランダムに抽選。所持済みも並ぶ＝2枚目以降は割増価格で購入可）
        // デバッグショップは深度・アンロック・レアリティ重みをすべて無視した完全ランダム
        var offers = _main.Db == null ? new List<CardDef>()
            : debugFree ? _main.Db.RandomCards(_main.Cfg != null ? _main.Cfg.shopOfferCount : 5, null, int.MaxValue, int.MaxValue, uniform: true)
            : _main.Db.RandomCards(_main.Cfg != null ? _main.Cfg.shopOfferCount : 5, null, treeNode.col, ProtoUnlocks.UnlockLevel);
        if (offers.Count == 0)
            ProtoUI.CreateText("SoldOut", rt, "本日は売り切れ……", 26, new Vector2(0, -10), new Vector2(600, 40), new Color(0.8f, 0.8f, 0.9f));
        ProtoUI.CreateText("SkillTitle", rt, "― カード ―", 22, new Vector2(0, 235), new Vector2(400, 28), new Color(0.85f, 0.78f, 0.5f));
        var offerButtons = new List<(CardDef card, Button btn, TextMeshProUGUI label, Image frame)>();
        float spacing = 260f; float startX = -(offers.Count - 1) * spacing / 2f;
        for (int i = 0; i < offers.Count; i++)
        {
            var card = offers[i];
            // レアは後光を背後に
            if (card.rarity >= 2)
            {
                var halo = ProtoUI.CreateGlow("Halo", rt, new Vector2(startX + i * spacing, -10), new Vector2(340, 390), new Color(1f, 0.82f, 0.35f, 0.5f));
                var hg = halo.gameObject.AddComponent<RareGlow>();
                hg.target = halo; hg.colA = new Color(1f, 0.8f, 0.3f, 0.2f); hg.colB = new Color(1f, 0.88f, 0.5f, 0.6f);
            }
            var frame = ProtoUI.CreatePanel($"Off_{card.id}", rt, new Vector2(startX + i * spacing, -10), new Vector2(240, 290),
                new Color(0.66f, 0.55f, 0.34f));
            var inner = ProtoUI.VGrad(ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(228, 278), new Color(0.15f, 0.13f, 0.21f)));
            inner.raycastTarget = false;
            if (card.rarity >= 2) ProtoUI.AddShine(inner, new Vector2(228, 278));   // 走査光
            var nm = ProtoUI.CreateText("N", inner.transform, card.displayName, 18, new Vector2(0, 116), new Vector2(220, 26), card.RarityColor);
            nm.fontStyle = FontStyles.Bold;
            if (card.rarity >= 2) { var rg = nm.gameObject.AddComponent<RareGlow>(); rg.target = nm; rg.colA = card.RarityColor; rg.colB = Color.white; }   // レアは光る
            ProtoUI.CreateText("K", inner.transform, $"{(CardDef.KindLabel(card.Category))} / {card.Size}マス / マナ{card.ManaCost}",
                13, new Vector2(0, 90), new Vector2(220, 20), new Color(0.8f, 0.85f, 1f));
            var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 20), new Vector2(200, 110), new Color(0.05f, 0.04f, 0.10f));
            art.raycastTarget = false;
            DrawMini(art.transform, card, 13f);
            string eff = !string.IsNullOrEmpty(card.description)
                ? (card.power > 0 ? $"威力{card.power}　{card.description}" : card.description)
                : (card.kind == CardKind.Attack ? $"威力 {card.power}" : "");
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
                if (closing) return;
                int pr = CardPrice(c, debugFree);
                if (_main.Money < pr) { say("おっと、お金が足りないようだね……"); return; }
                _main.AddMoney(-pr); _main.AddCard(c.id); say("毎度あり！　いい買い物だ。"); refresh();
            });
            offerButtons.Add((card, btn, price, frame));
        }

        // 装備売り場（ランダム3種・1つだけ所持可能）
        ProtoUI.CreateText("EqTitle", rt, "― 装備 ―", 22, new Vector2(0, -210), new Vector2(400, 28), new Color(0.85f, 0.78f, 0.5f));
        var equipOffers = new List<EquipKind>(EquipInfo.All);
        for (int i = equipOffers.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (equipOffers[i], equipOffers[j]) = (equipOffers[j], equipOffers[i]); }
        if (equipOffers.Count > 3) equipOffers.RemoveRange(3, equipOffers.Count - 3);
        int eqPrice = debugFree ? 0 : Mathf.RoundToInt(EquipInfo.ShopPrice * _main.ShopPriceMul);
        var equipButtons = new List<(EquipKind kind, Button btn, TextMeshProUGUI label, Image frame)>();
        float eqSpacing = 300f, eqStartX = -(equipOffers.Count - 1) * eqSpacing / 2f;
        for (int i = 0; i < equipOffers.Count; i++)
        {
            var kind = equipOffers[i];
            var frame = ProtoUI.CreatePanel($"Eq_{kind}", rt, new Vector2(eqStartX + i * eqSpacing, -278), new Vector2(286, 96),
                new Color(0.66f, 0.55f, 0.34f));
            var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(274, 84), new Color(0.10f, 0.08f, 0.16f));
            inner.raycastTarget = false;
            var nm = ProtoUI.CreateText("N", inner.transform, EquipInfo.Name(kind), 18, new Vector2(0, 22), new Vector2(268, 26), Color.white);
            nm.fontStyle = FontStyles.Bold;
            ProtoUI.CreateText("D", inner.transform, EquipInfo.Desc(kind), 13, new Vector2(0, -2), new Vector2(268, 22), new Color(0.85f, 0.9f, 1f)).raycastTarget = false;
            var lab = ProtoUI.CreateText("P", inner.transform, "", 16, new Vector2(0, -26), new Vector2(268, 22), ProtoUI.Gold);
            var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
            var k = kind;
            btn.onClick.AddListener(() =>
            {
                if (closing) return;
                if (_main.Equipped == k) { say("それは装備中だよ。"); return; }
                if (_main.Money < eqPrice) { say("おっと、お金が足りないようだね……"); return; }
                if (_main.Equipped == EquipKind.None)
                {
                    _main.SpendAndEquip(k, eqPrice); say($"{EquipInfo.Name(k)}、毎度あり！"); refresh();
                }
                else
                {
                    ShowEquipSwapConfirm(k, () => { _main.SpendAndEquip(k, eqPrice); say($"{EquipInfo.Name(k)}、毎度あり！"); refresh(); });
                }
            });
            equipButtons.Add((kind, btn, lab, frame));
        }

        // カード売却（在庫のみ・購入価格の半額）
        ProtoUI.CreateGoldButton("SellBtn", rt, "カードを売る", 20, new Vector2(-330, -380), new Vector2(240, 60),
            new Color(0.5f, 0.4f, 0.2f, 0.98f), () => { if (!closing) ShowSellPicker(say, refresh); });

        // 呪いの浄化（盤面の呪いマスをすべて通常マスに戻す）
        int purifyPrice = debugFree ? 0 : Mathf.RoundToInt(GameBalance.CursePurifyPrice * _main.ShopPriceMul);
        ProtoUI.CreateGoldButton("PurifyBtn", rt, $"呪いを浄化する（{purifyPrice}コイン）", 18, new Vector2(330, -380), new Vector2(300, 60),
            new Color(0.35f, 0.25f, 0.5f, 0.98f), () =>
            {
                if (closing) return;
                int n = _main.Panel.CountKind(CellKind.Curse);
                if (n == 0) { say("おや、あんたの盤面に呪いは見当たらないよ。"); return; }
                if (_main.Money < purifyPrice) { say("おっと、お金が足りないようだね……"); return; }
                _main.AddMoney(-purifyPrice);
                _main.Panel.ClearKind(CellKind.Curse);
                say($"呪いマス{n}個を清めておいたよ。もう安心だね。");
                refresh();
            });

        // デバッグ：全カードから選んで無料入手
        if (debugFree)
            ProtoUI.CreateGoldButton("DbgPick", rt, "カードを選んで入手(デバッグ)", 16, new Vector2(-600, 380), new Vector2(300, 48),
                new Color(0.5f, 0.3f, 0.15f, 0.98f), () => { if (!closing) ShowDebugCardPicker(say); });

        var closeBtn = ProtoUI.CreateGoldButton("Close", rt, "店を出る", 22, new Vector2(0, -380), new Vector2(260, 60),
            new Color(0.45f, 0.3f, 0.55f), null);
        closeBtn.onClick.AddListener(() =>
        {
            if (closing) return;
            closing = true;
            closeBtn.interactable = false;
            foreach (var o in offerButtons) o.btn.interactable = false;   // 退店演出中の購入を防ぐ
            foreach (var e in equipButtons) e.btn.interactable = false;
            StartCoroutine(ShopExit(keeper, treeNode));
        });

        refresh = () =>
        {
            money.text = $"￥{_main.Money}";

            foreach (var o in offerButtons)
            {
                bool owned = _main.OwnsCard(o.card.id);
                int price = CardPrice(o.card, debugFree);
                bool afford = _main.Money >= price;
                o.label.text = price == 0 ? "無料" : owned ? $"追加購入 {price}" : $"購入 {price}";
                o.btn.interactable = !closing; // 複数枚購入可（所持金不足はセリフで知らせる）
                o.frame.color = afford ? new Color(0.66f, 0.55f, 0.34f) : new Color(0.45f, 0.38f, 0.26f);
            }
            foreach (var e in equipButtons)
            {
                bool isThis = _main.Equipped == e.kind;
                bool afford = _main.Money >= eqPrice;
                e.label.text = isThis ? "装備中" : eqPrice == 0 ? "無料" : $"購入 {eqPrice}";
                e.btn.interactable = !isThis; // 装備中以外は押せる（所持中は持ち替え確認）
                e.frame.color = isThis ? new Color(0.4f, 0.5f, 0.34f)
                    : afford ? new Color(0.66f, 0.55f, 0.34f) : new Color(0.45f, 0.38f, 0.26f);
            }
        };

        refresh();
    }

    // 成長させるカードを選ぶピッカー
    GameObject _growPicker;
    void ShowGrowPicker(System.Action<CardDef> onPick)
    {
        if (_growPicker != null) Destroy(_growPicker);
        var ov = ProtoUI.CreateFullScreen("GrowPicker", _root);
        _growPicker = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);

        ProtoUI.CreateFramedPanel("GPBox", ov, Vector2.zero, new Vector2(1000, 620),
            new Color(0.07f, 0.09f, 0.07f, 0.98f), new Color(0.5f, 0.85f, 0.55f, 0.9f));
        var t = ProtoUI.CreateText("GPT", ov, "成長させるカードを選ぶ", 30, new Vector2(0, 250), new Vector2(900, 44), new Color(0.8f, 1f, 0.85f));
        ProtoUI.StyleTitle(t, new Color(0.8f, 1f, 0.85f), 5f);

        // 配置済みも含め、所持しているカード種別すべてを対象にする
        var owned = new List<CardDef>();
        foreach (var id in _main.OwnedCardIds) { var c = _main.Db != null ? _main.Db.FindCard(id) : null; if (c != null) owned.Add(c); }
        int perRow = 4; float cw = 224f, ch = 150f, gx = 8f, gy = 12f;
        float startX = -(perRow - 1) * (cw + gx) / 2f, startY = 150f;
        for (int i = 0; i < owned.Count; i++)
        {
            var card = owned[i];
            int r = i / perRow, c = i % perRow;
            var pos = new Vector2(startX + c * (cw + gx), startY - r * (ch + gy));
            var frame = ProtoUI.CreatePanel($"GP_{card.id}", ov, pos, new Vector2(cw, ch), new Color(0.66f, 0.55f, 0.34f));
            var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(cw - 10, ch - 10), new Color(0.10f, 0.08f, 0.16f));
            inner.raycastTarget = false;
            var nm = ProtoUI.CreateText("N", inner.transform, card.displayName, 17, new Vector2(0, 58), new Vector2(cw - 16, 24), Color.white);
            nm.fontStyle = FontStyles.Bold; nm.enableAutoSizing = true; nm.fontSizeMin = 11; nm.fontSizeMax = 17; nm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 8), new Vector2(cw - 30, 56), new Color(0.05f, 0.04f, 0.10f));
            art.raycastTarget = false; DrawMini(art.transform, card, 11f);
            ProtoUI.CreateText("K", inner.transform, $"{CardDef.KindLabel(card.Category)} / {card.Size}マス", 12, new Vector2(0, -62), new Vector2(cw - 16, 18), new Color(0.8f, 0.85f, 1f)).raycastTarget = false;
            var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
            var cd = card;
            btn.onClick.AddListener(() => { Destroy(_growPicker); _growPicker = null; onPick?.Invoke(cd); });
        }

        ProtoUI.CreateGoldButton("GPCancel", ov, "やめる", 22, new Vector2(0, -250), new Vector2(240, 60),
            new Color(0.45f, 0.3f, 0.4f, 0.98f), () => { Destroy(_growPicker); _growPicker = null; });
    }

    // 装備の持ち替え確認ポップアップ
    GameObject _equipConfirm;
    void ShowEquipSwapConfirm(EquipKind newEquip, System.Action onYes)
    {
        if (_equipConfirm != null) Destroy(_equipConfirm);
        var ov = ProtoUI.CreateFullScreen("EquipSwap", _root);
        _equipConfirm = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);

        ProtoUI.CreateFramedPanel("ESBox", ov, Vector2.zero, new Vector2(720, 320),
            new Color(0.10f, 0.08f, 0.16f, 0.98f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
        ProtoUI.CreateText("ESMsg", ov, "すでに装備を持っています。\n新しい装備に持ち替えますか？", 26, new Vector2(0, 80), new Vector2(660, 90), Color.white);
        ProtoUI.CreateText("ESInfo", ov, $"現在：{EquipInfo.Name(_main.Equipped)}　→　{EquipInfo.Name(newEquip)}", 20, new Vector2(0, 0), new Vector2(660, 30), new Color(0.85f, 0.9f, 1f));
        ProtoUI.CreateGoldButton("ESYes", ov, "持ち替える", 22, new Vector2(-150, -100), new Vector2(240, 64),
            new Color(0.30f, 0.45f, 0.32f, 0.98f), () => { Destroy(_equipConfirm); _equipConfirm = null; onYes?.Invoke(); });
        ProtoUI.CreateGoldButton("ESNo", ov, "やめる", 22, new Vector2(150, -100), new Vector2(240, 64),
            new Color(0.45f, 0.25f, 0.25f, 0.98f), () => { Destroy(_equipConfirm); _equipConfirm = null; });
    }

    // ==================== ？マス：選択肢つきランダムイベント（10種） ====================
    void OpenRandomEvent(Node node)
    {
        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("RandomEvent", _root);
        _shopOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.88f);

        ProtoUI.CreateFramedPanel("EvBox", rt, new Vector2(0, 40), new Vector2(900, 560),
            new Color(0.08f, 0.07f, 0.12f, 0.98f), new Color(0.82f, 0.58f, 0.18f, 0.92f));

        int id = Random.Range(0, 10);
        string title = "", desc = "";
        var choices = new List<(string label, System.Action act)>();
        var result = ProtoUI.CreateText("EvResult", rt, "", 22, new Vector2(0, -120), new Vector2(820, 60), ProtoUI.Gold);

        // 結果表示→少し待ってマップへ
        System.Action<string> finish = (msg) =>
        {
            result.text = msg;
            foreach (Transform c in rt) if (c.name.StartsWith("EvChoice")) Destroy(c.gameObject);
            StartCoroutine(EventClose(node));
        };

        switch (id)
        {
            case 0:
                title = "迷子の商人"; desc = "「た、助かった…お礼に安く売るよ！」\n回復薬を25コインで売ってくれるようだ。";
                choices.Add(("買う（25コイン→HP30回復）", () => {
                    if (_main.Money < 25) { finish("お金が足りなかった……"); return; }
                    _main.AddMoney(-25); _main.SetCurrentHP(_main.CurrentHP + 30); finish("HPが30回復した！"); }));
                choices.Add(("断る", () => finish("商人は去っていった。")));
                break;
            case 1:
                title = "古びた宝箱"; desc = "苔むした宝箱がぽつんと置かれている。\n罠の気配もするが……";
                choices.Add(("開ける（50%: コイン+60 / 50%: 罠でHP-10）", () => {
                    if (Random.value < 0.5f) { _main.AddMoney(60); finish("宝箱にはコインが詰まっていた！ +60"); }
                    else { _main.SetCurrentHP(_main.CurrentHP - 10); finish("罠だ！ 毒針が刺さった……HP-10"); } }));
                choices.Add(("開けない", () => finish("触らぬ神に祟りなし。")));
                break;
            case 2:
                title = "謎の泉"; desc = "淡く光る泉が湧いている。\n飲めば力が湧きそうだが、少し嫌な匂いもする。";
                choices.Add(("飲む（50%: 全回復 / 50%: 最大HP-5）", () => {
                    if (Random.value < 0.5f) { _main.HealFull(); finish("体中に力がみなぎる！ HP全回復！"); }
                    else { if (_main.Stats != null) _main.Stats.MaxHP = Mathf.Max(10, _main.Stats.MaxHP - 5); _main.SetCurrentHP(_main.CurrentHP); finish("苦い……！ 最大HP-5"); } }));
                choices.Add(("手を清める（HP10回復）", () => { _main.SetCurrentHP(_main.CurrentHP + 10); finish("気分が晴れた。HP+10"); }));
                choices.Add(("立ち去る", () => finish("泉には近寄らなかった。")));
                break;
            case 3:
                title = "行き倒れの冒険者"; desc = "傷ついた冒険者が倒れている。\n「た、たすけて……お礼は、するから……」";
                choices.Add(("手当てする（HP-10→カードを1枚もらう）", () => {
                    var pool = _main.Db != null ? _main.Db.RandomCards(1, null, node.col, ProtoUnlocks.UnlockLevel) : null;
                    _main.SetCurrentHP(_main.CurrentHP - 10);
                    if (pool != null && pool.Count > 0) { _main.AddCard(pool[0].id); finish($"お礼に「{pool[0].displayName}」をもらった！"); }
                    else finish("お礼をもらえなかったが、良いことをした気分だ。"); }));
                choices.Add(("見なかったことにする", () => finish("……先を急ごう。")));
                break;
            case 4:
                title = "石像の祠"; desc = "古い祠に、欠けた石像が祀られている。\n供物を捧げれば加護がありそうだ。";
                choices.Add(("30コインを供える（最大HP+5）", () => {
                    if (_main.Money < 30) { finish("供えるお金がなかった……"); return; }
                    _main.AddMoney(-30); if (_main.Stats != null) _main.Stats.MaxHP += 5; _main.SetCurrentHP(_main.CurrentHP + 5); finish("温かい光に包まれた。最大HP+5！"); }));
                choices.Add(("石像を壊す（ストックマス+1 / 50%でHP-15）", () => {
                    _main.AwardCells(1);
                    if (Random.value < 0.5f) { _main.SetCurrentHP(_main.CurrentHP - 15); finish("祟りだ！ HP-15……だがストックマス+1"); }
                    else finish("石像の中から輝くマスが出てきた。ストックマス+1"); }));
                break;
            case 5:
                title = "怪しい賭博師"; desc = "「へっへっへ、旅人さん。ちょいと勝負しないかい？」\nコイントスで勝てば倍返しだという。";
                choices.Add(("30コイン賭ける（50%: +60 / 50%: 没収）", () => {
                    if (_main.Money < 30) { finish("賭け金が足りなかった……"); return; }
                    _main.AddMoney(-30);
                    if (Random.value < 0.5f) { _main.AddMoney(60); finish("表だ！ 60コインせしめた！"); }
                    else finish("裏だ……30コインを失った。"); }));
                choices.Add(("断る", () => finish("「チッ、つまらないねぇ」")));
                break;
            case 6:
                title = "廃品置き場"; desc = "壊れた武具や道具が山積みになっている。\n掘れば何か出てきそうだ。";
                choices.Add(("漁ってみる", () => {
                    float r = Random.value;
                    if (r < 0.4f) { _main.AddMoney(20); finish("小銭入れを見つけた！ +20コイン"); }
                    else if (r < 0.7f) { _main.AwardCells(1); finish("使えるマスが埋まっていた！ ストックマス+1"); }
                    else { _main.SetCurrentHP(_main.CurrentHP - 5); finish("錆びた釘を踏んだ……HP-5"); } }));
                choices.Add(("立ち去る", () => finish("ガラクタに用はない。")));
                break;
            case 7:
                title = "魔法の砥石"; desc = "台座に光る砥石が置かれている。\nカードを一枚、研ぎ澄ませられそうだ。";
                choices.Add(("カードを1枚成長させる", () => {
                    if (_main.OwnedCardIds.Count == 0) { finish("成長できるカードがない……"); return; }
                    ShowGrowPicker(card => { string nm = card.displayName; _main.GrowCard(card.id); finish($"「{nm}＋」に成長した！"); }); }));
                choices.Add(("立ち去る", () => finish("砥石は静かに光を失った。")));
                break;
            case 8:
                title = "呪われた像"; desc = "黄金の像が禍々しい光を放っている。\n持ち帰れば大金になるが、呪われそうだ……";
                choices.Add(("持ち帰る（コイン+80 / 最大HP-10）", () => {
                    _main.AddMoney(80);
                    if (_main.Stats != null) _main.Stats.MaxHP = Mathf.Max(10, _main.Stats.MaxHP - 10);
                    _main.SetCurrentHP(_main.CurrentHP);
                    finish("+80コイン！ しかし体が重い……最大HP-10"); }));
                choices.Add(("立ち去る", () => finish("欲は身を滅ぼす。賢明な判断だ。")));
                break;
            default:
                title = "旅の吟遊詩人"; desc = "焚き火のそばで詩人が歌っている。\n「一曲どうだい？ 心が安らぐよ」";
                choices.Add(("20コインで聴く（HP20回復）", () => {
                    if (_main.Money < 20) { finish("お金が足りなかった……"); return; }
                    _main.AddMoney(-20); _main.SetCurrentHP(_main.CurrentHP + 20); finish("美しい歌声に癒やされた。HP+20"); }));
                choices.Add(("そっと通り過ぎる", () => finish("歌声が遠ざかっていく……")));
                break;
        }

        var tt = ProtoUI.CreateText("EvTitle", rt, title, 36, new Vector2(0, 250), new Vector2(800, 50), new Color(1f, 0.85f, 0.5f));
        ProtoUI.StyleTitle(tt, new Color(1f, 0.85f, 0.5f), 6f);
        ProtoUI.CreateText("EvDesc", rt, desc, 22, new Vector2(0, 150), new Vector2(820, 110), new Color(0.94f, 0.93f, 1f));

        for (int i = 0; i < choices.Count; i++)
        {
            var ch = choices[i];
            ProtoUI.CreateGoldButton($"EvChoice{i}", rt, ch.label, 20, new Vector2(0, 40 - i * 88), new Vector2(620, 70),
                new Color(0.3f, 0.28f, 0.45f, 0.98f), () => ch.act());
        }
    }

    IEnumerator EventClose(Node node)
    {
        yield return new WaitForSeconds(1.4f);
        CloseShop(node);   // cleared化＋オートセーブ込み
    }

    // デバッグ：全カードから選んで無料入手するピッカー（何枚でも取れる）
    GameObject _dbgPickGO;
    void ShowDebugCardPicker(System.Action<string> say)
    {
        if (_dbgPickGO != null) { Destroy(_dbgPickGO); _dbgPickGO = null; return; }
        var ov = ProtoUI.CreateFullScreen("DbgCardPicker", _root);
        _dbgPickGO = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.05f, 0.96f);

        var t = ProtoUI.CreateText("DPT", ov, "カードを選んで入手（デバッグ・無料）", 28, new Vector2(0, 400), new Vector2(800, 42), new Color(1f, 0.8f, 0.5f));
        ProtoUI.StyleTitle(t, new Color(1f, 0.8f, 0.5f), 4f);

        // スクロールリスト（全カード・深度/アンロック無視）
        var viewport = ProtoUI.CreateRect("DPView", ov);
        viewport.anchoredPosition = new Vector2(0, -20);
        viewport.sizeDelta = new Vector2(1100, 660);
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.3f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var sr = viewport.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.viewport = viewport;
        sr.scrollSensitivity = 30f; sr.movementType = ScrollRect.MovementType.Clamped;
        var content = ProtoUI.CreateRect("DPContent", viewport);
        content.anchorMin = new Vector2(0.5f, 1f); content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        sr.content = content;

        var all = new List<CardDef>();
        if (_main.Db != null) foreach (var c in _main.Db.cards) if (c != null) all.Add(c);
        all.Sort((a, b) => a.rarity != b.rarity ? a.rarity.CompareTo(b.rarity) : a.Size.CompareTo(b.Size));

        const int perRow = 4;
        float cw = 250f, chh = 130f, gx = 12f, gy = 10f;
        float startX = -(perRow - 1) * (cw + gx) / 2f;
        for (int i = 0; i < all.Count; i++)
        {
            var card = all[i];
            int r = i / perRow, col = i % perRow;
            var pos = new Vector2(startX + col * (cw + gx), -16f - chh / 2f - r * (chh + gy));
            var frame = ProtoUI.CreatePanel($"DP_{card.id}", content, pos, new Vector2(cw, chh), new Color(0.5f, 0.35f, 0.2f));
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1f);
            var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(cw - 8, chh - 8), new Color(0.09f, 0.08f, 0.14f));
            inner.raycastTarget = false;

            var nm = ProtoUI.CreateText("N", inner.transform, $"{card.displayName}（×{_main.OwnedCount(card.id)}）", 16,
                new Vector2(0, 44), new Vector2(cw - 16, 24), card.RarityColor);
            nm.fontStyle = FontStyles.Bold; nm.enableAutoSizing = true; nm.fontSizeMin = 10; nm.fontSizeMax = 16;
            nm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            ProtoUI.CreateText("K", inner.transform, $"{card.RarityLabel} / {card.Size}マス / マナ{card.ManaCost}", 12,
                new Vector2(0, 22), new Vector2(cw - 16, 18), new Color(0.8f, 0.85f, 1f));
            var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, -18), new Vector2(cw - 60, 46), new Color(0.04f, 0.04f, 0.09f));
            art.raycastTarget = false; DrawMini(art.transform, card, 7f);

            var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
            var cd = card; var label = nm;
            btn.onClick.AddListener(() =>
            {
                _main.AddCard(cd.id);
                label.text = $"{cd.displayName}（×{_main.OwnedCount(cd.id)}）";
                say?.Invoke($"デバッグ入手：{cd.displayName}");
            });
        }
        int rows = Mathf.CeilToInt(all.Count / (float)perRow);
        content.sizeDelta = new Vector2(1080, 32f + rows * (chh + gy));

        ProtoUI.CreateGoldButton("DPClose", ov, "閉じる", 22, new Vector2(0, -400), new Vector2(240, 56),
            new Color(0.45f, 0.3f, 0.4f, 0.98f), () => { Destroy(_dbgPickGO); _dbgPickGO = null; });
    }

    // カード売却ピッカー（在庫があるカードのみ。売値＝基本価格の半額）
    GameObject _sellPicker;
    void ShowSellPicker(System.Action<string> say, System.Action refreshShop)
    {
        if (_sellPicker != null) Destroy(_sellPicker);
        var ov = ProtoUI.CreateFullScreen("SellPicker", _root);
        _sellPicker = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.82f);

        int sellPrice = Mathf.Max(1, (_main.Cfg != null ? _main.Cfg.shopBuyPrice : 40) / 2);
        var t = ProtoUI.CreateText("SPT", ov, $"売るカードを選ぶ（1枚 {sellPrice}コイン）", 28, new Vector2(0, 388), new Vector2(900, 42), ProtoUI.Gold);
        ProtoUI.StyleTitle(t, ProtoUI.Gold, 5f);

        // スクロールリスト（カードが増えてもはみ出さない）
        var viewport = ProtoUI.CreateRect("SPView", ov);
        viewport.anchoredPosition = new Vector2(0, -10);
        viewport.sizeDelta = new Vector2(1000, 640);
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.35f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var srv = viewport.gameObject.AddComponent<ScrollRect>();
        srv.horizontal = false; srv.vertical = true; srv.viewport = viewport;
        srv.scrollSensitivity = 30f; srv.movementType = ScrollRect.MovementType.Clamped;
        var listRoot = ProtoUI.CreateRect("SPList", viewport);
        listRoot.anchorMin = new Vector2(0.5f, 1f); listRoot.anchorMax = new Vector2(0.5f, 1f);
        listRoot.pivot = new Vector2(0.5f, 1f); listRoot.anchoredPosition = Vector2.zero;
        srv.content = listRoot;

        System.Action rebuild = null;
        rebuild = () =>
        {
            foreach (Transform c in listRoot) Destroy(c.gameObject);
            var owned = _main.OwnedCards();   // 在庫が1以上あるカード
            if (owned.Count == 0)
                ProtoUI.CreateText("SPEmpty", listRoot, "売れるカードがない（配置中のカードは売れません）", 20, new Vector2(0, -80), new Vector2(800, 30), new Color(0.8f, 0.8f, 0.9f));
            int perRow = 4; float cw = 224f, ch = 150f, gx = 8f, gy = 12f;
            float startX = -(perRow - 1) * (cw + gx) / 2f;
            for (int i = 0; i < owned.Count; i++)
            {
                var card = owned[i];
                int r = i / perRow, c2 = i % perRow;
                var pos = new Vector2(startX + c2 * (cw + gx), -16f - ch / 2f - r * (ch + gy));
                var frame = ProtoUI.CreatePanel($"SP_{card.id}", listRoot, pos, new Vector2(cw, ch), new Color(0.66f, 0.55f, 0.34f));
                var frtSp = (RectTransform)frame.transform;
                frtSp.anchorMin = frtSp.anchorMax = new Vector2(0.5f, 1f);   // 上端基準
                var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(cw - 10, ch - 10), new Color(0.10f, 0.08f, 0.16f));
                inner.raycastTarget = false;
                var nm = ProtoUI.CreateText("N", inner.transform, $"{card.displayName} ×{_main.OwnedCount(card.id)}", 16, new Vector2(0, 58), new Vector2(cw - 16, 24), card.RarityColor);
                nm.fontStyle = FontStyles.Bold; nm.enableAutoSizing = true; nm.fontSizeMin = 11; nm.fontSizeMax = 16; nm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 8), new Vector2(cw - 30, 56), new Color(0.05f, 0.04f, 0.10f));
                art.raycastTarget = false; DrawMini(art.transform, card, 11f);
                ProtoUI.CreateText("P", inner.transform, $"売却 {sellPrice}", 14, new Vector2(0, -58), new Vector2(cw - 16, 20), ProtoUI.Gold).raycastTarget = false;
                var btn = frame.gameObject.AddComponent<Button>(); btn.targetGraphic = frame;
                var cd = card;
                btn.onClick.AddListener(() =>
                {
                    if (_main.ConsumeCard(cd.id))   // 在庫を1減らして売却
                    {
                        _main.AddMoney(sellPrice);
                        say?.Invoke($"{cd.displayName}、買い取ったよ！");
                        refreshShop?.Invoke();
                        rebuild();
                    }
                });
            }
            int rows = Mathf.CeilToInt(owned.Count / (float)perRow);
            listRoot.sizeDelta = new Vector2(980, 32f + rows * (ch + gy));   // スクロール範囲を更新
        };
        rebuild();

        ProtoUI.CreateGoldButton("SPClose", ov, "閉じる", 22, new Vector2(0, -388), new Vector2(240, 58),
            new Color(0.45f, 0.3f, 0.4f, 0.98f), () => { Destroy(_sellPicker); _sellPicker = null; });
    }

    // カード価格：基本価格×アセンション補正。所持済み（2枚目以降）は1.5倍
    int CardPrice(CardDef c, bool debugFree)
    {
        if (debugFree) return 0;
        float pr = (_main.Cfg != null ? _main.Cfg.shopBuyPrice : 40) * _main.ShopPriceMul;
        if (_main.OwnsCard(c.id)) pr *= 1.5f;
        return Mathf.RoundToInt(pr);
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
            ProtoUI.Bevel(ProtoUI.CreatePanel("M", parent, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.CategoryColor)).raycastTarget = false;
    }

    void CloseShop(Node treeNode)
    {
        if (_shopOverlay != null) { Destroy(_shopOverlay); _shopOverlay = null; }
        if (_dbgPickGO != null) { Destroy(_dbgPickGO); _dbgPickGO = null; }
        if (_sellPicker != null) { Destroy(_sellPicker); _sellPicker = null; }
        treeNode.cleared = true;
        _main.PlayMapBgm(0);   // イベントBGMからマップ曲へ戻す
        RefreshNodes();
        _main.AutoSaveRun();
    }

    // ==================== 神聖樹マス ====================
    void OpenSpiritTree(Node node)
    {
        _main.PlayTreeBgm();   // 神聖樹BGM
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

        var healTxt = ProtoUI.CreateText("STHeal", rt, "HPと状態異常を回復した！", 24, new Vector2(0, 120), new Vector2(900, 34), Color.white);
        healTxt.gameObject.SetActive(false);

        var gold = new Color(0.85f, 0.72f, 0.4f, 0.95f);

        // 選択ボタン（金枠付き）。セリフ表示が終わるまで隠す
        var cellBorder = ProtoUI.CreatePanel("STCellBorder", rt, new Vector2(-250, 10), new Vector2(432, 96), gold);
        cellBorder.raycastTarget = false;
        var cellBtn = ProtoUI.CreateButton("STCell", rt, "ストックマス ＋3", 24, new Vector2(-250, 10), new Vector2(420, 84),
            new Color(0.24f, 0.5f, 0.32f, 0.98f), null);
        var hpBorder = ProtoUI.CreatePanel("STHpBorder", rt, new Vector2(250, 10), new Vector2(432, 96), gold);
        hpBorder.raycastTarget = false;
        var hpBtn = ProtoUI.CreateButton("STHp", rt, "最大HP ＋10", 24, new Vector2(250, 10), new Vector2(420, 84),
            new Color(0.5f, 0.28f, 0.30f, 0.98f), null);

        // カードを1枚ランダムでもらう（左下）
        var getBorder = ProtoUI.CreatePanel("STGetBorder", rt, new Vector2(-250, -130), new Vector2(432, 96), gold);
        getBorder.raycastTarget = false;
        var getBtn = ProtoUI.CreateButton("STGet", rt, "カードを1枚ランダムで入手", 18, new Vector2(-250, -130), new Vector2(420, 84),
            new Color(0.32f, 0.5f, 0.4f, 0.98f), null);

        // 所持カードを1枚成長（右下）
        var growBorder = ProtoUI.CreatePanel("STGrowBorder", rt, new Vector2(250, -130), new Vector2(432, 96), gold);
        growBorder.raycastTarget = false;
        var growBtn = ProtoUI.CreateButton("STGrow", rt, "カードを1枚 成長（効果+20%）", 18, new Vector2(250, -130), new Vector2(420, 84),
            new Color(0.3f, 0.42f, 0.55f, 0.98f), null);

        // 何も受け取らずに立ち去る（金枠なし）
        var leaveBtn = ProtoUI.CreateButton("STLeave", rt, "何も受け取らず立ち去る", 20, new Vector2(0, -245), new Vector2(280, 60),
            new Color(0.4f, 0.34f, 0.5f, 0.98f), null);

        cellBorder.gameObject.SetActive(false); cellBtn.gameObject.SetActive(false);
        hpBorder.gameObject.SetActive(false); hpBtn.gameObject.SetActive(false);
        getBorder.gameObject.SetActive(false); getBtn.gameObject.SetActive(false);
        growBorder.gameObject.SetActive(false); growBtn.gameObject.SetActive(false);
        leaveBtn.gameObject.SetActive(false);

        System.Action<string> choose = (notice) =>
        {
            cellBtn.interactable = false; hpBtn.interactable = false; getBtn.interactable = false; growBtn.interactable = false; leaveBtn.interactable = false;
            if (_notice != null) _notice.text = notice;
            StartCoroutine(SpiritTreeFarewell(msg, node));
        };
        getBtn.onClick.AddListener(() =>
        {
            var pool = _main.Db != null ? _main.Db.RandomCards(1, new HashSet<string>(_main.OwnedCardIds), node.col, ProtoUnlocks.UnlockLevel) : null;
            if (pool == null || pool.Count == 0) { if (_notice != null) _notice.text = "もらえる新しいカードがない…"; return; }
            var got = pool[0];
            _main.AddCard(got.id);
            healTxt.text = $"「{got.displayName}」を手に入れた！"; // 入手したカードを画面に表示
            choose($"神聖樹の恵み：{got.displayName} を入手！");
        });
        cellBtn.onClick.AddListener(() => { _main.AwardCells(3); choose("神聖樹の恵み：ストックマス +3"); });
        hpBtn.onClick.AddListener(() =>
        {
            if (_main.Stats != null) _main.Stats.MaxHP += 10;
            _main.HealFull();
            choose("神聖樹の恵み：最大HP +10");
        });
        growBtn.onClick.AddListener(() =>
        {
            if (_main.OwnedCardIds.Count == 0) { if (_notice != null) _notice.text = "成長できるカードがない…"; return; }
            ShowGrowPicker(card =>
            {
                string nm = card.displayName;
                _main.GrowCard(card.id);
                choose($"神聖樹の恵み：{nm}＋ に成長！");
            });
        });
        leaveBtn.onClick.AddListener(() => choose("神聖樹をあとにした。"));

        // 導入のセリフを順番に表示 → 終わったらHP回復表示とボタンを出す
        StartCoroutine(Typewriter(msg,
            "おお、よくぞ来たな旅人よ。\nその傷、わしが癒やそう……ほれ、もう大丈夫じゃ。\nさらに我が恵み、ひとつだけ授けよう。どちらが望みじゃ？",
            40f, () =>
            {
                healTxt.gameObject.SetActive(true);
                cellBorder.gameObject.SetActive(true); cellBtn.gameObject.SetActive(true);
                hpBorder.gameObject.SetActive(true); hpBtn.gameObject.SetActive(true);
                getBorder.gameObject.SetActive(true); getBtn.gameObject.SetActive(true);
                growBorder.gameObject.SetActive(true); growBtn.gameObject.SetActive(true);
                leaveBtn.gameObject.SetActive(true);
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
            if (t == null) yield break; // 破棄済みなら中断
            shown += cps * Time.deltaTime;
            t.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }
        if (t == null) yield break;
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
        _main.PlayEvilBgm();   // 悪魔の契約BGM
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
            info.text = $"最大HP {_main.Stats.MaxHP}　／　ストックマス {_main.CellStock}";
        };

        var payBtn = ProtoUI.CreateGoldButton("Pay", rt, "最大HP -10 → ストックマス +1", 22, new Vector2(0, 20), new Vector2(460, 70),
            new Color(0.55f, 0.18f, 0.22f, 0.98f), null);
        payBtn.onClick.AddListener(() =>
        {
            if (_main.ContractTradeHpForCell()) say("くくく、よい契約だ……命を糧に力は増した。");
            else say("もう差し出す命がないとはな。これ以上は無理な相談よ。");
            refresh();
        });

        var closeBtn = ProtoUI.CreateButton("CClose", rt, "立ち去る", 22, new Vector2(0, -245), new Vector2(280, 60),
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
        // スコア計算とメタ進行の更新
        int score = 1000 * (_main.Ascension + 1) + _main.CurrentHP * 3 + _main.Money + _main.BoardCells * 10;
        bool newBest = score > ProtoUnlocks.BestScore;
        int prevMaxAsc = ProtoUnlocks.MaxAscUnlocked;
        ProtoUnlocks.OnClear(_main.Ascension, score);
        bool unlockedAsc = ProtoUnlocks.MaxAscUnlocked > prevMaxAsc;

        if (_clearOverlay != null) Destroy(_clearOverlay);
        var rt = ProtoUI.CreateFullScreen("Clear", _root);
        _clearOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);
        var t = ProtoUI.CreateText("CT", rt, "ゲームクリア！", 60, new Vector2(0, 200), new Vector2(900, 90));
        ProtoUI.StyleTitle(t, ProtoUI.Gold, 10f);
        ProtoUI.CreateText("CS", rt, $"難易度 {ProtoUnlocks.AscName(_main.Ascension)} をクリア！", 24, new Vector2(0, 130), new Vector2(900, 40), new Color(0.9f, 0.9f, 1f));

        ProtoUI.CreateFramedPanel("ScoreBox", rt, new Vector2(0, 25), new Vector2(600, 190),
            new Color(0.07f, 0.06f, 0.11f, 0.96f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
        ProtoUI.CreateText("ScoreV", rt, $"スコア　{score}", 40, new Vector2(0, 55), new Vector2(520, 50), ProtoUI.Gold);
        ProtoUI.CreateText("BestV", rt, newBest ? "★ ニューレコード！" : $"ベスト {ProtoUnlocks.BestScore}", 22, new Vector2(0, 10), new Vector2(520, 32),
            newBest ? new Color(1f, 0.9f, 0.4f) : new Color(0.8f, 0.85f, 1f));
        ProtoUI.CreateText("ClearCount", rt, $"通算クリア {ProtoUnlocks.Clears} 回", 16, new Vector2(0, -20), new Vector2(520, 24), new Color(0.75f, 0.8f, 0.95f));
        ProtoUI.CreateText("RunStats", rt, $"総ダメージ {_main.StatTotalDamage}　／　最大一撃 {_main.StatMaxHit}", 18, new Vector2(0, -55), new Vector2(560, 26), new Color(0.9f, 0.85f, 0.7f));

        if (unlockedAsc)
            ProtoUI.CreateText("AscUnlock", rt, $"◆ 難易度 {ProtoUnlocks.AscName(ProtoUnlocks.MaxAscUnlocked)} 解放！（メニューの設定で選択）", 20,
                new Vector2(0, -110), new Vector2(900, 30), new Color(1f, 0.7f, 0.9f));

        ProtoUI.CreateGoldButton("Replay", rt, "もう一度挑戦", 24, new Vector2(0, -180), new Vector2(280, 64),
            new Color(0.35f, 0.3f, 0.55f), ResetRun);
    }

    public void ResetRun()
    {
        if (_clearOverlay != null) { Destroy(_clearOverlay); _clearOverlay = null; }
        _main.NewMapSeed();
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
