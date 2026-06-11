using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class CardUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI masCountText;
    public TextMeshProUGUI powerText;
    public Transform masGridParent;   // マスを並べる親オブジェクト（GridLayoutGroup推奨）
    public GameObject masCellPrefab;  // 1マス分のImageプレハブ

    [Header("Colors")]
    public Color masActiveColor = new Color(0.8f, 0.6f, 1f);   // 紫系
    public Color masInactiveColor = new Color(0.2f, 0.2f, 0.3f);
    public Color flashColor = new Color(1f, 1f, 0.4f);          // チャレンジで光る色（黄色）

    [Header("Interaction")]
    public Button cardButton;
    public Image cardBackground;
    public Color normalColor = new Color(0.15f, 0.12f, 0.22f, 1f);
    public Color hoverColor = new Color(0.25f, 0.2f, 0.38f, 1f);
    public Color disabledColor = new Color(0.08f, 0.08f, 0.12f, 1f);

    private CardData _data;
    private readonly List<Image> _cellImages = new List<Image>();

    // チャレンジで光らせられるマスの総数（グリッド全体）
    public int TotalCells => _cellImages.Count;

    public void Setup(CardData data)
    {
        _data = data;
        cardNameText.text = data.cardName;
        masCountText.text = $"{data.masCount}マス";
        powerText.text = $"威力 {data.power}";

        BuildMasGrid(data);

        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(OnCardClicked);

        // ホバー演出
        var trigger = GetComponent<UnityEngine.EventSystems.EventTrigger>();
        if (trigger != null)
        {
            trigger.triggers.Clear();
            AddPointerEvent(trigger, UnityEngine.EventSystems.EventTriggerType.PointerEnter, _ => SetHighlight(true));
            AddPointerEvent(trigger, UnityEngine.EventSystems.EventTriggerType.PointerExit, _ => SetHighlight(false));
        }
    }

    void BuildMasGrid(CardData data)
    {
        // 既存セルをクリア
        foreach (Transform child in masGridParent)
            Destroy(child.gameObject);
        _cellImages.Clear();

        // グリッドサイズを決定（5列固定）
        int cols = 5;
        int rows = Mathf.CeilToInt(data.masCount / (float)cols);
        rows = Mathf.Max(rows, 1);

        var grid = masGridParent.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraintCount = cols;
        }

        // マスを生成（masCountぶんだけON）
        for (int i = 0; i < rows * cols; i++)
        {
            var cell = Instantiate(masCellPrefab, masGridParent);
            var img = cell.GetComponent<Image>();
            if (img != null)
            {
                img.color = (i < data.masCount) ? masActiveColor : masInactiveColor;
                _cellImages.Add(img);
            }
        }
    }

    // ランダムな count 個のマスを duration 秒だけ光らせる
    public IEnumerator FlashCells(int count, float duration)
    {
        // インデックスをシャッフルして先頭 count 個を光らせる
        var indices = new List<int>();
        for (int i = 0; i < _cellImages.Count; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        var flashed = new List<Image>();
        var originalColors = new List<Color>();
        for (int i = 0; i < count && i < indices.Count; i++)
        {
            var img = _cellImages[indices[i]];
            flashed.Add(img);
            originalColors.Add(img.color);
            img.color = flashColor;
        }

        yield return new WaitForSeconds(duration);

        for (int i = 0; i < flashed.Count; i++)
            flashed[i].color = originalColors[i];
    }

    void OnCardClicked()
    {
        if (BattleManager.Instance.CurrentState != BattleState.PlayerTurn) return;
        BattleManager.Instance.PlayerSelectCard(_data, this);
    }

    public void SetInteractable(bool interactable)
    {
        cardButton.interactable = interactable;
        cardBackground.color = interactable ? normalColor : disabledColor;
    }

    void SetHighlight(bool on)
    {
        if (!cardButton.interactable) return;
        cardBackground.color = on ? hoverColor : normalColor;
    }

    void AddPointerEvent(UnityEngine.EventSystems.EventTrigger trigger,
        UnityEngine.EventSystems.EventTriggerType type,
        UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData> action)
    {
        var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }
}
