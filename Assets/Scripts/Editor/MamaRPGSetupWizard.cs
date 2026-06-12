// このファイルは Assets/Scripts/Editor/ フォルダに置いてください
// （Editor フォルダが無ければ作成する）

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  MamaRPG セットアップウィザード
//  Unity メニュー > MamaRPG > から実行できます
// ─────────────────────────────────────────────
public class MamaRPGSetupWizard : EditorWindow
{
    [MenuItem("MamaRPG/① Prefabを自動生成する")]
    public static void CreatePrefabs()
    {
        // 保存先フォルダを作成
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        CreateCellPrefab();
        CreateCardPrefab();
        CreatePieceButtonPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完了", "Prefabを Assets/Prefabs/UI/ に生成しました！\n\nSkillPanelManager の Inspector に\n  ・CellPrefab\n  ・PieceButtonPrefab\nをドラッグ&ドロップしてください。", "OK");
    }

    // ─────────────────────────────────────────
    //  CellPrefab
    // ─────────────────────────────────────────
    static void CreateCellPrefab()
    {
        const string path = "Assets/Prefabs/UI/CellPrefab.prefab";

        // ルート
        var root = new GameObject("CellPrefab");
        root.AddComponent<RectTransform>().sizeDelta = new Vector2(48, 48);

        // 背景Image
        var img = root.AddComponent<Image>();
        img.color = Color.white;

        // Button
        var btn = root.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.8f, 0.9f, 1f);
        colors.pressedColor = new Color(0.6f, 0.8f, 1f);
        btn.colors = colors;

        // ラベル（スキル頭文字）
        var textObj = new GameObject("Label");
        textObj.transform.SetParent(root.transform, false);
        var rect = textObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black;

        SavePrefab(root, path);
    }

    // ─────────────────────────────────────────
    //  CardPrefab
    // ─────────────────────────────────────────
    static void CreateCardPrefab()
    {
        const string path = "Assets/Prefabs/UI/CardPrefab.prefab";

        var root = new GameObject("CardPrefab");
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(110, 160);

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.95f, 0.95f, 0.95f);

        var btn = root.AddComponent<Button>();
        var cardView = root.AddComponent<CardView>();

        // 背景への参照
        cardView.cardBackground = bg;

        // ── アイコン ──
        var iconObj = CreateChild(root, "IconImage", new Vector2(90, 80), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        iconObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -10);
        var iconImg = iconObj.AddComponent<Image>();
        iconImg.color = new Color(0.8f, 0.8f, 0.8f);
        cardView.iconImage = iconImg;

        // ── スキル名 ──
        var nameObj = CreateChild(root, "NameText", new Vector2(100, 24), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        nameObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 50);
        var nameTmp = nameObj.AddComponent<TextMeshProUGUI>();
        nameTmp.text = "スキル名";
        nameTmp.fontSize = 13;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color = Color.black;
        cardView.nameText = nameTmp;

        // ── 威力テキスト ──
        var powerObj = CreateChild(root, "PowerText", new Vector2(100, 20), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        powerObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 28);
        var powerTmp = powerObj.AddComponent<TextMeshProUGUI>();
        powerTmp.text = "威力 10";
        powerTmp.fontSize = 11;
        powerTmp.alignment = TextAlignmentOptions.Center;
        powerTmp.color = Color.black;
        cardView.powerText = powerTmp;

        // ── タイプテキスト ──
        var typeObj = CreateChild(root, "TypeText", new Vector2(100, 20), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        typeObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 10);
        var typeTmp = typeObj.AddComponent<TextMeshProUGUI>();
        typeTmp.text = "通常攻撃";
        typeTmp.fontSize = 11;
        typeTmp.alignment = TextAlignmentOptions.Center;
        typeTmp.color = Color.black;
        cardView.typeText = typeTmp;

        SavePrefab(root, path);
    }

    // ─────────────────────────────────────────
    //  PieceButtonPrefab
    // ─────────────────────────────────────────
    static void CreatePieceButtonPrefab()
    {
        const string path = "Assets/Prefabs/UI/PieceButtonPrefab.prefab";

        var root = new GameObject("PieceButtonPrefab");
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(160, 40);

        var img = root.AddComponent<Image>();
        img.color = new Color(0.85f, 0.85f, 0.9f);

        root.AddComponent<Button>();

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(root.transform, false);
        var rect = textObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 0);
        rect.offsetMax = new Vector2(-8, 0);

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "スキル名";
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.black;

        SavePrefab(root, path);
    }

    // ─────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────
    static GameObject CreateChild(GameObject parent, string name, Vector2 size,
                                   Vector2 anchorMin, Vector2 anchorMax)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        return obj;
    }

    static void SavePrefab(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        DestroyImmediate(root);
        Debug.Log($"[MamaRPG] Prefab作成: {path}");
    }

    // ─────────────────────────────────────────
    //  日本語フォント設定ガイドを開く
    // ─────────────────────────────────────────
    [MenuItem("MamaRPG/② 日本語フォントの設定方法を確認する")]
    public static void ShowFontGuide()
    {
        EditorUtility.DisplayDialog(
            "日本語フォント設定",
            "【手順】\n\n" +
            "1. 日本語フォント (.ttf/.otf) を\n   Assets/Fonts/ に入れる\n   （Noto Sans JP などが無料で使えます）\n\n" +
            "2. Project で フォントを右クリック\n   → Create > TextMeshPro > Font Asset\n\n" +
            "3. 生成された Font Asset を開き\n   Generation Settings の\n   Character Set を「Unicode Range」に設定\n   → Generate Font Atlas をクリック\n\n" +
            "4. Project Settings > TextMeshPro\n   → Default Font Asset に\n   作成した Font Asset をセット\n\n" +
            "これで日本語が正常に表示されます。",
            "OK");
    }
}
#endif
