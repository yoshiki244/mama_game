// ゲームバランス定数の一元管理。
// 特殊マス・四隅の加護など、調整頻度の高い数値はここに集める（コード内に直書きしない）。
// ※将来のフェーズCでCSV/ScriptableObject読み込みに置き換える際も、参照はこのクラス経由のまま維持する。
public static class GameBalance
{
    // ---- 特殊マス ----
    public const float SpecialRollChance = 0.18f; // マス解放時に特殊マスになる確率
    public const float PowerPctPerCell = 0.5f;    // 強化マス：覆った攻撃カードの威力 +50%/マス
    public const int GoldCoinPerCell = 5;         // 黄金マス：覆ったカードのドロー時コイン +5/マス
    public const int ResonanceMult = 3;           // 共鳴マス：絡む隣接シナジーを3倍で数える
    public const float CurseWeightMult = 3f;      // 呪いマス：覆ったカードの出現率 3倍
    public const int CurseHpPerCell = 3;          // 呪いマス：使用時HP -3/マス
    public const int CursePurifyPrice = 80;       // ショップの呪い浄化サービスの価格

    // 特殊マス種別の抽選重み（合計100）
    public const int RollPower = 33;
    public const int RollGold = 27;
    public const int RollResonance = 25;
    public const int RollCurse = 15;

    // ---- 四隅の加護 ----
    public const float CornerAtkPct = 0.05f;      // Lv1：攻撃威力 +5%
    public const int CornerBlock = 3;             // Lv2：毎ターン ブロック +3
    public const int CornerRegen = 2;             // Lv3：毎ターン HP +2回復
    public const int CornerMana = 1;              // Lv4：最大マナ +1

    // ---- 形状シナジー ----
    public const int ColCompleteBlock = 4;        // 列コンプリート：毎ターン ブロック +4/列
    public const float CorePowerMult = 1.5f;      // コア（完全包囲）：そのカードの威力 1.5倍

    // ---- オーバードライブ（同ターン連続攻撃で威力が乗算的に上昇） ----
    public const float OverdrivePerStack = 0.25f; // 攻撃1発ごとに +25%（同ターン内で累積）
    public const int OverdriveMaxStack = 6;       // 最大6スタック（+150%）

    // 表示用（%表記）
    public static int PowerPctInt => (int)(PowerPctPerCell * 100f);
    public static int CornerAtkPctInt => (int)(CornerAtkPct * 100f);
    public static int CorePowerPctInt => (int)((CorePowerMult - 1f) * 100f);
    public static int OverdrivePctInt => (int)(OverdrivePerStack * 100f);
}
