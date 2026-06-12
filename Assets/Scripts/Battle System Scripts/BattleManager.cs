using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  戦闘の状態
// ─────────────────────────────────────────────
public enum BattlePhase
{
    DrawCards,      // カードを配る
    PlayerSelect,   // プレイヤーがカードを選ぶ
    PlayerAction,   // 選んだスキルを実行
    EnemyAction,    // 敵の行動
    TurnEnd,        // ターン終了処理
    Victory,        // 勝利
    Defeat,         // 敗北
}

// ─────────────────────────────────────────────
//  BattleManager
//  SkillPanelManager の確率マップを使って
//  3枚ドロー → 選択 → 実行のループを管理
// ─────────────────────────────────────────────
public class BattleManager : MonoBehaviour
{
    // ── シングルトン ────────────────────────
    public static BattleManager Instance { get; private set; }

    // ── Inspector 設定 ──────────────────────
    [Header("参照")]
    public SkillPanelManager skillPanelManager;
    public BattleUIManager   battleUI;

    [Header("デフォルトスキル（空白マス用）")]
    public SkillData normalAttackSkill;     // 通常攻撃
    public SkillData weakAttackSkill;       // 弱攻撃

    [Header("パーティ設定")]
    public CharacterStats playerStats  = new CharacterStats();
    public CharacterStats mamaStats    = new CharacterStats();
    public CharacterStats enemyStats   = new CharacterStats();

    [Header("手札枚数")]
    [Range(2, 5)] public int drawCount = 3;

    // ── 内部状態 ─────────────────────────────
    public BattlePhase CurrentPhase { get; private set; }
    public List<SkillData> HandCards { get; private set; } = new List<SkillData>();
    private Dictionary<SkillData, float> _probMap;

    // ─────────────────────────────────────────
    //  Unity ライフサイクル
    // ─────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─────────────────────────────────────────
    //  戦闘開始（UIManager から呼ぶ）
    // ─────────────────────────────────────────
    public void StartBattle()
    {
        playerStats.Initialize();
        mamaStats.Initialize();
        enemyStats.Initialize();

        // 確率マップを盤面から生成
        _probMap = skillPanelManager.BuildProbabilityMap();

        // 空白マス分を通常攻撃として追加
        float emptyRatio = skillPanelManager.EmptyCellRatio();
        if (emptyRatio > 0f && normalAttackSkill != null)
        {
            if (_probMap.ContainsKey(normalAttackSkill))
                _probMap[normalAttackSkill] += emptyRatio;
            else
                _probMap[normalAttackSkill] = emptyRatio;
        }

        battleUI?.RefreshStatus(playerStats, mamaStats, enemyStats);
        TransitionTo(BattlePhase.DrawCards);
    }

    // ─────────────────────────────────────────
    //  フェーズ遷移
    // ─────────────────────────────────────────
    private void TransitionTo(BattlePhase next)
    {
        CurrentPhase = next;
        switch (next)
        {
            case BattlePhase.DrawCards:    HandleDrawCards();   break;
            case BattlePhase.PlayerSelect: battleUI?.ShowHand(HandCards); break;
            case BattlePhase.TurnEnd:      StartCoroutine(HandleTurnEnd()); break;
            case BattlePhase.Victory:      battleUI?.ShowResult(true);  break;
            case BattlePhase.Defeat:       battleUI?.ShowResult(false); break;
        }
    }

    // ─────────────────────────────────────────
    //  カードのドロー（確率サンプリング）
    // ─────────────────────────────────────────
    private void HandleDrawCards()
    {
        HandCards.Clear();
        for (int i = 0; i < drawCount; i++)
            HandCards.Add(SampleSkill());

        TransitionTo(BattlePhase.PlayerSelect);
    }

    /// <summary>確率マップに従いスキルを1枚選ぶ</summary>
    private SkillData SampleSkill()
    {
        float roll = Random.value;  // [0, 1)
        float cumulative = 0f;

        foreach (var kv in _probMap)
        {
            cumulative += kv.Value;
            if (roll < cumulative)
                return kv.Key;
        }

        // フォールバック：通常攻撃 or 最初のスキル
        return normalAttackSkill ?? (_probMap.Count > 0 ? new List<SkillData>(_probMap.Keys)[0] : null);
    }

