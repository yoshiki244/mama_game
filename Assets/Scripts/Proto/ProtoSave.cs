using UnityEngine;
using System.Collections.Generic;
using System.IO;

// セーブ/ロード（persistentDataPath にJSONファイル・バージョン内蔵）。
// ランの途中状態（Wave/HP/最大HP/深度/アセンション/マップ）まで丸ごと保存する。
public static class ProtoSave
{
    const int Version = 5;
    const string LegacyKey = "mama_save_v4"; // 旧PlayerPrefs形式（読み込みのみ対応）
    static string FilePath => Path.Combine(Application.persistentDataPath, "mama_save.json");

    [System.Serializable]
    public class PieceSave { public string cardId; public int[] xs; public int[] ys; }

    [System.Serializable]
    public class SaveData
    {
        public int version;
        public int money;
        public int cellStock;          // ストックマス
        public int equip;              // 装備（EquipKind）
        public int[] ux;               // 解放マスX
        public int[] uy;               // 解放マスY
        public List<string> owned = new List<string>();
        public List<int> ownedCounts = new List<int>();   // owned と同じ順の在庫数
        public List<PieceSave> pieces = new List<PieceSave>();
        public List<string> growIds = new List<string>();    // 成長カードid
        public List<int> growLevels = new List<int>();       // growIdsと同じ順の成長段階

        // ---- ランの途中状態（v5） ----
        public int wave = 1;
        public int curHP = -1;         // -1=未保存（旧データ）
        public int maxHP = -1;
        public int curDepth = 1;
        public int ascension;
        public int mapSeed;            // マップ再生成用シード
        public List<int> clearedNodes = new List<int>(); // 踏破済みノードindex
        public int curNode = -1;       // 現在地ノードindex
    }

    public static void Save(ProtoMain main)
    {
        var unlocked = main.Panel.GetUnlockedCells();
        var d = new SaveData
        {
            version = Version,
            money = main.Money,
            cellStock = main.CellStock,
            equip = (int)main.Equipped,
            ux = new int[unlocked.Count],
            uy = new int[unlocked.Count],
            owned = new List<string>(main.OwnedCardIds),
            wave = main.Wave,
            curHP = main.CurrentHP,
            maxHP = main.Stats != null ? main.Stats.MaxHP : -1,
            curDepth = main.CurrentDepth,
            ascension = main.Ascension,
            mapSeed = main.MapSeed,
        };
        foreach (var id in d.owned) d.ownedCounts.Add(main.OwnedCount(id));
        foreach (var kv in main.GrowthLevels) { d.growIds.Add(kv.Key); d.growLevels.Add(kv.Value); }
        for (int i = 0; i < unlocked.Count; i++) { d.ux[i] = unlocked[i].x; d.uy[i] = unlocked[i].y; }
        foreach (var p in main.Panel.Placements)
        {
            var ps = new PieceSave { cardId = p.card.id, xs = new int[p.cells.Count], ys = new int[p.cells.Count] };
            for (int i = 0; i < p.cells.Count; i++) { ps.xs[i] = p.cells[i].x; ps.ys[i] = p.cells[i].y; }
            d.pieces.Add(ps);
        }
        main.CaptureMap(d.clearedNodes, out d.curNode); // マップ踏破状況

        try { File.WriteAllText(FilePath, JsonUtility.ToJson(d)); }
        catch (System.Exception e) { Debug.LogWarning($"[ProtoSave] 保存に失敗: {e.Message}"); }
    }

    public static bool HasSave() => File.Exists(FilePath) || PlayerPrefs.HasKey(LegacyKey);

    // セーブデータを完全消去（「最初から」用）
    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        if (PlayerPrefs.HasKey(LegacyKey)) { PlayerPrefs.DeleteKey(LegacyKey); PlayerPrefs.Save(); }
    }

    public static bool Load(ProtoMain main)
    {
        SaveData d = null;
        try { if (File.Exists(FilePath)) d = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath)); }
        catch (System.Exception e) { Debug.LogWarning($"[ProtoSave] 読込に失敗: {e.Message}"); }
        if (d == null && PlayerPrefs.HasKey(LegacyKey))
            d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(LegacyKey)); // 旧形式（ラン状態なし）
        if (d == null) return false;

        // お金・ストック・解放マス・所持カードを反映
        var unlocked = new List<Vector2Int>();
        if (d.ux != null && d.uy != null)
            for (int i = 0; i < d.ux.Length && i < d.uy.Length; i++)
                unlocked.Add(new Vector2Int(d.ux[i], d.uy[i]));
        main.ApplyLoaded(d.money, d.cellStock, d.owned, d.ownedCounts, unlocked, d.equip, d.growIds, d.growLevels);

        // 盤面の配置を復元
        if (d.pieces != null)
            foreach (var ps in d.pieces)
            {
                var card = main.Db != null ? main.Db.FindCard(ps.cardId) : null;
                if (card == null) continue;
                var cells = new List<Vector2Int>();
                for (int i = 0; i < ps.xs.Length; i++) cells.Add(new Vector2Int(ps.xs[i], ps.ys[i]));
                main.Panel.PlaceCells(card, cells);
            }

        // ランの途中状態（v5のみ）
        if (d.version >= 5)
            main.ApplyLoadedRun(d.wave, d.curHP, d.maxHP, d.curDepth, d.ascension, d.mapSeed, d.clearedNodes, d.curNode);
        return true;
    }
}
