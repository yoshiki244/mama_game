using UnityEngine;
using System.Collections.Generic;

// カードの種別
// 注意: シリアライズ値を保つため Attack=0, Skill=1 は固定。新種別は末尾に追加すること。
public enum CardKind { Attack, Skill, Defense, Heal }

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
    Burn,                 // 敵をやけど状態にする（毎ターン amount ダメージ）
    // ---- 拡張効果（※シリアライズ値保持のため末尾に追加すること） ----
    MultiHit,             // amount回の多段ヒット（powerが1ヒットあたりの威力）
    LifeSteal,            // 与ダメージの amount% をHP回復
    Execute,              // 敵HPが amount% 以下なら威力2倍
    Detonate,             // 敵の毒・やけどを消費し、残量×amount の追加ダメージ
    SelfDamage,           // 自分に amount ダメージ（反動）
    GrowingPower,         // この戦闘で使うたび威力+amount（累積）
    BoardPower,           // 解放マス数×amount を威力に加算
    LowHpPower,           // 失ったHP割合に応じ威力増（最大+amount%）
    ManaBurst,            // 残りマナを全消費し マナ×amount を威力に加算
    HandPower,            // 使用後の手札枚数×amount を威力に加算
    Gamble5050,           // 50%で威力2倍、50%で不発（威力0）
    StunChance,           // amount% で敵を1ターン行動不能
    Stun,                 // 敵を duration(最低1)ターン行動不能
    CurrentHpDmg,         // 敵の現在HP×amount% を追加ダメージ
    Regen,                // durationターンの間、毎ターンHPをamount回復
    Thorns,               // durationターンの間、被弾時に敵へamount反撃
    BlockRegen,           // durationターンの間、毎ターンブロックをamount獲得
    Reflect,              // 次の被ダメージをamount%軽減し、軽減分を敵に返す
    GuardTurns,           // durationターンの間、被ダメージをamount%軽減
    Counter,              // 次に被弾したとき敵へamountダメージの反撃
    GainMoney,            // お金を amount 入手
    Vulnerable,           // durationターンの間、敵の受けるダメージ+amount%
    AilmentAmp,           // この戦闘中、毒・やけどダメージ+amount
    ManaNow,              // このターンのマナ+amount
    RandomDiscardDraw,    // 手札をランダムに1枚捨てて amount 枚引く
    RedrawAll,            // 手札をすべて捨てて同じ枚数引き直す
    NextTurnExtraCards,   // 次のターンの手札+amount枚
    PoisonBoost,          // 毒amountを付与。すでに毒なら合計を2倍
    BurnBoost,            // やけどamountを付与。すでにやけどなら合計を2倍
    HealOverflowBlock,    // amount回復し、最大HPを超えた分はブロックに変換
    HealMissing,          // 失っているHPの amount% を回復
    TimeBomb,             // durationターン後に敵へ amount の大ダメージ
    GaugeOnUse,           // 使用時にゲージストップ発動（威力倍率）
    TapOrderOnUse,        // 使用時に数字順タップ発動（威力倍率）
    SlotOnUse,            // 使用時にスロット発動（威力倍率）
    MashOnUse,            // 使用時に連打チャレンジ発動（威力倍率）
    RouletteOnUse,        // 使用時にルーレット発動（威力倍率）
    TraceOnUse,           // 使用時に軌道なぞり発動（威力倍率）
    ParryStance,          // 次の敵の攻撃にパリィチャンス（成功で被ダメ大幅軽減）
    ChargeOnUse,          // 使用時に長押しチャージ発動（溜め量で威力倍率・溜めすぎ暴発）
    DualGaugeOnUse,       // 使用時に2本ゲージ発動（両方止めて威力倍率）
    CountdownOnUse,       // 使用時にカウントダウン読み発動（ジャストで威力倍率）
    AdjacencyPower,       // 盤面でこのカードのピースに隣接するピース数×amount を威力に加算
    FillEmptyOnUse,       // この戦闘中、抽選の空きマスを amount 減らす（通常攻撃率が下がる）
    GambleDiscard,        // 手札からランダムに1枚捨て、そのマス数×amount を威力に加算
}

