using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// メニュー画面: マップから開く（Bキー）。ステータス / ビルド / 設定 / セーブ / 閉じる。
// 本設では単一キャラ・成長は盤面とピース。仲間/通電/点滅設定は廃止。
public class MenuScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;
    RectTransform _statusContent, _settingsContent, _statsArea;
    TextMeshProUGUI _statusText, _volumeText, _bgmLabel, _notice;

    readonly List<(Image img, System.Action action)> _items = new List<(Image, System.Action)>();
    readonly List<System.Action> _previews = new List<System.Action>();
    int _selIndex;
    static readonly Color ItemNormal = new Color(0.2f, 0.17f, 0.32f);
    static readonly Color ItemSelected = new Color(0.48f, 0.38f, 0.7f);

    public void Init(ProtoMain main) { _main = main; BuildUI(); Hide(); }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        ShowStatusTab();
        _notice.text = "";
        _selIndex = 0;
        RefreshSelection();
    }

    public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MenuScreen", _main.Canvas.transform);
        _root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);

        var panel = ProtoUI.CreatePanel("Panel", _root, Vector2.zero, new Vector2(1100, 700), new Color(0.09f, 0.07f, 0.15f));
        ProtoUI.CreatePanel("GoldLine", panel.transform, new Vector2(0, 282), new Vector2(1060, 3), new Color(0.85f, 0.72f, 0.4f, 0.9f)).raycastTarget = false;
        var title = ProtoUI.CreateText("Title", panel.transform, "メニュー", 30, new Vector2(0, 316), new Vector2(400, 44));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 10f);

        float by = 220;
        CreateMenuButton(panel.transform, "ステータス", ref by, ShowStatusTab, ShowStatusTab);
        CreateMenuButton(panel.transform, "ビルド", ref by, () => _main.ShowBuild());
        CreateMenuButton(panel.transform, "設定", ref by, ShowSettingsTab, ShowSettingsTab);
        CreateMenuButton(panel.transform, "セーブ", ref by, SaveGame);
        CreateMenuButton(panel.transform, "閉じる", ref by, () => _main.ShowMap());

        _notice = ProtoUI.CreateText("Notice", panel.transform, "", 18, new Vector2(-380, -300), new Vector2(320, 30), new Color(0.6f, 1f, 0.6f));

        BuildStatusContent(panel.transform);
        BuildSettingsContent(panel.transform);
    }

    void CreateMenuButton(Transform parent, string label, ref float y, System.Action onClick, System.Action onPreview = null)
    {
        int index = _items.Count;
        var btn = ProtoUI.CreateButton($"Menu_{label}", parent, label, 22, new Vector2(-380, y), new Vector2(260, 62), ItemNormal,
            () => { _selIndex = index; RefreshSelection(); onClick(); });
        _items.Add(((Image)btn.targetGraphic, onClick));
        _previews.Add(onPreview);
        y -= 84;
    }

    void RefreshSelection()
    {
        for (int i = 0; i < _items.Count; i++) _items[i].img.color = i == _selIndex ? ItemSelected : ItemNormal;
    }

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;
        int move = 0; bool submit = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move = -1;
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move = +1;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) submit = true;
            if (kb.escapeKey.wasPressedThisFrame) { _main.ShowMap(); return; }
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (move == 0) { if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) move = -1; else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) move = +1; }
        if (!submit && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))) submit = true;
        if (Input.GetKeyDown(KeyCode.Escape)) { _main.ShowMap(); return; }
