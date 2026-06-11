using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("Characters")]
    public BattleCharacter player;
    public BattleCharacter enemy;

    [Header("Cards")]
    public List<CardData> playerCards = new List<CardData>();

    [Header("Critical Challenge")]
    public CriticalChallenge challenge;   // 入力チャレンジUI
    public float flashDuration = 0.6f;    // マスが光る時間（秒）
    public float answerTimeLimit = 5f;    // 回答制限時間（秒）

    [Header("Enemy Settings")]
    public int enemyMinDamage = 10;
    public int enemyMaxDamage = 25;
    public float enemyThinkTime = 1.2f;

    public BattleState CurrentState { get; private set; }

    public event Action<BattleState> OnStateChanged;
    public event Action<string> OnBattleMessage;
    public event Action<bool> OnBattleEnd; // true = player wins

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        SetState(BattleState.PlayerTurn);
        OnBattleMessage?.Invoke("あなたのターン！カードを選んで技を出そう！");
    }

    public void SetState(BattleState state)
    {
        CurrentState = state;
        OnStateChanged?.Invoke(state);
    }

    // プレイヤーがカードを選択したとき呼ぶ（CardUIから）
    public void PlayerSelectCard(CardData card, CardUI cardUI)
    {
        if (CurrentState != BattleState.PlayerTurn) return;

        SetState(BattleState.Challenge);
        StartCoroutine(ChallengeRoutine(card, cardUI));
    }

    // マスを光らせる → 数を回答 → 倍率を決めて攻撃
    IEnumerator ChallengeRoutine(CardData card, CardUI cardUI)
    {
        OnBattleMessage?.Invoke("マスが光る…！数を数えろ！");
        yield return new WaitForSeconds(0.8f);

        // 光る数はグリッド全体からランダムに決定
        int flashCount = UnityEngine.Random.Range(1, cardUI.TotalCells + 1);
        yield return cardUI.FlashCells(flashCount, flashDuration);

        // 入力チャレンジ開始
        yield return challenge.Run(answerTimeLimit);

        // 倍率を計算
        float multiplier;
        string resultMsg;
        if (challenge.Answer < 0)
        {
            multiplier = 1f;
            resultMsg = $"時間切れ…！正解は {flashCount} マスだった。通常攻撃！";
        }
        else
        {
            int diff = Mathf.Abs(challenge.Answer - flashCount);
            switch (diff)
            {
                case 0:
                    multiplier = 2f;
                    resultMsg = $"ジャスト！{flashCount} マス！クリティカル！！（威力200%）";
                    break;
                case 1:
                    multiplier = 1.5f;
                    resultMsg = $"おしい！正解は {flashCount} マス。準クリティカル！（威力150%）";
                    break;
                case 2:
                    multiplier = 1.2f;
                    resultMsg = $"正解は {flashCount} マス。少し威力アップ！（威力120%）";
                    break;
                default:
                    multiplier = 1f;
                    resultMsg = $"はずれ…正解は {flashCount} マスだった。通常攻撃！";
                    break;
            }
        }

        OnBattleMessage?.Invoke(resultMsg);
        yield return new WaitForSeconds(1.2f);

        SetState(BattleState.Executing);
        yield return ExecutePlayerCard(card, multiplier);
    }

    IEnumerator ExecutePlayerCard(CardData card, float multiplier)
    {
        int finalPower = Mathf.RoundToInt(card.power * multiplier);

        string actionMsg = card.cardType == CardType.Heal
            ? $"MAMAは「{card.cardName}」を使った！"
            : $"MAMAは「{card.cardName}」を放った！";
        OnBattleMessage?.Invoke(actionMsg);

        yield return new WaitForSeconds(0.8f);

        if (card.cardType == CardType.Heal)
        {
            player.Heal(finalPower);
            OnBattleMessage?.Invoke($"HPが {finalPower} 回復した！");
        }
        else
        {
            enemy.TakeDamage(finalPower);
            string critTag = multiplier >= 2f ? "クリティカル！" : "";
            OnBattleMessage?.Invoke($"{critTag}スライムに {finalPower} のダメージ！");
        }

        yield return new WaitForSeconds(0.8f);

        if (enemy.IsDead)
        {
            OnBattleMessage?.Invoke("スライムを倒した！勝利！");
            SetState(BattleState.Result);
            yield return new WaitForSeconds(1f);
            OnBattleEnd?.Invoke(true);
            yield break;
        }

        SetState(BattleState.EnemyTurn);
        OnBattleMessage?.Invoke("スライムのターン…");
        yield return new WaitForSeconds(enemyThinkTime);
        StartCoroutine(ExecuteEnemyTurn());
    }

    IEnumerator ExecuteEnemyTurn()
    {
        int damage = UnityEngine.Random.Range(enemyMinDamage, enemyMaxDamage + 1);
        OnBattleMessage?.Invoke($"スライムの攻撃！");

        yield return new WaitForSeconds(0.6f);

        player.TakeDamage(damage);
        OnBattleMessage?.Invoke($"MAMAは {damage} のダメージを受けた！");

        yield return new WaitForSeconds(0.8f);

        if (player.IsDead)
        {
            OnBattleMessage?.Invoke("MAMAは倒れてしまった…");
            SetState(BattleState.Result);
            yield return new WaitForSeconds(1f);
            OnBattleEnd?.Invoke(false);
            yield break;
        }

        SetState(BattleState.PlayerTurn);
        OnBattleMessage?.Invoke("あなたのターン！カードを選んで技を出そう！");
    }

    public void RestartBattle()
    {
        player.ResetHP();
        enemy.ResetHP();
        SetState(BattleState.PlayerTurn);
        OnBattleMessage?.Invoke("あなたのターン！カードを選んで技を出そう！");
    }
}

public enum BattleState
{
    PlayerTurn,
    Challenge,
    Executing,
    EnemyTurn,
    Result
}
