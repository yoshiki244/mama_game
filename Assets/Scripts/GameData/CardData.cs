using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "Battle/Card Data")]
public class CardData : ScriptableObject
{
    public string cardName;
    [TextArea] public string description;
    public int power;
    public int masCount;        // マス数（威力の視覚的指標）
    public MasPattern masPattern; // マスの形状
    public CardType cardType;
}

public enum CardType
{
    Attack,
    Heal,
    Buff
}

[System.Serializable]
public class MasPattern
{
    // 最大5x5グリッドでマスのON/OFFを表現
    public bool[] grid = new bool[25]; // 5x5

    // 指定列数のパターンを自動生成するユーティリティ
    public static MasPattern Create(int count)
    {
        var p = new MasPattern();
        for (int i = 0; i < Mathf.Min(count, 25); i++)
            p.grid[i] = true;
        return p;
    }
}
