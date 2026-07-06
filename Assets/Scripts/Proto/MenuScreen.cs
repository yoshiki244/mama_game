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
    TextMeshProUGUI _statusText, _volumeText, _bgmLabel, _notice, _ascText, _speedLabel;

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

    public void Hide()
    {
        if (_confirmOverlay != null) { Destroy(_confirmOverlay); _confirmOverlay = null; }
        if (_dexGO != null) { Destroy(_dexGO); _dexGO = null; }
        RevertUnsavedSettings();   // 「設定を保存」していない変更は破棄して保存値に戻す
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // 保存済みの設定値へ巻き戻す（プレビューだけして保存しなかった場合）
    void RevertUnsavedSettings()
    {
        if (_main == null) return;
        AudioListener.volume = PlayerPrefs.GetFloat("volume", 0.8f);
        _main.SetBgmEnabled(PlayerPrefs.GetInt("bgm", 1) == 1, save: false);
        _main.SetGameSpeed(PlayerPrefs.GetFloat("gamespeed", 1f), save: false);
        _pAsc = ProtoUnlocks.Ascension;
    }

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MenuScreen", _main.Canvas.transform);
        var rootImg = _root.gameObject.AddComponent<Image>();
        var menuBg = ProtoPixelArt.MenuBackground();
        if (menuBg != null) { rootImg.sprite = menuBg; rootImg.color = Color.white; rootImg.preserveAspect = false; }
        else rootImg.color = new Color(0, 0, 0, 0.65f);

        // 画面外周の細い金枠
        var gold = new Color(0.92f, 0.78f, 0.34f, 0.95f);
        float ew = 1592f, eh = 892f, et = 3f;
        ProtoUI.CreatePanel("EdgeTop", _root, new Vector2(0, eh / 2f), new Vector2(ew, et), gold).raycastTarget = false;
        ProtoUI.CreatePanel("EdgeBottom", _root, new Vector2(0, -eh / 2f), new Vector2(ew, et), gold).raycastTarget = false;
        ProtoUI.CreatePanel("EdgeLeft", _root, new Vector2(-ew / 2f, 0), new Vector2(et, eh), gold).raycastTarget = false;
        ProtoUI.CreatePanel("EdgeRight", _root, new Vector2(ew / 2f, 0), new Vector2(et, eh), gold).raycastTarget = false;

        var panel = ProtoUI.CreatePanel("Panel", _root, Vector2.zero, new Vector2(1100, 700), new Color(0, 0, 0, 0));
        ProtoUI.CreatePanel("GoldLine", panel.transform, new Vector2(0, 282), new Vector2(1060, 3), new Color(0.85f, 0.72f, 0.4f, 0.9f)).raycastTarget = false;
        var title = ProtoUI.CreateText("Title", panel.transform, "メニュー", 30, new Vector2(0, 316), new Vector2(400, 44));
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 10f);

        float by = 220;
        CreateMenuButton(panel.transform, "ステータス", ref by, ShowStatusTab, ShowStatusTab);
        CreateMenuButton(panel.transform, "ビルド", ref by, () => _main.ShowBuild());
        CreateMenuButton(panel.transform, "図鑑", ref by, ShowCardDex);
        CreateMenuButton(panel.transform, "設定", ref by, ShowSettingsTab, ShowSettingsTab);
        CreateMenuButton(panel.transform, "閉じる", ref by, () => _main.ShowMap());
        // ※手動セーブは廃止（マス到達・イベントごとにオートセーブ）

        // 最初から（すべてリセット）＝赤ボタン・一番下（間隔を1つ空ける）
        by -= 30;
        ProtoUI.CreatePanel("MB_Restart", panel.transform, new Vector2(-380, by), new Vector2(270, 70), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreateButton("Menu_Restart", panel.transform, "最初から", 22, new Vector2(-380, by), new Vector2(260, 62),
            new Color(0.62f, 0.16f, 0.16f, 0.98f), ShowRestartConfirm);

        _notice = ProtoUI.CreateText("Notice", panel.transform, "", 18, new Vector2(-380, -300), new Vector2(320, 30), new Color(0.6f, 1f, 0.6f));

        BuildStatusContent(panel.transform);
        BuildSettingsContent(panel.transform);
    }

    GameObject _confirmOverlay;

    // 「最初から」確認ダイアログ（はい＝リセット / いいえ＝メニューへ戻る）
    void ShowRestartConfirm()
    {
        if (_confirmOverlay != null) Destroy(_confirmOverlay);
        var ov = ProtoUI.CreateFullScreen("RestartConfirm", _root);
        _confirmOverlay = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

        var box = ProtoUI.CreateFramedPanel("ConfirmBox", ov, Vector2.zero, new Vector2(560, 280),
            new Color(0.10f, 0.08f, 0.16f, 0.98f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
        ProtoUI.CreateText("CMsg", box.transform, "最初からやり直しますか？", 26, new Vector2(0, 70), new Vector2(520, 40), Color.white);
        ProtoUI.CreateText("CSub", box.transform, "すべての進行がリセットされます", 18, new Vector2(0, 26), new Vector2(520, 30), new Color(1f, 0.7f, 0.7f));
        ProtoUI.CreateGoldButton("CYes", box.transform, "はい", 24, new Vector2(-130, -70), new Vector2(200, 64),
            new Color(0.62f, 0.16f, 0.16f, 0.98f), () => { Destroy(_confirmOverlay); _confirmOverlay = null; _main.RestartRun(); });
        ProtoUI.CreateGoldButton("CNo", box.transform, "いいえ", 24, new Vector2(130, -70), new Vector2(200, 64),
            new Color(0.3f, 0.3f, 0.4f, 0.98f), () => { Destroy(_confirmOverlay); _confirmOverlay = null; });
    }

    void CreateMenuButton(Transform parent, string label, ref float y, System.Action onClick, System.Action onPreview = null)
    {
        int index = _items.Count;
        ProtoUI.CreatePanel($"MB_{label}", parent, new Vector2(-380, y), new Vector2(270, 70), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false; // 金枠
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
            ("HP",        $"{_main.MaxHP}"),
            ("盤面マス",   $"{_main.BoardCells} / {ProtoMain.MaxCells}"),
            ("ストックマス", $"{_main.CellStock}"),
            ("最大マナ",   $"{_main.MaxMana}"),
            ("お金",       $"{_main.Money}"),
            ("所持カード", $"{_main.OwnedCardIds.Count}種"),
            ("装備",       EquipInfo.Name(_main.Equipped)),
            ("現在地",     $"Wave {_main.Wave}"),
            ("通算クリア",  $"{ProtoUnlocks.Clears} 回"),
            ("ベストスコア", $"{ProtoUnlocks.BestScore}"),
        };
        float rowH = 44f, gap = 10f, top = 210f;
        for (int i = 0; i < rows.Length; i++)
        {
            float y = top - i * (rowH + gap);
            var box = ProtoUI.CreateFramedPanel($"Stat{i}", _statsArea, new Vector2(0, y), new Vector2(400, rowH),
                new Color(0.06f, 0.07f, 0.11f, 0.92f), new Color(0.65f, 0.55f, 0.36f, 0.7f));
            box.raycastTarget = false;
            var lab = ProtoUI.CreateText("L", box.transform, rows[i].Item1, 22, new Vector2(-110, 0), new Vector2(170, rowH - 8), new Color(0.78f, 0.85f, 1f));
            lab.alignment = TextAlignmentOptions.Left;
            var val = ProtoUI.CreateText("V", box.transform, rows[i].Item2, 24, new Vector2(80, 0), new Vector2(210, rowH - 8), Color.white);
            val.alignment = TextAlignmentOptions.Right; val.fontStyle = FontStyles.Bold;
            val.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            val.enableAutoSizing = true; val.fontSizeMin = 13; val.fontSizeMax = 24;
        }
    }

    // ---- 設定 ----
    void BuildSettingsContent(Transform parent)
    {
        _settingsContent = ProtoUI.CreateRect("SettingsContent", parent);
        _settingsContent.anchoredPosition = new Vector2(140, -10);
        _settingsContent.sizeDelta = new Vector2(700, 560);

        ProtoUI.CreateText("VolLabel", _settingsContent, "音量", 24, new Vector2(-180, 120), new Vector2(140, 36));
        ProtoUI.CreateGoldButton("VolDown", _settingsContent, "−", 28, new Vector2(-40, 120), new Vector2(64, 56), new Color(0.2f, 0.17f, 0.32f), () => ChangeVolume(-0.1f));
        _volumeText = ProtoUI.CreateText("VolValue", _settingsContent, "", 24, new Vector2(60, 120), new Vector2(120, 36));
        ProtoUI.CreateGoldButton("VolUp", _settingsContent, "＋", 28, new Vector2(160, 120), new Vector2(64, 56), new Color(0.2f, 0.17f, 0.32f), () => ChangeVolume(+0.1f));

        ProtoUI.CreateText("BgmLabel", _settingsContent, "BGM", 24, new Vector2(-180, 20), new Vector2(140, 36));
        var bgmBtn = ProtoUI.CreateGoldButton("BgmToggle", _settingsContent, "", 22, new Vector2(60, 20), new Vector2(160, 56), new Color(0.2f, 0.17f, 0.32f), ToggleBgm);
        _bgmLabel = bgmBtn.GetComponentInChildren<TextMeshProUGUI>();

        // 演出速度（周回の快適性）
        ProtoUI.CreateText("SpdLabel", _settingsContent, "演出速度", 24, new Vector2(-180, -40), new Vector2(160, 36));
        var spdBtn = ProtoUI.CreateGoldButton("SpdToggle", _settingsContent, "", 22, new Vector2(60, -40), new Vector2(160, 56), new Color(0.2f, 0.17f, 0.32f), ChangeSpeed);
        _speedLabel = spdBtn.GetComponentInChildren<TextMeshProUGUI>();

        // 難度（アセンション）：解放済みの範囲で選択。「最初から」で次ランに反映
        ProtoUI.CreateText("AscLabel", _settingsContent, "難易度", 24, new Vector2(-180, -120), new Vector2(160, 36));
        ProtoUI.CreateGoldButton("AscDown", _settingsContent, "−", 28, new Vector2(-40, -120), new Vector2(64, 56), new Color(0.2f, 0.17f, 0.32f), () => ChangeAsc(-1));
        _ascText = ProtoUI.CreateText("AscValue", _settingsContent, "", 24, new Vector2(70, -120), new Vector2(150, 36));
        ProtoUI.CreateGoldButton("AscUp", _settingsContent, "＋", 28, new Vector2(180, -120), new Vector2(64, 56), new Color(0.2f, 0.17f, 0.32f), () => ChangeAsc(+1));
        ProtoUI.CreateText("AscHint", _settingsContent, "難易度は「最初から」で反映（上位ほど敵が強化される）", 15,
            new Vector2(0, -180), new Vector2(660, 24), new Color(0.7f, 0.75f, 0.9f));
        RefreshAsc();

        // 設定を保存（押すまで確定されない。保存せず閉じると元の設定に戻る）
        ProtoUI.CreateGoldButton("SettingsSave", _settingsContent, "設定を保存", 22, new Vector2(0, -255), new Vector2(260, 60),
            new Color(0.30f, 0.45f, 0.32f, 0.98f), SaveSettings);

        _settingsContent.gameObject.SetActive(false);
    }

    void ShowSettingsTab()
    {
        _statusContent.gameObject.SetActive(false);
        _settingsContent.gameObject.SetActive(true);
        _pAsc = ProtoUnlocks.Ascension;   // 難易度の仮選択を保存値から開始
        RefreshSettingsView();
    }

    void RefreshSettingsView()
    {
        _volumeText.text = $"{Mathf.RoundToInt(AudioListener.volume * 100)}%";
        _bgmLabel.text = _main.BgmEnabled ? "ON" : "OFF";
        if (_speedLabel != null) _speedLabel.text = $"×{_main.GameSpeed:0.#}";
        RefreshAsc();
    }

    // ==================== カード図鑑 ====================
    GameObject _dexGO;
    void ShowCardDex()
    {
        if (_dexGO != null) { Destroy(_dexGO); _dexGO = null; }
        var ov = ProtoUI.CreateFullScreen("CardDex", _root);
        _dexGO = ov.gameObject;
        ov.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.05f, 0.97f);

        var title = ProtoUI.CreateText("DexTitle", ov, "カード図鑑", 36, new Vector2(0, 400), new Vector2(600, 50), ProtoUI.Gold);
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 6f);

        // 収集状況（アンロック済みカードのうち入手したことがある種類数）
        var all = _main.Db != null ? _main.Db.cards : new List<CardDef>();
        int discovered = 0, unlocked = 0;
        foreach (var c in all)
        {
            if (c == null) continue;
            if (c.unlockTier <= ProtoUnlocks.UnlockLevel) unlocked++;
            if (ProtoUnlocks.IsDiscovered(c.id)) discovered++;   // 発見済み＝一度でも入手したことがある（永続）
        }

        // 盤面に配置中の枚数（在庫と合算して「本当の所持数」を出す）
        var placedCnt = new Dictionary<string, int>();
        if (_main.Panel != null)
            foreach (var p in _main.Panel.Placements)
                if (p.card != null)
                { placedCnt.TryGetValue(p.card.id, out var n); placedCnt[p.card.id] = n + 1; }
        ProtoUI.CreateText("DexCount", ov, $"入手済み {discovered} 種　／　解放済み {unlocked} 種　／　全 {all.Count} 種", 18,
            new Vector2(0, 358), new Vector2(800, 26), new Color(0.8f, 0.85f, 1f));

        // スクロールリスト
        var viewport = ProtoUI.CreateRect("DexView", ov);
        viewport.anchoredPosition = new Vector2(0, -30);
        viewport.sizeDelta = new Vector2(1100, 640);
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.3f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var sr = viewport.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.viewport = viewport;
        sr.scrollSensitivity = 30f; sr.movementType = ScrollRect.MovementType.Clamped;

        var content = ProtoUI.CreateRect("DexContent", viewport);
        content.anchorMin = new Vector2(0.5f, 1f); content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        sr.content = content;

        // レアリティ→マス数の順に整列
        var sorted = new List<CardDef>();
        foreach (var c in all) if (c != null) sorted.Add(c);
        sorted.Sort((a, b) => a.rarity != b.rarity ? a.rarity.CompareTo(b.rarity) : a.Size.CompareTo(b.Size));

        const int perRow = 4;
        const float cw = 250f, chh = 170f, gx = 12f, gy = 12f;
        float startX = -(perRow - 1) * (cw + gx) / 2f;
        for (int i = 0; i < sorted.Count; i++)
        {
            var card = sorted[i];
            int r = i / perRow, col = i % perRow;
            var pos = new Vector2(startX + col * (cw + gx), -20f - chh / 2f - r * (chh + gy));
            bool tierOpen = card.unlockTier <= ProtoUnlocks.UnlockLevel;
            bool isUnlocked = tierOpen && ProtoUnlocks.IsDiscovered(card.id);   // 詳細表示は「発見済み」のみ

            // レアは後光（ハロー）を背後に敷く
            if (isUnlocked && card.rarity >= 2)
            {
                var halo = ProtoUI.CreateGlow("Halo", content, pos, new Vector2(cw + 100, chh + 100), new Color(1f, 0.82f, 0.35f, 0.5f));
                var hrt = (RectTransform)halo.transform;
                hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
                var hg = halo.gameObject.AddComponent<RareGlow>();
                hg.target = halo; hg.colA = new Color(1f, 0.8f, 0.3f, 0.22f); hg.colB = new Color(1f, 0.88f, 0.5f, 0.65f);
            }

            var frame = ProtoUI.CreatePanel($"Dex_{card.id}", content, pos, new Vector2(cw, chh),
                isUnlocked ? Color.Lerp(card.RarityColor, new Color(0.4f, 0.35f, 0.25f), 0.55f) : new Color(0.2f, 0.2f, 0.24f));
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1f);   // コンテンツ上端基準で並べる（スクロール位置ずれ防止）
            var inner = ProtoUI.VGrad(ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(cw - 8, chh - 8), new Color(0.14f, 0.13f, 0.20f)));
            inner.raycastTarget = false;
            if (isUnlocked && card.rarity >= 2) ProtoUI.AddShine(inner, new Vector2(cw - 8, chh - 8));   // 走査光

            if (!isUnlocked)
            {
                ProtoUI.CreateText("Q", inner.transform, "？？？", 30, new Vector2(0, 16), new Vector2(200, 40), new Color(0.5f, 0.5f, 0.6f));
                ProtoUI.CreateText("H", inner.transform, tierOpen ? "未入手（入手すると図鑑に登録）" : "クリアすると解放", 14,
                    new Vector2(0, -40), new Vector2(240, 22), new Color(0.45f, 0.45f, 0.55f));
                continue;
            }

            int stock = _main.OwnedCount(card.id);
            placedCnt.TryGetValue(card.id, out int placed);
            int totalOwned = stock + placed;   // 在庫＋盤面配置中＝本当の所持数
            var nm = ProtoUI.CreateText("N", inner.transform,
                totalOwned > 0 ? $"{card.displayName} ×{totalOwned}" : card.displayName, 17,
                new Vector2(0, 66), new Vector2(cw - 20, 24), card.RarityColor);
            nm.fontStyle = FontStyles.Bold; nm.enableAutoSizing = true; nm.fontSizeMin = 11; nm.fontSizeMax = 17;
            nm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            // レアは枠と名前が金色に脈動して光る
            if (card.rarity >= 2)
            {
                var fg = frame.gameObject.AddComponent<RareGlow>();
                fg.target = frame; fg.colA = Color.Lerp(card.RarityColor, Color.black, 0.35f); fg.colB = Color.Lerp(card.RarityColor, Color.white, 0.6f);
                var ng = nm.gameObject.AddComponent<RareGlow>();
                ng.target = nm; ng.colA = card.RarityColor; ng.colB = Color.white;
            }

            ProtoUI.CreateText("K", inner.transform,
                $"{card.RarityLabel}　{CardDef.KindLabel(card.Category)} / {card.Size}マス / マナ{card.ManaCost}" +
                (placed > 0 ? $"　<color=#8FE08F>配置中{placed}</color>" : ""), 12,
                new Vector2(0, 44), new Vector2(cw - 16, 18), new Color(0.8f, 0.85f, 1f));

            var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 2), new Vector2(cw - 40, 58), new Color(0.04f, 0.04f, 0.09f));
            art.raycastTarget = false;
            DexMini(art.transform, card, 9f);

            string eff = !string.IsNullOrEmpty(card.description)
                ? (card.power > 0 ? $"威力{card.power}　{card.description}" : card.description)
                : (card.power > 0 ? $"威力 {card.power}" : "");
            var dt = ProtoUI.CreateText("D", inner.transform, eff, 12, new Vector2(0, -52), new Vector2(cw - 20, 46),
                new Color(0.9f, 0.92f, 1f), TextAlignmentOptions.Top);
            dt.enableAutoSizing = true; dt.fontSizeMin = 9; dt.fontSizeMax = 12;
        }

        int rows = Mathf.CeilToInt(sorted.Count / (float)perRow);
        content.sizeDelta = new Vector2(1080, 40f + rows * (chh + gy));

        ProtoUI.CreateGoldButton("DexClose", ov, "閉じる", 22, new Vector2(0, -400), new Vector2(240, 58),
            new Color(0.45f, 0.3f, 0.4f, 0.98f), () => { Destroy(_dexGO); _dexGO = null; });
    }

    // 図鑑用ミニ形状
    void DexMini(Transform parent, CardDef card, float cs)
    {
        var shape = card.Shape;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var v in shape) { minX = Mathf.Min(minX, v.x); minY = Mathf.Min(minY, v.y); maxX = Mathf.Max(maxX, v.x); maxY = Mathf.Max(maxY, v.y); }
        float gap = 1.5f, ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.Bevel(ProtoUI.CreatePanel("M", parent,
                new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)),
                new Vector2(cs, cs), card.CategoryColor)).raycastTarget = false;
    }

    // ---- 設定の変更は「プレビュー」扱い。『設定を保存』で確定、保存せず閉じると元に戻る ----
    int _pAsc;   // 難易度の仮選択（保存で確定）

    void ChangeVolume(float delta)
    {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);   // 試聴のみ（保存しない）
        RefreshSettingsView();
    }

    void ToggleBgm() { _main.SetBgmEnabled(!_main.BgmEnabled, save: false); RefreshSettingsView(); }

    void ChangeAsc(int delta) { _pAsc = Mathf.Clamp(_pAsc + delta, 0, ProtoUnlocks.MaxAscUnlocked); RefreshAsc(); }
    void RefreshAsc()
    {
        if (_ascText != null) _ascText.text = ProtoUnlocks.AscName(_pAsc);
    }

    void ChangeSpeed()
    {
        float s = _main.GameSpeed >= 2f ? 1f : _main.GameSpeed >= 1.5f ? 2f : 1.5f; // 1.0→1.5→2.0→1.0
        _main.SetGameSpeed(s, save: false);
        RefreshSettingsView();
    }

    // 『設定を保存』：現在のプレビュー値をすべて確定
    void SaveSettings()
    {
        PlayerPrefs.SetFloat("volume", AudioListener.volume);
        PlayerPrefs.SetInt("bgm", _main.BgmEnabled ? 1 : 0);
        PlayerPrefs.SetFloat("gamespeed", _main.GameSpeed);
        ProtoUnlocks.Ascension = _pAsc;
        PlayerPrefs.Save();
        _notice.text = "設定を保存しました！";
    }
}
