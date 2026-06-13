using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// バトル画面: 100マス=100枚の山札・引き切りドロー（GDD推奨方式）
// スキル使用時は点滅カウント・チャレンジ（3択タップ回答）で倍率が決まる
public class ProtoBattle : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;

    // --- UI ---
    TextMeshProUGUI _enemyName, _enemyHPText, _message, _deckText, _resultSub;
    Image _enemyFill;

    // パーティのHPバー（メンバーごとに分割表示）
    RectTransform _pHpRow;
    readonly List<Image> _pFills = new List<Image>();
    readonly List<TextMeshProUGUI> _pHpTexts = new List<TextMeshProUGUI>();
    float _pBarW;
    int[] _memberHP; // メンバー個別のHP
    RectTransform _handArea;
    RectTransform _challengeRoot;
    TextMeshProUGUI _challengePrompt;
    Image _timerFill;
    RectTransform _pieceArea;
    TMP_InputField _challengeInput;
    RectTransform _resultRoot;
    TextMeshProUGUI _resultText;

    const float GaugeWidth = 320f;
    const int HandSize = 3;
    const float FlashDuration = 0.5f;   // 単発フラッシュ（光過敏性対応: 3回/秒以下）
    const float AnswerTime = 3f;

    // --- 状態 ---
    int _playerHP, _playerMaxHP;
    int _enemyHP, _enemyMaxHP;
    List<List<ProtoSkill>> _decks = new List<List<ProtoSkill>>(); // メンバーごとの山札（自分の盤面から生成）
    List<List<ProtoSkill>> _hands = new List<List<ProtoSkill>>(); // メンバーごとの手札（各3枚）
    bool[] _acted; // ターン制: そのメンバーが行動済みか
    bool _inputLocked;

    AudioSource _sfx;
    AudioClip[] _hitClips;
    AudioClip _swingClip;

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();

        // ヒット効果音（4段階）を生成
        _sfx = gameObject.AddComponent<AudioSource>();
        _hitClips = new[]
        {
            ProtoAudio.CreateHitClip(0),
            ProtoAudio.CreateHitClip(1),
            ProtoAudio.CreateHitClip(2),
            ProtoAudio.CreateHitClip(3),
        };
        _swingClip = ProtoAudio.CreateSwing();

        Hide();
    }

    public void Hide()
    {
        // 進行中のターン処理・チャレンジ・エフェクトをすべて止めてから隠す
        // （途中でビルドへ戻っても、次のBegin()で完全リセットされる）
        StopAllCoroutines();
        Time.timeScale = 1f; // ヒットストップ中に戻ってもスローのまま固まらないように
        _inputLocked = false;
        _root.gameObject.SetActive(false);
    }

    ProtoEnemy _enemy; // 今戦っている敵
    int _effWave;      // 敵の実効レベル（Wave + ボスのレベル補正）

    RectTransform _enemyShadow; // 地上の敵の足元の影（飛行する敵にはつけない）

    // --- 通電連鎖の状態 ---
    // メンバーごとに「充電された配置」の集合（戦闘中のみ。戦闘開始でクリア）
    List<HashSet<PanelModel.Placement>> _charged = new List<HashSet<PanelModel.Placement>>();
    // 通電起点の選択結果
    PanelModel.Placement _chainSource;
    bool _chainWasCharged;

    // --- 一時バフの状態（メンバー別・残ターン付き）---
    int[] _buffAtk, _buffDef, _buffTurns;

    public void Begin(ProtoEnemy enemy)
    {
        _enemy = enemy;

        _root.gameObject.SetActive(true);
        _resultRoot.gameObject.SetActive(false);
        _challengeRoot.gameObject.SetActive(false);

        // メンバーごとに個別のHPを持つ（全員0で敗北）
        _memberHP = new int[_main.MemberStats.Count];
        _playerMaxHP = 0;
        for (int i = 0; i < _main.MemberStats.Count; i++)
        {
            _memberHP[i] = _main.MemberStats[i].MaxHP;
            _playerMaxHP += _memberHP[i];
        }
        _playerHP = _playerMaxHP; // 合計値（敗北判定用）
        BuildPartyHpBars();
        _effWave = _main.Wave + enemy.levelOffset; // ボスはレベル補正で大幅に強くなる
        _enemyMaxHP = enemy.baseHP + 40 * (_effWave - 1);
        _enemyHP = _enemyMaxHP;
        _enemyName.text = $"{enemy.enemyName} Lv{_effWave}";

        // 敵の見た目を差し替え。地上の敵は足が地面ラインに着く位置＋影、飛行の敵は浮いたまま
        _slimeImg.sprite = enemy.sprite;
        _slimeRt.sizeDelta = enemy.battleSize;
        _enemyInner.sizeDelta = enemy.battleSize;
        _slimeRt.anchoredPosition = enemy.flying
            ? new Vector2(460, 70)
            : new Vector2(460, GroundY + enemy.battleSize.y / 2f);

        if (_enemyShadow != null) Destroy(_enemyShadow.gameObject);
        _enemyShadow = null;
        if (!enemy.flying)
        {
            AddGroundShadow(_slimeRt, enemy.battleSize.x * 0.5f);
            _enemyShadow = (RectTransform)_slimeRt.GetChild(0); // AddGroundShadowは先頭に挿入する
        }

        BuildMembers(); // パーティの人数ぶんキャラを表示

        // メンバーごとに「自分の盤面」から山札を作り、手札3枚を配る
        _decks.Clear();
        _hands.Clear();
        _acted = new bool[_main.Party.Count];

        // 通電・バフ状態を初期化（戦闘ごとにリセット）
        _charged.Clear();
        int pn = _main.Party.Count;
        _buffAtk = new int[pn];
        _buffDef = new int[pn];
        _buffTurns = new int[pn];

        for (int m = 0; m < _main.Party.Count; m++)
        {
            _decks.Add(Shuffle(_main.Panels[m].BuildDeck()));
            var hand = new List<ProtoSkill>();
            for (int i = 0; i < HandSize; i++) hand.Add(Draw(m));
            _hands.Add(hand);
            _charged.Add(new HashSet<PanelModel.Placement>());
        }

        _inputLocked = false;
        RefreshAll(dealAnimation: true);
        _message.text = "あなたのターン！カードを選ぼう！";
    }

    // ==================== UI構築 ====================

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("BattleScreen", _main.Canvas.transform);

        // 上部HUDの帯（背景から文字を分離する半透明バー＋金の飾りライン）
        var topBar = ProtoUI.CreatePanel("TopBar", _root, new Vector2(0, 400), new Vector2(1700, 115),
            new Color(0.05f, 0.04f, 0.10f, 0.62f));
        topBar.raycastTarget = false;
        var goldLine = ProtoUI.CreatePanel("TopBarLine", _root, new Vector2(0, 341), new Vector2(1700, 3),
            new Color(0.85f, 0.72f, 0.4f, 0.9f));
        goldLine.raycastTarget = false;

        // パーティHUD（左上）: メンバーごとのHPバーをBegin時に人数ぶん生成する
        _pHpRow = ProtoUI.CreateRect("PartyHpRow", _root);
        _pHpRow.anchoredPosition = new Vector2(-470, 392);
        _pHpRow.sizeDelta = new Vector2(660, 100);

        // 敵HUD（右上）
        _enemyName = ProtoUI.CreateText("EName", _root, "スライム", 28, new Vector2(560, 410), new Vector2(300, 40),
            new Color(1f, 0.5f, 0.45f));
        ProtoUI.StyleTitle(_enemyName, new Color(1f, 0.55f, 0.5f));
        ProtoUI.CreateGauge("EGauge", _root, new Vector2(560, 370), new Vector2(GaugeWidth, 20),
            new Color(0.15f, 0.12f, 0.2f), new Color(1f, 0.35f, 0.35f), out _enemyFill);
        _enemyHPText = ProtoUI.CreateText("EHP", _root, "", 17, new Vector2(560, 370), new Vector2(300, 26));
        _enemyHPText.fontStyle = TMPro.FontStyles.Bold;

        // Wave＆山札カウンタ（上部中央）
        _deckText = ProtoUI.CreateText("Deck", _root, "", 21, new Vector2(0, 408), new Vector2(500, 34));
        ProtoUI.StyleTitle(_deckText, ProtoUI.Gold, 3f);

        // 逃走ボタン（戦闘を破棄してマップへ戻る）。カードと重ならない右上に配置
        ProtoUI.CreateButton("RetreatBtn", _root, "逃げる", 15,
            new Vector2(738, 408), new Vector2(110, 38), new Color(0.35f, 0.25f, 0.3f),
            () => _main.ShowMap());

        // 敵キャラ（ドット絵）。外側=モーション用 / 内側=アイドルアニメ用 の2層構造
        // ※味方パーティはBegin()のBuildMembers()で人数ぶん生成する
        _slimeImg = CreateCharacterSprite("EnemySprite", ProtoPixelArt.Dragon(), new Vector2(460, 70), new Vector2(540, 355));
        _slimeRt = (RectTransform)_slimeImg.transform.parent;
        _enemyInner = (RectTransform)_slimeImg.transform;

        // メッセージ欄（キャラと重ならないよう上部HUDのすぐ下に配置）
        var msgBox = ProtoUI.CreatePanel("MsgBox", _root, new Vector2(0, 308), new Vector2(1300, 56),
            new Color(0.08f, 0.06f, 0.14f, 0.92f));
        ProtoUI.CreatePanel("MsgLineTop", msgBox.transform, new Vector2(0, 28), new Vector2(1300, 2),
            new Color(0.85f, 0.72f, 0.4f, 0.7f)).raycastTarget = false;
        ProtoUI.CreatePanel("MsgLineBottom", msgBox.transform, new Vector2(0, -28), new Vector2(1300, 2),
            new Color(0.85f, 0.72f, 0.4f, 0.7f)).raycastTarget = false;
        _message = ProtoUI.CreateText("Msg", msgBox.transform, "", 22, Vector2.zero, new Vector2(1260, 56));
        _message.characterSpacing = 2f;

        // 手札エリア
        _handArea = ProtoUI.CreateRect("Hand", _root);
        _handArea.anchoredPosition = new Vector2(0, -300);
        _handArea.sizeDelta = new Vector2(1760, 220);

        // ---- 点滅チャレンジ用オーバーレイ ----
        _challengeRoot = ProtoUI.CreateFullScreen("Challenge", _root);
        var dim = _challengeRoot.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.75f);

        _challengePrompt = ProtoUI.CreateText("CPrompt", _challengeRoot, "", 30, new Vector2(0, 260), new Vector2(900, 50));
        _pieceArea = ProtoUI.CreateRect("PieceArea", _challengeRoot);
        _pieceArea.anchoredPosition = new Vector2(0, 40);
        _challengeInput = ProtoUI.CreateInputField("AnswerInput", _challengeRoot, new Vector2(0, -200), new Vector2(240, 70), 32);
        _challengeInput.gameObject.SetActive(false); // 旧:数値入力。順次点滅では使わない
        ProtoUI.CreateGauge("Timer", _challengeRoot, new Vector2(0, -290), new Vector2(500, 14),
            new Color(0.2f, 0.18f, 0.28f), new Color(1f, 0.85f, 0.3f), out _timerFill);

        // ---- 勝敗オーバーレイ ----
        _resultRoot = ProtoUI.CreateFullScreen("Result", _root);
        var rdim = _resultRoot.gameObject.AddComponent<Image>();
        rdim.color = new Color(0, 0, 0, 0.8f);
        _resultText = ProtoUI.CreateText("RText", _resultRoot, "", 60, new Vector2(0, 90), new Vector2(800, 90));
        ProtoUI.StyleTitle(_resultText, ProtoUI.Gold, 10f);
        _resultSub = ProtoUI.CreateText("RSub", _resultRoot, "", 24, new Vector2(0, -10), new Vector2(700, 110));
        ProtoUI.CreateButton("BackBtn", _resultRoot, "マップへ戻る", 24,
            new Vector2(0, -60), new Vector2(280, 64), new Color(0.35f, 0.3f, 0.55f),
            () => { if (_enemyHP <= 0) _main.OnBattleWon(); else _main.ShowMap(); });
    }

    // ==================== ドロー ====================

    // 指定メンバーの山札から1枚引く（引き切り: 使い切ったら自分の盤面から再シャッフル）
    ProtoSkill Draw(int member)
    {
        if (_decks[member].Count == 0)
            _decks[member] = Shuffle(_main.Panels[member].BuildDeck());
        var top = _decks[member][0];
        _decks[member].RemoveAt(0);
        return top;
    }

    static List<ProtoSkill> Shuffle(List<ProtoSkill> src)
    {
        var list = new List<ProtoSkill>(src);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    // ==================== 表示更新 ====================

    void RefreshAll(bool dealAnimation = false)
    {
        // メンバーごとのHPバーを更新（戦闘不能は赤いバー表示に）
        for (int i = 0; i < _pFills.Count && _memberHP != null && i < _memberHP.Length; i++)
        {
            var s = _main.MemberStats[i];
            ProtoUI.SetGauge(_pFills[i], _memberHP[i] / (float)s.MaxHP, _pBarW);
            _pHpTexts[i].text = _memberHP[i] <= 0 ? "戦闘不能" : $"{_memberHP[i]}/{s.MaxHP}";
        }

        ProtoUI.SetGauge(_enemyFill, (float)_enemyHP / _enemyMaxHP, GaugeWidth);
        _enemyHPText.text = $"{_enemyHP}/{_enemyMaxHP}";
        _deckText.text = $"— Wave {_main.Wave} —";
        RefreshHand(dealAnimation);
    }

    Image _actorImg, _slimeImg;
    RectTransform _actorRt, _slimeRt;      // _actor系=いま行動中のメンバー（攻撃モーションが動かす）
    RectTransform _enemyInner;             // 敵の内側（アイドルアニメが動かす）

    // パーティメンバーの表示（外側/内側/画像）
    readonly List<RectTransform> _memberRts = new List<RectTransform>();
    readonly List<RectTransform> _memberInners = new List<RectTransform>();
    readonly List<Image> _memberImgs = new List<Image>();

    // メンバーごとのHPバーを人数ぶん生成（見切れないよう幅を自動調整）
    void BuildPartyHpBars()
    {
        foreach (Transform c in _pHpRow) Destroy(c.gameObject);
        _pFills.Clear();
        _pHpTexts.Clear();

        var party = _main.Party;
        int n = party.Count;
        _pBarW = n == 1 ? 320f : n == 2 ? 255f : 195f;
        float gap = 16f;
        float startX = -(n - 1) * (_pBarW + gap) / 2f;

        for (int i = 0; i < n; i++)
        {
            float x = startX + i * (_pBarW + gap);

            // 名前（髪色）
            var name = ProtoUI.CreateText($"PName{i}", _pHpRow, party[i].name, 16,
                new Vector2(x, 24), new Vector2(_pBarW, 22));
            name.fontStyle = TMPro.FontStyles.Bold;
            name.color = Color.Lerp(party[i].hair, Color.white, 0.3f);

            // HPバー＋数値（バーの上に重ねる）
            ProtoUI.CreateGauge($"PGauge{i}", _pHpRow, new Vector2(x, -4), new Vector2(_pBarW, 17),
                new Color(0.15f, 0.12f, 0.2f), new Color(0.8f, 0.4f, 1f), out var fill);
            var hp = ProtoUI.CreateText($"PHP{i}", _pHpRow, "", 13, new Vector2(x, -4), new Vector2(_pBarW, 18));
            hp.fontStyle = TMPro.FontStyles.Bold;

            _pFills.Add(fill);
            _pHpTexts.Add(hp);
        }
    }

    const float GroundY = -160f; // 背景の草地に合わせた地面ライン（足の位置）

    // 足元の楕円影。これがあるだけで「地面に立っている」感が出る
    void AddGroundShadow(RectTransform holder, float width)
    {
        var sh = ProtoUI.CreateRect("Shadow", holder);
        sh.SetAsFirstSibling(); // キャラの絵より後ろに描画
        sh.anchoredPosition = new Vector2(0, -holder.sizeDelta.y / 2f + 8f);
        sh.sizeDelta = new Vector2(width, 26f);
        sh.localRotation = Quaternion.Euler(0, 0, 45);
        sh.localScale = new Vector3(1f, 0.35f, 1f);
        var img = sh.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.3f);
        img.raycastTarget = false;
    }

    // パーティの人数ぶんキャラを並べる（後衛ほど左上に小さく）
    void BuildMembers()
    {
        foreach (var rt in _memberRts) Destroy(rt.gameObject);
        _memberRts.Clear();
        _memberInners.Clear();
        _memberImgs.Clear();

        var party = _main.Party;
        // 横並び・小さめ（カード選択エリアを広く使うため）。足が地面ラインに着くよう配置
        float size = party.Count == 1 ? 280f : 190f;
        float height = size * 1.23f;
        float startX = party.Count == 1 ? -470f : -620f;
        for (int i = 0; i < party.Count; i++)
        {
            Vector2 pos = new Vector2(startX + i * (size * 0.78f), GroundY + height / 2f);
            var img = CreateCharacterSprite($"Member_{party[i].name}", party[i].BattleSprite(),
                pos, new Vector2(size, height));
            var holder = (RectTransform)img.transform.parent;
            AddGroundShadow(holder, size * 0.55f);
            _memberRts.Add(holder);
            _memberInners.Add((RectTransform)img.transform);
            _memberImgs.Add(img);
        }

        // デフォルトの行動者はリーダー
        _actorRt = _memberRts[0];
        _actorImg = _memberImgs[0];
    }

    Image CreateCharacterSprite(string name, Sprite sprite, Vector2 pos, Vector2 size)
    {
        // 外側ホルダー: 攻撃モーション用
        var holder = ProtoUI.CreateRect(name, _root);
        holder.anchoredPosition = pos;
        holder.sizeDelta = size;

        // 内側: 絵本体（アイドルアニメで上下にゆれる）
        var inner = ProtoUI.CreateRect("Sprite", holder);
        inner.sizeDelta = size;
        var img = inner.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        return img;
    }

    // 待機中のアイドルアニメ（呼吸のゆれ）＋Escで逃げる
    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        // Escキーでいつでも逃げる（マップへ戻る）
        bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
        var kbEsc = UnityEngine.InputSystem.Keyboard.current;
        if (kbEsc != null && kbEsc.escapeKey.wasPressedThisFrame) escPressed = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!escPressed && Input.GetKeyDown(KeyCode.Escape)) escPressed = true;
