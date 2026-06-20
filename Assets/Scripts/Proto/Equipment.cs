using System.Collections.Generic;

// 装備（1つだけ所持可能。初期はなし）。
// ※保存値を保つため末尾に追加すること（None=0, LifePendant=1, ManaPendant=2, GuardPendant=3）
public enum EquipKind { None, LifePendant, ManaPendant, GuardPendant }

public static class EquipInfo
{
    // ショップ価格
    public const int ShopPrice = 80;

    // 購入・ドロップ対象の装備一覧
    public static readonly EquipKind[] All = { EquipKind.LifePendant, EquipKind.ManaPendant, EquipKind.GuardPendant };

    public static string Name(EquipKind k)
    {
        switch (k)
        {
            case EquipKind.LifePendant: return "生命のペンダント";
            case EquipKind.ManaPendant: return "マナのペンダント";
            case EquipKind.GuardPendant: return "加護のペンダント";
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
            default: return "";
        }
    }
}
