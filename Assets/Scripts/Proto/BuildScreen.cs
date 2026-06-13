using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// ビルド画面: メンバーごとの盤面にピースを配置する（GDD USP①スキルパネルビルド）
// 盤面の形はキャラごとに違う（どれも100マス）。上部タブでメンバーを切り替える。
// 左クリック=配置 / 右クリック=撤去 / ドラッグ=移動 / クリック選択→Rキー=回転
public class BuildScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;

    Image[,] _cellImages;
    TextMeshProUGUI _title;
    TextMeshProUGUI _info;
    TextMeshProUGUI _selectedText;
    readonly List<(ProtoSkill skill, Image img, TextMeshProUGUI label)> _trayButtons
        = new List<(ProtoSkill, Image, TextMeshProUGUI)>();

    ProtoSkill _selected;
    int _rotation;
    RectTransform _gridRt;
    RectTransform _trayContent; // スクロールするピース一覧の中身
    ScrollRect _trayScroll;
    Image _boardBg;
    Vector2Int? _boardSel; // 盤面で選択中のピース（ピボットマスで保持）

    // メンバー切り替え
    int _member;
    RectTransform _tabsRow;
    readonly List<Image> _tabImgs = new List<Image>();
    PanelModel P => _main.Panels[_member];

    // ドラッグ移動の状態
    bool _isDragging;
    ProtoSkill _dragSkill;
    List<Vector2Int> _dragOrigCells;
    Vector2Int _dragGrabCell;

    float _cellSize = 40f; // 盤面の大きさに合わせて自動調整
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
        if (_member >= _main.Panels.Count) _member = 0; // 仲間を外した直後の保険
        RefreshTabs();
        RefreshTray();   // 所持ピースが増えている可能性があるので再構築
        BuildBoard();
        RefreshBoard();
    }

    // 所持ピースの一覧を作り直す（敵撃破で増える）
    void RefreshTray()
    {
        foreach (Transform c in _trayContent) Destroy(c.gameObject);
        _trayButtons.Clear();

        var owned = _main.OwnedSkills();
        const float rowH = 56f, pad = 6f;
        _trayContent.sizeDelta = new Vector2(0, owned.Count * rowH + pad * 2);

        for (int i = 0; i < owned.Count; i++)
        {
            var s = owned[i];
            float iy = -(pad + rowH / 2f + i * rowH); // contentの上端からの相対位置
            var img = ProtoUI.CreatePanel($"Tray_{s.id}", _trayContent, new Vector2(0, iy),
                new Vector2(430, 48), s == _selected ? TraySelected : TrayNormal);
            // 上端基準にして、contentの一番上から下へ並べる
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0, iy);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SelectSkill(s));

            // ピース形状のミニプレビュー（行の左端に実際の形を表示）
            float cs = 8f, cgap = 1f;
            int minX = s.shape.Min(v => v.x), minY = s.shape.Min(v => v.y);
            int maxX = s.shape.Max(v => v.x), maxY = s.shape.Max(v => v.y);
            float ox = -195f - (maxX - minX) * (cs + cgap) / 2f;
            float oy = (maxY - minY) * (cs + cgap) / 2f;
            foreach (var v in s.shape)
            {
                ProtoUI.CreatePanel("Mas", img.transform,
                    new Vector2(ox + (v.x - minX) * (cs + cgap), oy - (v.y - minY) * (cs + cgap)),
                    new Vector2(cs, cs), s.color).raycastTarget = false;
            }
            var label = ProtoUI.CreateText("Label", img.transform,
                $"{s.skillName}　{s.Size}マス / 威力 {s.power}", 16,
                new Vector2(40, 0), new Vector2(330, 48), Color.white, TextAlignmentOptions.Left);

            _trayButtons.Add((s, img, label));
        }

        // スクロール位置を一番上に戻す（増減後に下に張り付くのを防ぐ）
        if (_trayScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            _trayScroll.verticalNormalizedPosition = 1f;
        }
    }

    public void Hide() => _root.gameObject.SetActive(false);

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("BuildScreen", _main.Canvas.transform);

        _title = ProtoUI.CreateText("Title", _root, "ビルド画面", 34, new Vector2(0, 410), new Vector2(800, 50));
        ProtoUI.StyleTitle(_title, ProtoUI.Gold, 8f);

        // メンバー切り替えタブ（盤面の上）
        _tabsRow = ProtoUI.CreateRect("Tabs", _root);
        _tabsRow.anchoredPosition = new Vector2(-330, 345);
        _tabsRow.sizeDelta = new Vector2(700, 50);

        // 盤面の土台（透明の入れ物。マス自体が盤面の見た目になる）
        _boardBg = ProtoUI.CreatePanel("Board", _root, new Vector2(-330, 85),
            new Vector2(440, 440), Color.clear);
        _boardBg.raycastTarget = false;
        var grid = ProtoUI.CreateRect("Grid", _boardBg.transform);
        _gridRt = grid;
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

        // ---- ピース一覧（右側・スクロール可能） ----
        ProtoUI.CreateText("TrayTitle", _root, "所持ピース（クリックで選択）", 22,
            new Vector2(330, 335), new Vector2(440, 30));

        // スクロールビュー（所持ピースが増えても見切れない）
        var viewport = ProtoUI.CreateRect("TrayViewport", _root);
        viewport.anchoredPosition = new Vector2(330, 95);
        viewport.sizeDelta = new Vector2(460, 440);
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0.08f, 0.06f, 0.14f, 0.55f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var sr = viewport.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.viewport = viewport;
        sr.scrollSensitivity = 28f;
        sr.movementType = ScrollRect.MovementType.Clamped;

        _trayContent = ProtoUI.CreateRect("TrayContent", viewport);
        _trayContent.anchorMin = new Vector2(0f, 1f);   // 上端で横ストレッチ（ScrollRect標準）
        _trayContent.anchorMax = new Vector2(1f, 1f);
        _trayContent.pivot = new Vector2(0.5f, 1f);
        _trayContent.anchoredPosition = Vector2.zero;
        _trayContent.sizeDelta = new Vector2(0, 10);
        sr.content = _trayContent;
        _trayScroll = sr;

        // 選択中表示と回転ボタン（スクロールビューの下に固定）
        _selectedText = ProtoUI.CreateText("Selected", _root, "ピース未選択", 18,
            new Vector2(230, -175), new Vector2(300, 50));
        ProtoUI.CreateButton("RotateBtn", _root, "回転", 20,
            new Vector2(470, -175), new Vector2(120, 44), new Color(0.3f, 0.25f, 0.45f), RotateAny);

        // 操作ヒント
        ProtoUI.CreateText("Hint", _root, "ピース選択→盤面でマウス移動するとプレビュー表示／左クリックで確定\n右クリック=撤去　ドラッグ=移動　盤面のピースをクリックで選択→Rキーで回転\n1マス=出現率1%　空白マスは「通常攻撃」になる", 14,
            new Vector2(330, -245), new Vector2(460, 70), new Color(0.7f, 0.7f, 0.8f));

        // 出現確率の内訳（盤面の下。スキルごとに1行・上揃え）
        _info = ProtoUI.CreateText("Info", _root, "", 17,
            new Vector2(-330, -270), new Vector2(720, 230), new Color(0.85f, 0.8f, 1f),
            TextAlignmentOptions.Top);

        // メニューへ戻るボタン
        ProtoUI.CreateButton("BackBtn", _root, "メニューへ戻る", 24,
            new Vector2(330, -330), new Vector2(260, 70), new Color(0.7f, 0.3f, 0.45f),
            () => _main.ShowMenu());
    }

    // ==================== メンバータブ ====================

    void RefreshTabs()
    {
        foreach (Transform c in _tabsRow) Destroy(c.gameObject);
        _tabImgs.Clear();

        var party = _main.Party;
        float xs = -(party.Count - 1) * 150f / 2f;
        for (int i = 0; i < party.Count; i++)
        {
            int mi = i;
            var btn = ProtoUI.CreateButton($"Tab_{party[i].name}", _tabsRow, party[i].name, 18,
                new Vector2(xs + i * 150f, 0), new Vector2(140, 44), TrayNormal,
                () => SwitchMember(mi));
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            label.color = Color.Lerp(party[i].hair, Color.white, 0.3f);
            _tabImgs.Add((Image)btn.targetGraphic);
        }
        UpdateTabHighlight();
    }

    void UpdateTabHighlight()
    {
        for (int i = 0; i < _tabImgs.Count; i++)
            _tabImgs[i].color = i == _member ? TraySelected : TrayNormal;
        _title.text = $"ビルド — {_main.Party[_member].name}";
    }

    void SwitchMember(int index)
    {
        _member = index;
        _boardSel = null;
        _isDragging = false;
        UpdateTabHighlight();
        UpdateSelectedText();
        BuildBoard();
        RefreshBoard();
    }

    // ==================== 盤面の構築（メンバーごとに形が違う） ====================

    void BuildBoard()
    {
        foreach (Transform c in _gridRt) Destroy(c.gameObject);

        int w = P.W, h = P.H;
        _cellSize = 40f; // どの形でもMAMAの盤面と同じマスサイズ

        var layout = _gridRt.GetComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(_cellSize, _cellSize);
        layout.spacing = new Vector2(CellGap, CellGap);
        layout.constraintCount = w;

        float unit = _cellSize + CellGap;
        _gridRt.sizeDelta = new Vector2(w * unit, h * unit);
        ((RectTransform)_boardBg.transform).sizeDelta = new Vector2(w * unit + CellGap * 2, h * unit + CellGap * 2);

        _cellImages = new Image[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int cx = x, cy = y;
                var cell = ProtoUI.CreatePanel($"Cell_{x}_{y}", _gridRt, Vector2.zero,
                    new Vector2(_cellSize, _cellSize), EmptyColor);
                _cellImages[x, y] = cell;

                if (!P.IsValid(x, y))
                {
                    // 盤面の形の外側: 透明＆クリック不可（グリッド整列のための見えない埋め草）
                    cell.color = Color.clear;
                    cell.raycastTarget = false;
                    continue;
                }

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
            }
        }
    }

    // ==================== 選択・配置・回転 ====================

    void SelectSkill(ProtoSkill s)
    {
        _selected = (_selected == s) ? null : s; // 同じピースをもう一度クリックで選択解除
        _boardSel = null;
        _rotation = 0;
        UpdateSelectedText();
        foreach (var (skill, img, _) in _trayButtons)
            img.color = skill == _selected ? TraySelected : TrayNormal;
    }

    void RotateSelected()
    {
        _rotation = (_rotation + 1) % 4;
        UpdateSelectedText();
    }

    void UpdateSelectedText()
    {
        if (_boardSel.HasValue)
        {
            var p = P.GetAt(_boardSel.Value.x, _boardSel.Value.y);
            if (p != null)
            {
                _selectedText.text = $"選択中: {p.skill.skillName}\nRキーで回転";
                return;
            }
        }
        _selectedText.text = _selected == null
            ? "ピース未選択"
            : $"選択中: {_selected.skillName}\n回転 {_rotation * 90}°";
    }

    void OnCellLeftClick(int x, int y)
    {
        var placed = P.GetAt(x, y);

        if (placed != null)
        {
            _boardSel = placed.cells[0];
            _selected = null;
            foreach (var (skill, img, _) in _trayButtons)
                img.color = TrayNormal;
            UpdateSelectedText();
            RefreshBoard();
            return;
        }

        _boardSel = null;
        bool placeFailed = _selected != null && !P.Place(_selected, new Vector2Int(x, y), _rotation);
        UpdateSelectedText();
        RefreshBoard();

        if (placeFailed)
            ShowNotice("ここには置けません！スペースが足りません");
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

    bool _hoverShown; // 前フレームでゴーストを描いたか（描いた時だけ盤面を戻す）

    // ピース選択中、マウス位置に配置ゴーストを表示する（左クリックで確定＝OnCellLeftClick）
    void UpdateHoverPreview()
    {
        // 配置候補を出すのは「トレイから選択中」かつ「ドラッグ中でない」ときだけ
        if (_selected == null || _isDragging)
        {
            if (_hoverShown) { RefreshBoard(); _hoverShown = false; }
            return;
        }

        if (!TryGetPointerPos(out var screenPos) ||
            !TryGetCellAtScreenPoint(screenPos, null, out var cell))
        {
            if (_hoverShown) { RefreshBoard(); _hoverShown = false; }
            return;
        }

        // 盤面を一旦描き直してから候補マスを上描き（ドラッグ移動のプレビューと同じ流儀）
        RefreshBoard();
        _hoverShown = true;

        var candidate = new List<Vector2Int>();
        foreach (var c in P.Cells(_selected, new Vector2Int(cell.x, cell.y), _rotation))
            candidate.Add(c);

        bool valid = P.CanPlace(_selected, new Vector2Int(cell.x, cell.y), _rotation);
        Color baseCol = valid ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.35f, 0.35f);
        // 置けるマスは選択ピースの色寄り・置けないマスは赤系。半透明っぽく明るめに乗せる
        foreach (var c in candidate)
            if (P.IsValid(c.x, c.y) && P.GetAt(c.x, c.y) == null)
                _cellImages[c.x, c.y].color = valid
                    ? Color.Lerp(_selected.color, baseCol, 0.5f)
                    : baseCol;
    }

    // マウス/タッチのスクリーン座標を取得（Input System / 旧Input 両対応）
    bool TryGetPointerPos(out Vector2 pos)
    {
        pos = default;
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null) { pos = mouse.position.ReadValue(); return true; }
        var touch = UnityEngine.InputSystem.Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.isPressed)
        { pos = touch.primaryTouch.position.ReadValue(); return true; }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pos = Input.mousePosition;
        return true;
