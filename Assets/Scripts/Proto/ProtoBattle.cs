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
    TextMeshProUGUI _playerName, _enemyName, _playerHPText, _enemyHPText, _message, _deckText;
    Image _playerFill, _enemyFill;
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
    List<ProtoSkill> _deck = new List<ProtoSkill>();
    List<ProtoSkill> _hand = new List<ProtoSkill>();
    bool _inputLocked;
    int _challengeAnswer; // -1=時間切れ, それ以外=選んだ数
    bool _challengeDone;

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        Hide();
    }

    public void Hide()
    {
        // 進行中のターン処理・チャレンジ・エフェクトをすべて止めてから隠す
        // （途中でビルドへ戻っても、次のBegin()で完全リセットされる）
        StopAllCoroutines();
        _inputLocked = false;
        _root.gameObject.SetActive(false);
    }

    public void Begin()
    {
        _root.gameObject.SetActive(true);
        _resultRoot.gameObject.SetActive(false);
        _challengeRoot.gameObject.SetActive(false);

        _playerMaxHP = 120;
        _playerHP = _playerMaxHP;
        _enemyMaxHP = 100 + 50 * (_main.Wave - 1); // Waveごとに敵が強くなる
        _enemyHP = _enemyMaxHP;
        _enemyName.text = $"ドラゴン Lv{_main.Wave}";

        _deck = Shuffle(_main.Panel.BuildDeck());
        _hand.Clear();
        for (int i = 0; i < HandSize; i++) _hand.Add(Draw());

        _inputLocked = false;
        RefreshAll();
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

        // プレイヤーHUD（左上）
        _playerName = ProtoUI.CreateText("PName", _root, "MAMA", 28, new Vector2(-560, 410), new Vector2(300, 40));
        ProtoUI.StyleTitle(_playerName, new Color(0.96f, 0.93f, 1f));
        ProtoUI.CreateGauge("PGauge", _root, new Vector2(-560, 370), new Vector2(GaugeWidth, 20),
            new Color(0.15f, 0.12f, 0.2f), new Color(0.8f, 0.4f, 1f), out _playerFill);
        _playerHPText = ProtoUI.CreateText("PHP", _root, "", 17, new Vector2(-560, 370), new Vector2(300, 26));
        _playerHPText.fontStyle = TMPro.FontStyles.Bold;

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

        // ビルドへ戻るボタン（戦闘は破棄してやり直し）
        ProtoUI.CreateButton("RetreatBtn", _root, "← ビルドへ戻る", 16,
            new Vector2(-690, -410), new Vector2(180, 46), new Color(0.25f, 0.22f, 0.38f),
            () => _main.ShowBuild());

        // キャラクター（ドット絵）。外側=モーション用 / 内側=アイドルアニメ用 の2層構造
        _mamaImg = CreateCharacterSprite("MamaSprite", ProtoPixelArt.Mama(), new Vector2(-470, 40), new Vector2(400, 475));
        _slimeImg = CreateCharacterSprite("DragonSprite", ProtoPixelArt.Dragon(), new Vector2(460, 70), new Vector2(480, 330));
        _mamaRt = (RectTransform)_mamaImg.transform.parent;
        _slimeRt = (RectTransform)_slimeImg.transform.parent;
        _mamaInner = (RectTransform)_mamaImg.transform;
        _enemyInner = (RectTransform)_slimeImg.transform;

        // メッセージ欄
        var msgBox = ProtoUI.CreatePanel("MsgBox", _root, new Vector2(0, -130), new Vector2(1300, 56),
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
        _handArea.sizeDelta = new Vector2(1200, 220);

        // ---- 点滅チャレンジ用オーバーレイ ----
        _challengeRoot = ProtoUI.CreateFullScreen("Challenge", _root);
        var dim = _challengeRoot.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.75f);

        _challengePrompt = ProtoUI.CreateText("CPrompt", _challengeRoot, "", 30, new Vector2(0, 260), new Vector2(900, 50));
        _pieceArea = ProtoUI.CreateRect("PieceArea", _challengeRoot);
        _pieceArea.anchoredPosition = new Vector2(0, 40);
        _challengeInput = ProtoUI.CreateInputField("AnswerInput", _challengeRoot, new Vector2(0, -200), new Vector2(240, 70), 32);
        _challengeInput.onSubmit.AddListener(OnChallengeSubmit);
        ProtoUI.CreateGauge("Timer", _challengeRoot, new Vector2(0, -290), new Vector2(500, 14),
            new Color(0.2f, 0.18f, 0.28f), new Color(1f, 0.85f, 0.3f), out _timerFill);

        // ---- 勝敗オーバーレイ ----
        _resultRoot = ProtoUI.CreateFullScreen("Result", _root);
        var rdim = _resultRoot.gameObject.AddComponent<Image>();
        rdim.color = new Color(0, 0, 0, 0.8f);
        _resultText = ProtoUI.CreateText("RText", _resultRoot, "", 60, new Vector2(0, 60), new Vector2(800, 90));
        ProtoUI.StyleTitle(_resultText, ProtoUI.Gold, 10f);
        ProtoUI.CreateButton("BackBtn", _resultRoot, "ビルド画面へ", 24,
            new Vector2(0, -60), new Vector2(280, 64), new Color(0.35f, 0.3f, 0.55f),
            () => { if (_enemyHP <= 0) _main.OnBattleWon(); else _main.ShowBuild(); });
    }

    // ==================== ドロー ====================

    ProtoSkill Draw()
    {
        if (_deck.Count == 0)
            _deck = Shuffle(_main.Panel.BuildDeck()); // 引き切り: 使い切ったら全100枚を再シャッフル
        var top = _deck[0];
        _deck.RemoveAt(0);
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

    void RefreshAll()
    {
        ProtoUI.SetGauge(_playerFill, (float)_playerHP / _playerMaxHP, GaugeWidth);
        ProtoUI.SetGauge(_enemyFill, (float)_enemyHP / _enemyMaxHP, GaugeWidth);
        _playerHPText.text = $"{_playerHP}/{_playerMaxHP}";
        _enemyHPText.text = $"{_enemyHP}/{_enemyMaxHP}";
        _deckText.text = $"— Wave {_main.Wave} —";
        RefreshHand();
    }

    Image _mamaImg, _slimeImg;
    RectTransform _mamaRt, _slimeRt;       // 外側（攻撃モーションが動かす）
    RectTransform _mamaInner, _enemyInner; // 内側（アイドルアニメが動かす）

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

    // 待機中のアイドルアニメ（呼吸のゆれ）
    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;
        float t = Time.time;

        if (_mamaInner != null)
        {
            // MAMA: 上下＋わずかな伸縮（呼吸）
            _mamaInner.anchoredPosition = new Vector2(0, Mathf.Sin(t * 3.0f) * 4f);
            _mamaInner.localScale = new Vector3(1f, 1f + Mathf.Sin(t * 3.0f) * 0.012f, 1f);
        }
        if (_enemyInner != null)
        {
            // ドラゴン: ふわふわ浮遊（位相をずらして同期感をなくす）
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
            yield return Lunge(_mamaRt, new Vector2(150, 0)); // 通常攻撃は踏み込み
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
            default:       yield return Lunge(_mamaRt, new Vector2(150, 0)); break;
        }
    }

    // マザーフレア: タメ → 火球を投射
    IEnumerator MotionFlare(ProtoSkill skill)
    {
        StartCoroutine(FlashSprite(_mamaImg, new Color(1f, 0.85f, 0.55f)));
        yield return Pulse(_mamaRt, 1.1f, 0.22f);
        yield return Projectile(skill.color, 38f, 0.32f, false);
    }

    // クリップスティンガー: 2連ジャブ＋2連射
    IEnumerator MotionStinger(ProtoSkill skill)
    {
        for (int i = 0; i < 2; i++)
        {
            yield return Jab(_mamaRt, new Vector2(70, 0), 0.07f);
            StartCoroutine(Projectile(skill.color, 20f, 0.18f, false));
            yield return new WaitForSeconds(0.12f);
        }
        yield return new WaitForSeconds(0.1f);
    }

    // アクアスラッシュ: 敵を斬り抜ける居合ダッシュ
    IEnumerator MotionSlash(ProtoSkill skill)
    {
        Vector2 origin = _mamaRt.anchoredPosition;
        Vector2 through = _slimeRt.anchoredPosition + new Vector2(170, 0); // 敵の向こう側へ

        // 一瞬の構え
        yield return Jab(_mamaRt, new Vector2(-40, 0), 0.1f);

        // 高速で斬り抜ける（残像スパーク付き）
        float t = 0f;
        while (t < 0.16f)
        {
            t += Time.deltaTime;
            float p = t / 0.16f;
            _mamaRt.anchoredPosition = Vector2.Lerp(origin, through, p * p);
            if (Random.value < 0.5f)
                SpawnBurst(_mamaRt.anchoredPosition, new Color(skill.color.r, skill.color.g, skill.color.b, 0.6f), 1, 30f);
            yield return null;
        }

        // 斬撃線を敵の上に表示
        SpawnSlashLine(_slimeRt.anchoredPosition, skill.color);
        yield return new WaitForSeconds(0.25f);

        // 元の位置へ戻る
        t = 0f;
        Vector2 back = _mamaRt.anchoredPosition;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            _mamaRt.anchoredPosition = Vector2.Lerp(back, origin, Mathf.SmoothStep(0, 1, t / 0.2f));
            yield return null;
        }
        _mamaRt.anchoredPosition = origin;
    }

    // サイクロンバースト: その場で高速スピン → 蛇行する竜巻弾
    IEnumerator MotionCyclone(ProtoSkill skill)
    {
        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            _mamaRt.localRotation = Quaternion.Euler(0, 0, -720f * (t / 0.45f));
            if (Random.value < 0.4f)
                SpawnBurst(_mamaRt.anchoredPosition, skill.color, 1, 80f);
            yield return null;
        }
        _mamaRt.localRotation = Quaternion.identity;
        yield return Projectile(skill.color, 34f, 0.4f, true); // 蛇行弾
    }

    // ジャッジメントボイス: 仁王立ちで衝撃波リング3連
    IEnumerator MotionVoice(ProtoSkill skill)
    {
        yield return Pulse(_mamaRt, 1.15f, 0.18f);
        for (int i = 0; i < 3; i++)
        {
            StartCoroutine(RingWave(_mamaRt.anchoredPosition + new Vector2(80, 60), _slimeRt.anchoredPosition, skill.color));
            StartCoroutine(Pulse(_mamaRt, 1.08f, 0.12f));
            yield return new WaitForSeconds(0.16f);
        }
        yield return new WaitForSeconds(0.25f);
    }

    // アスラ・レガリア: 大ジャンプ → 敵へ急降下プレス
    IEnumerator MotionAsura(ProtoSkill skill)
    {
        Vector2 origin = _mamaRt.anchoredPosition;
        Vector2 apex = origin + new Vector2(120, 320);
        Vector2 slam = _slimeRt.anchoredPosition + new Vector2(-60, 40);

        // 沈み込み → 大ジャンプ
        yield return Jab(_mamaRt, new Vector2(0, -30), 0.12f);
        float t = 0f;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            _mamaRt.anchoredPosition = Vector2.Lerp(origin, apex, Mathf.Sin(t / 0.25f * Mathf.PI * 0.5f));
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
            _mamaRt.anchoredPosition = Vector2.Lerp(apex, slam, p * p);
            _mamaRt.localRotation = Quaternion.Euler(0, 0, -25f * p);
            yield return null;
        }
        SpawnBurst(slam, new Color(1f, 1f, 1f, 0.8f), 8, 140f); // 着地の土煙

        yield return new WaitForSeconds(0.2f);

        // 戻る
        t = 0f;
        Vector2 back = _mamaRt.anchoredPosition;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0, 1, t / 0.25f);
            _mamaRt.anchoredPosition = Vector2.Lerp(back, origin, p);
            _mamaRt.localRotation = Quaternion.Euler(0, 0, -25f * (1f - p));
            yield return null;
        }
        _mamaRt.anchoredPosition = origin;
        _mamaRt.localRotation = Quaternion.identity;
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
        Vector2 from = _mamaRt.anchoredPosition + new Vector2(130, 50);
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
    IEnumerator Impact(RectTransform target, Image targetImg, ProtoSkill skill, bool critical)
    {
        int size = skill?.Size ?? 1;
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

        // クリティカルは少し遅れて二段目の爆発
        if (critical)
            StartCoroutine(DelayedBurst(target.anchoredPosition, burstColor, count / 2, radius * 0.8f, 0.15f));

        yield return Shake(target, shakeAmp, shakeDur);
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

    void RefreshHand()
    {
        foreach (Transform child in _handArea) Destroy(child.gameObject);

        float xs = -(_hand.Count - 1) * 230f / 2f;
        for (int i = 0; i < _hand.Count; i++)
        {
            int idx = i;
            var skill = _hand[i];
            var card = CreateCardUI(skill, new Vector2(xs + i * 230f, 0), () => OnCardClicked(idx));
        }
    }

    // トレーディングカード風のカードUI（金フレーム＋名前帯＋アート枠＋威力帯）
    Button CreateCardUI(ProtoSkill skill, Vector2 pos, System.Action onClick)
    {
        string cardName = skill == null ? "通常攻撃" : skill.skillName;
        int power = skill == null ? ProtoSkills.NormalAttackPower : skill.power;
        Color accent = skill == null ? new Color(0.5f, 0.5f, 0.58f) : skill.color;

        // 外枠（金フレーム）
        var frame = ProtoUI.CreatePanel("Card", _handArea, pos, new Vector2(200, 272),
            new Color(0.66f, 0.55f, 0.34f));
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
        var nameText = ProtoUI.CreateText("Name", header.transform, cardName, 14, Vector2.zero, new Vector2(174, 36));
        nameText.fontStyle = TMPro.FontStyles.Bold;

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

        return btn;
    }

    // ==================== ターン進行 ====================

    void OnCardClicked(int index)
    {
        if (_inputLocked) return;
        _inputLocked = true;
        RefreshHand();
        StartCoroutine(PlayCard(index));
    }

    IEnumerator PlayCard(int index)
    {
        var skill = _hand[index];
        _hand.RemoveAt(index);

        float multiplier = 1f;
        int basePower = skill == null ? ProtoSkills.NormalAttackPower : skill.power;

        if (skill != null)
        {
            yield return RunChallenge(skill);
            multiplier = _challengeMultiplier;
        }
        else
        {
            _message.text = "MAMAの通常攻撃！";
            yield return new WaitForSeconds(0.5f);
        }

        // 技ごとの攻撃モーション → ヒットエフェクト
        bool isCritical = multiplier >= 2f;
        yield return AttackMotionFor(skill);
        yield return Impact(_slimeRt, _slimeImg, skill, isCritical);

        int damage = Mathf.RoundToInt(basePower * multiplier);
        _enemyHP = Mathf.Max(0, _enemyHP - damage);
        string critTag = isCritical ? "クリティカル！" : "";
        _message.text = $"{critTag}{(skill == null ? "通常攻撃" : skill.skillName)}で {damage} ダメージ！";
        RefreshAll();
        yield return new WaitForSeconds(1f);

        if (_enemyHP <= 0)
        {
            _resultText.text = $"Wave {_main.Wave} クリア！";
            _resultRoot.gameObject.SetActive(true);
            yield break;
        }

        // 敵のターン
        _message.text = "ドラゴンのターン…";
        yield return new WaitForSeconds(0.9f);

        // 敵の攻撃モーション → 被弾エフェクト
        yield return Lunge(_slimeRt, new Vector2(-150, 0));
        yield return Impact(_mamaRt, _mamaImg, null, false);

        int enemyDamage = Random.Range(8, 17) + 4 * (_main.Wave - 1);
        _playerHP = Mathf.Max(0, _playerHP - enemyDamage);
        _message.text = $"ドラゴンの攻撃！MAMAは {enemyDamage} のダメージ！";
        RefreshAll();
        yield return new WaitForSeconds(1f);

        if (_playerHP <= 0)
        {
            _resultText.text = "MAMAは倒れてしまった…";
            _resultRoot.gameObject.SetActive(true);
            yield break;
        }

        // 手札補充して次のターンへ
        _hand.Add(Draw());
        _inputLocked = false;
        RefreshAll();
        _message.text = "あなたのターン！カードを選ぼう！";
    }

    // ==================== 点滅カウント・チャレンジ ====================

    float _challengeMultiplier;

    IEnumerator RunChallenge(ProtoSkill skill)
    {
        _challengeRoot.gameObject.SetActive(true);
        _challengePrompt.text = $"「{skill.skillName}」発動！　光るマスを数えろ…！";
        _challengeInput.gameObject.SetActive(false);
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        // ピース形状を中央に描画
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        var cellImages = new List<Image>();
        float cs = 56f, gap = 5f;
        int minX = skill.shape.Min(v => v.x), minY = skill.shape.Min(v => v.y);
        int maxX = skill.shape.Max(v => v.x), maxY = skill.shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f;
        float oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in skill.shape)
        {
            var img = ProtoUI.CreatePanel("PCell", _pieceArea,
                new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)),
                new Vector2(cs, cs), Color.Lerp(skill.color, Color.black, 0.45f));
            cellImages.Add(img);
        }

        yield return new WaitForSeconds(0.7f);

        // 単発フラッシュ（点滅は1回のみ・0.5秒。光過敏性ガイドライン準拠）
        int flashCount = Random.Range(1, skill.Size + 1);
        var order = Enumerable.Range(0, cellImages.Count).OrderBy(_ => Random.value).ToList();
        var flashed = order.Take(flashCount).Select(i => cellImages[i]).ToList();
        var original = flashed.Select(img => img.color).ToList();
        foreach (var img in flashed) img.color = Color.white;
        yield return new WaitForSeconds(FlashDuration);
        for (int i = 0; i < flashed.Count; i++) flashed[i].color = original[i];

        // 手入力（キーボードで数字を打ってEnter）
        _challengePrompt.text = "何マス光った？　数字を入力してEnter！";
        _challengeAnswer = -1;
        _challengeDone = false;

        _challengeInput.gameObject.SetActive(true);
        _challengeInput.text = "";
        _challengeInput.ActivateInputField();

        // 制限時間
        float remaining = AnswerTime;
        while (remaining > 0f && !_challengeDone)
        {
            remaining -= Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, remaining / AnswerTime, 500f);
            yield return null;
        }

        // 倍率判定
        string msg;
        if (_challengeAnswer < 0)
        {
            _challengeMultiplier = 1f;
            msg = $"時間切れ！正解は {flashCount}。通常威力！";
        }
        else
        {
            int diff = Mathf.Abs(_challengeAnswer - flashCount);
            _challengeMultiplier = diff == 0 ? 2f : diff == 1 ? 1.5f : diff == 2 ? 1.2f : 1f;
            msg = diff == 0
                ? $"ジャスト {flashCount}！クリティカル！！（威力200%）"
                : $"正解は {flashCount}（{diff}ずれ）→ 威力{Mathf.RoundToInt(_challengeMultiplier * 100)}%";
        }

        _challengePrompt.text = msg;
        _challengeInput.DeactivateInputField();
        _challengeInput.gameObject.SetActive(false);
        yield return new WaitForSeconds(1.1f);
        _challengeRoot.gameObject.SetActive(false);
    }

    void OnChallengeSubmit(string text)
    {
        if (_challengeDone) return;
        if (int.TryParse(text, out int value))
        {
            _challengeAnswer = value;
            _challengeDone = true;
        }
        else
        {
            // 数字以外はクリアして再入力
            _challengeInput.text = "";
            _challengeInput.ActivateInputField();
        }
    }
}
