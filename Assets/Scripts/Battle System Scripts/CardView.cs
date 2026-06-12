using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  1枚のカードUIを制御するコンポーネント
//  CardPrefab にアタッチする
//
//  【プレハブ構成例】
//  CardRoot (Button + Image + CardView)
//   ├─ IconImage  (Image)
//   ├─ NameText   (TextMeshProUGUI)
//   ├─ PowerText  (TextMeshProUGUI)
//   └─ TypeText   (TextMeshProUGUI)
// ─────────────────────────────────────────────
public class CardView : MonoBehaviour
{
    [Header("UI パーツ")]
    public Image           iconImage;
    public TMP_Text        nameText;
    public TMP_Text        powerText;
    public TMP_Text        typeText;
    public Image           cardBackground;

    // スキルタイプ別の背景色
    private static readonly Color[] TypeColors =
    {
        new Color(0.75f, 0.75f, 0.75f), // NormalAttack   — グレー
        new Color(0.90f, 0.90f, 0.50f), // WeakAttack     — 黄
        new Color(1.00f, 0.35f, 0.35f), // HeavyAttack    — 赤
        new Color(0.35f, 0.90f, 0.55f), // Heal           — 緑
        new Color(0.35f, 0.65f, 1.00f), // Buff           — 青
        new Color(0.70f, 0.35f, 1.00f), // Debuff         — 紫
    };

    private static readonly string[] TypeLabels =
    {
        "通常攻撃", "弱攻撃", "大技", "回復", "バフ", "デバフ"
    };

    // ─────────────────────────────────────────
    //  カードの内容を設定する
    // ─────────────────────────────────────────
    public void Setup(SkillData skill)
    {
        if (skill == null) return;

        // アイコン
        if (iconImage != null)
        {
            iconImage.gameObject.SetActive(skill.icon != null);
            if (skill.icon != null) iconImage.sprite = skill.icon;
        }

        // テキスト
        if (nameText  != null) nameText.text  = skill.skillName;
        if (powerText != null) powerText.text = skill.power > 0 ? $"威力 {skill.power}" : "";
        if (typeText  != null) typeText.text  = GetTypeLabel(skill.skillType);

        // 背景色
        if (cardBackground != null)
        {
            int idx = (int)skill.skillType;
            cardBackground.color = idx < TypeColors.Length ? TypeColors[idx] : Color.white;
        }
    }

    // ─────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────
    private string GetTypeLabel(SkillType type)
    {
        int idx = (int)type;
        return idx < TypeLabels.Length ? TypeLabels[idx] : type.ToString();
    }
}
