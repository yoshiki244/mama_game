using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class BattleHUD : MonoBehaviour
{
    [Header("Player HUD")]
    public Slider playerHPBar;
    public TextMeshProUGUI playerHPText;
    public TextMeshProUGUI playerNameText;

    [Header("Enemy HUD")]
    public Slider enemyHPBar;
    public TextMeshProUGUI enemyHPText;
    public TextMeshProUGUI enemyNameText;

    [Header("Message")]
    public TextMeshProUGUI messageText;

    [Header("Cards")]
    public List<CardUI> cardUIs; // Inspector で3枚割り当て

    [Header("Result Panel")]
    public GameObject resultPanel;
    public TextMeshProUGUI resultText;
    public Button retryButton;

    void Start()
    {
        var bm = BattleManager.Instance;

        // カードデータをカードUIにセット
        for (int i = 0; i < cardUIs.Count && i < bm.playerCards.Count; i++)
            cardUIs[i].Setup(bm.playerCards[i]);

        // HP変化を購読
        bm.player.OnHPChanged += (cur, max) => UpdateHP(playerHPBar, playerHPText, cur, max);
        bm.enemy.OnHPChanged += (cur, max) => UpdateHP(enemyHPBar, enemyHPText, cur, max);

        // 初期HP表示
        UpdateHP(playerHPBar, playerHPText, bm.player.currentHP, bm.player.maxHP);
        UpdateHP(enemyHPBar, enemyHPText, bm.enemy.currentHP, bm.enemy.maxHP);
        playerNameText.text = bm.player.characterName;
        enemyNameText.text = bm.enemy.characterName;

        // バトル状態変化
        bm.OnStateChanged += OnStateChanged;
        bm.OnBattleMessage += msg => messageText.text = msg;
        bm.OnBattleEnd += ShowResult;

        resultPanel.SetActive(false);
        retryButton.onClick.AddListener(() =>
        {
            resultPanel.SetActive(false);
            bm.RestartBattle();
        });
    }

    void UpdateHP(Slider bar, TextMeshProUGUI label, int cur, int max)
    {
        bar.maxValue = max;
        bar.value = cur;
        label.text = $"{cur}/{max}";
    }

    void OnStateChanged(BattleState state)
    {
        bool playerTurn = state == BattleState.PlayerTurn;
        foreach (var card in cardUIs)
            card.SetInteractable(playerTurn);
    }

    void ShowResult(bool playerWon)
    {
        resultPanel.SetActive(true);
        resultText.text = playerWon ? "勝利！" : "敗北…";
    }
}
