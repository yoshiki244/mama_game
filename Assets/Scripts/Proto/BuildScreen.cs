using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// ビルド画面: 単一の可変サイズ盤面(5×5〜10×10)にカード(ピース)を配置する。
// 左クリック=配置 / 右クリック=撤去 / ドラッグ=移動 / クリック選択→Rキー=回転
// 配置プレビュー（マウス位置にゴースト表示→左クリック確定）。所持カードのみ一覧表示・スクロール可。
public class BuildScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;

    Image[,] _cellImages;
    TextMeshProUGUI _title, _info, _selectedText, _boardCountText, _manaInfoText;
    readonly List<(CardDef card, Image img)> _trayButtons = new List<(CardDef, Image)>();

    CardDef _selected;
    int _rotation;
    RectTransform _gridRt, _trayContent;
    ScrollRect _trayScroll;
    Image _boardBg;
    Vector2Int? _boardSel;

    PanelModel P => _main.Panel;

    bool _isDragging;
    CardDef _dragCard;
    List<Vector2Int> _dragOrigCells;
    Vector2Int _dragGrabCell;

    float _cellSize = 40f;
    const float CellGap = 3f;
    const float BoardArea = 460f; // 盤面の表示領域（おおよそ正方形）

    static readonly Color EmptyColor = new Color(0.16f, 0.14f, 0.24f);
    static readonly Color LockedColor = new Color(0.05f, 0.05f, 0.07f, 0.85f);   // 未解放マス
    static readonly Color LockedAddColor = new Color(0.18f, 0.30f, 0.22f, 0.9f); // 追加モード時の未解放マス
    static readonly Color TraySelected = new Color(0.45f, 0.35f, 0.65f);
    static readonly Color TrayNormal = new Color(0.22f, 0.18f, 0.32f);

    // 絞り込み・並び替え
    readonly HashSet<int> _fSizes = new HashSet<int>();   // マス数フィルタ（空＝全部）
    readonly HashSet<int> _fMana = new HashSet<int>();    // マナ数フィルタ（空＝全部）
    bool _fAttack, _fDefense, _fHeal, _fSkill;            // 種別フィルタ
    readonly List<(string key, bool asc)> _sorts = new List<(string, bool)>(); // 並び替え（順序付き複数）
    GameObject _sortOverlay;
    int _sortTab;                                         // 0=絞り込み 1=並び替え
    System.Action _sortRebuild;

    bool _addMode;                 // マス配置モード（マスストックを解放）
    TextMeshProUGUI _addBtnText;
    TextMeshProUGUI _stockText;
    Image _addBtnImg;
    GameObject _resetBtnGO;        // 配置モード中のみ表示「リセットする」
    GameObject _confirmGO;         // 「これで配置します」確認ポップアップ
    List<Vector2Int> _sessionBaseline;  // 配置開始時点の解放マス（リセットの基準）
    static readonly Color AddBtnOff = new Color(0.28f, 0.42f, 0.32f, 0.98f); // 緑：マスを配置する
    static readonly Color AddBtnOn = new Color(0.62f, 0.16f, 0.16f, 0.98f);  // 赤：配置をやめる

    public void Init(ProtoMain main) { _main = main; BuildUI(); Hide(); }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        RefreshTray();
        BuildBoard();
        // 配置モードは毎回OFFで開始
        _addMode = false;
        if (_addBtnText != null) _addBtnText.text = "マスを配置する";
        if (_addBtnImg != null) _addBtnImg.color = AddBtnOff;
        if (_resetBtnGO != null) _resetBtnGO.SetActive(false);
        if (_confirmGO != null) { Destroy(_confirmGO); _confirmGO = null; }
        RefreshBoard();
    }

    public void Hide()
    {
        if (_confirmGO != null) { Destroy(_confirmGO); _confirmGO = null; }
        if (_sortOverlay != null) { Destroy(_sortOverlay); _sortOverlay = null; }
        if (_root != null) _root.gameObject.SetActive(false);
    }

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("BuildScreen", _main.Canvas.transform);
        var rootImg = _root.gameObject.AddComponent<Image>();
        var bg = ProtoPixelArt.MenuBackground();
        if (bg != null) { rootImg.sprite = bg; rootImg.color = Color.white; rootImg.preserveAspect = false; }
        else rootImg.color = new Color(0.05f, 0.04f, 0.09f, 0.96f);

        _title = ProtoUI.CreateText("Title", _root, "スキルビルド", 34, new Vector2(0, 410), new Vector2(560, 50));
        ProtoUI.StyleTitle(_title, ProtoUI.Gold, 6f);
        ProtoUI.CreatePanel("TitleLine", _root, new Vector2(0, 384), new Vector2(620, 3), new Color(0.85f, 0.72f, 0.4f, 0.9f)).raycastTarget = false;

        // マス配置モード切替（マスストックをロック中のセルへ配置して盤面を広げる）
        var addBtn = ProtoUI.CreateButton("AddCellBtn", _root, "マスを配置する", 22, new Vector2(-330, 318), new Vector2(340, 46),
            AddBtnOff, OnAddButton);
        _addBtnText = addBtn.GetComponentInChildren<TextMeshProUGUI>();
        _addBtnImg = (Image)addBtn.targetGraphic;

        // 盤面の外枠（色付きで見やすく）
        ProtoUI.CreatePanel("BoardBorder", _root, new Vector2(-330, 25),
            new Vector2(BoardArea + 16, BoardArea + 16), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreatePanel("BoardBorderInner", _root, new Vector2(-330, 25),
            new Vector2(BoardArea + 6, BoardArea + 6), new Color(0.12f, 0.10f, 0.18f, 0.95f)).raycastTarget = false;

        // 盤面の左横に情報枠を縦3つ（間隔をあけて配置）
        const float bx = -665f;
        // マスストック（上枠＝盤面上端 y263）
        ProtoUI.CreateFramedPanel("StockBox", _root, new Vector2(bx, 198), new Vector2(160, 130),
            new Color(0.06f, 0.09f, 0.07f, 0.96f), new Color(0.5f, 0.8f, 0.45f, 0.85f));
        ProtoUI.CreateText("StockLabel", _root, "マスストック", 16, new Vector2(bx, 236), new Vector2(160, 24), new Color(0.7f, 1f, 0.7f));
        _stockText = ProtoUI.CreateText("StockVal", _root, "0", 40, new Vector2(bx, 182), new Vector2(160, 52), Color.white);
        _stockText.fontStyle = FontStyles.Bold;
        // 盤面マス数（中央）
        ProtoUI.CreateFramedPanel("BoardCntBox", _root, new Vector2(bx, 25), new Vector2(160, 130),
            new Color(0.06f, 0.07f, 0.11f, 0.96f), new Color(0.65f, 0.55f, 0.36f, 0.85f));
        ProtoUI.CreateText("BoardCntLabel", _root, "盤面マス", 16, new Vector2(bx, 63), new Vector2(160, 24), ProtoUI.Gold);
        _boardCountText = ProtoUI.CreateText("BoardCntVal", _root, "", 26, new Vector2(bx, 8), new Vector2(160, 44), Color.white);
        _boardCountText.fontStyle = FontStyles.Bold;
        // マナ（下枠＝盤面下端 y-213）
        ProtoUI.CreateFramedPanel("ManaBox", _root, new Vector2(bx, -148), new Vector2(160, 130),
            new Color(0.035f, 0.105f, 0.22f, 0.96f), new Color(0.38f, 0.78f, 1f, 0.8f));
        ProtoUI.CreateText("ManaLbl", _root, "マナ", 16, new Vector2(bx, -110), new Vector2(160, 24), new Color(0.6f, 0.85f, 1f));
        _manaInfoText = ProtoUI.CreateText("ManaVal", _root, "", 40, new Vector2(bx, -164), new Vector2(160, 52), Color.white);
        _manaInfoText.fontStyle = FontStyles.Bold;

        // リセットするボタン（配置モード中のみ表示・盤面の下）
        var resetBtn = ProtoUI.CreateButton("ResetCellsBtn", _root, "リセットする", 18, new Vector2(bx, -258), new Vector2(160, 46),
            new Color(0.6f, 0.2f, 0.2f, 0.98f), OnResetCells);
        _resetBtnGO = resetBtn.gameObject;
        _resetBtnGO.SetActive(false);

        // 盤面の土台
        _boardBg = ProtoUI.CreatePanel("Board", _root, new Vector2(-330, 25),
            new Vector2(BoardArea, BoardArea), new Color(0.05f, 0.04f, 0.10f, 0.6f));
        _boardBg.raycastTarget = false;
        var grid = ProtoUI.CreateRect("Grid", _boardBg.transform);
        _gridRt = grid;
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

        // ピース一覧（右・スクロール）
        ProtoUI.CreateText("TrayTitle", _root, "所持カード（クリックで選択）", 22,
            new Vector2(150, 318), new Vector2(420, 30));
        // ソート/絞り込みボタン（タイトル右）
        ProtoUI.CreatePanel("SortBtnBorder", _root, new Vector2(379, 318), new Vector2(54, 46), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        var sortBtn = ProtoUI.CreateButton("SortBtn", _root, "", 18, new Vector2(379, 318), new Vector2(46, 38), new Color(0.2f, 0.2f, 0.3f, 0.98f), OpenSortPanel);
        var si = ProtoPixelArt.SortIcon();
        if (si != null) { var ic = ProtoUI.CreateRect("SortIco", sortBtn.transform); ic.anchoredPosition = Vector2.zero; ic.sizeDelta = new Vector2(34, 34); var im = ic.gameObject.AddComponent<Image>(); im.sprite = si; im.preserveAspect = true; im.raycastTarget = false; }
        else sortBtn.GetComponentInChildren<TextMeshProUGUI>().text = "並";

        // ソートボタンの説明ツールチップ（カーソルを合わせると表示）
        var tip = ProtoUI.CreateRect("SortTip", _root);
        tip.anchoredPosition = new Vector2(560, 318); tip.sizeDelta = new Vector2(300, 56);
        var tipInner = ProtoUI.CreateFramedPanel("SortTipBox", tip, Vector2.zero, new Vector2(300, 56),
            new Color(0.07f, 0.06f, 0.12f, 0.97f), new Color(0.85f, 0.72f, 0.4f, 0.95f));
        var tipTxt = ProtoUI.CreateText("SortTipTxt", tipInner.transform, "所持カードの絞り込み・並び替え",
            16, Vector2.zero, new Vector2(284, 46), Color.white);
        tipTxt.raycastTarget = false;
        tip.gameObject.SetActive(false);
        var hov = sortBtn.gameObject.AddComponent<HoverTip>();
        hov.target = tip.gameObject;

        var viewport = ProtoUI.CreateRect("TrayViewport", _root);
        viewport.anchoredPosition = new Vector2(180, 25);
        viewport.sizeDelta = new Vector2(470, 476);
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0.08f, 0.06f, 0.14f, 0.55f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var sr = viewport.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.viewport = viewport;
        sr.scrollSensitivity = 28f; sr.movementType = ScrollRect.MovementType.Clamped;

        _trayContent = ProtoUI.CreateRect("TrayContent", viewport);
        _trayContent.anchorMin = new Vector2(0f, 1f);
        _trayContent.anchorMax = new Vector2(1f, 1f);
        _trayContent.pivot = new Vector2(0.5f, 1f);
        _trayContent.anchoredPosition = Vector2.zero;
        _trayContent.sizeDelta = new Vector2(0, 10);
        sr.content = _trayContent;
        _trayScroll = sr;

        // 選択中ピース／エラー表示（盤面の下・中央）
        _selectedText = ProtoUI.CreateText("Selected", _root, "カード未選択", 24,
            new Vector2(-330, -252), new Vector2(520, 40), null, TextAlignmentOptions.Center);
        _selectedText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        var hint = ProtoUI.CreateText("Hint", _root,
            "カードを選択して左クリック：ピースを配置　　　右クリック：ピースを回転（ピース選択中）／ピースを削除", 16,
            new Vector2(0, -408), new Vector2(1560, 30), new Color(0.72f, 0.72f, 0.82f));
        hint.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        hint.enableAutoSizing = true; hint.fontSizeMin = 10; hint.fontSizeMax = 15;

        // カードの出現率（カード一覧の右側に別枠・縦サイズは一覧と同じ）
        ProtoUI.CreateFramedPanel("ProbBox", _root, new Vector2(585, 25), new Vector2(320, 476),
            new Color(0.06f, 0.07f, 0.11f, 0.96f), new Color(0.65f, 0.55f, 0.36f, 0.85f));
        ProtoUI.CreateText("ProbTitle", _root, "出現率", 22, new Vector2(585, 238), new Vector2(300, 30), ProtoUI.Gold);
        _info = ProtoUI.CreateText("Info", _root, "", 16,
            new Vector2(585, 5), new Vector2(292, 430), new Color(0.88f, 0.85f, 1f), TextAlignmentOptions.Top);

        var goldB = new Color(0.85f, 0.72f, 0.4f, 0.95f);
        ProtoUI.CreatePanel("MenuBackBorder", _root, new Vector2(340, -312), new Vector2(260, 74), goldB).raycastTarget = false;
        ProtoUI.CreateButton("MenuBackBtn", _root, "メニューへ戻る", 22,
            new Vector2(340, -312), new Vector2(250, 64), new Color(0.3f, 0.28f, 0.5f), () => _main.ShowMenu());
        ProtoUI.CreatePanel("BackBorder", _root, new Vector2(620, -312), new Vector2(260, 74), goldB).raycastTarget = false;
        ProtoUI.CreateButton("BackBtn", _root, "マップへ戻る", 22,
            new Vector2(620, -312), new Vector2(250, 64), new Color(0.7f, 0.3f, 0.45f), () => _main.ShowMap());
    }

    // ==================== 盤面 ====================

    void BuildBoard()
    {
        foreach (Transform c in _gridRt) Destroy(c.gameObject);

        int w = P.W, h = P.H;
        _cellSize = Mathf.Floor((BoardArea - CellGap * (w + 1)) / w);

        var layout = _gridRt.GetComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(_cellSize, _cellSize);
        layout.spacing = new Vector2(CellGap, CellGap);
        layout.constraintCount = w;

        float unit = _cellSize + CellGap;
        _gridRt.sizeDelta = new Vector2(w * unit, h * unit);

        _cellImages = new Image[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int cx = x, cy = y;
                var cell = ProtoUI.CreatePanel($"Cell_{x}_{y}", _gridRt, Vector2.zero,
                    new Vector2(_cellSize, _cellSize), EmptyColor);
                _cellImages[x, y] = cell;

                var handler = cell.gameObject.AddComponent<CellClickHandler>();
                handler.onClick = e =>
                {
                    if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) OnCellLeftClick(cx, cy);
                    else if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) OnCellRightClick(cx, cy);
                };
                handler.onBeginDrag = e => OnCellBeginDrag(cx, cy);
                handler.onDrag = OnCellDrag;
                handler.onEndDrag = OnCellEndDrag;
            }
    }

    // 絞り込み＋並び替えを適用
    List<CardDef> ApplyFilterSort(List<CardDef> src)
    {
        var list = new List<CardDef>();
        foreach (var c in src)
        {
            if (_fSizes.Count > 0 && !_fSizes.Contains(c.Size)) continue;
            if (_fMana.Count > 0 && !_fMana.Contains(c.ManaCost)) continue;
            if (_fAttack || _fDefense || _fHeal || _fSkill)
            {
                var cat = c.Category;
                bool ok = (_fAttack && cat == CardKind.Attack) || (_fDefense && cat == CardKind.Defense)
                          || (_fHeal && cat == CardKind.Heal) || (_fSkill && cat == CardKind.Skill);
                if (!ok) continue;
            }
            list.Add(c);
        }
        // 並び替え（後ろの条件から安定ソート→先頭条件が優先）
        for (int i = _sorts.Count - 1; i >= 0; i--)
        {
            var s = _sorts[i];
            System.Func<CardDef, int> key = s.key == "mana" ? (System.Func<CardDef, int>)(c => c.ManaCost) : (c => c.Size);
            list = s.asc ? list.OrderBy(key).ToList() : list.OrderByDescending(key).ToList();
        }
        return list;
    }

    // ==================== ソート/絞り込み画面 ====================
    static readonly Color CkOn = new Color(0.35f, 0.7f, 0.42f, 0.98f);
    static readonly Color CkOff = new Color(0.18f, 0.18f, 0.26f, 0.98f);

    void MakeToggle(Transform parent, Vector2 pos, Vector2 size, string label, System.Func<bool> get, System.Action<bool> set, System.Action after = null)
    {
        ProtoUI.CreatePanel("TgB", parent, pos, size + new Vector2(6, 6), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false; // 金枠
        var box = ProtoUI.CreatePanel("Tg", parent, pos, size, get() ? CkOn : CkOff);
        var t = ProtoUI.CreateText("Tl", box.transform, label, 16, Vector2.zero, size, Color.white);
        t.raycastTarget = false;
        var btn = box.gameObject.AddComponent<Button>(); btn.targetGraphic = box;
        btn.onClick.AddListener(() => { bool nv = !get(); set(nv); box.color = nv ? CkOn : CkOff; after?.Invoke(); });
    }

    bool HasSort(string key, bool asc) => _sorts.Exists(s => s.key == key && s.asc == asc);
    void SetSort(string key, bool asc, bool v) { _sorts.RemoveAll(s => s.key == key); if (v) _sorts.Add((key, asc)); }

    void OpenSortPanel()
    {
        if (_sortOverlay != null) Destroy(_sortOverlay);
        var ov = ProtoUI.CreateFullScreen("SortPanel", _root);
        _sortOverlay = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.85f);
        ProtoUI.CreateFramedPanel("SBox", ov, Vector2.zero, new Vector2(760, 580),
            new Color(0.07f, 0.06f, 0.12f, 1f), new Color(0.85f, 0.72f, 0.4f, 0.95f));
        var title = ProtoUI.CreateText("STitle", ov, "ソート", 30, new Vector2(0, 240), new Vector2(600, 40), Color.white);
        ProtoUI.StyleTitle(title, Color.white, 4f);

        var content = ProtoUI.CreateRect("SContent", ov);
        content.anchoredPosition = new Vector2(-30, -20); content.sizeDelta = new Vector2(720, 420);

        // タブ（金枠・選択側を発光）
        var tabOff = new Color(0.22f, 0.2f, 0.34f, 0.98f);
        var tabOn = new Color(0.5f, 0.42f, 0.8f, 1f);
        ProtoUI.CreatePanel("TabFB", ov, new Vector2(-115, 168), new Vector2(216, 56), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        var tabF = ProtoUI.CreateButton("TabF", ov, "絞り込み", 20, new Vector2(-115, 168), new Vector2(208, 48), tabOff, null);
        ProtoUI.CreatePanel("TabSB", ov, new Vector2(115, 168), new Vector2(216, 56), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        var tabS = ProtoUI.CreateButton("TabS", ov, "並び替え", 20, new Vector2(115, 168), new Vector2(208, 48), tabOff, null);
        var tabFImg = (Image)tabF.targetGraphic; var tabSImg = (Image)tabS.targetGraphic;

        System.Action rebuild = null;
        rebuild = () =>
        {
            tabFImg.color = _sortTab == 0 ? tabOn : tabOff;
            tabSImg.color = _sortTab == 1 ? tabOn : tabOff;
            foreach (Transform c in content) Destroy(c.gameObject);
            if (_sortTab == 0) BuildFilterTab(content); else BuildSortTab(content);
        };
        _sortRebuild = rebuild;
        tabF.onClick.AddListener(() => { _sortTab = 0; rebuild(); });
        tabS.onClick.AddListener(() => { _sortTab = 1; rebuild(); });
        rebuild();

        // 初期設定に戻す（赤）＋適用して閉じる を横並び・少し上に
        ProtoUI.CreatePanel("SResetB", ov, new Vector2(-175, -210), new Vector2(310, 66), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreateButton("SReset", ov, "初期設定に戻す", 22, new Vector2(-175, -210), new Vector2(300, 56),
            new Color(0.62f, 0.18f, 0.18f, 0.98f), () => { _fSizes.Clear(); _fMana.Clear(); _fAttack = false; _fDefense = false; _fHeal = false; _fSkill = false; _sorts.Clear(); rebuild(); });
        ProtoUI.CreatePanel("SApplyB", ov, new Vector2(175, -210), new Vector2(310, 66), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreateButton("SApply", ov, "適用して閉じる", 22, new Vector2(175, -210), new Vector2(300, 56),
            new Color(0.45f, 0.3f, 0.55f, 0.98f), () => { Destroy(_sortOverlay); _sortOverlay = null; RefreshTray(); });
    }

    void BuildFilterTab(Transform p)
    {
        ProtoUI.CreateText("FL1", p, "マス数", 18, new Vector2(-262, 112), new Vector2(110, 26), Color.white, TextAlignmentOptions.Left);
        for (int i = 1; i <= 10; i++) { int n = i; MakeToggle(p, new Vector2(-190 + (i - 1) * 56, 112), new Vector2(48, 42), n.ToString(), () => _fSizes.Contains(n), v => { if (v) _fSizes.Add(n); else _fSizes.Remove(n); }); }
        ProtoUI.CreateText("FL2", p, "マナ数", 18, new Vector2(-262, 42), new Vector2(110, 26), Color.white, TextAlignmentOptions.Left);
        for (int i = 1; i <= 10; i++) { int n = i; MakeToggle(p, new Vector2(-190 + (i - 1) * 56, 42), new Vector2(48, 42), n.ToString(), () => _fMana.Contains(n), v => { if (v) _fMana.Add(n); else _fMana.Remove(n); }); }
        ProtoUI.CreateText("FL3", p, "スキル種別", 18, new Vector2(-262, -38), new Vector2(130, 26), Color.white, TextAlignmentOptions.Left);
        MakeToggle(p, new Vector2(-159, -38), new Vector2(110, 44), "攻撃", () => _fAttack, v => _fAttack = v);
        MakeToggle(p, new Vector2(-39, -38), new Vector2(110, 44), "ガード", () => _fDefense, v => _fDefense = v);
        MakeToggle(p, new Vector2(81, -38), new Vector2(110, 44), "ヒール", () => _fHeal, v => _fHeal = v);
        MakeToggle(p, new Vector2(201, -38), new Vector2(110, 44), "スキル", () => _fSkill, v => _fSkill = v);
        ProtoUI.CreateText("FHint", p, "未選択＝すべて表示", 15, new Vector2(0, -110), new Vector2(600, 24), new Color(0.7f, 0.7f, 0.8f));
    }

    void BuildSortTab(Transform p)
    {
        ProtoUI.CreateText("ZL1", p, "マナ数", 20, new Vector2(-220, 110), new Vector2(160, 28), Color.white, TextAlignmentOptions.Left);
        MakeToggle(p, new Vector2(-65, 110), new Vector2(160, 46), "昇順", () => HasSort("mana", true), v => SetSort("mana", true, v), _sortRebuild);
        MakeToggle(p, new Vector2(125, 110), new Vector2(160, 46), "降順", () => HasSort("mana", false), v => SetSort("mana", false, v), _sortRebuild);
        ProtoUI.CreateText("ZL2", p, "ピース数", 20, new Vector2(-220, 30), new Vector2(160, 28), Color.white, TextAlignmentOptions.Left);
        MakeToggle(p, new Vector2(-65, 30), new Vector2(160, 46), "昇順", () => HasSort("size", true), v => SetSort("size", true, v), _sortRebuild);
        MakeToggle(p, new Vector2(125, 30), new Vector2(160, 46), "降順", () => HasSort("size", false), v => SetSort("size", false, v), _sortRebuild);
        ProtoUI.CreateText("ZHint", p, "複数選択可（上の項目が優先）", 15, new Vector2(0, -60), new Vector2(600, 24), new Color(0.7f, 0.7f, 0.8f));
    }

    // ==================== 所持カード一覧 ====================

    void RefreshTray()
    {
        foreach (Transform c in _trayContent) Destroy(c.gameObject);
        _trayButtons.Clear();

        var owned = ApplyFilterSort(_main.OwnedCards());
        const float rowH = 76f, pad = 10f, bottomPad = 40f; // 行間を広げて各カードを金枠で囲む
        _trayContent.sizeDelta = new Vector2(0, owned.Count * rowH + pad + bottomPad);

        for (int i = 0; i < owned.Count; i++)
        {
            var c = owned[i];
            float iy = -(pad + i * rowH);   // pivot上なのでこれが各カードの上端
            // 金枠（行の背後に少し大きく・上下左右に出す）
            var border = ProtoUI.CreatePanel($"TrayB_{c.id}", _trayContent, new Vector2(0, iy),
                new Vector2(452, 66), new Color(0.85f, 0.72f, 0.4f, 0.97f));
            var brt = border.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f); brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0, iy + 5); border.raycastTarget = false; // 上に5pxずらして上下も囲む

            var img = ProtoUI.CreatePanel($"Tray_{c.id}", _trayContent, new Vector2(0, iy),
                new Vector2(440, 56), c == _selected ? TraySelected : TrayNormal);
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0, iy);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var card = c;
            btn.onClick.AddListener(() => SelectCard(card));

            // 形状ミニプレビュー
            float cs = 7f, cgap = 1f;
            var shape = c.Shape;
            int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y);
            int maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
            float ox = -200f - (maxX - minX) * (cs + cgap) / 2f;
            float oy = (maxY - minY) * (cs + cgap) / 2f;
            foreach (var v in shape)
                ProtoUI.CreatePanel("Mas", img.transform,
                    new Vector2(ox + (v.x - minX) * (cs + cgap), oy - (v.y - minY) * (cs + cgap)),
                    new Vector2(cs, cs), c.color).raycastTarget = false;

            string kindTag = CardDef.KindLabel(c.Category);
            var title = ProtoUI.CreateText("T", img.transform,
                $"{c.displayName}　<size=13>[{kindTag}] {c.Size}マス / マナ{c.ManaCost}</size>", 16,
                new Vector2(40, 12), new Vector2(360, 24), Color.white, TextAlignmentOptions.Left);
            title.raycastTarget = false;
            var desc = ProtoUI.CreateText("D", img.transform,
                c.Category == CardKind.Attack && string.IsNullOrEmpty(c.description) ? $"威力 {c.power}" :
                (string.IsNullOrEmpty(c.description) ? "" : c.description), 13,
                new Vector2(40, -13), new Vector2(360, 22), new Color(0.8f, 0.82f, 0.95f), TextAlignmentOptions.Left);
            desc.raycastTarget = false;

            // 所持数（右端）
            var cnt = ProtoUI.CreateText("Cnt", img.transform, $"×{_main.OwnedCount(c.id)}", 22,
                new Vector2(185, 0), new Vector2(60, 40), ProtoUI.Gold, TextAlignmentOptions.Right);
            cnt.fontStyle = FontStyles.Bold; cnt.raycastTarget = false;

            _trayButtons.Add((c, img));
        }

        if (_trayScroll != null) { Canvas.ForceUpdateCanvases(); _trayScroll.verticalNormalizedPosition = 1f; }
    }

    // ==================== 選択・配置・回転 ====================

    void SelectCard(CardDef c)
    {
        _selected = (_selected == c) ? null : c;
        _boardSel = null; _rotation = 0;
        UpdateSelectedText();
        foreach (var (card, img) in _trayButtons) img.color = card == _selected ? TraySelected : TrayNormal;
    }

    void UpdateSelectedText()
    {
        if (_boardSel.HasValue)
        {
            var p = P.GetAt(_boardSel.Value.x, _boardSel.Value.y);
            if (p != null) { _selectedText.text = $"選択中: {p.card.displayName}"; return; }
        }
        _selectedText.text = _selected == null ? "カード未選択"
            : $"選択中: {_selected.displayName}（{_selected.Size}マス）";
    }

    void OnAddButton()
    {
        if (!_addMode)
        {
            if (_main.CellStock <= 0) { ShowNotice("エラー：マスストックが0です！配置できません"); return; } // ストック無しは入れない
            SetAddMode(true);   // 配置開始
        }
        else ShowPlaceConfirm();           // 配置をやめる→確認
    }

    void SetAddMode(bool on)
    {
        _addMode = on;
        if (on) _sessionBaseline = P.GetUnlockedCells();   // この配置セッションの基準を記録
        if (_addBtnText != null) _addBtnText.text = on ? "配置をやめる" : "マスを配置する";
        if (_addBtnImg != null) _addBtnImg.color = on ? AddBtnOn : AddBtnOff;
        if (_resetBtnGO != null) _resetBtnGO.SetActive(on);
        _selected = null; _boardSel = null;
        foreach (var (card, img) in _trayButtons) img.color = TrayNormal;
        UpdateSelectedText(); RefreshBoard();
    }

    void OnResetCells()
    {
        _main.ResetToBaseline(_sessionBaseline);   // この配置セッションで解放した分だけ戻す
        RefreshBoard();
        ShowNotice("今回配置したマスをリセットしました");
    }

    // 「これで配置します。よろしいですか？」確認ポップアップ
    void ShowPlaceConfirm()
    {
        if (_confirmGO != null) Destroy(_confirmGO);
        var ov = ProtoUI.CreateFullScreen("PlaceConfirm", _root);
        _confirmGO = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        var box = ProtoUI.CreateFramedPanel("Box", ov, Vector2.zero, new Vector2(540, 240),
            new Color(0.10f, 0.08f, 0.16f, 0.98f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
        ProtoUI.CreateText("M", box.transform, "これで配置します。よろしいですか？", 24, new Vector2(0, 50), new Vector2(500, 40), Color.white);
        ProtoUI.CreateButton("Yes", box.transform, "はい", 22, new Vector2(-120, -50), new Vector2(190, 62),
            new Color(0.30f, 0.45f, 0.32f, 0.98f), () => { Destroy(_confirmGO); _confirmGO = null; SetAddMode(false); });
        ProtoUI.CreateButton("No", box.transform, "いいえ", 22, new Vector2(120, -50), new Vector2(190, 62),
            new Color(0.3f, 0.3f, 0.4f, 0.98f), () => { Destroy(_confirmGO); _confirmGO = null; });
    }

    void OnCellLeftClick(int x, int y)
    {
        // マス追加モード：未解放マスをクリックで解放（マスストックを1消費）
        if (_addMode)
        {
            if (!P.IsUnlocked(x, y))
            {
                if (_main.UnlockCell(x, y)) RefreshBoard();
                else ShowNotice(_main.CellStock <= 0 ? "エラー：マスストックが0です！配置できません" : "これ以上拡張できません(最大100)");
            }
            return;
        }

        var placed = P.GetAt(x, y);
        if (placed != null)
        {
            _boardSel = placed.cells[0]; _selected = null;
            foreach (var (card, img) in _trayButtons) img.color = TrayNormal;
            UpdateSelectedText(); RefreshBoard();
            return;
        }
        _boardSel = null;
        if (_selected != null)
        {
            if (_main.OwnedCount(_selected.id) <= 0) { RefreshBoard(); ShowNotice("このカードの在庫がありません"); return; }
            if (P.Place(_selected, new Vector2Int(x, y), _rotation))
            {
                _main.ConsumeCard(_selected.id);                       // 配置で在庫消費
                if (_main.OwnedCount(_selected.id) <= 0) _selected = null; // 在庫切れなら選択解除
                RefreshTray();
                UpdateSelectedText(); RefreshBoard();
            }
            else { RefreshBoard(); ShowNotice("ここには置けません！スペースが足りません"); } // 通知を上書きしない
        }
        else { UpdateSelectedText(); RefreshBoard(); }
    }

    void OnCellRightClick(int x, int y)
    {
        // ピース所持中は右クリックで回転
        if (_selected != null)
        {
            _rotation = (_rotation + 1) % 4;
            UpdateSelectedText(); RefreshBoard();
            return;
        }
        // 未所持は右クリックで撤去（在庫に戻す）
        var pl = P.GetAt(x, y);
        if (pl != null) { var id = pl.card.id; P.RemoveAt(x, y); _main.ReturnCard(id); RefreshTray(); }
        RefreshBoard(); UpdateSelectedText();
    }

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        bool rPressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) rPressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!rPressed && Input.GetKeyDown(KeyCode.R)) rPressed = true;
#endif
        if (rPressed) RotateAny();

        UpdateHoverPreview();
    }

    void RotateAny()
    {
        if (_boardSel.HasValue)
        {
            var sel = _boardSel.Value;
            if (P.RotatePlacementAt(sel.x, sel.y))
            {
                if (P.Placements.Count > 0) _boardSel = P.Placements[P.Placements.Count - 1].cells[0];
                RefreshBoard();
            }
            else ShowNotice("回転できません！まわりのスペースが足りません");
            return;
        }
        if (_selected != null) { _rotation = (_rotation + 1) % 4; UpdateSelectedText(); }
    }

    bool _hoverShown;
    Vector2Int _hoverCell; int _hoverRot; CardDef _hoverCard; // ゴースト再描画の差分判定
    void UpdateHoverPreview()
    {
        if (_selected == null || _isDragging)
        {
            if (_hoverShown) { RefreshBoard(); _hoverShown = false; _hoverCard = null; }
            return;
        }
        if (!TryGetPointerPos(out var screenPos) || !TryGetCellAtScreenPoint(screenPos, null, out var cell))
        {
            if (_hoverShown) { RefreshBoard(); _hoverShown = false; _hoverCard = null; }
            return;
        }
        // マス・回転・選択カードが前フレームと同じなら描画し直さない（毎フレームのRefreshBoardを回避）
        var cv = new Vector2Int(cell.x, cell.y);
        if (_hoverShown && _hoverCell == cv && _hoverRot == _rotation && _hoverCard == _selected) return;
        _hoverCell = cv; _hoverRot = _rotation; _hoverCard = _selected;

        RefreshBoard(); _hoverShown = true;

        bool valid = P.CanPlace(_selected, new Vector2Int(cell.x, cell.y), _rotation);
        Color baseCol = valid ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.35f, 0.35f);
        foreach (var c in P.Cells(_selected, new Vector2Int(cell.x, cell.y), _rotation))
            if (P.IsValid(c.x, c.y) && P.GetAt(c.x, c.y) == null)
                _cellImages[c.x, c.y].color = valid ? Color.Lerp(_selected.color, baseCol, 0.5f) : baseCol;
    }

    bool TryGetPointerPos(out Vector2 pos)
    {
        pos = default;
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null) { pos = mouse.position.ReadValue(); return true; }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pos = Input.mousePosition; return true;
#else
        return pos != default;
