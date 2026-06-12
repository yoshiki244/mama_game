using UnityEngine;
using UnityEngine.UI;
using TMPro;

// メニュー画面: マップから開く（Bキー/ボタン）
// 項目: ビルド / ステータス / 設定（音量・BGM） / セーブ / 閉じる
public class MenuScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;
    RectTransform _statusContent;
    RectTransform _settingsContent;

    // ステータス表示
    TextMeshProUGUI _statusText;
    Image _expFill;
    const float ExpBarWidth = 380f;

    // 設定表示
    TextMeshProUGUI _volumeText;
    TextMeshProUGUI _bgmBtnLabel;

    // 通知
    TextMeshProUGUI _notice;

    // キーボード操作（矢印で選択・Enterで決定）
    readonly System.Collections.Generic.List<(Image img, System.Action action)> _items
        = new System.Collections.Generic.List<(Image, System.Action)>();
    int _selIndex;
    static readonly Color ItemNormal = new Color(0.2f, 0.17f, 0.32f);
    static readonly Color ItemSelected = new Color(0.48f, 0.38f, 0.7f);

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        Hide();
    }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        ShowStatusTab();
        _notice.text = "";
        _selIndex = 0;
        RefreshSelection();
    }

    public void Hide() => _root.gameObject.SetActive(false);

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MenuScreen", _main.Canvas.transform);
        var dim = _root.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.65f);

        // メニュー本体パネル
        var panel = ProtoUI.CreatePanel("Panel", _root, Vector2.zero, new Vector2(1100, 700),
            new Color(0.09f, 0.07f, 0.15f));
        ProtoUI.CreatePanel("GoldLine", panel.transform, new Vector2(0, 282), new Vector2(1060, 3),
            new Color(0.85f, 0.72f, 0.4f, 0.9f)).raycastTarget = false;

        var title = ProtoUI.CreateText("Title", panel.transform, "メニュー", 30, new Vector2(0, 316), new Vector2(400, 44));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 10f);

        // ---- 左の項目ボタン列 ----
        float by = 200;
        CreateMenuButton(panel.transform, "ビルド", ref by, () => _main.ShowBuild());
        CreateMenuButton(panel.transform, "ステータス", ref by, ShowStatusTab);
        CreateMenuButton(panel.transform, "設定", ref by, ShowSettingsTab);
        CreateMenuButton(panel.transform, "セーブ", ref by, SaveGame);
        CreateMenuButton(panel.transform, "閉じる", ref by, () => _main.ShowMap());

        // 通知欄（セーブ完了など）
        _notice = ProtoUI.CreateText("Notice", panel.transform, "", 18, new Vector2(-380, -300), new Vector2(300, 30),
            new Color(0.6f, 1f, 0.6f));

        // ---- 右の内容エリア ----
        BuildStatusContent(panel.transform);
        BuildSettingsContent(panel.transform);
    }

    void CreateMenuButton(Transform parent, string label, ref float y, System.Action onClick)
    {
        int index = _items.Count;
        var btn = ProtoUI.CreateButton($"Menu_{label}", parent, label, 22,
            new Vector2(-380, y), new Vector2(260, 62), ItemNormal,
            () => { _selIndex = index; RefreshSelection(); onClick(); });
        _items.Add(((Image)btn.targetGraphic, onClick));
        y -= 78;
    }

    // 矢印キーで選択を移動、Enter/Spaceで決定
    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        int move = 0;
        bool submit = false;

#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move = -1;
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move = +1;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                || kb.spaceKey.wasPressedThisFrame) submit = true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (move == 0)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) move = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) move = +1;
        }
        if (!submit && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))) submit = true;
