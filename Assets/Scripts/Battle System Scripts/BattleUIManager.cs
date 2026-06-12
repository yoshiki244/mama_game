using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  戦闘UIの管理
//  ・手札カードの表示と選択
//  ・HP/MPゲージの更新
//  ・アクションログ
//  ・勝敗画面
// ─────────────────────────────────────────────
public class BattleUIManager : MonoBehaviour
{
    // ── Inspector 設定 ──────────────────────
    [Header("手札エリア")]
    public Transform      handParent;       // 手札カードの親
    public GameObject     cardPrefab;       // カードのプレハブ

    [Header("ステータス表示")]
    public Slider  playerHpBar;
    public Slider  playerMpBar;
    public TMP_Text playerHpText;
    public TMP_Text playerMpText;

    public Slider  mamaHpBar;
    public TMP_Text mamaHpText;

    public Slider  enemyHpBar;
    public TMP_Text enemyHpText;

    [Header("ログ")]
    public TMP_Text actionLogText;

    [Header("結果パネル")]
    public GameObject resultPanel;
    public TMP_Text   resultText;
    public Button     returnToSkillPanelButton;

    // ─────────────────────────────────────────
    //  手札の表示
    // ─────────────────────────────────────────
    public void ShowHand(List<SkillData> cards)
    {
        // 既存のカードを削除
        foreach (Transform child in handParent)
            Destroy(child.gameObject);

        foreach (var skill in cards)
        {
            var cardObj = Instantiate(cardPrefab, handParent);
            var card    = cardObj.GetComponent<CardView>();
            card?.Setup(skill);

            // カード選択時に BattleManager へ通知
            SkillData s = skill;
            cardObj.GetComponent<Button>()?.onClick.AddListener(() =>
            {
                BattleManager.Instance.OnCardSelected(s);
                // 選択後は手札を非表示
                foreach (Transform c in handParent)
                    Destroy(c.gameObject);
            });
        }
    }

    // ─────────────────────────────────────────
    //  ステータスバー更新
    // ─────────────────────────────────────────
    public void RefreshStatus(CharacterStats player, CharacterStats mama, CharacterStats enemy)
    {
        SetBar(playerHpBar, playerHpText, player.currentHP, player.maxHP, "HP");
        SetBar(playerMpBar, playerMpText, player.currentMP, player.maxMP, "MP");
        SetBar(mamaHpBar,   mamaHpText,   mama.currentHP,   mama.maxHP,   "HP");
        SetBar(enemyHpBar,  enemyHpText,  enemy.currentHP,  enemy.maxHP,  "HP");
    }

    private void SetBar(Slider bar, TMP_Text label, int current, int max, string prefix)
    {
        if (bar   != null) { bar.maxValue = max; bar.value = current; }
        if (label != null) label.text = $"{prefix} {current}/{max}";
    }

    // ─────────────────────────────────────────
    //  アクションログ
    // ─────────────────────────────────────────
    public void ShowActionLog(string message)
    {
        if (actionLogText == null) return;
        actionLogText.text += message + "\n";

        // 長くなりすぎたら先頭を切る（最新10行を保持）
        var lines = actionLogText.text.Split('\n');
        if (lines.Length > 11)
            actionLogText.text = string.Join("\n", lines, lines.Length - 11, 10);
    }

    // ─────────────────────────────────────────
    //  勝敗画面
    // ─────────────────────────────────────────
    public void ShowResult(bool isVictory)
    {
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText  != null)
            resultText.text = isVictory ? "勝利！" : "敗北…";
    }
}
