using System.Collections.Generic;

// 装備（1つだけ所持可能。初期はなし）。
// ※保存値を保つため末尾に追加すること
public enum EquipKind
{
    None, LifePendant, ManaPendant, GuardPendant,
    GravityPendant,   // 戦闘開始時に敵HP-5%
    AilmentPendant,   // 敵の状態異常ダメージ2倍
    HandPendant,      // 手札6枚
    GaneshaPendant,   // 入手コイン2倍
    CellPendant,      // 入手ストックマス2倍
}

public static class EquipInfo
{
    // ショップ価格
    public const int ShopPrice = 80;

    // 購入・ドロップ対象の装備一覧
    public static readonly EquipKind[] All =
    {
        EquipKind.LifePendant, EquipKind.ManaPendant, EquipKind.GuardPendant,
        EquipKind.GravityPendant, EquipKind.AilmentPendant, EquipKind.HandPendant,
        EquipKind.GaneshaPendant, EquipKind.CellPendant,
    };

    public static string Name(EquipKind k)
    {
        switch (k)
        {
            case EquipKind.LifePendant: return "生命のペンダント";
            case EquipKind.ManaPendant: return "マナのペンダント";
            case EquipKind.GuardPendant: return "加護のペンダント";
            case EquipKind.GravityPendant: return "グラヴィティペンダント";
            case EquipKind.AilmentPendant: return "状態異常のペンダント";
            case EquipKind.HandPendant: return "手札増強のペンダント";
            case EquipKind.GaneshaPendant: return "ガネーシャのペンダント";
            case EquipKind.CellPendant: return "マス増強のペンダント";
            default: return "なし";
        }
    }

    public static string Desc(EquipKind k)
    {
        switch (k)
        {
            case EquipKind.LifePendant: return "HP10％増加";
            case EquipKind.ManaPendant: return "マナの最大値＋1";
            case EquipKind.GuardPendant: return "被ダメージを5％軽減";
            case EquipKind.GravityPendant: return "戦闘開始時に敵HP5％減少";
            case EquipKind.AilmentPendant: return "敵の状態異常ダメージ2倍";
            case EquipKind.HandPendant: return "手札の枚数が6枚になる";
            case EquipKind.GaneshaPendant: return "入手コインが2倍";
            case EquipKind.CellPendant: return "入手ストックマスが2倍";
            default: return "";
        }
    }
}