#endif
    }

    // ---- ドラッグ移動 ----
    void OnCellBeginDrag(int x, int y)
    {
        var p = P.GetAt(x, y);
        if (p == null) return;
        _isDragging = true; _boardSel = null; UpdateSelectedText();
        _dragCard = p.card; _dragOrigCells = new List<Vector2Int>(p.cells); _dragGrabCell = new Vector2Int(x, y);
        P.RemoveAt(x, y); RefreshBoard();
    }

    void OnCellDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        if (!_isDragging) return;
        RefreshBoard();
        if (!TryGetCellUnderPointer(e, out var cell)) return;
        var delta = cell - _dragGrabCell;
        var candidate = _dragOrigCells.Select(c => c + delta).ToList();
        bool valid = P.CanPlaceCells(candidate);
        Color preview = valid ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.35f, 0.35f);
        foreach (var c in candidate) if (P.IsValid(c.x, c.y)) _cellImages[c.x, c.y].color = preview;
    }

    void OnCellEndDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        bool placed = false;
        if (TryGetCellUnderPointer(e, out var cell))
        {
            var delta = cell - _dragGrabCell;
            placed = P.PlaceCells(_dragCard, _dragOrigCells.Select(c => c + delta).ToList());
        }
        if (!placed) { P.PlaceCells(_dragCard, _dragOrigCells); ShowNotice("そこには動かせません！元の場所に戻しました"); }
        RefreshBoard();
    }

    bool TryGetCellUnderPointer(UnityEngine.EventSystems.PointerEventData e, out Vector2Int cell)
        => TryGetCellAtScreenPoint(e.position, e.pressEventCamera, out cell);

    bool TryGetCellAtScreenPoint(Vector2 screenPos, Camera cam, out Vector2Int cell)
    {
        cell = default;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_gridRt, screenPos, cam, out var local))
            return false;
        float unit = _cellSize + CellGap;
        float gw = P.W * unit, gh = P.H * unit;
        int x = Mathf.FloorToInt((local.x + gw / 2f) / unit);
        int y = Mathf.FloorToInt((gh / 2f - local.y) / unit);
        if (x < 0 || y < 0 || x >= P.W || y >= P.H) return false;
        cell = new Vector2Int(x, y);
        return true;
    }

    Coroutine _noticeCo;
    void ShowNotice(string msg)
    {
        if (_noticeCo != null) StopCoroutine(_noticeCo);
        _noticeCo = StartCoroutine(NoticeRoutine(msg));
    }
    IEnumerator NoticeRoutine(string msg)
    {
        _selectedText.text = $"<color=#FF7070>{msg}</color>";
        yield return new WaitForSeconds(1.5f);
        _noticeCo = null; UpdateSelectedText();
    }

    // ==================== 表示更新 ====================

    void RefreshBoard()
    {
        var panel = P;
        for (int x = 0; x < panel.W; x++)
            for (int y = 0; y < panel.H; y++)
            {
                if (!panel.IsUnlocked(x, y))
                {
                    _cellImages[x, y].color = _addMode ? LockedAddColor : LockedColor; // 未解放
                    continue;
                }
                var p = panel.GetAt(x, y);
                _cellImages[x, y].color = p == null ? EmptyColor : p.card.color;
            }

        if (_boardSel.HasValue)
        {
            var sel = panel.GetAt(_boardSel.Value.x, _boardSel.Value.y);
            if (sel == null) _boardSel = null;
            else foreach (var c in sel.cells)
                _cellImages[c.x, c.y].color = Color.Lerp(sel.card.color, Color.white, 0.45f);
        }

        int total = panel.ValidCount();
        int occupied = panel.OccupiedCount();
        var counts = panel.CountByCard();

        if (_boardCountText != null) _boardCountText.text = $"{total}/{ProtoMain.MaxCells}";
        if (_manaInfoText != null) _manaInfoText.text = $"{_main.MaxMana}";
        if (_stockText != null) _stockText.text = $"{_main.CellStock}";

        var parts = new List<string> { $"通常攻撃: {Pct(total - occupied, total)}%" };
        foreach (var kv in counts)
        {
            string hex = ColorUtility.ToHtmlStringRGB(kv.Key.color);
            parts.Add($"<color=#{hex}>{kv.Key.displayName}: {Pct(kv.Value, total)}%（マナ{kv.Key.ManaCost}）</color>");
        }
        _info.text = string.Join("\n", parts);
    }

    static int Pct(int n, int total) => total > 0 ? Mathf.RoundToInt(100f * n / total) : 0;
}

// カーソルを合わせている間だけ target を表示するツールチップ用ハンドラ
public class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject target;
    public void OnPointerEnter(PointerEventData e) { if (target != null) target.SetActive(true); }
    public void OnPointerExit(PointerEventData e) { if (target != null) target.SetActive(false); }
    void OnDisable() { if (target != null) target.SetActive(false); }
}
