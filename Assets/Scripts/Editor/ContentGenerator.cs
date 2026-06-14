#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// 本設のカード/敵/設定の ScriptableObject を一括生成するエディタツール。
//   メニュー: MamaGame > コンテンツ(SO)を生成
// 生成後は各 .asset を Inspector で自由に編集・追加できる。
public static class ContentGenerator
{
    const string CardsDir   = "Assets/GameData/Cards";
    const string EnemiesDir = "Assets/GameData/Enemies";
    const string ResDir     = "Assets/Resources/GameData";

    [MenuItem("MamaGame/コンテンツ(SO)を生成")]
    public static void Generate()
    {
        if (!EditorUtility.DisplayDialog("コンテンツ生成",
            "カード/敵/設定のSOアセットを生成します。\n既存の自動生成アセットは上書きされます（手動編集はリセットされます）。続けますか？",
            "生成する", "やめる"))
            return;

        EnsureFolder("Assets/GameData");
        EnsureFolder(CardsDir);
        EnsureFolder(EnemiesDir);
        EnsureFolder("Assets/Resources");
        EnsureFolder(ResDir);

        // ---- GameConfig ----
        var config = CreateOrReplace<GameConfig>($"{ResDir}/GameConfig.asset");

        // ---- 通常攻撃カード ----
        var normal = CreateOrReplace<CardDef>($"{CardsDir}/card_normal.asset");
        normal.id = "normal"; normal.displayName = "通常攻撃"; normal.kind = CardKind.Attack;
        normal.shapeRows = new[] { "X" }; normal.power = config.normalAttackPower;
        normal.manaCostOverride = config.normalAttackMana; normal.color = new Color(0.6f, 0.6f, 0.68f);
        normal.description = "空白マスから出る基本攻撃。";
        EditorUtility.SetDirty(normal);

        // ---- カード定義 ----
        var cards = new List<CardDef>();
        // アタック（効果なし、size5〜19）
        cards.Add(MakeAttack("slash",     "スラッシュ",     5,  C(0.9f,0.95f,1f)));
        cards.Add(MakeAttack("fireball",  "ファイアボール", 6,  C(1f,0.55f,0.35f)));
        cards.Add(MakeAttack("aquaedge",  "アクアエッジ",   7,  C(0.4f,0.8f,1f)));
        cards.Add(MakeAttack("thunder",   "サンダーボルト", 8,  C(1f,0.9f,0.4f)));
        cards.Add(MakeAttack("sunshine",  "サンシャイン",   9,  C(1f,0.85f,0.55f)));
        cards.Add(MakeAttack("shingan",   "心眼一閃",       10, C(0.8f,0.95f,0.9f)));
        cards.Add(MakeAttack("rekku",     "烈空斬",         11, C(0.7f,0.85f,1f)));
        cards.Add(MakeAttack("gouka",     "業火爆裂",       12, C(1f,0.45f,0.3f)));
        cards.Add(MakeAttack("hyoga",     "氷河衝",         13, C(0.6f,0.9f,1f)));
        cards.Add(MakeAttack("raijin",    "雷神撃",         14, C(1f,0.95f,0.5f)));
        cards.Add(MakeAttack("amaterasu", "天照爆",         15, C(1f,0.8f,0.4f)));
        cards.Add(MakeAttack("kokuu",     "虚空斬",         16, C(0.7f,0.6f,0.95f)));
        cards.Add(MakeAttack("guren",     "紅蓮華",         17, C(1f,0.4f,0.55f)));
        cards.Add(MakeAttack("soukyu",    "蒼穹閃",         18, C(0.5f,0.7f,1f)));
        cards.Add(MakeAttack("kannari",   "神鳴り",         19, C(0.85f,0.85f,1f)));
        // size20: 使用時に点滅
        var shuen = MakeAttack("shuen", "終焉ノ刃", 20, C(0.85f,0.3f,0.85f));
        shuen.description = "使用時に点滅ゲームを実施する。";
        shuen.effects = new[] { Eff(CardEffectType.BlinkOnUse, 0, 0) };
        cards.Add(shuen);

        // スキル
        cards.Add(MakeSkill("draw",      "ドロー",         3,  C(0.7f,0.95f,0.7f), "カードを1枚ドローする。", 0,
            Eff(CardEffectType.Draw, 1, 0)));
        cards.Add(MakeSkill("twodraw",   "ツードロー",     11, C(0.6f,0.9f,0.6f), "カードを2枚ドローする。", 5,
            Eff(CardEffectType.Draw, 2, 0)));
        cards.Add(MakeSkill("blink",     "点滅",           3,  C(1f,0.85f,0.4f), "次に使うアタックで点滅ゲームを実施する。", 0,
            Eff(CardEffectType.PrimeNextAttackBlink, 0, 0)));
        cards.Add(MakeSkill("protect",   "プロテクト",     7,  C(0.6f,0.8f,1f), "次の相手の攻撃ダメージを50%軽減する。", 2,
            Eff(CardEffectType.Protect, 50, 1)));
        cards.Add(MakeSkill("manaboost", "マナブースト",   3,  C(0.8f,0.7f,1f), "次の自分のターンに使えるマナを+1する。", 3,
            Eff(CardEffectType.ManaBoostNextTurn, 1, 0)));
        // StS系5種
        cards.Add(MakeSkill("defend",    "防御",           5,  C(0.55f,0.75f,1f), "ブロックを8得る（被ダメージを肩代わり）。", 0,
            Eff(CardEffectType.Block, 8, 0)));
        cards.Add(MakeSkill("weak",      "弱体化",         5,  C(0.7f,0.5f,0.85f), "敵の攻撃力を25%下げる（2ターン）。", 3,
            Eff(CardEffectType.Weak, 25, 2)));
        cards.Add(MakeSkill("poison",    "毒",             6,  C(0.5f,0.85f,0.4f), "敵に毒5を与える（毎ターンダメージ）。", 4,
            Eff(CardEffectType.Poison, 5, 0)));
        cards.Add(MakeSkill("strength",  "筋力",           6,  C(1f,0.6f,0.4f), "攻撃力を3上げる（戦闘中持続）。", 6,
            Eff(CardEffectType.Strength, 3, 0)));
        cards.Add(MakeSkill("heal",      "治癒",           5,  C(0.5f,1f,0.7f), "HPを12回復する。", 2,
            Eff(CardEffectType.Heal, 12, 0)));

        // ---- 敵定義 ----
        var enemies = new List<EnemyDef>();
        enemies.Add(MakeEnemy("slime", "スライム", EnemySpriteKey.Slime, 160, 7, 13, new Vector2(300,200), new Vector2(78,52), false, 0, 18,
            new[]{ EA("たいあたり",1.0f,1,45), EA("のしかかり",1.6f,1,20), EA("ぷるぷる連打",0.55f,2,15), EA("酸のしぶき",0.85f,1,12), EA("ようすを見ている",0f,0,8) }));
        enemies.Add(MakeEnemy("bat", "コウモリ", EnemySpriteKey.Bat, 120, 9, 15, new Vector2(310,160), new Vector2(80,42), true, 0, 22,
            new[]{ EA("ひっかき",1.0f,1,40), EA("急降下アタック",1.5f,1,18), EA("連続ひっかき",0.5f,3,20), EA("超音波",0.75f,1,14), EA("きりもみ突進",1.3f,1,8) }));
        enemies.Add(MakeEnemy("golem", "ゴーレム", EnemySpriteKey.Golem, 340, 11, 19, new Vector2(430,390), new Vector2(66,60), false, 0, 35,
            new[]{ EA("なぐりつけ",1.0f,1,40), EA("じしんふみつけ",1.8f,1,22), EA("岩石ラッシュ",0.65f,2,18), EA("岩とばし",0.85f,1,12), EA("ちからをためている",0f,0,8) }));
        enemies.Add(MakeEnemy("oni", "オニ", EnemySpriteKey.Oni, 680, 17, 25, new Vector2(460,530), new Vector2(92,106), false, 4, 80,
            new[]{ EA("かなぼう振り回し",1.0f,1,40), EA("地獄突き",1.9f,1,25), EA("鬼の連打",0.65f,3,20), EA("雄叫びをあげている",0f,0,15) }));
        var dragon = MakeEnemy("dragon", "ドラゴン", EnemySpriteKey.Dragon, 620, 13, 21, new Vector2(720,440), new Vector2(135,82), true, 9, 200,
            new[]{ EA("かみつき",1.0f,1,35), EA("ほのおのブレス",1.9f,1,22), EA("つばさの連撃",0.6f,3,20), EA("しっぽ振り回し",0.85f,2,15), EA("天をあおいで咆哮した",0f,0,8) });
        dragon.useDragonFrontOnMap = true; EditorUtility.SetDirty(dragon);
        enemies.Add(dragon);

        // ---- データベース ----
        var db = CreateOrReplace<ContentDatabase>($"{ResDir}/ContentDatabase.asset");
        db.config = config;
        db.normalAttack = normal;
        db.cards = cards;
        db.enemies = enemies;
        EditorUtility.SetDirty(db);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完了",
            $"生成しました。\nカード: {cards.Count + 1}種 / 敵: {enemies.Count}種\n\n{ResDir}/ContentDatabase.asset", "OK");
    }

    // ---- ヘルパー ----
    static Color C(float r, float g, float b) => new Color(r, g, b);

    static CardEffect Eff(CardEffectType t, int amount, int duration)
        => new CardEffect { type = t, amount = amount, duration = duration };

    static CardDef MakeAttack(string id, string name, int size, Color col)
    {
        var c = CreateOrReplace<CardDef>($"{CardsDir}/card_{id}.asset");
        c.id = id; c.displayName = name; c.kind = CardKind.Attack;
        c.shapeRows = Block(size); c.power = size * 12; c.color = col;
        c.manaCostOverride = -1; c.effects = new CardEffect[0];
        c.minDepth = Mathf.Clamp(size - 5, 0, 15);   // 大型（強力）ほど深い深度でのみ入手
        c.maxDepth = size >= 17 ? 0 : c.minDepth + 5; // 弱小アタックは終盤に出ない（強カードは上限なし）
        c.description = "";
        EditorUtility.SetDirty(c);
        return c;
    }

    static CardDef MakeSkill(string id, string name, int size, Color col, string desc, int minDepth, params CardEffect[] effects)
    {
        var c = CreateOrReplace<CardDef>($"{CardsDir}/card_{id}.asset");
        c.id = id; c.displayName = name; c.kind = CardKind.Skill;
        c.shapeRows = Block(size); c.power = 0; c.color = col;
        c.manaCostOverride = -1; c.effects = effects; c.description = desc;
        c.minDepth = minDepth;
        c.maxDepth = 0; // スキルは終盤まで有用なため上限なし（必要なら個別にInspector調整）
        EditorUtility.SetDirty(c);
        return c;
    }

    static EnemyDef MakeEnemy(string id, string name, EnemySpriteKey key, int hp, int minA, int maxA,
        Vector2 bsize, Vector2 msize, bool flying, int lvOff, int money, EnemyAttackDef[] atks)
    {
        var e = CreateOrReplace<EnemyDef>($"{EnemiesDir}/enemy_{id}.asset");
        e.id = id; e.enemyName = name; e.spriteKey = key;
        e.baseHP = hp; e.minAtk = minA; e.maxAtk = maxA;
        e.battleSize = bsize; e.mapSize = msize; e.flying = flying;
        e.levelOffset = lvOff; e.moneyReward = money; e.attacks = atks;
        EditorUtility.SetDirty(e);
        return e;
    }

    static EnemyAttackDef EA(string name, float mult, int hits, int weight)
        => new EnemyAttackDef { name = name, mult = mult, hits = hits, weight = weight };

    // n マスを連結した「ほぼ正方形」の形状を文字マップで作る
    static string[] Block(int n)
    {
        int w = Mathf.CeilToInt(Mathf.Sqrt(n));
        int rows = Mathf.CeilToInt(n / (float)w);
        var result = new string[rows];
        int placed = 0;
        for (int y = 0; y < rows; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < w; x++)
                sb.Append(placed++ < n ? 'X' : '.');
            result[y] = sb.ToString();
        }
        return result;
    }

    static T CreateOrReplace<T>(string path) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing; // 既存を再利用（参照を保つ）
        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string leaf = path.Substring(slash + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
