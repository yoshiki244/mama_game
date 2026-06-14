using UnityEngine;

// パーティメンバーの定義（最大3人。GDDの「味方スロット」構造の前段）
// 見た目はMAMAの髪色違いで生成する（プリプロ用）
public class PartyMember
{
    public string name;
    public Color hair, hairShadow, hairLight;
    public bool usePhoto; // trueなら実画像(Resources/mama_character)を使う

    public bool isLeader; // MAMA本人（元画像そのまま＝塗り替えしない）

    public Sprite BattleSprite()
    {
        if (!usePhoto) return ProtoPixelArt.Mama(hair, hairShadow, hairLight);
        return isLeader ? ProtoPixelArt.MamaPhoto() : ProtoPixelArt.MamaPhotoTinted(hair);
    }
    public Sprite MapSprite(int dir, int frame)
    {
        if (!usePhoto) return ProtoPixelArt.MapMama(dir, frame, hair, hairShadow);
        return isLeader ? ProtoPixelArt.MamaPhoto() : ProtoPixelArt.MamaPhotoTinted(hair);
    }
}

public static class ProtoParty
{
    // 加入順のロスター（1人目は主人公MAMA固定）
    public static readonly PartyMember[] Roster =
    {
        new PartyMember { name = "MAMA", usePhoto = true, isLeader = true,
            hair = new Color(0.87f, 0.88f, 0.95f), hairShadow = new Color(0.66f, 0.68f, 0.80f), hairLight = new Color(0.98f, 0.99f, 1f) },
        new PartyMember { name = "アカネ", usePhoto = true,
            hair = new Color(0.92f, 0.30f, 0.28f), hairShadow = new Color(0.68f, 0.20f, 0.18f), hairLight = new Color(1f, 0.55f, 0.50f) },
        new PartyMember { name = "ソラ", usePhoto = true,
            hair = new Color(0.40f, 0.55f, 0.95f), hairShadow = new Color(0.28f, 0.40f, 0.72f), hairLight = new Color(0.65f, 0.80f, 1f) },
    };

    public const int MaxMembers = 3;

    // キャラごとの盤面の形（どれも100マスちょうど）
    // 0: MAMA   = 10×10の正方形
    // 1: アカネ = 三角形（ピラミッド型。1+3+5+…+19=100）
    // 2: ソラ   = ひし形（鋭く広がる。横長18×10）
    public static bool[,] BoardMask(int memberIndex)
    {
        int[] widths;
        int maxW;
        switch (memberIndex)
        {
            case 1:  widths = new[] { 1, 3, 5, 7, 9, 11, 13, 15, 17, 19 };  maxW = 19; break; // 計100
            case 2:  widths = new[] { 2, 6, 10, 14, 18, 18, 14, 10, 6, 2 }; maxW = 18; break; // 計100
            default: widths = new[] { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10 }; maxW = 10; break;
        }

        var mask = new bool[maxW, widths.Length];
        for (int y = 0; y < widths.Length; y++)
        {
            int x0 = (maxW - widths[y]) / 2; // 各行を中央寄せ
            for (int x = 0; x < widths[y]; x++)
                mask[x0 + x, y] = true;
        }
        return mask;
    }
}