[System.Serializable]
public class CardEffect
{
    public CardEffectType type;
    public int amount;
    public int duration; // 必要な効果のみ使用（Weak等）
}

// カードの系統（ビルドテーマ）。効果から自動判定するためアセット側の変更は不要。
// UIに【タグ】として色付き表示し、「いま自分は何ビルドか」を見える化する。
public enum CardTheme
{
    None,      // 無系統（素のアタックなど）
    Critical,  // 会心：ミニゲームで威力を伸ばす
    Formation, // 布陣：盤面・手札・マナの状態を参照する（このゲームの独自軸）
    Ailment,   // 侵蝕：毒・やけどでじわじわ削る
    Heal,      // 回復：回復・吸血で粘り勝つ
    Guard,     // 鉄壁：ブロック・軽減・反撃・パリィで受け切る
    Tactics,   // 計略：ドロー・マナ・妨害で流れを支配する
    Might,     // 剛力：多段・自己強化・捨て身の一撃
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

    [Tooltip("アンロック段階。0=最初から / 1=1回クリア後 / 2=2回クリア後 …（報酬・ショップ・精霊樹の抽選に影響）")]
    public int unlockTier = 0;

    [Tooltip("レアリティ。0=コモン / 1=アンコモン / 2=レア（出現率と枠色に影響）")]
    public int rarity = 0;