    // ─────────────────────────────────────────
    //  プレイヤーがカードを選んだ（UIから呼ぶ）
    // ─────────────────────────────────────────
    public void OnCardSelected(SkillData skill)
    {
        if (CurrentPhase != BattlePhase.PlayerSelect) return;
        TransitionTo(BattlePhase.PlayerAction);
        StartCoroutine(ExecutePlayerAction(skill));
    }

    // ─────────────────────────────────────────
    //  プレイヤーアクション実行
    // ─────────────────────────────────────────
    private IEnumerator ExecutePlayerAction(SkillData skill)
    {
        battleUI?.ShowActionLog($"プレイヤーが「{skill.skillName}」を使った！");
        yield return new WaitForSeconds(0.5f);

        // MP チェック
        if (playerStats.currentMP < skill.mpCost)
        {
            battleUI?.ShowActionLog("MPが足りない！通常攻撃に切り替え");
            skill = normalAttackSkill;
        }

        playerStats.UseMp(skill.mpCost);
        ApplySkillEffect(skill, playerStats, enemyStats);

        battleUI?.RefreshStatus(playerStats, mamaStats, enemyStats);
        yield return new WaitForSeconds(0.8f);

        // 敵が死んでいれば勝利
        if (!enemyStats.IsAlive)
        {
            TransitionTo(BattlePhase.Victory);
            yield break;
        }

        // ── ママの行動（ヒーラー固定）──────────
        battleUI?.ShowActionLog($"{mamaStats.characterName}がヒールしてくれた！");
        yield return new WaitForSeconds(0.5f);
        int healAmt = Mathf.RoundToInt(20 * (1 + mamaStats.ATK * 0.05f));
        playerStats.Heal(healAmt);
        battleUI?.RefreshStatus(playerStats, mamaStats, enemyStats);
        yield return new WaitForSeconds(0.5f);

        TransitionTo(BattlePhase.EnemyAction);
        StartCoroutine(ExecuteEnemyAction());
    }

    // ─────────────────────────────────────────
    //  敵の行動
    // ─────────────────────────────────────────
    private IEnumerator ExecuteEnemyAction()
    {
        battleUI?.ShowActionLog($"敵の攻撃！");
        yield return new WaitForSeconds(0.5f);

        int dmg = Mathf.Max(1, enemyStats.ATK);
        playerStats.TakeDamage(dmg);
        battleUI?.RefreshStatus(playerStats, mamaStats, enemyStats);
        yield return new WaitForSeconds(0.5f);

        if (!playerStats.IsAlive)
        {
            TransitionTo(BattlePhase.Defeat);
            yield break;
        }

        TransitionTo(BattlePhase.TurnEnd);
    }

    // ─────────────────────────────────────────
    //  ターン終了処理
    // ─────────────────────────────────────────
    private IEnumerator HandleTurnEnd()
    {
        playerStats.TickModifiers();
        mamaStats.TickModifiers();
        enemyStats.TickModifiers();
        yield return new WaitForSeconds(0.3f);
        TransitionTo(BattlePhase.DrawCards);
    }

    // ─────────────────────────────────────────
    //  スキル効果の適用
    // ─────────────────────────────────────────
    private void ApplySkillEffect(SkillData skill, CharacterStats user, CharacterStats target)
    {
        switch (skill.skillType)
        {
            case SkillType.NormalAttack:
            case SkillType.WeakAttack:
            case SkillType.HeavyAttack:
                int dmg = Mathf.RoundToInt(skill.power * skill.powerMultiplier + user.ATK);
                target.TakeDamage(dmg);
                battleUI?.ShowActionLog($"  → {dmg} ダメージ！");
                break;

            case SkillType.Heal:
                int heal = Mathf.RoundToInt(skill.power * skill.powerMultiplier);
                user.Heal(heal);
                battleUI?.ShowActionLog($"  → HP {heal} 回復！");
                break;

            case SkillType.Buff:
            case SkillType.Debuff:
                foreach (var mod in skill.modifiers)
                {
                    var applyTarget = skill.skillType == SkillType.Buff ? user : target;
                    applyTarget.ApplyModifier(mod);
                }
                battleUI?.ShowActionLog($"  → ステータスが変化した！");
                break;
        }
    }
}
