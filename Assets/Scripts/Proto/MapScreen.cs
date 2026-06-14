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
    TextMeshProUGUI _moneyText, _notice;
    GameObject _shopOverlay, _clearOverlay;
    bool _moving;

    enum TileType { Start, Enemy, Event, SpiritTree, Boss }

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

    const int MidColumns = 15;       // 中間の列数
    const float ColSpacing = 165f;
    const float LaneSpacing = 150f;
    static readonly int[] Lanes = { -1, 0, 1 };
    static readonly Color LineColor = new Color(0.55f, 0.45f, 0.3f, 0.7f);

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
        _root.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.17f, 0.12f);

        var title = ProtoUI.CreateText("MapTitle", _root, "ダンジョンマップ", 26, new Vector2(0, 412), new Vector2(600, 40));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 4f);

        _moneyText = ProtoUI.CreateText("Money", _root, "", 22, new Vector2(-600, 412), new Vector2(360, 32), ProtoUI.Gold);
        _moneyText.alignment = TextAlignmentOptions.Left;

        // 横スクロールのビューポート
        var viewport = ProtoUI.CreateRect("MapViewport", _root);
        viewport.anchoredPosition = new Vector2(0, 30);
        viewport.sizeDelta = new Vector2(1560, 600);
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
        _playerIcon.sizeDelta = new Vector2(70, 84);
        var pimg = _playerIcon.gameObject.AddComponent<Image>();
        pimg.sprite = ProtoPixelArt.Mama(); pimg.preserveAspect = true; pimg.raycastTarget = false;

        _notice = ProtoUI.CreateText("Notice", _root, "", 20, new Vector2(0, 360), new Vector2(1200, 30), new Color(1f, 0.92f, 0.6f));

        ProtoUI.CreatePanel("BottomBar", _root, new Vector2(0, -424), new Vector2(1700, 56), new Color(0.05f, 0.04f, 0.10f, 0.92f)).raycastTarget = false;
        ProtoUI.CreateText("Hint", _root, "光るノードをクリックで進む（左→右）　エネミー=戦闘 / イベント=お金 / 精神樹=拡張・購入　B=メニュー", 16,
            new Vector2(60, -424), new Vector2(1200, 30));
        ProtoUI.CreateButton("MenuBtn", _root, "メニュー", 18, new Vector2(-700, -424), new Vector2(150, 42),
            new Color(0.3f, 0.25f, 0.45f), () => _main.ShowMenu());
    }

    // ==================== マップ生成（固定・横向き） ====================

    void BuildMap()
    {
        _nodes.Clear();
        int bossCol = MidColumns + 1;

        // コンテンツ幅を先に確定（ノード座標は中央基準で配置する）
        _contentWidth = (bossCol + 1) * ColSpacing + 120f;
        _halfWidth = _contentWidth / 2f;
        _nodeLayer.sizeDelta = new Vector2(_contentWidth, 600);

        // 開始ノード（左端・中央レーン）
        var start = NewNode(0, 0, TileType.Start);

        // 中間ノード（各列3レーン）＋種別を5:3:1パターンで割り当て
        var byCol = new Dictionary<int, List<Node>>();
        byCol[0] = new List<Node> { start };
        TileType[] cycle = {
            TileType.Enemy, TileType.Enemy, TileType.Enemy, TileType.Enemy, TileType.Enemy,
            TileType.Event, TileType.Event, TileType.Event, TileType.SpiritTree
        };
        int k = 0;
        for (int col = 1; col <= MidColumns; col++)
        {
            var list = new List<Node>();
            foreach (int lane in Lanes)
            {
                var type = cycle[k % cycle.Length]; k++;
                var n = NewNode(col, lane, type);
                if (type == TileType.Enemy) n.enemy = EnemyForColumn(col);
                list.Add(n);
            }
            byCol[col] = list;
        }

        // ボス（右端・中央）
        var boss = NewNode(bossCol, 0, TileType.Boss);
        boss.enemy = _main.Db != null ? _main.Db.FindEnemy("dragon") : null;
        byCol[bossCol] = new List<Node> { boss };

        // エッジ（次の列でレーン差≤1）
        for (int col = 0; col < bossCol; col++)
            foreach (var a in byCol[col])
                foreach (var b in byCol[col + 1])
                    if (Mathf.Abs(a.lane - b.lane) <= 1)
                        a.next.Add(b);

        // 精神樹が5未満なら、後ろのイベントを精神樹に変換して補う
        EnsureMinSpiritTrees(5);

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
            pos = new Vector2(col * ColSpacing + 60f - _halfWidth, lane * LaneSpacing),
        };
        _nodes.Add(n);
        return n;
    }

    void EnsureMinSpiritTrees(int min)
    {
        int trees = _nodes.FindAll(n => n.type == TileType.SpiritTree).Count;
        if (trees >= min) return;
        // 後方のイベントから順に精神樹へ変換
        var events = _nodes.FindAll(n => n.type == TileType.Event);
        events.Sort((a, b) => b.col.CompareTo(a.col));
        foreach (var e in events)
        {
            if (trees >= min) break;
            e.type = TileType.SpiritTree;
            trees++;
        }
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
        seg.anchoredPosition = mid; seg.sizeDelta = new Vector2(5f, d.magnitude);
        seg.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
        var img = seg.gameObject.AddComponent<Image>(); img.color = LineColor; img.raycastTarget = false;
    }

    void BuildNodeUI(Node n)
    {
        var markerRt = ProtoUI.CreateRect($"Mk_{n.col}_{n.lane}", _nodeLayer);
        markerRt.anchoredPosition = n.pos; markerRt.sizeDelta = new Vector2(86, 86);
        n.marker = markerRt.gameObject.AddComponent<Image>(); n.marker.color = Color.clear; n.marker.raycastTarget = false;

        var iconRt = ProtoUI.CreateRect($"Nd_{n.col}_{n.lane}", _nodeLayer);
        iconRt.anchoredPosition = n.pos;
        iconRt.sizeDelta = n.type == TileType.Boss ? new Vector2(120, 120) : new Vector2(64, 64);
        n.icon = iconRt.gameObject.AddComponent<Image>(); n.icon.preserveAspect = true;

        switch (n.type)
        {
            case TileType.Start: n.icon.sprite = ProtoPixelArt.Mama(); break;
            case TileType.Enemy: n.icon.sprite = n.enemy != null ? n.enemy.MapSprite() : ProtoPixelArt.Slime(); break;
            case TileType.Boss:  n.icon.sprite = n.enemy != null ? n.enemy.MapSprite() : ProtoPixelArt.Dragon(); break;
            case TileType.Event:
                n.icon.sprite = null; n.icon.color = new Color(0.9f, 0.8f, 0.3f);
                AddLabel(iconRt, "?", 36, new Color(0.2f, 0.15f, 0f)); break;
            case TileType.SpiritTree:
                n.icon.sprite = null; n.icon.color = new Color(0.4f, 0.75f, 0.5f);
                AddLabel(iconRt, "樹", 30, new Color(0.05f, 0.2f, 0.1f)); break;
        }

        n.button = iconRt.gameObject.AddComponent<Button>();
        n.button.targetGraphic = n.icon;
        var node = n;
        n.button.onClick.AddListener(() => OnNodeClicked(node));
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
            n.marker.color = reachable ? new Color(1f, 0.85f, 0.3f, 0.85f) : Color.clear;

            // 種別の基本色を保ちつつ、クリア済みは暗く
            if (n.type == TileType.Event || n.type == TileType.SpiritTree)
            {
                Color baseC = n.type == TileType.Event ? new Color(0.9f, 0.8f, 0.3f) : new Color(0.4f, 0.75f, 0.5f);
                n.icon.color = n.cleared ? baseC * 0.4f : baseC;
            }
            else
            {
                n.icon.color = (n.cleared && n.type != TileType.Start) ? new Color(0.4f, 0.4f, 0.45f) : Color.white;
            }
        }
        _playerIcon.anchoredPosition = _current.pos;
        _playerIcon.SetAsLastSibling();
        _moneyText.text = $"お金: {_main.Money}";
        ScrollToCurrent();
    }

    void ScrollToCurrent()
    {
        if (_scroll == null) return;
        const float vw = 1560f;
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
                OpenShop(n);
                break;
        }
    }

    // バトル勝利時（ProtoMain.OnBattleWon から）
    public void OnEnemyDefeated()
    {
        if (_engaged == null) return;
        var node = _engaged; _engaged = null;
        node.cleared = true; _current = node; _moving = false;
        if (node.type == TileType.Boss) ShowClear();
    }

    // ==================== 精神樹ショップ ====================

    void OpenShop(Node treeNode)
    {
        if (_shopOverlay != null) Destroy(_shopOverlay);
        var rt = ProtoUI.CreateFullScreen("Shop", _root);
        _shopOverlay = rt.gameObject;
        rt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);

        var title = ProtoUI.CreateText("ST", rt, "精神樹", 40, new Vector2(0, 380), new Vector2(600, 50));
        ProtoUI.StyleTitle(title, new Color(0.6f, 1f, 0.7f), 6f);

        var money = ProtoUI.CreateText("M", rt, "", 24, new Vector2(0, 330), new Vector2(600, 30), ProtoUI.Gold);

        // 盤面拡張ボタン（この訪問で1回まで）
        var expandBtnGO = ProtoUI.CreateButton("Expand", rt, "", 20, new Vector2(0, 250), new Vector2(560, 60),
            new Color(0.3f, 0.45f, 0.5f), null);
        var expandLabel = expandBtnGO.GetComponentInChildren<TextMeshProUGUI>();
        bool[] expandedThisVisit = { false };

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
            money.text = $"お金: {_main.Money}";
            int cost = _main.NextExpansionCost();
            bool canExpand = !expandedThisVisit[0] && _main.CanExpand();
            if (_main.Expansions >= 5) expandLabel.text = "盤面は最大(10×10)です";
            else if (expandedThisVisit[0]) expandLabel.text = "拡張はこの訪問で使用済み";
            else expandLabel.text = $"盤面拡張 {_main.BoardSize}×{_main.BoardSize}→{_main.BoardSize + 1}×{_main.BoardSize + 1}（{cost}）";
            expandBtnGO.interactable = canExpand;

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

        expandBtnGO.onClick.AddListener(() => { if (_main.ExpandBoard()) { expandedThisVisit[0] = true; refresh(); } });
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

    void ResetRun()
    {
        if (_clearOverlay != null) { Destroy(_clearOverlay); _clearOverlay = null; }
        foreach (var n in _nodes) n.cleared = n.type == TileType.Start;
        _current = _nodes[0];
        _playerIcon.anchoredPosition = _current.pos;
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
