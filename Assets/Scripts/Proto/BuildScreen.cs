using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// ビルド画面: 10x10盤面にピースを配置する（GDD USP①スキルパネルビルド）
// 左クリック=配置 / 右クリック=撤去 / 回転ボタンでピース回転
public class BuildScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;

    Image[,] _cellImages;
    TextMeshProUGUI _title;
    TextMeshProUGUI _info;
    TextMeshProUGUI _selectedText;
    readonly List<(ProtoSkill skill, Image img)> _trayButtons = new List<(ProtoSkill, Image)>();

    ProtoSkill _selected;
    int _rotation;
    RectTransform _gridRt;
    Vector2Int? _boardSel; // 盤面で選択中のピース（ピボットマスで保持）

    // ドラッグ移動の状態
    bool _isDragging;
    ProtoSkill _dragSkill;
    List<Vector2Int> _dragOrigCells;
    Vector2Int _dragGrabCell;

    const float CellSize = 40f;
    const float CellGap = 3f;

    static readonly Color EmptyColor = new Color(0.16f, 0.14f, 0.24f);
    static readonly Color TraySelected = new Color(0.45f, 0.35f, 0.65f);
    static readonly Color TrayNormal = new Color(0.22f, 0.18f, 0.32f);

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        Hide();
    }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        _title.text = $"ビルド画面 — Wave {_main.Wave}";
        RefreshBoard();
    }

    public void Hide() => _root.gameObject.SetActive(false);

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("BuildScreen", _main.Canvas.transform);

        _title = ProtoUI.CreateText("Title", _root, "ビルド画面", 34, new Vector2(0, 410), new Vector2(800, 50));

        // ---- 盤面（左側） ----
        var board = ProtoUI.CreatePanel("Board", _root, new Vector2(-330, -20),
            new Vector2(10 * (CellSize + CellGap) + CellGap, 10 * (CellSize + CellGap) + CellGap),
            new Color(0.08f, 0.07f, 0.14f));

        var grid = ProtoUI.CreateRect("Grid", board.transform);
        grid.sizeDelta = new Vector2(10 * (CellSize + CellGap), 10 * (CellSize + CellGap));
        _gridRt = grid;
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(CellSize, CellSize);
        layout.spacing = new Vector2(CellGap, CellGap);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 10;

        _cellImages = new Image[10, 10];
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                int cx = x, cy = y;
                var cell = ProtoUI.CreatePanel($"Cell_{x}_{y}", grid, Vector2.zero,
                    new Vector2(CellSize, CellSize), EmptyColor);
                var handler = cell.gameObject.AddComponent<CellClickHandler>();
                handler.onClick = e =>
                {
                    if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
                        OnCellLeftClick(cx, cy);
                    else if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                        OnCellRightClick(cx, cy);
                };
                handler.onBeginDrag = e => OnCellBeginDrag(cx, cy);
                handler.onDrag = OnCellDrag;
                handler.onEndDrag = OnCellEndDrag;
                _cellImages[x, y] = cell;
            }
        }

        // ---- ピース一覧（右側） ----
        ProtoUI.CreateText("TrayTitle", _root, "ピース一覧（クリックで選択）", 22,
            new Vector2(330, 330), new Vector2(420, 30));

        float ty = 280;
        foreach (var skill in ProtoSkills.All)
        {
            var s = skill;
            var img = ProtoUI.CreatePanel($"Tray_{s.id}", _root, new Vector2(330, ty),
                new Vector2(420, 48), TrayNormal);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SelectSkill(s));

            // 色見本
            ProtoUI.CreatePanel("Swatch", img.transform, new Vector2(-185, 0), new Vector2(28, 28), s.color);
            ProtoUI.CreateText("Label", img.transform,
                $"{s.skillName}　{s.Size}マス / 威力 {s.power}", 19,
                new Vector2(20, 0), new Vector2(360, 48), Color.white, TextAlignmentOptions.Left);

            _trayButtons.Add((s, img));
            ty -= 56;
        }

        // 選択中表示と回転ボタン
        _selectedText = ProtoUI.CreateText("Selected", _root, "ピース未選択", 20,
            new Vector2(250, ty - 10), new Vector2(280, 40));
        ProtoUI.CreateButton("RotateBtn", _root, "回転", 20,
            new Vector2(460, ty - 10), new Vector2(120, 44), new Color(0.3f, 0.25f, 0.45f), RotateAny);

        // 操作ヒント
        ProtoUI.CreateText("Hint", _root, "左クリック=配置　右クリック=撤去　ドラッグ=移動\n盤面のピースをクリックで選択（光る）→ Rキーで回転\n1マス=出現率1%　空白マスは「通常攻撃」になる", 14,
            new Vector2(330, ty - 75), new Vector2(460, 70), new Color(0.7f, 0.7f, 0.8f));

        // 占有率表示
        _info = ProtoUI.CreateText("Info", _root, "", 18,
            new Vector2(-330, -310), new Vector2(460, 80), new Color(0.85f, 0.8f, 1f));

        // 出撃ボタン
        ProtoUI.CreateButton("StartBtn", _root, "出撃！", 28,
            new Vector2(330, -330), new Vector2(260, 70), new Color(0.7f, 0.3f, 0.45f),
            () => _main.StartBattle());
    }

    void SelectSkill(ProtoSkill s)
    {
        _selected = (_selected == s) ? null : s; // 同じピースをもう一度クリックで選択解除
        _boardSel = null; // トレイを触ったら盤面側の選択は解除
        _rotation = 0;
        UpdateSelectedText();
        foreach (var (skill, img) in _trayButtons)
            img.color = skill == _selected ? TraySelected : TrayNormal;
    }

    void RotateSelected()
    {
        _rotation = (_rotation + 1) % 4;
        UpdateSelectedText();
    }

    void UpdateSelectedText()
    {
        _selectedText.text = _selected == null
            ? "ピース未選択"
            : $"選択中: {_selected.skillName}（回転 {_rotation * 90}°）";
    }

    void OnCellLeftClick(int x, int y)
    {
        var placed = _main.Panel.GetAt(x, y);

        // 配置済みピースをクリック → そのピースを選択（活性化）
        if (placed != null)
        {
            _boardSel = placed.cells[0]; // ピボットマスで記憶（回転後も追従できる）
            _selected = null;            // トレイ選択は解除
            UpdateSelectedText();
            foreach (var (skill, img) in _trayButtons)
                img.color = TrayNormal;
            RefreshBoard();
            return;
        }

        // 空きマスをクリック → トレイ選択中のピースを配置（選択は解除）
        _boardSel = null;
        if (_selected != null && _main.Panel.Place(_selected, new Vector2Int(x, y), _rotation))
            RefreshBoard();
        else
            RefreshBoard(); // 選択解除の反映
    }

    // Rキー: 盤面で選択中のピースを回転。なければトレイ選択中のピースを回転
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
    }

    // 回転の共通処理（Rキーと「回転」ボタンの両方から呼ばれる）
    void RotateAny()
    {
        if (_boardSel.HasValue)
        {
            var sel = _boardSel.Value;
            if (_main.Panel.RotatePlacementAt(sel.x, sel.y))
            {
                // 回転後のピース（リスト末尾に追加される）を選択し直す
                var placements = _main.Panel.Placements;
                if (placements.Count > 0)
                    _boardSel = placements[placements.Count - 1].cells[0];
                RefreshBoard();
            }
            return;
        }
        RotateSelected();
    }

    void OnCellRightClick(int x, int y)
    {
        _main.Panel.RemoveAt(x, y);
        RefreshBoard();
    }

    // ---- 配置済みピースのドラッグ移動 ----

    void OnCellBeginDrag(int x, int y)
    {
        var p = _main.Panel.GetAt(x, y);
        if (p == null) return;

        _isDragging = true;
        _boardSel = null; // ドラッグ開始で選択解除
        _dragSkill = p.skill;
        _dragOrigCells = new List<Vector2Int>(p.cells);
        _dragGrabCell = new Vector2Int(x, y);
        _main.Panel.RemoveAt(x, y); // 一旦盤面から持ち上げる
        RefreshBoard();
    }

    void OnCellDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        if (!_isDragging) return;
        RefreshBoard();

        if (!TryGetCellUnderPointer(e, out var cell)) return;

        // 掴んだマスを基準に移動先のマス集合を計算してプレビュー表示
        var delta = cell - _dragGrabCell;
        var candidate = new List<Vector2Int>();
        foreach (var c in _dragOrigCells) candidate.Add(c + delta);

        bool valid = _main.Panel.CanPlaceCells(candidate);
        Color preview = valid ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.35f, 0.35f);
        foreach (var c in candidate)
            if (c.x >= 0 && c.y >= 0 && c.x < 10 && c.y < 10)
                _cellImages[c.x, c.y].color = preview;
    }

    void OnCellEndDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        bool placed = false;
        if (TryGetCellUnderPointer(e, out var cell))
        {
            var delta = cell - _dragGrabCell;
            var candidate = new List<Vector2Int>();
            foreach (var c in _dragOrigCells) candidate.Add(c + delta);
            placed = _main.Panel.PlaceCells(_dragSkill, candidate);
        }

        if (!placed)
            _main.Panel.PlaceCells(_dragSkill, _dragOrigCells); // 置けなければ元の場所に戻す

        RefreshBoard();
    }

    // ポインタ座標 → 盤面のマス座標
    bool TryGetCellUnderPointer(UnityEngine.EventSystems.PointerEventData e, out Vector2Int cell)
        => TryGetCellAtScreenPoint(e.position, e.pressEventCamera, out cell);

    bool TryGetCellAtScreenPoint(Vector2 screenPos, Camera cam, out Vector2Int cell)
    {
        cell = default;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _gridRt, screenPos, cam, out var local))
            return false;

        float unit = CellSize + CellGap;
        float gw = 10 * unit, gh = 10 * unit;
        int x = Mathf.FloorToInt((local.x + gw / 2f) / unit);
        int y = Mathf.FloorToInt((gh / 2f - local.y) / unit);
        if (x < 0 || y < 0 || x >= 10 || y >= 10) return false;

        cell = new Vector2Int(x, y);
        return true;
    }

    void RefreshBoard()
    {
        var panel = _main.Panel;
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                var p = panel.GetAt(x, y);
                _cellImages[x, y].color = p == null ? EmptyColor : p.skill.color;
            }
        }

        // 盤面で選択中のピースを白寄りに光らせる（活性化表示）
        if (_boardSel.HasValue)
        {
            var selPlacement = panel.GetAt(_boardSel.Value.x, _boardSel.Value.y);
            if (selPlacement == null)
            {
                _boardSel = null; // 撤去などで消えていたら選択解除
            }
            else
            {
                foreach (var c in selPlacement.cells)
                    _cellImages[c.x, c.y].color = Color.Lerp(selPlacement.skill.color, Color.white, 0.45f);
            }
        }

        int occupied = panel.OccupiedCount();
        var lines = new List<string> { $"占有 {occupied}/100 マス（スキル発動率 {occupied}% / 通常攻撃 {100 - occupied}%）" };
        foreach (var kv in panel.CountBySkill())
            lines.Add($"{kv.Key.skillName}: {kv.Value}%");
        _info.text = string.Join("　", lines);
    }
}
