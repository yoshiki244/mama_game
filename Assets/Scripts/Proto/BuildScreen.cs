using UnityEngine;
using UnityEngine.UI;
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
    TextMeshProUGUI _title, _info, _selectedText, _manaText;
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
    static readonly Color TraySelected = new Color(0.45f, 0.35f, 0.65f);
    static readonly Color TrayNormal = new Color(0.22f, 0.18f, 0.32f);

    public void Init(ProtoMain main) { _main = main; BuildUI(); Hide(); }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        RefreshTray();
        BuildBoard();
        RefreshBoard();
    }

    public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("BuildScreen", _main.Canvas.transform);

        _title = ProtoUI.CreateText("Title", _root, "ビルド画面", 34, new Vector2(-330, 410), new Vector2(560, 50));
        ProtoUI.StyleTitle(_title, ProtoUI.Gold, 6f);

        _manaText = ProtoUI.CreateText("Mana", _root, "", 20, new Vector2(-330, 360), new Vector2(560, 30),
            new Color(0.7f, 0.85f, 1f));

        // 盤面の土台
        _boardBg = ProtoUI.CreatePanel("Board", _root, new Vector2(-330, 70),
            new Vector2(BoardArea, BoardArea), new Color(0.05f, 0.04f, 0.10f, 0.6f));
        _boardBg.raycastTarget = false;
        var grid = ProtoUI.CreateRect("Grid", _boardBg.transform);
        _gridRt = grid;
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

        // ピース一覧（右・スクロール）
        ProtoUI.CreateText("TrayTitle", _root, "所持カード（クリックで選択）", 22,
            new Vector2(330, 360), new Vector2(440, 30));

        var viewport = ProtoUI.CreateRect("TrayViewport", _root);
        viewport.anchoredPosition = new Vector2(330, 110);
        viewport.sizeDelta = new Vector2(470, 440);
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

        _selectedText = ProtoUI.CreateText("Selected", _root, "カード未選択", 18,
            new Vector2(230, -160), new Vector2(300, 60));
        ProtoUI.CreateButton("RotateBtn", _root, "回転", 20,
            new Vector2(470, -160), new Vector2(120, 44), new Color(0.3f, 0.25f, 0.45f), RotateAny);

        ProtoUI.CreateText("Hint", _root,
            "カード選択→盤面でマウス移動でプレビュー／左クリックで確定\n右クリック=撤去　ドラッグ=移動　盤面のカードをクリックで選択→Rキーで回転\n出現率 = カードのマス数 ÷ 盤面マス数　空白マスは「通常攻撃」", 14,
            new Vector2(330, -235), new Vector2(470, 80), new Color(0.7f, 0.7f, 0.8f));

        _info = ProtoUI.CreateText("Info", _root, "", 16,
            new Vector2(-330, -250), new Vector2(560, 230), new Color(0.85f, 0.8f, 1f), TextAlignmentOptions.Top);

        ProtoUI.CreateButton("BackBtn", _root, "マップへ戻る", 22,
            new Vector2(330, -330), new Vector2(260, 64), new Color(0.7f, 0.3f, 0.45f), () => _main.ShowMap());
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

    // ==================== 所持カード一覧 ====================

    void RefreshTray()
    {
        foreach (Transform c in _trayContent) Destroy(c.gameObject);
        _trayButtons.Clear();

        var owned = _main.OwnedCards();
        const float rowH = 64f, pad = 6f;
        _trayContent.sizeDelta = new Vector2(0, owned.Count * rowH + pad * 2);

        for (int i = 0; i < owned.Count; i++)
        {
            var c = owned[i];
            float iy = -(pad + rowH / 2f + i * rowH);
            var img = ProtoUI.CreatePanel($"Tray_{c.id}", _trayContent, new Vector2(0, iy),
                new Vector2(440, 58), c == _selected ? TraySelected : TrayNormal);
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

            string kindTag = c.kind == CardKind.Attack ? "攻撃" : "スキル";
            var title = ProtoUI.CreateText("T", img.transform,
                $"{c.displayName}　<size=13>[{kindTag}] {c.Size}マス / マナ{c.ManaCost}</size>", 16,
                new Vector2(40, 12), new Vector2(360, 24), Color.white, TextAlignmentOptions.Left);
            title.raycastTarget = false;
            var desc = ProtoUI.CreateText("D", img.transform,
                c.kind == CardKind.Attack && string.IsNullOrEmpty(c.description) ? $"威力 {c.power}" :
                (string.IsNullOrEmpty(c.description) ? "" : c.description), 13,
                new Vector2(40, -13), new Vector2(360, 22), new Color(0.8f, 0.82f, 0.95f), TextAlignmentOptions.Left);
            desc.raycastTarget = false;

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
            if (p != null) { _selectedText.text = $"選択中: {p.card.displayName}\nRキーで回転"; return; }
        }
        _selectedText.text = _selected == null ? "カード未選択"
            : $"選択中: {_selected.displayName}（マナ{_selected.ManaCost}）\n回転 {_rotation * 90}°";
    }

    void OnCellLeftClick(int x, int y)
    {
        var placed = P.GetAt(x, y);
        if (placed != null)
        {
            _boardSel = placed.cells[0]; _selected = null;
            foreach (var (card, img) in _trayButtons) img.color = TrayNormal;
            UpdateSelectedText(); RefreshBoard();
            return;
        }
        _boardSel = null;
        bool failed = _selected != null && !P.Place(_selected, new Vector2Int(x, y), _rotation);
        UpdateSelectedText(); RefreshBoard();
        if (failed) ShowNotice("ここには置けません！スペースが足りません");
    }

    void OnCellRightClick(int x, int y) { P.RemoveAt(x, y); RefreshBoard(); UpdateSelectedText(); }

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
    void UpdateHoverPreview()
    {
        if (_selected == null || _isDragging)
        {
            if (_hoverShown) { RefreshBoard(); _hoverShown = false; }
            return;
        }
        if (!TryGetPointerPos(out var screenPos) || !TryGetCellAtScreenPoint(screenPos, null, out var cell))
        {
            if (_hoverShown) { RefreshBoard(); _hoverShown = false; }
            return;
        }
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

        _manaText.text = $"盤面 {panel.W}×{panel.H}（{total}マス）　最大マナ {_main.MaxMana}　お金 {_main.Money}";

        var parts = new List<string> { $"通常攻撃: {Pct(total - occupied, total)}%" };
        foreach (var kv in counts)
        {
            string hex = ColorUtility.ToHtmlStringRGB(kv.Key.color);
            parts.Add($"<color=#{hex}>{kv.Key.displayName}: {Pct(kv.Value, total)}%（マナ{kv.Key.ManaCost}）</color>");
        }
        _info.text = $"占有 {occupied}/{total} マス\n" + string.Join("\n", parts);
    }

    static int Pct(int n, int total) => total > 0 ? Mathf.RoundToInt(100f * n / total) : 0;
}
