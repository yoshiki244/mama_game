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
        var slash = MakeAttack("slash", "スラッシュ", 5, C(0.9f,0.95f,1f));
        slash.shapeRows = new[]{ "XXX", "..X", "..X" }; slash.manaCostOverride = 3; EditorUtility.SetDirty(slash);
        cards.Add(slash);
        var fireball = MakeAttack("fireball", "ファイアボール", 6, C(1f,0.55f,0.35f));
        fireball.manaCostOverride = 3; fireball.description = "敵をやけど状態にする（毎ターン5ダメージ）。";
        fireball.effects = new[]{ Eff(CardEffectType.Burn, 5, 0) }; EditorUtility.SetDirty(fireball);
        cards.Add(fireball);
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
        var blink = MakeSkill("blink", "点滅", 3, C(1f,0.85f,0.4f), "次に使うアタックで点滅ゲームを実施する。", 0,
            Eff(CardEffectType.PrimeNextAttackBlink, 0, 0));
        blink.shapeRows = new[]{ "X", "X", "X" }; EditorUtility.SetDirty(blink);
        cards.Add(blink);
        cards.Add(TuneS(MakeSkill("protect",   "プロテクト",     7,  C(0.6f,0.8f,1f), "次の相手の攻撃ダメージを50%軽減する。", 2,
            Eff(CardEffectType.Protect, 50, 1)), 2));
        cards.Add(MakeSkill("manaboost", "マナブースト",   3,  C(0.8f,0.7f,1f), "次の自分のターンに使えるマナを+1する。", 3,
            Eff(CardEffectType.ManaBoostNextTurn, 1, 0)));
        // StS系5種
        var defend = MakeSkill("defend", "ガード", 5, C(0.55f,0.75f,1f), "ダメージを軽減する（25%）。", 0,
            Eff(CardEffectType.Protect, 25, 1));
        defend.shapeRows = new[]{ "X..", "XXX", "X.." }; defend.manaCostOverride = 3; EditorUtility.SetDirty(defend);
        cards.Add(defend);
        cards.Add(MakeSkill("weak",      "弱体化",         5,  C(0.7f,0.5f,0.85f), "敵の攻撃力を25%下げる（2ターン）。", 3,
            Eff(CardEffectType.Weak, 25, 2)));
        cards.Add(MakeSkill("poison",    "毒",             6,  C(0.5f,0.85f,0.4f), "敵に毒5を与える（毎ターンダメージ）。", 4,
            Eff(CardEffectType.Poison, 5, 0)));
        cards.Add(TuneS(MakeSkill("strength",  "筋力",           6,  C(1f,0.6f,0.4f), "攻撃力を3上げる（戦闘中持続）。", 6,
            Eff(CardEffectType.Strength, 3, 0)), 2));
        var heal = MakeSkill("heal", "ヒール", 6, C(0.5f,1f,0.7f), "最大HPの50%を回復する。", 2,
            Eff(CardEffectType.HealPercent, 50, 0));
        heal.shapeRows = new[]{ "X.X", "XXX", ".X." }; heal.manaCostOverride = 3; EditorUtility.SetDirty(heal);
        cards.Add(heal);

        // ==================== 拡張カード（バランス調整済み） ====================
        // 攻撃特化：同マス数の素攻撃（マス×12）より威力を抑え、効果で差別化
        // minDepth: 序盤0-2 / 中盤3-6 / 終盤7以降

        // ---- 攻撃系 ----
        cards.Add(TuneA(MakeAttack("renzan",    "連斬",           6,  C(0.95f,0.8f,0.8f)), 3, 25, 1, "3回連続でヒットする。",
            Eff(CardEffectType.MultiHit, 3, 0)));
        cards.Add(TuneA(MakeAttack("kyuketsu",  "吸血斬",         7,  C(0.8f,0.3f,0.5f)), 3, 65, 3, "与ダメージの50%をHP回復。",
            Eff(CardEffectType.LifeSteal, 50, 0)));
        cards.Add(TuneA(MakeAttack("shokei",    "処刑",           8,  C(0.7f,0.25f,0.3f)), 3, 80, 4, "敵HPが30%以下なら威力2倍。",
            Eff(CardEffectType.Execute, 30, 0)));
        cards.Add(TuneA(MakeAttack("kibaku",    "起爆",           7,  C(1f,0.5f,0.2f)), 3, 40, 5, "毒・やけどを消費し残量×6の追加ダメージ。",
            Eff(CardEffectType.Detonate, 6, 0)));
        cards.Add(TuneA(MakeAttack("handou",    "反動砲",         12, C(0.9f,0.6f,0.3f)), 4, 300, 7, "超威力。ただし反動で自分に20ダメージ。",
            Eff(CardEffectType.SelfDamage, 20, 0)));
        cards.Add(TuneA(MakeAttack("chargeken", "チャージ斬",     6,  C(0.85f,0.85f,0.5f)), 3, 50, 2, "この戦闘で使うたび威力+15。",
            Eff(CardEffectType.GrowingPower, 15, 0)));
        cards.Add(TuneA(MakeAttack("banmenzan", "盤面斬",         8,  C(0.6f,0.8f,0.9f)), 3, 30, 5, "解放マス数×2を威力に加算。",
            Eff(CardEffectType.BoardPower, 2, 0)));
        cards.Add(TuneA(MakeAttack("lastresort","ラストリゾート", 9,  C(0.9f,0.4f,0.4f)), 4, 80, 6, "HPが低いほど威力増（最大+200%）。",
            Eff(CardEffectType.LowHpPower, 200, 0)));
        cards.Add(TuneA(MakeAttack("manabakuha","マナバースト",   10, C(0.6f,0.5f,1f)), 1, 40, 6, "残りマナを全消費しマナ×40を威力に加算。",
            Eff(CardEffectType.ManaBurst, 40, 0)));
        cards.Add(TuneA(MakeAttack("tefudadan", "手札爆撃",       8,  C(0.9f,0.7f,0.5f)), 3, 30, 5, "手札の残り枚数×25を威力に加算。",
            Eff(CardEffectType.HandPower, 25, 0)));
        cards.Add(TuneA(MakeAttack("ooburi",    "空振り大剣",     10, C(0.8f,0.75f,0.7f)), 4, 200, 4, "50%で威力2倍、50%で不発。",
            Eff(CardEffectType.Gamble5050, 0, 0)));
        cards.Add(TuneA(MakeAttack("dokuga",    "毒牙",           5,  C(0.55f,0.8f,0.35f)), 2, 45, 2, "ダメージ＋毒3を付与。",
            Eff(CardEffectType.Poison, 3, 0)));
        cards.Add(TuneA(MakeAttack("hiken",     "炎の刃",         5,  C(1f,0.45f,0.25f)), 2, 45, 2, "ダメージ＋やけど3を付与。",
            Eff(CardEffectType.Burn, 3, 0)));
        cards.Add(TuneA(MakeAttack("hyouken",   "氷の刃",         6,  C(0.55f,0.85f,1f)), 2, 50, 3, "ダメージ＋敵の攻撃力を20%低下（1ターン）。",
            Eff(CardEffectType.Weak, 20, 1)));
        cards.Add(TuneA(MakeAttack("raigeki2",  "雷撃",           7,  C(1f,0.95f,0.45f)), 3, 70, 4, "25%で敵を1ターン麻痺させる。",
            Eff(CardEffectType.StunChance, 25, 0)));
        cards.Add(TuneA(MakeAttack("juryoku",   "重力斬",         11, C(0.6f,0.45f,0.8f)), 4, 50, 8, "敵の現在HPの10%を追加ダメージ。",
            Eff(CardEffectType.CurrentHpDmg, 10, 0)));
        cards.Add(TuneA(MakeAttack("michizure", "道連れ",         9,  C(0.5f,0.3f,0.35f)), 4, 150, 6, "強力だが自分にも40ダメージ。",
            Eff(CardEffectType.SelfDamage, 40, 0)));

        // ミニゲーム連動攻撃
        cards.Add(TuneA(MakeAttack("kaishin",   "会心の一撃",     9,  C(1f,0.75f,0.3f)), 4, 110, 3, "ゲージストップ発動！会心ゾーンで威力UP。",
            Eff(CardEffectType.GaugeOnUse, 0, 0)));
        cards.Add(TuneA(MakeAttack("keisanzan", "計算斬",         10, C(0.7f,0.9f,0.85f)), 4, 130, 5, "数字を小さい順にタップ！正答率で威力UP。",
            Eff(CardEffectType.TapOrderOnUse, 0, 0)));
        cards.Add(TuneA(MakeAttack("slotken",   "スロット剣",     8,  C(0.95f,0.55f,0.75f)), 3, 100, 4, "スロット発動！絵柄が揃えば威力UP。",
            Eff(CardEffectType.SlotOnUse, 0, 0)));

        // ---- 防御系 ----
        cards.Add(TuneS(MakeSkill("teppeki",   "鉄壁",           7,  C(0.5f,0.65f,0.9f), "ブロックを20得る。", 3,
            Eff(CardEffectType.Block, 20, 0)), 2));
        cards.Add(TuneS(MakeSkill("hansha",    "反射盾",         8,  C(0.55f,0.7f,0.95f), "次の被ダメージを50%軽減し、軽減分を敵に返す。", 5,
            Eff(CardEffectType.Reflect, 50, 0)), 3));
        cards.Add(TuneS(MakeSkill("kaihi",     "完全回避",       6,  C(0.7f,0.8f,1f), "次の敵の攻撃を完全無効化する。", 6,
            Eff(CardEffectType.Protect, 100, 1)), 4));
        cards.Add(TuneS(MakeSkill("ibara",     "茨の鎧",         7,  C(0.45f,0.6f,0.5f), "3ターンの間、被弾時に敵へ5反撃。", 4,
            Eff(CardEffectType.Thorns, 5, 3)), 2));
        cards.Add(TuneS(MakeSkill("saiseikamae","再生の構え",    8,  C(0.5f,0.75f,0.8f), "3ターンの間、毎ターンブロック5獲得。", 5,
            Eff(CardEffectType.BlockRegen, 5, 3)), 3));
        cards.Add(TuneS(MakeSkill("chouhatsu", "挑発",     5,  C(0.75f,0.6f,0.55f), "敵の次の行動を封じる。", 4,
            Eff(CardEffectType.Stun, 0, 1)), 2));
        cards.Add(TuneS(MakeSkill("tounokabe", "凍結の壁", 9,  C(0.6f,0.85f,1f), "ブロック10＋敵を1ターン凍結。", 7,
            Eff(CardEffectType.Block, 10, 0), Eff(CardEffectType.Stun, 0, 1)), 3));
        cards.Add(MakeSkill("chinotate", "血の盾",         6,  C(0.75f,0.3f,0.35f), "HPを10払ってブロック30を得る。", 4,
            Eff(CardEffectType.SelfDamage, 10, 0), Eff(CardEffectType.Block, 30, 0)));
        cards.Add(TuneS(MakeSkill("seichikai", "聖なる誓い",     8,  C(0.9f,0.9f,0.7f), "3ターンの間、被ダメージを25%軽減。", 5,
            Eff(CardEffectType.GuardTurns, 25, 3)), 3));
        cards.Add(TuneS(MakeSkill("counter",   "カウンター構え", 7,  C(0.8f,0.65f,0.5f), "次に被弾したとき敵へ100の反撃。", 6,
            Eff(CardEffectType.Counter, 100, 0)), 2));

        // ---- 回復系 ----
        cards.Add(TuneS(MakeSkill("regen",     "リジェネ",       6,  C(0.55f,0.95f,0.7f), "3ターンの間、毎ターンHP8回復。", 3,
            Eff(CardEffectType.Regen, 8, 3)), 2));
        cards.Add(TuneS(MakeSkill("kajouchiyu","過剰治癒",       7,  C(0.6f,1f,0.75f), "HP30回復。あふれた分はブロックに変換。", 5,
            Eff(CardEffectType.HealOverflowBlock, 30, 0)), 2));
        cards.Add(TuneS(MakeSkill("inori", "祈り",         4,  C(0.85f,0.95f,0.8f), "HP10回復＋次のターンのマナ+1。", 1,
            Eff(CardEffectType.Heal, 10, 0), Eff(CardEffectType.ManaBoostNextTurn, 1, 0)), 1));
        cards.Add(TuneS(MakeSkill("hanten",  "反転治療",   7,  C(0.5f,0.9f,0.65f), "失っているHPの40%を回復。", 6,
            Eff(CardEffectType.HealMissing, 40, 0)), 2));
        cards.Add(TuneS(MakeSkill("mamaai",  "母の愛",     10, C(1f,0.85f,0.9f), "HP全回復＋ブロック20。MAMAの切り札。", 12,
            Eff(CardEffectType.HealPercent, 100, 0), Eff(CardEffectType.Block, 20, 0)), 4));

        // ---- スキル系 ----
        cards.Add(TuneS(MakeSkill("juso",    "呪詛",       6,  C(0.6f,0.4f,0.7f), "毒5＋敵の攻撃力25%低下（2ターン）。", 5,
            Eff(CardEffectType.Poison, 5, 0), Eff(CardEffectType.Weak, 25, 2)), 2));
        cards.Add(TuneS(MakeSkill("kobu",    "鼓舞",       7,  C(1f,0.65f,0.45f), "攻撃力を6上げる（戦闘中持続）。", 7,
            Eff(CardEffectType.Strength, 6, 0)), 3));
        cards.Add(TuneS(MakeSkill("izumi",     "マナの泉",       5,  C(0.7f,0.6f,1f), "このターンのマナ+2。", 4,
            Eff(CardEffectType.ManaNow, 2, 0)), 2));
        cards.Add(MakeSkill("rensei",    "手札錬成",       5,  C(0.75f,0.85f,0.6f), "手札をランダムに1枚捨てて2枚引く。", 3,
            Eff(CardEffectType.RandomDiscardDraw, 2, 0)));
        cards.Add(MakeSkill("tokei",     "時間操作",       6,  C(0.65f,0.75f,0.95f), "手札をすべて捨てて引き直す。", 4,
            Eff(CardEffectType.RedrawAll, 0, 0)));
        cards.Add(TuneS(MakeSkill("kokuin",  "刻印",       6,  C(0.55f,0.5f,0.4f), "この戦闘中、毒・やけどダメージ+3。", 6,
            Eff(CardEffectType.AilmentAmp, 3, 0)), 2));
        cards.Add(TuneS(MakeSkill("kanpa",   "弱点看破",   7,  C(0.9f,0.8f,0.55f), "2ターンの間、敵の受けるダメージ+25%。", 5,
            Eff(CardEffectType.Vulnerable, 25, 2)), 2));
        cards.Add(MakeSkill("nusumi",    "盗み",           5,  C(0.8f,0.7f,0.35f), "お金を25入手する。", 2,
            Eff(CardEffectType.GainMoney, 25, 0)));
        cards.Add(TuneS(MakeSkill("kasoku",  "加速",       7,  C(0.6f,0.9f,0.95f), "次のターンの手札+2枚。", 5,
            Eff(CardEffectType.NextTurnExtraCards, 2, 0)), 2));
        cards.Add(TuneS(MakeSkill("dokunuma","毒沼",       7,  C(0.45f,0.7f,0.3f), "毒3を付与。すでに毒なら合計を2倍にする。", 6,
            Eff(CardEffectType.PoisonBoost, 3, 0)), 2));
        cards.Add(TuneS(MakeSkill("goukaen", "業火",       7,  C(1f,0.4f,0.15f), "やけど2を付与。すでにやけどなら合計を2倍にする。", 6,
            Eff(CardEffectType.BurnBoost, 2, 0)), 2));
        cards.Add(TuneS(MakeSkill("jigenbaku","時限爆弾",  8,  C(0.85f,0.5f,0.3f), "3ターン後に敵へ200の大ダメージ。", 7,
            Eff(CardEffectType.TimeBomb, 200, 3)), 3));

        // ==================== アンロック段階（0=最初から / 1=1回クリア後 / 2=2回クリア後） ====================
        // 初回プレイは分かりやすい基本カード群、クリアするごとに派手な/複雑なカードが解放される
        SetTier(cards, 1, "renzan","kyuketsu","kibaku","chargeken","dokuga","hiken","hyouken","raigeki2",
            "kaishin","slotken","teppeki","ibara","chouhatsu","chinotate","regen","inori","kobu","izumi",
            "rensei","nusumi","dokunuma","goukaen","juso");
        SetTier(cards, 2, "shokei","handou","banmenzan","lastresort","manabakuha","tefudadan","ooburi",
            "juryoku","michizure","keisanzan","hansha","kaihi","saiseikamae","tounokabe","seichikai","counter",
            "kajouchiyu","hanten","mamaai","tokei","kokuin","kanpa","kasoku","jigenbaku");

        // ==================== レアリティ（0=コモン / 1=アンコモン / 2=レア） ====================
        SetRarity(cards, 1, "thunder","sunshine","shingan","rekku","gouka","hyoga","raijin",
            "kyuketsu","shokei","kibaku","chargeken","banmenzan","tefudadan","hyouken","raigeki2",
            "twodraw","hansha","ibara","saiseikamae","tounokabe","seichikai","regen","kajouchiyu","hanten",
            "juso","kobu","izumi","kokuin","kanpa","kasoku","dokunuma","goukaen","slotken","kaishin");
        SetRarity(cards, 2, "amaterasu","kokuu","guren","soukyu","kannari","shuen",
            "handou","lastresort","manabakuha","ooburi","juryoku","michizure","keisanzan",
            "kaihi","counter","mamaai","tokei","jigenbaku");

        // ---- 敵定義 ----
        var enemies = new List<EnemyDef>();
        enemies.Add(MakeEnemy("slime", "スライム", EnemySpriteKey.Slime, 160, 7, 13, new Vector2(300,200), new Vector2(78,52), false, 0, 18,
            new[]{ EA("たいあたり",1.0f,1,45), EA("のしかかり",1.6f,1,20), EA("ぷるぷる連打",0.55f,2,15), EA("酸のしぶき",0.85f,1,12), EA("ようすを見ている",0f,0,8) }));
        enemies.Add(MakeEnemy("bat", "コウモリ", EnemySpriteKey.Bat, 120, 9, 15, new Vector2(310,160), new Vector2(80,42), true, 0, 22,
            new[]{ EA("ひっかき",1.0f,1,40), EA("急降下アタック",1.5f,1,18), EA("連続ひっかき",0.5f,3,20), EA("超音波",0.75f,1,14), EA("きりもみ突進",1.3f,1,8) }));
        enemies.Add(MakeEnemy("golem", "ゴーレム", EnemySpriteKey.Golem, 340, 11, 19, new Vector2(430,390), new Vector2(66,60), false, 0, 35,
            new[]{ EA("なぐりつけ",1.0f,1,40), EA("じしんふみつけ",1.8f,1,22), EA("岩石ラッシュ",0.65f,2,18), EA("岩とばし",0.85f,1,12), EAct("ちからをためている", EnemyActKind.Guard, 12, 14) }));
        // ---- 追加の雑魚敵（深度で出し分け。※スプライトは既存6種を流用・後で差し替え予定） ----
        enemies.Add(MakeEnemy("mushroom", "マイコニド", EnemySpriteKey.Slime, 140, 6, 11, new Vector2(300,210), new Vector2(76,54), false, 0, 20,
            new[]{ EA("たいあたり",1.0f,1,40), EA("胞子ばらまき",0.7f,1,25), EAct("毒の胞子", EnemyActKind.PoisonPlayer, 3, 22, 0.9f), EA("ようすを見ている",0f,0,15) }));
        enemies.Add(MakeEnemy("goblin", "ゴブリン", EnemySpriteKey.Knight, 175, 8, 14, new Vector2(320,300), new Vector2(78,72), false, 0, 24,
            new[]{ EA("こんぼう",1.0f,1,42), EA("だまし討ち",1.5f,1,22), EA("れんぞく突き",0.6f,2,24), EA("石を投げる",0.8f,1,12) }));
        enemies.Add(MakeEnemy("wolf", "ダイアウルフ", EnemySpriteKey.Bat, 150, 11, 17, new Vector2(320,220), new Vector2(84,58), false, 0, 26,
            new[]{ EA("かみつき",1.0f,1,40), EA("とびかかり",1.6f,1,22), EA("連続かみつき",0.55f,3,26), EAct("遠吠え", EnemyActKind.PowerUp, 3, 14) }));
        enemies.Add(MakeEnemy("ghost", "ゴースト", EnemySpriteKey.Bat, 120, 10, 16, new Vector2(300,240), new Vector2(74,60), true, 0, 28,
            new[]{ EA("たたり",1.0f,1,40), EA("ドレインタッチ",1.4f,1,25), EA("うらめしや",0.7f,2,20), EAct("消えている", EnemyActKind.Guard, 10, 16) }));
        enemies.Add(MakeEnemy("skeleton", "スケルトン", EnemySpriteKey.Knight, 190, 10, 16, new Vector2(320,320), new Vector2(80,76), false, 1, 30,
            new[]{ EA("さびた剣",1.0f,1,44), EA("骨投げ",0.85f,1,20), EA("たたき割り",1.7f,1,20), EA("がしゃがしゃ連撃",0.55f,2,16) }));
        enemies.Add(MakeEnemy("harpy", "ハーピー", EnemySpriteKey.Bat, 165, 11, 18, new Vector2(320,210), new Vector2(84,54), true, 1, 32,
            new[]{ EA("かぎ爪",1.0f,1,40), EA("急降下",1.6f,1,24), EA("風の刃",0.6f,3,24), EAct("かく乱の羽ばたき", EnemyActKind.PowerUp, 2, 13) }));
        enemies.Add(MakeEnemy("mudgolem", "マッドゴーレム", EnemySpriteKey.Golem, 330, 11, 18, new Vector2(420,380), new Vector2(70,62), false, 1, 36,
            new[]{ EA("泥のこぶし",1.0f,1,42), EA("押しつぶし",1.8f,1,22), EA("泥だまり",0.7f,2,20), EAct("固まっている", EnemyActKind.Guard, 14, 17) }));
        enemies.Add(MakeEnemy("lizardman", "リザードマン", EnemySpriteKey.Knight, 300, 13, 20, new Vector2(340,340), new Vector2(82,78), false, 2, 40,
            new[]{ EA("斬りかかり",1.0f,1,42), EA("尾撃",1.5f,1,24), EA("二刀連撃",0.6f,2,24), EAct("威嚇", EnemyActKind.PowerUp, 2, 11) }));
        enemies.Add(MakeEnemy("sandworm", "サンドワーム", EnemySpriteKey.Slime, 380, 14, 22, new Vector2(340,240), new Vector2(84,58), false, 3, 46,
            new[]{ EA("のみこみ",1.0f,1,40), EA("地中からの一撃",1.9f,1,24), EA("砂あらし",0.7f,2,22), EAct("もぐっている", EnemyActKind.Charge, 0, 16) }));
        enemies.Add(MakeEnemy("oni", "オニ", EnemySpriteKey.Oni, 680, 17, 25, new Vector2(460,530), new Vector2(92,106), false, 4, 80,
            new[]{ EA("かなぼう振り回し",1.0f,1,40), EA("地獄突き",1.9f,1,25), EA("鬼の連打",0.65f,3,20), EAct("雄叫びをあげている", EnemyActKind.PowerUp, 4, 16) }));

        // ---- ボス（Wave別・全3体） ----
        var boss1 = MakeEnemy("boss_goblinking", "ゴブリンキング", EnemySpriteKey.Knight, 520, 13, 20, new Vector2(430,420), new Vector2(104,104), false, 2, 180,
            new[]{ EA("王の一撃",1.0f,1,34), EA("大号令・連撃",0.7f,3,24), EA("兜割り",1.9f,1,22), EAct("咆哮で鼓舞", EnemyActKind.PowerUp, 4, 12) });
        enemies.Add(boss1);
        var boss2 = MakeEnemy("boss_guardian", "大守護像", EnemySpriteKey.Golem, 760, 15, 22, new Vector2(500,470), new Vector2(112,104), false, 5, 220,
            new[]{ EA("聖なる鉄槌",1.0f,1,34), EA("大地割り",2.0f,1,24), EA("光弾連射",0.6f,3,22), EAct("チャージ", EnemyActKind.Charge, 0, 14) });
        enemies.Add(boss2);
        var dragon = MakeEnemy("dragon", "エンシェントドラゴン", EnemySpriteKey.Dragon, 900, 16, 24, new Vector2(720,440), new Vector2(135,82), true, 9, 300,
            new[]{ EA("かみつき",1.0f,1,32), EA("灼熱のブレス",2.0f,1,24), EA("つばさの連撃",0.6f,3,20), EA("しっぽ振り回し",0.9f,2,14), EAct("天をあおいで咆哮した", EnemyActKind.PowerUp, 5, 12) });
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

    // マナ帯：マス数から一貫したコストを算出（5-8→3 / 9-12→4 / 13-16→5 / 17-20→6）
    // 基準：スラッシュ(5マス/威力60)=3マナ。威力はマス数に比例するためコストも段階的に上がる
    static int AttackMana(int size) => size <= 8 ? 3 : size <= 12 ? 4 : size <= 16 ? 5 : 6;

    static CardDef MakeAttack(string id, string name, int size, Color col)
    {
        var c = CreateOrReplace<CardDef>($"{CardsDir}/card_{id}.asset");
        c.id = id; c.displayName = name; c.kind = CardKind.Attack;
        c.shapeRows = Block(size); c.power = size * 12; c.color = col;
        c.manaCostOverride = AttackMana(size); c.effects = new CardEffect[0];
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

    // ギミック行動（防御/強化/毒攻撃/チャージ）
    static EnemyAttackDef EAct(string name, EnemyActKind act, int amount, int weight, float mult = 1f, int hits = 1)
        => new EnemyAttackDef { name = name, mult = mult, hits = hits, weight = weight, act = act, amount = amount };

    // 攻撃カードの調整（マナ・威力・深度・説明・効果を上書き）
    static CardDef TuneA(CardDef c, int mana, int power, int minDepth, string desc, params CardEffect[] effects)
    {
        c.manaCostOverride = mana; c.power = power; c.minDepth = minDepth; c.maxDepth = 0;
        c.description = desc; c.effects = effects;
        EditorUtility.SetDirty(c);
        return c;
    }

    // スキルカードのマナ調整
    static CardDef TuneS(CardDef c, int mana)
    {
        c.manaCostOverride = mana;
        EditorUtility.SetDirty(c);
        return c;
    }

    // 指定idのカードにアンロック段階を設定
    static void SetTier(System.Collections.Generic.List<CardDef> cards, int tier, params string[] ids)
    {
        var set = new System.Collections.Generic.HashSet<string>(ids);
        foreach (var c in cards)
            if (c != null && set.Contains(c.id)) { c.unlockTier = tier; EditorUtility.SetDirty(c); }
    }

    // 指定idのカードにレアリティを設定
    static void SetRarity(System.Collections.Generic.List<CardDef> cards, int rarity, params string[] ids)
    {
        var set = new System.Collections.Generic.HashSet<string>(ids);
        foreach (var c in cards)
            if (c != null && set.Contains(c.id)) { c.rarity = rarity; EditorUtility.SetDirty(c); }
    }

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
