using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// 本設バトル: 単一キャラ(MAMA) vs 敵。マナ制カード戦闘。
// 毎ターン手札を配り、マナ予算内でカードを使用→「ターン終了」で敵の行動。
// 点滅順番当ては効果（BlinkOnUse / 直前のPrimeNextAttackBlink）でのみ発動。
public class ProtoBattle : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;

    // UI
    TextMeshProUGUI _enemyName, _enemyHPText, _message, _manaText, _resultText, _resultSub, _statusText;
    Image _enemyFill, _pFill;
    TextMeshProUGUI _pHpText;
    RectTransform _handArea;
    readonly List<CardDef> _pendingDrawn = new List<CardDef>();
    readonly List<RectTransform> _cardRects = new List<RectTransform>();
    RectTransform _boardOverlay;   // カード選択時に中央へ出す盤面プレビュー（枠＋背景）
    RectTransform _boardContent;   // 盤面プレビューの動的中身（マス・文字）
    int _selectedIndex = -1;
    bool _dead;                    // HP0で倒れた状態（以降アニメ停止）
    Coroutine _glowCo;
    readonly List<Image> _glowImgs = new List<Image>();
    readonly List<Color> _glowBase = new List<Color>();
    RectTransform _challengeRoot, _pieceArea;
    TextMeshProUGUI _challengePrompt;
    Image _timerFill;
    RectTransform _resultRoot, _rewardArea;
    Button _endTurnBtn;

    const float GaugeWidth = 320f;
    const float GroundY = -160f;
    const float BaseFlashOn = 0.45f, BaseFlashGap = 0.18f; // ×blinkTimeScale

    // 状態
    int _playerHP, _playerMaxHP;
    int _enemyHP, _enemyMaxHP, _effWave;
    EnemyDef _enemy;
    List<CardDef> _hand = new List<CardDef>();
    int _mana, _manaBoostNext;
    bool _inputLocked;

    // 戦闘中の一時ステータス
    int _block;          // 被ダメージを肩代わり（自ターン開始でリセット）
    int _strength;       // 攻撃力上昇（戦闘中持続）
    int _protectPct;     // 次の被弾を%軽減（1回）
    int _weakPct, _weakTurns; // 敵の攻撃力低下
    int _poison;         // 敵への毒
    bool _primeBlink;    // 次のアタックで点滅

    AudioSource _sfx;
    AudioClip[] _hitClips;
    AudioClip _swingClip;

    Image _actorImg, _slimeImg, _faceImg;
    RectTransform _actorRt, _slimeRt, _enemyInner, _playerInner, _enemyShadow;

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        _sfx = gameObject.AddComponent<AudioSource>();
        _hitClips = new[] { ProtoAudio.CreateHitClip(0), ProtoAudio.CreateHitClip(1), ProtoAudio.CreateHitClip(2), ProtoAudio.CreateHitClip(3) };
        _swingClip = ProtoAudio.CreateSwing();
        Hide();
    }

    public void Hide()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        _inputLocked = false;
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ==================== 開始 ====================

    public void Begin(EnemyDef enemy)
    {
        _enemy = enemy;
        _root.gameObject.SetActive(true);
        _resultRoot.gameObject.SetActive(false);
        _challengeRoot.gameObject.SetActive(false);

        // キャラ立ち絵・顔・位置サイズを通常に戻す（前回の倒れ絵/被弾絵をリセット）
        _dead = false;
        if (_actorImg != null) { _actorImg.sprite = ProtoPixelArt.MamaPhoto(); _actorImg.color = Color.white; }
        if (_faceImg != null) _faceImg.sprite = ProtoPixelArt.FrontMama();
        if (_actorRt != null) { _actorRt.anchoredPosition = new Vector2(-470, GroundY + 205f); _actorRt.sizeDelta = new Vector2(295, 375); _actorRt.localRotation = Quaternion.identity; _actorRt.localScale = Vector3.one; }
        if (_playerInner != null) _playerInner.sizeDelta = new Vector2(295, 375);

        // 残っているGAME OVERオーバーレイがあれば消す
        var oldGo = _root.Find("GameOver");
        if (oldGo != null) Destroy(oldGo.gameObject);

        _playerMaxHP = _main.Stats.MaxHP;
        _playerHP = _playerMaxHP;
        _block = 0; _strength = 0; _protectPct = 0; _weakPct = 0; _weakTurns = 0; _poison = 0;
        _manaBoostNext = 0; _primeBlink = false;

        _effWave = _main.Wave + enemy.levelOffset;
        _enemyMaxHP = enemy.baseHP + 40 * (_effWave - 1);
        _enemyHP = _enemyMaxHP;
        _enemyName.text = $"{enemy.enemyName} Lv{_effWave}";

        _slimeImg.sprite = enemy.BattleSprite();
        _slimeRt.sizeDelta = enemy.battleSize;
        _enemyInner.sizeDelta = enemy.battleSize;
        _slimeRt.anchoredPosition = enemy.flying ? new Vector2(460, 70) : new Vector2(460, GroundY + enemy.battleSize.y / 2f);

        if (_enemyShadow != null) Destroy(_enemyShadow.gameObject);
        _enemyShadow = null;
        if (!enemy.flying) { AddGroundShadow(_slimeRt, enemy.battleSize.x * 0.5f); _enemyShadow = (RectTransform)_slimeRt.GetChild(0); }

        StartPlayerTurn(firstTurn: true);
    }

    void StartPlayerTurn(bool firstTurn = false)
    {
        _block = 0; // ブロックは自ターン開始でリセット
        _mana = _main.MaxMana + _manaBoostNext;
        _manaBoostNext = 0;

        // 手札を配り直す（盤面の出現確率でサンプリング）
        DealHand();

        _inputLocked = false;
        RefreshAll(dealAnimation: true);
        _message.text = firstTurn ? "戦闘開始！カードを選ぼう！" : "あなたのターン！";
    }

    void DealHand()
    {
        _hand.Clear();
        var deck = _main.Panel.BuildDeck();
        int n = _main.Cfg != null ? _main.Cfg.handSize : 5;
        for (int i = 0; i < n; i++)
        {
            CardDef c = deck.Count > 0 ? deck[Random.Range(0, deck.Count)] : null;
            _hand.Add(c ?? _main.Db.normalAttack);
        }
    }

    // ==================== UI構築 ====================

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("BattleScreen", _main.Canvas.transform);

        // バトル専用の背景画像（全画面）。最背面に敷く
        var bgRt = ProtoUI.CreateFullScreen("BattleBG", _root);
        var bgImg = bgRt.gameObject.AddComponent<Image>();
        bgImg.sprite = ProtoPixelArt.BattleBackground();
        bgImg.raycastTarget = false;
        bgRt.SetAsFirstSibling();

        ProtoUI.CreatePanel("BattleShade", _root, new Vector2(0, -382), new Vector2(1700, 260), new Color(0.015f, 0.018f, 0.028f, 0.45f)).raycastTarget = false;
        ProtoUI.CreatePanel("TopBar", _root, new Vector2(0, 410), new Vector2(1700, 96), ProtoUI.Ink).raycastTarget = false;
        ProtoUI.CreatePanel("TopBarLine", _root, new Vector2(0, 356), new Vector2(1700, 2), new Color(0.95f, 0.78f, 0.36f, 0.62f)).raycastTarget = false;

        // プレイヤーHUD（左上）
        var pName = ProtoUI.CreateText("PName", _root, "MAMA", 27, new Vector2(-470, 416), new Vector2(320, 38), new Color(0.9f, 0.92f, 1f));
        ProtoUI.StyleTitle(pName, new Color(0.9f, 0.92f, 1f), 3f);
        ProtoUI.CreateGauge("PGauge", _root, new Vector2(-470, 382), new Vector2(GaugeWidth, 18),
            new Color(0.08f, 0.07f, 0.11f, 0.92f), new Color(0.72f, 0.36f, 0.95f), out _pFill);
        _pHpText = ProtoUI.CreateText("PHP", _root, "", 16, new Vector2(-470, 382), new Vector2(300, 24));
        _pHpText.fontStyle = FontStyles.Bold;

        // ステータス（プレイヤーHUD下・左上）
        _statusText = ProtoUI.CreateText("Status", _root, "", 20, new Vector2(-460, 346), new Vector2(560, 28), new Color(0.8f, 0.9f, 0.8f));
        _statusText.alignment = TextAlignmentOptions.Left;

        // マナを移動して空いた左上スペースに顔アイコン（画像をそのまま表示）
        var faceRt = ProtoUI.CreateRect("FaceIcon", _root);
        faceRt.anchoredPosition = new Vector2(-690, 410);
        faceRt.sizeDelta = new Vector2(84, 84);
        _faceImg = faceRt.gameObject.AddComponent<Image>();
        _faceImg.sprite = ProtoPixelArt.FrontMama(); _faceImg.preserveAspect = true; _faceImg.raycastTarget = false;

        // マナ（左下・手札カードの横に配置）
        var manaBg = ProtoUI.CreateFramedPanel("ManaBadge", _root, new Vector2(-665, -298), new Vector2(164, 84), new Color(0.035f, 0.105f, 0.22f, 0.96f), new Color(0.38f, 0.78f, 1f, 0.8f));
        manaBg.raycastTarget = false;
        var manaLabel = ProtoUI.CreateText("ManaLabel", manaBg.transform, "マナ", 18, new Vector2(0, 24), new Vector2(150, 22), new Color(0.6f, 0.85f, 1f));
        ProtoUI.StyleTitle(manaLabel, new Color(0.6f, 0.85f, 1f), 4f);
        _manaText = ProtoUI.CreateText("Mana", manaBg.transform, "", 36, new Vector2(0, -12), new Vector2(150, 44), new Color(0.88f, 0.96f, 1f));
        ProtoUI.StyleTitle(_manaText, new Color(0.88f, 0.96f, 1f), 3f);
        _manaText.outlineWidth = 0.3f; _manaText.outlineColor = new Color32(8, 24, 56, 255);

        // 敵HUD（右上）
        _enemyName = ProtoUI.CreateText("EName", _root, "", 27, new Vector2(560, 416), new Vector2(320, 38), new Color(1f, 0.55f, 0.48f));
        ProtoUI.StyleTitle(_enemyName, new Color(1f, 0.55f, 0.5f), 3f);
        ProtoUI.CreateGauge("EGauge", _root, new Vector2(560, 382), new Vector2(GaugeWidth, 18),
            new Color(0.08f, 0.07f, 0.11f, 0.92f), new Color(0.95f, 0.27f, 0.27f), out _enemyFill);
        _enemyHPText = ProtoUI.CreateText("EHP", _root, "", 16, new Vector2(560, 382), new Vector2(300, 24));
        _enemyHPText.fontStyle = FontStyles.Bold;

        // キャラ
        _slimeImg = CreateCharacterSprite("EnemySprite", ProtoPixelArt.Dragon(), new Vector2(460, 70), new Vector2(540, 355));
        _slimeRt = (RectTransform)_slimeImg.transform.parent;
        _enemyInner = (RectTransform)_slimeImg.transform;

        _actorImg = CreateCharacterSprite("Player", ProtoPixelArt.MamaPhoto(), new Vector2(-470, GroundY + 205f), new Vector2(295, 375));
        _actorRt = (RectTransform)_actorImg.transform.parent;
        _playerInner = (RectTransform)_actorImg.transform;
        AddGroundShadow(_actorRt, 150f);

        // メッセージ
        var msgBox = ProtoUI.CreateFramedPanel("MsgBox", _root, new Vector2(0, 302), new Vector2(980, 52), new Color(0.035f, 0.04f, 0.06f, 0.92f), new Color(0.65f, 0.55f, 0.36f, 0.62f));
        _message = ProtoUI.CreateText("Msg", msgBox.transform, "", 22, Vector2.zero, new Vector2(940, 50));

        // 手札
        _handArea = ProtoUI.CreateRect("Hand", _root);
        _handArea.anchoredPosition = new Vector2(-64, -302);
        _handArea.sizeDelta = new Vector2(1500, 240);

        // カード選択時に中央へ出す盤面プレビュー：外周だけ金色、内側は不透明パネル（背景と同化しない）
        _boardOverlay = ProtoUI.CreateRect("BoardOverlay", _root);
        _boardOverlay.anchoredPosition = new Vector2(0, 95);
        _boardOverlay.sizeDelta = new Vector2(330, 330);
        var ovBg = _boardOverlay.gameObject.AddComponent<Image>();
        ovBg.color = new Color(0.9f, 0.78f, 0.42f, 0.97f); ovBg.raycastTarget = false; // 外周の金枠（細）
        var ovInner = ProtoUI.CreatePanel("OverlayInner", _boardOverlay, Vector2.zero, new Vector2(322, 322), new Color(0.07f, 0.08f, 0.13f, 0.98f));
        ovInner.raycastTarget = false; // 不透明の内側（背景が透けない・金は外周だけ）
        _boardContent = ProtoUI.CreateRect("BoardContent", _boardOverlay); // 動的中身（毎回作り直す）
        _boardContent.anchoredPosition = Vector2.zero; _boardContent.sizeDelta = new Vector2(330, 330);
        _boardOverlay.gameObject.SetActive(false);

        // 手札を最前面に（カードを上にドラッグしたとき盤面より前＝表で見える）
        _handArea.SetAsLastSibling();

        // ターン終了ボタン（大きめ・太い外枠）＋ 逃げるをその下に配置
        ProtoUI.CreatePanel("EndTurnBorder", _root, new Vector2(632, -262), new Vector2(224, 114), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        _endTurnBtn = ProtoUI.CreateButton("EndTurn", _root, "ターン終了", 24, new Vector2(632, -262), new Vector2(212, 102),
            new Color(0.38f, 0.13f, 0.12f, 0.96f), OnEndTurn);
        ProtoUI.CreatePanel("RetreatBorder", _root, new Vector2(632, -374), new Vector2(224, 80), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreateButton("RetreatBtn", _root, "逃げる", 20, new Vector2(632, -374), new Vector2(212, 68),
            new Color(0.16f, 0.14f, 0.18f, 0.96f), () => _main.ShowMap());

        // 点滅チャレンジ
        _challengeRoot = ProtoUI.CreateFullScreen("Challenge", _root);
        _challengeRoot.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.75f);
        _challengePrompt = ProtoUI.CreateText("CPrompt", _challengeRoot, "", 30, new Vector2(0, 260), new Vector2(1000, 50));
        _pieceArea = ProtoUI.CreateRect("PieceArea", _challengeRoot);
        _pieceArea.anchoredPosition = new Vector2(0, 30);
        ProtoUI.CreateGauge("Timer", _challengeRoot, new Vector2(0, -290), new Vector2(500, 14),
            new Color(0.2f, 0.18f, 0.28f), new Color(1f, 0.85f, 0.3f), out _timerFill);

        // 結果＋報酬
        _resultRoot = ProtoUI.CreateFullScreen("Result", _root);
        _resultRoot.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f); // 元画面を少し暗く
        _resultText = ProtoUI.CreateText("RText", _resultRoot, "", 54, new Vector2(0, 300), new Vector2(900, 80));
        ProtoUI.StyleTitle(_resultText, ProtoUI.Gold, 8f);
        _resultSub = ProtoUI.CreateText("RSub", _resultRoot, "", 22, new Vector2(0, 235), new Vector2(900, 50), new Color(0.9f, 0.95f, 1f));
        _rewardArea = ProtoUI.CreateRect("RewardArea", _resultRoot);
        _rewardArea.anchoredPosition = new Vector2(0, 20);
    }

    // ==================== 表示 ====================

    void RefreshAll(bool dealAnimation = false)
    {
        ProtoUI.SetGauge(_pFill, _playerHP / (float)_playerMaxHP, GaugeWidth);
        _pHpText.text = $"HP {_playerHP}/{_playerMaxHP}";
        ProtoUI.SetGauge(_enemyFill, _enemyHP / (float)_enemyMaxHP, GaugeWidth);
        _enemyHPText.text = $"{_enemyHP}/{_enemyMaxHP}";
        RefreshMana();

        var st = new List<string>();
        if (_block > 0) st.Add($"<color=#7FB0FF>ブロック{_block}</color>");
        if (_strength > 0) st.Add($"<color=#FF9060>筋力+{_strength}</color>");
        if (_protectPct > 0) st.Add($"<color=#90C0FF>軽減{_protectPct}%</color>");
        if (_primeBlink) st.Add("<color=#FFD040>点滅構え</color>");
        if (_weakTurns > 0) st.Add($"<color=#C080FF>敵弱体{_weakTurns}T</color>");
        if (_poison > 0) st.Add($"<color=#80FF60>敵毒{_poison}</color>");
        _statusText.text = string.Join("  ", st);

        RefreshHand(dealAnimation);
    }

    void RefreshHand(bool dealAnimation = false)
    {
        HideBoardOverlay();
        foreach (Transform c in _handArea) Destroy(c.gameObject);
        _cardRects.Clear();
        int n = _hand.Count;
        float cardW = 200f;
        float startX = -(n - 1) * cardW / 2f;
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            Vector2 pos = new Vector2(startX + i * cardW, 0);
            bool affordable = !_inputLocked && _mana >= _hand[i].ManaCost;
            var btn = CreateCardUI(_hand[i], pos, null, affordable);
            var crt = (RectTransform)btn.transform;
            _cardRects.Add(crt);
            if (affordable)
            {
                var slide = btn.gameObject.AddComponent<CardSlide>();
                slide.Setup(crt, pos, () => SelectCard(idx), () => TryPlayCard(idx));
            }
            if (dealAnimation) StartCoroutine(DealCard(crt, pos, i * 0.06f));
        }
    }

    // カードをクリック＝選択：中央に盤面を表示し、対応マスを光らせる
    void SelectCard(int index)
    {
        if (_inputLocked || index < 0 || index >= _hand.Count) return;
        _selectedIndex = index;
        // 選択カードを少し上げて拡大、ほかは元に戻す
        for (int i = 0; i < _cardRects.Count; i++)
        {
            var rt = _cardRects[i];
            if (rt == null) continue;
            bool sel = i == index;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, sel ? 44f : 0f);
            rt.localScale = sel ? Vector3.one * 1.08f : Vector3.one;
            rt.SetAsLastSibling();
        }
        if (index < _cardRects.Count && _cardRects[index] != null) _cardRects[index].SetAsLastSibling();
        // 種別＋マス数を画面上部のメッセージに表示
        var sc = _hand[index];
        _message.text = $"{sc.displayName}　{(sc.kind == CardKind.Attack ? "攻撃" : "スキル")} {sc.Size}マス";
        ShowBoardForCard(sc, index);
    }

    // カードを上にスライド＝発動
    void TryPlayCard(int index)
    {
        if (_inputLocked || index < 0 || index >= _hand.Count) return;
        var card = _hand[index];
        if (_mana < card.ManaCost) { _message.text = "マナが足りない！"; return; }
        HideBoardOverlay();
        _selectedIndex = -1;
        _inputLocked = true;
        var rt = index < _cardRects.Count ? _cardRects[index] : null;
        StartCoroutine(PlayWithDisappear(index, rt));
    }

    IEnumerator PlayWithDisappear(int index, RectTransform rt)
    {
        if (rt != null) yield return CardDisappear(rt);
        RefreshHand();
        yield return PlayCard(index);
    }

    // カードを上に消える演出（上昇＋縮小＋フェード）
    IEnumerator CardDisappear(RectTransform rt)
    {
        if (rt == null) yield break;
        var cg = rt.GetComponent<CanvasGroup>();
        if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
        Vector2 start = rt.anchoredPosition;
        float startScale = rt.localScale.x;
        float t = 0f; const float dur = 0.24f;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / dur);
            rt.anchoredPosition = start + new Vector2(0, 150f * p);
            rt.localScale = Vector3.one * Mathf.Lerp(startScale, 0.25f, p);
            cg.alpha = 1f - p;
            yield return null;
        }
    }

    void HideBoardOverlay()
    {
        if (_glowCo != null) { StopCoroutine(_glowCo); _glowCo = null; }
        _glowImgs.Clear(); _glowBase.Clear();
        if (_boardOverlay == null) return;
        if (_boardContent != null) foreach (Transform c in _boardContent) Destroy(c.gameObject);
        _boardOverlay.gameObject.SetActive(false);
    }

    // 配置マスを発光させるパルス
    IEnumerator GlowLoop()
    {
        while (true)
        {
            float t = Mathf.PingPong(Time.unscaledTime * 2.2f, 1f);
            for (int i = 0; i < _glowImgs.Count; i++)
            {
                if (_glowImgs[i] == null) continue;
                _glowImgs[i].color = Color.Lerp(_glowBase[i], Color.white, 0.15f + 0.55f * t);
            }
            yield return null;
        }
    }

    // 選択中カードに対応するマスを盤面上で発光表示
    void ShowBoardForCard(CardDef card, int handIndex)
    {
        if (_boardOverlay == null) return;
        foreach (Transform c in _boardContent) Destroy(c.gameObject);
        _boardOverlay.gameObject.SetActive(true);

        var panel = _main.Panel;
        int W = panel.W, H = panel.H;
        float area = 250f; // マス数が増えるほど1マスは自動的に小さくなる（最大10×10想定）
        float cell = Mathf.Min(area / W, area / H);
        float ox = -(W - 1) * cell / 2f, oy = (H - 1) * cell / 2f;

        // 手札内で同じカードが複数ある場合、その「何番目か」に対応する1配置だけを光らせる
        var matches = panel.Placements.Where(pp => pp.card == card).ToList();
        int rank = 0;
        for (int i = 0; i < handIndex && i < _hand.Count; i++)
            if (_hand[i] == card) rank++;

        var cellColor = new Dictionary<Vector2Int, Color>();
        Color hi = new Color(1f, 0.86f, 0.28f, 0.97f);
        if (matches.Count > 0)
            foreach (var c in matches[rank % matches.Count].cells)
                cellColor[c] = hi;

        ProtoUI.CreateText("OVTitle", _boardContent, "選択カードの配置マス", 18,
            new Vector2(0, 150), new Vector2(330, 26), Color.white);

        _glowImgs.Clear(); _glowBase.Clear();
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                var pl = panel.GetAt(x, y);
                bool match = cellColor.TryGetValue(new Vector2Int(x, y), out var hc);
                Color col = match ? hc
                          : pl != null ? new Color(0.30f, 0.32f, 0.42f, 0.7f)
                                       : new Color(0.10f, 0.12f, 0.18f, 0.55f);
                var p = ProtoUI.CreatePanel($"BC_{x}_{y}", _boardContent,
                    new Vector2(ox + x * cell, oy - y * cell), new Vector2(cell - 3, cell - 3), col);
                p.raycastTarget = false;
                if (match) { _glowImgs.Add(p); _glowBase.Add(hc); }   // 発光対象
            }

        if (_glowCo != null) StopCoroutine(_glowCo);
        if (_glowImgs.Count > 0) _glowCo = StartCoroutine(GlowLoop());

        ProtoUI.CreateText("OVHint", _boardContent, "↑ カードを上にスライドで発動", 15,
            new Vector2(0, -150), new Vector2(330, 24), new Color(0.85f, 0.92f, 1f));
    }

    void RefreshMana()
    {
        _manaText.text = $"{_mana}/{_main.MaxMana}";
    }

    Button CreateCardUI(CardDef card, Vector2 pos, System.Action onClick, bool affordable)
    {
        Color accent = card.color;
        var frame = ProtoUI.CreatePanel("Card", _handArea, pos, new Vector2(190, 262),
            affordable ? Color.Lerp(accent, ProtoUI.Border, 0.45f) : new Color(0.24f, 0.24f, 0.27f, 0.92f));
        var btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        btn.interactable = affordable;

        var inner = ProtoUI.CreatePanel("Inner", frame.transform, Vector2.zero, new Vector2(180, 252), affordable ? new Color(0.045f, 0.055f, 0.075f, 0.98f) : new Color(0.065f, 0.06f, 0.07f, 0.92f));
        inner.raycastTarget = false;
        ProtoUI.AddPanelTrim(inner, new Vector2(180, 252), Color.Lerp(accent, Color.black, 0.35f), new Color(1f, 1f, 1f, 0.06f));

        var accentLine = ProtoUI.CreatePanel("AccentLine", inner.transform, new Vector2(0, 123), new Vector2(168, 5), affordable ? accent : new Color(0.30f, 0.30f, 0.34f));
        accentLine.raycastTarget = false;
        var header = ProtoUI.CreatePanel("Header", inner.transform, new Vector2(0, 101), new Vector2(168, 38), Color.Lerp(accent, Color.black, affordable ? 0.72f : 0.86f));
        header.raycastTarget = false;
        // カード名：コストバッジの右側に左詰め、文字数に応じて枠内に収まるよう自動縮小
        var nameText = ProtoUI.CreateText("Name", header.transform, card.displayName, 18, new Vector2(18, 0), new Vector2(126, 32));
        nameText.alignment = TextAlignmentOptions.Left;
        nameText.fontStyle = FontStyles.Bold;
        nameText.enableWordWrapping = false;
        nameText.overflowMode = TextOverflowModes.Overflow;
        nameText.enableAutoSizing = true; nameText.fontSizeMin = 8; nameText.fontSizeMax = 19;

        // マナコストバッジ（左上）
        var manaBadge = ProtoUI.CreatePanel("Mana", inner.transform, new Vector2(-72, 101), new Vector2(34, 34), new Color(0.08f, 0.27f, 0.68f, 0.96f));
        manaBadge.raycastTarget = false;
        ProtoUI.CreateText("M", manaBadge.transform, card.ManaCost.ToString(), 20, Vector2.zero, new Vector2(34, 34)).fontStyle = FontStyles.Bold;

        // アート（形状）
        var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 4), new Vector2(160, 94), new Color(0.018f, 0.024f, 0.034f, 0.92f));
        art.raycastTarget = false;
        var shape = card.Shape;
        float cs = 14f, gap = 2f;
        int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y), maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.CreatePanel("Mas", art.transform, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.color).raycastTarget = false;

        // 効果説明：マス背景（形状アート）のすぐ下に配置
        var footer = ProtoUI.CreatePanel("Footer", inner.transform, new Vector2(0, -76), new Vector2(168, 64), new Color(0.075f, 0.08f, 0.105f, 0.94f));
        footer.raycastTarget = false;
        string footText = card.kind == CardKind.Attack
            ? (card.HasEffect(CardEffectType.BlinkOnUse) ? $"威力{card.power}・点滅" : $"威力 {card.power}")
            : (string.IsNullOrEmpty(card.description) ? "" : card.description);
        var ft = ProtoUI.CreateText("FT", footer.transform, footText, 13, Vector2.zero, new Vector2(160, 60), ProtoUI.Gold);
        ft.enableAutoSizing = true; ft.fontSizeMin = 9; ft.fontSizeMax = 14;

        return btn;
    }

    // ==================== ターン進行 ====================

    void OnCardClicked(int index)
    {
        if (_inputLocked || index >= _hand.Count) return;
        var card = _hand[index];
        if (_mana < card.ManaCost) { _message.text = "マナが足りない！"; return; }
        _inputLocked = true;
        RefreshHand();
        StartCoroutine(PlayCard(index));
    }

    IEnumerator PlayCard(int index)
    {
        var card = _hand[index];
        _hand.RemoveAt(index);
        _mana -= card.ManaCost;

        if (card.kind == CardKind.Skill)
            yield return ResolveSkill(card);
        else
            yield return ResolveAttack(card);

        RefreshAll();

        if (_enemyHP <= 0) { yield return Victory(); yield break; }

        if (_pendingDrawn.Count > 0)
        {
            RefreshHand();
            yield return RevealDrawnCards();
        }

        _inputLocked = false;
        RefreshHand();

        // 使えるカードが無くなったら自動でターン終了
        if (!HasAffordableCard())
        {
            _inputLocked = true;
            RefreshHand();
            _message.text = _hand.Count == 0 ? "手札がない。ターンを終了します…" : "マナ切れ。ターンを終了します…";
            yield return new WaitForSeconds(0.8f);
            _inputLocked = false;
            OnEndTurn();
        }
    }

    bool HasAffordableCard()
    {
        foreach (var c in _hand)
            if (_mana >= c.ManaCost) return true;
        return false;
    }

    IEnumerator ResolveAttack(CardDef card)
    {
        bool blink = card.HasEffect(CardEffectType.BlinkOnUse) || _primeBlink;
        _primeBlink = false;

        float mult = 1f;
        if (blink) { yield return RunChallenge(card); mult = _challengeMultiplier; }
        else { _message.text = $"{card.displayName}！"; yield return new WaitForSeconds(0.3f); }

        int dmg = Mathf.RoundToInt((card.power + _main.Stats.Attack + _strength) * mult);
        yield return AttackMotionFor(card);
        yield return Impact(_slimeRt, _slimeImg, card, dmg, mult);
        _enemyHP = Mathf.Max(0, _enemyHP - dmg);
        _message.text = $"{(mult > 1.01f ? "会心！" : "")}{card.displayName}で {dmg} ダメージ！";

        // アタックに付随する他効果（あれば）
        ApplyCardEffects(card, attackContext: true);
        yield return new WaitForSeconds(0.6f);
    }

    IEnumerator ResolveSkill(CardDef card)
    {
        _message.text = $"{card.displayName}！";
        yield return Pulse(_actorRt, 1.1f, 0.2f);
        StartCoroutine(FlashSprite(_actorImg, Color.Lerp(card.color, Color.white, 0.4f)));
        SpawnBurst(_actorRt.anchoredPosition, card.color, 10, 90f);
        ApplyCardEffects(card, attackContext: false);
        yield return new WaitForSeconds(0.5f);
    }

    void ApplyCardEffects(CardDef card, bool attackContext)
    {
        if (card.effects == null) return;
        foreach (var e in card.effects)
        {
            switch (e.type)
            {
                case CardEffectType.Draw:
                    var deck = _main.Panel.BuildDeck();
                    for (int i = 0; i < e.amount; i++)
                    {
                        var drawn = (deck.Count > 0 ? deck[Random.Range(0, deck.Count)] : null) ?? _main.Db.normalAttack;
                        _hand.Add(drawn);
                        _pendingDrawn.Add(drawn);
                    }
                    break;
                case CardEffectType.Block: _block += e.amount; break;
                case CardEffectType.Protect: _protectPct = Mathf.Max(_protectPct, e.amount); break;
                case CardEffectType.ManaBoostNextTurn: _manaBoostNext += e.amount; break;
                case CardEffectType.Strength: _strength += e.amount; break;
                case CardEffectType.Heal: _playerHP = Mathf.Min(_playerMaxHP, _playerHP + e.amount); break;
                case CardEffectType.Weak: _weakPct = e.amount; _weakTurns = Mathf.Max(_weakTurns, e.duration); break;
                case CardEffectType.Poison: _poison += e.amount; break;
                case CardEffectType.PrimeNextAttackBlink: _primeBlink = true; break;
                case CardEffectType.BlinkOnUse: break; // ResolveAttackで処理済み
            }
        }
    }

    void OnEndTurn()
    {
        if (_inputLocked) return;
        _inputLocked = true;
        RefreshHand();
        StartCoroutine(EnemyTurn());
    }

    IEnumerator EnemyTurn()
    {
        // 毒の処理
        if (_poison > 0)
        {
            _message.text = $"毒！敵に {_poison} ダメージ";
            yield return Impact(_slimeRt, _slimeImg, null, _poison, 1f, 0);
            _enemyHP = Mathf.Max(0, _enemyHP - _poison);
            RefreshAll();
            yield return new WaitForSeconds(0.5f);
            if (_enemyHP <= 0) { yield return Victory(); yield break; }
        }

        _message.text = $"{_enemy.enemyName}のターン…";
        yield return new WaitForSeconds(0.7f);
        yield return EnemyAttackSequence();

        if (_weakTurns > 0) _weakTurns--;

        if (_playerHP <= 0) { yield return Defeat(); yield break; }

        StartPlayerTurn();
    }

    // HP0：足元に倒れる → 画面を少し暗くして GAME OVER（専用オーバーレイを最前面に生成）
    IEnumerator Defeat()
    {
        _dead = true;
        _inputLocked = true;
        HideBoardOverlay();

        // 倒れ絵を元キャラの足元あたりに配置（以降は動かさない）
        if (_actorImg != null) { _actorImg.sprite = ProtoPixelArt.DownMama(); _actorImg.color = Color.white; }
        if (_faceImg != null) _faceImg.sprite = ProtoPixelArt.DamageMama();
        if (_actorRt != null) { _actorRt.anchoredPosition = new Vector2(-470, GroundY + 6f); _actorRt.sizeDelta = new Vector2(320, 130); _actorRt.localRotation = Quaternion.identity; _actorRt.localScale = Vector3.one; }
        if (_playerInner != null) _playerInner.sizeDelta = new Vector2(320, 130);
        _message.text = "";
        yield return new WaitForSeconds(0.8f);

        // GAME OVER オーバーレイ（元画面を少し暗く＋中央に文字＋選択肢）
        var go = ProtoUI.CreateFullScreen("GameOver", _root);
        var bg = go.gameObject.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.74f);
        go.SetAsLastSibling();

        var t = ProtoUI.CreateText("GOText", go, "GAME OVER", 64, new Vector2(0, 180), new Vector2(900, 100), new Color(1f, 0.36f, 0.36f));
        ProtoUI.StyleTitle(t, new Color(1f, 0.4f, 0.4f), 8f);
        ProtoUI.CreateText("GOSub", go, "MAMAは倒れてしまった…", 24, new Vector2(0, 110), new Vector2(900, 40), new Color(0.92f, 0.92f, 1f));
        ProtoUI.CreateButton("Retry", go, "もう一度やり直す", 24, new Vector2(0, 30), new Vector2(330, 68),
            new Color(0.30f, 0.45f, 0.32f), () => _main.RestartRun());
        ProtoUI.CreateButton("Quit", go, "ゲームを終了する", 22, new Vector2(0, -60), new Vector2(330, 62),
            new Color(0.45f, 0.25f, 0.25f), () => { /* ここでは何もしない */ });
    }

    IEnumerator EnemyAttackSequence()
    {
        var atk = _enemy.PickAttack();
        _message.text = $"{_enemy.enemyName}の {atk.name}！";
        yield return new WaitForSeconds(0.4f);

        if (atk.hits == 0)
        {
            yield return Pulse(_slimeRt, 1.08f, 0.25f);
            _message.text = "しかし何も起こらなかった！";
            yield return new WaitForSeconds(0.6f);
            yield break;
        }

        _sfx.PlayOneShot(_swingClip);
        yield return Lunge(_slimeRt, new Vector2(-150, 0));
        int sfxTier = atk.mult >= 1.5f ? 2 : atk.hits > 1 ? 0 : 1;

        for (int h = 0; h < atk.hits; h++)
        {
            int raw = Mathf.RoundToInt(Random.Range(_enemy.minAtk, _enemy.maxAtk + 1) * atk.mult) + 3 * (_effWave - 1);
            if (_weakTurns > 0) raw = Mathf.RoundToInt(raw * (1f - _weakPct / 100f));
            int dmg = Mathf.Max(1, raw);
            if (_protectPct > 0) { dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * (1f - _protectPct / 100f))); _protectPct = 0; }
            if (_block > 0) { int absorb = Mathf.Min(_block, dmg); _block -= absorb; dmg -= absorb; }

            if (dmg <= 0)
            {
                _message.text = "ブロックで防いだ！";
                yield return new WaitForSeconds(0.4f);
            }
            else
            {
                if (_faceImg != null) _faceImg.sprite = ProtoPixelArt.DamageMama(); // 被弾の瞬間だけ顔写真を差し替え
                yield return Impact(_actorRt, _actorImg, null, dmg, 1f, sfxTier);
                _playerHP = Mathf.Max(0, _playerHP - dmg);
                _message.text = atk.hits > 1 ? $"{h + 1}ヒット！{dmg} のダメージ！" : $"{dmg} のダメージ！";
            }
            RefreshAll();
            yield return new WaitForSeconds(0.35f);
            if (_faceImg != null) _faceImg.sprite = ProtoPixelArt.FrontMama(); // 顔写真を通常に戻す
            if (_playerHP <= 0) yield break;
        }
        yield return new WaitForSeconds(0.3f);
    }

    // ==================== 勝敗 ====================

    IEnumerator Victory()
    {
        _inputLocked = true;
        // レイアウトを通常（勝利）配置に戻す
        ((RectTransform)_resultText.transform).anchoredPosition = new Vector2(0, 300);
        _resultText.color = ProtoUI.Gold;
        ((RectTransform)_resultSub.transform).anchoredPosition = new Vector2(0, 235);
        _rewardArea.anchoredPosition = new Vector2(0, 20);
        _main.AddMoney(_enemy.moneyReward);
        _resultText.text = $"{_enemy.enemyName}を倒した！";
        _resultSub.text = $"お金 +{_enemy.moneyReward}　（所持 {_main.Money}）　報酬ピースを1つ選ぼう";
        _resultRoot.gameObject.SetActive(true);
        yield return BuildRewardChoices();
    }

    IEnumerator BuildRewardChoices()
    {
        foreach (Transform c in _rewardArea) Destroy(c.gameObject);

        int count = _main.Cfg != null ? _main.Cfg.rewardChoiceCount : 3;
        var owned = new HashSet<string>(_main.OwnedCardIds);
        var choices = _main.Db.RandomCards(count, owned, _main.CurrentDepth);

        if (choices.Count == 0)
        {
            _resultSub.text = $"お金 +{_enemy.moneyReward}　（所持 {_main.Money}）　獲得できるピースはもう無い";
            ProtoUI.CreateButton("Skip", _rewardArea, "マップへ戻る", 24, new Vector2(0, -180), new Vector2(280, 64),
                new Color(0.35f, 0.3f, 0.55f), () => _main.OnBattleWon());
            yield break;
        }

        float spacing = 300f;
        float startX = -(choices.Count - 1) * spacing / 2f;
        for (int i = 0; i < choices.Count; i++)
            BuildRewardCard(choices[i], new Vector2(startX + i * spacing, 0));

        ProtoUI.CreateText("Hint", _rewardArea, "ピースをクリックで選択して獲得", 18, new Vector2(0, -210), new Vector2(600, 26),
            new Color(0.8f, 0.8f, 0.9f));
        yield break;
    }

    void BuildRewardCard(CardDef card, Vector2 pos)
    {
        var frame = ProtoUI.CreatePanel($"RC_{card.id}", _rewardArea, pos, new Vector2(250, 300), new Color(0.66f, 0.55f, 0.34f));
        var btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        btn.onClick.AddListener(() =>
        {
            _main.AddCard(card.id);
            _main.OnBattleWon();
        });

        var inner = ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(238, 288), new Color(0.10f, 0.08f, 0.16f));
        inner.raycastTarget = false;
        var nm = ProtoUI.CreateText("N", inner.transform, card.displayName, 20, new Vector2(0, 120), new Vector2(230, 30), Color.white);
        nm.fontStyle = FontStyles.Bold;
        ProtoUI.CreateText("K", inner.transform,
            $"{(card.kind == CardKind.Attack ? "攻撃" : "スキル")} / {card.Size}マス / マナ{card.ManaCost}", 14,
            new Vector2(0, 92), new Vector2(230, 22), new Color(0.8f, 0.85f, 1f));

        var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 20), new Vector2(210, 120), new Color(0.05f, 0.04f, 0.10f));
        art.raycastTarget = false;
        var shape = card.Shape;
        float cs = 15f, gap = 2f;
        int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y), maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.CreatePanel("M", art.transform, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.color).raycastTarget = false;

        string eff = card.kind == CardKind.Attack
            ? (card.HasEffect(CardEffectType.BlinkOnUse) ? $"威力{card.power}・使用時に点滅" : $"威力 {card.power}")
            : card.description;
        var d = ProtoUI.CreateText("D", inner.transform, eff, 14, new Vector2(0, -100), new Vector2(220, 70),
            new Color(0.9f, 0.92f, 1f), TextAlignmentOptions.Top);
        d.raycastTarget = false;
    }

    // ==================== 点滅順番当て（効果発動時のみ） ====================

    float _challengeMultiplier;

    IEnumerator RunChallenge(CardDef card)
    {
        // blinkTimeScale は「速度倍率」。2なら2倍速＝点灯時間・間隔は半分。
        float scale = _main.Cfg != null && _main.Cfg.blinkTimeScale > 0.01f ? _main.Cfg.blinkTimeScale : 2f;
        float onTime = BaseFlashOn / scale, gapTime = BaseFlashGap / scale;

        _challengeRoot.gameObject.SetActive(true);
        _challengePrompt.text = $"「{card.displayName}」発動！　光る順番を覚えろ…！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        var shape = card.Shape;
        var cells = new List<Image>();
        var baseColors = new List<Color>();
        float cs = 54f, gap = 5f;
        int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y), maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;

        var taps = new List<int>();
        bool inputOpen = false;

        for (int i = 0; i < shape.Length; i++)
        {
            var v = shape[i];
            Color bc = Color.Lerp(card.color, Color.black, 0.45f);
            var img = ProtoUI.CreatePanel("PCell", _pieceArea,
                new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), bc);
            cells.Add(img); baseColors.Add(bc);
            int idx = i;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { if (inputOpen) { taps.Add(idx); StartCoroutine(TapFeedback(img, baseColors[idx])); } });
        }

        yield return new WaitForSeconds(0.6f);

        // 点灯数 L = 5 + (size-5)について各50%で+1
        int size = shape.Length;
        int L = 5;
        for (int i = 0; i < size - 5; i++) if (Random.value < 0.5f) L++;
        L = Mathf.Clamp(L, 1, size);

        // 重複なしの順番 S
        var pool = Enumerable.Range(0, size).OrderBy(_ => Random.value).ToList();
        var S = pool.Take(L).ToList();

        for (int i = 0; i < L; i++)
        {
            cells[S[i]].color = Color.white;
            yield return new WaitForSeconds(onTime);
            cells[S[i]].color = baseColors[S[i]];
            yield return new WaitForSeconds(gapTime);
        }

        _challengePrompt.text = $"同じ順番で {L} マスをタップ！";
        inputOpen = true;
        float total = 4f + L * 1.0f, remaining = total;
        while (remaining > 0f && taps.Count < L)
        {
            remaining -= Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, remaining / total, 500f);
            yield return null;
        }
        inputOpen = false;

        float ratio = ScoreBlink(taps, S, size);
        _challengeMultiplier = ratio <= 0.8f ? (ratio / 0.8f) : (1f + (ratio - 0.8f) / 0.2f * 0.25f);

        _challengePrompt.text = $"正答率 {Mathf.RoundToInt(ratio * 100)}% → 威力 {Mathf.RoundToInt(_challengeMultiplier * 100)}%";
        yield return new WaitForSeconds(1.0f);
        _challengeRoot.gameObject.SetActive(false);
    }

    // 採点：LCSベース。ratio = LCS(P∩S, S)/L − mistaps/size（0..1）
    float ScoreBlink(List<int> taps, List<int> S, int size)
    {
        if (S.Count == 0) return 1f;
        var sset = new HashSet<int>(S);
        var matched = new HashSet<int>();
        var P = new List<int>();
        int mistaps = 0;
        foreach (var t in taps)
        {
            if (sset.Contains(t) && !matched.Contains(t)) { matched.Add(t); P.Add(t); }
            else mistaps++; // S外、または重複押下
        }
        int lcs = Lcs(P, S);
        float ratio = (float)lcs / S.Count - (float)mistaps / size;
        return Mathf.Clamp01(ratio);
    }

    static int Lcs(List<int> a, List<int> b)
    {
        int n = a.Count, m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Mathf.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[n, m];
    }

    IEnumerator TapFeedback(Image img, Color baseCol)
    {
        img.color = Color.Lerp(baseCol, Color.white, 0.7f);
        yield return new WaitForSeconds(0.15f);
        img.color = baseCol;
    }

    // ==================== アイドル ====================

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        bool esc = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) esc = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!esc && Input.GetKeyDown(KeyCode.Escape)) esc = true;
#endif
        if (esc) { _main.ShowMap(); return; }

        float t = Time.time;
        // 倒れたら主人公は揺らさない
        if (_playerInner != null) _playerInner.anchoredPosition = _dead ? Vector2.zero : new Vector2(0, Mathf.Sin(t * 3f) * 4f);
        if (_enemyInner != null) _enemyInner.anchoredPosition = new Vector2(0, Mathf.Sin(t * 2.1f + 1.7f) * 9f);
    }

    // ==================== 演出ヘルパー（流用） ====================

    Image CreateCharacterSprite(string name, Sprite sprite, Vector2 pos, Vector2 size)
    {
        var holder = ProtoUI.CreateRect(name, _root);
        holder.anchoredPosition = pos; holder.sizeDelta = size;
        var inner = ProtoUI.CreateRect("Sprite", holder);
        inner.sizeDelta = size;
        var img = inner.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.preserveAspect = true;
        return img;
    }

    void AddGroundShadow(RectTransform holder, float width)
    {
        var sh = ProtoUI.CreateRect("Shadow", holder);
        sh.SetAsFirstSibling();
        sh.anchoredPosition = new Vector2(0, -holder.sizeDelta.y / 2f + 8f);
        sh.sizeDelta = new Vector2(width, 26f);
        sh.localRotation = Quaternion.Euler(0, 0, 45);
        sh.localScale = new Vector3(1f, 0.35f, 1f);
        var img = sh.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.3f); img.raycastTarget = false;
    }

    IEnumerator DealCard(RectTransform rt, Vector2 finalPos, float delay)
    {
        Vector2 startPos = finalPos + new Vector2(550, -260);
        rt.anchoredPosition = startPos; rt.localScale = Vector3.one * 0.25f; rt.localRotation = Quaternion.Euler(0, 0, -25f);
        yield return new WaitForSeconds(delay);
        float t = 0f; const float dur = 0.22f;
        while (t < dur)
        {
            t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / dur);
            rt.anchoredPosition = Vector2.Lerp(startPos, finalPos, p);
            rt.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, p);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-25f, 0f, p));
            yield return null;
        }
        rt.anchoredPosition = finalPos; rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
    }

    // カード効果でドローしたカードを目立たせる演出
    IEnumerator RevealDrawnCards()
    {
        int count = _pendingDrawn.Count;
        string names = string.Join("、", _pendingDrawn.ConvertAll(c => c.displayName));
        _pendingDrawn.Clear();
        _message.text = $"<color=#7FE0FF>＋{count}枚ドロー！</color> {names}";

        int total = _cardRects.Count;
        int from = Mathf.Max(0, total - count);
        for (int k = from; k < total; k++)
            StartCoroutine(DrawPop(_cardRects[k], (k - from) * 0.12f));
        yield return new WaitForSeconds(0.45f + count * 0.12f);
    }

    IEnumerator DrawPop(RectTransform rt, float delay)
    {
        if (rt == null) yield break;
        Vector2 finalPos = rt.anchoredPosition;
        Vector2 startPos = finalPos + new Vector2(0, -280);
        rt.anchoredPosition = startPos; rt.localScale = Vector3.one * 0.2f;
        var glow = ProtoUI.CreatePanel("DrawGlow", rt, Vector2.zero, new Vector2(214, 286), new Color(0.5f, 0.9f, 1f, 0f));
        glow.raycastTarget = false; glow.transform.SetAsFirstSibling();
        yield return new WaitForSeconds(delay);
        if (rt == null) yield break;
        float t = 0f; const float dur = 0.3f;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / dur);
            rt.anchoredPosition = Vector2.Lerp(startPos, finalPos, p);
            rt.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.12f, p);
            if (glow != null) glow.color = new Color(0.5f, 0.9f, 1f, 0.6f * (1f - p));
            yield return null;
        }
        t = 0f;
        while (t < 0.1f)
        {
            if (rt == null) yield break;
            t += Time.deltaTime;
            rt.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, t / 0.1f);
            yield return null;
        }
        if (rt != null) { rt.anchoredPosition = finalPos; rt.localScale = Vector3.one; }
        if (glow != null) Destroy(glow.gameObject);
    }

    IEnumerator Lunge(RectTransform rt, Vector2 dir)
    {
        Vector2 origin = rt.anchoredPosition; Vector3 baseScale = rt.localScale;
        float tiltSign = dir.x >= 0 ? -1f : 1f;
        float t = 0f;
        while (t < 0.16f) { t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / 0.16f); rt.anchoredPosition = Vector2.Lerp(origin, origin - dir * 0.3f, p); rt.localScale = Vector3.Lerp(baseScale, new Vector3(baseScale.x * 1.06f, baseScale.y * 0.88f, 1f), p); yield return null; }
        Vector2 windup = rt.anchoredPosition; t = 0f;
        while (t < 0.07f) { t += Time.deltaTime; float p = t / 0.07f; rt.anchoredPosition = Vector2.Lerp(windup, origin + dir, p * p); rt.localScale = Vector3.Lerp(rt.localScale, new Vector3(baseScale.x * 0.94f, baseScale.y * 1.08f, 1f), p); rt.localRotation = Quaternion.Euler(0, 0, tiltSign * 10f * p); yield return null; }
        yield return new WaitForSeconds(0.07f);
        Vector2 hitPos = rt.anchoredPosition; t = 0f;
        while (t < 0.22f) { t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / 0.22f); rt.anchoredPosition = Vector2.Lerp(hitPos, origin, p); rt.localScale = Vector3.Lerp(rt.localScale, baseScale, p); rt.localRotation = Quaternion.Euler(0, 0, tiltSign * 10f * (1f - p)); yield return null; }
        rt.anchoredPosition = origin; rt.localScale = baseScale; rt.localRotation = Quaternion.identity;
    }

    IEnumerator Shake(RectTransform rt, float amp, float duration)
    {
        Vector2 origin = rt.anchoredPosition; float t = 0f;
        while (t < duration) { t += Time.deltaTime; float decay = 1f - t / duration; rt.anchoredPosition = origin + Random.insideUnitCircle * amp * decay; yield return null; }
        rt.anchoredPosition = origin;
    }

    IEnumerator FlashSprite(Image img, Color flashColor)
    {
        Color original = img.color; img.color = flashColor;
        yield return new WaitForSeconds(0.12f); img.color = original;
    }

    void SpawnBurst(Vector2 pos, Color color, int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i + Random.Range(-15f, 15f);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var spark = ProtoUI.CreatePanel("Spark", _root, pos, new Vector2(18, 18), color);
            spark.transform.localRotation = Quaternion.Euler(0, 0, 45); spark.raycastTarget = false;
            StartCoroutine(SparkAnim(spark, pos, dir * radius));
        }
    }

    IEnumerator SparkAnim(Image spark, Vector2 from, Vector2 move)
    {
        var rt = (RectTransform)spark.transform; float t = 0f; Color c = spark.color;
        while (t < 0.45f) { t += Time.deltaTime; float p = t / 0.45f; rt.anchoredPosition = from + move * Mathf.SmoothStep(0, 1, p); rt.localScale = Vector3.one * (1f - p * 0.7f); c.a = 1f - p; spark.color = c; yield return null; }
        Destroy(spark.gameObject);
    }

    IEnumerator AttackMotionFor(CardDef card)
    {
        switch (card.id)
        {
            case "fireball": case "gouka": case "guren": yield return MotionFlare(card); break;
            case "aquaedge": case "hyoga": case "soukyu": yield return MotionSlash(card); break;
            case "thunder": case "raijin": case "kannari": yield return MotionCyclone(card); break;
            case "sunshine": case "amaterasu": case "shingan": yield return MotionVoice(card); break;
            case "shuen": case "kokuu": yield return MotionAsura(card); break;
            default: yield return Lunge(_actorRt, new Vector2(150, 0)); break;
        }
    }

    IEnumerator MotionFlare(CardDef card) { StartCoroutine(FlashSprite(_actorImg, new Color(1f, 0.85f, 0.55f))); yield return Pulse(_actorRt, 1.1f, 0.22f); yield return Projectile(card.color, 38f, 0.32f, false); }

    IEnumerator MotionSlash(CardDef card)
    {
        Vector2 origin = _actorRt.anchoredPosition; Vector2 through = _slimeRt.anchoredPosition + new Vector2(170, 0);
        yield return Jab(_actorRt, new Vector2(-40, 0), 0.1f);
        float t = 0f;
        while (t < 0.16f) { t += Time.deltaTime; float p = t / 0.16f; _actorRt.anchoredPosition = Vector2.Lerp(origin, through, p * p); if (Random.value < 0.5f) SpawnBurst(_actorRt.anchoredPosition, new Color(card.color.r, card.color.g, card.color.b, 0.6f), 1, 30f); yield return null; }
        SpawnSlashLine(_slimeRt.anchoredPosition, card.color);
        yield return new WaitForSeconds(0.25f);
        t = 0f; Vector2 back = _actorRt.anchoredPosition;
        while (t < 0.2f) { t += Time.deltaTime; _actorRt.anchoredPosition = Vector2.Lerp(back, origin, Mathf.SmoothStep(0, 1, t / 0.2f)); yield return null; }
        _actorRt.anchoredPosition = origin;
    }

    IEnumerator MotionCyclone(CardDef card)
    {
        float t = 0f;
        while (t < 0.45f) { t += Time.deltaTime; _actorRt.localRotation = Quaternion.Euler(0, 0, -720f * (t / 0.45f)); if (Random.value < 0.4f) SpawnBurst(_actorRt.anchoredPosition, card.color, 1, 80f); yield return null; }
        _actorRt.localRotation = Quaternion.identity; yield return Projectile(card.color, 34f, 0.4f, true);
    }

    IEnumerator MotionVoice(CardDef card)
    {
        yield return Pulse(_actorRt, 1.15f, 0.18f);
        for (int i = 0; i < 3; i++) { StartCoroutine(RingWave(_actorRt.anchoredPosition + new Vector2(80, 60), _slimeRt.anchoredPosition, card.color)); StartCoroutine(Pulse(_actorRt, 1.08f, 0.12f)); yield return new WaitForSeconds(0.16f); }
        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator MotionAsura(CardDef card)
    {
        Vector2 origin = _actorRt.anchoredPosition; Vector2 apex = origin + new Vector2(120, 320); Vector2 slam = _slimeRt.anchoredPosition + new Vector2(-60, 40);
        yield return Jab(_actorRt, new Vector2(0, -30), 0.12f);
        float t = 0f;
        while (t < 0.25f) { t += Time.deltaTime; _actorRt.anchoredPosition = Vector2.Lerp(origin, apex, Mathf.Sin(t / 0.25f * Mathf.PI * 0.5f)); yield return null; }
        yield return new WaitForSeconds(0.15f);
        t = 0f;
        while (t < 0.12f) { t += Time.deltaTime; float p = t / 0.12f; _actorRt.anchoredPosition = Vector2.Lerp(apex, slam, p * p); _actorRt.localRotation = Quaternion.Euler(0, 0, -25f * p); yield return null; }
        SpawnBurst(slam, new Color(1f, 1f, 1f, 0.8f), 8, 140f);
        yield return new WaitForSeconds(0.2f);
        t = 0f; Vector2 back = _actorRt.anchoredPosition;
        while (t < 0.25f) { t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / 0.25f); _actorRt.anchoredPosition = Vector2.Lerp(back, origin, p); _actorRt.localRotation = Quaternion.Euler(0, 0, -25f * (1f - p)); yield return null; }
        _actorRt.anchoredPosition = origin; _actorRt.localRotation = Quaternion.identity;
    }

    IEnumerator Jab(RectTransform rt, Vector2 dir, float halfTime)
    {
        Vector2 origin = rt.anchoredPosition; float t = 0f;
        while (t < halfTime) { t += Time.deltaTime; rt.anchoredPosition = Vector2.Lerp(origin, origin + dir, t / halfTime); yield return null; }
        t = 0f;
        while (t < halfTime) { t += Time.deltaTime; rt.anchoredPosition = Vector2.Lerp(origin + dir, origin, t / halfTime); yield return null; }
        rt.anchoredPosition = origin;
    }

    IEnumerator Pulse(RectTransform rt, float scale, float duration)
    {
        Vector3 baseScale = rt.localScale; float half = duration / 2f; float t = 0f;
        while (t < half) { t += Time.deltaTime; rt.localScale = Vector3.Lerp(baseScale, baseScale * scale, t / half); yield return null; }
        t = 0f;
        while (t < half) { t += Time.deltaTime; rt.localScale = Vector3.Lerp(baseScale * scale, baseScale, t / half); yield return null; }
        rt.localScale = baseScale;
    }

    IEnumerator Projectile(Color color, float size, float duration, bool wobble)
    {
        Vector2 from = _actorRt.anchoredPosition + new Vector2(130, 50); Vector2 to = _slimeRt.anchoredPosition;
        var proj = ProtoUI.CreatePanel("Projectile", _root, from, new Vector2(size, size), color); proj.raycastTarget = false;
        var rt = (RectTransform)proj.transform; float t = 0f;
        while (t < duration) { t += Time.deltaTime; float p = t / duration; Vector2 pos = Vector2.Lerp(from, to, p); if (wobble) pos.y += Mathf.Sin(p * 18f) * 30f; rt.anchoredPosition = pos; rt.Rotate(0, 0, 720f * Time.deltaTime); yield return null; }
        Destroy(proj.gameObject);
    }

    void SpawnSlashLine(Vector2 pos, Color color)
    {
        var line = ProtoUI.CreatePanel("Slash", _root, pos, new Vector2(260, 10), color); line.raycastTarget = false;
        line.transform.localRotation = Quaternion.Euler(0, 0, -35f); StartCoroutine(SlashAnim(line));
    }

    IEnumerator SlashAnim(Image line)
    {
        var rt = (RectTransform)line.transform; Color c = line.color; float t = 0f;
        while (t < 0.3f) { t += Time.deltaTime; float p = t / 0.3f; rt.localScale = new Vector3(1f + p * 0.4f, 1f - p * 0.8f, 1f); c.a = 1f - p; line.color = c; yield return null; }
        Destroy(line.gameObject);
    }

    IEnumerator RingWave(Vector2 from, Vector2 to, Color color)
    {
        var ring = ProtoUI.CreatePanel("Ring", _root, from, new Vector2(40, 40), color); ring.raycastTarget = false;
        ring.transform.localRotation = Quaternion.Euler(0, 0, 45); var rt = (RectTransform)ring.transform; Color c = color; float t = 0f;
        while (t < 0.38f) { t += Time.deltaTime; float p = t / 0.38f; rt.anchoredPosition = Vector2.Lerp(from, to, p); rt.localScale = Vector3.one * (1f + p * 2.2f); c.a = 0.85f * (1f - p); ring.color = c; yield return null; }
        Destroy(ring.gameObject);
    }

    IEnumerator Impact(RectTransform target, Image targetImg, CardDef card, int damage, float multiplier, int sfxTierOverride = -1)
    {
        bool critical = multiplier >= 1.2f;
        int size = card?.Size ?? 1;
        int sfxTier = sfxTierOverride >= 0 ? sfxTierOverride : critical ? 3 : size >= 12 ? 2 : size >= 7 ? 1 : 0;
        _sfx.PlayOneShot(_hitClips[Mathf.Clamp(sfxTier, 0, 3)]);
        Color burstColor = card?.color ?? Color.white;

        int count = 10 + size; float radius = 140f + size * 12f; float shakeAmp = 12f + size * 1.2f; float shakeDur = 0.28f + size * 0.02f;
        if (critical) { count += 12; radius *= 1.3f; burstColor = Color.Lerp(burstColor, new Color(1f, 0.85f, 0.3f), 0.6f); shakeAmp *= 1.5f; shakeDur += 0.15f; }

        StartCoroutine(ShockExpand(target.anchoredPosition, burstColor, 1f + size * 0.12f));
        if (size >= 9) StartCoroutine(ScreenFlash(burstColor, critical ? 0.4f : 0.26f));
        if (size >= 12) { SpawnBurst(target.anchoredPosition, Color.white, count / 2, radius * 0.5f); StartCoroutine(ShockExpand(target.anchoredPosition, Color.white, 0.7f + size * 0.08f)); }

        SpawnBurst(target.anchoredPosition, burstColor, count, radius);
        StartCoroutine(FlashSprite(targetImg, critical ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.45f)));
        if (multiplier >= 1.25f) yield return HitStop(0.12f);

        Vector2 popupPos = target.anchoredPosition + new Vector2(0, target.sizeDelta.y * 0.5f + 40f);
        StartCoroutine(DamagePopup(popupPos, damage, multiplier));
        yield return Shake(target, shakeAmp, shakeDur);
    }

    IEnumerator HitStop(float realSeconds) { Time.timeScale = 0.05f; yield return new WaitForSecondsRealtime(realSeconds); Time.timeScale = 1f; }

    IEnumerator DamagePopup(Vector2 pos, int damage, float multiplier)
    {
        float fontSize, popScale, life; bool useGradient; TMPro.VertexGradient gradient = default; Color flatColor = Color.white;
        if (multiplier >= 1.25f) { fontSize = 88; popScale = 2.6f; life = 0.8f; useGradient = true; gradient = new TMPro.VertexGradient(new Color(1f, 0.98f, 0.8f), new Color(1f, 0.95f, 0.65f), new Color(1f, 0.55f, 0.12f), new Color(0.95f, 0.38f, 0.08f)); }
        else if (multiplier >= 1.05f) { fontSize = 64; popScale = 1.8f; life = 0.65f; useGradient = false; flatColor = new Color(1f, 0.92f, 0.45f); }
        else { fontSize = 46; popScale = 1.3f; life = 0.55f; useGradient = false; flatColor = Color.white; }

        var holder = ProtoUI.CreateRect("DamagePopup", _root);
        holder.anchoredPosition = pos + new Vector2(Random.Range(-35f, 35f), 0); holder.sizeDelta = new Vector2(500, 120);
        var group = holder.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
        string text = damage.ToString();

        var shadow = ProtoUI.CreateText("Shadow", holder, text, fontSize, new Vector2(5, -5), new Vector2(500, 120), useGradient ? new Color(0.3f, 0.05f, 0.02f) : new Color(0.05f, 0.04f, 0.1f));
        shadow.fontStyle = FontStyles.Bold; shadow.characterSpacing = 4f; shadow.raycastTarget = false;
        var main = ProtoUI.CreateText("Main", holder, text, fontSize, Vector2.zero, new Vector2(500, 120), flatColor);
        main.fontStyle = FontStyles.Bold; main.characterSpacing = 4f; main.raycastTarget = false; main.outlineWidth = 0.28f;
        main.outlineColor = useGradient ? new Color32(60, 15, 5, 255) : new Color32(10, 8, 24, 255);
        if (useGradient) { main.enableVertexGradient = true; main.colorGradient = gradient; }

        float t = 0f;
        while (t < 0.12f) { t += Time.deltaTime; holder.localScale = Vector3.one * Mathf.Lerp(popScale, 0.92f, Mathf.SmoothStep(0, 1, t / 0.12f)); yield return null; }
        t = 0f;
        while (t < 0.08f) { t += Time.deltaTime; holder.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, t / 0.08f); yield return null; }
        yield return new WaitForSeconds(life * 0.5f);
        t = 0f; float fade = life * 0.5f;
        while (t < fade) { t += Time.deltaTime; group.alpha = 1f - t / fade; holder.anchoredPosition += new Vector2(0, Time.deltaTime * 120f); yield return null; }
        Destroy(holder.gameObject);
    }

    IEnumerator ShockExpand(Vector2 pos, Color color, float scale)
    {
        var ring = ProtoUI.CreatePanel("Shock", _root, pos, new Vector2(60, 60), color); ring.raycastTarget = false;
        ring.transform.localRotation = Quaternion.Euler(0, 0, 45); var rt = (RectTransform)ring.transform; Color c = color; float t = 0f; const float dur = 0.32f;
        while (t < dur) { t += Time.deltaTime; float p = t / dur; rt.localScale = Vector3.one * Mathf.Lerp(0.4f, 3.2f * scale, Mathf.Sqrt(p)); c.a = 0.7f * (1f - p); ring.color = c; yield return null; }
        Destroy(ring.gameObject);
    }

    IEnumerator ScreenFlash(Color color, float maxAlpha)
    {
        var flash = ProtoUI.CreateFullScreen("ScreenFlash", _root);
        var img = flash.gameObject.AddComponent<Image>(); img.raycastTarget = false;
        float t = 0f; const float dur = 0.35f;
        while (t < dur) { t += Time.deltaTime; float a = Mathf.Lerp(maxAlpha, 0f, t / dur); img.color = new Color(color.r, color.g, color.b, a); yield return null; }
        Destroy(flash.gameObject);
    }
}

// 手札カードの操作: タップ＝選択 / 上にスライド＝発動
public class CardSlide : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    RectTransform _rt;
    Vector2 _home;
    System.Action _onSelect, _onPlay;
    bool _dragging;

    public void Setup(RectTransform rt, Vector2 home, System.Action onSelect, System.Action onPlay)
    {
        _rt = rt; _home = home; _onSelect = onSelect; _onPlay = onPlay;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!_dragging) _onSelect?.Invoke();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        _dragging = true;
        _onSelect?.Invoke();
    }

    public void OnDrag(PointerEventData e)
    {
        if (_rt == null) return;
        float up = Mathf.Clamp(e.position.y - e.pressPosition.y, 0f, 260f);
        _rt.anchoredPosition = _home + new Vector2(0, up);
    }

    public void OnEndDrag(PointerEventData e)
    {
        _dragging = false;
        if (_rt == null) return;
        float up = e.position.y - e.pressPosition.y;
        if (up > Screen.height * 0.12f) { _onPlay?.Invoke(); }
        else { _rt.anchoredPosition = _home; }
    }
}