#endif

        if (move != 0)
        {
            _selIndex = (_selIndex + move + _items.Count) % _items.Count;
            RefreshSelection();
        }
        if (submit && _items.Count > 0)
            _items[_selIndex].action();
    }

    // 選択中の項目を明るく光らせる
    void RefreshSelection()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].img.color = i == _selIndex ? ItemSelected : ItemNormal;
    }

    // ==================== ステータス ====================

    void BuildStatusContent(Transform parent)
    {
        _statusContent = ProtoUI.CreateRect("StatusContent", parent);
        _statusContent.anchoredPosition = new Vector2(140, -10);
        _statusContent.sizeDelta = new Vector2(700, 560);

        // キャラ立ち絵
        var charRt = ProtoUI.CreateRect("Chara", _statusContent);
        charRt.anchoredPosition = new Vector2(-230, 0);
        charRt.sizeDelta = new Vector2(190, 280);
        var charImg = charRt.gameObject.AddComponent<Image>();
        charImg.sprite = ProtoPixelArt.Mama();
        charImg.preserveAspect = true;

        var name = ProtoUI.CreateText("Name", _statusContent, "MAMA", 30, new Vector2(-230, 170), new Vector2(250, 40));
        ProtoUI.StyleTitle(name, new Color(0.96f, 0.93f, 1f));

        // ステータス本文
        _statusText = ProtoUI.CreateText("Stats", _statusContent, "", 24,
            new Vector2(130, 10), new Vector2(400, 400), Color.white, TextAlignmentOptions.TopLeft);
        _statusText.lineSpacing = 24f;

        // EXPバー（ステータス本文の下にゆとりを持って配置）
        ProtoUI.CreateText("ExpLabel", _statusContent, "EXP", 18, new Vector2(-150, -175), new Vector2(60, 26));
        ProtoUI.CreateGauge("ExpBar", _statusContent, new Vector2(90, -175), new Vector2(ExpBarWidth, 16),
            new Color(0.18f, 0.15f, 0.28f), new Color(0.45f, 0.85f, 0.55f), out _expFill);
    }

    void ShowStatusTab()
    {
        _settingsContent.gameObject.SetActive(false);
        _statusContent.gameObject.SetActive(true);

        var s = _main.Stats;
        _statusText.text =
            $"レベル　 {s.Level}\n" +
            $"HP　　　 {s.MaxHP}\n" +
            $"攻撃力　 {s.Attack}\n" +
            $"防御力　 {s.Defense}\n" +
            $"素早さ　 {s.Speed}\n" +
            $"現在地　 Wave {_main.Wave}";
        ProtoUI.SetGauge(_expFill, (float)s.Exp / s.ExpToNext, ExpBarWidth);
    }

    // ==================== 設定 ====================

    void BuildSettingsContent(Transform parent)
    {
        _settingsContent = ProtoUI.CreateRect("SettingsContent", parent);
        _settingsContent.anchoredPosition = new Vector2(140, -10);
        _settingsContent.sizeDelta = new Vector2(700, 560);

        // 音量（−/＋ボタン式）
        ProtoUI.CreateText("VolLabel", _settingsContent, "音量", 24, new Vector2(-180, 120), new Vector2(140, 36));
        ProtoUI.CreateButton("VolDown", _settingsContent, "−", 28, new Vector2(-40, 120), new Vector2(64, 56),
            new Color(0.2f, 0.17f, 0.32f), () => ChangeVolume(-0.1f));
        _volumeText = ProtoUI.CreateText("VolValue", _settingsContent, "", 24, new Vector2(60, 120), new Vector2(120, 36));
        ProtoUI.CreateButton("VolUp", _settingsContent, "＋", 28, new Vector2(160, 120), new Vector2(64, 56),
            new Color(0.2f, 0.17f, 0.32f), () => ChangeVolume(+0.1f));

        // BGM ON/OFF
        ProtoUI.CreateText("BgmLabel", _settingsContent, "BGM", 24, new Vector2(-180, 20), new Vector2(140, 36));
        var bgmBtn = ProtoUI.CreateButton("BgmToggle", _settingsContent, "", 22, new Vector2(60, 20), new Vector2(160, 56),
            new Color(0.2f, 0.17f, 0.32f), ToggleBgm);
        _bgmBtnLabel = bgmBtn.GetComponentInChildren<TextMeshProUGUI>();

        ProtoUI.CreateText("Note", _settingsContent,
            "※BGMはコード生成の仮チップチューンです", 16,
            new Vector2(0, -60), new Vector2(500, 30), new Color(0.6f, 0.6f, 0.75f));

        _settingsContent.gameObject.SetActive(false);
    }

    void ShowSettingsTab()
    {
        _statusContent.gameObject.SetActive(false);
        _settingsContent.gameObject.SetActive(true);
        RefreshSettingsView();
    }

    void RefreshSettingsView()
    {
        _volumeText.text = $"{Mathf.RoundToInt(AudioListener.volume * 100)}%";
        _bgmBtnLabel.text = _main.BgmEnabled ? "ON" : "OFF";
    }

    void ChangeVolume(float delta)
    {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
        PlayerPrefs.SetFloat("volume", AudioListener.volume);
        RefreshSettingsView();
    }

    void ToggleBgm()
    {
        _main.SetBgmEnabled(!_main.BgmEnabled);
        RefreshSettingsView();
    }

    // ==================== セーブ ====================

    void SaveGame()
    {
        ProtoSave.Save(_main);
        _notice.text = "セーブしました！";
    }
}
