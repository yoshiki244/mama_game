using UnityEngine;

// MAMAの成長ステータス（GDD SYS-12 育成メタの前段）
// 勝利でEXP獲得 → レベルアップで4ステータスが上昇する
public class PlayerStats
{
    public int Level = 1;
    public int Exp = 0;

    public int MaxHP = 120;
    public int Attack = 6;    // カード威力に加算される（敵強化に合わせて低めに設定）
    public int Defense = 5;   // 被ダメージを軽減
    public int Speed = 5;     // 回避率に影響（Speed×2 %）

    public int ExpToNext => Level * 50;

    // EXPを加算し、レベルアップしたら true（複数レベル一気に上がることもある）
    public bool GainExp(int amount)
    {
        Exp += amount;
        bool leveled = false;
        while (Exp >= ExpToNext)
        {
            Exp -= ExpToNext;
            LevelUp();
            leveled = true;
        }
        return leveled;
    }

    void LevelUp()
    {
        Level++;
        MaxHP += 10;
        Attack += 2;
        Defense += 1;
        Speed += 1;
    }
}
