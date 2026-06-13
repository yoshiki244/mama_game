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
    // プロト検証用設定の値表示
    TextMeshProUGUI _chainBtnLabel, _chainAdjLabel, _flashBtnLabel, _flashThLabel, _chainMultLabel, _flashMultLabel;

    // 通知
    TextMeshProUGUI _notice;

    // キーボード操作（矢印で選択・Enterで決定）
    readonly System.Collections.Generic.List<(Image img, System.Action action)> _items
        = new System.Collections.Generic.List<(Image, System.Action)>();
    int _selIndex;
    static readonly Color ItemNormal = new Color(0.2f, 0.17f, 0.32f);
    static readonly Color ItemSelected = new Color(0.48f, 0.38f, 0.7f);

    // 設定パネル内のキーボード操作（→で入る / ↑↓で行選択 / ←→で値変更）
    readonly System.Collections.Generic.List<(Image rowBg, System.Action onLeft, System.Action onRight)> _setRows
        = new System.Collections.Generic.List<(Image, System.Action, System.Action)>();
    bool _rightFocus;  // true=設定パネル内を操作中
    int _setIndex;
    static readonly Color RowNormal = new Color(0, 0, 0, 0);
    static readonly Color RowSelected = new Color(0.4f, 0.32f, 0.62f, 0.45f);

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
        SetRightFocus(false);
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

        // ---- 左の項目ボタン列（開いたとき最初に表示されるステータスを先頭に） ----
        float by = 230;
        CreateMenuButton(panel.transform, "ステータス", ref by, ShowStatusTab, ShowStatusTab);
        CreateMenuButton(panel.transform, "ビルド", ref by, () => _main.ShowBuild());
        CreateMenuButton(panel.transform, "仲間", ref by, ShowPartyTab, ShowPartyTab);
        CreateMenuButton(panel.transform, "設定", ref by, ShowSettingsTab, ShowSettingsTab);
        CreateMenuButton(panel.transform, "セーブ", ref by, SaveGame);
        CreateMenuButton(panel.transform, "閉じる", ref by, () => _main.ShowMap());

        // 通知欄（セーブ完了など）
        _notice = ProtoUI.CreateText("Notice", panel.transform, "", 18, new Vector2(-380, -300), new Vector2(300, 30),
            new Color(0.6f, 1f, 0.6f));

        // ---- 右の内容エリア ----
        BuildStatusContent(panel.transform);
        BuildSettingsContent(panel.transform);
        BuildPartyContent(panel.transform);
    }

    // ==================== 仲間 ====================

    RectTransform _partyContent;
    RectTransform _partyList;

    void BuildPartyContent(Transform parent)
    {
        _partyContent = ProtoUI.CreateRect("PartyContent", parent);
        _partyContent.anchoredPosition = new Vector2(140, -10);
        _partyContent.sizeDelta = new Vector2(700, 560);

        var title = ProtoUI.CreateText("PartyTitle", _partyContent, "パーティ（最大3人）", 24,
            new Vector2(0, 240), new Vector2(400, 36));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 4f);

        _partyList = ProtoUI.CreateRect("PartyList", _partyContent);
        _partyList.sizeDelta = new Vector2(700, 360);
        _partyList.anchoredPosition = new Vector2(0, 30);

        ProtoUI.CreateButton("AddMember", _partyContent, "仲間を増やす", 22,
            new Vector2(-145, -210), new Vector2(260, 60), new Color(0.3f, 0.45f, 0.3f), AddMember);
        ProtoUI.CreateButton("RemoveMember", _partyContent, "仲間を外す", 22,
            new Vector2(145, -210), new Vector2(260, 60), new Color(0.45f, 0.3f, 0.3f), RemoveMember);
        ProtoUI.CreateText("PartyHint", _partyContent, "→キー = 仲間を増やす　　←キー = 仲間を外す", 15,
            new Vector2(0, -260), new Vector2(500, 26), new Color(0.6f, 0.6f, 0.75f));

        _partyContent.gameObject.SetActive(false);
    }

    void ShowPartyTab()
    {
        _rightFocus = false;
        _statusContent.gameObject.SetActive(false);
        _settingsContent.gameObject.SetActive(false);
        _partyContent.gameObject.SetActive(true);
        RefreshPartyList();
    }

    void RefreshPartyList()
    {
        foreach (Transform c in _partyList) Destroy(c.gameObject);

        var party = _main.Party;
        float xs = -(party.Count - 1) * 220f / 2f;
        for (int i = 0; i < party.Count; i++)
        {
            var m = party[i];
            var slot = ProtoUI.CreatePanel($"Member{i}", _partyList, new Vector2(xs + i * 220f, 0),
                new Vector2(190, 320), new Color(0.14f, 0.11f, 0.22f));
            slot.raycastTarget = false;

            var icon = ProtoUI.CreateRect("Icon", slot.transform);
            icon.anchoredPosition = new Vector2(0, -10);
            icon.sizeDelta = new Vector2(130, 220);
            var img = icon.gameObject.AddComponent<Image>();
            img.sprite = m.BattleSprite();
            img.preserveAspect = true;
            img.raycastTarget = false;

            var nameText = ProtoUI.CreateText("Name", slot.transform, m.name, 20,
                new Vector2(0, 135), new Vector2(180, 30));
            nameText.fontStyle = TMPro.FontStyles.Bold;
            nameText.color = Color.Lerp(m.hair, Color.white, 0.3f);

            ProtoUI.CreateText("Tag", slot.transform, i == 0 ? "リーダー" : "仲間", 14,
                new Vector2(0, -150), new Vector2(180, 24), new Color(0.7f, 0.7f, 0.85f));
        }
    }

    void AddMember()
    {
        if (_main.AddPartyMember())
        {
            _notice.text = $"{_main.Party[_main.Party.Count - 1].name} が仲間になった！";
            RefreshPartyList();
        }
        else
        {
            _notice.text = "<color=#FF7070>これ以上仲間にできません（最大3人）</color>";
        }
    }

    void RemoveMember()
    {
        string lastName = _main.Party[_main.Party.Count - 1].name;
        if (_main.RemovePartyMember())
        {
            _notice.text = $"{lastName} がパーティから離れた…";
            RefreshPartyList();
        }
        else
        {
            _notice.text = "<color=#FF7070>リーダー（MAMA）は外せません</color>";
        }
    }

    // onPreview: 矢印で選んだだけで右側に表示する内容（タブ系の項目のみ）
    readonly System.Collections.Generic.List<System.Action> _previews
        = new System.Collections.Generic.List<System.Action>();

    void CreateMenuButton(Transform parent, string label, ref float y, System.Action onClick, System.Action onPreview = null)
    {
        int index = _items.Count;
        var btn = ProtoUI.CreateButton($"Menu_{label}", parent, label, 22,
            new Vector2(-380, y), new Vector2(260, 62), ItemNormal,
            () => { _selIndex = index; RefreshSelection(); onClick(); });
        _items.Add(((Image)btn.targetGraphic, onClick));
        _previews.Add(onPreview);
        y -= 78;
    }

    // 矢印キーで選択を移動、Enter/Spaceで決定。
    // 設定タブ表示中に → を押すとパネル内に入り、↑↓で行選択・←→で値変更・Escで戻る
    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        int move = 0;
        bool submit = false, left = false, right = false, back = false;

