using UnityEngine;

// 敵の見た目（ProtoPixelArtのドット絵を選ぶ）
public enum EnemySpriteKey { Slime, Bat, Golem, Dragon, Oni }

[System.Serializable]
public class EnemyAttackDef
{
    public string name = "こうげき";
    public float mult = 1f;   // 基本攻撃力に掛ける倍率
    public int hits = 1;      // ヒット数（0=様子見）
    public int weight = 30;   // 抽選の重み
}

// 敵の定義。ScriptableObjectとしてUnity上で編集・追加できる。
[CreateAssetMenu(fileName = "Enemy", menuName = "MamaGame/Enemy")]
public class EnemyDef : ScriptableObject
{
    public string id = "enemy";
    public string enemyName = "敵";
    public EnemySpriteKey spriteKey = EnemySpriteKey.Slime;
    public bool useDragonFrontOnMap; // マップで正面向きドラゴンを使う

    public int baseHP = 100;
    public int minAtk = 8, maxAtk = 14;
    public Vector2 battleSize = new Vector2(300, 200);
    public Vector2 mapSize = new Vector2(78, 52);
    public bool flying;
    public int levelOffset;     // ボス用のレベル補正（Wave+この値で強さ）
    public int moneyReward = 20; // 撃破で得るお金（編集可）
    public EnemyAttackDef[] attacks;

    public Sprite BattleSprite() => Resolve(spriteKey);
    public Sprite MapSprite() => useDragonFrontOnMap ? ProtoPixelArt.DragonFront() : Resolve(spriteKey);

    static Sprite Resolve(EnemySpriteKey k)
    {
        switch (k)
        {
            case EnemySpriteKey.Bat:    return ProtoPixelArt.Bat();
            case EnemySpriteKey.Golem:  return ProtoPixelArt.Golem();
            case EnemySpriteKey.Dragon: return ProtoPixelArt.Dragon();
            case EnemySpriteKey.Oni:    return ProtoPixelArt.Oni();
            default:                    return ProtoPixelArt.Slime();
        }
    }

    public EnemyAttackDef PickAttack()
    {
        if (attacks == null || attacks.Length == 0) return new EnemyAttackDef();
        int total = 0;
        foreach (var a in attacks) total += a.weight;
        if (total <= 0) return attacks[0];
        int roll = Random.Range(0, total);
        foreach (var a in attacks) { roll -= a.weight; if (roll < 0) return a; }
        return attacks[0];
    }
}
