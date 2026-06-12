using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  スキルパネル管理
//  10×10グリッドへのピース配置と
//  確率マップの生成を担当する
// ─────────────────────────────────────────────
public class SkillPanelManager : MonoBehaviour
{
    // ── Inspector 設定 ──────────────────────
    [Header("グリッド設定")]
    public int gridWidth  = 10;
    public int gridHeight = 10;

    [Header("UI参照")]
    public Transform  gridParent;
    public GameObject cellPrefab;
    public Transform  pieceListParent;
    public GameObject pieceButtonPrefab;

    [Header("所持スキルリスト（仮データ）")]
    public List<SkillData> ownedSkills = new List<SkillData>();

    // ── 内部データ ───────────────────────────
    private SkillData[,] gridCells;
    private Image[,]     cellImages;
    private TextMeshProUGUI[,] cellLabels;
    private SkillData selectedSkill = null;

    // ─────────────────────────────────────────
    void Awake()
    {
        gridCells  = new SkillData[gridWidth, gridHeight];
        cellImages = new Image[gridWidth, gridHeight];
        cellLabels = new TextMeshProUGUI[gridWidth, gridHeight];

        // ── null チェック ──
        if (gridParent == null)
        {
            Debug.LogError("[SkillPanelManager] gridParent が設定されていません。InspectorでGridParentをセットしてください。");
            return;
        }
        if (cellPrefab == null)
        {
            Debug.LogError("[SkillPanelManager] cellPrefab が設定されていません。InspectorでCellPrefabをセットしてください。");
            return;
        }

        BuildGrid();

        if (pieceListParent != null && pieceButtonPrefab != null)
            BuildPieceList();
        else
            Debug.LogWarning("[SkillPanelManager] pieceListParent または pieceButtonPrefab が未設定です。ピース一覧は表示されません。");
    }

    // ─────────────────────────────────────────
    //  グリッドUI生成
    // ─────────────────────────────────────────
    private void BuildGrid()
    {
        foreach (Transform child in gridParent)
            Destroy(child.gameObject);

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                var cell = Instantiate(cellPrefab, gridParent);
                cell.name = $"Cell_{x}_{y}";

                cellImages[x, y] = cell.GetComponent<Image>();
                cellLabels[x, y] = cell.GetComponentInChildren<TextMeshProUGUI>();

                var btn = cell.GetComponent<Button>();
                if (btn == null)
                {
                    Debug.LogError($"[SkillPanelManager] cellPrefab に Button コンポーネントがありません。Cell_{x}_{y}");
                    continue;
                }

                int cx = x, cy = y;
                btn.onClick.AddListener(() => OnCellClicked(cx, cy));

                RefreshCell(x, y);
            }
        }
    }

    // ─────────────────────────────────────────
    //  ピース一覧UI生成
    // ─────────────────────────────────────────
    private void BuildPieceList()
    {
        foreach (Transform child in pieceListParent)
            Destroy(child.gameObject);

        var clearBtn = Instantiate(pieceButtonPrefab, pieceListParent);
        var clearTmp = clearBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (clearTmp != null) clearTmp.text = "選択解除";
        clearBtn.GetComponent<Button>()?.onClick.AddListener(() => selectedSkill = null);

        foreach (var skill in ownedSkills)
        {
            var btn = Instantiate(pieceButtonPrefab, pieceListParent);
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = skill.skillName;

            if (skill.icon != null)
            {
                var img = btn.GetComponentInChildren<Image>();
                if (img) img.sprite = skill.icon;
            }

            SkillData s = skill;
            btn.GetComponent<Button>()?.onClick.AddListener(() => selectedSkill = s);
        }
    }

    // ─────────────────────────────────────────
    //  セルクリック時の処理
    // ─────────────────────────────────────────
    private void OnCellClicked(int x, int y)
    {
        if (selectedSkill == null)
            RemovePiece(x, y);
        else
            TryPlacePiece(x, y, selectedSkill);
    }

    // ─────────────────────────────────────────
    //  ピース配置
    // ─────────────────────────────────────────
    public bool TryPlacePiece(int originX, int originY, SkillData skill)
    {
        if (skill.pieceShape == null || skill.pieceShape.Length == 0)
        {
            if (IsInBounds(originX, originY))
            {
                gridCells[originX, originY] = skill;
                RefreshCell(originX, originY);
                return true;
            }
            return false;
        }

        var targets = new List<Vector2Int>();
        foreach (var offset in skill.pieceShape)
        {
            int nx = originX + offset.x;
            int ny = originY + offset.y;
            if (!IsInBounds(nx, ny)) return false;
            if (gridCells[nx, ny] != null) return false;
            targets.Add(new Vector2Int(nx, ny));
        }

        foreach (var pos in targets)
        {
            gridCells[pos.x, pos.y] = skill;
            RefreshCell(pos.x, pos.y);
        }
        return true;
    }

    // ─────────────────────────────────────────
    //  ピース削除
    // ─────────────────────────────────────────
    public void RemovePiece(int x, int y)
    {
        var skill = gridCells[x, y];
        if (skill == null) return;

        for (int gy = 0; gy < gridHeight; gy++)
            for (int gx = 0; gx < gridWidth; gx++)
                if (gridCells[gx, gy] == skill)
                {
                    gridCells[gx, gy] = null;
                    RefreshCell(gx, gy);
                }
    }

    // ─────────────────────────────────────────
    //  セルのUI更新
    // ─────────────────────────────────────────
    private void RefreshCell(int x, int y)
    {
        var skill = gridCells[x, y];

        if (cellImages[x, y] != null)
            cellImages[x, y].color = skill != null
                ? GetSkillColor(skill.skillType)
                : Color.white;

        if (cellLabels[x, y] != null)
            cellLabels[x, y].text = (skill != null && skill.skillName.Length > 0)
                ? skill.skillName[..1]
                : "";
    }

    private Color GetSkillColor(SkillType type) => type switch
    {
        SkillType.NormalAttack => new Color(0.8f, 0.8f, 0.8f),
        SkillType.WeakAttack   => new Color(0.9f, 0.9f, 0.6f),
        SkillType.HeavyAttack  => new Color(1.0f, 0.4f, 0.4f),
        SkillType.Heal         => new Color(0.4f, 1.0f, 0.6f),
        SkillType.Buff         => new Color(0.4f, 0.7f, 1.0f),
        SkillType.Debuff       => new Color(0.7f, 0.4f, 1.0f),
        _                      => Color.white,
    };

    // ─────────────────────────────────────────
    //  確率マップの生成（BattleManager から呼ぶ）
    // ─────────────────────────────────────────
    public Dictionary<SkillData, float> BuildProbabilityMap()
    {
        var count = new Dictionary<SkillData, int>();
        int total = gridWidth * gridHeight;

        for (int y = 0; y < gridHeight; y++)
            for (int x = 0; x < gridWidth; x++)
            {
                var s = gridCells[x, y];
                if (s == null) continue;
                if (!count.ContainsKey(s)) count[s] = 0;
                count[s]++;
            }

        var result = new Dictionary<SkillData, float>();
        foreach (var kv in count)
            result[kv.Key] = (float)kv.Value / total;

        return result;
    }

    public float EmptyCellRatio()
    {
        int empty = 0;
        for (int y = 0; y < gridHeight; y++)
            for (int x = 0; x < gridWidth; x++)
                if (gridCells[x, y] == null) empty++;
        return (float)empty / (gridWidth * gridHeight);
    }

    private bool IsInBounds(int x, int y) =>
