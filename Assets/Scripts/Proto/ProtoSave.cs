using UnityEngine;
using System.Collections.Generic;

// セーブ/ロード（PlayerPrefsにJSONで保存。プリプロ用の簡易実装）
// 保存内容: ステータス・Wave・盤面のピース配置
public static class ProtoSave
{
    const string Key = "mama_save_v1";

    [System.Serializable]
    public class PieceSave
    {
        public string skillId;
        public int[] xs;
        public int[] ys;
    }

    [System.Serializable]
    public class SaveData
    {
        public int level, exp, maxHP, attack, defense, speed, wave;
        public List<PieceSave> pieces = new List<PieceSave>();
    }

    public static void Save(ProtoMain main)
    {
        var d = new SaveData
        {
            level = main.Stats.Level,
            exp = main.Stats.Exp,
            maxHP = main.Stats.MaxHP,
            attack = main.Stats.Attack,
            defense = main.Stats.Defense,
            speed = main.Stats.Speed,
            wave = main.Wave,
        };
        foreach (var p in main.Panel.Placements)
        {
            var ps = new PieceSave
            {
                skillId = p.skill.id,
                xs = new int[p.cells.Count],
                ys = new int[p.cells.Count],
            };
            for (int i = 0; i < p.cells.Count; i++)
            {
                ps.xs[i] = p.cells[i].x;
                ps.ys[i] = p.cells[i].y;
            }
            d.pieces.Add(ps);
        }

        PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
        PlayerPrefs.Save();
    }

    public static bool HasSave() => PlayerPrefs.HasKey(Key);

    public static bool Load(ProtoMain main)
    {
        if (!PlayerPrefs.HasKey(Key)) return false;
        var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key));
        if (d == null) return false;

        main.Stats.Level = d.level;
        main.Stats.Exp = d.exp;
        main.Stats.MaxHP = d.maxHP;
        main.Stats.Attack = d.attack;
        main.Stats.Defense = d.defense;
        main.Stats.Speed = d.speed;
        main.SetWave(d.wave);

        foreach (var ps in d.pieces)
        {
            var skill = ProtoSkills.All.Find(s => s.id == ps.skillId);
            if (skill == null) continue;
            var cells = new List<Vector2Int>();
            for (int i = 0; i < ps.xs.Length; i++)
                cells.Add(new Vector2Int(ps.xs[i], ps.ys[i]));
            main.Panel.PlaceCells(skill, cells);
        }
        return true;
    }
}
