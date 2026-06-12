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
        public int member; // どのメンバーの盤面か
    }

    [System.Serializable]
    public class MemberSave
    {
        public int level, exp, maxHP, attack, defense, speed;
    }

    [System.Serializable]
    public class SaveData
    {
        public int level, exp, maxHP, attack, defense, speed, wave; // 旧形式互換（リーダーの値）
        public List<MemberSave> members = new List<MemberSave>();   // メンバー別ステータス
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

        // メンバー別ステータス
        foreach (var s in main.MemberStats)
        {
            d.members.Add(new MemberSave
            {
                level = s.Level, exp = s.Exp, maxHP = s.MaxHP,
                attack = s.Attack, defense = s.Defense, speed = s.Speed,
            });
        }
        // 全メンバーの盤面を保存
        for (int m = 0; m < main.Panels.Count; m++)
        {
            foreach (var p in main.Panels[m].Placements)
            {
                var ps = new PieceSave
                {
                    skillId = p.skill.id,
                    member = m,
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

        if (d.members != null && d.members.Count > 0)
        {
            // 新形式: メンバーごとに復元
            for (int i = 0; i < d.members.Count && i < main.MemberStats.Count; i++)
            {
                var s = main.MemberStats[i];
                var ms = d.members[i];
                s.Level = ms.level; s.Exp = ms.exp; s.MaxHP = ms.maxHP;
                s.Attack = ms.attack; s.Defense = ms.defense; s.Speed = ms.speed;
            }
        }
        else
        {
            // 旧形式: リーダーだけ復元
            main.Stats.Level = d.level;
            main.Stats.Exp = d.exp;
            main.Stats.MaxHP = d.maxHP;
            main.Stats.Attack = d.attack;
            main.Stats.Defense = d.defense;
            main.Stats.Speed = d.speed;
        }
        main.SetWave(d.wave);

        foreach (var ps in d.pieces)
        {
            var skill = ProtoSkills.All.Find(s => s.id == ps.skillId);
            if (skill == null) continue;
            if (ps.member < 0 || ps.member >= main.Panels.Count) continue; // 外したメンバーの盤面は無視
            var cells = new List<Vector2Int>();
            for (int i = 0; i < ps.xs.Length; i++)
                cells.Add(new Vector2Int(ps.xs[i], ps.ys[i]));
            main.Panels[ps.member].PlaceCells(skill, cells);
        }
        return true;
    }
}