#else
        return pos != default;
#endif
    }

    void RotateAny()
    {
        if (_boardSel.HasValue)
        {
            var sel = _boardSel.Value;
            if (P.RotatePlacementAt(sel.x, sel.y))
            {
                var placements = P.Placements;
                if (placements.Count > 0)
                    _boardSel = placements[placements.Count - 1].cells[0];
                RefreshBoard();
            }
            else
            {
                ShowNotice("回転できません！まわりのスペースが足りません");
            }
            return;
        }
        RotateSelected();
    }

    // 一時的なエラーメッセージ（1.5秒後に元の表示へ戻る）
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
        _noticeCo = null;
        UpdateSelectedText();
    }

    void OnCellRightClick(int x, int y)
    {
        P.RemoveAt(x, y);
        RefreshBoard();
        UpdateSelectedText();
    }

    // ---- 配置済みピースのドラッグ移動 ----

    void OnCellBeginDrag(int x, int y)
    {
        var p = P.GetAt(x, y);
        if (p == null) return;

        _isDragging = true;
        _boardSel = null;
        UpdateSelectedText();
        _dragSkill = p.skill;
        _dragOrigCells = new List<Vector2Int>(p.cells);
        _dragGrabCell = new Vector2Int(x, y);
        P.RemoveAt(x, y);
        RefreshBoard();
    }

    void OnCellDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        if (!_isDragging) return;
        RefreshBoard();

        if (!TryGetCellUnderPointer(e, out var cell)) return;

        var delta = cell - _dragGrabCell;
        var candidate = new List<Vector2Int>();
        foreach (var c in _dragOrigCells) candidate.Add(c + delta);

        bool valid = P.CanPlaceCells(candidate);
        Color preview = valid ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.35f, 0.35f);
        foreach (var c in candidate)
            if (P.IsValid(c.x, c.y))
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
            placed = P.PlaceCells(_dragSkill, candidate);
        }

        if (!placed)
        {
            P.PlaceCells(_dragSkill, _dragOrigCells);
            ShowNotice("そこには動かせません！元の場所に戻しました");
        }

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

        float unit = _cellSize + CellGap;
        float gw = P.W * unit, gh = P.H * unit;
        int x = Mathf.FloorToInt((local.x + gw / 2f) / unit);
        int y = Mathf.FloorToInt((gh / 2f - local.y) / unit);
        if (x < 0 || y < 0 || x >= P.W || y >= P.H) return false;

        cell = new Vector2Int(x, y);
        return true;
    }

    // ==================== 表示更新 ====================

    void RefreshBoard()
    {
        var panel = P;
        for (int x = 0; x < panel.W; x++)
        {
            for (int y = 0; y < panel.H; y++)
            {
                if (!panel.IsValid(x, y)) continue; // 形の外側は透明のまま
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
                _boardSel = null;
            }
            else
            {
                foreach (var c in selPlacement.cells)
                    _cellImages[c.x, c.y].color = Color.Lerp(selPlacement.skill.color, Color.white, 0.45f);
            }
        }

        // 盤面下に出現確率の内訳を表示
        int total = panel.ValidCount();
        int occupied = panel.OccupiedCount();
        var counts = panel.CountBySkill();

        var parts = new List<string> { $"通常攻撃: {total - occupied}%" };
        foreach (var skill in ProtoSkills.All)
        {
            if (counts.TryGetValue(skill, out int pct))
            {
                string hex = ColorUtility.ToHtmlStringRGB(skill.color);
                parts.Add($"<color=#{hex}>{skill.skillName}: {pct}%</color>");
            }
        }
        _info.text = $"占有 {occupied}/{total} マス\n" + string.Join("\n", parts);
    }
}
