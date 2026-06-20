using UnityEngine;
using System.Collections.Generic;

// カードの種別
public enum CardKind { Attack, Defense, Heal, Skill }

// カード効果の種類
public enum CardEffectType
{
    Draw,                 // amount枚ドロー
    BlinkOnUse,           // このアタック使用時に点滅ゲームを起動
    PrimeNextAttackBlink, // 次に使うアタックで点滅ゲームを起動
    Protect,              // 次の被ダメージを amount% 軽減
    ManaBoostNextTurn,    // 次の自ターンのマナを amount 増やす
    Block,               // amount のブロック（被ダメージを肩代わり）
    Weak,                 // 敵の攻撃力を amount% 低下（duration ターン）
    Poison,               // 敵に amount の毒（毎ターンダメージ）
    Strength,             // 自分の攻撃力を amount 上昇（戦闘中持続）
    Heal,                 // amount 回復
    HealPercent,          // 最大HPの amount% を回復
}

[System.Serializable]
public class CardEffect
{
    public CardEffectType type;
    public int amount;
    public int duration; // 必要な効果のみ使用（Weak等）
}

// カード（=スキルピース）の定義。ScriptableObjectとしてUnity上で編集・追加できる。
[CreateAssetMenu(fileName = "Card", menuName = "MamaGame/Card")]
public class CardDef : ScriptableObject
{
    public string id = "card";
    public string displayName = "カード";
    [TextArea] public string description = "";
    public CardKind kind = CardKind.Attack;

    [Tooltip("'X'=マスあり。例: 行ごとに \"XX.\", \".X.\" のように記述")]
    public string[] shapeRows = { "X" };

    public int power = 0;            // アタックの基礎威力
    [Tooltip("このカードが報酬/ショップに出現する最低深度（マップの列番号）。深いほど強いカードに設定する")]
    public int minDepth = 0;        // 獲得可能な最低深度
    [Tooltip("このカードが報酬/ショップに出現する最高深度。これより深いマスでは出現しない（弱カードの終盤排除用）。0以下=上限なし")]
    public int maxDepth = 0;        // 獲得可能な最高深度（0以下=無制限）
    public Color color = Color.white;
    public Sprite icon;             // 後で差し替え可能な画像
    public CardEffect[] effects;

    [Tooltip("-1で自動（=Ceil(マス数/10)）。0以上で固定値")]
    public int manaCostOverride = -1;

    Vector2Int[] _shape; // パース結果のキャッシュ

    void OnEnable() => _shape = null; // 再生開始/アセット読込時にキャッシュをクリア

    public Vector2Int[] Shape
    {
        get { if (_shape == null) _shape = ParseShape(shapeRows); return _shape; }
    }

    public int Size => Shape.Length;

    public int ManaCost => manaCostOverride >= 0 ? manaCostOverride : Mathf.CeilToInt(Size / 10f);

    // 効果から自動分類した種別。
    // 攻撃=ダメージを与える / 回復=HPを回復する / 防御=被ダメージを軽減する / スキル=それ以外
    public CardKind Category
    {
        get
        {
            if (power > 0) return CardKind.Attack;
            if (HasEffect(CardEffectType.Heal) || HasEffect(CardEffectType.HealPercent)) return CardKind.Heal;
            if (HasEffect(CardEffectType.Protect) || HasEffect(CardEffectType.Block)) return CardKind.Defense;
            return CardKind.Skill;
        }
    }

    public static string KindLabel(CardKind k)
    {
        switch (k)
        {
            case CardKind.Attack: return "攻撃";
            case CardKind.Defense: return "防御";
            case CardKind.Heal: return "回復";
            default: return "スキル";
        }
    }

    public bool HasEffect(CardEffectType t)
    {
        if (effects != null) foreach (var e in effects) if (e.type == t) return true;
        return false;
    }

    public int EffectAmount(CardEffectType t)
    {
        if (effects != null) foreach (var e in effects) if (e.type == t) return e.amount;
        return 0;
    }

    public int EffectDuration(CardEffectType t)
    {
        if (effects != null) foreach (var e in effects) if (e.type == t) return e.duration;
        return 0;
    }

    // 文字マップ（"XX.", ".X." 等）をマスのオフセット集合に変換
    public static Vector2Int[] ParseShape(string[] rows)
    {
        var list = new List<Vector2Int>();
        if (rows != null)
            for (int y = 0; y < rows.Length; y++)
            {
                string row = rows[y];
                if (row == null) continue;
                for (int x = 0; x < row.Length; x++)
                    if (row[x] == 'X' || row[x] == 'x' || row[x] == '#')
                        list.Add(new Vector2Int(x, y));
            }
        if (list.Count == 0) list.Add(new Vector2Int(0, 0)); // 安全策
        return list.ToArray();
    }
}
