using UnityEngine;
using System.Collections.Generic;

// 敵の技（名前・威力倍率・ヒット数・出やすさ）
public class EnemyAttack
{
    public string name;
    public float mult;  // 基本攻撃力に掛ける倍率
    public int hits;    // ヒット数（連続攻撃）
    public int weight;  // 抽選の重み（大きいほど出やすい）
}

// 敵の定義（名前・ドット絵・ステータス・表示サイズ・技）
public class ProtoEnemy
{
    public string id;
    public string enemyName;
    public Sprite sprite;
    public int baseHP;
    public int minAtk, maxAtk;
    public Vector2 battleSize;   // バトル画面での大きさ
    public Vector2 mapSize;      // マップ上での大きさ
    public float atbInterval;    // ATBモードでの攻撃間隔（秒）
    public bool flying;          // 飛行する敵は地面に立たない（影もつけない）
    public int levelOffset;      // ボス用のレベル補正（Wave+この値で強さを計算）
    public EnemyAttack[] attacks;
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
                        baseHP = 60,  minAtk = 6,  maxAtk = 12, battleSize = new Vector2(300, 200), mapSize = new Vector2(78, 52),
                        atbInterval = 5.0f,
                        attacks = new[]
                        {
                            new EnemyAttack { name = "たいあたり",     mult = 1.0f,  hits = 1, weight = 45 },
                            new EnemyAttack { name = "のしかかり",     mult = 1.6f,  hits = 1, weight = 20 },
                            new EnemyAttack { name = "ぷるぷる連打",   mult = 0.55f, hits = 2, weight = 15 },
                            new EnemyAttack { name = "酸のしぶき",     mult = 0.85f, hits = 1, weight = 12 },
                            new EnemyAttack { name = "ようすを見ている", mult = 0f,  hits = 0, weight = 8 },
                        } },
                    new ProtoEnemy { id = "bat",    enemyName = "コウモリ", sprite = ProtoPixelArt.Bat(),
                        baseHP = 45,  minAtk = 8,  maxAtk = 14, battleSize = new Vector2(310, 160), mapSize = new Vector2(80, 42),
                        atbInterval = 3.5f, flying = true, // 素早いので攻撃間隔が短い
                        attacks = new[]
                        {
                            new EnemyAttack { name = "ひっかき",       mult = 1.0f,  hits = 1, weight = 40 },
                            new EnemyAttack { name = "急降下アタック", mult = 1.5f,  hits = 1, weight = 18 },
                            new EnemyAttack { name = "連続ひっかき",   mult = 0.5f,  hits = 3, weight = 20 },
                            new EnemyAttack { name = "超音波",         mult = 0.75f, hits = 1, weight = 14 },
                            new EnemyAttack { name = "きりもみ突進",   mult = 1.3f,  hits = 1, weight = 8 },
                        } },
                    new ProtoEnemy { id = "golem",  enemyName = "ゴーレム", sprite = ProtoPixelArt.Golem(),
                        baseHP = 140, minAtk = 10, maxAtk = 18, battleSize = new Vector2(430, 390), mapSize = new Vector2(66, 60),
                        atbInterval = 6.5f, // 鈍重だが一発が重い
                        attacks = new[]
                        {
                            new EnemyAttack { name = "なぐりつけ",     mult = 1.0f,  hits = 1, weight = 40 },
                            new EnemyAttack { name = "じしんふみつけ", mult = 1.8f,  hits = 1, weight = 22 },
                            new EnemyAttack { name = "岩石ラッシュ",   mult = 0.65f, hits = 2, weight = 18 },
                            new EnemyAttack { name = "岩とばし",       mult = 0.85f, hits = 1, weight = 12 },
                            new EnemyAttack { name = "ちからをためている", mult = 0f, hits = 0, weight = 8 },
                        } },
                    new ProtoEnemy { id = "dragon", enemyName = "ドラゴン", sprite = ProtoPixelArt.Dragon(),
                        baseHP = 170, minAtk = 12, maxAtk = 20, battleSize = new Vector2(700, 460), mapSize = new Vector2(112, 74),
                        atbInterval = 5.5f, flying = true,
                        levelOffset = 9, // 山頂のボス。序盤のレベルでは歯が立たない
                        attacks = new[]
                        {
                            new EnemyAttack { name = "かみつき",       mult = 1.0f,  hits = 1, weight = 35 },
                            new EnemyAttack { name = "ほのおのブレス", mult = 1.9f,  hits = 1, weight = 22 },
                            new EnemyAttack { name = "つばさの連撃",   mult = 0.6f,  hits = 3, weight = 20 },
                            new EnemyAttack { name = "しっぽ振り回し", mult = 0.85f, hits = 2, weight = 15 },
                            new EnemyAttack { name = "天をあおいで咆哮した", mult = 0f, hits = 0, weight = 8 },
                        } },
                    new ProtoEnemy { id = "kinggolem", enemyName = "キングゴーレム", sprite = ProtoPixelArt.Golem(),
                        baseHP = 300, minAtk = 16, maxAtk = 24, battleSize = new Vector2(560, 510), mapSize = new Vector2(105, 96),
                        atbInterval = 6.0f,
                        levelOffset = 4, // 風雨の森の中ボス
                        attacks = new[]
                        {
                            new EnemyAttack { name = "なぐりつけ",     mult = 1.0f, hits = 1, weight = 40 },
                            new EnemyAttack { name = "大地砕き",       mult = 2.0f, hits = 1, weight = 25 },
                            new EnemyAttack { name = "岩石乱舞",       mult = 0.7f, hits = 3, weight = 20 },
                            new EnemyAttack { name = "ちからをためている", mult = 0f, hits = 0, weight = 15 },
                        } },
                };
            }
            return _all;
        }
    }

    // 技の重み付き抽選
    public static EnemyAttack PickAttack(ProtoEnemy enemy)
    {
        int total = 0;
        foreach (var a in enemy.attacks) total += a.weight;
        int roll = Random.Range(0, total);
        foreach (var a in enemy.attacks)
        {
            roll -= a.weight;
            if (roll < 0) return a;
        }
        return enemy.attacks[0];
    }

    public static ProtoEnemy Find(string id) => All.Find(e => e.id == id);

    // エリア別の出現抽選（ドラゴンはボス専用になったので通常湧きしない）
    public static ProtoEnemy RandomEnemy(int area)
    {
        float r = Random.value;
        switch (area)
        {
            case 1: // 風雨の森: 強めの敵が多い
                if (r < 0.20f) return Find("slime");
                if (r < 0.55f) return Find("bat");
                return Find("golem");
            case 2: // 嵐の山頂: ゴーレムだけがうろつく
                return Find("golem");
            default: // 草原: 弱い敵中心
                if (r < 0.45f) return Find("slime");
                if (r < 0.80f) return Find("bat");
                return Find("golem");
        }
    }
}