#endif
        if (move != 0)
        {
            _selIndex = (_selIndex + move + _items.Count) % _items.Count;
            RefreshSelection();
            if (_selIndex < _previews.Count && _previews[_selIndex] != null) _previews[_selIndex]();
        }
        if (submit && _items.Count > 0) _items[_selIndex].action();
    }

    // ---- ステータス ----
    void BuildStatusContent(Transform parent)
    {
        _statusContent = ProtoUI.CreateRect("StatusContent", parent);
        _statusContent.anchoredPosition = new Vector2(140, -10);
        _statusContent.sizeDelta = new Vector2(700, 560);

        var charRt = ProtoUI.CreateRect("Chara", _statusContent);
        charRt.anchoredPosition = new Vector2(-220, 0);
        charRt.sizeDelta = new Vector2(190, 280);
        var img = charRt.gameObject.AddComponent<Image>();
        img.sprite = ProtoPixelArt.MamaMapPhoto(); img.preserveAspect = true;

        var name = ProtoUI.CreateText("Name", _statusContent, "MAMA", 30, new Vector2(-220, 215), new Vector2(250, 40));
        ProtoUI.StyleTitle(name, new Color(0.96f, 0.93f, 1f));

        // 各ステータスを枠で囲って並べる領域
        _statsArea = ProtoUI.CreateRect("StatsArea", _statusContent);
        _statsArea.anchoredPosition = new Vector2(150, 0);
        _statsArea.sizeDelta = new Vector2(420, 480);

        _statusContent.gameObject.SetActive(false);
    }

    void ShowStatusTab()
    {
        _settingsContent.gameObject.SetActive(false);
        _statusContent.gameObject.SetActive(true);

        foreach (Transform c in _statsArea) Destroy(c.gameObject);
        var rows = new (string, string)[]
        {
            ("HP",        $"{_main.Stats.MaxHP}"),
            ("攻撃力",     $"{_main.Stats.Attack}"),
            ("盤面",       $"{_main.BoardSize}×{_main.BoardSize}"),
            ("最大マナ",   $"{_main.MaxMana}"),
            ("お金",       $"{_main.Money}"),
            ("所持カード", $"{_main.OwnedCardIds.Count}種"),
            ("現在地",     $"Wave {_main.Wave}"),
        };
        float rowH = 50f, gap = 14f, top = 200f;
        for (int i = 0; i < rows.Length; i++)
        {
            float y = top - i * (rowH + gap);
            var box = ProtoUI.CreateFramedPanel($"Stat{i}", _statsArea, new Vector2(0, y), new Vector2(400, rowH),
                new Color(0.06f, 0.07f, 0.11f, 0.92f), new Color(0.65f, 0.55f, 0.36f, 0.7f));
            box.raycastTarget = false;
            var lab = ProtoUI.CreateText("L", box.transform, rows[i].Item1, 22, new Vector2(-110, 0), new Vector2(170, rowH - 8), new Color(0.78f, 0.85f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            var val = ProtoUI.CreateText("V", box.transform, rows[i].Item2, 24, new Vector2(95, 0), new Vector2(180, rowH - 8), Color.white);
            val.alignment = TextAlignmentOptions.Right; val.fontStyle = FontStyles.Bold;
        }
    }

    // ---- 設定 ----
    void BuildSettingsContent(Transform parent)
    {
        _settingsContent = ProtoUI.CreateRect("SettingsContent", parent);
        _settingsContent.anchoredPosition = new Vector2(140, -10);
        _settingsContent.sizeDelta = new Vector2(700, 560);

        ProtoUI.CreateText("VolLabel", _settingsContent, "音量", 24, new Vector2(-180, 120), new Vector2(140, 36));
        ProtoUI.CreateButton("VolDown", _settingsContent, "−", 28, new Vector2(-40, 120), new Vector2(64, 56), new Color(0.2f, 0.17f, 0.32f), () => ChangeVolume(-0.1f));
        _volumeText = ProtoUI.CreateText("VolValue", _settingsContent, "", 24, new Vector2(60, 120), new Vector2(120, 36));
        ProtoUI.CreateButton("VolUp", _settingsContent, "＋", 28, new Vector2(160, 120), new Vector2(64, 56), new Color(0.2f, 0.17f, 0.32f), () => ChangeVolume(+0.1f));

        ProtoUI.CreateText("BgmLabel", _settingsContent, "BGM", 24, new Vector2(-180, 20), new Vector2(140, 36));
        var bgmBtn = ProtoUI.CreateButton("BgmToggle", _settingsContent, "", 22, new Vector2(60, 20), new Vector2(160, 56), new Color(0.2f, 0.17f, 0.32f), ToggleBgm);
        _bgmLabel = bgmBtn.GetComponentInChildren<TextMeshProUGUI>();

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
        _bgmLabel.text = _main.BgmEnabled ? "ON" : "OFF";
    }

    void ChangeVolume(float delta)
    {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
        PlayerPrefs.SetFloat("volume", AudioListener.volume);
        RefreshSettingsView();
    }

    void ToggleBgm() { _main.SetBgmEnabled(!_main.BgmEnabled); RefreshSettingsView(); }

    void SaveGame() { ProtoSave.Save(_main); _notice.text = "セーブしました！"; }
}
