using UnityEngine;
using System.Collections.Generic;

// スキルの種別（攻撃 / 回復 / バフ）。データ駆動で効果を分岐する
public enum SkillKind { Attack, Heal, Buff }

// プリプロ用スキル定義（GDD: ポリオミノ型ピース）
// プリプロではScriptableObject化せずコード内定義で高速に回す
public class ProtoSkill
{
    public string id;
    public string skillName;
    public int power;
    public Color color;
    public Vector2Int[] shape; // ピース形状（マスのオフセット集合）

    // 種別と効果量（既定は攻撃。回復は power を回復量として使う）
    public SkillKind kind = SkillKind.Attack;
    public int buffAtk;     // バフ時の攻撃力上昇
    public int buffDef;     // バフ時の防御力上昇
    public int buffTurns;   // バフの持続ターン

    public int Size => shape.Length;

    public ProtoSkill(string id, string skillName, int power, Color color, params Vector2Int[] shape)
    {
        this.id = id;
        this.skillName = skillName;
        this.power = power;
        this.color = color;
        this.shape = shape;
    }

    // 種別・バフ量を後付け設定する（コレクション初期化子で連結できるようthisを返す）
    public ProtoSkill SetKind(SkillKind k) { kind = k; return this; }
    public ProtoSkill SetBuff(int atk, int def, int turns)
    {
        kind = SkillKind.Buff; buffAtk = atk; buffDef = def; buffTurns = turns; return this;
    }
}

public static class ProtoSkills
{
    static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    public static readonly List<ProtoSkill> All = new List<ProtoSkill>
    {
        // 4マス: S型（隙間ができやすい厄介形状）
        new ProtoSkill("vacuum", "サイクロンバースト",   80,  new Color(1f, 0.95f, 0.5f),
            V(1,0), V(2,0), V(0,1), V(1,1)),
        // 4マス: T型（初期所持）
        new ProtoSkill("blade",  "クロスエッジ",         85,  new Color(0.55f, 0.95f, 0.7f),
            V(0,0), V(1,0), V(2,0), V(1,1)),
        // 5マス: F型ペントミノ（パズル界屈指の収まりの悪さ）
        new ProtoSkill("scold",  "ジャッジメントボイス", 110, new Color(1f, 0.55f, 0.8f),
            V(1,0), V(2,0), V(0,1), V(1,1), V(1,2)),
        // 9マス: 稲妻型の階段9連（最高威力＝最凶のいびつさ。広い空き地が必須）
        new ProtoSkill("apron",  "アスラ・レガリア",     200, new Color(0.75f, 0.5f, 1f),
            V(0,0), V(1,0), V(1,1), V(2,1), V(2,2), V(3,2), V(3,3), V(4,3), V(4,4)),

        // 5マス: 十字型の回復（HPを回復する。中型で素直に置ける）
        new ProtoSkill("heal",   "ヒーリングサークル",    70,  new Color(0.45f, 1f, 0.7f),
            V(1,0), V(0,1), V(1,1), V(2,1), V(1,2)).SetKind(SkillKind.Heal),

        // 6マス: 2×3ブロックのバフ（味方の攻撃・防御を一時上昇）
        new ProtoSkill("buff",   "ガーディアンヴェール",  0,   new Color(0.5f, 0.75f, 1f),
            V(0,0), V(1,0), V(2,0), V(0,1), V(1,1), V(2,1)).SetBuff(8, 6, 3),

        // 12マス: 3×4ブロックの大技（順次点滅の発動対象。閾値10で発動）
        new ProtoSkill("meikyo", "明鏡止水",             280, new Color(0.6f, 0.9f, 0.95f),
            V(0,0), V(1,0), V(2,0), V(0,1), V(1,1), V(2,1),
            V(0,2), V(1,2), V(2,2), V(0,3), V(1,3), V(2,3)),

        // 17マス: 4×4ブロック＋突起1（順次点滅の発動対象。閾値15でも発動）
        new ProtoSkill("vermilion", "ヴァーミリオンノヴァ", 420, new Color(1f, 0.4f, 0.3f),
            V(0,0), V(1,0), V(2,0), V(3,0), V(0,1), V(1,1), V(2,1), V(3,1),
            V(0,2), V(1,2), V(2,2), V(3,2), V(0,3), V(1,3), V(2,3), V(3,3), V(4,1)),

        // 25マス: 5×5ブロックの最強技（盤面の1/4を占有する超ハイリスク）
        new ProtoSkill("catastrophe", "カタストロフ",      650, new Color(0.85f, 0.3f, 0.85f),
            V(0,0), V(1,0), V(2,0), V(3,0), V(4,0), V(0,1), V(1,1), V(2,1), V(3,1), V(4,1),
            V(0,2), V(1,2), V(2,2), V(3,2), V(4,2), V(0,3), V(1,3), V(2,3), V(3,3), V(4,3),
            V(0,4), V(1,4), V(2,4), V(3,4), V(4,4)),

        // --- 追加ピース ---
        // 4マス: 田型（O）※初期所持
        new ProtoSkill("quartz", "クォーツプリズム",      85,  new Color(0.8f, 0.7f, 1f),
            V(0,0), V(1,0), V(0,1), V(1,1)),
        // 7マス: 十字＋（中型の高威力）
        new ProtoSkill("tempest", "テンペストエッジ",     150, new Color(0.5f, 0.95f, 0.85f),
            V(1,0), V(0,1), V(1,1), V(2,1), V(1,2), V(0,2), V(2,2)),
    };

    public const int NormalAttackPower = 10; // 空白マス=通常攻撃

    // 初期所持ピース（4マスを3種類）
    public static readonly string[] InitialOwned = { "vacuum", "blade", "quartz" };

    // 敵を倒すごとに解放されるピース（すべて5マス以上・後半ほど大型）
    public static readonly string[] UnlockOrder =
    {
        "heal", "scold", "buff", "tempest", "apron", "meikyo", "vermilion", "catastrophe",
    };

    public static ProtoSkill Find(string id) => All.Find(s => s.id == id);
}
