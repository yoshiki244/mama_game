using UnityEngine;
using System.Collections.Generic;

// プリプロ用スキル定義（GDD: ポリオミノ型ピース 1〜10マス想定）
// プリプロではScriptableObject化せずコード内定義で高速に回す
public class ProtoSkill
{
    public string id;
    public string skillName;
    public int power;
    public Color color;
    public Vector2Int[] shape; // ピース形状（マスのオフセット集合）

    public int Size => shape.Length;

    public ProtoSkill(string id, string skillName, int power, Color color, params Vector2Int[] shape)
    {
        this.id = id;
        this.skillName = skillName;
        this.power = power;
        this.color = color;
        this.shape = shape;
    }
}

public static class ProtoSkills
{
    static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    public static readonly List<ProtoSkill> All = new List<ProtoSkill>
    {
        // 1マス: 低威力・確実
        new ProtoSkill("cook",   "マザーフレア",         30,  new Color(1f, 0.65f, 0.45f),
            V(0,0)),
        // 2マス: I型
        new ProtoSkill("clip",   "クリップスティンガー", 45,  new Color(0.55f, 0.95f, 0.6f),
            V(0,0), V(1,0)),
        // 3マス: L型（角に引っかかる）
        new ProtoSkill("towel",  "アクアスラッシュ",     60,  new Color(0.5f, 0.8f, 1f),
            V(0,0), V(0,1), V(1,1)),
        // 4マス: S型（隙間ができやすい厄介形状）
        new ProtoSkill("vacuum", "サイクロンバースト",   80,  new Color(1f, 0.95f, 0.5f),
            V(1,0), V(2,0), V(0,1), V(1,1)),
        // 5マス: F型ペントミノ（パズル界屈指の収まりの悪さ）
        new ProtoSkill("scold",  "ジャッジメントボイス", 110, new Color(1f, 0.55f, 0.8f),
            V(1,0), V(2,0), V(0,1), V(1,1), V(1,2)),
        // 9マス: 稲妻型の階段9連（最高威力＝最凶のいびつさ。広い空き地が必須）
        new ProtoSkill("apron",  "アスラ・レガリア",     200, new Color(0.75f, 0.5f, 1f),
            V(0,0), V(1,0), V(1,1), V(2,1), V(2,2), V(3,2), V(3,3), V(4,3), V(4,4)),
    };

    public const int NormalAttackPower = 10; // 空白マス=通常攻撃
}
