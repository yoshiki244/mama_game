using UnityEngine;

// ─────────────────────────────────────────────
//  スキルの種類
// ─────────────────────────────────────────────
public enum SkillType
{
    NormalAttack,   // 通常攻撃
    WeakAttack,     // 弱攻撃
    HeavyAttack,    // 大技
    Heal,           // 回復
    Buff,           // バフ
    Debuff,         // デバフ
}

// ─────────────────────────────────────────────
//  1スキルのデータ（ScriptableObject）
//  Assets/Data/Skills/ に .asset を作成する
// ─────────────────────────────────────────────
[CreateAssetMenu(fileName = "NewSkill", menuName = "MamaRPG/SkillData")]
public class SkillData : ScriptableObject
{
    [Header("基本情報")]
    public string skillName = "スキル名";
    [TextArea] public string description = "説明";
    public Sprite icon;                         // カードに表示するアイコン
    public SkillType skillType = SkillType.NormalAttack;

    [Header("パネル上のピース形状（5〜25マス）")]
    // 5×5の相対座標リスト。(0,0) がピースの基準点
    public Vector2Int[] pieceShape;

    [Header("戦闘パラメータ")]
    public int power = 10;                      // 攻撃 or 回復量の基礎値
    public float powerMultiplier = 1.0f;        // 乗算倍率
    public int mpCost = 0;                      // MP消費

    [Header("バフ/デバフ")]
    public StatModifier[] modifiers;            // 付与するステータス変化
}

// ─────────────────────────────────────────────
//  ステータス変化の定義（SkillData内で使用）
// ─────────────────────────────────────────────
[System.Serializable]
public class StatModifier
{
    public enum StatTarget { ATK, DEF, SPD, HEAL_RATE }
    public StatTarget target;
    public float value;         // 加算値（負数でデバフ）
    public int duration;        // 持続ターン数
}
