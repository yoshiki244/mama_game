using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  キャラクターのランタイムステータス
//  戦闘中の一時バフも管理する
// ─────────────────────────────────────────────
[System.Serializable]
public class CharacterStats
{
    public string characterName = "キャラクター";

    [Header("基本値")]
    public int maxHP = 100;
    public int maxMP = 50;
    public int baseATK = 15;
    public int baseDEF = 5;
    public int baseSPD = 10;

    // ランタイム値
    [HideInInspector] public int currentHP;
    [HideInInspector] public int currentMP;

    // 現在の実効値（バフ込み）
    public int ATK => Mathf.Max(1, baseATK + GetModifier(StatModifier.StatTarget.ATK));
    public int DEF => Mathf.Max(0, baseDEF + GetModifier(StatModifier.StatTarget.DEF));
    public int SPD => Mathf.Max(1, baseSPD + GetModifier(StatModifier.StatTarget.SPD));

    // ─── バフ/デバフ管理 ─────────────────────
    private readonly List<ActiveModifier> _activeModifiers = new List<ActiveModifier>();

    public void Initialize()
    {
        currentHP = maxHP;
        currentMP = maxMP;
        _activeModifiers.Clear();
    }

    public void ApplyModifier(StatModifier mod)
    {
        _activeModifiers.Add(new ActiveModifier { mod = mod, remainingTurns = mod.duration });
    }

    /// <summary>ターン終了時にバフ残ターンを1減らす</summary>
    public void TickModifiers()
    {
        for (int i = _activeModifiers.Count - 1; i >= 0; i--)
        {
            _activeModifiers[i].remainingTurns--;
            if (_activeModifiers[i].remainingTurns <= 0)
                _activeModifiers.RemoveAt(i);
        }
    }

    private int GetModifier(StatModifier.StatTarget target)
    {
        int total = 0;
        foreach (var am in _activeModifiers)
            if (am.mod.target == target)
                total += Mathf.RoundToInt(am.mod.value);
        return total;
    }

    public bool IsAlive => currentHP > 0;

    // ─── ダメージ & 回復 ─────────────────────
    public void TakeDamage(int raw)
    {
        int dmg = Mathf.Max(1, raw - DEF);
        currentHP = Mathf.Max(0, currentHP - dmg);
    }

    public void Heal(int amount)
    {
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }

    public void UseMp(int amount)
    {
        currentMP = Mathf.Max(0, currentMP - amount);
    }

    // ─── 内部クラス ──────────────────────────
    private class ActiveModifier
    {
        public StatModifier mod;
        public int remainingTurns;
    }
}
