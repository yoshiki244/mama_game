using UnityEngine;
using System.Collections.Generic;

// セーブ/ロード（PlayerPrefsにJSON）。本設：単一キャラ・お金・盤面拡張・所持カード・盤面配置。
public static class ProtoSave
{
    const string Key = "mama_save_v3";

    [System.Serializable]
    public class PieceSave { public string cardId; public int[] xs; public int[] ys; }

    [System.Serializable]
    public class SaveData
    {
        public int money;
        public int cellStock;          // マスストック
        public int[] ux;               // 解放マスX
        public int[] uy;               // 解放マスY
        public List<string> owned = new List<string>();
        public List<PieceSave> pieces = new List<PieceSave>();
    }

    public static void Save(ProtoMain main)
    {
        var unlocked = main.Panel.GetUnlockedCells();
        var d = new SaveData
        {
            money = main.Money,
            cellStock = main.CellStock,
            ux = new int[unlocked.Count],
            uy = new int[unlocked.Count],
            owned = new List<string>(main.OwnedCardIds),
        };
        for (int i = 0; i < unlocked.Count; i++) { d.ux[i] = unlocked[i].x; d.uy[i] = unlocked[i].y; }
        foreach (var p in main.Panel.Placements)
        {
            var ps = new PieceSave { cardId = p.card.id, xs = new int[p.cells.Count], ys = new int[p.cells.Count] };
            for (int i = 0; i < p.cells.Count; i++) { ps.xs[i] = p.cells[i].x; ps.ys[i] = p.cells[i].y; }
            d.pieces.Add(ps);
        }
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
        PlayerPrefs.Save();
    }

    public static bool HasSave() => PlayerPrefs.HasKey(Key);

    // セーブデータを完全消去（「最初から」用）
    public static void Clear()
    {
        if (PlayerPrefs.HasKey(Key)) PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    public static bool Load(ProtoMain main)
    {
        if (!PlayerPrefs.HasKey(Key)) return false;
        var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key));
        if (d == null) return false;

        // お金・マスストック・解放マス・所持カードを反映
        var unlocked = new List<Vector2Int>();
        if (d.ux != null && d.uy != null)
            for (int i = 0; i < d.ux.Length && i < d.uy.Length; i++)
                unlocked.Add(new Vector2Int(d.ux[i], d.uy[i]));
        main.ApplyLoaded(d.money, d.cellStock, d.owned, unlocked);

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
        return true;
    }
}