#endif
        if (escPressed)
        {
            _main.ShowMap();
            return;
        }

        float t = Time.time;

        // パーティ全員: 上下＋わずかな伸縮（呼吸）。位相をずらして自然に
        for (int i = 0; i < _memberInners.Count; i++)
        {
            float ph = i * 0.9f;
            _memberInners[i].anchoredPosition = new Vector2(0, Mathf.Sin(t * 3.0f + ph) * 4f);
            _memberInners[i].localScale = new Vector3(1f, 1f + Mathf.Sin(t * 3.0f + ph) * 0.012f, 1f);
        }
        if (_enemyInner != null)
        {
            // 敵: ふわふわ浮遊（位相をずらして同期感をなくす）
            _enemyInner.anchoredPosition = new Vector2(0, Mathf.Sin(t * 2.1f + 1.7f) * 9f);
        }
    }

    // ==================== モーション＆エフェクト ====================

    // 攻撃モーション: タメ（後ろに引いて沈む）→ 高速突進（前傾）→ ヒットストップ → 戻る
    IEnumerator Lunge(RectTransform rt, Vector2 dir)
    {
        Vector2 origin = rt.anchoredPosition;
        Vector3 baseScale = rt.localScale;
        float tiltSign = dir.x >= 0 ? -1f : 1f; // 進行方向へ前傾

        // ① タメ: 少し後ろに引きつつ縦に沈む（0.16秒）
        float t = 0f;
        while (t < 0.16f)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0, 1, t / 0.16f);
            rt.anchoredPosition = Vector2.Lerp(origin, origin - dir * 0.3f, p);
            rt.localScale = Vector3.Lerp(baseScale, new Vector3(baseScale.x * 1.06f, baseScale.y * 0.88f, 1f), p);
            yield return null;
        }

        // ② 突進: 一気に前へ（0.07秒・前傾姿勢＋縦に伸びる）
        Vector2 windup = rt.anchoredPosition;
        t = 0f;
        while (t < 0.07f)
        {
            t += Time.deltaTime;
            float p = t / 0.07f;
            rt.anchoredPosition = Vector2.Lerp(windup, origin + dir, p * p); // 加速感
            rt.localScale = Vector3.Lerp(rt.localScale, new Vector3(baseScale.x * 0.94f, baseScale.y * 1.08f, 1f), p);
            rt.localRotation = Quaternion.Euler(0, 0, tiltSign * 10f * p);
            yield return null;
        }

        // ③ ヒットストップ: 当たった瞬間ピタッと止める（打撃感）
        yield return new WaitForSeconds(0.07f);

        // ④ 戻り: ゆったり戻って姿勢リセット（0.22秒）
        Vector2 hitPos = rt.anchoredPosition;
        t = 0f;
        while (t < 0.22f)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0, 1, t / 0.22f);
            rt.anchoredPosition = Vector2.Lerp(hitPos, origin, p);
            rt.localScale = Vector3.Lerp(rt.localScale, baseScale, p);
            rt.localRotation = Quaternion.Euler(0, 0, tiltSign * 10f * (1f - p));
            yield return null;
        }
        rt.anchoredPosition = origin;
        rt.localScale = baseScale;
        rt.localRotation = Quaternion.identity;
    }

    // 被弾側の揺れ
    IEnumerator Shake(RectTransform rt, float amp, float duration)
    {
        Vector2 origin = rt.anchoredPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float decay = 1f - t / duration;
            rt.anchoredPosition = origin + Random.insideUnitCircle * amp * decay;
            yield return null;
        }
        rt.anchoredPosition = origin;
    }

    // 被弾側の色フラッシュ
    IEnumerator FlashSprite(Image img, Color flashColor)
    {
        Color original = img.color;
        img.color = flashColor;
        yield return new WaitForSeconds(0.12f);
        img.color = original;
    }

    // ヒット時に飛び散るキラキラ
    void SpawnBurst(Vector2 pos, Color color, int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i + Random.Range(-15f, 15f);
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var spark = ProtoUI.CreatePanel("Spark", _root, pos, new Vector2(18, 18), color);
            spark.transform.localRotation = Quaternion.Euler(0, 0, 45); // ひし形に
            spark.raycastTarget = false;
            StartCoroutine(SparkAnim(spark, pos, dir * radius));
        }
    }

    IEnumerator SparkAnim(Image spark, Vector2 from, Vector2 move)
    {
        var rt = (RectTransform)spark.transform;
        float t = 0f;
        Color c = spark.color;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            float p = t / 0.45f;
            rt.anchoredPosition = from + move * Mathf.SmoothStep(0, 1, p);
            rt.localScale = Vector3.one * (1f - p * 0.7f);
            c.a = 1f - p;
            spark.color = c;
            yield return null;
        }
        Destroy(spark.gameObject);
    }

    // ==================== 技ごとの専用モーション ====================

    IEnumerator AttackMotionFor(ProtoSkill skill)
    {
        if (skill == null)
        {
            yield return Lunge(_actorRt, new Vector2(150, 0)); // 通常攻撃は踏み込み
            yield break;
        }
        switch (skill.id)
        {
            case "cook":   yield return MotionFlare(skill); break;
            case "clip":   yield return MotionStinger(skill); break;
            case "towel":  yield return MotionSlash(skill); break;
            case "vacuum": yield return MotionCyclone(skill); break;
            case "scold":  yield return MotionVoice(skill); break;
            case "apron":  yield return MotionAsura(skill); break;
            default:       yield return Lunge(_actorRt, new Vector2(150, 0)); break;
        }
    }

    // マザーフレア: タメ → 火球を投射
    IEnumerator MotionFlare(ProtoSkill skill)
    {
        StartCoroutine(FlashSprite(_actorImg, new Color(1f, 0.85f, 0.55f)));
        yield return Pulse(_actorRt, 1.1f, 0.22f);
        yield return Projectile(skill.color, 38f, 0.32f, false);
    }

    // クリップスティンガー: 2連ジャブ＋2連射
    IEnumerator MotionStinger(ProtoSkill skill)
    {
        for (int i = 0; i < 2; i++)
        {
            yield return Jab(_actorRt, new Vector2(70, 0), 0.07f);
            StartCoroutine(Projectile(skill.color, 20f, 0.18f, false));
            yield return new WaitForSeconds(0.12f);
        }
        yield return new WaitForSeconds(0.1f);
    }

    // アクアスラッシュ: 敵を斬り抜ける居合ダッシュ
    IEnumerator MotionSlash(ProtoSkill skill)
    {
        Vector2 origin = _actorRt.anchoredPosition;
        Vector2 through = _slimeRt.anchoredPosition + new Vector2(170, 0); // 敵の向こう側へ

        // 一瞬の構え
        yield return Jab(_actorRt, new Vector2(-40, 0), 0.1f);

        // 高速で斬り抜ける（残像スパーク付き）
        float t = 0f;
        while (t < 0.16f)
        {
            t += Time.deltaTime;
            float p = t / 0.16f;
            _actorRt.anchoredPosition = Vector2.Lerp(origin, through, p * p);
            if (Random.value < 0.5f)
                SpawnBurst(_actorRt.anchoredPosition, new Color(skill.color.r, skill.color.g, skill.color.b, 0.6f), 1, 30f);
            yield return null;
        }

        // 斬撃線を敵の上に表示
        SpawnSlashLine(_slimeRt.anchoredPosition, skill.color);
        yield return new WaitForSeconds(0.25f);

        // 元の位置へ戻る
        t = 0f;
        Vector2 back = _actorRt.anchoredPosition;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            _actorRt.anchoredPosition = Vector2.Lerp(back, origin, Mathf.SmoothStep(0, 1, t / 0.2f));
            yield return null;
        }
        _actorRt.anchoredPosition = origin;
    }

    // サイクロンバースト: その場で高速スピン → 蛇行する竜巻弾
    IEnumerator MotionCyclone(ProtoSkill skill)
    {
        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            _actorRt.localRotation = Quaternion.Euler(0, 0, -720f * (t / 0.45f));
            if (Random.value < 0.4f)
                SpawnBurst(_actorRt.anchoredPosition, skill.color, 1, 80f);
            yield return null;
        }
        _actorRt.localRotation = Quaternion.identity;
        yield return Projectile(skill.color, 34f, 0.4f, true); // 蛇行弾
    }

    // ジャッジメントボイス: 仁王立ちで衝撃波リング3連
    IEnumerator MotionVoice(ProtoSkill skill)
    {
        yield return Pulse(_actorRt, 1.15f, 0.18f);
        for (int i = 0; i < 3; i++)
        {
            StartCoroutine(RingWave(_actorRt.anchoredPosition + new Vector2(80, 60), _slimeRt.anchoredPosition, skill.color));
            StartCoroutine(Pulse(_actorRt, 1.08f, 0.12f));
            yield return new WaitForSeconds(0.16f);
        }
        yield return new WaitForSeconds(0.25f);
    }

    // アスラ・レガリア: 大ジャンプ → 敵へ急降下プレス
    IEnumerator MotionAsura(ProtoSkill skill)
    {
        Vector2 origin = _actorRt.anchoredPosition;
        Vector2 apex = origin + new Vector2(120, 320);
        Vector2 slam = _slimeRt.anchoredPosition + new Vector2(-60, 40);

        // 沈み込み → 大ジャンプ
        yield return Jab(_actorRt, new Vector2(0, -30), 0.12f);
        float t = 0f;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            _actorRt.anchoredPosition = Vector2.Lerp(origin, apex, Mathf.Sin(t / 0.25f * Mathf.PI * 0.5f));
            yield return null;
        }

        // 空中で一瞬タメ
        yield return new WaitForSeconds(0.15f);

        // 急降下（回転しながら）
        t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            float p = t / 0.12f;
            _actorRt.anchoredPosition = Vector2.Lerp(apex, slam, p * p);
            _actorRt.localRotation = Quaternion.Euler(0, 0, -25f * p);
            yield return null;
        }
        SpawnBurst(slam, new Color(1f, 1f, 1f, 0.8f), 8, 140f); // 着地の土煙

        yield return new WaitForSeconds(0.2f);

        // 戻る
        t = 0f;
        Vector2 back = _actorRt.anchoredPosition;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0, 1, t / 0.25f);
            _actorRt.anchoredPosition = Vector2.Lerp(back, origin, p);
            _actorRt.localRotation = Quaternion.Euler(0, 0, -25f * (1f - p));
            yield return null;
        }
        _actorRt.anchoredPosition = origin;
        _actorRt.localRotation = Quaternion.identity;
    }

    // ---- モーション部品 ----

    // 小刻みな突き（行って即戻る）
    IEnumerator Jab(RectTransform rt, Vector2 dir, float halfTime)
    {
        Vector2 origin = rt.anchoredPosition;
        float t = 0f;
        while (t < halfTime)
        {
            t += Time.deltaTime;
            rt.anchoredPosition = Vector2.Lerp(origin, origin + dir, t / halfTime);
            yield return null;
        }
        t = 0f;
        while (t < halfTime)
        {
            t += Time.deltaTime;
            rt.anchoredPosition = Vector2.Lerp(origin + dir, origin, t / halfTime);
            yield return null;
        }
        rt.anchoredPosition = origin;
    }

    // 拡縮パルス（タメ・叫び用）
    IEnumerator Pulse(RectTransform rt, float scale, float duration)
    {
        Vector3 baseScale = rt.localScale;
        float half = duration / 2f;
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            rt.localScale = Vector3.Lerp(baseScale, baseScale * scale, t / half);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            rt.localScale = Vector3.Lerp(baseScale * scale, baseScale, t / half);
            yield return null;
        }
        rt.localScale = baseScale;
    }

    // 飛び道具（MAMAの手元から敵へ。wobble=trueで蛇行）
    IEnumerator Projectile(Color color, float size, float duration, bool wobble)
    {
        Vector2 from = _actorRt.anchoredPosition + new Vector2(130, 50);
        Vector2 to = _slimeRt.anchoredPosition;
        var proj = ProtoUI.CreatePanel("Projectile", _root, from, new Vector2(size, size), color);
        proj.raycastTarget = false;
        var rt = (RectTransform)proj.transform;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            Vector2 pos = Vector2.Lerp(from, to, p);
            if (wobble) pos.y += Mathf.Sin(p * 18f) * 30f;
            rt.anchoredPosition = pos;
            rt.Rotate(0, 0, 720f * Time.deltaTime);
            yield return null;
        }
        Destroy(proj.gameObject);
    }

    // 斬撃線（細長いラインが一瞬走って消える）
    void SpawnSlashLine(Vector2 pos, Color color)
    {
        var line = ProtoUI.CreatePanel("Slash", _root, pos, new Vector2(260, 10), color);
        line.raycastTarget = false;
        line.transform.localRotation = Quaternion.Euler(0, 0, -35f);
        StartCoroutine(SlashAnim(line));
    }

    IEnumerator SlashAnim(Image line)
    {
        var rt = (RectTransform)line.transform;
        Color c = line.color;
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.deltaTime;
            float p = t / 0.3f;
            rt.localScale = new Vector3(1f + p * 0.4f, 1f - p * 0.8f, 1f);
            c.a = 1f - p;
            line.color = c;
            yield return null;
        }
        Destroy(line.gameObject);
    }

    // 衝撃波リング（広がりながら敵へ飛ぶ）
    IEnumerator RingWave(Vector2 from, Vector2 to, Color color)
    {
        var ring = ProtoUI.CreatePanel("Ring", _root, from, new Vector2(40, 40), color);
        ring.raycastTarget = false;
        ring.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var rt = (RectTransform)ring.transform;
        Color c = color;
        float t = 0f;
        while (t < 0.38f)
        {
            t += Time.deltaTime;
            float p = t / 0.38f;
            rt.anchoredPosition = Vector2.Lerp(from, to, p);
            rt.localScale = Vector3.one * (1f + p * 2.2f);
            c.a = 0.85f * (1f - p);
            ring.color = c;
            yield return null;
        }
        Destroy(ring.gameObject);
    }

    // 命中演出ひとまとめ。技のマス数が大きいほど派手になる（GDD: 大型ピース=高威力の三位一体）
    // skill=null は通常攻撃/敵攻撃（最小エフェクト）
    IEnumerator Impact(RectTransform target, Image targetImg, ProtoSkill skill, int damage, float multiplier,
        int sfxTierOverride = -1)
    {
        bool critical = multiplier >= 2f;
        int size = skill?.Size ?? 1;

        // 技の強さに応じたヒット音（小技→中技→大技→クリティカル）。敵の技は呼び出し側が指定
        int sfxTier = sfxTierOverride >= 0 ? sfxTierOverride
            : critical ? 3 : size >= 5 ? 2 : size >= 3 ? 1 : 0;
        _sfx.PlayOneShot(_hitClips[sfxTier]);
        Color burstColor = skill?.color ?? Color.white;

        // マス数でスケールするパラメータ（全体的に増量）
        int count = 10 + size * 3;           // キラキラの数: 13〜37
        float radius = 140f + size * 24f;    // 飛散範囲: 164〜356
        float shakeAmp = 12f + size * 2.2f;  // 揺れの強さ
        float shakeDur = 0.28f + size * 0.035f;

        if (critical)
        {
            count += 12;
            radius *= 1.35f;
            burstColor = Color.Lerp(burstColor, new Color(1f, 0.85f, 0.3f), 0.6f); // 金色寄りに
            shakeAmp *= 1.6f;
            shakeDur += 0.2f;
        }

        // 着弾点から広がる衝撃波リング（全ヒット共通）
        StartCoroutine(ShockExpand(target.anchoredPosition, burstColor, 1f + size * 0.18f));

        // 中技（4マス以上）から画面フラッシュ
        if (size >= 4)
            StartCoroutine(ScreenFlash(burstColor, critical ? 0.42f : 0.28f));

        // 大技（5マス以上）は白の内側バーストを重ねて三重に
        if (size >= 5)
        {
            SpawnBurst(target.anchoredPosition, Color.white, count / 2, radius * 0.5f);
            StartCoroutine(ShockExpand(target.anchoredPosition, Color.white, 0.7f + size * 0.12f));
        }

        SpawnBurst(target.anchoredPosition, burstColor, count, radius);
        StartCoroutine(FlashSprite(targetImg, critical ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.45f)));

        // 倍率に応じた追加演出（強いほど重く・派手に）
        if (multiplier >= 2f)
        {
            yield return HitStop(0.18f); // 長いヒットストップ
            StartCoroutine(DelayedBurst(target.anchoredPosition, burstColor, count / 2, radius * 0.8f, 0.15f));
        }
        else if (multiplier >= 1.5f)
        {
            yield return HitStop(0.08f); // 短いヒットストップ
        }

        // ダメージ数値ポップアップ（キャラの身長に応じて頭上に表示）
        Vector2 popupPos = target.anchoredPosition + new Vector2(0, target.sizeDelta.y * 0.5f + 40f);
        StartCoroutine(DamagePopup(popupPos, damage, multiplier));

        yield return Shake(target, shakeAmp, shakeDur);
    }

    // ヒットストップ: 一瞬だけ時間を超スローにして打撃の重みを出す
    IEnumerator HitStop(float realSeconds)
    {
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(realSeconds);
        Time.timeScale = 1f;
    }

    // ダメージ数値ポップアップ。倍率でサイズ・色・動きが4段階に変わる
    //   100%: 白・小さくポン
    //   120%: 黄色・少し大きく
    //   150%: 橙グラデ＋影・大きくバウンス
    //   200%: 金→赤橙グラデ＋影・特大で叩きつけバウンス
    IEnumerator DamagePopup(Vector2 pos, int damage, float multiplier)
    {
        float fontSize;
        float popScale;
        float life;
        bool useShadow;
        bool useGradient;
        TMPro.VertexGradient gradient = default;
        Color flatColor = Color.white;

        if (multiplier >= 2f)
        {
            fontSize = 96; popScale = 2.8f; life = 0.85f; useShadow = true; useGradient = true;
            gradient = new TMPro.VertexGradient(
                new Color(1f, 0.98f, 0.80f), new Color(1f, 0.95f, 0.65f),
                new Color(1f, 0.55f, 0.12f), new Color(0.95f, 0.38f, 0.08f));
        }
        else if (multiplier >= 1.5f)
        {
            fontSize = 70; popScale = 2.0f; life = 0.7f; useShadow = true; useGradient = true;
            gradient = new TMPro.VertexGradient(
                new Color(1f, 0.95f, 0.7f), new Color(1f, 0.9f, 0.6f),
                new Color(1f, 0.7f, 0.25f), new Color(1f, 0.6f, 0.2f));
        }
        else if (multiplier >= 1.2f)
        {
            fontSize = 56; popScale = 1.6f; life = 0.6f; useShadow = true; useGradient = false;
            flatColor = new Color(1f, 0.92f, 0.45f);
        }
        else
        {
            fontSize = 46; popScale = 1.3f; life = 0.55f; useShadow = true; useGradient = false;
            flatColor = Color.white;
        }

        // コンテナ（呼び出し側で頭上の位置が計算済み。横だけ毎回少しランダムにずらす）
        var holder = ProtoUI.CreateRect("DamagePopup", _root);
        holder.anchoredPosition = pos + new Vector2(Random.Range(-35f, 35f), 0);
        holder.sizeDelta = new Vector2(500, 120);
        var group = holder.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        string text = damage.ToString();

        if (useShadow)
        {
            // 影の色: グラデ系は暗赤、白/黄系はほぼ黒（どんな背景・キャラの上でも沈まない）
            var shadowColor = useGradient ? new Color(0.3f, 0.05f, 0.02f) : new Color(0.05f, 0.04f, 0.1f);
            var shadow = ProtoUI.CreateText("Shadow", holder, text, fontSize,
                new Vector2(5, -5), new Vector2(500, 120), shadowColor);
            shadow.fontStyle = TMPro.FontStyles.Bold;
            shadow.characterSpacing = 4f;
            shadow.raycastTarget = false;
        }

        var main = ProtoUI.CreateText("Main", holder, text, fontSize,
            Vector2.zero, new Vector2(500, 120), flatColor);
        main.fontStyle = TMPro.FontStyles.Bold;
        main.characterSpacing = 4f;
        main.raycastTarget = false;
        main.outlineWidth = 0.28f; // 全段階で太めの縁取り
        main.outlineColor = useGradient
            ? new Color32(60, 15, 5, 255)   // グラデ系: 焦げ茶
            : new Color32(10, 8, 24, 255);  // 白/黄系: ほぼ黒
        if (useGradient)
        {
            main.enableVertexGradient = true;
            main.colorGradient = gradient;
        }

        // 叩きつけ登場（行きすぎて戻るバウンス）
        float t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            holder.localScale = Vector3.one * Mathf.Lerp(popScale, 0.92f, Mathf.SmoothStep(0, 1, t / 0.12f));
            yield return null;
        }
        t = 0f;
        while (t < 0.08f)
        {
            t += Time.deltaTime;
            holder.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, t / 0.08f);
            yield return null;
        }

        yield return new WaitForSeconds(life * 0.5f);

        // 上に流れながらフェードアウト
        t = 0f;
        float fade = life * 0.5f;
        while (t < fade)
        {
            t += Time.deltaTime;
            group.alpha = 1f - t / fade;
            holder.anchoredPosition += new Vector2(0, Time.deltaTime * 120f);
            yield return null;
        }
        Destroy(holder.gameObject);
    }

    // 着弾点で拡大しながら消える衝撃波リング
    IEnumerator ShockExpand(Vector2 pos, Color color, float scale)
    {
        var ring = ProtoUI.CreatePanel("Shock", _root, pos, new Vector2(60, 60), color);
        ring.raycastTarget = false;
        ring.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var rt = (RectTransform)ring.transform;
        Color c = color;
        float t = 0f;
        const float dur = 0.32f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = t / dur;
            rt.localScale = Vector3.one * Mathf.Lerp(0.4f, 3.2f * scale, Mathf.Sqrt(p));
            c.a = 0.7f * (1f - p);
            ring.color = c;
            yield return null;
        }
        Destroy(ring.gameObject);
    }

    // 遅延バースト（クリティカルの二段爆発用）
    IEnumerator DelayedBurst(Vector2 pos, Color color, int count, float radius, float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnBurst(pos, color, count, radius);
        StartCoroutine(ShockExpand(pos, color, 1.2f));
    }

    // 画面全体を一瞬光らせる（大技用）。単発・短時間（光過敏性対応）
    IEnumerator ScreenFlash(Color color, float maxAlpha)
    {
        var flash = ProtoUI.CreateRect("ScreenFlash", _root);
        flash.anchorMin = Vector2.zero;
        flash.anchorMax = Vector2.one;
        flash.offsetMin = Vector2.zero;
        flash.offsetMax = Vector2.zero;
        var img = flash.gameObject.AddComponent<Image>();
        img.raycastTarget = false;

        float t = 0f;
        const float dur = 0.35f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(maxAlpha, 0f, t / dur);
            img.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        Destroy(flash.gameObject);
    }

    void RefreshHand(bool dealAnimation = false)
    {
        foreach (Transform child in _handArea) Destroy(child.gameObject);
        if (_hands.Count == 0) return;

        // 人数に応じてカードを縮小し、メンバーごとの列に分ける（画面幅いっぱいを使う）
        int n = _hands.Count;
        float scale = n == 1 ? 1f : n == 2 ? 0.95f : 0.85f; // 3人でも文字が読めるよう縮小を抑える
        float cardW = 205f * scale;  // カード間隔（やや詰める）
        float colW = cardW * HandSize;
        float colGap = 22f;
        float totalW = n * colW + (n - 1) * colGap;
        float startX = -totalW / 2f + colW / 2f;
        var party = _main.Party;

        for (int m = 0; m < n; m++)
        {
            float colX = startX + m * (colW + colGap);

            // 列の背景パネル（メンバーごとの領域をはっきり区切る。交互に明暗）
            if (n > 1)
            {
                var colBg = ProtoUI.CreatePanel($"ColBg{m}", _handArea,
                    new Vector2(colX, 10), new Vector2(colW + 18, 290f * scale + 80f),
                    m % 2 == 0 ? new Color(0.05f, 0.04f, 0.10f, 0.7f) : new Color(0.09f, 0.06f, 0.15f, 0.7f));
                colBg.raycastTarget = false;

                // 列の上端にメンバーの髪色のライン（誰の列か一目でわかる）
                var topLine = ProtoUI.CreatePanel($"ColLine{m}", _handArea,
                    new Vector2(colX, 10 + (290f * scale + 80f) / 2f - 2f), new Vector2(colW + 18, 4),
                    party[m].hair);
                topLine.raycastTarget = false;

                // 列の間に金の区切り線
                if (m > 0)
                {
                    var divider = ProtoUI.CreatePanel($"Divider{m}", _handArea,
                        new Vector2(colX - colW / 2f - colGap / 2f - 9f, 10), new Vector2(2, 290f * scale + 80f),
                        new Color(0.85f, 0.72f, 0.4f, 0.55f));
                    divider.raycastTarget = false;
                }
            }

            // メンバー名のヘッダー（行動済み/戦闘不能で表示が変わる）
            bool downed = _memberHP != null && m < _memberHP.Length && _memberHP[m] <= 0;
            bool actedDone = _acted != null && _acted[m];
            string headerText = downed ? $"{party[m].name}（戦闘不能）"
                : actedDone ? $"{party[m].name}（行動済み）" : party[m].name;
            var header = ProtoUI.CreateText($"Header{m}", _handArea, headerText, 18,
                new Vector2(colX, 140f * scale + 25f), new Vector2(260, 26));
            header.fontStyle = TMPro.FontStyles.Bold;
            header.color = downed ? new Color(0.85f, 0.35f, 0.35f)
                : actedDone ? new Color(0.5f, 0.5f, 0.55f) : Color.Lerp(party[m].hair, Color.white, 0.3f);

            var hand = _hands[m];
            float xs = colX - (hand.Count - 1) * cardW / 2f;
            for (int i = 0; i < hand.Count; i++)
            {
                int mi = m, idx = i;
                Vector2 finalPos = new Vector2(xs + i * cardW, 0);
                // 通電ON時、このカードのスキルに充電済みの配置があれば⚡バッジを出す
                bool charged = _main.ChainEnabled && hand[i] != null
                    && _charged != null && m < _charged.Count
                    && _main.Panels[m].Placements.Exists(p => p.skill == hand[i] && _charged[m].Contains(p));
                var btn = CreateCardUI(hand[i], finalPos, () => OnCardClicked(mi, idx), charged);
                btn.transform.localScale = Vector3.one * scale;
                btn.interactable = !_inputLocked && !actedDone && !downed;

                if (dealAnimation)
                    StartCoroutine(DealCard((RectTransform)btn.transform, finalPos, (m * HandSize + i) * 0.07f, scale));
            }
        }
    }

    // カードが山札（画面右下）から滑り込んでくる配布演出
    IEnumerator DealCard(RectTransform rt, Vector2 finalPos, float delay, float finalScale = 1f)
    {
        Vector2 startPos = finalPos + new Vector2(550, -260);
        rt.anchoredPosition = startPos;
        rt.localScale = Vector3.one * 0.25f;
        rt.localRotation = Quaternion.Euler(0, 0, -25f);

        yield return new WaitForSeconds(delay);

        float t = 0f;
        const float dur = 0.22f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0, 1, t / dur);
            rt.anchoredPosition = Vector2.Lerp(startPos, finalPos, p);
            rt.localScale = Vector3.one * Mathf.Lerp(0.25f, finalScale, p);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-25f, 0f, p));
            yield return null;
        }
        rt.anchoredPosition = finalPos;
        rt.localScale = Vector3.one * finalScale;
        rt.localRotation = Quaternion.identity;
    }

    // トレーディングカード風のカードUI（金フレーム＋名前帯＋アート枠＋威力帯）
    Button CreateCardUI(ProtoSkill skill, Vector2 pos, System.Action onClick, bool charged = false)
    {
        string cardName = skill == null ? "通常攻撃" : skill.skillName;
        int power = skill == null ? ProtoSkills.NormalAttackPower : skill.power;
        Color accent = skill == null ? new Color(0.5f, 0.5f, 0.58f) : skill.color;

        // 外枠（金フレーム。充電中は黄色く光る縁取りに）
        var frame = ProtoUI.CreatePanel("Card", _handArea, pos, new Vector2(200, 272),
            charged ? new Color(1f, 0.85f, 0.3f) : new Color(0.66f, 0.55f, 0.34f));
        var btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        btn.onClick.AddListener(() => onClick());
        btn.interactable = !_inputLocked;

        var colors = btn.colors;
        colors.highlightedColor = new Color(1.35f, 1.25f, 1.0f, 1f); // ホバーで金が輝く
        colors.pressedColor = new Color(0.8f, 0.75f, 0.65f, 1f);
        btn.colors = colors;

        // 内側（カード本体）
        var inner = ProtoUI.CreatePanel("Inner", frame.transform, Vector2.zero, new Vector2(190, 262),
            new Color(0.10f, 0.08f, 0.16f));
        inner.raycastTarget = false;

        // 名前帯（スキル色を暗くした帯＋カード名）
        var header = ProtoUI.CreatePanel("Header", inner.transform, new Vector2(0, 110), new Vector2(178, 36),
            Color.Lerp(accent, Color.black, 0.6f));
        header.raycastTarget = false;
        var nameText = ProtoUI.CreateText("Name", header.transform, cardName, 20, Vector2.zero, new Vector2(172, 34));
        nameText.fontStyle = TMPro.FontStyles.Bold;
        nameText.enableAutoSizing = true;     // 枠に収まる最大サイズに自動調整
        nameText.fontSizeMin = 10;
        nameText.fontSizeMax = 20;

        // マス数バッジ（小さな金縁）
        var badgeFrame = ProtoUI.CreatePanel("BadgeFrame", inner.transform, new Vector2(0, 78), new Vector2(80, 24),
            new Color(0.66f, 0.55f, 0.34f));
        badgeFrame.raycastTarget = false;
        var badge = ProtoUI.CreatePanel("Badge", badgeFrame.transform, Vector2.zero, new Vector2(76, 20),
            new Color(0.18f, 0.15f, 0.28f));
        badge.raycastTarget = false;
        ProtoUI.CreateText("BadgeText", badge.transform,
            skill == null ? "— 基本 —" : $"{skill.Size}マス", 12, Vector2.zero, new Vector2(76, 20));

        // アート枠（ピース形状の展示エリア）
        var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, -4), new Vector2(170, 122),
            new Color(0.05f, 0.04f, 0.10f));
        art.raycastTarget = false;

        if (skill != null)
        {
            float cs = 17f, gap = 2f;
            int minX = skill.shape.Min(v => v.x), minY = skill.shape.Min(v => v.y);
            int maxX = skill.shape.Max(v => v.x), maxY = skill.shape.Max(v => v.y);
            float ox = -(maxX - minX) * (cs + gap) / 2f;
            float oy = (maxY - minY) * (cs + gap) / 2f;
            foreach (var v in skill.shape)
            {
                ProtoUI.CreatePanel("Mas", art.transform,
                    new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)),
                    new Vector2(cs, cs), skill.color);
            }
        }
        else
        {
            ProtoUI.CreateText("Fist", art.transform, "たたく", 22, Vector2.zero, new Vector2(120, 50),
                new Color(0.6f, 0.6f, 0.7f));
        }

        // 威力帯（下部・ゴールド表記）
        var footer = ProtoUI.CreatePanel("Footer", inner.transform, new Vector2(0, -108), new Vector2(178, 30),
            new Color(0.16f, 0.13f, 0.24f));
        footer.raycastTarget = false;
        var powerText = ProtoUI.CreateText("Power", footer.transform, $"威力 {power}", 17, Vector2.zero, new Vector2(174, 30));
        ProtoUI.StyleTitle(powerText, ProtoUI.Gold, 2f);

        // 充電中バッジ（通電連鎖：使うとクリティカル）
        if (charged)
        {
            var chargeBadge = ProtoUI.CreatePanel("ChargeBadge", frame.transform, new Vector2(0, 150), new Vector2(150, 26),
                new Color(0.95f, 0.8f, 0.2f));
            chargeBadge.raycastTarget = false;
            var bt = ProtoUI.CreateText("ChargeText", chargeBadge.transform, "⚡充電中", 16, Vector2.zero, new Vector2(150, 26),
                new Color(0.2f, 0.12f, 0f));
            bt.fontStyle = TMPro.FontStyles.Bold;
        }

        return btn;
    }

    // ==================== ターン進行 ====================

    void OnCardClicked(int member, int index)
    {
        if (_inputLocked) return;
        if (_memberHP != null && _memberHP[member] <= 0) return; // 戦闘不能メンバーは動けない
        if (_acted[member]) return; // 行動済みメンバーは選べない
        _inputLocked = true;
        RefreshHand();
        StartCoroutine(PlayCard(member, index));
    }

    IEnumerator PlayCard(int member, int index)
    {
        // 行動するメンバーを切り替える（攻撃モーションはこのキャラが動く）
        _actorRt = _memberRts[member];
        _actorImg = _memberImgs[member];

        var skill = _hands[member][index];
        _hands[member].RemoveAt(index);

        float chainMult = 1f, flashMult = 1f;
        _chainSource = null; _chainWasCharged = false;
        int basePower = skill == null ? ProtoSkills.NormalAttackPower : skill.power;

        if (skill != null)
        {
            // ① 通電連鎖: 起点ピースを決定（充電済みならクリティカル）
            if (_main.ChainEnabled)
            {
                yield return SelectChainSource(member, skill);
                if (_chainWasCharged) chainMult = _main.ChainCritMult;
            }

            // ② 順次点滅チャレンジ: 発動条件を満たす大型ピースのみ
            if (_main.FlashEnabled && skill.Size >= _main.FlashThreshold)
            {
                yield return RunChallenge(skill);
                flashMult = _challengeMultiplier;
            }
        }
        else
        {
            _message.text = $"{_main.Party[member].name}の通常攻撃！";
            yield return new WaitForSeconds(0.5f);
        }

        float multiplier = chainMult * flashMult;
        bool isCritical = multiplier > 1.01f;

        // 回復・バフは敵を攻撃しない別処理へ分岐
        if (skill != null && skill.kind == SkillKind.Heal)
        {
            yield return ResolveHeal(member, skill, multiplier);
            yield return AfterAction(member, skill);
            yield break;
        }
        if (skill != null && skill.kind == SkillKind.Buff)
        {
            yield return ResolveBuff(member, skill);
            yield return AfterAction(member, skill);
            yield break;
        }

        // 技ごとの攻撃モーション → ヒットエフェクト（ダメージ数値付き）
        // ダメージ = (カード威力 + 行動メンバーの攻撃力 + バフ) × 倍率
        int atkStat = _main.MemberStats[member].Attack + (_buffAtk != null ? _buffAtk[member] : 0);
        int damage = Mathf.RoundToInt((basePower + atkStat) * multiplier);
        yield return AttackMotionFor(skill);
        yield return Impact(_slimeRt, _slimeImg, skill, damage, multiplier);

        _enemyHP = Mathf.Max(0, _enemyHP - damage);
        string critTag = isCritical ? "クリティカル！" : "";
        _message.text = $"{critTag}{(skill == null ? "通常攻撃" : skill.skillName)}で {damage} ダメージ！";
        RefreshAll();
        yield return new WaitForSeconds(1f);

        if (_enemyHP <= 0)
        {
            // EXP獲得とレベルアップ判定（パーティ全員が同じEXPをもらう）
            int expGain = _enemy.baseHP / 2 + 10 * _effWave; // 強い敵ほどEXPが多い
            var levelUps = new List<string>();
            for (int i = 0; i < _main.MemberStats.Count; i++)
            {
                int before = _main.MemberStats[i].Level;
                if (_main.MemberStats[i].GainExp(expGain))
                    levelUps.Add($"{_main.Party[i].name} Lv{before}→Lv{_main.MemberStats[i].Level}");
            }

            _resultText.text = $"{_enemy.enemyName}を倒した！";
            _resultSub.text = levelUps.Count > 0
                ? $"全員 EXP +{expGain}\nレベルアップ！　{string.Join("　", levelUps)}"
                : $"全員 EXP +{expGain}（次のレベルまで {_main.Stats.ExpToNext - _main.Stats.Exp}）";
            _resultRoot.gameObject.SetActive(true);
            yield break;
        }

        yield return AfterAction(member, skill);
    }

    // カード1枚の解決後の共通処理（攻撃/回復/バフ共通）:
    // 通電の伝播 → このメンバーを行動済みに → 全員行動したら敵のターン → 補充
    IEnumerator AfterAction(int member, ProtoSkill skill)
    {
        // 通電の伝播（充電を消費し、起点ピースの隣接を充電）
        ApplyChainPropagation(member);

        // このメンバーは行動済みに。まだ動けるメンバー（生存＆未行動）がいれば続行
        _acted[member] = true;
        bool allActed = true;
        for (int m = 0; m < _acted.Length; m++)
            if (!_acted[m] && _memberHP[m] > 0) allActed = false;

        if (!allActed)
        {
            _inputLocked = false;
            RefreshAll();
            _message.text = "つづけて仲間のカードを選ぼう！";
            yield break;
        }

        // 全員行動した → 敵のターン
        _message.text = $"{_enemy.enemyName}のターン…";
        yield return new WaitForSeconds(0.9f);
        yield return EnemyAttackSequence();

        if (_playerHP <= 0)
        {
            ShowDefeat();
            yield break;
        }

        // ターン経過: バフの残ターンを減らす
        TickBuffs();

        // 全員の手札を補充して次のターンへ
        for (int m = 0; m < _hands.Count; m++)
        {
            _acted[m] = false;
            _hands[m].Add(Draw(m));
        }
        _inputLocked = false;
        RefreshAll(dealAnimation: true);
        _message.text = "あなたのターン！カードを選ぼう！";
    }

    // ==================== 回復・バフ・通電連鎖 ====================

    // 回復スキル: 最もHP割合が低い生存メンバーを回復（倍率で回復量が増える）
    IEnumerator ResolveHeal(int member, ProtoSkill skill, float multiplier)
    {
        // 回復対象 = HP割合が最も低い生存メンバー（自分含む）
        int target = member;
        float worst = 2f;
        for (int i = 0; i < _memberHP.Length; i++)
        {
            if (_memberHP[i] <= 0) continue;
            float ratio = _memberHP[i] / (float)_main.MemberStats[i].MaxHP;
            if (ratio < worst) { worst = ratio; target = i; }
        }

        int heal = Mathf.RoundToInt(skill.power * multiplier);
        _message.text = $"{_main.Party[member].name}の{skill.skillName}！";
        yield return Pulse(_actorRt, 1.12f, 0.25f);
        StartCoroutine(FlashSprite(_memberImgs[target], new Color(0.5f, 1f, 0.7f)));

        int max = _main.MemberStats[target].MaxHP;
        _memberHP[target] = Mathf.Min(max, _memberHP[target] + heal);
        _playerHP = 0; foreach (var hp in _memberHP) _playerHP += hp;

        Vector2 pop = _memberRts[target].anchoredPosition + new Vector2(0, _memberRts[target].sizeDelta.y * 0.5f + 40f);
        StartCoroutine(DamagePopup(pop, heal, multiplier > 1.01f ? 1.5f : 1f));
        bool crit = multiplier > 1.01f;
        _message.text = $"{(crit ? "クリティカル！" : "")}{_main.Party[target].name}のHPが {heal} 回復！";
        RefreshAll();
        yield return new WaitForSeconds(1f);
    }

    // バフスキル: 行動メンバーに一時的な攻撃・防御上昇を付与
    IEnumerator ResolveBuff(int member, ProtoSkill skill)
    {
        _buffAtk[member] += skill.buffAtk;
        _buffDef[member] += skill.buffDef;
        _buffTurns[member] = Mathf.Max(_buffTurns[member], skill.buffTurns);

        _message.text = $"{_main.Party[member].name}の{skill.skillName}！";
        yield return Pulse(_actorRt, 1.15f, 0.3f);
        StartCoroutine(FlashSprite(_actorImg, new Color(0.6f, 0.85f, 1f)));
        SpawnBurst(_actorRt.anchoredPosition, skill.color, 14, 120f);
        _message.text = $"{_main.Party[member].name}の攻撃+{skill.buffAtk} 防御+{skill.buffDef}（{skill.buffTurns}ターン）！";
        RefreshAll();
        yield return new WaitForSeconds(1f);
    }

    // ターン終了時にバフの残ターンを減らし、切れたら効果を消す
    void TickBuffs()
    {
        if (_buffTurns == null) return;
        for (int i = 0; i < _buffTurns.Length; i++)
        {
            if (_buffTurns[i] <= 0) continue;
            _buffTurns[i]--;
            if (_buffTurns[i] <= 0) { _buffAtk[i] = 0; _buffDef[i] = 0; }
        }
    }

    // 通電: 使ったスキルの配置の中から起点を決める（充電済みなら自動/それ以外は盤面で選択）
    IEnumerator SelectChainSource(int member, ProtoSkill skill)
    {
        _chainSource = null; _chainWasCharged = false;

        var candidates = _main.Panels[member].Placements.FindAll(p => p.skill == skill);
        if (candidates.Count == 0) yield break; // 盤面に無い（通常攻撃など）

        var chargedCandidates = candidates.FindAll(p => _charged[member].Contains(p));

        if (candidates.Count == 1)
        {
            _chainSource = candidates[0];
        }
        else if (chargedCandidates.Count == 1)
        {
            // 充電済みが1つだけ → 自動で選択
            _chainSource = chargedCandidates[0];
        }
        else
        {
            // 複数候補 → 盤面オーバーレイでプレイヤーがタップ選択
            yield return ChooseChainPlacement(member, skill, candidates);
        }

        if (_chainSource != null)
            _chainWasCharged = _charged[member].Contains(_chainSource);
    }

    // 通電の伝播: 起点の充電を消費し、隣接する配置を充電する
    void ApplyChainPropagation(int member)
    {
        if (!_main.ChainEnabled || _chainSource == null) return;

        // 起点が充電済みだったら消費（クリティカルに使ったので放電）
        _charged[member].Remove(_chainSource);

        // 起点に隣接する別の配置を充電（辺接 or 頂点接は設定で切替）
        foreach (var p in _main.Panels[member].Placements)
        {
            if (p == _chainSource) continue;
            if (ArePlacementsAdjacent(_chainSource, p, _main.ChainCorner))
                _charged[member].Add(p);
        }
    }

    // 2つの配置が隣接しているか（corner=true: 頂点対角接も許可 / false: 辺接のみ）
    bool ArePlacementsAdjacent(PanelModel.Placement a, PanelModel.Placement b, bool corner)
    {
        foreach (var ca in a.cells)
            foreach (var cb in b.cells)
            {
                int dx = Mathf.Abs(ca.x - cb.x), dy = Mathf.Abs(ca.y - cb.y);
                if (corner)
                {
                    if (dx <= 1 && dy <= 1 && (dx + dy) > 0) return true; // 8近傍
                }
                else
                {
                    if (dx + dy == 1) return true; // 4近傍（辺接）
                }
            }
        return false;
    }

    // 複数候補があるとき、盤面オーバーレイで起点ピースをタップ選択させる
    IEnumerator ChooseChainPlacement(int member, ProtoSkill skill, List<PanelModel.Placement> candidates)
    {
        var panel = _main.Panels[member];
        PanelModel.Placement picked = null;

        var root = ProtoUI.CreateFullScreen("ChainSelect", _root);
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.8f);

        ProtoUI.CreateText("CTitle", root,
            $"「{skill.skillName}」を使う配置を選択（金枠=充電済み）", 26,
            new Vector2(0, 320), new Vector2(1100, 46), new Color(1f, 0.9f, 0.6f));

        float cs = 32f, gap = 3f, unit = cs + gap;
        var board = ProtoUI.CreateRect("CBoard", root);
        board.anchoredPosition = new Vector2(0, -10);

        for (int y = 0; y < panel.H; y++)
            for (int x = 0; x < panel.W; x++)
            {
                if (!panel.IsValid(x, y)) continue;
                var p = panel.GetAt(x, y);
                Vector2 pos = new Vector2(x * unit - (panel.W - 1) * unit / 2f,
                                          -(y * unit - (panel.H - 1) * unit / 2f));
                Color col = p == null ? new Color(0.16f, 0.14f, 0.24f) : p.skill.color;

                bool isCandidate = p != null && candidates.Contains(p);
                bool isCharged = p != null && _charged[member].Contains(p);
                if (isCharged) col = Color.Lerp(col, new Color(1f, 0.85f, 0.3f), 0.5f);
                if (isCandidate) col = Color.Lerp(col, Color.white, 0.25f);

                var cell = ProtoUI.CreatePanel($"CC_{x}_{y}", board, pos, new Vector2(cs, cs), col);

                if (isCandidate)
                {
                    // 候補ピースのマスをボタン化（金枠で強調）
                    var border = ProtoUI.CreatePanel("Bd", board, pos, new Vector2(cs + 4, cs + 4),
                        isCharged ? new Color(1f, 0.85f, 0.3f) : new Color(0.9f, 0.9f, 0.95f));
                    border.transform.SetAsFirstSibling();
                    var btn = cell.gameObject.AddComponent<Button>();
                    btn.targetGraphic = cell;
                    var sel = p;
                    btn.onClick.AddListener(() => picked = sel);
                }
                else
                {
                    cell.raycastTarget = false;
                }
            }

        // タップされるまで待つ
        while (picked == null) yield return null;

        _chainSource = picked;
        Destroy(root.gameObject);
    }

    // 敵の攻撃1回ぶん（技を抽選 → 連続ヒット対応 → 回避/防御判定）両モード共通
    IEnumerator EnemyAttackSequence()
    {
        var atk = ProtoEnemies.PickAttack(_enemy);
        _message.text = $"{_enemy.enemyName}の {atk.name}！";
        yield return new WaitForSeconds(0.5f);

        // 様子見・タメ行動（攻撃しない技）
        if (atk.hits == 0)
        {
            yield return Pulse(_slimeRt, 1.08f, 0.25f);
            _message.text = "しかし何も起こらなかった！";
            yield return new WaitForSeconds(0.8f);
            yield break;
        }

        _sfx.PlayOneShot(_swingClip); // 風切り音（ヒュッ！）
        yield return Lunge(_slimeRt, new Vector2(-150, 0));

        // 強い技ほど重いヒット音
        int sfxTier = atk.mult >= 1.5f ? 2 : atk.hits > 1 ? 0 : 1;

        for (int h = 0; h < atk.hits; h++)
        {
            // 生きているメンバーの中からランダムに狙う（防御と回避は本人の値）
            var alive = new List<int>();
            for (int i = 0; i < _memberHP.Length; i++)
                if (_memberHP[i] > 0) alive.Add(i);
            if (alive.Count == 0) yield break;
            int target = alive[Random.Range(0, alive.Count)];

            string targetName = _main.Party[target].name;
            var targetStats = _main.MemberStats[target];

            int raw = Mathf.RoundToInt(Random.Range(_enemy.minAtk, _enemy.maxAtk + 1) * atk.mult)
                      + 3 * (_effWave - 1);
            int defStat = targetStats.Defense + (_buffDef != null ? _buffDef[target] : 0);
            int dmg = Mathf.Max(1, raw - defStat);
            bool dodged = Random.Range(0, 100) < targetStats.Speed * 2; // 素早さ×2 %で回避

            if (dodged)
            {
                _message.text = $"{targetName}はひらりとかわした！";
                yield return new WaitForSeconds(0.55f);
                continue;
            }

            yield return Impact(_memberRts[target], _memberImgs[target], null, dmg, 1f, sfxTier);
            _memberHP[target] = Mathf.Max(0, _memberHP[target] - dmg);
            _playerHP = 0;
            foreach (var hp in _memberHP) _playerHP += hp; // 合計を再計算（敗北判定用）

            _message.text = atk.hits > 1
                ? $"{h + 1}ヒット！{targetName}は {dmg} のダメージ！"
                : $"{targetName}は {dmg} のダメージ！";
            RefreshAll();

            // 戦闘不能になったらキャラを暗くして手札も使用不可に
            if (_memberHP[target] <= 0)
            {
                _memberImgs[target].color = new Color(0.4f, 0.35f, 0.45f);
                _message.text = $"{targetName}は力尽きた…！";
                yield return new WaitForSeconds(0.7f);
            }

            yield return new WaitForSeconds(0.45f);
            if (_playerHP <= 0) yield break;
        }
        yield return new WaitForSeconds(0.4f);
    }

    void ShowDefeat()
    {
        _resultText.text = _main.Party.Count > 1 ? "パーティは倒れてしまった…" : "MAMAは倒れてしまった…";
        _resultSub.text = "";
        _resultRoot.gameObject.SetActive(true);
    }

    // ==================== 順次点滅チャレンジ（Simon式：順番・位置の再現） ====================

    float _challengeMultiplier;
    const float FlashOnTime = 0.45f;   // 1マスの点灯時間
    const float FlashGapTime = 0.18f;  // 点灯と点灯の間（合計 <3Hz で光過敏性配慮）

    IEnumerator RunChallenge(ProtoSkill skill)
    {
        _challengeRoot.gameObject.SetActive(true);
        _challengeInput.gameObject.SetActive(false); // 数値入力は使わない（タップ再現方式）
        _challengePrompt.text = $"「{skill.skillName}」発動！　光る順番を覚えろ…！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        // ピース形状を中央に描画（マスをタップできるようButton化）
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        var cellImages = new List<Image>();
        var baseColors = new List<Color>();
        float cs = 54f, gap = 5f;
        int minX = skill.shape.Min(v => v.x), minY = skill.shape.Min(v => v.y);
        int maxX = skill.shape.Max(v => v.x), maxY = skill.shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f;
        float oy = (maxY - minY) * (cs + gap) / 2f;

        var playerTaps = new List<int>();
        bool inputOpen = false;

        for (int i = 0; i < skill.shape.Length; i++)
        {
            var v = skill.shape[i];
            Color baseCol = Color.Lerp(skill.color, Color.black, 0.45f);
            var img = ProtoUI.CreatePanel("PCell", _pieceArea,
                new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)),
                new Vector2(cs, cs), baseCol);
            cellImages.Add(img);
            baseColors.Add(baseCol);

            int idx = i;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (!inputOpen) return;
                playerTaps.Add(idx);
                StartCoroutine(TapFeedback(img, baseColors[idx])); // タップしたマスを一瞬光らせる
            });
        }

        yield return new WaitForSeconds(0.7f);

        // 点灯シーケンスを生成（マス数が多いほど長い。順番・位置の両方を覚える）
        int seqLen = Mathf.Clamp(Mathf.RoundToInt(skill.Size * 0.5f), 4, 8);
        seqLen = Mathf.Min(seqLen, cellImages.Count);
        var seq = new List<int>();
        var pool = Enumerable.Range(0, cellImages.Count).OrderBy(_ => Random.value).ToList();
        for (int i = 0; i < seqLen; i++) seq.Add(pool[i]); // 重複なしの順番

        // 順番に1マスずつ点灯（Simon式の提示フェーズ）
        for (int i = 0; i < seq.Count; i++)
        {
            int ci = seq[i];
            cellImages[ci].color = Color.white;
            yield return new WaitForSeconds(FlashOnTime);
            cellImages[ci].color = baseColors[ci];
            yield return new WaitForSeconds(FlashGapTime);
        }

        // 再現フェーズ：同じ順番でタップ
        _challengePrompt.text = $"同じ順番で {seqLen} マスをタップ！";
        inputOpen = true;

        float remaining = AnswerTime + seqLen * 0.6f; // 長いシーケンスほど時間に余裕を持たせる
        float total = remaining;
        while (remaining > 0f && playerTaps.Count < seqLen)
        {
            remaining -= Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, remaining / total, 500f);
            yield return null;
        }
        inputOpen = false;

        // 採点：先頭から連続して正解できた数で倍率を決める
        int correct = 0;
        for (int i = 0; i < seqLen && i < playerTaps.Count; i++)
        {
            if (playerTaps[i] == seq[i]) correct++;
            else break;
        }
        float frac = seqLen > 0 ? correct / (float)seqLen : 0f;
        _challengeMultiplier = Mathf.Lerp(1f, _main.FlashCritMult, frac);

        // 正解の順番をハイライトしながら結果表示
        string msg = correct >= seqLen
            ? $"パーフェクト！クリティカル！（威力{Mathf.RoundToInt(_challengeMultiplier * 100)}%）"
            : correct == 0
                ? "失敗…通常威力"
                : $"{correct}/{seqLen} 正解 → 威力{Mathf.RoundToInt(_challengeMultiplier * 100)}%";
        _challengePrompt.text = msg;

        yield return new WaitForSeconds(1.1f);
        _challengeRoot.gameObject.SetActive(false);
    }

    // タップしたマスを一瞬だけ明るくするフィードバック
    IEnumerator TapFeedback(Image img, Color baseCol)
    {
        img.color = Color.Lerp(baseCol, Color.white, 0.7f);
        yield return new WaitForSeconds(0.15f);
        img.color = baseCol;
    }
}
