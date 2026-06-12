using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  画面フロー管理
//  スキルパネル画面 ⇄ 戦闘画面 の切り替えを担当
//
//  【シーン構成例】
//  Canvas
//   ├─ SkillPanelScreen   (SkillPanelManager を子に持つパネル)
//   └─ BattleScreen       (BattleUIManager を子に持つパネル)
// ─────────────────────────────────────────────
public class GameFlowManager : MonoBehaviour
{
    // ── Inspector 設定 ──────────────────────
    [Header("画面パネル")]
    public GameObject skillPanelScreen;
    public GameObject battleScreen;

    [Header("参照")]
    public BattleManager    battleManager;
    public SkillPanelManager skillPanelManager;

    [Header("ボタン")]
    public Button startBattleButton;        // 「戦闘開始」ボタン
    public Button returnToSkillPanelButton; // 「スキルパネルに戻る」ボタン

    // ─────────────────────────────────────────
    void Start()
    {
        startBattleButton?.onClick.AddListener(GoToBattle);
        returnToSkillPanelButton?.onClick.AddListener(GoToSkillPanel);

        // 最初はスキルパネル画面
        GoToSkillPanel();
    }

    // ─────────────────────────────────────────
    //  スキルパネル画面を表示
    // ─────────────────────────────────────────
    public void GoToSkillPanel()
    {
        skillPanelScreen?.SetActive(true);
        battleScreen?.SetActive(false);
    }

    // ─────────────────────────────────────────
    //  戦闘画面に切り替えて戦闘を開始
    // ─────────────────────────────────────────
    public void GoToBattle()
    {
        skillPanelScreen?.SetActive(false);
        battleScreen?.SetActive(true);

        battleManager?.StartBattle();
    }
}