    public Color RarityColor => rarity >= 2 ? new Color(1f, 0.79f, 0.30f)   // レア=金
        : rarity == 1 ? new Color(0.50f, 0.83f, 1f)                         // アンコモン=水色
        : new Color(0.92f, 0.92f, 0.95f);                                   // コモン=白
    public string RarityLabel => rarity >= 2 ? "レア" : rarity == 1 ? "アンコモン" : "コモン";

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
            if (HasEffect(CardEffectType.Heal) || HasEffect(CardEffectType.HealPercent)
                || HasEffect(CardEffectType.Regen) || HasEffect(CardEffectType.HealMissing)
                || HasEffect(CardEffectType.HealOverflowBlock)) return CardKind.Heal;
            if (HasEffect(CardEffectType.Protect) || HasEffect(CardEffectType.Block)
                || HasEffect(CardEffectType.Thorns) || HasEffect(CardEffectType.BlockRegen)
                || HasEffect(CardEffectType.Reflect) || HasEffect(CardEffectType.GuardTurns)
                || HasEffect(CardEffectType.Counter)) return CardKind.Defense;
            return CardKind.Skill;
        }
    }

    // ==================== 系統（テーマ）判定 ====================

    // 効果から系統を自動判定。複数系統にまたがる場合は優先順（会心>布陣>侵蝕>回復>鉄壁>計略>剛力）で主系統を1つ返す
    public CardTheme Theme
    {
        get
        {
            if (HasAny(CardEffectType.BlinkOnUse, CardEffectType.PrimeNextAttackBlink,
                CardEffectType.GaugeOnUse, CardEffectType.TapOrderOnUse, CardEffectType.SlotOnUse,
                CardEffectType.MashOnUse, CardEffectType.RouletteOnUse, CardEffectType.TraceOnUse,
                CardEffectType.ChargeOnUse, CardEffectType.DualGaugeOnUse, CardEffectType.CountdownOnUse)) return CardTheme.Critical;
            if (HasAny(CardEffectType.AdjacencyPower, CardEffectType.BoardPower, CardEffectType.FillEmptyOnUse,
                CardEffectType.HandPower, CardEffectType.ManaBurst, CardEffectType.GambleDiscard)) return CardTheme.Formation;
            if (HasAny(CardEffectType.Poison, CardEffectType.Burn, CardEffectType.PoisonBoost,
                CardEffectType.BurnBoost, CardEffectType.Detonate, CardEffectType.AilmentAmp)) return CardTheme.Ailment;
            if (HasAny(CardEffectType.Heal, CardEffectType.HealPercent, CardEffectType.Regen,
                CardEffectType.HealMissing, CardEffectType.HealOverflowBlock, CardEffectType.LifeSteal)) return CardTheme.Heal;
            if (HasAny(CardEffectType.Protect, CardEffectType.Block, CardEffectType.Thorns,
                CardEffectType.BlockRegen, CardEffectType.Reflect, CardEffectType.GuardTurns,
                CardEffectType.Counter, CardEffectType.ParryStance)) return CardTheme.Guard;
            if (HasAny(CardEffectType.Draw, CardEffectType.ManaBoostNextTurn, CardEffectType.ManaNow,
                CardEffectType.RandomDiscardDraw, CardEffectType.RedrawAll, CardEffectType.NextTurnExtraCards,
                CardEffectType.Weak, CardEffectType.Vulnerable, CardEffectType.Stun, CardEffectType.StunChance,
                CardEffectType.GainMoney, CardEffectType.TimeBomb)) return CardTheme.Tactics;
            if (HasAny(CardEffectType.MultiHit, CardEffectType.Strength, CardEffectType.GrowingPower,
                CardEffectType.Execute, CardEffectType.LowHpPower, CardEffectType.Gamble5050,
                CardEffectType.SelfDamage, CardEffectType.CurrentHpDmg)) return CardTheme.Might;
            return CardTheme.None;
        }
    }

    bool HasAny(params CardEffectType[] types)
    {
        if (effects == null) return false;
        foreach (var e in effects)
            foreach (var t in types)
                if (e.type == t) return true;
        return false;
    }

    public static string ThemeLabel(CardTheme t)
    {
        switch (t)
        {
            case CardTheme.Critical: return "会心";
            case CardTheme.Formation: return "布陣";
            case CardTheme.Ailment: return "侵蝕";
            case CardTheme.Heal: return "回復";
            case CardTheme.Guard: return "鉄壁";
            case CardTheme.Tactics: return "計略";
            case CardTheme.Might: return "剛力";
            default: return "無系統";
        }
    }

    public static Color ThemeColor(CardTheme t)
    {
        switch (t)
        {
            case CardTheme.Critical: return new Color(1f, 0.80f, 0.30f);   // 金
            case CardTheme.Formation: return new Color(0.40f, 0.88f, 0.85f); // 青緑
            case CardTheme.Ailment: return new Color(0.62f, 0.85f, 0.32f); // 毒緑
            case CardTheme.Heal: return new Color(0.40f, 0.95f, 0.62f);    // 若草
            case CardTheme.Guard: return new Color(0.48f, 0.72f, 1f);      // 青
            case CardTheme.Tactics: return new Color(0.72f, 0.66f, 1f);    // 藤
            case CardTheme.Might: return new Color(1f, 0.52f, 0.38f);      // 朱
            default: return new Color(0.7f, 0.7f, 0.75f);
        }
    }

    // 系統選択UI（将来の看板・初期セット）で列挙する一覧（Noneを除く）
    public static readonly CardTheme[] AllThemes =
    {
        CardTheme.Critical, CardTheme.Formation, CardTheme.Ailment, CardTheme.Heal,
        CardTheme.Guard, CardTheme.Tactics, CardTheme.Might,
    };

    // リッチテキストのタグ（例: <color=#..>【侵蝕】</color>）。無系統は空文字
    public string ThemeTagRich()
    {
        var t = Theme;
        if (t == CardTheme.None) return "";
        return $"<color=#{ColorUtility.ToHtmlStringRGB(ThemeColor(t))}>【{ThemeLabel(t)}】</color>";
    }

    // 効果を日本語で要約（ホバー詳細などで使用）
    public string EffectSummary()
    {
        var lines = new List<string>();
        if (power > 0) lines.Add($"威力 {power} のダメージ");
        if (effects != null)
            foreach (var e in effects)
            {
                switch (e.type)
                {
                    case CardEffectType.Draw: lines.Add($"カードを {e.amount} 枚ドロー"); break;
                    case CardEffectType.BlinkOnUse: lines.Add("使用時に点滅ゲームが発動"); break;
                    case CardEffectType.PrimeNextAttackBlink: lines.Add("次のアタックで点滅ゲームが発動"); break;
                    case CardEffectType.Protect: lines.Add($"次の被ダメージを {e.amount}% 軽減"); break;
                    case CardEffectType.ManaBoostNextTurn: lines.Add($"次ターンのマナを {e.amount} 増加"); break;
                    case CardEffectType.Block: lines.Add($"{e.amount} のブロックを獲得"); break;
                    case CardEffectType.Weak: lines.Add($"敵の攻撃力を {e.amount}% 低下（{e.duration}ターン）"); break;
                    case CardEffectType.Poison: lines.Add($"敵に {e.amount} の毒を付与"); break;
                    case CardEffectType.Burn: lines.Add($"敵をやけど状態にする（毎ターン {e.amount} ダメージ）"); break;
                    case CardEffectType.Strength: lines.Add($"自分の攻撃力を {e.amount} 上昇（戦闘中）"); break;
                    case CardEffectType.Heal: lines.Add($"HPを {e.amount} 回復"); break;
                    case CardEffectType.HealPercent: lines.Add($"最大HPの {e.amount}% を回復"); break;
                    case CardEffectType.MultiHit: lines.Add($"{e.amount} 回連続でヒット"); break;
                    case CardEffectType.LifeSteal: lines.Add($"与ダメージの {e.amount}% をHP回復"); break;
                    case CardEffectType.Execute: lines.Add($"敵HPが {e.amount}% 以下なら威力2倍"); break;
                    case CardEffectType.Detonate: lines.Add($"毒・やけどを消費し 残量×{e.amount} の追加ダメージ"); break;
                    case CardEffectType.SelfDamage: lines.Add($"反動で自分に {e.amount} ダメージ"); break;
                    case CardEffectType.GrowingPower: lines.Add($"この戦闘で使うたび威力+{e.amount}"); break;
                    case CardEffectType.BoardPower: lines.Add($"解放マス数×{e.amount} を威力に加算"); break;
                    case CardEffectType.LowHpPower: lines.Add($"HPが低いほど威力増（最大+{e.amount}%）"); break;
                    case CardEffectType.ManaBurst: lines.Add($"残マナを全消費し マナ×{e.amount} を威力に加算"); break;
                    case CardEffectType.HandPower: lines.Add($"手札の残り枚数×{e.amount} を威力に加算"); break;
                    case CardEffectType.Gamble5050: lines.Add("50%で威力2倍、50%で不発"); break;
                    case CardEffectType.StunChance: lines.Add($"{e.amount}% で敵を1ターン行動不能"); break;
                    case CardEffectType.Stun: lines.Add($"敵を {Mathf.Max(1, e.duration)} ターン行動不能"); break;
                    case CardEffectType.CurrentHpDmg: lines.Add($"敵の現在HPの {e.amount}% を追加ダメージ"); break;
                    case CardEffectType.Regen: lines.Add($"{e.duration}ターンの間、毎ターンHP{e.amount}回復"); break;
                    case CardEffectType.Thorns: lines.Add($"{e.duration}ターンの間、被弾時に敵へ{e.amount}反撃"); break;
                    case CardEffectType.BlockRegen: lines.Add($"{e.duration}ターンの間、毎ターンブロック{e.amount}獲得"); break;
                    case CardEffectType.Reflect: lines.Add($"次の被ダメージを{e.amount}%軽減し、軽減分を敵に返す"); break;
                    case CardEffectType.GuardTurns: lines.Add($"{e.duration}ターンの間、被ダメージを{e.amount}%軽減"); break;
                    case CardEffectType.Counter: lines.Add($"次に被弾したとき敵へ{e.amount}の反撃"); break;
                    case CardEffectType.GainMoney: lines.Add($"お金を {e.amount} 入手"); break;
                    case CardEffectType.Vulnerable: lines.Add($"{e.duration}ターンの間、敵の受けるダメージ+{e.amount}%"); break;
                    case CardEffectType.AilmentAmp: lines.Add($"この戦闘中、毒・やけどダメージ+{e.amount}"); break;
                    case CardEffectType.ManaNow: lines.Add($"このターンのマナ+{e.amount}"); break;
                    case CardEffectType.RandomDiscardDraw: lines.Add($"手札を1枚捨てて {e.amount} 枚引く"); break;
                    case CardEffectType.RedrawAll: lines.Add("手札をすべて引き直す"); break;
                    case CardEffectType.NextTurnExtraCards: lines.Add($"次のターンの手札+{e.amount}枚"); break;
                    case CardEffectType.PoisonBoost: lines.Add($"毒{e.amount}を付与。すでに毒なら合計2倍"); break;
                    case CardEffectType.BurnBoost: lines.Add($"やけど{e.amount}を付与。すでにやけどなら合計2倍"); break;
                    case CardEffectType.HealOverflowBlock: lines.Add($"HP{e.amount}回復。あふれた分はブロックに"); break;
                    case CardEffectType.HealMissing: lines.Add($"失ったHPの {e.amount}% を回復"); break;
                    case CardEffectType.TimeBomb: lines.Add($"{e.duration}ターン後に敵へ {e.amount} の大ダメージ"); break;
                    case CardEffectType.GaugeOnUse: lines.Add("使用時にゲージストップ（会心）が発動"); break;
                    case CardEffectType.TapOrderOnUse: lines.Add("使用時に数字順タップが発動"); break;
                    case CardEffectType.SlotOnUse: lines.Add("使用時にスロットが発動"); break;
                    case CardEffectType.MashOnUse: lines.Add("使用時に連打チャレンジが発動"); break;
                    case CardEffectType.RouletteOnUse: lines.Add("使用時にルーレットが発動"); break;
                    case CardEffectType.TraceOnUse: lines.Add("使用時に軌道なぞりが発動"); break;
                    case CardEffectType.ParryStance: lines.Add("次の敵の攻撃をパリィできる構えを取る"); break;
                    case CardEffectType.ChargeOnUse: lines.Add("使用時にチャージ（長押し）が発動"); break;
                    case CardEffectType.DualGaugeOnUse: lines.Add("使用時に2本ゲージ停止が発動"); break;
                    case CardEffectType.CountdownOnUse: lines.Add("使用時にカウントダウン読みが発動"); break;
                    case CardEffectType.AdjacencyPower: lines.Add($"盤面で隣接するピース1つにつき威力+{e.amount}"); break;
                    case CardEffectType.FillEmptyOnUse: lines.Add("使うとこの戦闘中、通常攻撃が出にくくなる"); break;
                    case CardEffectType.GambleDiscard: lines.Add($"手札を1枚捨て、そのマス数×{e.amount}を威力に加算"); break;
                }
            }
        if (lines.Count == 0 && !string.IsNullOrEmpty(description)) lines.Add(description);
        return string.Join("\n", lines);
    }

    // 種別ごとの固定色（攻撃=赤 / 防御=青 / 回復=緑 / スキル=黄）
    public Color CategoryColor
    {
        get
        {
            if (id == "normal") return color; // 通常攻撃は色なし（元のニュートラル色）
            switch (Category)
            {
                case CardKind.Attack: return new Color(0.88f, 0.27f, 0.27f);
                case CardKind.Defense: return new Color(0.30f, 0.55f, 0.95f);
                case CardKind.Heal: return new Color(0.32f, 0.80f, 0.42f);
                default: return new Color(0.96f, 0.58f, 0.18f); // スキル＝オレンジ
            }
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