#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move = -1;
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move = +1;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                || kb.spaceKey.wasPressedThisFrame) submit = true;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) left = true;
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) right = true;
            if (kb.escapeKey.wasPressedThisFrame) back = true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (move == 0)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) move = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) move = +1;
        }
        if (!submit && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))) submit = true;
        if (!left && (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))) left = true;
        if (!right && (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))) right = true;
        if (!back && Input.GetKeyDown(KeyCode.Escape)) back = true;
#endif

        if (_rightFocus)
        {
            // ===== 設定パネル内の操作 =====
            if (back)
            {
                SetRightFocus(false);
                return;
            }
            if (move != 0)
            {
                _setIndex = (_setIndex + move + _setRows.Count) % _setRows.Count;
                RefreshRowHighlight();
            }
            if (left) _setRows[_setIndex].onLeft();   // ← 値を下げる/切り替え
            else if (right) _setRows[_setIndex].onRight(); // → 値を上げる/切り替え
            else if (submit)
            {
                if (_setIndex == _setRows.Count - 1)
                {
                    SetRightFocus(false); // 最後の項目でEnter → 左のメニューへ戻る
                }
                else
                {
                    _setIndex++; // Enter = 次の設定項目へ
                    RefreshRowHighlight();
                }
            }
            return;
        }

        // ===== 仲間タブ表示中: ←→で仲間を増減 =====
        if (_partyContent != null && _partyContent.gameObject.activeSelf)
        {
            if (right) { AddMember(); return; }    // → 仲間を増やす
            if (left) { RemoveMember(); return; }  // ← 仲間を外す
        }

        // ===== 左のメニュー項目の操作 =====
        if (right && _settingsContent.gameObject.activeSelf)
        {
            SetRightFocus(true); // 設定が開いていれば → でパネル内に入る
            return;
        }
        if (move != 0)
        {
            _selIndex = (_selIndex + move + _items.Count) % _items.Count;
            RefreshSelection();
            // 選んだだけで右側に詳細を表示（ビルド/セーブ/閉じるは何もしない）
            if (_selIndex < _previews.Count && _previews[_selIndex] != null)
                _previews[_selIndex]();
        }
        if (submit && _items.Count > 0)
            _items[_selIndex].action();
    }

    // 設定パネル内フォーカスの切り替え
    void SetRightFocus(bool on)
    {
        _rightFocus = on;
        _setIndex = 0;
        RefreshRowHighlight();
    }

    void RefreshRowHighlight()
    {
        for (int i = 0; i < _setRows.Count; i++)
            _setRows[i].rowBg.color = (_rightFocus && i == _setIndex) ? RowSelected : RowNormal;
    }

    // 選択中の項目を明るく光らせる
    void RefreshSelection()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].img.color = i == _selIndex ? ItemSelected : ItemNormal;
    }

    // ==================== ステータス ====================

    int _statusMember; // ステータス表示中のメンバー
    Image _statusPortrait;
    TextMeshProUGUI _statusName;
    RectTransform _statusTabs;
    readonly System.Collections.Generic.List<Image> _statusTabImgs
        = new System.Collections.Generic.List<Image>();

    void BuildStatusContent(Transform parent)
    {
        _statusContent = ProtoUI.CreateRect("StatusContent", parent);
        _statusContent.anchoredPosition = new Vector2(140, -10);
        _statusContent.sizeDelta = new Vector2(700, 560);

        // メンバー切り替えタブ（上部）
        _statusTabs = ProtoUI.CreateRect("StatusTabs", _statusContent);
        _statusTabs.anchoredPosition = new Vector2(0, 245);
        _statusTabs.sizeDelta = new Vector2(700, 46);

        // キャラ立ち絵
        var charRt = ProtoUI.CreateRect("Chara", _statusContent);
        charRt.anchoredPosition = new Vector2(-230, -20);
        charRt.sizeDelta = new Vector2(190, 280);
        _statusPortrait = charRt.gameObject.AddComponent<Image>();
        _statusPortrait.sprite = ProtoPixelArt.Mama();
        _statusPortrait.preserveAspect = true;

        _statusName = ProtoUI.CreateText("Name", _statusContent, "MAMA", 30, new Vector2(-230, 155), new Vector2(250, 40));
        ProtoUI.StyleTitle(_statusName, new Color(0.96f, 0.93f, 1f));

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
        _rightFocus = false;
        _settingsContent.gameObject.SetActive(false);
        if (_partyContent != null) _partyContent.gameObject.SetActive(false);
        _statusContent.gameObject.SetActive(true);

        if (_statusMember >= _main.Party.Count) _statusMember = 0; // 仲間を外した直後の保険
        RefreshStatusTabs();
        RefreshStatusView();
    }

    // メンバー切り替えタブを作り直す（パーティの増減に追従）
    void RefreshStatusTabs()
    {
        foreach (Transform c in _statusTabs) Destroy(c.gameObject);
        _statusTabImgs.Clear();

        var party = _main.Party;
        float xs = -(party.Count - 1) * 150f / 2f;
        for (int i = 0; i < party.Count; i++)
        {
            int mi = i;
            var btn = ProtoUI.CreateButton($"STab_{party[i].name}", _statusTabs, party[i].name, 17,
                new Vector2(xs + i * 150f, 0), new Vector2(140, 42), new Color(0.2f, 0.17f, 0.32f),
                () => { _statusMember = mi; RefreshStatusTabs(); RefreshStatusView(); });
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            label.color = Color.Lerp(party[i].hair, Color.white, 0.3f);
            var img = (Image)btn.targetGraphic;
            if (i == _statusMember) img.color = new Color(0.48f, 0.38f, 0.7f);
            _statusTabImgs.Add(img);
        }
    }

    // 選択中メンバーのステータスを表示
    void RefreshStatusView()
    {
        var member = _main.Party[_statusMember];
        var s = _main.MemberStats[_statusMember];

        _statusPortrait.sprite = member.BattleSprite();
        _statusName.text = member.name;
        _statusName.color = Color.Lerp(member.hair, Color.white, 0.3f);

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

        _setRows.Clear();
        float y = 232f;        // 行の開始Y
        const float step = 52f; // 行間
        var btnCol = new Color(0.2f, 0.17f, 0.32f);

        // 各行の生成ヘルパー: ラベル＋値表示ボタン（−/＋付き）。onLeft/onRightはキーボード操作用
        TextMeshProUGUI Row(string id, string label, System.Action onLeft, System.Action onRight, bool withPlusMinus)
        {
            var row = ProtoUI.CreatePanel($"Row{id}", _settingsContent, new Vector2(0, y), new Vector2(620, 46), RowNormal);
            row.raycastTarget = false;
            _setRows.Add((row, onLeft, onRight));

            ProtoUI.CreateText($"L{id}", _settingsContent, label, 20, new Vector2(-200, y), new Vector2(220, 34),
                Color.white, TextAlignmentOptions.Left);

            if (withPlusMinus)
            {
                ProtoUI.CreateButton($"Dn{id}", _settingsContent, "−", 24, new Vector2(70, y), new Vector2(48, 40), btnCol, onLeft);
                ProtoUI.CreateButton($"Up{id}", _settingsContent, "＋", 24, new Vector2(240, y), new Vector2(48, 40), btnCol, onRight);
                var val = ProtoUI.CreateText($"V{id}", _settingsContent, "", 20, new Vector2(155, y), new Vector2(120, 34));
                y -= step;
                return val;
            }
            else
            {
                var btn = ProtoUI.CreateButton($"B{id}", _settingsContent, "", 20, new Vector2(150, y), new Vector2(190, 40), btnCol, onRight);
                y -= step;
                return btn.GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        _volumeText     = Row("Vol",   "音量",        () => ChangeVolume(-0.1f), () => ChangeVolume(+0.1f), true);
        _bgmBtnLabel    = Row("Bgm",   "BGM",         ToggleBgm, ToggleBgm, false);
        _chainBtnLabel  = Row("Chain", "通電連鎖",     ToggleChain, ToggleChain, false);
        _chainAdjLabel  = Row("Adj",   "通電の隣接",   ToggleChainAdj, ToggleChainAdj, false);
        _flashBtnLabel  = Row("Flash", "順次点滅",     ToggleFlash, ToggleFlash, false);
        _flashThLabel   = Row("FlTh",  "点滅発動条件", ToggleFlashTh, ToggleFlashTh, false);
        _chainMultLabel = Row("CMul",  "通電クリ倍率", () => ChangeChainMult(-0.05f), () => ChangeChainMult(+0.05f), true);
        _flashMultLabel = Row("FMul",  "点滅クリ倍率", () => ChangeFlashMult(-0.05f), () => ChangeFlashMult(+0.05f), true);

        ProtoUI.CreateText("Note", _settingsContent,
            "→キーで設定の中へ　←→で変更　Enterで次の項目　Escで戻る", 14,
            new Vector2(0, y - 8), new Vector2(600, 40), new Color(0.6f, 0.6f, 0.75f));

        _settingsContent.gameObject.SetActive(false);
    }

    void ToggleChain()    { _main.SetChainEnabled(!_main.ChainEnabled); RefreshSettingsView(); }
    void ToggleChainAdj() { _main.SetChainCorner(!_main.ChainCorner); RefreshSettingsView(); }
    void ToggleFlash()    { _main.SetFlashEnabled(!_main.FlashEnabled); RefreshSettingsView(); }
    void ToggleFlashTh()  { _main.SetFlashThreshold(_main.FlashThreshold == 10 ? 15 : 10); RefreshSettingsView(); }
    void ChangeChainMult(float d) { _main.SetChainCritMult(_main.ChainCritMult + d); RefreshSettingsView(); }
    void ChangeFlashMult(float d) { _main.SetFlashCritMult(_main.FlashCritMult + d); RefreshSettingsView(); }

    void ShowSettingsTab()
    {
        _statusContent.gameObject.SetActive(false);
        if (_partyContent != null) _partyContent.gameObject.SetActive(false);
        _settingsContent.gameObject.SetActive(true);
        RefreshSettingsView();
    }

    void RefreshSettingsView()
    {
        _volumeText.text = $"{Mathf.RoundToInt(AudioListener.volume * 100)}%";
        _bgmBtnLabel.text = _main.BgmEnabled ? "ON" : "OFF";
        _chainBtnLabel.text = _main.ChainEnabled ? "ON" : "OFF";
        _chainAdjLabel.text = _main.ChainCorner ? "頂点接" : "辺接";
        _flashBtnLabel.text = _main.FlashEnabled ? "ON" : "OFF";
        _flashThLabel.text = $"{_main.FlashThreshold}マス以上";
        _chainMultLabel.text = $"{_main.ChainCritMult:0.00}倍";
        _flashMultLabel.text = $"{_main.FlashCritMult:0.00}倍";
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
