using System.Collections.Generic;
using UnityEngine;

// ラン間で永続するメタ進行（PlayerPrefs）。クリア回数・アンロック・アセンション・ベストスコア・シード。
public static class ProtoUnlocks
{
    const string KClears = "meta_clears";
    const string KAsc = "meta_asc";          // 現在選択中のアセンション
    const string KMaxAsc = "meta_maxasc";    // 解放済み最大アセンション
    const string KBest = "meta_best";
    const string KTotal = "meta_totalscore"; // 累計スコア（敗北でも加算＝挫折防止のアンロック源）

    public const int MaxAscension = 4;   // EASY〜MASTER の5段階（0〜4）

    // 難易度名（アセンション値→表示名）
    public static string AscName(int a) =>
        a <= 0 ? "EASY" : a == 1 ? "NORMAL" : a == 2 ? "HARD" : a == 3 ? "VERY HARD" : "MASTER";

    // クリア回数（カード/装備のアンロック段階に使う）
    public static int Clears { get => PlayerPrefs.GetInt(KClears, 0); set { PlayerPrefs.SetInt(KClears, value); PlayerPrefs.Save(); } }

    // 選択中アセンション（0〜解放済み）
    public static int Ascension
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(KAsc, 0), 0, MaxAscUnlocked);
        set { PlayerPrefs.SetInt(KAsc, Mathf.Clamp(value, 0, MaxAscUnlocked)); PlayerPrefs.Save(); }
    }

    // デフォルトで HARD(2) まで選択可。クリアで VERY HARD(3)、VERY HARDクリアで MASTER(4) 解禁
    public static int MaxAscUnlocked { get => Mathf.Clamp(PlayerPrefs.GetInt(KMaxAsc, 2), 2, MaxAscension); set { PlayerPrefs.SetInt(KMaxAsc, Mathf.Clamp(value, 2, MaxAscension)); PlayerPrefs.Save(); } }

    public static int BestScore { get => PlayerPrefs.GetInt(KBest, 0); private set { PlayerPrefs.SetInt(KBest, value); PlayerPrefs.Save(); } }

    // 累計スコア（クリア・敗北を問わず加算される）
    public static int TotalScore { get => PlayerPrefs.GetInt(KTotal, 0); private set { PlayerPrefs.SetInt(KTotal, value); PlayerPrefs.Save(); } }
    public static void AddRunScore(int score) { if (score > 0) TotalScore = TotalScore + score; }

    // クリア時に呼ぶ。難易度を解禁し、ベスト/累計スコアを更新
    //   初クリア（難易度不問）→ VERY HARD 解禁 ／ VERY HARD以上をクリア → MASTER 解禁
    public static void OnClear(int ascension, int score)
    {
        Clears = Clears + 1;
        if (MaxAscUnlocked < 3) MaxAscUnlocked = 3;
        if (ascension >= 3 && MaxAscUnlocked < 4) MaxAscUnlocked = 4;
        if (score > BestScore) BestScore = score;
        AddRunScore(score);
    }

    // ---- 図鑑の発見記録（一度入手したカードは「最初から」でも図鑑に残る） ----
    const string KDisc = "meta_discovered";
    static HashSet<string> _disc;
    static HashSet<string> Disc
    {
        get
        {
            if (_disc == null)
                _disc = new HashSet<string>(PlayerPrefs.GetString(KDisc, "")
                    .Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            return _disc;
        }
    }
    public static bool IsDiscovered(string id) => !string.IsNullOrEmpty(id) && Disc.Contains(id);
    public static void MarkDiscovered(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Disc.Add(id)) { PlayerPrefs.SetString(KDisc, string.Join(",", Disc)); PlayerPrefs.Save(); }
    }

    // 図鑑の発見記録をリセット（「最初から」で呼ばれる）
    public static void ClearDiscovered()
    {
        _disc = new HashSet<string>();
        PlayerPrefs.SetString(KDisc, "");
        PlayerPrefs.Save();
    }

    // アンロック段階：クリア回数 または 累計スコア（tier×2000）のどちらかで解放
    // → 敗北が続いても遊ぶほどカードが増える（挫折防止）
    public static int UnlockLevel => Mathf.Max(Clears, TotalScore / 2000);
    public static bool CardTierUnlocked(int tier) => tier <= UnlockLevel;
}
