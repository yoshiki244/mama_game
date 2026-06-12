using UnityEngine;
using System.Collections.Generic;

// 敵の定義（名前・ドット絵・ステータス・表示サイズ）
public class ProtoEnemy
{
    public string id;
    public string enemyName;
    public Sprite sprite;
    public int baseHP;
    public int minAtk, maxAtk;
    public Vector2 battleSize; // バトル画面での大きさ
    public Vector2 mapSize;    // マップ上での大きさ
}

public static class ProtoEnemies
{
    static List<ProtoEnemy> _all;

    public static List<ProtoEnemy> All
    {
        get
        {
            if (_all == null)
            {
                _all = new List<ProtoEnemy>
                {
                    new ProtoEnemy { id = "slime",  enemyName = "スライム", sprite = ProtoPixelArt.Slime(),
                        baseHP = 60,  minAtk = 6,  maxAtk = 12, battleSize = new Vector2(300, 200), mapSize = new Vector2(78, 52) },
                    new ProtoEnemy { id = "bat",    enemyName = "コウモリ", sprite = ProtoPixelArt.Bat(),
                        baseHP = 45,  minAtk = 8,  maxAtk = 14, battleSize = new Vector2(310, 160), mapSize = new Vector2(80, 42) },
                    new ProtoEnemy { id = "golem",  enemyName = "ゴーレム", sprite = ProtoPixelArt.Golem(),
                        baseHP = 140, minAtk = 10, maxAtk = 18, battleSize = new Vector2(340, 310), mapSize = new Vector2(66, 60) },
                    new ProtoEnemy { id = "dragon", enemyName = "ドラゴン", sprite = ProtoPixelArt.Dragon(),
                        baseHP = 170, minAtk = 12, maxAtk = 20, battleSize = new Vector2(540, 355), mapSize = new Vector2(112, 74) },
                };
            }
            return _all;
        }
    }

    // 出現抽選（弱い敵ほど出やすく、ドラゴンはレア）
    public static ProtoEnemy RandomEnemy()
    {
        float r = Random.value;
        if (r < 0.12f) return All[3]; // ドラゴン 12%
        if (r < 0.35f) return All[2]; // ゴーレム 23%
        if (r < 0.65f) return All[1]; // コウモリ 30%
        return All[0];                // スライム 35%
    }
}
