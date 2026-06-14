// プレイヤー（MAMA）の基礎ステータス。本設では単一キャラ・レベル成長なし。
// 成長は「盤面拡張」と「ピース獲得」に一本化（HP/攻撃は固定＋戦闘中バフのみ）。
public class PlayerStats
{
    public int MaxHP = 80;
    public int Attack = 6;

    public PlayerStats() { }

    public PlayerStats(GameConfig config)
    {
        if (config != null)
        {
            MaxHP = config.playerMaxHP;
            Attack = config.playerAttack;
        }
    }
}
