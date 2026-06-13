using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// マップ画面: 『Slay the Spire』風のノードマップ（すごろく・仮ローグライク）。
// 下の開始点から上のボスへ。分岐路をクリックで選んで進み、敵ノードに入ると強制戦闘。
// すべての分岐は最終的に同じボス（ドラゴン）へ収束する。倒すたびにピースを1つ獲得。
// ※現段階ではランダム生成はせず固定レイアウト。ショップ/イベントは未配置（敵のみ）。
public class MapScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;
    RectTransform _nodeLayer;     // 線とノードを載せる層
    RectTransform _playerIcon;
    TextMeshProUGUI _notice;
    GameObject _clearOverlay;
    bool _moving;

    class Node
    {
        public int row, col;
        public bool isBoss, isStart, cleared;
        public ProtoEnemy enemy;
        public Vector2 pos;
        public readonly List<Node> next = new List<Node>();
        public Image icon;
        public Button button;
        public Image marker; // 到達可能ハイライト
    }

    readonly List<Node> _nodes = new List<Node>();
    Node _current;   // 今いるノード
    Node _engaged;   // 戦闘に入ったノード

    // 行ごとの列配置（col 0..4）。row0=開始 / 最終row=ボス。三叉路は3ノードの行。
    static readonly int[][] RowCols =
    {
        new[]{ 2 },          // row0 開始
        new[]{ 1, 3 },       // row1
        new[]{ 0, 2, 4 },    // row2 三叉
        new[]{ 1, 3 },       // row3
        new[]{ 0, 2, 4 },    // row4 三叉
        new[]{ 1, 3 },       // row5
        new[]{ 2 },          // row6
        new[]{ 2 },          // row7 ボス
    };

    const float ColSpacing = 140f;
    const float BottomY = -300f, TopY = 330f;
    static readonly Color LineColor = new Color(0.55f, 0.45f, 0.3f, 0.7f);

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        BuildMap();
        Hide();
    }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        _moving = false;
        _main.PlayMapBgm(0);
        RefreshNodes();
    }

    public void Hide()
    {
        StopAllCoroutines();
        _moving = false;
        _root.gameObject.SetActive(false);
    }

    // ==================== UI構築 ====================

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MapScreen", _main.Canvas.transform);
        var backdrop = _root.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0.20f, 0.17f, 0.12f); // 羊皮紙風の地

        // タイトル
        var title = ProtoUI.CreateText("MapTitle", _root, "ダンジョンマップ", 26,
            new Vector2(0, 412), new Vector2(600, 40));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 4f);

        // 線とノードを載せる層
        _nodeLayer = ProtoUI.CreateRect("NodeLayer", _root);
        _nodeLayer.anchoredPosition = Vector2.zero;
        _nodeLayer.sizeDelta = new Vector2(1000, 720);

        // プレイヤーの現在地マーカー
        _playerIcon = ProtoUI.CreateRect("PlayerIcon", _root);
        _playerIcon.sizeDelta = new Vector2(70, 84);
        var pimg = _playerIcon.gameObject.AddComponent<Image>();
        pimg.sprite = ProtoPixelArt.MapMama(1, 0); // 背面（上を向いて進む）
        pimg.preserveAspect = true;
        pimg.raycastTarget = false;

        // 通知（ピース獲得など）
        _notice = ProtoUI.CreateText("MapNotice", _root, "", 20,
            new Vector2(0, 372), new Vector2(1100, 30), new Color(1f, 0.92f, 0.6f));

        // 下部バー＋メニュー
        ProtoUI.CreatePanel("BottomBar", _root, new Vector2(0, -424), new Vector2(1700, 56),
            new Color(0.05f, 0.04f, 0.10f, 0.92f)).raycastTarget = false;
        ProtoUI.CreateText("Hint", _root,
            "光っているノードをクリックして進む　　敵に入ると戦闘　　B = メニュー", 17,
            new Vector2(60, -424), new Vector2(1100, 30));
        ProtoUI.CreateButton("MenuBtn", _root, "メニュー", 18,
            new Vector2(-700, -424), new Vector2(150, 42),
            new Color(0.3f, 0.25f, 0.45f), () => _main.ShowMenu());
    }

    // ==================== マップ生成（固定レイアウト） ====================

    void BuildMap()
    {
        _nodes.Clear();
        int lastRow = RowCols.Length - 1;
        float rowStep = (TopY - BottomY) / (RowCols.Length - 1);

        // ノード生成（行→列）
        var byRow = new List<List<Node>>();
        for (int r = 0; r < RowCols.Length; r++)
        {
            var rowList = new List<Node>();
            foreach (int c in RowCols[r])
            {
                var n = new Node
                {
                    row = r,
                    col = c,
                    isStart = r == 0,
                    isBoss = r == lastRow,
                    pos = new Vector2((c - 2) * ColSpacing, BottomY + r * rowStep),
                };
                n.enemy = EnemyForRow(r, c, lastRow);
                if (n.isStart) n.cleared = true;
                rowList.Add(n);
                _nodes.Add(n);
            }
            byRow.Add(rowList);
        }

        // エッジ生成（次の行で列差1以内のノードへ接続）。全分岐がボスへ収束する構造
        for (int r = 0; r < lastRow; r++)
            foreach (var a in byRow[r])
                foreach (var b in byRow[r + 1])
                    if (Mathf.Abs(a.col - b.col) <= 1)
                        a.next.Add(b);

        // 先に接続線を描く（ノードより後ろに表示される）
        foreach (var a in _nodes)
            foreach (var b in a.next)
                DrawLine(a, b);

        // ノードUI（線の上に重なる）
        foreach (var n in _nodes) BuildNodeUI(n);

        _current = byRow[0][0];
        _playerIcon.anchoredPosition = _current.pos;
    }

    ProtoEnemy EnemyForRow(int row, int col, int lastRow)
    {
        if (row == 0) return null;                       // 開始点は戦闘なし
        if (row == lastRow) return ProtoEnemies.Find("dragon"); // 単一のボス
        if (row <= 2) return ProtoEnemies.Find(col % 2 == 0 ? "slime" : "bat");
        if (row <= 4) return ProtoEnemies.Find(col % 2 == 0 ? "bat" : "golem");
        return ProtoEnemies.Find("golem");
    }

    void DrawLine(Node a, Node b)
    {
        var seg = ProtoUI.CreateRect("Line", _nodeLayer);
        Vector2 mid = (a.pos + b.pos) / 2f;
        Vector2 d = b.pos - a.pos;
        seg.anchoredPosition = mid;
        seg.sizeDelta = new Vector2(5f, d.magnitude);
        seg.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
        var img = seg.gameObject.AddComponent<Image>();
        img.color = LineColor;
        img.raycastTarget = false;
    }

    void BuildNodeUI(Node n)
    {
        // 到達可能ハイライト（アイコンの後ろ。普段は非表示）
        var markerRt = ProtoUI.CreateRect($"Marker_{n.row}_{n.col}", _nodeLayer);
        markerRt.anchoredPosition = n.pos;
        markerRt.sizeDelta = new Vector2(n.isBoss ? 128 : 84, n.isBoss ? 128 : 84);
        n.marker = markerRt.gameObject.AddComponent<Image>();
        n.marker.color = Color.clear;
        n.marker.raycastTarget = false;

        // アイコン
        var iconRt = ProtoUI.CreateRect($"Node_{n.row}_{n.col}", _nodeLayer);
        iconRt.anchoredPosition = n.pos;
        iconRt.sizeDelta = n.isBoss ? new Vector2(120, 120) : new Vector2(64, 64);
        n.icon = iconRt.gameObject.AddComponent<Image>();
        n.icon.preserveAspect = true;
        if (n.isStart)
            n.icon.sprite = ProtoPixelArt.MapMama(0, 0); // 開始点はMAMAの絵
        else
            n.icon.sprite = n.enemy.mapSpriteOverride != null ? n.enemy.mapSpriteOverride : n.enemy.sprite;

        n.button = iconRt.gameObject.AddComponent<Button>();
        n.button.targetGraphic = n.icon;
        var node = n;
        n.button.onClick.AddListener(() => OnNodeClicked(node));
    }

    // ==================== 進行 ====================

    // 到達可能ノードを光らせ、それ以外は押せなくする。クリア済みは暗く。
    void RefreshNodes()
    {
        foreach (var n in _nodes)
        {
            bool reachable = !_moving && _current.next.Contains(n) && !n.cleared;
            n.button.interactable = reachable;

            if (n.isStart)
                n.icon.color = new Color(0.7f, 0.7f, 0.7f);
            else if (n.cleared)
                n.icon.color = new Color(0.35f, 0.35f, 0.4f); // 撃破済みは暗く
            else
                n.icon.color = Color.white;

            // 到達可能ノードは金色のリングで強調
            n.marker.color = reachable ? new Color(1f, 0.85f, 0.3f, 0.85f) : Color.clear;
        }

        // クリア済みの経路の線を緑に（通った道がわかる）
        _playerIcon.anchoredPosition = _current.pos;
    }

    void OnNodeClicked(Node n)
    {
        if (_moving) return;
        if (!_current.next.Contains(n) || n.cleared) return;
        StartCoroutine(MoveAndFight(n));
    }

    IEnumerator MoveAndFight(Node n)
    {
        _moving = true;
        _notice.text = "";
        foreach (var node in _nodes) node.button.interactable = false;
        foreach (var node in _nodes) node.marker.color = Color.clear;

        // プレイヤーアイコンを移動
        Vector2 from = _playerIcon.anchoredPosition;
        float t = 0f;
        const float dur = 0.35f;
        while (t < dur)
        {
            t += Time.deltaTime;
            _playerIcon.anchoredPosition = Vector2.Lerp(from, n.pos, Mathf.SmoothStep(0, 1, t / dur));
            yield return null;
        }
        _playerIcon.anchoredPosition = n.pos;

        // 敵ノードなら戦闘へ（戦闘結果は OnEnemyDefeated / 敗北で戻る）
        _engaged = n;
        if (n.enemy != null)
        {
            _main.StartBattle(n.enemy);
        }
        else
        {
            _current = n;
            _moving = false;
            RefreshNodes();
        }
    }

    // バトル勝利時に ProtoMain.OnBattleWon から呼ばれる
    public void OnEnemyDefeated()
    {
        if (_engaged == null) return;
        var node = _engaged;
        node.cleared = true;
        _current = node;
        _engaged = null;
        _moving = false;

        if (node.isBoss)
        {
            ShowClear();
            return;
        }

        // ピースを1つ獲得（解放）
        var unlocked = _main.UnlockNextSkill();
        if (unlocked != null)
            ShowNotice($"ピース獲得！「{unlocked.skillName}」（{unlocked.Size}マス）— ビルドで配置できます");
        else
            ShowNotice("勝利！（解放できるピースは全て入手済み）");
    }

    void ShowNotice(string msg)
    {
        if (_notice != null) _notice.text = msg;
    }

    // ==================== クリア演出 ====================

    void ShowClear()
    {
        if (_clearOverlay != null) Destroy(_clearOverlay);
        var rt = ProtoUI.CreateFullScreen("ClearOverlay", _root);
        _clearOverlay = rt.gameObject;
        var dim = rt.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.85f);

        var t = ProtoUI.CreateText("ClearText", rt, "ゲームクリア！", 64, new Vector2(0, 80), new Vector2(900, 100));
        ProtoUI.StyleTitle(t, ProtoUI.Gold, 10f);
        ProtoUI.CreateText("ClearSub", rt, "ボスを撃破した！プロトタイプの最終地点に到達しました。", 24,
            new Vector2(0, -10), new Vector2(900, 60), new Color(0.9f, 0.9f, 1f));
        ProtoUI.CreateButton("ReplayBtn", rt, "もう一度挑戦", 24,
            new Vector2(0, -90), new Vector2(280, 64), new Color(0.35f, 0.3f, 0.55f), ResetRun);
    }

    // マップを最初から（クリア状態をリセット。獲得ピースは保持）
    void ResetRun()
    {
        if (_clearOverlay != null) { Destroy(_clearOverlay); _clearOverlay = null; }
        foreach (var n in _nodes) n.cleared = n.isStart;
        _current = _nodes[0];
        _playerIcon.anchoredPosition = _current.pos;
        RefreshNodes();
    }

    // ==================== 入力（Bでメニュー） ====================

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        bool menuKey = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame) menuKey = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!menuKey && Input.GetKeyDown(KeyCode.B)) menuKey = true;
#endif
        if (menuKey) _main.ShowMenu();
    }
}
