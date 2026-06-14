using UnityEngine;

// ゲーム全体の調整値。ScriptableObjectとしてUnity上で編集できる。
[CreateAssetMenu(fileName = "GameConfig", menuName = "MamaGame/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("プレイヤー")]
    public int playerMaxHP = 80;
    public int playerAttack = 6;

    [Header("マナ")]
    [Tooltip("初期(5×5)時の最大マナ。実際の最大マナ = baseMana + 盤面拡張回数")]
    public int baseMana = 3;

    [Header("手札")]
    public int handSize = 5;

    [Header("通常攻撃（空白マス）")]
    public int normalAttackPower = 10;
    public int normalAttackMana = 1;

    [Header("盤面拡張（最大5回：5×5→10×10）")]
    public int[] expansionCosts = { 50, 80, 120, 170, 230 };

    [Header("精神樹ショップ")]
    public int shopOfferCount = 5;    // 購入候補の提示数
    public int shopBuyPrice = 40;     // ピース1個の購入価格

    [Header("報酬")]
    public int eventMoney = 25;       // イベントマスの獲得額
    public int rewardChoiceCount = 3; // 戦闘報酬の選択肢数

    [Header("点滅順番当てゲーム")]
    [Tooltip("点滅の速度倍率（2=2倍速＝点灯時間・間隔が半分でテンポ良く）")]
    public float blinkTimeScale = 2f;

    [Header("初期所持カードid")]
    public string[] initialOwned = { "slash", "fireball", "draw", "blink", "defend" };
}
