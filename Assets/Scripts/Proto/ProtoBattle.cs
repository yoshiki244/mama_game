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
    RectTransform _boardOverlay;   // 盤面プレビュー（キャラ左横・常時表示）
    RectTransform _boardContent;   // 盤面の動的中身（マス）
    RectTransform _detailPanel;    // カード詳細（中央・ホバー時）
    RectTransform _detailContent;  // カード詳細の動的中身
    TextMeshProUGUI _boardLabel;   // 盤面の説明ラベル
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
    int _overdrive;      // オーバードライブ：同ターン内に攻撃するたび+1（次の攻撃が+25%/スタック）
    readonly bool[] _slotStopReq = new bool[3];  // 各リールのSTOP要求（独立）

    // オーバードライブの1スタックあたり上昇量（連撃のペンダントで+10%）
    float OdPerStack => GameBalance.OverdrivePerStack + (_main.Equipped == EquipKind.ComboPendant ? 0.10f : 0f);
    int OdPctInt => Mathf.RoundToInt(OdPerStack * 100f);
    int _strength;       // 攻撃力上昇（戦闘中持続）
    int _protectPct;     // 次の被弾を%軽減（1回）
    int _weakPct, _weakTurns; // 敵の攻撃力低下
    int _poison;         // 敵への毒
    int _burn;           // 敵のやけど（毎ターン amount ダメージ）
    TextMeshProUGUI _enemyStatusText; // 敵HPゲージ横の状態異常表示
    bool _primeBlink;    // 次のアタックで点滅

    // 拡張効果の状態
    int _stunTurns;                       // 敵の行動不能ターン
    int _regenAmt, _regenTurns;           // 毎ターン回復
    int _thornsDmg, _thornsTurns;         // 被弾時反撃
    int _blockRegenAmt, _blockRegenTurns; // 毎ターンブロック
    int _reflectPct;                      // 次の被弾を軽減＆反射（1回）
    int _guardPct, _guardTurns;           // 継続被ダメ軽減
    int _counterDmg;                      // 次の被弾で反撃（1回）
    int _vulnPct, _vulnTurns;             // 敵の被ダメ増加
    int _ailmentAmp;                      // 毒・やけど強化（この戦闘中）
    int _nextTurnExtra;                   // 次ターンの手札追加枚数
    int _timeBombDmg, _timeBombTurns;     // 時限爆弾
    readonly Dictionary<string, int> _useCounts = new Dictionary<string, int>(); // GrowingPower用

    EnemyAttackDef _intent;               // 敵の次の行動（インテント）
    TextMeshProUGUI _intentText; Image _intentBg; GameObject _intentBadge; // インテント表示
    ProtoMain.Synergy _syn;               // 盤面シナジー（戦闘開始時に確定）

    // 敵ギミックの状態
    int _enemyBlock;      // 敵のブロック（プレイヤーの攻撃を吸収。敵ターン開始でリセット）
    int _enemyAtkUp;      // 敵の攻撃力上昇（戦闘中持続）
    bool _enemyCharged;   // チャージ中（次の攻撃1.8倍）
    int _playerPoison;    // プレイヤーが受けた毒（自ターン開始にダメージ、毎ターン1減衰）

    AudioSource _sfx;
    AudioClip[] _hitClips;
    AudioClip _swingClip, _coinClip, _curseClip, _parryClip;
    AudioClip _magicCastClip, _bigChargeClip, _bigReleaseClip, _healCastClip, _guardClip;
    AudioClip _reachClip, _heartbeatClip, _fanfareClip, _coinShowerClip;

    Image _actorImg, _slimeImg, _faceImg;
    RectTransform _actorRt, _slimeRt, _enemyInner, _playerInner, _enemyShadow;

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        _sfx = gameObject.AddComponent<AudioSource>();
        _hitClips = new[] { ProtoAudio.CreateHitClip(0), ProtoAudio.CreateHitClip(1), ProtoAudio.CreateHitClip(2), ProtoAudio.CreateHitClip(3) };
        _swingClip = ProtoAudio.CreateSwing();
        _coinClip = ProtoAudio.CreateCoinChime();
        _curseClip = ProtoAudio.CreateCurseHit();
        _parryClip = ProtoAudio.CreateSpecialChime();
        _magicCastClip = ProtoAudio.CreateMagicCast();
        _bigChargeClip = ProtoAudio.CreateBigCharge();
        _bigReleaseClip = ProtoAudio.CreateBigRelease();
        _healCastClip = ProtoAudio.CreateHealCast();
        _guardClip = ProtoAudio.CreateGuard();
        _reachClip = ProtoAudio.CreateReachAlarm();
        _heartbeatClip = ProtoAudio.CreateHeartbeat();
        _fanfareClip = ProtoAudio.CreateJackpotFanfare();
        _coinShowerClip = ProtoAudio.CreateCoinShower();
        Hide();
    }

    public void Hide()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        if (_main != null && _main.Panel != null) _main.Panel.Sealed.Clear();   // 歪みマスを持ち越さない
        HideBlessingPopup();
        ReleaseCardTextures();   // 焼き込みカード画像を解放
        _inputLocked = false;
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ==================== 開始 ====================

    public void Begin(EnemyDef enemy)
    {
        _enemy = enemy;
        // 中ボス以上（歪みマスを生む強敵）か判定
        _eliteBattle = enemy != null && (enemy.id == "dragon" || enemy.id.StartsWith("boss_") || enemy.id.StartsWith("midboss_"));
        _emptyReduce = 0;
        if (_main != null && _main.Panel != null)
        {
            _main.Panel.Sealed.Clear();   // 歪みマスは戦闘ごとにリセット
            // 歪みの契約：毎戦闘この数だけ歪みマスを確定発生
            var seedPool = _main.Panel.GetUnlockedCells();
            for (int i = 0; i < _main.CursedSeals && seedPool.Count > 0; i++)
            {
                int di = Random.Range(0, seedPool.Count);
                _main.Panel.Sealed.Add(seedPool[di]); seedPool.RemoveAt(di);
            }
        }
        _root.gameObject.SetActive(true);
        _resultRoot.gameObject.SetActive(false);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);

        // キャラ立ち絵・顔・位置サイズを通常に戻す（前回の倒れ絵/被弾絵をリセット）
        _dead = false;
        _bigCastActive = false;
        _castActive = false;
        ClearLingeringFx();   // 前回の大技で残った魔力エフェクトを掃除
        if (_actorImg != null) { _actorImg.sprite = ProtoPixelArt.MamaPhoto(); _actorImg.color = Color.white; }
        if (_faceImg != null) _faceImg.sprite = ProtoPixelArt.FrontMama();
        if (_actorRt != null) { _actorRt.anchoredPosition = new Vector2(-330, GroundY + 205f); _actorRt.sizeDelta = new Vector2(295, 375); _actorRt.localRotation = Quaternion.identity; _actorRt.localScale = Vector3.one; }
        _actorHome = new Vector2(-330, GroundY + 205f);   // 立ち位置の基準（足元固定の拡大に使用）
        if (_playerInner != null) _playerInner.sizeDelta = new Vector2(295, 375);

        // 残っているGAME OVERオーバーレイがあれば消す
        var oldGo = _root.Find("GameOver");
        if (oldGo != null) Destroy(oldGo.gameObject);

        // 前回の戦闘でHide()のStopAllCoroutinesにより破棄されず残った演出FXを掃除
        for (int i = _root.childCount - 1; i >= 0; i--)
        {
            string nm = _root.GetChild(i).name;
            if (nm == "DamagePopup" || nm == "Shock" || nm == "ScreenFlash"
                || nm == "Spark" || nm == "Ring" || nm == "Projectile" || nm == "Slash")
                Destroy(_root.GetChild(i).gameObject);
        }

        _playerMaxHP = _main.MaxHP;
        _playerHP = Mathf.Clamp(_main.CurrentHP, 1, _playerMaxHP); // 前回の戦闘後HPを継続
        _block = 0; _strength = 0; _protectPct = 0; _weakPct = 0; _weakTurns = 0; _poison = 0; _burn = 0;
        _manaBoostNext = 0; _primeBlink = false;
        _stunTurns = 0; _regenAmt = 0; _regenTurns = 0; _thornsDmg = 0; _thornsTurns = 0;
        _blockRegenAmt = 0; _blockRegenTurns = 0; _reflectPct = 0; _guardPct = 0; _guardTurns = 0;
        _counterDmg = 0; _vulnPct = 0; _vulnTurns = 0; _ailmentAmp = 0; _nextTurnExtra = 0; _overdrive = 0;
        _timeBombDmg = 0; _timeBombTurns = 0; _useCounts.Clear();
        _enemyBlock = 0; _enemyAtkUp = 0; _enemyCharged = false; _playerPoison = 0;
        _parryStance = false;

        _effWave = _main.Wave + enemy.levelOffset;
        _enemyMaxHP = Mathf.RoundToInt((enemy.baseHP + 40 * (_effWave - 1)) * _main.EnemyHpMul); // アセンションでHP増
        _enemyHP = _enemyMaxHP;
        // グラヴィティペンダント：戦闘開始時に敵HP-5%
        if (_main.Equipped == EquipKind.GravityPendant) _enemyHP = Mathf.Max(1, Mathf.RoundToInt(_enemyMaxHP * 0.95f));
        _enemyName.text = $"{enemy.enemyName} Lv{_effWave}";

        _slimeImg.sprite = enemy.BattleSprite();
        _slimeRt.sizeDelta = enemy.battleSize;
        _enemyInner.sizeDelta = enemy.battleSize;
        _slimeRt.anchoredPosition = enemy.flying ? new Vector2(400, 70) : new Vector2(400, GroundY + enemy.battleSize.y / 2f);

        if (_enemyShadow != null) Destroy(_enemyShadow.gameObject);
        _enemyShadow = null;
        if (!enemy.flying) { AddGroundShadow(_slimeRt, enemy.battleSize.x * 0.5f); _enemyShadow = (RectTransform)_slimeRt.GetChild(0); }

        _syn = _main.ComputeSynergy(); // 盤面シナジーを確定
        RollIntent(); // 初回のインテント
        StartPlayerTurn(firstTurn: true);
    }

    void StartPlayerTurn(bool firstTurn = false)
    {
        _block = _syn.block;   // シナジー：開始ブロック
        if (_main.CornersUnlocked >= 2) _block += GameBalance.CornerBlock;   // 四隅の加護Lv2：毎ターン開始ブロック
        _mana = _main.MaxMana + _manaBoostNext + _syn.mana; // シナジー：マナ
        if (_main.PainContract) _mana += 1;   // 痛みの契約：毎ターンマナ+1
        if (_main.Gluttony) _mana += 2;       // 暴食の契約：最大マナ+2
        _manaBoostNext = 0;

        // 暴食の契約：毎ターン開始時にHP-3（命は尽きない＝最低1）
        if (_main.Gluttony && !firstTurn)
        {
            _playerHP = Mathf.Max(1, _playerHP - 3);
            StartCoroutine(TextPopup(new Vector2(-330f, 160f), "-3 暴食", new Color(0.9f, 0.4f, 0.5f), 30));
        }

        // シナジー：毎ターン回復
        if (_syn.regen > 0) _playerHP = Mathf.Min(_playerMaxHP, _playerHP + _syn.regen);
        if (_main.CornersUnlocked >= 3) _playerHP = Mathf.Min(_playerMaxHP, _playerHP + GameBalance.CornerRegen);   // 四隅の加護Lv3：毎ターンHP回復

        // プレイヤーの毒（毎ターンダメージ→1ずつ減衰）
        if (_playerPoison > 0)
        {
            _playerHP = Mathf.Max(0, _playerHP - _playerPoison);
            _message.text = $"毒が体を蝕む……{_playerPoison} ダメージ";
            _playerPoison--;
            if (_playerHP <= 0) { StartCoroutine(Defeat()); return; }
        }

        // 継続効果：リジェネ／持続ブロック
        if (_regenTurns > 0) { _playerHP = Mathf.Min(_playerMaxHP, _playerHP + _regenAmt); _regenTurns--; }
        if (_blockRegenTurns > 0) { _block += _blockRegenAmt; _blockRegenTurns--; }

        // 毎ターン、盤面構成の確率（出現率）に従って手札を配り直す（次ターン追加分＋行コンプリート＋魂の器を加算）
        DealHand(_nextTurnExtra + _syn.draw + (_main.SoulVessel ? 2 : 0));
        _nextTurnExtra = 0;
        _overdrive = 0;   // オーバードライブは自ターン開始でリセット

        _inputLocked = false;
        RefreshAll(dealAnimation: true);
        StartCoroutine(DealSourceRipple());   // 配られたカードの出どころピースを盤面上で順に光らせる
        _message.text = firstTurn ? "戦闘開始！カードを選ぼう！" : "あなたのターン！";
    }

    // 手札が配られるのに合わせて、ミニ盤面上の「そのカードを生んだピース」を順にキラッと光らせる
    // （盤面ビルド＝出現率、という因果を言葉なしで伝える演出）
    IEnumerator DealSourceRipple()
    {
        if (_boardContent == null) yield break;
        var panel = _main.Panel;
        int W = panel.W, H = panel.H;
        float area = 220f;
        float cell = Mathf.Min(area / W, area / H);
        float ox = -(W - 1) * cell / 2f, oy = (H - 1) * cell / 2f;

        var counted = new Dictionary<CardDef, int>();   // 同名カードの何枚目か
        for (int i = 0; i < _hand.Count; i++)
        {
            var card = _hand[i];
            int rank = counted.TryGetValue(card, out var r) ? r : 0;
            counted[card] = rank + 1;
            yield return new WaitForSeconds(0.12f);   // カードが配られるテンポに合わせて順番に
            if (_boardContent == null) yield break;

            var matches = panel.Placements.Where(pp => pp.card == card).ToList();
            if (matches.Count == 0) continue;   // 通常攻撃（空きマス）はスキップ
            var cells = matches[rank % matches.Count].cells;
            foreach (var c in cells)
            {
                var flash = ProtoUI.CreatePanel("SrcFlash", _boardContent,
                    new Vector2(ox + c.x * cell, oy - c.y * cell), new Vector2(cell - 2, cell - 2),
                    Color.Lerp(card.CategoryColor, Color.white, 0.75f));
                flash.raycastTarget = false;
                StartCoroutine(SrcFlashFade(flash));
            }
        }
    }

    IEnumerator SrcFlashFade(Image img)
    {
        float t = 0f, dur = 0.5f; Color c0 = img.color;
        while (t < dur && img != null)
        {
            t += Time.deltaTime; float p = t / dur;
            var c = c0; c.a = 0.95f * (1f - p * p); img.color = c;
            img.transform.localScale = Vector3.one * (1f + p * 0.25f);
            yield return null;
        }
        if (img != null) Destroy(img.gameObject);
    }

    float HpRatio() => _playerMaxHP > 0 ? Mathf.Clamp01(_playerHP / (float)_playerMaxHP) : 1f;

    // 1枚ドロー（黄金マスを覆うカードならコイン獲得）
    CardDef DrawCard()
    {
        var c = _main.Panel.PickWeighted(HpRatio(), _emptyReduce) ?? _main.Db.normalAttack;
        int g = _main.Panel.MaxKindCover(c.id, CellKind.Gold);
        if (g > 0)
        {
            int coin = GameBalance.GoldCoinPerCell * g;   // 黄金マス：手札に出るたびコイン獲得
            _main.AddMoney(coin);
            if (_sfx != null && _coinClip != null) _sfx.PlayOneShot(_coinClip, 0.8f);
            StartCoroutine(TextPopup(new Vector2(-330f, 260f), $"+{coin}コイン", new Color(1f, 0.85f, 0.3f)));
        }
        return c;
    }

    // ==================== ママの加護ポップアップ（ホバー） ====================
    GameObject _blessPopup;
    void ShowBlessingPopup()
    {
        HideBlessingPopup();
        var lines = new List<string>();

        // 特殊マスの加護（盤面のピースで覆われて効いているもの）
        int spPw = 0, spGd = 0, spCs = 0; bool spRs = false;
        foreach (var pl in _main.Panel.Placements)
            foreach (var cc in pl.cells)
                switch (_main.Panel.KindAt(cc.x, cc.y))
                {
                    case CellKind.Power: spPw++; break;
                    case CellKind.Gold: spGd++; break;
                    case CellKind.Resonance: spRs = true; break;
                    case CellKind.Curse: spCs++; break;
                }
        if (spPw > 0) lines.Add($"<color=#FF7340>強化マス</color>　攻撃威力 +{GameBalance.PowerPctInt * spPw}%");
        if (spGd > 0) lines.Add($"<color=#FFD84D>黄金マス</color>　ドロー時コイン +{GameBalance.GoldCoinPerCell * spGd}");
        if (spRs) lines.Add($"<color=#66E5FF>共鳴マス</color>　シナジー {GameBalance.ResonanceMult}倍");
        if (spCs > 0) lines.Add($"<color=#BF66F2>呪いマス</color>　出現率{GameBalance.CurseWeightMult:0.#}倍／使用時HP-{GameBalance.CurseHpPerCell * spCs}");

        // 四隅の加護
        int corners = _main.CornersUnlocked;
        string[] bless = { $"攻撃威力 +{GameBalance.CornerAtkPctInt}%", $"毎ターン ブロック +{GameBalance.CornerBlock}", $"毎ターン HP +{GameBalance.CornerRegen}回復", $"最大マナ +{GameBalance.CornerMana}" };
        for (int bi = 0; bi < corners && bi < 4; bi++)
            lines.Add($"<color=#FFE080>四隅Lv{bi + 1}</color>　{bless[bi]}");

        // 盤面シナジー（この戦闘中ずっと有効）
        if (_syn.attackPct > 0) lines.Add($"<color=#FF7040>盤面シナジー</color>　攻撃 +{_syn.attackPct}%");
        int shapeMult = _main.Equipped == EquipKind.ArchitectPendant ? 2 : 1;
        int adjBlock = _syn.block - _syn.cols * GameBalance.ColCompleteBlock * shapeMult;   // 隣接分のみ（列コンプリートは別行で表示）
        if (adjBlock > 0) lines.Add($"<color=#7FB0FF>盤面シナジー</color>　ブロック +{adjBlock}");
        if (_syn.regen > 0) lines.Add($"<color=#70FF90>盤面シナジー</color>　再生 +{_syn.regen}");
        if (_syn.mana > 0) lines.Add($"<color=#C0A0FF>盤面シナジー</color>　マナ +{_syn.mana}");

        // 形状シナジー（盤面の形そのものから生まれる加護）
        if (_syn.draw > 0) lines.Add($"<color=#FFAA55>行コンプリート×{_syn.rows}</color>　毎ターン手札 +{_syn.draw}");
        if (_syn.cols > 0) lines.Add($"<color=#7FD0FF>列コンプリート×{_syn.cols}</color>　毎ターンブロック +{_syn.cols * GameBalance.ColCompleteBlock * shapeMult}");
        if (_syn.cores > 0) lines.Add($"<color=#66E5FF>コア×{_syn.cores}</color>　囲まれたカードの威力 +{GameBalance.CorePowerPctInt}%");
        if (_overdrive >= 1) lines.Add($"<color=#FF6040>オーバードライブ×{_overdrive}</color>　次の攻撃 +{OdPctInt * _overdrive}%");

        // 契約（悪魔との取引）
        if (_main.PainContract) lines.Add("<color=#E06080>痛みの契約</color>　使用毎HP-1／毎ターンマナ+1");
        if (_main.DemonHeart) lines.Add("<color=#E06080>悪魔の心臓</color>　瀕死で攻撃+30%");
        if (_main.SoulVessel) lines.Add("<color=#FF9060>魂の器</color>　最大HP半減／毎ターン手札+2");
        if (_main.Gluttony) lines.Add("<color=#FF9060>暴食の契約</color>　マナ+2／毎ターンHP-3");
        if (_main.Berserk) lines.Add("<color=#FF9060>破壊神の腕</color>　攻撃1.5倍／被ダメ+25%");

        string body = lines.Count > 0 ? string.Join("\n", lines) : "<color=#8a8a98>発動中の加護はありません</color>";
        int rows = Mathf.Max(1, lines.Count);
        float h = 62f + rows * 30f;

        // 顔アイコン（-690, 400）の右下に表示
        var pos = new Vector2(-690f + 215f + 46f, 400f - h / 2f - 50f);
        var holder = ProtoUI.CreateRect("BlessPopup", _root);
        holder.anchoredPosition = pos; holder.sizeDelta = new Vector2(430, h);
        _blessPopup = holder.gameObject;
        ProtoUI.CreateFramedPanel("BPBox", holder, Vector2.zero, new Vector2(430, h),
            new Color(0.08f, 0.07f, 0.05f, 0.98f), new Color(0.85f, 0.72f, 0.4f, 0.95f));
        var title = ProtoUI.CreateText("BPT", holder, "発動中の加護", 20, new Vector2(0, h / 2f - 26f), new Vector2(400, 28), ProtoUI.Gold);
        title.raycastTarget = false;
        var b = ProtoUI.CreateText("BPB", holder, body, 17, new Vector2(0, -14f), new Vector2(400, h - 56f), new Color(0.94f, 0.94f, 1f), TextAlignmentOptions.Top);
        b.lineSpacing = 8f; b.raycastTarget = false;
    }

    void HideBlessingPopup()
    {
        if (_blessPopup != null) { Destroy(_blessPopup); _blessPopup = null; }
    }

    // 逃げる：ペナルティとして所持金の20%を落とす（ノーリスク離脱の防止）
    void Retreat()
    {
        int loss = Mathf.RoundToInt(_main.Money * 0.2f);
        if (loss > 0) _main.AddMoney(-loss);
        _main.SetCurrentHP(_playerHP);
        _main.AutoSaveRun();
        _main.ShowMap();
    }

    // 敵の次の行動を抽選（インテント）
    void RollIntent() { _intent = _enemy != null ? _enemy.PickAttack() : null; }

    // インテントの予測ダメージ（1ヒットあたり、弱体を反映）
    int IntentPerHit()
    {
        if (_intent == null) return 0;
        float baseAtk = (_enemy.minAtk + _enemy.maxAtk) / 2f + _enemyAtkUp; // 敵の強化を反映
        int per = Mathf.RoundToInt((baseAtk * _intent.mult + 3 * (_effWave - 1)) * _main.EnemyDmgMul);
        if (_enemyCharged) per = Mathf.RoundToInt(per * 1.8f);              // チャージ済みなら1.8倍
        if (_weakTurns > 0) per = Mathf.RoundToInt(per * (1f - _weakPct / 100f));
        return Mathf.Max(1, per);
    }

    void UpdateIntent()
    {
        if (_intentText == null) return;
        bool show = _intent != null && !_dead && _enemyHP > 0;
        if (_intentBadge != null) _intentBadge.SetActive(show);
        if (!show) return;
        if (_stunTurns > 0) { _intentText.text = "行動不能"; _intentText.color = new Color(0.7f, 0.8f, 1f); return; }
        // ギミック行動の予告
        switch (_intent.act)
        {
            case EnemyActKind.Guard: _intentText.text = $"防御 {_intent.amount}"; _intentText.color = new Color(0.6f, 0.8f, 1f); return;
            case EnemyActKind.PowerUp: _intentText.text = $"強化 +{_intent.amount}"; _intentText.color = new Color(1f, 0.6f, 0.9f); return;
            case EnemyActKind.Charge: _intentText.text = "チャージ中…"; _intentText.color = new Color(1f, 0.85f, 0.4f); return;
            case EnemyActKind.PoisonPlayer:
                _intentText.text = $"毒攻撃 {IntentPerHit()}"; _intentText.color = new Color(0.65f, 0.95f, 0.4f); return;
        }
        if (_intent.hits == 0) { _intentText.text = "様子見"; _intentText.color = new Color(0.85f, 0.85f, 0.9f); return; }
        int per = IntentPerHit();

        // 実効値：軽減（装備/継続/1回）とブロックを加味した「実際に受けそうなダメージ」
        float cut = per;
        if (_main.Equipped == EquipKind.GuardPendant) cut *= 0.95f;
        if (_guardTurns > 0) cut *= 1f - _guardPct / 100f;
        if (_protectPct > 0) cut *= 1f - _protectPct / 100f;   // 次の1発のみだが目安として反映
        int eff = Mathf.Max(0, Mathf.RoundToInt(cut) * _intent.hits - _block);

        string baseTxt = _intent.hits > 1 ? $"攻撃 {per}×{_intent.hits}" : $"攻撃 {per}";
        int rawTotal = per * _intent.hits;
        _intentText.text = eff < rawTotal ? $"{baseTxt} → {eff}" : baseTxt;
        _intentText.color = new Color(1f, 0.85f, 0.75f);
    }

    void DealHand(int extra = 0)
    {
        _hand.Clear();
        int n = (_main.Equipped == EquipKind.HandPendant ? 6 : 5) + Mathf.Max(0, extra);   // 手札枚数（手札増強で6枚）
        for (int i = 0; i < n; i++)
        {
            CardDef c = DrawCard();   // HPが低いほど大型（強）カードが出やすい（黄金マスのコイン込み）
            _hand.Add(c);
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
        bgRt.sizeDelta = new Vector2(160, 160);   // 画面より少し大きく（画面シェイクで端に隙間が出ないように）
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.SetAsFirstSibling();

        ProtoUI.CreatePanel("BattleShade", _root, new Vector2(0, -382), new Vector2(1700, 260), new Color(0.015f, 0.018f, 0.028f, 0.45f)).raycastTarget = false;
        ProtoUI.CreatePanel("TopBar", _root, new Vector2(0, 410), new Vector2(1700, 96), ProtoUI.Ink).raycastTarget = false;
        ProtoUI.CreatePanel("TopBarLine", _root, new Vector2(0, 356), new Vector2(1700, 2), new Color(0.95f, 0.78f, 0.36f, 0.62f)).raycastTarget = false;

        // プレイヤーHUD（左上）
        var pName = ProtoUI.CreateText("PName", _root, "MAMA", 27, new Vector2(-470, 416), new Vector2(320, 38), new Color(0.9f, 0.92f, 1f));
        ProtoUI.StyleTitle(pName, new Color(0.9f, 0.92f, 1f), 3f);
        ProtoUI.CreatePanel("PGaugeBorder", _root, new Vector2(-470, 382), new Vector2(GaugeWidth + 8, 26), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
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
        _faceImg.sprite = ProtoPixelArt.FrontMama(); _faceImg.preserveAspect = true;

        // 顔アイコンにカーソルを当てると発動中の加護をポップアップ表示
        var trig = faceRt.gameObject.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowBlessingPopup());
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideBlessingPopup());
        trig.triggers.Add(enter); trig.triggers.Add(exit);

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
        ProtoUI.CreatePanel("EGaugeBorder", _root, new Vector2(560, 382), new Vector2(GaugeWidth + 8, 26), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreateGauge("EGauge", _root, new Vector2(560, 382), new Vector2(GaugeWidth, 18),
            new Color(0.08f, 0.07f, 0.11f, 0.92f), new Color(0.95f, 0.27f, 0.27f), out _enemyFill);
        _enemyHPText = ProtoUI.CreateText("EHP", _root, "", 16, new Vector2(560, 382), new Vector2(300, 24));
        _enemyHPText.fontStyle = FontStyles.Bold;
        // 敵HPゲージの左横に状態異常（やけど等）を表示（ゲージに重ならない）
        _enemyStatusText = ProtoUI.CreateText("EStatus", _root, "", 18, new Vector2(300, 382), new Vector2(180, 26),
            new Color(1f, 0.55f, 0.3f), TextAlignmentOptions.Right);
        _enemyStatusText.fontStyle = FontStyles.Bold;

        // 敵インテントのバッジ表示は廃止（行動の抽選・実行ロジックは維持）
        // ※復活させる場合はここでバッジUIを生成し、_intentText/_intentBadge に代入する

        // キャラ
        _slimeImg = CreateCharacterSprite("EnemySprite", ProtoPixelArt.Dragon(), new Vector2(400, 70), new Vector2(540, 355));
        _slimeRt = (RectTransform)_slimeImg.transform.parent;
        _enemyInner = (RectTransform)_slimeImg.transform;

        _actorImg = CreateCharacterSprite("Player", ProtoPixelArt.MamaPhoto(), new Vector2(-330, GroundY + 205f), new Vector2(280, 355));
        _actorRt = (RectTransform)_actorImg.transform.parent;
        _playerInner = (RectTransform)_actorImg.transform;
        AddGroundShadow(_actorRt, 150f);

        // メッセージ
        var msgBox = ProtoUI.CreateFramedPanel("MsgBox", _root, new Vector2(0, 302), new Vector2(980, 52), new Color(0.035f, 0.04f, 0.06f, 0.92f), new Color(0.65f, 0.55f, 0.36f, 0.62f));
        _message = ProtoUI.CreateText("Msg", msgBox.transform, "", 22, Vector2.zero, new Vector2(940, 50));

        // 手札
        _handArea = ProtoUI.CreateRect("Hand", _root);
        _handArea.anchoredPosition = new Vector2(0, -230);   // 扇の外側カードが画面下で切れないよう高めに
        _handArea.sizeDelta = new Vector2(1500, 240);

        // 盤面プレビュー（キャラの左横・常時表示）：外周金枠＋不透明内側
        _boardOverlay = ProtoUI.CreateRect("BoardOverlay", _root);
        _boardOverlay.anchoredPosition = new Vector2(-668, 55);
        _boardOverlay.sizeDelta = new Vector2(244, 244);
        var ovBg = _boardOverlay.gameObject.AddComponent<Image>();
        ovBg.color = new Color(0.9f, 0.78f, 0.42f, 0.97f); ovBg.raycastTarget = false;
        var ovInner = ProtoUI.CreatePanel("OverlayInner", _boardOverlay, Vector2.zero, new Vector2(236, 236), new Color(0.07f, 0.08f, 0.13f, 0.98f));
        ovInner.raycastTarget = false;
        _boardContent = ProtoUI.CreateRect("BoardContent", _boardOverlay);
        _boardContent.anchoredPosition = Vector2.zero; _boardContent.sizeDelta = new Vector2(244, 244);
        _boardLabel = ProtoUI.CreateText("BoardLabel", _root, "ビルド構成", 18, new Vector2(-668, 198), new Vector2(250, 26), ProtoUI.Gold);
        _boardLabel.fontStyle = FontStyles.Bold;

        // カード詳細（中央・ホバー時のみ表示）
        _detailPanel = ProtoUI.CreateRect("CardDetail", _root);
        _detailPanel.anchoredPosition = new Vector2(0, 112);
        _detailPanel.sizeDelta = new Vector2(234, 300);
        var dpBg = _detailPanel.gameObject.AddComponent<Image>();
        dpBg.color = new Color(0.85f, 0.72f, 0.4f, 0.97f); dpBg.raycastTarget = false;
        ProtoUI.CreatePanel("DetailInner", _detailPanel, Vector2.zero, new Vector2(226, 292), new Color(0.06f, 0.07f, 0.11f, 0.98f)).raycastTarget = false;
        _detailContent = ProtoUI.CreateRect("DetailContent", _detailPanel);
        _detailContent.anchoredPosition = Vector2.zero; _detailContent.sizeDelta = new Vector2(234, 300);
        _detailPanel.gameObject.SetActive(false);

        // 手札を最前面に
        _handArea.SetAsLastSibling();

        // ターン終了ボタン（大きめ・太い外枠）＋ 逃げるをその下に配置
        ProtoUI.CreatePanel("EndTurnBorder", _root, new Vector2(632, -262), new Vector2(224, 114), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        _endTurnBtn = ProtoUI.CreateButton("EndTurn", _root, "ターン終了", 24, new Vector2(632, -262), new Vector2(212, 102),
            new Color(0.38f, 0.13f, 0.12f, 0.96f), OnEndTurn);
        ProtoUI.CreatePanel("RetreatBorder", _root, new Vector2(632, -374), new Vector2(224, 80), new Color(0.85f, 0.72f, 0.4f, 0.95f)).raycastTarget = false;
        ProtoUI.CreateButton("RetreatBtn", _root, "逃げる", 20, new Vector2(632, -374), new Vector2(212, 68),
            new Color(0.16f, 0.14f, 0.18f, 0.96f), Retreat);


        // 点滅チャレンジ（高級ステージ演出付き）
        _challengeRoot = ProtoUI.CreateFullScreen("Challenge", _root);
        _challengeRoot.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.05f, 0.82f);
        // 中央を照らすアンビエント光（集中線的な焦点）
        ProtoUI.CreateGlow("CStageGlow", _challengeRoot, new Vector2(0, 20), new Vector2(1200, 900), new Color(0.25f, 0.3f, 0.55f, 0.22f)).raycastTarget = false;
        // 枠付きステージ台
        ProtoUI.CreateGlow("CStageHalo", _challengeRoot, new Vector2(0, 20), new Vector2(880, 560), new Color(0.5f, 0.42f, 0.2f, 0.18f)).raycastTarget = false;
        ProtoUI.CreateFramedPanel("CStage", _challengeRoot, new Vector2(0, 20), new Vector2(820, 500),
            new Color(0.05f, 0.06f, 0.10f, 0.92f), new Color(0.85f, 0.72f, 0.4f, 0.9f)).raycastTarget = false;
        // 四隅の金アクセント
        Vector2[] corners = { new Vector2(-400, 260), new Vector2(400, 260), new Vector2(-400, -230), new Vector2(400, -230) };
        foreach (var cp in corners)
        {
            ProtoUI.CreatePanel("CCornerH", _challengeRoot, cp, new Vector2(44, 5), new Color(0.95f, 0.8f, 0.4f, 0.9f)).raycastTarget = false;
            ProtoUI.CreatePanel("CCornerV", _challengeRoot, cp, new Vector2(5, 44), new Color(0.95f, 0.8f, 0.4f, 0.9f)).raycastTarget = false;
        }
        _challengePrompt = ProtoUI.CreateText("CPrompt", _challengeRoot, "", 26, new Vector2(0, 212), new Vector2(740, 72));
        _challengePrompt.fontStyle = FontStyles.Bold; _challengePrompt.outlineWidth = 0.2f; _challengePrompt.outlineColor = new Color32(8, 6, 20, 255);
        _challengePrompt.textWrappingMode = TMPro.TextWrappingModes.Normal;
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
        if (_enemyStatusText != null)
        {
            var es = new List<string>();
            if (_burn > 0) es.Add("やけど");
            if (_poison > 0) es.Add("毒");
            if (_stunTurns > 0) es.Add("麻痺");
            if (_vulnTurns > 0) es.Add("弱点");
            if (_enemyBlock > 0) es.Add($"盾{_enemyBlock}");
            if (_enemyAtkUp > 0) es.Add($"攻+{_enemyAtkUp}");
            _enemyStatusText.text = string.Join(" ", es);
        }
        UpdateIntent();
        RefreshMana();

        var st = new List<string>();
        if (_strength > 0) st.Add($"<color=#FF9060>筋力+{_strength}</color>");
        if (_protectPct > 0) st.Add($"<color=#90C0FF>軽減{_protectPct}%</color>");
        if (_primeBlink) st.Add("<color=#FFD040>点滅構え</color>");
        if (_weakTurns > 0) st.Add($"<color=#C080FF>敵弱体{_weakTurns}T</color>");
        if (_playerPoison > 0) st.Add($"<color=#A0E060>毒{_playerPoison}</color>");
        if (_guardTurns > 0) st.Add($"<color=#90C0FF>継続軽減{_guardPct}%</color>");
        if (_parryStance) st.Add("<color=#8FE8FF>パリィ構え</color>");
        if (_thornsTurns > 0) st.Add($"<color=#90FFB0>茨{_thornsDmg}</color>");
        // 盤面の加護などの常設情報はMAMAの顔にカーソルを当てると表示（ここには出さない）
        _statusText.text = string.Join("  ", st);

        RefreshHand(dealAnimation);
    }

    // ==================== カードのRenderTexture焼き込み ====================
    // 入れ子＋角丸スプライトの合成カードは回転すると描画が歪む。
    // そこで一度「1枚の平らな画像」に焼いて、その画像だけを傾ける（平面の回転は歪まない）。
    const int BakeLayer = 30;
    const int BakeW = 400, BakeH = 552;          // 焼き込み解像度（表示190x262の約2倍＝くっきり）
    const float BakeUnitW = 200f, BakeUnitH = 276f;   // 焼き込みキャンバスの論理サイズ（カード＋余白）
    Camera _bakeCam; RectTransform _bakeRoot;
    readonly List<RenderTexture> _cardTextures = new List<RenderTexture>();

    void EnsureBaker()
    {
        if (_bakeCam != null) return;
        // 遠くに置いたワールド空間キャンバス＋専用カメラ（本編カメラとは独立）
        var cg = new GameObject("CardBakeCanvas", typeof(RectTransform));
        cg.transform.position = new Vector3(8000f, 8000f, 0f);
        cg.layer = BakeLayer;
        var canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        _bakeRoot = (RectTransform)cg.transform;
        _bakeRoot.sizeDelta = new Vector2(BakeUnitW, BakeUnitH);
        _bakeRoot.localScale = Vector3.one;

        var camGo = new GameObject("CardBakeCam");
        camGo.transform.position = new Vector3(8000f, 8000f, -100f);
        _bakeCam = camGo.AddComponent<Camera>();
        _bakeCam.orthographic = true;
        _bakeCam.orthographicSize = BakeUnitH / 2f;
        _bakeCam.cullingMask = 1 << BakeLayer;
        _bakeCam.clearFlags = CameraClearFlags.SolidColor;
        _bakeCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _bakeCam.nearClipPlane = 1f; _bakeCam.farClipPlane = 300f;
        _bakeCam.enabled = false;   // 手動レンダリングのみ
        canvas.worldCamera = _bakeCam;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    // カードを1枚の画像に焼いて返す
    RenderTexture BakeCard(CardDef card, bool affordable)
    {
        EnsureBaker();
        foreach (Transform c in _bakeRoot) Destroy(c.gameObject);   // 前のカードを掃除
        var btn = CreateCardUI(card, Vector2.zero, null, affordable);
        var crt = (RectTransform)btn.transform;
        crt.SetParent(_bakeRoot, false);
        crt.anchoredPosition = Vector2.zero; crt.localRotation = Quaternion.identity; crt.localScale = Vector3.one;
        SetLayerRecursive(crt.gameObject, BakeLayer);
        Canvas.ForceUpdateCanvases();

        var rt = new RenderTexture(BakeW, BakeH, 24, RenderTextureFormat.ARGB32);   // 深度24bit（URPのカメラ出力に必須）
        rt.Create();
        _bakeCam.targetTexture = rt;
        _bakeCam.Render();
        _bakeCam.targetTexture = null;
        Destroy(btn.gameObject);
        _cardTextures.Add(rt);
        return rt;
    }

    void ReleaseCardTextures()
    {
        foreach (var t in _cardTextures) if (t != null) { t.Release(); Destroy(t); }
        _cardTextures.Clear();
    }

    void RefreshHand(bool dealAnimation = false)
    {
        HideCardDetail();
        RenderBoard(null, -1);   // 盤面は常時表示（ハイライト無し）
        foreach (Transform c in _handArea) Destroy(c.gameObject);
        _cardRects.Clear();
        ReleaseCardTextures();   // 前ターンの焼き込み画像を解放
        int n = _hand.Count;
        float mid = (n - 1) / 2f;
        // 扇形：等間隔の横位置＋放物線アーチ（端が下がる）＋線形の傾き。カードは焼き込んだ平面画像なので傾けても崩れない
        float spacing = n > 1 ? Mathf.Min(88f, 500f / (n - 1)) : 0f;    // 横間隔
        float tiltPer = n > 1 ? Mathf.Min(6f, 36f / (n - 1)) : 0f;      // 1枚あたりの傾き（手で持った扇形）
        const float ArchDepth = 46f;                                    // 端の下がり量
        Vector2 cardSize = new Vector2(BakeUnitW, BakeUnitH);           // 表示サイズ（焼き込みの論理サイズと同じ比率）

        var cards = new List<RectTransform>();
        var hoverLayer = ProtoUI.CreateRect("HandHoverLayer", _handArea);
        hoverLayer.anchoredPosition = Vector2.zero; hoverLayer.sizeDelta = _handArea.sizeDelta;
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            float d = i - mid;
            float norm = mid > 0 ? d / mid : 0f;
            Vector2 pos = new Vector2(d * spacing, -ArchDepth * norm * norm);
            var rot = Quaternion.Euler(0, 0, -tiltPer * d);

            bool canAfford = _mana >= _hand[i].ManaCost;
            bool affordable = !_inputLocked && canAfford;
            var card = _hand[i];

            // カードを1枚の平面画像に焼く → RawImageで表示（平面なので傾けても歪まない）
            var tex = BakeCard(card, affordable);
            var rawGo = new GameObject("Card", typeof(RectTransform), typeof(RawImage));
            var crt = (RectTransform)rawGo.transform;
            crt.SetParent(_handArea, false);
            crt.anchoredPosition = pos; crt.sizeDelta = cardSize; crt.localRotation = rot;
            var raw = rawGo.GetComponent<RawImage>();
            raw.texture = tex; raw.raycastTarget = false;
            _cardRects.Add(crt);
            cards.Add(crt);

            // 固定の当たり判定ゾーン（別オブジェクト・ホバーで動かさない）
            var hit = ProtoUI.CreatePanel("CardHit", _handArea, pos, cardSize, new Color(0f, 0f, 0f, 0f));
            var hitRt = (RectTransform)hit.transform;
            hitRt.localRotation = rot;
            var hover = hit.gameObject.AddComponent<CardHover>();
            hover.Setup(crt, _handArea, hoverLayer, pos, rot, raw, Color.white,
                onEnter: () => { if (_inputLocked) return; RenderBoard(card, idx); },
                onExit: () => { RenderBoard(null, -1); },
                onClick: () => { if (_inputLocked) return; if (!canAfford) { _message.text = "マナが足りないので選択できません。"; return; } TryPlayCard(idx); });

            if (dealAnimation) StartCoroutine(DealCard(crt, pos, i * 0.06f));
        }
        // 見た目：左のカードを前面に。判定ゾーンはその上（透明）
        for (int i = cards.Count - 1; i >= 0; i--) cards[i].SetAsLastSibling();
        for (int i = _handArea.childCount - 1; i >= 0; i--)
        {
            var ch = _handArea.GetChild(i);
            if (ch.name == "CardHit") ch.SetAsLastSibling();
        }
        hoverLayer.SetAsLastSibling();
    }

    // カードをクリック＝即発動
    void TryPlayCard(int index)
    {
        if (_inputLocked || index < 0 || index >= _hand.Count) return;
        var card = _hand[index];
        if (_mana < card.ManaCost) { _message.text = "マナが足りない！"; return; }
        HideCardDetail();
        _inputLocked = true;
        var rt = index < _cardRects.Count ? _cardRects[index] : null;
        StartCoroutine(PlayWithDisappear(index, rt));
    }

    void ShowCardDetail(CardDef card)
    {
        if (_detailPanel == null) return;
        foreach (Transform c in _detailContent) Destroy(c.gameObject);
        _detailPanel.gameObject.SetActive(true);

        // マナコストバッジ（左上）
        var dMana = ProtoUI.CreatePanel("DMana", _detailContent, new Vector2(-92, 118), new Vector2(34, 34), new Color(0.08f, 0.27f, 0.68f, 0.98f));
        dMana.raycastTarget = false;
        ProtoUI.CreateText("DManaT", dMana.transform, card.ManaCost.ToString(), 20, Vector2.zero, new Vector2(34, 34)).fontStyle = FontStyles.Bold;

        ProtoUI.CreateText("DName", _detailContent, card.displayName, 24, new Vector2(12, 118), new Vector2(190, 32), ProtoUI.Gold);
        ProtoUI.CreateText("DKind", _detailContent, $"{card.ThemeTagRich()}{(CardDef.KindLabel(card.Category))}　{card.Size}マス", 15,
            new Vector2(0, 88), new Vector2(250, 22), new Color(0.8f, 0.88f, 1f));

        // 形状アート
        var art = ProtoUI.CreatePanel("DArt", _detailContent, new Vector2(0, 22), new Vector2(150, 96), new Color(0.02f, 0.03f, 0.05f, 0.9f));
        art.raycastTarget = false;
        var shape = card.Shape;
        float cs = 18f, gap = 3f;
        int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y), maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.Bevel(ProtoUI.CreatePanel("M", art.transform, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.CategoryColor)).raycastTarget = false;

        string eff = !string.IsNullOrEmpty(card.description)
            ? (card.power > 0 ? $"威力{card.power}　{card.description}" : card.description)
            : (card.kind == CardKind.Attack ? $"威力 {card.power}" : "");
        var deff = ProtoUI.CreateText("DEff", _detailContent, eff, 15, new Vector2(0, -88), new Vector2(202, 104), new Color(0.92f, 0.92f, 1f));
        deff.textWrappingMode = TMPro.TextWrappingModes.Normal;
        deff.enableAutoSizing = true; deff.fontSizeMin = 11; deff.fontSizeMax = 16;
    }

    void HideCardDetail()
    {
        if (_detailPanel != null) _detailPanel.gameObject.SetActive(false);
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
        Quaternion startRot = rt.localRotation; // 扇状の傾きを正面に戻しながら消す
        float t = 0f; const float dur = 0.24f;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / dur);
            rt.anchoredPosition = start + new Vector2(0, 150f * p);
            rt.localScale = Vector3.one * Mathf.Lerp(startScale, 0.25f, p);
            rt.localRotation = Quaternion.Slerp(startRot, Quaternion.identity, p);
            cg.alpha = 1f - p;
            yield return null;
        }
        rt.localRotation = Quaternion.identity;
    }

    void HideBoardOverlay()
    {
        RenderBoard(null, -1);
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

    // 盤面（キャラ左横・常時表示）を描画。card!=nullでそのカードの配置マスを発光
    void RenderBoard(CardDef card, int handIndex)
    {
        if (_boardOverlay == null || _boardContent == null) return;
        if (_boardLabel != null) _boardLabel.text = card != null ? $"{card.displayName}の配置マス" : "ビルド構成";
        if (_glowCo != null) { StopCoroutine(_glowCo); _glowCo = null; }
        _glowImgs.Clear(); _glowBase.Clear();
        foreach (Transform c in _boardContent) Destroy(c.gameObject);

        var panel = _main.Panel;
        int W = panel.W, H = panel.H;
        float area = 220f;   // 金枠が隠れないよう内側に収める
        float cell = Mathf.Min(area / W, area / H);
        float ox = -(W - 1) * cell / 2f, oy = (H - 1) * cell / 2f;

        var cellColor = new Dictionary<Vector2Int, Color>();
        Color hi = new Color(1f, 0.86f, 0.28f, 0.97f);
        if (card != null)
        {
            var matches = panel.Placements.Where(pp => pp.card == card).ToList();
            int rank = 0;
            for (int i = 0; i < handIndex && i < _hand.Count; i++) if (_hand[i] == card) rank++;
            if (matches.Count > 0)
                foreach (var c in matches[rank % matches.Count].cells) cellColor[c] = hi;
        }

        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                bool match = cellColor.TryGetValue(new Vector2Int(x, y), out var hc);
                Color col;
                if (match) col = hc;
                else if (!panel.IsUnlocked(x, y)) col = new Color(0.04f, 0.04f, 0.06f, 0.85f);  // 未解放
                else if (panel.Sealed.Contains(new Vector2Int(x, y))) col = new Color(0.32f, 0.12f, 0.40f, 0.95f);  // 歪みマス（この戦闘中は封印）
                else { var pl = panel.GetAt(x, y); col = pl != null ? pl.card.CategoryColor : new Color(0.16f, 0.14f, 0.24f, 0.95f); } // ピース/空き
                var p = ProtoUI.Bevel(ProtoUI.CreatePanel($"BC_{x}_{y}", _boardContent,
                    new Vector2(ox + x * cell, oy - y * cell), new Vector2(cell - 2, cell - 2), col));   // 立体タイル
                p.raycastTarget = false;
                if (match) { _glowImgs.Add(p); _glowBase.Add(hc); }
            }

        if (_glowImgs.Count > 0) _glowCo = StartCoroutine(GlowLoop());
    }

    void RefreshMana()
    {
        _manaText.text = $"{_mana}/{_main.MaxMana}";
    }

    Button CreateCardUI(CardDef card, Vector2 pos, System.Action onClick, bool affordable)
    {
        Color accent = card.CategoryColor;
        // マナ不足でも種別カラーは残し、明るさを落として「使えない」を表現
        var frame = ProtoUI.CreatePanel("Card", _handArea, pos, new Vector2(190, 262),
            affordable ? Color.Lerp(accent, ProtoUI.Border, 0.45f) : Color.Lerp(accent, Color.black, 0.55f));
        var btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        btn.interactable = affordable;

        var inner = ProtoUI.VGrad(ProtoUI.CreatePanel("Inner", frame.transform, Vector2.zero, new Vector2(180, 252),
            affordable ? new Color(0.10f, 0.11f, 0.16f, 0.98f) : new Color(0.09f, 0.085f, 0.10f, 0.92f)));   // 上品な縦グラデ
        inner.raycastTarget = false;
        if (card.rarity >= 2 && affordable) ProtoUI.AddShine(inner, new Vector2(180, 252));   // レアの手札は走査光
        ProtoUI.AddPanelTrim(inner, new Vector2(180, 252), Color.Lerp(accent, Color.black, 0.35f), new Color(1f, 1f, 1f, 0.06f));

        var accentLine = ProtoUI.CreatePanel("AccentLine", inner.transform, new Vector2(0, 123), new Vector2(168, 5), affordable ? accent : Color.Lerp(accent, Color.black, 0.4f));
        accentLine.raycastTarget = false;
        var header = ProtoUI.CreatePanel("Header", inner.transform, new Vector2(0, 101), new Vector2(168, 38), Color.Lerp(accent, Color.black, affordable ? 0.72f : 0.8f));
        header.raycastTarget = false;
        // カード名：コストバッジの右側に左詰め、文字数に応じて枠内に収まるよう自動縮小
        var nameText = ProtoUI.CreateText("Name", header.transform, card.displayName, 18, new Vector2(18, 0), new Vector2(126, 32));
        nameText.alignment = TextAlignmentOptions.Left;
        nameText.fontStyle = FontStyles.Bold;
        nameText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
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
            ProtoUI.Bevel(ProtoUI.CreatePanel("Mas", art.transform, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.CategoryColor)).raycastTarget = false;

        // 効果説明：マス背景（形状アート）のすぐ下に配置
        var footer = ProtoUI.CreatePanel("Footer", inner.transform, new Vector2(0, -76), new Vector2(168, 64), new Color(0.075f, 0.08f, 0.105f, 0.94f));
        footer.raycastTarget = false;
        string footText = !string.IsNullOrEmpty(card.description)
            ? (card.power > 0 ? $"威力{card.power}　{card.description}" : card.description)
            : (card.kind == CardKind.Attack ? $"威力 {card.power}" : "");
        var ft = ProtoUI.CreateText("FT", footer.transform, card.ThemeTagRich() + footText, 13, Vector2.zero, new Vector2(160, 60), ProtoUI.Gold);
        ft.enableAutoSizing = true; ft.fontSizeMin = 9; ft.fontSizeMax = 14;

        // マナ不足のカードは暗いオーバーレイを重ねて「使えない」を明確化
        if (!affordable)
        {
            var dim = ProtoUI.CreatePanel("Dim", frame.transform, Vector2.zero, new Vector2(190, 262), new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = false;
        }

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

        // 痛みの契約：カードを使うたびHP-1
        if (_main.PainContract) { _playerHP = Mathf.Max(1, _playerHP - 1); StartCoroutine(TextPopup(new Vector2(-330f, 160f), "-1 痛", new Color(0.9f, 0.4f, 0.4f), 34)); }

        // 呪いマス：覆っているカードは使用時にHPを失う（HP1未満にはならない）
        // 禁忌のペンダント：代償を無効化（出現率3倍の恩恵だけ受ける）
        int curse = _main.Equipped == EquipKind.TabooPendant ? 0 : _main.Panel.MaxKindCover(card.id, CellKind.Curse);
        if (curse > 0)
        {
            int cost = GameBalance.CurseHpPerCell * curse;
            _playerHP = Mathf.Max(1, _playerHP - cost);
            _message.text = $"呪いの代償……HP-{cost}";
            if (_sfx != null && _curseClip != null) _sfx.PlayOneShot(_curseClip);
            StartCoroutine(TextPopup(new Vector2(-330f, 200f), $"-{cost} 呪い", new Color(0.85f, 0.3f, 1f), 44));
            RefreshAll();
        }

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

    // ミニゲームごとの大成功ボーナス。倍率が高いほど・種類ごとに違うご褒美が出る
    IEnumerator ApplyMinigameBonus(CardDef card, float mult)
    {
        Vector2 pop = new Vector2(0, 120f);

        // スロット：揃い具合に応じてコイン獲得（ジャックポットは大金）
        if (card.HasEffect(CardEffectType.SlotOnUse) && mult >= 1.2f)
        {
            int coin = mult >= 2.5f ? 50 : mult >= 1.8f ? 25 : 10;
            _main.AddMoney(coin);
            if (_sfx != null && _coinClip != null) _sfx.PlayOneShot(_coinClip);
            StartCoroutine(TextPopup(pop, $"+{coin}コイン！", new Color(1f, 0.85f, 0.3f), mult >= 2.5f ? 46 : 36));
            yield return new WaitForSeconds(0.35f);
        }
        // ルーレット：200%を引いたら運が開けて次ターン手札+1
        else if (card.HasEffect(CardEffectType.RouletteOnUse) && mult >= 2f)
        {
            _nextTurnExtra += 1;
            StartCoroutine(TextPopup(pop, "運命が開けた！次ターン手札+1", new Color(0.85f, 0.6f, 1f), 34));
            yield return new WaitForSeconds(0.35f);
        }
        // ゲージ（会心の一撃）：ジャストで急所＝敵1ターン麻痺
        else if (card.HasEffect(CardEffectType.GaugeOnUse) && mult >= 2f)
        {
            _stunTurns = Mathf.Max(_stunTurns, 1);
            StartCoroutine(TextPopup(pop, "急所を突いた！敵は麻痺！", new Color(1f, 0.95f, 0.5f), 34));
            yield return new WaitForSeconds(0.35f);
        }
        // チャージ（溜め斬り）：完璧なタメで刃が熱を帯びる＝やけど+3
        else if (card.HasEffect(CardEffectType.ChargeOnUse) && mult >= 2f)
        {
            _burn += 3;
            StartCoroutine(TextPopup(pop, "刃が燃え上がる！やけど+3", new Color(1f, 0.5f, 0.25f), 34));
            yield return new WaitForSeconds(0.35f);
        }
        // カウントダウン（拍動剣）：ジャストでリズムに乗る＝パリィの構え
        else if (card.HasEffect(CardEffectType.CountdownOnUse) && mult >= 2f)
        {
            _parryStance = true;
            StartCoroutine(TextPopup(pop, "リズムに乗った！パリィの構え！", new Color(0.5f, 0.9f, 1f), 34));
            yield return new WaitForSeconds(0.35f);
        }
        // 2本ゲージ・連打：大成功で勢いが乗る＝オーバードライブ+1
        else if ((card.HasEffect(CardEffectType.DualGaugeOnUse) && mult >= 1.9f)
              || (card.HasEffect(CardEffectType.MashOnUse) && mult >= 1.8f))
        {
            if (_overdrive < GameBalance.OverdriveMaxStack)
            {
                _overdrive++;
                StartCoroutine(TextPopup(pop, $"勢いが乗る！オーバードライブ+1", new Color(1f, 0.55f, 0.25f), 34));
                yield return new WaitForSeconds(0.35f);
            }
        }
    }

    IEnumerator ResolveAttack(CardDef card)
    {
        bool blink = card.HasEffect(CardEffectType.BlinkOnUse) || _primeBlink;
        _primeBlink = false;

        // 攻撃ポーズのアニメ再生開始（コマが複数あればパラパラ動く。無ければ通常立ち絵のまま）
        if (_atkAnimCo != null) StopCoroutine(_atkAnimCo);
        _atkAnimCo = StartCoroutine(AttackFrameLoop());

        // ミニゲーム（点滅／ゲージ／数字順タップ／スロット）で倍率決定
        float mult = 1f;
        if (blink) { yield return RunChallenge(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.GaugeOnUse)) { yield return RunGauge(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.TapOrderOnUse)) { yield return RunTapOrder(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.SlotOnUse)) { yield return RunSlot(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.MashOnUse)) { yield return RunMash(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.RouletteOnUse)) { yield return RunRoulette(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.TraceOnUse)) { yield return RunTrace(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.ChargeOnUse)) { yield return RunCharge(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.DualGaugeOnUse)) { yield return RunDualGauge(card); mult = _challengeMultiplier; }
        else if (card.HasEffect(CardEffectType.CountdownOnUse)) { yield return RunCountdown(card); mult = _challengeMultiplier; }
        else { _message.text = $"{card.displayName}！"; yield return new WaitForSeconds(0.3f); }

        // ミニゲームごとの大成功ボーナス（種類で違うご褒美＝どのミニゲームを厚くするかのビルド選択）
        yield return ApplyMinigameBonus(card, mult);

        // ---- 威力計算（基礎＋加算系） ----
        int basePow = card.power + _main.Stats.Attack + _strength;
        // 連鎖斬：盤面でこのカードのピースに隣接するピース数×amount を加算
        if (card.HasEffect(CardEffectType.AdjacencyPower))
            basePow += AdjacentPieceCount(card) * card.EffectAmount(CardEffectType.AdjacencyPower);
        // 賭博師：手札から1枚捨て、そのマス数×amount を加算
        if (card.HasEffect(CardEffectType.GambleDiscard) && _hand.Count > 0)
        {
            int di = Random.Range(0, _hand.Count);
            var discarded = _hand[di]; _hand.RemoveAt(di);
            basePow += discarded.Size * card.EffectAmount(CardEffectType.GambleDiscard);
            _message.text = $"賭博！「{discarded.displayName}」を捨てて威力に変えた！";
        }
        // 悪魔の心臓：HPが半分以下で攻撃+30%
        if (_main.DemonHeart && card.power > 0 && HpRatio() <= 0.5f) basePow = Mathf.RoundToInt(basePow * 1.3f);
        // 破壊神の腕：すべての攻撃威力1.5倍
        if (_main.Berserk && card.power > 0) basePow = Mathf.RoundToInt(basePow * 1.5f);
        if (_syn.attackPct > 0 && card.power > 0) basePow = Mathf.RoundToInt(basePow * (1f + _syn.attackPct / 100f)); // 盤面シナジー
        if (_main.CornersUnlocked >= 1 && card.power > 0) basePow = Mathf.RoundToInt(basePow * (1f + GameBalance.CornerAtkPct));            // 四隅の加護Lv1：攻撃威力アップ
        int pwCells = _main.Panel.MaxKindCover(card.id, CellKind.Power);
        if (pwCells > 0 && card.power > 0) basePow = Mathf.RoundToInt(basePow * (1f + GameBalance.PowerPctPerCell * pwCells));               // 強化マス：威力アップ/マス
        // コア（完全包囲）：盤面で他ピースに囲まれたカードは威力1.5倍
        bool isCore = card.power > 0 && _main.Panel.IsCoreCard(card.id);
        if (isCore)
        {
            basePow = Mathf.RoundToInt(basePow * GameBalance.CorePowerMult);
            StartCoroutine(TextPopup(new Vector2(-330f, 300f), "コア発動！", new Color(0.55f, 0.9f, 1f), 30));
        }
        // オーバードライブ：同ターン内の攻撃1発ごとに +25%（連撃のペンダントで+35%。乗算的に膨らむ）
        if (_overdrive > 0 && card.power > 0)
            basePow = Mathf.RoundToInt(basePow * (1f + OdPerStack * _overdrive));
        if (card.HasEffect(CardEffectType.GrowingPower))
        {
            int used = _useCounts.TryGetValue(card.id, out var u) ? u : 0;
            basePow += used * card.EffectAmount(CardEffectType.GrowingPower);
            _useCounts[card.id] = used + 1;
        }
        if (card.HasEffect(CardEffectType.BoardPower)) basePow += _main.BoardCells * card.EffectAmount(CardEffectType.BoardPower);
        if (card.HasEffect(CardEffectType.HandPower)) basePow += _hand.Count * card.EffectAmount(CardEffectType.HandPower);
        if (card.HasEffect(CardEffectType.ManaBurst)) { basePow += _mana * card.EffectAmount(CardEffectType.ManaBurst); _mana = 0; }
        if (card.HasEffect(CardEffectType.CurrentHpDmg)) basePow += Mathf.RoundToInt(_enemyHP * card.EffectAmount(CardEffectType.CurrentHpDmg) / 100f);

        // ---- 倍率系 ----
        if (card.HasEffect(CardEffectType.LowHpPower))
            basePow = Mathf.RoundToInt(basePow * (1f + (1f - HpRatio()) * card.EffectAmount(CardEffectType.LowHpPower) / 100f));
        if (card.HasEffect(CardEffectType.Execute) && _enemyHP <= _enemyMaxHP * card.EffectAmount(CardEffectType.Execute) / 100f)
            basePow *= 2;
        bool fizzle = false;
        if (card.HasEffect(CardEffectType.Gamble5050))
        {
            if (Random.value < 0.5f) basePow *= 2;
            else fizzle = true;
        }
        if (_vulnTurns > 0) basePow = Mathf.RoundToInt(basePow * (1f + _vulnPct / 100f)); // 弱点看破

        int hits = card.HasEffect(CardEffectType.MultiHit) ? Mathf.Max(1, card.EffectAmount(CardEffectType.MultiHit)) : 1;
        int totalDealt = 0;

        yield return AttackMotionFor(card);
        if (fizzle)
        {
            _message.text = $"{card.displayName}は空を切った……！";
            yield return new WaitForSeconds(0.6f);
        }
        else
        {
            for (int h = 0; h < hits; h++)
            {
                int dmg = Mathf.Max(1, Mathf.RoundToInt(basePow * mult));
                if (_enemyBlock > 0)   // 敵のブロックが吸収
                {
                    int ab = Mathf.Min(_enemyBlock, dmg);
                    _enemyBlock -= ab; dmg -= ab;
                    if (dmg <= 0)
                    {
                        _message.text = $"{_enemy.enemyName}のブロックに防がれた！";
                        yield return Pulse(_slimeRt, 1.04f, 0.15f);
                        RefreshAll();
                        continue;
                    }
                }
                yield return Impact(_slimeRt, _slimeImg, card, dmg, mult);
                _enemyHP = Mathf.Max(0, _enemyHP - dmg); _main.AddDamageStat(dmg);
                totalDealt += dmg;
                _message.text = hits > 1
                    ? $"{h + 1}ヒット！{dmg} ダメージ！"
                    : $"{(mult > 1.01f ? "会心！" : "")}{card.displayName}で {dmg} ダメージ！";
                RefreshAll();
                if (_enemyHP <= 0) break;
                if (hits > 1) yield return new WaitForSeconds(0.18f);
            }
        }

        // オーバードライブ：攻撃するたびスタック＋。次の攻撃が威力アップ（連撃ビルドの爆発力）
        if (card.power > 0 && !fizzle && _overdrive < GameBalance.OverdriveMaxStack)
        {
            _overdrive++;
            if (_overdrive >= 2)   // 2連撃目から見せる（毎回出すとうるさい）
            {
                var odCol = Color.Lerp(new Color(1f, 0.8f, 0.3f), new Color(1f, 0.3f, 0.2f), (_overdrive - 2) / 4f);
                StartCoroutine(TextPopup(new Vector2(-330f, 330f), $"オーバードライブ ×{_overdrive}（次+{OdPctInt * _overdrive}%）", odCol, 28 + _overdrive * 2));
                SpawnBurst(_actorRt.anchoredPosition + new Vector2(0, 60f), odCol, 6 + _overdrive * 2, 70f + _overdrive * 12f);
                if (_overdrive >= 4) StartCoroutine(ScreenFlash(odCol, 0.12f));   // 高スタックで画面が燃え始める
            }
        }

        // 起爆：毒・やけどを消費して追加ダメージ
        if (card.HasEffect(CardEffectType.Detonate) && (_poison > 0 || _burn > 0) && _enemyHP > 0)
        {
            int stock = _poison + _burn;
            int extra = stock * card.EffectAmount(CardEffectType.Detonate);
            if (_vulnTurns > 0) extra = Mathf.RoundToInt(extra * (1f + _vulnPct / 100f));
            _poison = 0; _burn = 0;
            _message.text = $"起爆！ {extra} の追加ダメージ！";
            yield return Impact(_slimeRt, _slimeImg, card, extra, 1.3f);
            _enemyHP = Mathf.Max(0, _enemyHP - extra); _main.AddDamageStat(extra);
            totalDealt += extra;
            RefreshAll();
        }

        // 吸血
        if (card.HasEffect(CardEffectType.LifeSteal) && totalDealt > 0)
        {
            int heal = Mathf.RoundToInt(totalDealt * card.EffectAmount(CardEffectType.LifeSteal) / 100f);
            _playerHP = Mathf.Min(_playerMaxHP, _playerHP + heal);
            _message.text = $"HPを {heal} 吸収した！";
            RefreshAll();
            yield return new WaitForSeconds(0.35f);
        }

        // アタックに付随する他効果（あれば）
        ApplyCardEffects(card, attackContext: true);
        // 攻撃アニメを止めて通常立ち絵に戻す（大技で拡大していたスケールもここで解除）
        if (_atkAnimCo != null) { StopCoroutine(_atkAnimCo); _atkAnimCo = null; }
        if (_bigCastActive) yield return MotionBigRecover();   // 大技は腕を下ろす動きで滑らかに戻す
        else if (_castActive) yield return MotionCastRecover(); // 通常攻撃は逆再生で戻す
        else if (_actorImg != null) _actorImg.sprite = ProtoPixelArt.MamaPhoto();
        if (_actorRt != null) { _actorRt.localScale = Vector3.one; _actorRt.anchoredPosition = _actorHome; }
        yield return new WaitForSeconds(0.6f);
    }

    // 攻撃ポーズのコマを順番に再生（最後のコマで止める）。1枚だけなら実質差し替え
    Coroutine _atkAnimCo;
    IEnumerator AttackFrameLoop()
    {
        // 1コマ目（構え）を表示して保持。以降のコマ切替はモーション側（MotionCast）が制御する
        var frames = ProtoPixelArt.AttackFrames();
        if (frames == null || frames.Count == 0 || _actorImg == null) yield break;
        _actorImg.sprite = frames[0];
        yield break;
    }

    IEnumerator ResolveSkill(CardDef card)
    {
        _message.text = $"{card.displayName}！";
        bool isHeal = card.HasEffect(CardEffectType.Heal) || card.HasEffect(CardEffectType.HealPercent) || card.HasEffect(CardEffectType.Regen);
        if (isHeal) { yield return MotionHeal(card); }
        else
        {
            yield return Pulse(_actorRt, 1.1f, 0.2f);
            StartCoroutine(FlashSprite(_actorImg, Color.Lerp(card.CategoryColor, Color.white, 0.4f)));
            SpawnBurst(_actorRt.anchoredPosition, card.CategoryColor, 10, 90f);
        }
        ApplyCardEffects(card, attackContext: false);
        yield return new WaitForSeconds(0.5f);
    }

    // 回復詠唱：大技と同じく両手を上げ（そこまで同じアニメ）→頭上で癒しの光→逆再生で待機に戻る
    IEnumerator MotionHeal(CardDef card)
    {
        Color heal = new Color(0.5f, 1f, 0.7f);      // 癒しの緑
        Color healW = new Color(0.85f, 1f, 0.9f);
        var frames = ProtoPixelArt.BigAttackFrames();
        int holdIdx = (frames != null && frames.Count > 0) ? Mathf.Min(4, frames.Count - 1) : -1;
        Vector2 body = _actorHome;
        Vector2 handsPos = _actorHome + new Vector2(2, 198);

        Vector2 feet = _actorHome + new Vector2(0, -150);   // 足元
        // 足元から立ち上る癒しのオーラ（横長・下寄り）
        var footAura = ProtoUI.CreateGlow("HealFootAura", _root, feet + new Vector2(0, 30), new Vector2(230, 120), heal); footAura.raycastTarget = false;
        var fart = (RectTransform)footAura.transform;

        _prevActorS = 1f; _prevActorFeet = FeetIdle;
        // ① 大技と同じ動きで両手を上げていく。癒しの粒は足元から立ち上る
        if (holdIdx >= 0)
        {
            for (int i = 0; i <= holdIdx; i++)
            {
                if (_actorImg == null) break;
                _actorImg.sprite = frames[i];
                float p = holdIdx > 0 ? (float)i / holdIdx : 1f;
                float bump = Mathf.Lerp(1f, 1.16f, p);   // 手を挙げきったところで少し大きく（足元固定）
                float fp = 0.6f + Mathf.Sin(Time.time * 12f) * 0.1f;
                fart.localScale = new Vector3(fp, 1f, 1f);
                var fc = heal; fc.a = 0.5f; footAura.color = fc;
                for (int k = 0; k < 3; k++)
                {
                    Vector2 f = feet + new Vector2(Random.Range(-90f, 90f), Random.Range(-20f, 30f));
                    StartCoroutine(RisingSpark(f, f + new Vector2(Random.Range(-15f, 15f), Random.Range(240f, 360f)), Random.value < 0.5f ? heal : healW));
                }
                yield return EaseActorScale(BigScaleAt(i) * bump, BigFeetAt(i), 0.15f);
            }
            if (_actorImg != null) _actorImg.sprite = frames[holdIdx];
        }
        else if (_actorImg != null) _actorImg.sprite = ProtoPixelArt.HealMama();
        float holdBase = holdIdx >= 0 ? BigScaleAt(holdIdx) * 1.16f : 1f;

        // ② 頭上で癒しの光。上昇する粒＋淡いフラッシュ
        var aura = ProtoUI.CreateGlow("HealAura", _root, handsPos, new Vector2(180, 180), heal); aura.raycastTarget = false;
        var art = (RectTransform)aura.transform;
        if (_sfx != null && _healCastClip != null) _sfx.PlayOneShot(_healCastClip);   // 回復の澄んだ響き
        StartCoroutine(ScreenFlash(heal, 0.14f));
        float t = 0f, dur = 0.9f;
        while (t < dur)
        {
            t += Time.deltaTime; float p = t / dur;
            float pulse = 1f + Mathf.Sin(t * 12f) * 0.12f;
            art.localScale = Vector3.one * ((0.6f + p * 1.4f) * pulse);
            // 頭上保持中、キャラを微かに呼吸させて自然に
            if (holdIdx >= 0) SetActorScaleFoot(holdBase * (1f + Mathf.Sin(t * 5f) * 0.015f), BigFeetAt(holdIdx));
            var ca = heal; ca.a = 0.6f * (1f - p * 0.5f); aura.color = ca;
            var fc2 = heal; fc2.a = 0.5f * (1f - p * 0.3f); footAura.color = fc2;
            for (int k = 0; k < 4; k++)
            {
                Vector2 f = feet + new Vector2(Random.Range(-95f, 95f), Random.Range(-20f, 30f));
                StartCoroutine(RisingSpark(f, f + new Vector2(Random.Range(-15f, 15f), Random.Range(260f, 380f)), Random.value < 0.5f ? heal : healW));
            }
            yield return null;
        }
        StartCoroutine(FlashSprite(_actorImg, healW));
        StartCoroutine(ShockExpand(feet + new Vector2(0, 20f), heal, 1.3f));
        SpawnBurst(handsPos, heal, 14, 90f);
        SpawnBurst(feet + new Vector2(0, 20f), heal, 16, 120f);
        Destroy(aura.gameObject);
        Destroy(footAura.gameObject);

        // ③ 逆再生でゆっくり待機モーションへ戻す。
        // 腕の動きが連続するコマ(349→350→351)だけ戻し、合わせ手の中間コマ(341/342)は飛ばして待機へ直接
        if (holdIdx >= 0)
        {
            int stopIdx = Mathf.Min(2, holdIdx);   // 腕が上がり始めるコマまで
            for (int i = holdIdx; i >= stopIdx; i--)
            {
                if (_actorImg == null) break;
                _actorImg.sprite = frames[i];
                yield return EaseActorScale(BigScaleAt(i), BigFeetAt(i), 0.11f);
            }
        }
        if (_actorImg != null) _actorImg.sprite = ProtoPixelArt.MamaPhoto();
        if (_actorRt != null) { _actorRt.localScale = Vector3.one; _actorRt.anchoredPosition = _actorHome; }
    }

    // 上へ舞い上がる癒しの粒
    IEnumerator RisingSpark(Vector2 from, Vector2 to, Color color)
    {
        var s = ProtoUI.CreatePanel("Spark", _root, from, new Vector2(9, 9), color); s.raycastTarget = false;
        var rt = (RectTransform)s.transform; Color c = color; float t = 0f, dur = 0.6f;
        while (t < dur) { t += Time.deltaTime; float p = t / dur; rt.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, p)); rt.localScale = Vector3.one * (1f - p * 0.5f); c.a = 1f - p; s.color = c; yield return null; }
        Destroy(s.gameObject);
    }

    void ApplyCardEffects(CardDef card, bool attackContext)
    {
        if (card.effects == null) return;
        foreach (var e in card.effects)
        {
            switch (e.type)
            {
                case CardEffectType.Draw:
                    for (int i = 0; i < e.amount; i++)
                    {
                        var drawn = DrawCard(); // HP連動の重み抽選＋黄金マス
                        _hand.Add(drawn);
                        _pendingDrawn.Add(drawn);
                    }
                    break;
                case CardEffectType.Block: _block += e.amount; break;
                case CardEffectType.Protect: _protectPct = Mathf.Max(_protectPct, e.amount); break;
                case CardEffectType.ParryStance: _parryStance = true; _message.text = "パリィの構え！次の敵の攻撃を弾け！"; break;
                case CardEffectType.FillEmptyOnUse: _emptyReduce += Mathf.Max(1, e.amount); _message.text = "盤面が満ちる……通常攻撃が出にくくなった！"; break;
                case CardEffectType.ManaBoostNextTurn: _manaBoostNext += e.amount; break;
                case CardEffectType.Strength: _strength += e.amount; break;
                case CardEffectType.Heal: _playerHP = Mathf.Min(_playerMaxHP, _playerHP + e.amount); break;
                case CardEffectType.HealPercent: _playerHP = Mathf.Min(_playerMaxHP, _playerHP + Mathf.RoundToInt(_playerMaxHP * e.amount / 100f)); break;
                case CardEffectType.Weak: _weakPct = e.amount; _weakTurns = Mathf.Max(_weakTurns, e.duration); break;
                case CardEffectType.Poison: _poison += e.amount; break;
                case CardEffectType.Burn: _burn += e.amount; break;
                case CardEffectType.PrimeNextAttackBlink: _primeBlink = true; break;
                case CardEffectType.BlinkOnUse: break; // ResolveAttackで処理済み

                // ---- 拡張効果 ----
                case CardEffectType.SelfDamage: _playerHP = Mathf.Max(0, _playerHP - e.amount); break;
                case CardEffectType.StunChance: if (Random.Range(0, 100) < e.amount) { _stunTurns = Mathf.Max(_stunTurns, 1); _message.text = "敵は麻痺した！"; } break;
                case CardEffectType.Stun: _stunTurns = Mathf.Max(_stunTurns, Mathf.Max(1, e.duration)); break;
                case CardEffectType.Regen: _regenAmt = e.amount; _regenTurns = Mathf.Max(_regenTurns, e.duration); break;
                case CardEffectType.Thorns: _thornsDmg = e.amount; _thornsTurns = Mathf.Max(_thornsTurns, e.duration); break;
                case CardEffectType.BlockRegen: _blockRegenAmt = e.amount; _blockRegenTurns = Mathf.Max(_blockRegenTurns, e.duration); break;
                case CardEffectType.Reflect: _reflectPct = Mathf.Max(_reflectPct, e.amount); break;
                case CardEffectType.GuardTurns: _guardPct = e.amount; _guardTurns = Mathf.Max(_guardTurns, e.duration); break;
                case CardEffectType.Counter: _counterDmg += e.amount; break;
                case CardEffectType.GainMoney: _main.AddMoney(e.amount); break;
                case CardEffectType.Vulnerable: _vulnPct = e.amount; _vulnTurns = Mathf.Max(_vulnTurns, e.duration); break;
                case CardEffectType.AilmentAmp: _ailmentAmp += e.amount; break;
                case CardEffectType.ManaNow: _mana += e.amount; break;
                case CardEffectType.NextTurnExtraCards: _nextTurnExtra += e.amount; break;
                case CardEffectType.PoisonBoost: { bool had = _poison > 0; _poison += e.amount; if (had) _poison *= 2; } break;
                case CardEffectType.BurnBoost: { bool had = _burn > 0; _burn += e.amount; if (had) _burn *= 2; } break;
                case CardEffectType.HealOverflowBlock:
                    {
                        int over = Mathf.Max(0, _playerHP + e.amount - _playerMaxHP);
                        _playerHP = Mathf.Min(_playerMaxHP, _playerHP + e.amount);
                        _block += over;
                    }
                    break;
                case CardEffectType.HealMissing: _playerHP = Mathf.Min(_playerMaxHP, _playerHP + Mathf.RoundToInt((_playerMaxHP - _playerHP) * e.amount / 100f)); break;
                case CardEffectType.TimeBomb: _timeBombDmg += e.amount; _timeBombTurns = Mathf.Max(1, e.duration); break;
                case CardEffectType.RandomDiscardDraw:
                    {
                        if (_hand.Count > 0) _hand.RemoveAt(Random.Range(0, _hand.Count));
                        for (int i = 0; i < e.amount; i++)
                        {
                            var dr = DrawCard();
                            _hand.Add(dr); _pendingDrawn.Add(dr);
                        }
                    }
                    break;
                case CardEffectType.RedrawAll:
                    {
                        int keep = _hand.Count;
                        _hand.Clear();
                        for (int i = 0; i < keep; i++)
                        {
                            var dr = DrawCard();
                            _hand.Add(dr); _pendingDrawn.Add(dr);
                        }
                    }
                    break;
                // 以下はResolveAttack側で処理する（ここでは何もしない）
                case CardEffectType.MultiHit:
                case CardEffectType.LifeSteal:
                case CardEffectType.Execute:
                case CardEffectType.Detonate:
                case CardEffectType.GrowingPower:
                case CardEffectType.BoardPower:
                case CardEffectType.LowHpPower:
                case CardEffectType.ManaBurst:
                case CardEffectType.HandPower:
                case CardEffectType.Gamble5050:
                case CardEffectType.CurrentHpDmg:
                case CardEffectType.GaugeOnUse:
                case CardEffectType.TapOrderOnUse:
                case CardEffectType.SlotOnUse:
                    break;
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
        int ailMul = _main.Equipped == EquipKind.AilmentPendant ? 2 : 1; // 状態異常のペンダント：状態異常ダメージ2倍

        // 毒の処理
        if (_poison > 0)
        {
            int pdmg = (_poison + _ailmentAmp) * ailMul; // 刻印で強化
            _message.text = $"毒！敵に {pdmg} ダメージ";
            yield return Impact(_slimeRt, _slimeImg, null, pdmg, 1f, 0);
            _enemyHP = Mathf.Max(0, _enemyHP - pdmg); _main.AddDamageStat(pdmg);
            RefreshAll();
            yield return new WaitForSeconds(0.5f);
            if (_enemyHP <= 0) { yield return Victory(); yield break; }
        }

        // やけどの処理
        if (_burn > 0)
        {
            int bdmg = (_burn + _ailmentAmp) * ailMul;
            _message.text = $"やけど！敵に {bdmg} ダメージ";
            yield return Impact(_slimeRt, _slimeImg, null, bdmg, 1f, 0);
            _enemyHP = Mathf.Max(0, _enemyHP - bdmg); _main.AddDamageStat(bdmg);
            RefreshAll();
            yield return new WaitForSeconds(0.5f);
            if (_enemyHP <= 0) { yield return Victory(); yield break; }
        }

        // 時限爆弾のカウントダウン
        if (_timeBombDmg > 0)
        {
            _timeBombTurns--;
            if (_timeBombTurns <= 0)
            {
                _message.text = $"時限爆弾が爆発！ {_timeBombDmg} ダメージ！";
                yield return Impact(_slimeRt, _slimeImg, null, _timeBombDmg, 1.3f, 2);
                _enemyHP = Mathf.Max(0, _enemyHP - _timeBombDmg); _main.AddDamageStat(_timeBombDmg);
                _timeBombDmg = 0;
                RefreshAll();
                yield return new WaitForSeconds(0.5f);
                if (_enemyHP <= 0) { yield return Victory(); yield break; }
            }
            else { _message.text = $"時限爆弾……あと {_timeBombTurns} ターン"; yield return new WaitForSeconds(0.5f); }
        }

        // 麻痺・凍結などの行動不能
        if (_stunTurns > 0)
        {
            _stunTurns--;
            _message.text = $"{_enemy.enemyName}は動けない！";
            yield return Pulse(_slimeRt, 1.05f, 0.3f);
            yield return new WaitForSeconds(0.7f);
        }
        else
        {
            _message.text = $"{_enemy.enemyName}のターン…";
            yield return new WaitForSeconds(0.7f);
            yield return EnemyAttackSequence();
        }

        if (_weakTurns > 0) _weakTurns--;
        if (_vulnTurns > 0) _vulnTurns--;
        if (_thornsTurns > 0) _thornsTurns--;
        if (_guardTurns > 0) _guardTurns--;

        if (_playerHP <= 0) { yield return Defeat(); yield break; }

        RollIntent(); // 次のプレイヤーターン用に予告を更新
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
        if (_actorRt != null) { _actorRt.anchoredPosition = new Vector2(-330, GroundY + 6f); _actorRt.sizeDelta = new Vector2(320, 130); _actorRt.localRotation = Quaternion.identity; _actorRt.localScale = Vector3.one; }
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

        // 敗北でもスコアが貯まる（到達度に応じて加算→累計でカード解放が進む）
        int prevLv = ProtoUnlocks.UnlockLevel;
        int runScore = _main.CurrentDepth * 25 + (_main.Wave - 1) * 250 + _main.StatTotalDamage / 50;
        ProtoUnlocks.AddRunScore(runScore);
        ProtoUI.CreateText("GOScore", go, $"今回のスコア {runScore}　／　累計 {ProtoUnlocks.TotalScore}", 22,
            new Vector2(0, 68), new Vector2(900, 32), ProtoUI.Gold);
        if (ProtoUnlocks.UnlockLevel > prevLv)
            ProtoUI.CreateText("GOUnlock", go, "◆ 新しいカードが解放された！", 20,
                new Vector2(0, 34), new Vector2(900, 28), new Color(1f, 0.7f, 0.9f));

        ProtoUI.CreateGoldButton("Retry", go, "もう一度やり直す", 24, new Vector2(0, -40), new Vector2(330, 68),
            new Color(0.30f, 0.45f, 0.32f), () => _main.RestartRun());
        ProtoUI.CreateGoldButton("Quit", go, "ゲームを終了する", 22, new Vector2(0, -130), new Vector2(330, 62),
            new Color(0.45f, 0.25f, 0.25f), () => { /* ここでは何もしない */ });
    }

    IEnumerator EnemyAttackSequence()
    {
        var atk = _intent ?? _enemy.PickAttack(); // 予告した行動を実行
        _enemyBlock = 0;   // 敵ブロックは敵ターン開始でリセット

        // ---- ギミック行動 ----
        if (atk.act == EnemyActKind.Guard)
        {
            _enemyBlock = atk.amount;
            _message.text = $"{_enemy.enemyName}は身を固めた！（ブロック{atk.amount}）";
            yield return Pulse(_slimeRt, 1.08f, 0.25f);
            yield return new WaitForSeconds(0.6f); RefreshAll(); yield break;
        }
        if (atk.act == EnemyActKind.PowerUp)
        {
            _enemyAtkUp += atk.amount;
            _message.text = $"{_enemy.enemyName}の攻撃力が上がった！（+{_enemyAtkUp}）";
            StartCoroutine(FlashSprite(_slimeImg, new Color(1f, 0.5f, 0.8f)));
            yield return Pulse(_slimeRt, 1.15f, 0.3f);
            yield return new WaitForSeconds(0.6f); RefreshAll(); yield break;
        }
        if (atk.act == EnemyActKind.Charge)
        {
            _enemyCharged = true;
            _message.text = $"{_enemy.enemyName}は力を溜めている……次の攻撃が強力になる！";
            StartCoroutine(FlashSprite(_slimeImg, new Color(1f, 0.9f, 0.4f)));
            yield return Pulse(_slimeRt, 1.12f, 0.3f);
            yield return new WaitForSeconds(0.7f); RefreshAll(); yield break;
        }
        _message.text = $"{_enemy.enemyName}の {atk.name}！";
        yield return new WaitForSeconds(0.4f);

        if (atk.hits == 0)
        {
            yield return Pulse(_slimeRt, 1.08f, 0.25f);
            _message.text = "しかし何も起こらなかった！";
            yield return new WaitForSeconds(0.6f);
            yield break;
        }

        int parryCut = 0;
        if (_parryStance)
        {
            _parryStance = false;
            yield return RunParry();
            parryCut = _parryCutPct;
        }
        _sfx.PlayOneShot(_swingClip);
        yield return Lunge(_slimeRt, new Vector2(-150, 0));
        int sfxTier = atk.mult >= 1.5f ? 2 : atk.hits > 1 ? 0 : 1;

        // ブロック等で防御している状態なら、攻撃を受ける前にガードの構えを取る
        bool guarding = _block > 0 || _protectPct > 0 || _guardTurns > 0;
        if (guarding) yield return MotionGuardEnter();

        for (int h = 0; h < atk.hits; h++)
        {
            int raw = Mathf.RoundToInt(((Random.Range(_enemy.minAtk, _enemy.maxAtk + 1) + _enemyAtkUp) * atk.mult + 3 * (_effWave - 1)) * _main.EnemyDmgMul); // アセンション・強化で攻撃増
            if (_enemyCharged) raw = Mathf.RoundToInt(raw * 1.8f);   // チャージ解放
            if (_weakTurns > 0) raw = Mathf.RoundToInt(raw * (1f - _weakPct / 100f));
            if (parryCut > 0) raw = Mathf.RoundToInt(raw * (1f - parryCut / 100f));   // パリィ軽減
            if (_main.Berserk) raw = Mathf.RoundToInt(raw * 1.25f);   // 破壊神の腕：被ダメージ+25%
            int dmg = Mathf.Max(1, raw);
            if (_main.Equipped == EquipKind.GuardPendant) dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.95f)); // 加護のペンダント
            if (_guardTurns > 0) dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * (1f - _guardPct / 100f)));        // 聖なる誓い（継続軽減）
            if (_protectPct > 0) { dmg = Mathf.Max(0, Mathf.RoundToInt(dmg * (1f - _protectPct / 100f))); _protectPct = 0; }
            int reflected = 0;
            if (_reflectPct > 0)
            {
                reflected = Mathf.RoundToInt(dmg * _reflectPct / 100f);
                dmg -= reflected; _reflectPct = 0; // 反射は1回きり
            }
            if (_block > 0) { int absorb = Mathf.Min(_block, dmg); _block -= absorb; dmg -= absorb; }
            if (guarding) StartCoroutine(GuardHitFx(dmg <= 0));   // ガードのエフェクト＆音

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

            // 毒攻撃：ヒット時にプレイヤーへ毒を付与
            if (atk.act == EnemyActKind.PoisonPlayer && dmg > 0 && atk.amount > 0)
            {
                _playerPoison += atk.amount;
                _message.text = $"毒を受けた！（毒{_playerPoison}）";
                yield return new WaitForSeconds(0.3f);
            }

            // 反射・反撃・茨のダメージを敵へ返す
            int payback = reflected + (_thornsTurns > 0 ? _thornsDmg : 0);
            if (_counterDmg > 0) { payback += _counterDmg; _counterDmg = 0; }
            if (payback > 0 && _enemyHP > 0)
            {
                _message.text = $"反撃！敵に {payback} ダメージ！";
                yield return Impact(_slimeRt, _slimeImg, null, payback, 1f, 0);
                _enemyHP = Mathf.Max(0, _enemyHP - payback); _main.AddDamageStat(payback);
                RefreshAll();
                if (_enemyHP <= 0) { yield return Victory(); yield break; }
            }
        }
        _enemyCharged = false;   // チャージは攻撃1回で消費
        if (guarding && _playerHP > 0) yield return MotionGuardExit();   // ガードを解いて待機へ

        // 中ボス以上：攻撃のあと盤面に歪みマスを生む（この戦闘中だけマスを封印）
        if (_eliteBattle && _playerHP > 0 && atk.hits > 0)
            yield return TryDistortBoard();

        yield return new WaitForSeconds(0.3f);
    }

    // 盤面に歪みマスを1つ生む（中ボス以上の攻撃時。ランダムな解放マスを封印。上限あり）
    IEnumerator TryDistortBoard()
    {
        var panel = _main.Panel;
        int unlocked = panel.UnlockedCount();
        int cap = Mathf.Max(1, unlocked / 3);   // 盤面の1/3までしか歪ませない
        if (panel.Sealed.Count >= cap) yield break;
        if (Random.value >= 0.5f) yield break;   // 攻撃のたび50%で発生

        var pool = new List<Vector2Int>();
        foreach (var c in panel.GetUnlockedCells())
            if (!panel.Sealed.Contains(c)) pool.Add(c);
        if (pool.Count == 0) yield break;

        panel.Sealed.Add(pool[Random.Range(0, pool.Count)]);
        _message.text = $"{_enemy.enemyName}の力で盤面が歪んだ！マスが封じられた……";
        StartCoroutine(ScreenFlash(new Color(0.5f, 0.15f, 0.6f), 0.28f));
        RefreshAll();
        yield return new WaitForSeconds(0.7f);
    }

    // ==================== 勝敗 ====================

    IEnumerator Victory()
    {
        _inputLocked = true;
        _main.SetCurrentHP(_playerHP);   // 戦闘後HPを保存（次戦闘へ継続）
        _main.Panel.Sealed.Clear();   // 歪みマスは戦闘終了で解除

        // お金はランダム
        int reward = Mathf.Max(1, Mathf.RoundToInt(_enemy.moneyReward * Random.Range(0.7f, 1.5f)));
        _main.AddMoney(reward);

        // 獲得ピース候補
        int count = _main.Cfg != null ? _main.Cfg.rewardChoiceCount : 3;
        var owned = new HashSet<string>(_main.OwnedCardIds);
        var choices = _main.Db.RandomCards(count, owned, _main.CurrentDepth, ProtoUnlocks.UnlockLevel);

        // レイアウト（中央寄り）
        ((RectTransform)_resultText.transform).anchoredPosition = new Vector2(0, 230);
        _resultText.color = ProtoUI.Gold;
        ((RectTransform)_resultSub.transform).anchoredPosition = new Vector2(0, 160);
        _rewardArea.anchoredPosition = new Vector2(0, -40);

        _resultText.text = $"{_enemy.enemyName}を倒した！";
        _resultSub.text = $"お金：+{reward}￥　　獲得ピース：{(choices.Count == 0 ? "なし" : $"{choices.Count}ピース")}";
        _resultRoot.gameObject.SetActive(true);
        yield return BuildRewardChoices(choices);
    }

    IEnumerator BuildRewardChoices(List<CardDef> choices)
    {
        foreach (Transform c in _rewardArea) Destroy(c.gameObject);

        if (choices.Count == 0)
        {
            ProtoUI.CreateGoldButton("Skip", _rewardArea, "マップへ戻る", 24, new Vector2(0, -120), new Vector2(280, 64),
                new Color(0.35f, 0.3f, 0.55f), () => _main.OnBattleWon());
            yield break;
        }

        float spacing = 300f;
        float startX = -(choices.Count - 1) * spacing / 2f;
        for (int i = 0; i < choices.Count; i++)
            BuildRewardCard(choices[i], new Vector2(startX + i * spacing, 0));

        ProtoUI.CreateText("Hint", _rewardArea, "ピースをクリックで選択して獲得", 18, new Vector2(0, -185), new Vector2(600, 26),
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

        var inner = ProtoUI.VGrad(ProtoUI.CreatePanel("In", frame.transform, Vector2.zero, new Vector2(238, 288), new Color(0.15f, 0.13f, 0.21f)));
        inner.raycastTarget = false;
        if (card.rarity >= 2)   // レア報酬：後光＋走査光
        {
            var halo = ProtoUI.CreateGlow("Halo", frame.transform, Vector2.zero, new Vector2(350, 400), new Color(1f, 0.82f, 0.35f, 0.5f));
            halo.transform.SetAsFirstSibling();
            var hg = halo.gameObject.AddComponent<RareGlow>();
            hg.target = halo; hg.colA = new Color(1f, 0.8f, 0.3f, 0.2f); hg.colB = new Color(1f, 0.88f, 0.5f, 0.6f);
            ProtoUI.AddShine(inner, new Vector2(238, 288));
        }
        var nm = ProtoUI.CreateText("N", inner.transform, card.displayName, 20, new Vector2(0, 120), new Vector2(230, 30), card.RarityColor);
        nm.fontStyle = FontStyles.Bold;
        if (card.rarity >= 2) { var rg = nm.gameObject.AddComponent<RareGlow>(); rg.target = nm; rg.colA = card.RarityColor; rg.colB = Color.white; }   // レアは光る
        ProtoUI.CreateText("K", inner.transform,
            $"{card.ThemeTagRich()}{(CardDef.KindLabel(card.Category))} / {card.Size}マス / マナ{card.ManaCost}", 14,
            new Vector2(0, 92), new Vector2(230, 22), new Color(0.8f, 0.85f, 1f));

        var art = ProtoUI.CreatePanel("Art", inner.transform, new Vector2(0, 20), new Vector2(210, 120), new Color(0.05f, 0.04f, 0.10f));
        art.raycastTarget = false;
        var shape = card.Shape;
        float cs = 15f, gap = 2f;
        int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y), maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;
        foreach (var v in shape)
            ProtoUI.Bevel(ProtoUI.CreatePanel("M", art.transform, new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), card.CategoryColor)).raycastTarget = false;

        string eff = card.kind == CardKind.Attack
            ? (card.HasEffect(CardEffectType.BlinkOnUse) ? $"威力{card.power}・使用時に点滅" : $"威力 {card.power}")
            : card.description;
        var d = ProtoUI.CreateText("D", inner.transform, eff, 14, new Vector2(0, -100), new Vector2(220, 70),
            new Color(0.9f, 0.92f, 1f), TextAlignmentOptions.Top);
        d.raycastTarget = false;
    }

    // ==================== 点滅順番当て（効果発動時のみ） ====================

    static Sprite _circleSprite;
    static Sprite CircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int N = 64; float r = N / 2f - 1f, c = (N - 1) / 2f;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(r - d + 0.5f); // 円の縁を少しなめらかに
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _circleSprite;
    }

    float _challengeMultiplier;

    // このフレームで左クリックされたか（ミニゲーム用）
    bool ClickedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (ms != null && ms.leftButton.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0)) return true;
#endif
        return false;
    }

    bool _parryStance;   // パリィの構え（次の敵の攻撃でQTE発動）
    int _parryCutPct;    // パリィ結果の被ダメ軽減%
    bool _eliteBattle;   // 中ボス以上（攻撃で盤面に歪みマスを作る）
    int _emptyReduce;    // 圧縮カード：抽選の空きマスを減らす（この戦闘中）

    // ==================== ミニゲーム：連打 ====================
    // 制限時間内にひたすらクリック！連打数で威力倍率（20連打で2倍）
    IEnumerator RunMash(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　連打しろ！！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        var counter = ProtoUI.CreateText("MashN", _pieceArea, "0", 120, new Vector2(0, 0), new Vector2(500, 140), Color.white);
        counter.fontStyle = FontStyles.Bold;
        yield return null;

        const float dur = 3f;
        float t = 0f; int count = 0;
        while (t < dur)
        {
            t += Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, 1f - t / dur, 500f);
            if (ClickedThisFrame())
            {
                count++;
                counter.text = count.ToString();
                counter.rectTransform.localScale = Vector3.one * 1.25f;
                counter.color = count >= 20 ? new Color(1f, 0.5f, 0.2f) : Color.white;
            }
            counter.rectTransform.localScale = Vector3.Lerp(counter.rectTransform.localScale, Vector3.one, Time.deltaTime * 10f);
            yield return null;
        }
        _challengeMultiplier = Mathf.Min(2f, 0.8f + count * 0.06f);
        _challengePrompt.text = $"{count}連打！ → 威力 {Mathf.RoundToInt(_challengeMultiplier * 100)}%";
        yield return new WaitForSeconds(0.9f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // ==================== ミニゲーム：ルーレット ====================
    // 盤面を回るボールを当たりポケット（赤）で止める。ぴったり2.2倍／近く1.4倍／外れ0.9倍
    IEnumerator RunRoulette(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　高い倍率のマスで止めろ！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        const float RAD = 150f;
        Vector2 Dir(float deg) => new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));

        // 16マスの倍率配分：200×1・150×2・120×3・100×5・50×5（高倍率が散らばるよう配置）
        float[] mults = { 1.0f, 0.5f, 1.2f, 1.0f, 0.5f, 1.5f, 1.0f, 0.5f, 1.2f, 1.0f, 0.5f, 2.0f, 1.0f, 0.5f, 1.2f, 1.5f };
        int SLOTS = mults.Length;
        float STEP = 360f / SLOTS;
        Color MultColor(float m) => m >= 2f ? new Color(1f, 0.82f, 0.25f)      // 200%＝金
                                  : m >= 1.5f ? new Color(0.85f, 0.35f, 0.95f) // 150%＝紫
                                  : m >= 1.2f ? new Color(0.35f, 0.6f, 1f)      // 120%＝青
                                  : m >= 1.0f ? new Color(0.35f, 0.8f, 0.45f)   // 100%＝緑
                                  : new Color(0.5f, 0.16f, 0.18f);              // 50%＝暗赤

        // 外周のソフト光
        ProtoUI.CreateGlow("RouRing", _pieceArea, Vector2.zero, new Vector2(2f * RAD + 110f, 2f * RAD + 110f), new Color(0.5f, 0.42f, 0.2f, 0.3f)).raycastTarget = false;

        // 手続き生成したホイール画像（色分けセグメント＋境界線＋リム）を1枚で表示
        float wheelRadius = RAD + 30f;
        var wheelTex = BuildWheelTexture(mults, MultColor, 360);
        var wheelGo = new GameObject("RouWheel", typeof(RectTransform), typeof(RawImage));
        var wrt = (RectTransform)wheelGo.transform;
        wrt.SetParent(_pieceArea, false); wrt.anchoredPosition = Vector2.zero; wrt.sizeDelta = new Vector2(2f * wheelRadius, 2f * wheelRadius);
        var wraw = wheelGo.GetComponent<RawImage>(); wraw.texture = wheelTex; wraw.raycastTarget = false;

        // 各マスの倍率テキスト＋200%マスの脈動グロー
        for (int i = 0; i < SLOTS; i++)
        {
            float a = i * STEP;
            Vector2 lp = Dir(a) * (RAD + 52f);   // 盤の外側（リムのすぐ外）＝盤と被らず読める
            if (mults[i] >= 2f) StartCoroutine(PocketPulse((RectTransform)ProtoUI.CreateGlow("RPGlow", _pieceArea, Dir(a) * RAD, new Vector2(66, 66), new Color(1f, 0.9f, 0.35f, 0.55f)).transform));
            var lbl = ProtoUI.CreateText($"RM{i}", _pieceArea, $"{Mathf.RoundToInt(mults[i] * 100)}", 26, lp, new Vector2(56, 32), Color.white);
            lbl.fontStyle = FontStyles.Bold; lbl.raycastTarget = false;
            lbl.outlineWidth = 0.25f; lbl.outlineColor = new Color32(0, 0, 0, 230);
        }

        // 中央ハブ
        var hub = ProtoUI.CreatePanel("RouHub", _pieceArea, Vector2.zero, new Vector2(34, 34), new Color(0.82f, 0.68f, 0.34f, 1f));
        hub.raycastTarget = false; hub.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var hub2 = ProtoUI.CreatePanel("RouHub2", _pieceArea, Vector2.zero, new Vector2(16, 16), new Color(0.12f, 0.11f, 0.09f, 1f));
        hub2.raycastTarget = false; hub2.transform.localRotation = Quaternion.Euler(0, 0, 45);

        // 針：根元が太く先端が細い三角形（中心から外周へ。円内に収める）
        float needleLen = RAD - 6f;
        var needleGo = new GameObject("RNeedle", typeof(RectTransform), typeof(Image));
        var srt = (RectTransform)needleGo.transform;
        srt.SetParent(_pieceArea, false); srt.pivot = new Vector2(0.5f, 0f); srt.anchoredPosition = Vector2.zero;
        srt.sizeDelta = new Vector2(34, needleLen);
        var nImg = needleGo.GetComponent<Image>(); nImg.sprite = NeedleSprite(); nImg.type = Image.Type.Simple; nImg.raycastTarget = false;
        // 根元の飾り（金の丸）
        ProtoUI.CreatePanel("RNeedleHub", _pieceArea, Vector2.zero, new Vector2(26, 26), new Color(0.85f, 0.7f, 0.35f, 1f)).transform.localRotation = Quaternion.Euler(0, 0, 45);
        // ボール（外周の白い玉。これが指すマスが結果）
        var ball = ProtoUI.CreateGlow("RBall", _pieceArea, Dir(0) * (RAD - 4f), new Vector2(30, 30), Color.white);
        ball.raycastTarget = false;
        var ballCore = ProtoUI.CreatePanel("RBallCore", ball.transform, Vector2.zero, new Vector2(13, 13), Color.white);
        ballCore.raycastTarget = false; ballCore.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var ballRt = (RectTransform)ball.transform;
        float ballR = RAD - 4f;   // ボールの周回半径（リムの内側）
        yield return null;

        float angle = 0f; float speed = 300f; float timeout = 5f; bool stopped = false; float trailAcc = 0f;
        while (timeout > 0f)
        {
            angle = (angle + speed * Time.deltaTime) % 360f;
            srt.localRotation = Quaternion.Euler(0, 0, -angle);
            ballRt.anchoredPosition = Dir(angle) * ballR;
            trailAcc += Time.deltaTime;
            if (trailAcc >= 0.02f) { trailAcc = 0f; StartCoroutine(BallTrail(_pieceArea, Dir(angle) * ballR)); }
            timeout -= Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, timeout / 5f, 500f);
            if (ClickedThisFrame()) { stopped = true; break; }
            yield return null;
        }
        // 最寄りマスへコトッと吸い込む
        int slot = Mathf.RoundToInt(angle / STEP) % SLOTS;
        if (stopped)
        {
            float snapTo = slot * STEP;
            float t2 = 0f;
            while (t2 < 0.25f)
            {
                t2 += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t2 / 0.25f);
                float ang = Mathf.LerpAngle(angle, snapTo, p);
                srt.localRotation = Quaternion.Euler(0, 0, -ang);
                ballRt.anchoredPosition = Dir(ang) * ballR;
                yield return null;
            }
            angle = snapTo;
        }
        _challengeMultiplier = stopped ? mults[slot] : 0.5f;   // 止められなければ最低（50%）
        int pct = Mathf.RoundToInt(_challengeMultiplier * 100);
        yield return MinigameResultFx(_challengeMultiplier, _pieceArea.anchoredPosition + Dir(angle) * ballR);
        _challengePrompt.text = _challengeMultiplier >= 2f ? $"ジャックポット！ 威力{pct}%！！"
            : _challengeMultiplier >= 1.2f ? $"当たり！ 威力{pct}%！"
            : _challengeMultiplier >= 1.0f ? $"威力{pct}%"
            : $"ハズレ… 威力{pct}%";
        yield return new WaitForSeconds(0.8f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
        Destroy(wheelTex);   // 生成したホイール画像を破棄
    }

    // 根元が太く先端が細い三角形の針スプライト（下がベース＝pivot、上が先端）
    Sprite _needleSprite;
    Sprite NeedleSprite()
    {
        if (_needleSprite != null) return _needleSprite;
        int w = 40, h = 220;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        var fill = new Color(0.96f, 0.83f, 0.42f, 1f);
        var edge = new Color(0.5f, 0.36f, 0.14f, 1f);
        var hi = new Color(1f, 0.95f, 0.7f, 1f);
        float cx = w / 2f;
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            float half = Mathf.Lerp(w / 2f - 1.5f, 0.6f, t * t * 0.6f + t * 0.4f);   // 根元太→先細
            for (int x = 0; x < w; x++)
            {
                float d = Mathf.Abs(x + 0.5f - cx);
                Color c;
                if (d > half) c = new Color(0, 0, 0, 0);
                else if (d > half - 2.2f) c = edge;             // 縁
                else if (d < 3f) c = Color.Lerp(fill, hi, 0.6f); // 中央ハイライト
                else c = fill;
                px[y * w + x] = c;
            }
        }
        tex.SetPixels(px); tex.Apply();
        _needleSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 100f);
        return _needleSprite;
    }

    // ルーレット盤を手続き生成（色分けセグメント＋黒い境界線＋外周リム）。1枚の画像として表示する
    Texture2D BuildWheelTexture(float[] mults, System.Func<float, Color> colorOf, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float cx = size / 2f, cy = size / 2f;
        float radius = size / 2f - 3f;
        float rimW = size * 0.05f;
        int slots = mults.Length; float step = 360f / slots;
        float lineHalf = size * 0.005f + 1.2f;
        var clear = new Color(0, 0, 0, 0);
        var rimCol = new Color(0.30f, 0.17f, 0.10f, 1f);   // 焦げ茶のリム
        var rimEdge = new Color(0.55f, 0.42f, 0.2f, 1f);   // 金の細縁
        var lineCol = new Color(0.03f, 0.03f, 0.05f, 1f);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                Color c;
                if (r > radius) c = clear;
                else if (r > radius - 2f) c = rimEdge;
                else if (r > radius - rimW) c = rimCol;
                else
                {
                    float deg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; if (deg < 0) deg += 360f;
                    int slot = Mathf.RoundToInt(deg / step) % slots;
                    c = colorOf(mults[slot]);
                    // 境界線（隣のマスとの境目）
                    float bnd = (Mathf.Round(deg / step - 0.5f) + 0.5f) * step;
                    float dPix = Mathf.Abs(Mathf.DeltaAngle(deg, bnd)) * Mathf.Deg2Rad * r;
                    if (dPix < lineHalf) c = lineCol;
                    // 内周の細いリング線
                    if (Mathf.Abs(r - (radius - rimW)) < 1.5f) c = lineCol;
                }
                px[y * size + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        return tex;
    }

    // ボールの光の尾
    IEnumerator BallTrail(Transform parent, Vector2 pos)
    {
        var g = ProtoUI.CreateGlow("BallTrail", parent, pos, new Vector2(22, 22), new Color(1f, 1f, 0.9f, 0.6f)); g.raycastTarget = false;
        float t = 0f, dur = 0.25f; Color c = g.color;
        while (t < dur && g != null) { t += Time.deltaTime; float p = t / dur; c.a = 0.6f * (1f - p); g.color = c; ((RectTransform)g.transform).localScale = Vector3.one * (1f - p * 0.5f); yield return null; }
        if (g != null) Destroy(g.gameObject);
    }

    // 当たりポケットの発光をゆっくり脈打たせる
    IEnumerator PocketPulse(RectTransform rt)
    {
        float t = 0f;
        while (rt != null)
        {
            t += Time.deltaTime;
            rt.localScale = Vector3.one * (1f + Mathf.Sin(t * 6f) * 0.18f);
            yield return null;
        }
    }

    // ==================== ミニゲーム：軌道なぞり ====================
    // 光る印をカーソルで①から順になぞる。なぞれた数とスピードで威力倍率（最大1.8倍）
    IEnumerator RunTrace(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　①から順に、線から外れずになぞれ！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        // 複雑な剣型を毎回ランダムに生成（折り返し・交差する軌道＝難しい）
        var pts = TracePattern();
        int N = pts.Length;

        // ガイド軌道：区間ごとに色を①→順にグラデーション（虹）させ、どの線がどの順かを色で追えるようにする
        var trailLayer = ProtoUI.CreateRect("TraceGuide", _pieceArea); trailLayer.anchoredPosition = Vector2.zero;
        int segCount = N - 1;
        var segDots = new System.Collections.Generic.List<Image>[segCount];
        var segBase = new Color[segCount];
        for (int i = 0; i < segCount; i++)
        {
            float f = segCount <= 1 ? 0f : i / (float)(segCount - 1);
            segBase[i] = Color.HSVToRGB((0.5f - 0.5f * f + 1f) % 1f, 0.7f, 1f);   // 青緑→紫→赤へ順に変化
            segDots[i] = new System.Collections.Generic.List<Image>();
            Vector2 a = pts[i], b = pts[i + 1];
            int dots = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 17f));   // 密に打って「連続した線」に見せる
            for (int k = 1; k < dots; k++)
            {
                var gp = Vector2.Lerp(a, b, k / (float)dots);
                var d = ProtoUI.CreatePanel($"TGuide{i}", trailLayer, gp, new Vector2(9, 9), segBase[i]);
                d.raycastTarget = false;
                segDots[i].Add(d);
            }
        }

        var nodes = new Image[N];
        var labels = new TextMeshProUGUI[N];
        string circ = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫";
        for (int i = 0; i < N; i++)
        {
            ProtoUI.CreateGlow($"TNG{i}", _pieceArea, pts[i], new Vector2(74, 74), new Color(0.3f, 0.4f, 0.8f, 0.5f)).raycastTarget = false;
            var node = ProtoUI.CreatePanel($"TN{i}", _pieceArea, pts[i], new Vector2(58, 58), new Color(0.22f, 0.28f, 0.5f, 0.98f));
            node.raycastTarget = false; node.transform.localRotation = Quaternion.Euler(0, 0, 45);
            var lb = ProtoUI.CreateText($"TL{i}", _pieceArea, circ.Substring(i, 1), 30, pts[i], new Vector2(56, 40), Color.white);
            lb.raycastTarget = false;
            nodes[i] = node; labels[i] = lb;
        }
        // カーソル位置の光る剣先
        var tip = ProtoUI.CreateGlow("TraceTip", _pieceArea, Vector2.zero, new Vector2(34, 34), new Color(0.7f, 0.95f, 1f, 0.9f)); tip.raycastTarget = false;
        yield return null;

        float dur = 2.8f + N * 0.42f;   // ノード数に応じた制限時間
        float fastT = dur * 0.62f;      // これ以内で完走なら神速
        float t = 0f; int reached = 0; bool brokeOff = false; float trailAcc = 0f; float onPathGrace = 0f;
        const float TOL = 32f;        // ノード到達判定（狭め＝難しい）
        const float PATH_TOL = 38f;   // 線からの許容外れ幅（超えたら失敗＝難しい）
        var area = (RectTransform)_pieceArea;

        // ガイドの強調更新：今なぞる線（reached-1区間）だけを明るく点滅、済みは薄く、これからは中間の濃さ
        void RefreshGuide()
        {
            float ps = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            int active = reached - 1;
            for (int s = 0; s < segCount; s++)
            {
                Color c; float sz;
                if (s == active) { c = Color.Lerp(segBase[s], Color.white, 0.4f); c.a = 0.55f + 0.45f * ps; sz = 12f; }   // 今の線
                else if (s < active) { c = segBase[s]; c.a = 0.16f; sz = 8f; }                                            // 済み
                else { c = segBase[s]; c.a = 0.42f; sz = 9f; }                                                            // これから
                foreach (var d in segDots[s]) { if (d == null) continue; d.color = c; ((RectTransform)d.transform).sizeDelta = new Vector2(sz, sz); }
            }
            // 次に目指すノードを少し脈動させて分かりやすく
            for (int i = 0; i < N; i++) if (nodes[i] != null) nodes[i].rectTransform.localScale = Vector3.one;
            if (reached < N && nodes[reached] != null) nodes[reached].rectTransform.localScale = Vector3.one * (1f + 0.14f * ps);
        }

        while (t < dur && reached < N && !brokeOff)
        {
            t += Time.deltaTime;
            RefreshGuide();
            ProtoUI.SetGauge(_timerFill, 1f - t / dur, 500f);
            Vector2 lp;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(area, MouseScreenPos(), null, out lp))
            {
                tip.rectTransform.anchoredPosition = lp;
                // 自分の描いた軌跡を残す（水色の光の粒）
                trailAcc += Time.deltaTime;
                if (trailAcc >= 0.016f) { trailAcc = 0f; StartCoroutine(TraceStroke(trailLayer, lp)); }

                var target = ((RectTransform)nodes[reached].transform).anchoredPosition;
                // 次ノードに到達
                if (Vector2.Distance(lp, target) <= TOL)
                {
                    nodes[reached].color = new Color(0.35f, 0.95f, 0.6f);
                    labels[reached].color = new Color(0.1f, 0.2f, 0.12f);
                    StartCoroutine(ShockExpand(_pieceArea.anchoredPosition + target, new Color(0.5f, 1f, 0.7f), 0.8f));
                    SpawnBurst(_pieceArea.anchoredPosition + target, new Color(0.5f, 1f, 0.7f), 8, 60f);
                    _sfx?.PlayOneShot(_swingClip, 0.5f);
                    reached++;
                }
                // 線から大きく外れたら失敗（現ノード→次ノードの線分からの距離で判定）
                else if (reached > 0)
                {
                    float dl = DistToSegment(lp, ((RectTransform)nodes[reached - 1].transform).anchoredPosition, target);
                    if (dl > PATH_TOL) { onPathGrace += Time.deltaTime; if (onPathGrace > 0.12f) brokeOff = true; }
                    else onPathGrace = 0f;
                }
            }
            yield return null;
        }
        bool all = reached >= N;
        _challengeMultiplier = brokeOff ? 0.7f
            : all ? (t <= fastT ? 2.0f : 1.6f)
            : 0.8f + (0.7f / N) * reached;
        Vector2 fxAt = _pieceArea.anchoredPosition + (reached > 0 ? ((RectTransform)nodes[Mathf.Min(reached, N - 1)].transform).anchoredPosition : Vector2.zero);
        yield return MinigameResultFx(_challengeMultiplier, fxAt);
        _challengePrompt.text = brokeOff ? "線から外れた……（威力70%）"
            : all ? (t <= fastT ? "神速の剣筋！（威力200%）" : "なぞりきった！（威力160%）")
            : $"{reached}/{N} で途切れた……（威力 {Mathf.RoundToInt(_challengeMultiplier * 100)}%）";
        yield return new WaitForSeconds(0.7f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // なぞった軌跡（フェードする光の粒）
    IEnumerator TraceStroke(Transform parent, Vector2 pos)
    {
        var g = ProtoUI.CreateGlow("Stroke", parent, pos, new Vector2(20, 20), new Color(0.55f, 0.9f, 1f, 0.9f)); g.raycastTarget = false;
        float t = 0f, dur = 0.6f; Color c = g.color;
        while (t < dur && g != null) { t += Time.deltaTime; float p = t / dur; c.a = 0.9f * (1f - p); g.color = c; yield return null; }
        if (g != null) Destroy(g.gameObject);
    }

    // 剣型パターンを毎回ランダム生成（正規化(-1..1)で作り、プレイエリアに合わせて拡大）
    Vector2[] TracePattern()
    {
        const float RX = 345f, RY = 178f; var C = new Vector2(0, -12f);   // 縦に大きく広げ、中心を少し下げて下の余白も使う
        Vector2 Map(float nx, float ny) => C + new Vector2(nx * RX, ny * RY);
        System.Func<float, Vector2> Pol = deg => { float r = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), Mathf.Sin(r)); };

        int kind = Random.Range(0, 4);
        var list = new System.Collections.Generic.List<Vector2>();
        float rot = Random.Range(0f, 360f);   // 全体を回して毎回違う向きに

        switch (kind)
        {
            case 0: // 五芒星（一筆書きで線が交差する）
            {
                for (int k = 0; k < 5; k++) { int idx = (k * 2) % 5; var d = Pol(rot + 90f + 72f * idx); list.Add(Map(d.x, d.y)); }
                break;
            }
            case 1: // 渦巻き（外→内へ巻き込む）
            {
                int n = 7;
                for (int k = 0; k < n; k++) { float ang = rot + k * 150f; float rr = 1f - 0.1f * k; var d = Pol(ang); list.Add(Map(d.x * rr, d.y * rr)); }
                break;
            }
            case 2: // 稲妻の折り返し（右へ往路→左へ復路。x が反転して交差）
            {
                int top = 4;
                for (int k = 0; k < top; k++) { float x = -1f + 2f * k / (top - 1); float y = (k % 2 == 0 ? 0.95f : 0.2f); list.Add(Map(x, y)); }
                int bot = 3;
                for (int k = 0; k < bot; k++) { float x = 0.6f - 1.6f * k / (bot - 1); float y = (k % 2 == 0 ? -0.25f : -0.95f); list.Add(Map(x, y)); }
                break;
            }
            default: // 砂時計／蝶（×字に交差する対角軌道）
            {
                float[,] hg = { { -1f, 0.9f }, { 1f, 0.9f }, { -1f, -0.9f }, { 1f, -0.9f }, { 0f, 0f }, { 0.8f, 0.3f } };
                for (int k = 0; k < hg.GetLength(0); k++)
                {
                    float x = hg[k, 0], y = hg[k, 1];
                    float rr = rot * Mathf.Deg2Rad;   // 回転行列で全体を回す
                    float rx = x * Mathf.Cos(rr) - y * Mathf.Sin(rr);
                    float ry = x * Mathf.Sin(rr) + y * Mathf.Cos(rr);
                    list.Add(Map(rx * 0.98f, ry * 0.98f));
                }
                break;
            }
        }
        // 画面外に出ないようクランプ（下の余白まで使う）
        for (int i = 0; i < list.Count; i++)
            list[i] = new Vector2(Mathf.Clamp(list[i].x, -350f, 350f), Mathf.Clamp(list[i].y, -195f, 155f));
        return list.ToArray();
    }

    // 点から線分への距離
    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a; float len2 = ab.sqrMagnitude;
        float u = len2 < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * u);
    }

    // マウスのスクリーン座標（新旧Input両対応）
    Vector2 MouseScreenPos()
    {
#if ENABLE_INPUT_SYSTEM
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (ms != null) return ms.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return (Vector2)Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }

    // 連鎖斬：指定カードの全ピースに隣接している「別ピース」の数（重複なし）
    int AdjacentPieceCount(CardDef card)
    {
        var panel = _main.Panel;
        var own = new HashSet<Vector2Int>();
        foreach (var p in panel.Placements) if (p.card != null && p.card.id == card.id) foreach (var c in p.cells) own.Add(c);
        if (own.Count == 0) return 0;
        var seen = new HashSet<PanelModel.Placement>();
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        foreach (var c in own)
            foreach (var d in dirs)
            {
                var np = panel.GetAt(c.x + d.x, c.y + d.y);
                if (np != null && !(np.card != null && np.card.id == card.id)) seen.Add(np);
            }
        return seen.Count;
    }

    // ==================== ミニゲーム：チャージ斬り（長押し） ====================
    // ボタンを押している間ゲージが溜まる。離した瞬間の溜め量で威力。溜めすぎ（100%到達）で暴発＝威力半減
    IEnumerator RunCharge(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　長押しで溜めて、良いところで離せ！（溜めすぎ注意）";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);
        const float W = 640f;
        var barBg = ProtoUI.CreatePanel("CBar", _pieceArea, Vector2.zero, new Vector2(W, 46), new Color(0.12f, 0.12f, 0.2f, 0.98f));
        ProtoUI.CreatePanel("CZone", barBg.transform, new Vector2(W * (0.32f - 0.11f * (MgZone - 1f)), 0), new Vector2(W * 0.22f * MgZone, 46), new Color(0.85f, 0.65f, 0.2f, 0.85f)).raycastTarget = false;   // 会心帯（達人で拡大）
        ProtoUI.CreatePanel("CDanger", barBg.transform, new Vector2(W * 0.46f, 0), new Vector2(W * 0.08f, 46), new Color(0.9f, 0.25f, 0.2f, 0.9f)).raycastTarget = false;   // 暴発帯
        var fill = ProtoUI.CreatePanel("CFill", barBg.transform, new Vector2(-W / 2f, 0), new Vector2(4, 42), new Color(0.4f, 0.9f, 1f));
        fill.raycastTarget = false; var fillRt = (RectTransform)fill.transform; fillRt.pivot = new Vector2(0f, 0.5f);

        // ▽マーカー：会心帯の中心を指し、狙う場所を示す
        float critX = W * (0.32f - 0.11f * (MgZone - 1f));
        var mark = ProtoUI.CreateText("CMark", _pieceArea, "▽", 44, new Vector2(critX, 52f), new Vector2(60, 60), new Color(1f, 0.9f, 0.4f));
        mark.fontStyle = FontStyles.Bold; mark.raycastTarget = false;
        var markLbl = ProtoUI.CreateText("CMarkL", _pieceArea, "ここで離せ", 20, new Vector2(critX, 84f), new Vector2(160, 30), new Color(1f, 0.85f, 0.5f));
        markLbl.raycastTarget = false;
        yield return null;

        // ==== 溜めの魔力オーブ（バーの少し上で膨らむ） ====
        var orbPos = new Vector2(0, 120f);
        var orbHalo = ProtoUI.CreateGlow("COrbHalo", _pieceArea, orbPos, new Vector2(40, 40), new Color(0.4f, 0.8f, 1f, 0.5f)); orbHalo.raycastTarget = false;
        var orb = ProtoUI.CreateGlow("COrb", _pieceArea, orbPos, new Vector2(24, 24), new Color(0.7f, 0.95f, 1f, 0.95f)); orb.raycastTarget = false;
        var orbRt = orb.rectTransform; var haloRt = orbHalo.rectTransform;

        float charge = 0f; bool held = false; float timeout = 4f; bool blew = false;
        float sparkAcc = 0f; float pulseT = 0f; bool warned = false;
        while (timeout > 0f)
        {
            timeout -= Time.deltaTime;
            bool down = MouseHeld();
            if (down) { held = true; charge += Time.deltaTime * 0.5f; }
            if (charge >= 1f) { charge = 1f; blew = true; break; }          // 暴発
            if (held && !down) break;                                        // 離した
            fillRt.sizeDelta = new Vector2(W * charge, 42);

            bool hot = charge > 0.82f;
            fill.color = hot ? new Color(1f, 0.4f, 0.2f) : new Color(0.4f, 0.9f, 1f);
            ProtoUI.SetGauge(_timerFill, Mathf.Clamp01(timeout / 4f), 500f);   // 制限時間バーを更新

            // オーブが溜めに応じて膨張＋鼓動、色は青→白→赤へ
            pulseT += Time.deltaTime * (6f + charge * 14f);
            float pulse = 1f + Mathf.Sin(pulseT) * 0.12f * (0.4f + charge);
            float size = (24f + charge * 120f) * pulse;
            orbRt.sizeDelta = new Vector2(size, size);
            haloRt.sizeDelta = new Vector2(size * 2.1f, size * 2.1f);
            Color oc = hot ? Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.2f, 0.15f), (charge - 0.82f) / 0.18f)
                           : Color.Lerp(new Color(0.6f, 0.9f, 1f), new Color(1f, 0.95f, 0.7f), charge);
            orb.color = oc; orbHalo.color = new Color(oc.r, oc.g, oc.b, 0.45f);

            // 溜めるほど魔力の火花が舞い散る
            if (held)
            {
                sparkAcc += Time.deltaTime;
                if (sparkAcc >= Mathf.Lerp(0.14f, 0.03f, charge))
                {
                    sparkAcc = 0f;
                    float ang = Random.Range(0f, Mathf.PI * 2f); float r = size * 0.9f;
                    StartCoroutine(ChargeSpark(orbPos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r, orbPos, oc));
                }
            }
            // 溜めが強いほど画面が震える
            if (charge > 0.5f && Random.value < (charge - 0.5f) * 0.5f)
                StartCoroutine(ScreenShake(charge * 4f, 0.06f));
            // 暴発直前の警告
            if (hot && !warned) { warned = true; StartCoroutine(ScreenFlash(new Color(1f, 0.3f, 0.1f), 0.2f)); }
            if (!hot) warned = false;
            yield return null;
        }
        fillRt.sizeDelta = new Vector2(W * charge, 42);
        _challengeMultiplier = blew ? 0.5f
            : charge >= 0.82f - 0.18f * MgZone && charge <= 0.82f ? 2f    // 会心帯（達人で下限が広がる）
            : charge >= 0.45f - 0.18f * (MgZone - 1f) ? 1.4f
            : 0.8f + charge * 0.4f;

        // ==== 解放演出 ====
        if (blew)
        {
            orb.color = new Color(1f, 0.3f, 0.15f);
            StartCoroutine(ShockExpand(orbPos, new Color(1f, 0.35f, 0.15f), 2.6f));
            SpawnBurst(orbPos, new Color(1f, 0.4f, 0.2f), 20, 130f);
            StartCoroutine(ScreenShake(16f, 0.4f));
            StartCoroutine(ScreenFlash(new Color(1f, 0.35f, 0.1f), 0.6f));
        }
        else
        {
            var col = _challengeMultiplier >= 2f ? new Color(1f, 0.9f, 0.4f) : new Color(0.5f, 0.9f, 1f);
            int power = _challengeMultiplier >= 2f ? 3 : 1;
            for (int r = 0; r < power; r++) StartCoroutine(ShockExpand(orbPos, col, 1.6f + r * 0.9f));
            SpawnBurst(orbPos, col, _challengeMultiplier >= 2f ? 24 : 12, 140f);
        }
        // オーブ消滅
        StartCoroutine(FadeAway(orb.gameObject, 0.4f));
        StartCoroutine(FadeAway(orbHalo.gameObject, 0.4f));

        yield return MinigameResultFx(_challengeMultiplier, orbPos);
        _challengePrompt.text = blew ? "溜めすぎて暴発！（威力50%）"
            : _challengeMultiplier >= 2f ? "完璧なタメ！会心の一撃！（威力200%）"
            : _challengeMultiplier > 1f ? "良いタメだ！（威力140%）" : "溜めが足りない……";
        yield return new WaitForSeconds(0.7f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // 溜め中に中心へ吸い込まれる火花
    IEnumerator ChargeSpark(Vector2 from, Vector2 to, Color col)
    {
        var g = ProtoUI.CreateGlow("CSpark", _pieceArea, from, new Vector2(14, 14), col); g.raycastTarget = false;
        float t = 0f, dur = 0.35f; var rt = g.rectTransform; Color c = col;
        while (t < dur && g != null)
        {
            t += Time.deltaTime; float p = t / dur;
            rt.anchoredPosition = Vector2.Lerp(from, to, p * p);
            c.a = 1f - p; g.color = c;
            rt.sizeDelta = Vector2.one * (14f * (1f - p * 0.5f));
            yield return null;
        }
        if (g != null) Destroy(g.gameObject);
    }

    // 汎用フェードアウト消滅
    IEnumerator FadeAway(GameObject go, float dur)
    {
        var img = go.GetComponent<Image>(); if (img == null) { Destroy(go); yield break; }
        float t = 0f; Color c = img.color; var rt = (RectTransform)go.transform; Vector2 s0 = rt.sizeDelta;
        while (t < dur && go != null)
        {
            t += Time.deltaTime; float p = t / dur;
            c.a = (1f - p) * 0.95f; img.color = c;
            rt.sizeDelta = s0 * (1f + p * 0.4f);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // ==================== ミニゲーム：デュアルゲージ ====================
    // 2本のゲージを順番に会心ゾーンで止める。両方ジャストで最大倍率
    IEnumerator RunDualGauge(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　2本とも会心ゾーンで止めろ！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);
        const float W = 640f;
        float[] result = new float[2];
        for (int g = 0; g < 2; g++)
        {
            foreach (Transform c in _pieceArea) Destroy(c.gameObject);
            var barBg = ProtoUI.CreatePanel($"DBar{g}", _pieceArea, new Vector2(0, g == 0 ? 40 : -40), new Vector2(W, 40), new Color(0.12f, 0.12f, 0.2f, 0.98f));
            ProtoUI.CreatePanel("DZone", barBg.transform, Vector2.zero, new Vector2(W * 0.16f * MgZone, 40), new Color(0.9f, 0.35f, 0.25f, 0.9f)).raycastTarget = false;
            var cursor = ProtoUI.CreatePanel("DCur", barBg.transform, Vector2.zero, new Vector2(8, 54), Color.white);
            cursor.raycastTarget = false;
            _challengePrompt.text = g == 0 ? "1本目！クリックで止める" : "2本目！クリックで止める";
            yield return null;
            float t = 0f; float speed = 1.7f + g * 0.4f; float timeout = 4f; float pos = 0f; bool stopped = false;
            while (timeout > 0f)
            {
                t += Time.deltaTime * speed; pos = Mathf.PingPong(t, 1f);
                ((RectTransform)cursor.transform).anchoredPosition = new Vector2((pos - 0.5f) * W, 0);
                timeout -= Time.deltaTime;
                if (ClickedThisFrame()) { stopped = true; break; }
                yield return null;
            }
            float dist = Mathf.Abs(pos - 0.5f);
            result[g] = !stopped ? 0.7f : dist <= 0.08f * MgZone ? 1.4f : dist <= 0.18f * MgZone ? 1.1f : 0.8f;
            yield return new WaitForSeconds(0.2f);
        }
        _challengeMultiplier = result[0] * result[1];   // 両方1.4→約1.96倍
        _challengePrompt.text = $"合成倍率 → 威力 {Mathf.RoundToInt(_challengeMultiplier * 100)}%";
        yield return new WaitForSeconds(0.9f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // ==================== ミニゲーム：カウントダウン（タイミング斬り） ====================
    // 3・2・1のリズムに乗って、縮んでくるひし形が枠にピッタリ重なった瞬間にクリック
    IEnumerator RunCountdown(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　リズムに乗って、枠に重なった瞬間クリック！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        const float beat = 0.7f;
        const float targetSize = 110f;
        var center = new Vector2(0, 10f);

        // 目標の枠（固定のひし形の輪郭）
        var frame = ProtoUI.CreatePanel("CDFrame", _pieceArea, center, new Vector2(targetSize, targetSize), new Color(0.45f, 0.95f, 1f, 0.95f));
        frame.raycastTarget = false; frame.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var hole = ProtoUI.CreatePanel("CDHole", frame.transform, Vector2.zero, new Vector2(targetSize - 14f, targetSize - 14f), new Color(0.04f, 0.06f, 0.10f, 0.75f));
        hole.raycastTarget = false;
        // カウント数字（枠の上）
        var big = ProtoUI.CreateText("CDNum", _pieceArea, "", 120, new Vector2(0, 150f), new Vector2(400, 200), Color.white);
        big.fontStyle = FontStyles.Bold; big.raycastTarget = false;
        // 縮んでくるひし形（beat*3かけて targetSize まで縮む＝1の直後にピッタリ重なる）
        float startSize = targetSize * 4.2f;
        var incoming = ProtoUI.CreatePanel("CDIn", _pieceArea, center, new Vector2(startSize, startSize), new Color(1f, 0.55f, 0.2f, 0.5f));
        incoming.raycastTarget = false; incoming.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var inRt = (RectTransform)incoming.transform;
        yield return null;

        const float total = beat * 3f;   // 3→2→1 のリズムでちょうど重なる
        float t = 0f; bool clicked = false; float clickErr = 999f; int lastBeat = -1;
        // 少し行き過ぎても取れるよう total*1.35 まで待つ
        while (t < total * 1.35f)
        {
            t += Time.deltaTime;
            float p = t / total;                    // 1.0 で枠にピッタリ
            float size = Mathf.Lerp(startSize, targetSize, Mathf.Clamp01(p));
            inRt.sizeDelta = new Vector2(size, size);
            // ジャストに近いほど明るく
            float near = 1f - Mathf.Clamp01(Mathf.Abs(size - targetSize) / (targetSize * 1.5f));
            incoming.color = Color.Lerp(new Color(1f, 0.55f, 0.2f, 0.45f), new Color(1f, 0.95f, 0.5f, 0.9f), near);

            // 拍ごとに数字と鼓動
            int b = Mathf.Clamp(3 - Mathf.FloorToInt(t / beat), 0, 3);
            if (b != lastBeat && b >= 1 && t < total)
            {
                lastBeat = b;
                big.text = b.ToString(); big.color = Color.white; big.rectTransform.localScale = Vector3.one * 1.4f;
                if (_sfx != null && _heartbeatClip != null) _sfx.PlayOneShot(_heartbeatClip, 0.9f);
            }
            big.rectTransform.localScale = Vector3.Lerp(big.rectTransform.localScale, Vector3.one, Time.deltaTime * 8f);
            ProtoUI.SetGauge(_timerFill, Mathf.Clamp01(1f - t / (total * 1.35f)), 500f);

            if (ClickedThisFrame()) { clicked = true; clickErr = Mathf.Abs(t - total); break; }
            yield return null;
        }

        _challengeMultiplier = !clicked ? 0.7f
            : clickErr <= 0.07f * MgZone ? 2.2f
            : clickErr <= 0.18f * MgZone ? 1.4f : 0.9f;
        big.text = _challengeMultiplier >= 2f ? "斬！！" : _challengeMultiplier > 1f ? "斬！" : "外し";
        big.color = _challengeMultiplier > 1f ? new Color(1f, 0.85f, 0.4f) : new Color(0.7f, 0.7f, 0.8f);
        if (_challengeMultiplier > 1f)
        {
            StartCoroutine(ShockExpand(_pieceArea.anchoredPosition + center, new Color(1f, 0.9f, 0.4f), _challengeMultiplier >= 2f ? 2.2f : 1.5f));
            SpawnBurst(_pieceArea.anchoredPosition + center, new Color(1f, 0.9f, 0.4f), _challengeMultiplier >= 2f ? 18 : 10, 120f);
        }
        yield return MinigameResultFx(_challengeMultiplier, _pieceArea.anchoredPosition + center);
        _challengePrompt.text = _challengeMultiplier >= 2f ? "ジャスト！（威力220%）"
            : _challengeMultiplier > 1f ? "惜しい！（威力140%）" : "タイミングを外した…";
        yield return new WaitForSeconds(0.7f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // マウス左ボタンを押している最中か（チャージ用）
    bool MouseHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (ms != null && ms.leftButton.isPressed) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(0)) return true;
#endif
        return false;
    }

    // ==================== パリィ（敵の攻撃をタイミングよく弾く） ====================
    // 目標の枠に、縮んでくる大きなひし形がピッタリ重なった瞬間にクリック。ジャスト=-75% / 惜しい=-30% / 外し=0%
    IEnumerator RunParry()
    {
        _parryCutPct = 0;
        Time.timeScale = 1f;
        _message.text = "パリィ！　枠にひし形がピッタリ重なった瞬間にクリック！";
        var pos = new Vector2(0f, 40f);
        const float baseSize = 96f;

        // 目標の枠（ひし形の輪郭）：外枠シアン＋内側を暗くして「枠」に見せる
        var frame = ProtoUI.CreatePanel("ParryFrame", _root, pos, new Vector2(baseSize, baseSize), new Color(0.45f, 0.95f, 1f, 0.95f));
        frame.raycastTarget = false; frame.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var hole = ProtoUI.CreatePanel("ParryHole", frame.transform, Vector2.zero, new Vector2(baseSize - 16f, baseSize - 16f), new Color(0.04f, 0.06f, 0.10f, 0.7f));
        hole.raycastTarget = false;

        // 上から縮んでくる大きなひし形（半透明で枠が透ける）
        var incoming = ProtoUI.CreatePanel("ParryIn", _root, pos, new Vector2(baseSize, baseSize), new Color(1f, 1f, 1f, 0.45f));
        incoming.raycastTarget = false; incoming.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var inRt = (RectTransform)incoming.transform;
        yield return null;

        const float dur = 1.15f;
        float t = 0f; bool clicked = false; float clickScale = 3f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            float sc = Mathf.Lerp(3f, 0.55f, p);   // 大→小
            inRt.localScale = Vector3.one * sc;
            bool near = Mathf.Abs(sc - 1f) <= 0.28f;   // 重なりが近いと緑に光って合図
            incoming.color = near ? new Color(0.4f, 1f, 0.5f, 0.85f) : new Color(1f, 1f, 1f, 0.45f);
            if (ClickedThisFrame()) { clicked = true; clickScale = sc; break; }
            yield return null;
        }
        float err = Mathf.Abs(clickScale - 1f);
        if (clicked && err <= 0.12f)
        {
            _parryCutPct = 75;
            _message.text = "ジャストパリィ！（被ダメ-75%）";
            if (_parryClip != null) _sfx.PlayOneShot(_parryClip);
            StartCoroutine(ScreenFlash(new Color(0.5f, 0.9f, 1f), 0.3f));
        }
        else if (clicked && err <= 0.28f)
        {
            _parryCutPct = 30;
            _message.text = "パリィ！（被ダメ-30%）";
            if (_parryClip != null) _sfx.PlayOneShot(_parryClip, 0.6f);
        }
        else _message.text = clicked ? "タイミングが合わなかった……" : "パリィ失敗……";
        yield return new WaitForSecondsRealtime(0.5f);
        Destroy(frame.gameObject); Destroy(incoming.gameObject);
        Time.timeScale = _main.GameSpeed;
    }

    // ==================== ミニゲーム：ゲージストップ ====================
    // 達人のペンダント：ミニゲームの成功判定ゾーンが1.5倍に広がる
    float MgZone => _main.Equipped == EquipKind.MasterPendant ? 1.5f : 1f;

    // 高速で往復するカーソルを会心ゾーンで止める。ど真ん中=2倍 / ゾーン内=1.4倍 / 外=0.9倍
    IEnumerator RunGauge(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;   // ミニゲーム中は演出速度に関係なく等速（反射神経ゲーのため）
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　会心ゾーンで止めろ！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        const float W = 640f;
        // 台座＋金縁のバー（高級感）
        ProtoUI.CreateGlow("GBarHalo", _pieceArea, Vector2.zero, new Vector2(W + 90f, 120f), new Color(0.5f, 0.42f, 0.2f, 0.25f)).raycastTarget = false;
        ProtoUI.CreatePanel("GBarFrame", _pieceArea, Vector2.zero, new Vector2(W + 20f, 62f), new Color(0.82f, 0.68f, 0.34f, 0.95f)).raycastTarget = false;
        var barBg = ProtoUI.CreatePanel("GBar", _pieceArea, new Vector2(0, 0), new Vector2(W, 46), new Color(0.08f, 0.09f, 0.15f, 1f));
        // ゾーン（発光つき）。会心ゾーンは脈動させて狙いを誘う
        var zone = ProtoUI.CreatePanel("GZone", barBg.transform, new Vector2(0, 0), new Vector2(W * 0.26f * MgZone, 46), new Color(0.9f, 0.7f, 0.25f, 0.9f)); zone.raycastTarget = false;
        var critGlow = ProtoUI.CreateGlow("GCritGlow", barBg.transform, Vector2.zero, new Vector2(W * 0.2f * MgZone, 90f), new Color(1f, 0.35f, 0.25f, 0.8f)); critGlow.raycastTarget = false;
        var crit = ProtoUI.CreatePanel("GCrit", barBg.transform, new Vector2(0, 0), new Vector2(W * 0.08f * MgZone, 46), new Color(1f, 0.35f, 0.28f, 1f)); crit.raycastTarget = false;
        // 発光するカーソル（芯＋ハロー）
        var curGlow = ProtoUI.CreateGlow("GCurGlow", barBg.transform, Vector2.zero, new Vector2(46, 90), new Color(0.7f, 0.95f, 1f, 0.9f)); curGlow.raycastTarget = false;
        var cursor = ProtoUI.CreatePanel("GCur", barBg.transform, Vector2.zero, new Vector2(7, 66), Color.white);
        cursor.raycastTarget = false;

        yield return null; // 開いた瞬間のクリックを無視

        float t = 0f; const float speed = 1.6f; float timeout = 5f; float pos = 0f; bool stopped = false; float trailAcc = 0f;
        while (timeout > 0f)
        {
            t += Time.deltaTime * speed;
            pos = Mathf.PingPong(t, 1f);                       // 0..1
            float cx = (pos - 0.5f) * W;
            ((RectTransform)cursor.transform).anchoredPosition = new Vector2(cx, 0);
            ((RectTransform)curGlow.transform).anchoredPosition = new Vector2(cx, 0);
            // 会心ゾーンの脈動
            float pulse = 1f + Mathf.Sin(Time.time * 10f) * 0.15f;
            ((RectTransform)critGlow.transform).localScale = new Vector3(pulse, pulse, 1f);
            var cc = new Color(1f, 0.35f, 0.25f, 0.55f + 0.3f * Mathf.Sin(Time.time * 10f)); critGlow.color = cc;
            // カーソルの残像（軌跡）
            trailAcc += Time.deltaTime;
            if (trailAcc >= 0.02f) { trailAcc = 0f; StartCoroutine(CursorTrail(barBg.transform, new Vector2(cx, 0))); }
            timeout -= Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, timeout / 5f, 500f);
            if (ClickedThisFrame()) { stopped = true; break; }
            yield return null;
        }

        float dist = Mathf.Abs(pos - 0.5f); // 中心からの距離（0〜0.5）
        _challengeMultiplier = !stopped ? 0.9f : dist <= 0.04f * MgZone ? 2f : dist <= 0.13f * MgZone ? 1.4f : 0.9f;
        // 止めた瞬間の演出
        Vector2 hitPos = _pieceArea.anchoredPosition + new Vector2((pos - 0.5f) * W, 0);
        yield return MinigameResultFx(_challengeMultiplier, hitPos);
        _challengePrompt.text = _challengeMultiplier >= 2f ? "ジャスト！ 会心の一撃！（威力200%）"
            : _challengeMultiplier > 1f ? "いい感じ！（威力140%）" : "うーん、外した…（威力90%）";
        yield return new WaitForSeconds(0.7f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // カーソルの淡い残像
    IEnumerator CursorTrail(Transform parent, Vector2 pos)
    {
        var g = ProtoUI.CreateGlow("Trail", parent, pos, new Vector2(30, 70), new Color(0.6f, 0.9f, 1f, 0.5f)); g.raycastTarget = false;
        float t = 0f, dur = 0.22f; Color c = g.color;
        while (t < dur && g != null) { t += Time.deltaTime; float p = t / dur; c.a = 0.5f * (1f - p); g.color = c; yield return null; }
        if (g != null) Destroy(g.gameObject);
    }

    // ミニゲーム成功/失敗の共通フィードバック（止めた位置で炸裂）
    IEnumerator MinigameResultFx(float mult, Vector2 pos)
    {
        bool crit = mult >= 2f, good = mult > 1f;
        Color col = crit ? new Color(1f, 0.5f, 0.2f) : good ? new Color(1f, 0.85f, 0.35f) : new Color(0.6f, 0.65f, 0.8f);
        if (crit || good)
        {
            _sfx?.PlayOneShot(_parryClip, crit ? 1f : 0.7f);
            StartCoroutine(ScreenFlash(col, crit ? 0.35f : 0.2f));
            if (crit) StartCoroutine(ScreenShake(16f, 0.3f));
            StartCoroutine(ShockExpand(pos, Color.Lerp(col, Color.white, 0.6f), crit ? 2.2f : 1.4f));
            SpawnBurst(pos, col, crit ? 40 : 22, crit ? 200f : 130f);
            if (crit) { SpawnBurst(pos, Color.white, 20, 120f); StartCoroutine(TextPopup(pos + new Vector2(0, 70f), "PERFECT!", col, 46)); }
            yield return HitStop(crit ? 0.1f : 0.05f);
        }
        else
        {
            _sfx?.PlayOneShot(_swingClip, 0.5f);
            SpawnBurst(pos, col, 8, 70f);
        }
    }

    // ==================== ミニゲーム：数字順タップ ====================
    // ピースのマスに数字が表示される。小さい順にタップ！ 正答率で倍率
    IEnumerator RunTapOrder(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;   // ミニゲーム中は演出速度に関係なく等速（反射神経ゲーのため）
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　数字を小さい順にタップ！";
        ProtoUI.SetGauge(_timerFill, 1f, 500f);

        var shape = card.Shape;
        float cs = 64f, gap = 6f;
        int minX = shape.Min(v => v.x), minY = shape.Min(v => v.y), maxX = shape.Max(v => v.x), maxY = shape.Max(v => v.y);
        float ox = -(maxX - minX) * (cs + gap) / 2f, oy = (maxY - minY) * (cs + gap) / 2f;

        int n = shape.Length;
        var order = Enumerable.Range(1, n).OrderBy(_ => Random.value).ToList(); // 各マスに割り当てる数字
        int expected = 1, correct = 0, wrong = 0; bool open = true;

        for (int i = 0; i < n; i++)
        {
            var v = shape[i];
            int num = order[i];
            Color bc = Color.Lerp(card.CategoryColor, Color.black, 0.4f);
            var img = ProtoUI.CreatePanel("TCell", _pieceArea,
                new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), bc);
            var label = ProtoUI.CreateText("TNum", img.transform, num.ToString(), 32, Vector2.zero, new Vector2(cs, cs), Color.white);
            label.fontStyle = FontStyles.Bold; label.raycastTarget = false;
            var btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (!open || !btn.interactable) return;
                if (num == expected)
                {
                    expected++; correct++;
                    img.color = new Color(0.3f, 0.7f, 0.35f, 0.9f); btn.interactable = false;
                }
                else { wrong++; StartCoroutine(TapFeedback(img, bc)); }
            });
        }

        float total = 3f + n * 0.8f, remain = total;
        while (remain > 0f && correct < n)
        {
            remain -= Time.deltaTime;
            ProtoUI.SetGauge(_timerFill, remain / total, 500f);
            yield return null;
        }
        open = false;

        float ratio = Mathf.Clamp01((float)correct / n - wrong * 0.1f);
        _challengeMultiplier = ratio <= 0.8f ? (ratio / 0.8f) : (1f + (ratio - 0.8f) / 0.2f * 0.5f); // 全問正解で1.5倍
        _challengePrompt.text = $"正答率 {Mathf.RoundToInt(ratio * 100)}% → 威力 {Mathf.RoundToInt(_challengeMultiplier * 100)}%";
        yield return new WaitForSeconds(1.0f);
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // ==================== ミニゲーム：スロット ====================
    // 3つのリールをクリックで順に止める。絵柄は固定順で回るので「目押し」ができる。
    // 7×3=2.5倍 / 絵柄3つ=1.8倍 / 2つ=1.2倍 / バラバラ=0.8倍
    static readonly string[] SlotSymbols = { "７", "♦", "♥", "★" };
    static readonly Color[] SlotSymbolColors = {
        new Color(0.95f, 0.2f, 0.2f),    // ７＝赤
        new Color(0.2f, 0.55f, 1f),      // ♦＝青
        new Color(0.9f, 0.25f, 0.45f),   // ♥＝ピンク
        new Color(1f, 0.8f, 0.2f),       // ★＝金
    };
    IEnumerator RunSlot(CardDef card)
    {
        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;   // ミニゲーム中は演出速度に関係なく等速（反射神経ゲーのため）
        foreach (Transform c in _pieceArea) Destroy(c.gameObject);
        _challengePrompt.text = $"「{card.displayName}」発動！　クリックでリールを止めろ！";
        if (_timerFill != null) _timerFill.transform.parent.gameObject.SetActive(false);   // スロットは時間制限なし＝ゲージを隠す

        // ==== 実機風の筐体 ====
        ProtoUI.CreateGlow("SlotHalo", _pieceArea, new Vector2(0, 0), new Vector2(620, 380), new Color(1f, 0.5f, 0.2f, 0.25f)).raycastTarget = false;
        ProtoUI.CreatePanel("SlotCabOuter", _pieceArea, new Vector2(0, 0), new Vector2(540, 300), new Color(0.85f, 0.68f, 0.28f, 1f)).raycastTarget = false;    // 金の外枠
        ProtoUI.CreatePanel("SlotCabRed", _pieceArea, new Vector2(0, -6), new Vector2(516, 276), new Color(0.62f, 0.12f, 0.14f, 1f)).raycastTarget = false;       // 赤いキャビネット
        // 上部マーキー
        var marquee = ProtoUI.CreatePanel("SlotMarquee", _pieceArea, new Vector2(0, 116), new Vector2(300, 44), new Color(0.15f, 0.05f, 0.06f, 1f)); marquee.raycastTarget = false;
        ProtoUI.CreatePanel("SlotMarqueeFr", _pieceArea, new Vector2(0, 116), new Vector2(310, 52), new Color(0.9f, 0.75f, 0.35f, 1f)).transform.SetAsFirstSibling();
        var mt = ProtoUI.CreateText("SlotTitle", marquee.transform, "★ JACKPOT ★", 24, Vector2.zero, new Vector2(300, 44), new Color(1f, 0.9f, 0.4f)); mt.fontStyle = FontStyles.Bold;
        // リール背景（黒）＋ペイライン
        ProtoUI.CreatePanel("SlotReelBg", _pieceArea, new Vector2(0, -14), new Vector2(470, 168), new Color(0.04f, 0.04f, 0.06f, 1f)).raycastTarget = false;
        // ペイライン（中央の横ライン＋左右の矢印）
        ProtoUI.CreatePanel("SlotPayline", _pieceArea, new Vector2(0, -14), new Vector2(470, 4), new Color(1f, 0.85f, 0.3f, 0.85f)).raycastTarget = false;
        var payL = ProtoUI.CreatePanel("SlotPayL", _pieceArea, new Vector2(-250, -14), new Vector2(20, 20), new Color(1f, 0.5f, 0.2f)); payL.raycastTarget = false; payL.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var payR = ProtoUI.CreatePanel("SlotPayR", _pieceArea, new Vector2(250, -14), new Vector2(20, 20), new Color(1f, 0.5f, 0.2f)); payR.raycastTarget = false; payR.transform.localRotation = Quaternion.Euler(0, 0, 45);

        // リールごとの独立したSTOPボタン（各リール真下。押したリールだけが止まる）
        for (int i = 0; i < 3; i++) _slotStopReq[i] = false;
        var stopBtns = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            float bx = (i - 1) * 150f;
            ProtoUI.CreatePanel($"SlotStopRing{i}", _pieceArea, new Vector2(bx, -122), new Vector2(120, 56), new Color(0.9f, 0.75f, 0.35f, 1f)).raycastTarget = false;
            var sb = ProtoUI.CreateButton($"SlotStop{i}", _pieceArea, "STOP", 22, new Vector2(bx, -122), new Vector2(110, 46),
                new Color(0.85f, 0.15f, 0.15f, 1f), () => _slotStopReq[idx] = true);
            var sl = sb.GetComponentInChildren<TextMeshProUGUI>();
            if (sl != null) sl.fontStyle = FontStyles.Bold;
            stopBtns[i] = sb;
        }

        // ==== 縦に流れる本物風リール ====
        // 各リールは縦に並んだシンボル帯（cell）で構成し、上から下へスクロールする。
        // 窓（box）にRectMask2Dを付けてはみ出しをクリップ＝リールが回っているように見える。
        const int CELLS = 5;          // 窓に収まる縦セル数（中央±2）
        const float CELL_H = 84f;     // セルの縦間隔
        var boxes = new Image[3];
        var cellRt = new RectTransform[3][];
        var cellLbl = new TextMeshProUGUI[3][];
        var cellSym = new int[3][];   // 各セルが表示しているシンボル
        var topSym = new int[3];      // 一番上のセルの次に流し込むシンボル
        var result = new int[3];
        var resultLbl = new TextMeshProUGUI[3];   // 停止後、中央にあるラベル（演出用）
        for (int i = 0; i < 3; i++)
        {
            ProtoUI.CreatePanel($"ReelFr{i}", _pieceArea, new Vector2((i - 1) * 150f, -14), new Vector2(140, 160), new Color(0.7f, 0.72f, 0.78f, 1f)).raycastTarget = false;   // リール窓の銀縁
            var box = ProtoUI.CreatePanel($"Reel{i}", _pieceArea, new Vector2((i - 1) * 150f, -14), new Vector2(128, 150), new Color(0.96f, 0.96f, 0.98f, 1f));   // 白いリール面
            box.raycastTarget = false;
            box.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();   // 窓の外をクリップ

            cellRt[i] = new RectTransform[CELLS];
            cellLbl[i] = new TextMeshProUGUI[CELLS];
            cellSym[i] = new int[CELLS];
            int start = Random.Range(0, SlotSymbols.Length);
            for (int j = 0; j < CELLS; j++)
            {
                // j=0 が一番上、j=CELLS-1 が一番下。中央(j=2)がペイライン
                int sym = ((start + j) % SlotSymbols.Length + SlotSymbols.Length) % SlotSymbols.Length;
                float y = (CELLS / 2 - j) * CELL_H;   // 168,84,0,-84,-168
                var lbl = ProtoUI.CreateText($"RS{i}_{j}", box.transform, SlotSymbols[sym], 78, new Vector2(0, y), new Vector2(128, CELL_H), SlotSymbolColors[sym]);
                lbl.fontStyle = FontStyles.Bold; lbl.raycastTarget = false;
                cellRt[i][j] = lbl.rectTransform; cellLbl[i][j] = lbl; cellSym[i][j] = sym;
            }
            topSym[i] = ((start - 1) % SlotSymbols.Length + SlotSymbols.Length) % SlotSymbols.Length;

            // 上下の陰影を最前面に（回転する円筒っぽい暗がり）
            ProtoUI.CreatePanel("RShTop", box.transform, new Vector2(0, 60), new Vector2(128, 34), new Color(0f, 0f, 0f, 0.4f)).raycastTarget = false;
            ProtoUI.CreatePanel("RShBot", box.transform, new Vector2(0, -60), new Vector2(128, 34), new Color(0f, 0f, 0f, 0.4f)).raycastTarget = false;
            boxes[i] = box;
        }

        yield return null;

        _challengePrompt.text = "各リールのSTOPボタンで、狙って止めろ！";

        // 3リール同時回転。上から下へシンボルが流れ、押したリールだけが独立して止まる（目押し）
        const float bottomLimit = -(CELLS / 2) * CELL_H - CELL_H / 2f;   // これを下回ったセルは上へ再利用
        var spinning = new bool[3] { true, true, true };
        var speed = new float[3];
        for (int i = 0; i < 3; i++) speed[i] = (255f + i * 35f) / MgZone;   // px/秒。CELL_H=84なので約3絵柄/秒＝目押しできる速さ（達人でさらにゆっくり）

        int stoppedCount = 0; bool reachAnnounced = false;
        while (stoppedCount < 3)   // 時間制限なし。全リールをSTOPで止めるまで回り続ける
        {
            for (int i = 0; i < 3; i++)
            {
                if (!spinning[i]) continue;
                float dy = speed[i] * Time.deltaTime;
                for (int j = 0; j < CELLS; j++)
                {
                    var rt = cellRt[i][j];
                    float y = rt.anchoredPosition.y - dy;
                    if (y < bottomLimit)
                    {
                        y += CELLS * CELL_H;                       // 一番上へ回す
                        int sym = topSym[i];
                        topSym[i] = ((topSym[i] - 1) % SlotSymbols.Length + SlotSymbols.Length) % SlotSymbols.Length;
                        cellSym[i][j] = sym;
                        cellLbl[i][j].text = SlotSymbols[sym]; cellLbl[i][j].color = SlotSymbolColors[sym];
                    }
                    rt.anchoredPosition = new Vector2(0, y);
                }

                // このリールのSTOP要求（そのボタンだけが自分を止める）
                if (_slotStopReq[i])
                {
                    _slotStopReq[i] = false;
                    spinning[i] = false; stoppedCount++;
                    // 中央(y=0)に一番近いセルを結果に採用し、そこへピタッと吸着
                    int best = 0; float bestAbs = 9999f;
                    for (int j = 0; j < CELLS; j++)
                    {
                        float ay = Mathf.Abs(cellRt[i][j].anchoredPosition.y);
                        if (ay < bestAbs) { bestAbs = ay; best = j; }
                    }
                    result[i] = cellSym[i][best]; resultLbl[i] = cellLbl[i][best];
                    StartCoroutine(SlotSnap(cellRt[i], cellRt[i][best].anchoredPosition.y));
                    stopBtns[i].interactable = false;
                    if (stopBtns[i].targetGraphic is Image bi) bi.color = new Color(0.35f, 0.2f, 0.2f, 1f);
                    StartCoroutine(Pulse(boxes[i].rectTransform, 1.1f, 0.16f));
                    if (_sfx != null && _swingClip != null) _sfx.PlayOneShot(_swingClip, 0.5f);
                }
            }
            // ---- リーチ演出：2つ止まって揃っていて、残り1つが回転中 ----
            if (!reachAnnounced && stoppedCount == 2)
            {
                int a = -1, b = -1;
                for (int i = 0; i < 3; i++) { if (!spinning[i]) { if (a < 0) a = i; else b = i; } }
                if (a >= 0 && b >= 0 && result[a] == result[b])
                {
                    reachAnnounced = true;
                    if (_sfx != null && _reachClip != null) _sfx.PlayOneShot(_reachClip);
                    _challengePrompt.text = result[a] == 0 ? "７が2つ…！ 大チャンス！！" : "リーチ！！ 残り1つを狙え！";
                    StartCoroutine(ScreenFlash(new Color(1f, 0.25f, 0.2f), 0.25f));
                    StartCoroutine(TextPopup(new Vector2(0, 210f), "リーチ！！", new Color(1f, 0.35f, 0.25f), 58));
                    for (int i = 0; i < 3; i++) if (spinning[i]) speed[i] = 175f / MgZone;   // 残りをスローにして狙わせる
                }
            }
            yield return null;
        }

        bool all = result[0] == result[1] && result[1] == result[2];
        bool pair = result[0] == result[1] || result[1] == result[2] || result[0] == result[2];
        _challengeMultiplier = all && result[0] == 0 ? 2.5f : all ? 1.8f : pair ? 1.2f : 0.8f;
        _challengePrompt.text = all && result[0] == 0 ? "７７７！ 大当たり！（威力250%）"
            : all ? "絵柄が揃った！（威力180%）"
            : pair ? "惜しい！2つ揃い（威力120%）" : "揃わず…（威力80%）";

        // 全部揃ったらジャックポット演出
        if (all) yield return SlotJackpotFx(resultLbl, boxes, result[0] == 0);
        else yield return new WaitForSeconds(1.1f);
        if (_timerFill != null) _timerFill.transform.parent.gameObject.SetActive(true);   // 隠したゲージを他ミニゲーム用に戻す
        Time.timeScale = _main.GameSpeed;
        _challengeRoot.gameObject.SetActive(false);
    }

    // 停止時：中央に一番近いセルがちょうど y=0 になるよう、全セルを少し戻して吸着（本物の「ガコン」）
    IEnumerator SlotSnap(RectTransform[] cells, float centerY)
    {
        float delta = -centerY;               // これだけ全体をずらすと中央セルが 0 に来る
        var from = new float[cells.Length];
        for (int j = 0; j < cells.Length; j++) from[j] = cells[j].anchoredPosition.y;
        float t = 0f, dur = 0.14f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);   // ease-out
            for (int j = 0; j < cells.Length; j++)
                if (cells[j] != null) cells[j].anchoredPosition = new Vector2(0, from[j] + delta * p);
            yield return null;
        }
        for (int j = 0; j < cells.Length; j++)
            if (cells[j] != null) cells[j].anchoredPosition = new Vector2(0, from[j] + delta);
    }

    // ジャックポット演出：一拍タメ→白フラッシュ→昇天ファンファーレ＋コインの雨。777は虹色の祝祭
    IEnumerator SlotJackpotFx(TextMeshProUGUI[] labels, Image[] boxes, bool is777)
    {
        Color gold = new Color(1f, 0.85f, 0.25f);
        Color goldW = new Color(1f, 0.97f, 0.75f);

        // ①タメの静寂（一拍おいてから炸裂＝脳汁の基本）
        yield return new WaitForSeconds(0.35f);

        // ②炸裂：白フラッシュ→ファンファーレ＋コインシャワー音＋シェイク
        StartCoroutine(ScreenFlash(Color.white, is777 ? 0.85f : 0.55f));
        if (_sfx != null && _fanfareClip != null) _sfx.PlayOneShot(_fanfareClip);
        if (_sfx != null && _coinShowerClip != null) _sfx.PlayOneShot(_coinShowerClip);
        StartCoroutine(ScreenShake(is777 ? 26f : 14f, is777 ? 0.7f : 0.4f));
        StartCoroutine(TextPopup(new Vector2(0, 190f), is777 ? "★ JACKPOT!! ★" : "大当たり！", gold, is777 ? 72 : 50));
        StartCoroutine(CoinRain(is777 ? 46 : 22, is777 ? 2.4f : 1.4f));

        float t = 0f, dur = is777 ? 2.6f : 1.5f;
        int wave = 0; float shower = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            for (int i = 0; i < 3; i++)
            {
                float ph = t * 10f - i * 1.2f;
                // 777は虹色に回る（パチンコの虹＝最高予告）
                Color hi = is777 ? Color.HSVToRGB((t * 0.8f + i * 0.15f) % 1f, 0.75f, 1f) : gold;
                labels[i].color = Color.Lerp(hi, Color.white, Mathf.Sin(ph) * 0.5f + 0.5f);
                labels[i].rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, Mathf.Sin(ph)) * (is777 ? 0.4f : 0.25f));
                if (boxes[i] != null) boxes[i].color = Color.Lerp(new Color(0.1f, 0.1f, 0.18f, 0.98f), new Color(hi.r * 0.5f, hi.g * 0.4f, hi.b * 0.15f, 0.98f), Mathf.Sin(ph) * 0.5f + 0.5f);
            }
            // 光の吹き出し（波状。リール位置＝画面中央やや上）
            if (t * 6f > wave)
            {
                wave++;
                for (int i = 0; i < 3; i++)
                    SpawnBurst(new Vector2((i - 1) * 150f, 30f), wave % 2 == 0 ? gold : goldW, is777 ? 6 : 3, 100f + wave * 10f);
                if (is777 && wave % 2 == 0) StartCoroutine(ShockExpand(new Vector2(0, 30f), Color.HSVToRGB((wave * 0.13f) % 1f, 0.7f, 1f), 0.9f + wave * 0.12f));
            }
            // 追いコイン音（チャリンチャリンが続く）
            shower += Time.deltaTime;
            if (shower >= 0.5f && is777)
            {
                shower = 0f;
                if (_sfx != null && _coinClip != null) _sfx.PlayOneShot(_coinClip, 0.7f);
            }
            yield return null;
        }
        for (int i = 0; i < 3; i++) { labels[i].color = ProtoUI.Gold; labels[i].rectTransform.localScale = Vector3.one; }
    }

    // コインの雨：金色のコインが画面上から回転しながら降り注ぐ
    IEnumerator CoinRain(int count, float dur)
    {
        for (int i = 0; i < count; i++)
        {
            StartCoroutine(CoinFall(new Vector2(Random.Range(-620f, 620f), 480f)));
            yield return new WaitForSeconds(dur / count);
        }
    }
    IEnumerator CoinFall(Vector2 from)
    {
        var coin = ProtoUI.CreatePanel("Coin", _root, from, new Vector2(22, 22), new Color(1f, 0.84f, 0.2f));
        coin.raycastTarget = false;
        var rim = ProtoUI.CreatePanel("CoinRim", coin.transform, Vector2.zero, new Vector2(14, 14), new Color(1f, 0.95f, 0.6f));
        rim.raycastTarget = false;
        var rt = (RectTransform)coin.transform;
        float vy = Random.Range(520f, 760f), vx = Random.Range(-60f, 60f), rot = Random.Range(240f, 520f);
        float t = 0f, life = 1.9f;
        while (t < life && rt != null)
        {
            t += Time.deltaTime;
            rt.anchoredPosition += new Vector2(vx, -vy) * Time.deltaTime;
            rt.Rotate(0, 0, rot * Time.deltaTime);
            // くるくる回って見えるよう横幅を揺らす
            rt.localScale = new Vector3(Mathf.Abs(Mathf.Sin(t * 9f)) * 0.7f + 0.3f, 1f, 1f);
            if (rt.anchoredPosition.y < -500f) break;
            yield return null;
        }
        if (coin != null) Destroy(coin.gameObject);
    }

    IEnumerator RunChallenge(CardDef card)
    {
        // blinkTimeScale は「速度倍率」。2なら2倍速＝点灯時間・間隔は半分。
        float scale = _main.Cfg != null && _main.Cfg.blinkTimeScale > 0.01f ? _main.Cfg.blinkTimeScale : 2f;
        float onTime = BaseFlashOn / scale, gapTime = BaseFlashGap / scale;

        _challengeRoot.gameObject.SetActive(true);
        Time.timeScale = 1f;   // ミニゲーム中は演出速度に関係なく等速（反射神経ゲーのため）
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
        var tapLabels = new List<TextMeshProUGUI>();
        bool inputOpen = false;

        for (int i = 0; i < shape.Length; i++)
        {
            var v = shape[i];
            Color bc = Color.Lerp(card.CategoryColor, Color.black, 0.45f);
            var img = ProtoUI.CreatePanel("PCell", _pieceArea,
                new Vector2(ox + (v.x - minX) * (cs + gap), oy - (v.y - minY) * (cs + gap)), new Vector2(cs, cs), bc);
            cells.Add(img); baseColors.Add(bc);
            // タップ順を表示する丸数字（既定は非表示）
            float dia = cs * 0.66f;
            var circ = ProtoUI.CreateRect("PNumCirc", img.transform);
            circ.anchoredPosition = Vector2.zero; circ.sizeDelta = new Vector2(dia, dia);
            var circImg = circ.gameObject.AddComponent<Image>();
            circImg.sprite = CircleSprite(); circImg.color = new Color(0.10f, 0.10f, 0.16f, 0.96f); circImg.raycastTarget = false;
            var num = ProtoUI.CreateText("PNum", circ, "", 24, Vector2.zero, new Vector2(dia, dia), Color.white);
            num.fontStyle = FontStyles.Bold; num.raycastTarget = false;
            circ.gameObject.SetActive(false);
            tapLabels.Add(num);
            int idx = i;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (!inputOpen) return;
                taps.Add(idx);
                tapLabels[idx].text = taps.Count.ToString(); // 押した順番
                tapLabels[idx].transform.parent.gameObject.SetActive(true); // 丸を表示
                StartCoroutine(TapFeedback(img, baseColors[idx]));
            });
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
        Time.timeScale = _main.GameSpeed;
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
        if (img == null) yield break;
        img.color = Color.Lerp(baseCol, Color.white, 0.7f);
        yield return new WaitForSeconds(0.15f);
        if (img == null) yield break; // 破棄済みなら何もしない
        img.color = baseCol;
    }

    // ==================== アイドル ====================

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        // 効果音の再生速度をゲーム速度（倍速）に同期させ、演出と音のズレを防ぐ
        if (_sfx != null) _sfx.pitch = Mathf.Max(0.2f, Time.timeScale);

        bool esc = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) esc = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!esc && Input.GetKeyDown(KeyCode.Escape)) esc = true;
#endif
        if (esc && !_dead) { Retreat(); return; }   // Esc離脱も「逃げる」と同じペナルティ（戦闘不能後は不可）

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
        if (rt == null) yield break;
        Quaternion targetRot = rt.localRotation;   // 扇の角度を保持
        Vector2 startPos = finalPos + new Vector2(550, -260);
        rt.anchoredPosition = startPos; rt.localScale = Vector3.one * 0.25f;
        yield return new WaitForSeconds(delay);
        if (rt == null) yield break;   // 配り直しで破棄されていたら中断
        float t = 0f; const float dur = 0.22f;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.deltaTime; float p = Mathf.SmoothStep(0, 1, t / dur);
            rt.anchoredPosition = Vector2.Lerp(startPos, finalPos, p);
            rt.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, p);
            rt.localRotation = Quaternion.Slerp(Quaternion.Euler(0, 0, -25f), targetRot, p);
            yield return null;
        }
        if (rt == null) yield break;
        rt.anchoredPosition = finalPos; rt.localScale = Vector3.one; rt.localRotation = targetRot;
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
        // 高威力技はカード種別より優先して専用アニメ
        if (card.power >= 30) { yield return MotionBigCast(card); yield break; }
        switch (card.id)
        {
            case "fireball": case "gouka": case "guren": yield return MotionFlare(card); break;
            case "thunder": case "raijin": case "kannari": yield return MotionCyclone(card); break;
            case "sunshine": case "amaterasu": case "shingan": yield return MotionVoice(card); break;
            // 近接で殴りに行く系はやめ、その場で手から魔法を放つ演出に統一
            default: yield return MotionCast(card); break;
        }
    }

    // その場で詠唱：構えでタメる→コマ送りで振り→手から魔法を放つ（前進しない・全コマ再生）
    bool _castActive;   // 通常攻撃の放出ポーズを保持中（命中後に逆再生で戻す）

    IEnumerator MotionCast(CardDef card)
    {
        _castActive = true;
        var frames = ProtoPixelArt.AttackFrames();
        // 魔力を練る位置（胸元で手を合わせる先頭コマの両手）と、放つ位置（腕を伸ばした指先）
        Vector2 castPos = _actorRt.anchoredPosition + new Vector2(18, 70);
        Vector2 firePos = _actorRt.anchoredPosition + new Vector2(110, 143);
        Color col = card.CategoryColor;
        Color colW = Color.Lerp(col, Color.white, 0.7f);

        // ① 構え（先頭コマ）。両手の間で魔力の玉を生成し、脈動しながら膨らむ
        if (frames != null && frames.Count > 0 && _actorImg != null) _actorImg.sprite = frames[0];
        var orb = ProtoUI.CreateGlow("CastOrb", _root, castPos, new Vector2(60, 60), col);
        orb.raycastTarget = false;
        var ort = (RectTransform)orb.transform;
        var core = ProtoUI.CreateGlow("CastCore", _root, castPos, new Vector2(26, 26), colW);
        core.raycastTarget = false;
        var crt = (RectTransform)core.transform;
        float charge = 0.85f, t = 0f;
        while (t < charge)
        {
            t += Time.deltaTime; float p = t / charge;
            float pulse = 1f + Mathf.Sin(t * 30f) * 0.12f;
            ort.localScale = Vector3.one * ((0.15f + p * 1.6f) * pulse);
            crt.localScale = Vector3.one * ((0.1f + p * 1.3f) * pulse);
            // 周囲の魔力が手元へ吸い込まれるように集束
            for (int k = 0; k < 2; k++)
            {
                Vector2 around = castPos + new Vector2(Random.Range(-100f, 100f), Random.Range(-85f, 85f));
                StartCoroutine(ConvergeSpark(around, castPos, Random.value < 0.5f ? col : colW));
            }
            // 手元で回る火花のリング（塗り四角ではなく粒で表現）
            float ang = t * 8f;
            for (int k = 0; k < 3; k++)
            {
                float a = ang + k * (Mathf.PI * 2f / 3f);
                float rad = 34f * (0.6f + p * 0.7f);
                SpawnBurst(castPos + new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad), colW, 1, 6f);
            }
            yield return null;
        }
        // 溜め切りの一瞬フラッシュ
        StartCoroutine(ShockExpand(castPos, colW, 0.9f));
        SpawnBurst(castPos, col, 10, 70f);

        // ② 中割りのコマを再生（腕を伸ばす動き）。魔力の玉を手元から指先へ運び、尾を引く
        Vector2 orbFrom = castPos;
        if (frames != null && frames.Count > 1)
        {
            int steps = frames.Count - 1;
            for (int i = 1; i < frames.Count; i++)
            {
                if (_actorImg == null) break;
                _actorImg.sprite = frames[i];
                float fp = (float)i / steps;
                Vector2 op = Vector2.Lerp(orbFrom, firePos, Mathf.SmoothStep(0, 1, fp));
                ort.anchoredPosition = op; crt.anchoredPosition = op;
                SpawnBurst(op, col, 2, 26f);
                StartCoroutine(ConvergeSpark(op + new Vector2(Random.Range(-20f,20f), Random.Range(-20f,20f)), op, colW));
                yield return new WaitForSeconds(0.07f);
            }
        }
        ort.anchoredPosition = firePos; crt.anchoredPosition = firePos;

        // ③ 指先で解き放つ：多重衝撃波＋放射バースト＋画面フラッシュ → 指先から敵へ極太の魔法を放つ
        if (_sfx != null && _magicCastClip != null) _sfx.PlayOneShot(_magicCastClip);
        StartCoroutine(FlashSprite(_actorImg, colW));
        StartCoroutine(ScreenFlash(col, 0.18f));
        StartCoroutine(ShockExpand(firePos, colW, 1.5f));
        StartCoroutine(ShockExpand(firePos, col, 2.1f));
        SpawnBurst(firePos, col, 22, 130f);
        SpawnBurst(firePos, colW, 12, 80f);
        for (int r = 0; r < 3; r++) StartCoroutine(RingWave(firePos, _slimeRt.anchoredPosition, col));
        Destroy(orb.gameObject); Destroy(core.gameObject);
        yield return Projectile(firePos, col, 64f, 0.42f, false);
        yield return new WaitForSeconds(0.18f);
    }

    // 高威力技：両手を挙げ、頭上の手のところで巨大な魔力を練り上げてから放つ
    // 各大技コマを待機立ち絵と同じ見かけの身長にそろえる補正（身長比から算出）
    static readonly float[] _bigFrameScale = { 1.001f, 1.084f, 1.160f, 1.080f, 0.950f, 1.135f, 1.124f, 1.128f };
    float BigScaleAt(int i) => (i >= 0 && i < _bigFrameScale.Length) ? _bigFrameScale[i] : 1f;

    // 前回の技で破棄し損ねた魔力エフェクトを名前で掃除する
    static readonly string[] _fxNames = { "BigOrb", "BigCore", "BigChargeFlash", "CastOrb", "CastCore", "CastGlow", "Projectile", "ProjCore" };
    void ClearLingeringFx()
    {
        if (_root == null) return;
        for (int i = _root.childCount - 1; i >= 0; i--)
        {
            var ch = _root.GetChild(i);
            foreach (var nm in _fxNames)
                if (ch.name == nm) { Destroy(ch.gameObject); break; }
        }
    }

    bool _bigCastActive;   // 大技の放出ポーズを保持中（命中後にリカバリー動作で戻す）
    Vector2 _actorHome;    // 立ち位置の基準（拡大時に足元を固定するため）

    // 各コマの足位置（キャンバス下端からの割合。ポーズで異なるため個別に保持）
    const float FeetIdle = 0.9663f;
    static readonly float[] _bigFeet = { 0.9663f, 0.9655f, 0.9137f, 0.9589f, 0.9646f, 0.9071f, 0.8914f, 0.8939f };
    static readonly float[] _retFeet = { 0.8939f, 0.8914f, 0.9655f, 0.9663f };
    float BigFeetAt(int i) => (i >= 0 && i < _bigFeet.Length) ? _bigFeet[i] : FeetIdle;
    float RetFeetAt(int i) => (i >= 0 && i < _retFeet.Length) ? _retFeet[i] : FeetIdle;

    // 拡大しても、そのコマの実際の足位置が待機と同じ高さに来るよう位置補正する
    void SetActorScaleFoot(float s, float feetFrac)
    {
        if (_actorRt == null) return;
        float rth = _actorRt.sizeDelta.y;
        _actorRt.localScale = Vector3.one * s;
        float y = _actorHome.y + (0.5f - FeetIdle) * rth - (0.5f - feetFrac) * rth * s;
        _actorRt.anchoredPosition = new Vector2(_actorHome.x, y);
    }

    // 直前の値から目標のスケール・足位置へ滑らかに補間（コマ間のサイズ段差＝カクつきを防ぐ）
    float _prevActorS = 1f, _prevActorFeet = FeetIdle;
    IEnumerator EaseActorScale(float toS, float toFeet, float dur)
    {
        float fromS = _prevActorS, fromF = _prevActorFeet, t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime; float k = Mathf.SmoothStep(0f, 1f, t / dur);
            SetActorScaleFoot(Mathf.Lerp(fromS, toS, k), Mathf.Lerp(fromF, toFeet, k));
            yield return null;
        }
        SetActorScaleFoot(toS, toFeet);
        _prevActorS = toS; _prevActorFeet = toFeet;
    }

    IEnumerator MotionBigCast(CardDef card)
    {
        var frames = ProtoPixelArt.BigAttackFrames();
        if (frames == null || frames.Count == 0) { yield return MotionCast(card); yield break; }
        _bigCastActive = true;
        // 頭上に挙げた両手の位置（拡大＋足元補正で手が上がる分も見込んで、手の上でためて見せる）
        Vector2 handsPos = _actorRt.anchoredPosition + new Vector2(2, 198);
        Color col = card.CategoryColor;
        Color colW = Color.Lerp(col, Color.white, 0.7f);

        Vector2 chestPos = _actorRt.anchoredPosition + new Vector2(2, 55);
        Vector2 firePos = _actorRt.anchoredPosition + new Vector2(130, 80);
        // 挙げきる（両手が頭上）コマの位置
        int holdIdx = Mathf.Min(4, frames.Count - 1);

        // 各コマを待機立ち絵と同じ見かけの大きさにそろえる。タメ中はさらに拡大（足元は固定）
        const float RAISE_BUMP = 1.06f;    // 両手を挙げるまでは少し大きく
        const float CHARGE_BUMP = 1.12f;   // タメ中
        const float RELEASE_BUMP = 0.97f;  // 放出時は少し小さく

        // 魔力球（頭上で育て、後半に前へ運ぶ）
        Image orb = ProtoUI.CreateGlow("BigOrb", _root, chestPos, new Vector2(70, 70), col); orb.raycastTarget = false;
        Image core = ProtoUI.CreateGlow("BigCore", _root, chestPos, new Vector2(30, 30), colW); core.raycastTarget = false;
        var ort = (RectTransform)orb.transform; var crt = (RectTransform)core.transform;
        RectTransform chargeFlash = null;
        _prevActorS = 1f; _prevActorFeet = FeetIdle;   // 待機サイズから開始
        try
        {
        // ① 両手を挙げていくコマ送り（胸元→頭上へ魔力を集める）。サイズは滑らかに補間
        for (int i = 0; i <= holdIdx; i++)
        {
            if (_actorImg == null) break;
            _actorImg.sprite = frames[i];
            float p = (float)i / holdIdx;
            float bump = Mathf.Lerp(1f, RAISE_BUMP, p);
            Vector2 op = Vector2.Lerp(chestPos, handsPos, p);
            ort.anchoredPosition = op; crt.anchoredPosition = op;
            ort.localScale = Vector3.one * (0.6f + p * 1.5f);
            crt.localScale = Vector3.one * (0.4f + p * 1.3f);
            for (int k = 0; k < 4; k++)
            {
                Vector2 around = op + new Vector2(Random.Range(-140f, 140f), Random.Range(-110f, 130f));
                StartCoroutine(ConvergeSpark(around, op, Random.value < 0.5f ? col : colW));
            }
            SpawnBurst(op, col, 4, 38f);
            yield return EaseActorScale(BigScaleAt(i) * bump, BigFeetAt(i), 0.15f);
        }

        // ② 頭上で長めのタメ。魔力球が渦巻いて膨張、周囲から激しく吸い込み、画面が色付く
        if (_actorImg != null) _actorImg.sprite = frames[holdIdx];
        float holdScaleS = BigScaleAt(holdIdx) * CHARGE_BUMP;
        yield return EaseActorScale(holdScaleS, BigFeetAt(holdIdx), 0.12f);   // タメへ滑らかに拡大
        if (_sfx != null && _bigChargeClip != null) _sfx.PlayOneShot(_bigChargeClip, 0.9f);   // タメ音
        chargeFlash = ProtoUI.CreateFullScreen("BigChargeFlash", _root);
        var cfImg = chargeFlash.gameObject.AddComponent<Image>(); cfImg.raycastTarget = false;
        float charge = 1.9f, t = 0f, fxAccum = 0f;
        while (t < charge)
        {
            t += Time.deltaTime; float p = t / charge;
            float pulse = 1f + Mathf.Sin(t * 24f) * 0.16f;
            // タメが進むほど画面全体を強く揺らす
            float sa = 2f + p * p * 16f;
            _root.anchoredPosition = new Vector2(Random.Range(-sa, sa), Random.Range(-sa, sa));
            // 溜まるほど本体もわずかに膨らむ（足元は固定）
            SetActorScaleFoot(holdScaleS * (1f + p * 0.06f + Mathf.Sin(t * 18f) * 0.01f), BigFeetAt(holdIdx));
            ort.anchoredPosition = handsPos; crt.anchoredPosition = handsPos;
            ort.localScale = Vector3.one * ((1.8f + p * 3.0f) * pulse);
            crt.localScale = Vector3.one * ((1.3f + p * 2.4f) * pulse);
            cfImg.color = new Color(col.r, col.g, col.b, 0.22f * p);   // 画面がじわ光る
            // エフェクトは一定間隔でのみ生成（毎フレーム大量生成による処理落ち＝カクつきを防ぐ）
            fxAccum += Time.deltaTime;
            if (fxAccum >= 0.05f)
            {
                fxAccum = 0f;
                for (int k = 0; k < 3; k++)
                {
                    Vector2 around = handsPos + new Vector2(Random.Range(-220f, 220f), Random.Range(-140f, 230f));
                    StartCoroutine(ConvergeSpark(around, handsPos, Random.value < 0.5f ? col : colW));
                }
                float ang = t * 9f;
                for (int k = 0; k < 4; k++)
                {
                    float a = ang + k * (Mathf.PI * 2f / 4f);
                    float rad = 70f * (0.6f + p * 1.0f);
                    SpawnBurst(handsPos + new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad), colW, 1, 8f);
                    float a2 = -ang * 1.3f + k * (Mathf.PI * 2f / 4f);
                    float rad2 = 40f * (0.6f + p * 0.8f);
                    SpawnBurst(handsPos + new Vector2(Mathf.Cos(a2) * rad2, Mathf.Sin(a2) * rad2), col, 1, 6f);
                }
                if (Random.value < 0.35f) StartCoroutine(ShockExpand(handsPos, colW, 0.8f + p * 1.2f));
                if (Random.value < 0.3f) StartCoroutine(RingWave(handsPos + new Vector2(Random.Range(-40f,40f), Random.Range(-40f,40f)), handsPos, colW));
            }
            yield return null;
        }
        _root.anchoredPosition = Vector2.zero;   // 揺れをいったん原点へ戻す
        StartCoroutine(ShockExpand(handsPos, colW, 1.6f));
        SpawnBurst(handsPos, col, 26, 150f);
        SpawnBurst(handsPos, colW, 14, 90f);
        _prevActorS = holdScaleS;   // タメ終わりのサイズから放出へ補間

        // ③ 残りのコマで両手を前へ突き出し、魔力球を前方へ運ぶ。サイズは滑らかに補間
        for (int i = holdIdx + 1; i < frames.Count; i++)
        {
            if (_actorImg == null) break;
            _actorImg.sprite = frames[i];
            float p = (float)(i - holdIdx) / (frames.Count - holdIdx);
            Vector2 op = Vector2.Lerp(handsPos, firePos, p);
            ort.anchoredPosition = op; crt.anchoredPosition = op;
            SpawnBurst(op, col, 5, 44f);
            yield return EaseActorScale(BigScaleAt(i) * RELEASE_BUMP, BigFeetAt(i), 0.06f);
        }
        ort.anchoredPosition = firePos; crt.anchoredPosition = firePos;

        // ④ 特大の一撃：多重画面フラッシュ＋衝撃波＋リングウェーブ大量＋極太弾＋拡散弾
        if (_sfx != null && _magicCastClip != null) _sfx.PlayOneShot(_magicCastClip, 1.1f);   // 発射のシュッ
        StartCoroutine(FlashSprite(_actorImg, colW));
        StartCoroutine(ScreenFlash(colW, 0.5f));
        StartCoroutine(ScreenFlash(col, 0.35f));
        StartCoroutine(ScreenShake(30f, 0.6f));   // 画面全体を揺らして迫力＋モーションの粗を隠す
        StartCoroutine(Shake(_actorRt, 16f, 0.4f));
        StartCoroutine(ShockExpand(firePos, colW, 1.4f));
        StartCoroutine(ShockExpand(firePos, col, 1.9f));
        StartCoroutine(ShockExpand(firePos, col, 2.4f));
        SpawnBurst(firePos, col, 64, 260f);
        SpawnBurst(firePos, colW, 34, 150f);
        for (int r = 0; r < 10; r++) StartCoroutine(RingWave(firePos, _slimeRt.anchoredPosition, r % 2 == 0 ? col : colW));
        if (chargeFlash != null) { Destroy(chargeFlash.gameObject); chargeFlash = null; }
        Destroy(orb.gameObject); Destroy(core.gameObject); orb = null; core = null;
        // 極太の本命弾＋前後に散る追撃弾
        StartCoroutine(Projectile(firePos, colW, 60f, 0.42f, true));
        StartCoroutine(Projectile(firePos, col, 60f, 0.42f, true));
        yield return Projectile(firePos, col, 120f, 0.5f, false);
        // 着弾＝敵の位置で大爆発
        StartCoroutine(BigExplosion(_slimeRt.anchoredPosition, col));
        // 放ち切ったポーズのまま少し余韻（残り火をゆらす）。待機へは戻さず、技命中まで構えを保持
        for (int e = 0; e < 2; e++) { SpawnBurst(firePos, e % 2 == 0 ? col : colW, 4, 60f); yield return new WaitForSeconds(0.14f); }
        }
        finally
        {
            // 途中で戦闘が終わってコルーチンが停止されても、魔力エフェクトを必ず片付ける
            if (orb != null) Destroy(orb.gameObject);
            if (core != null) Destroy(core.gameObject);
            if (chargeFlash != null) Destroy(chargeFlash.gameObject);
            if (_root != null) _root.anchoredPosition = Vector2.zero;   // 画面揺れの残りを必ず戻す
        }
    }

    // 大技後の戻りアニメ用スケール補正（本体高さで正規化）
    static readonly float[] _retFrameScale = { 1.128f, 1.124f, 1.084f, 1.0f };
    float RetScaleAt(int i) => (i >= 0 && i < _retFrameScale.Length) ? _retFrameScale[i] : 1f;

    // ガードアニメ用のスケール・足位置補正
    static readonly float[] _guardScale = { 1.000f, 1.084f, 1.124f, 1.128f };
    static readonly float[] _guardFeet = { 0.9663f, 0.9655f, 0.8914f, 0.8939f };
    float GuardScaleAt(int i) => (i >= 0 && i < _guardScale.Length) ? _guardScale[i] : 1f;
    float GuardFeetAt(int i) => (i >= 0 && i < _guardFeet.Length) ? _guardFeet[i] : FeetIdle;

    // ガード時のエフェクト＆効果音（青い盾のリングが弾ける）
    IEnumerator GuardHitFx(bool fullBlock)
    {
        if (_sfx != null && _guardClip != null) _sfx.PlayOneShot(_guardClip, fullBlock ? 1f : 0.7f);
        Vector2 pos = _actorRt != null ? _actorRt.anchoredPosition + new Vector2(20, 20) : Vector2.zero;
        Color shield = fullBlock ? new Color(0.5f, 0.8f, 1f) : new Color(0.7f, 0.75f, 0.9f);
        Color shieldW = Color.Lerp(shield, Color.white, 0.6f);
        StartCoroutine(ShockExpand(pos, shieldW, 1.2f));
        SpawnBurst(pos, shield, fullBlock ? 16 : 8, fullBlock ? 120f : 80f);
        StartCoroutine(FlashSprite(_actorImg, shieldW));
        // 六角形の盾フラッシュ（回転しながら弾ける）
        var hex = ProtoUI.CreateGlow("GuardShield", _root, pos, new Vector2(150, 170), shield); hex.raycastTarget = false;
        var hrt = (RectTransform)hex.transform; hrt.localRotation = Quaternion.Euler(0, 0, 30);
        float t = 0f, dur = 0.32f;
        while (t < dur)
        {
            t += Time.deltaTime; float p = t / dur;
            hrt.localScale = Vector3.one * (0.7f + p * 0.6f);
            var c = shield; c.a = 0.85f * (1f - p); hex.color = c;
            yield return null;
        }
        Destroy(hex.gameObject);
    }

    // ガードの構えに入る（コマを再生して防御姿勢へ）
    IEnumerator MotionGuardEnter()
    {
        var frames = ProtoPixelArt.GuardFrames();
        if (frames == null || frames.Count == 0) yield break;
        _prevActorS = 1f; _prevActorFeet = FeetIdle;
        for (int i = 0; i < frames.Count; i++)
        {
            if (_actorImg == null) break;
            _actorImg.sprite = frames[i]; _actorImg.color = Color.white;
            yield return EaseActorScale(GuardScaleAt(i), GuardFeetAt(i), 0.07f);
        }
    }

    // ガードを解いて待機へ戻す（逆再生）
    IEnumerator MotionGuardExit()
    {
        var frames = ProtoPixelArt.GuardFrames();
        if (frames != null && frames.Count > 0 && _actorImg != null)
        {
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                if (_actorImg == null) break;
                _actorImg.sprite = frames[i]; _actorImg.color = Color.white;
                yield return EaseActorScale(GuardScaleAt(i), GuardFeetAt(i), 0.06f);
            }
        }
        if (_actorImg != null) { _actorImg.sprite = ProtoPixelArt.MamaPhoto(); _actorImg.color = Color.white; }
        if (_actorRt != null) { _actorRt.localScale = Vector3.one; _actorRt.anchoredPosition = _actorHome; }
    }

    // 大技の後、放出ポーズから待機立ち絵へ滑らかに戻すリカバリー動作（専用コマ mama_ret を再生）
    IEnumerator MotionBigRecover()
    {
        if (_actorImg != null) _actorImg.color = Color.white;   // 発光ティントを解除して色を待機と揃える
        var frames = ProtoPixelArt.RecoverFrames();
        if (frames != null && frames.Count > 0 && _actorImg != null)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                if (_actorImg == null) break;
                _actorImg.sprite = frames[i];
                _actorImg.color = Color.white;
                yield return EaseActorScale(RetScaleAt(i), RetFeetAt(i), 0.07f);
            }
        }
        // 仕上げ：待機立ち絵へ、拡大を等倍・立ち位置を基準に戻す
        if (_actorImg != null) { _actorImg.sprite = ProtoPixelArt.MamaPhoto(); _actorImg.color = Color.white; }
        if (_actorRt != null) { _actorRt.localScale = Vector3.one; _actorRt.anchoredPosition = _actorHome; }
        _bigCastActive = false;
    }

    // 通常攻撃の後、放出ポーズから待機立ち絵へ逆再生で戻す
    IEnumerator MotionCastRecover()
    {
        if (_actorImg != null) _actorImg.color = Color.white;
        var frames = ProtoPixelArt.AttackFrames();
        if (frames != null && frames.Count > 0 && _actorImg != null)
        {
            // 指先を伸ばした放出コマ→胸元の構えコマ へ逆順に再生
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                if (_actorImg == null) break;
                _actorImg.sprite = frames[i];
                _actorImg.color = Color.white;
                yield return new WaitForSeconds(0.05f);
            }
        }
        if (_actorImg != null) { _actorImg.sprite = ProtoPixelArt.MamaPhoto(); _actorImg.color = Color.white; }
        _castActive = false;
    }

    // 一点へ吸い込まれる魔力の粒
    IEnumerator ConvergeSpark(Vector2 from, Vector2 to, Color color)
    {
        var s = ProtoUI.CreatePanel("Spark", _root, from, new Vector2(10, 10), color); s.raycastTarget = false;
        var rt = (RectTransform)s.transform; Color c = color; float t = 0f, dur = 0.28f;
        while (t < dur) { t += Time.deltaTime; float p = t / dur; rt.anchoredPosition = Vector2.Lerp(from, to, p * p); rt.localScale = Vector3.one * (1f - p * 0.6f); c.a = 1f - p; s.color = c; yield return null; }
        Destroy(s.gameObject);
    }

    IEnumerator MotionFlare(CardDef card) { StartCoroutine(FlashSprite(_actorImg, new Color(1f, 0.85f, 0.55f))); yield return Pulse(_actorRt, 1.1f, 0.22f); yield return Projectile(card.CategoryColor, 38f, 0.32f, false); }

    IEnumerator MotionSlash(CardDef card)
    {
        Vector2 origin = _actorRt.anchoredPosition; Vector2 through = _slimeRt.anchoredPosition + new Vector2(170, 0);
        yield return Jab(_actorRt, new Vector2(-40, 0), 0.1f);
        float t = 0f;
        while (t < 0.16f) { t += Time.deltaTime; float p = t / 0.16f; _actorRt.anchoredPosition = Vector2.Lerp(origin, through, p * p); if (Random.value < 0.5f) SpawnBurst(_actorRt.anchoredPosition, new Color(card.CategoryColor.r, card.CategoryColor.g, card.CategoryColor.b, 0.6f), 1, 30f); yield return null; }
        SpawnSlashLine(_slimeRt.anchoredPosition, card.CategoryColor);
        yield return new WaitForSeconds(0.25f);
        t = 0f; Vector2 back = _actorRt.anchoredPosition;
        while (t < 0.2f) { t += Time.deltaTime; _actorRt.anchoredPosition = Vector2.Lerp(back, origin, Mathf.SmoothStep(0, 1, t / 0.2f)); yield return null; }
        _actorRt.anchoredPosition = origin;
    }

    IEnumerator MotionCyclone(CardDef card)
    {
        float t = 0f;
        while (t < 0.45f) { t += Time.deltaTime; _actorRt.localRotation = Quaternion.Euler(0, 0, -720f * (t / 0.45f)); if (Random.value < 0.4f) SpawnBurst(_actorRt.anchoredPosition, card.CategoryColor, 1, 80f); yield return null; }
        _actorRt.localRotation = Quaternion.identity; yield return Projectile(card.CategoryColor, 34f, 0.4f, true);
    }

    IEnumerator MotionVoice(CardDef card)
    {
        yield return Pulse(_actorRt, 1.15f, 0.18f);
        for (int i = 0; i < 3; i++) { StartCoroutine(RingWave(_actorRt.anchoredPosition + new Vector2(80, 60), _slimeRt.anchoredPosition, card.CategoryColor)); StartCoroutine(Pulse(_actorRt, 1.08f, 0.12f)); yield return new WaitForSeconds(0.16f); }
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
        yield return Projectile(_actorRt.anchoredPosition + new Vector2(130, 50), color, size, duration, wobble);
    }

    IEnumerator Projectile(Vector2 from, Color color, float size, float duration, bool wobble)
    {
        Vector2 to = _slimeRt.anchoredPosition;
        // 発光する外殻＋白い芯の二重弾
        var proj = ProtoUI.CreateGlow("Projectile", _root, from, new Vector2(size, size), color); proj.raycastTarget = false;
        var coreImg = ProtoUI.CreateGlow("ProjCore", _root, from, new Vector2(size * 0.5f, size * 0.5f), Color.Lerp(color, Color.white, 0.75f)); coreImg.raycastTarget = false;
        var rt = (RectTransform)proj.transform; var crt = (RectTransform)coreImg.transform; float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime; float p = t / duration; Vector2 pos = Vector2.Lerp(from, to, p);
            if (wobble) pos.y += Mathf.Sin(p * 18f) * 30f;
            rt.anchoredPosition = pos; crt.anchoredPosition = pos;
            rt.Rotate(0, 0, 720f * Time.deltaTime);
            float pulse = 1f + Mathf.Sin(t * 40f) * 0.15f; rt.localScale = Vector3.one * pulse;
            // 尾を引く火花
            SpawnBurst(pos, color, 2, 30f);
            if (Random.value < 0.6f) StartCoroutine(ConvergeSpark(pos + new Vector2(Random.Range(-24f, 24f), Random.Range(-24f, 24f)), pos, color));
            yield return null;
        }
        Destroy(proj.gameObject); Destroy(coreImg.gameObject);
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
        Color burstColor = card?.CategoryColor ?? Color.white;

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

    IEnumerator HitStop(float realSeconds) { Time.timeScale = 0.05f; yield return new WaitForSecondsRealtime(realSeconds); Time.timeScale = _main.GameSpeed; }

    // 汎用の浮き上がりテキスト（コイン獲得・呪いの代償など）
    IEnumerator TextPopup(Vector2 pos, string text, Color col, float fontSize = 34)
    {
        var holder = ProtoUI.CreateRect("TextPopup", _root);
        holder.anchoredPosition = pos + new Vector2(Random.Range(-25f, 25f), 0);
        holder.sizeDelta = new Vector2(400, 60);
        var group = holder.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
        var shadow = ProtoUI.CreateText("Shadow", holder, text, fontSize, new Vector2(3, -3), new Vector2(400, 60), new Color(0.05f, 0.04f, 0.1f));
        shadow.fontStyle = FontStyles.Bold; shadow.raycastTarget = false;
        var main = ProtoUI.CreateText("Main", holder, text, fontSize, Vector2.zero, new Vector2(400, 60), col);
        main.fontStyle = FontStyles.Bold; main.raycastTarget = false; main.outlineWidth = 0.22f; main.outlineColor = new Color32(10, 8, 24, 255);
        float t = 0f;
        while (t < 0.1f) { t += Time.deltaTime; holder.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, t / 0.1f); yield return null; }
        yield return new WaitForSeconds(0.35f);
        t = 0f; const float fade = 0.4f;
        while (t < fade) { t += Time.deltaTime; group.alpha = 1f - t / fade; holder.anchoredPosition += new Vector2(0, Time.deltaTime * 100f); yield return null; }
        Destroy(holder.gameObject);
    }

    IEnumerator DamagePopup(Vector2 pos, int damage, float multiplier)
    {
        float fontSize, popScale, life; bool useGradient; TMPro.VertexGradient gradient = default; Color flatColor = Color.white;
        // 3桁ダメージは倍率に関係なく最上位の演出（爆発ビルドのご褒美）
        bool huge = damage >= 100;
        if (huge || multiplier >= 1.25f) { fontSize = huge ? 110 : 88; popScale = huge ? 3.2f : 2.6f; life = huge ? 1.0f : 0.8f; useGradient = true; gradient = new TMPro.VertexGradient(new Color(1f, 0.98f, 0.8f), new Color(1f, 0.95f, 0.65f), new Color(1f, 0.55f, 0.12f), new Color(0.95f, 0.38f, 0.08f)); }
        else if (multiplier >= 1.05f) { fontSize = 64; popScale = 1.8f; life = 0.65f; useGradient = false; flatColor = new Color(1f, 0.92f, 0.45f); }
        else { fontSize = 46; popScale = 1.3f; life = 0.55f; useGradient = false; flatColor = Color.white; }
        if (huge)
        {
            StartCoroutine(ScreenFlash(new Color(1f, 0.7f, 0.2f), 0.3f));
            StartCoroutine(ScreenShake(20f, 0.35f));
            StartCoroutine(TextPopup(pos + new Vector2(0, 90f), damage >= 300 ? "壊滅的一撃！！" : "強烈な一撃！", new Color(1f, 0.6f, 0.15f), damage >= 300 ? 44 : 36));
        }

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

    // 着弾時の大爆発（膨張する火球＋多重衝撃波＋飛散＋閃光＋画面揺れ）
    IEnumerator BigExplosion(Vector2 pos, Color color)
    {
        if (_sfx != null && _bigReleaseClip != null) _sfx.PlayOneShot(_bigReleaseClip);   // 着弾のドォン
        Color colW = Color.Lerp(color, Color.white, 0.75f);
        Color hot = Color.Lerp(color, new Color(1f, 0.9f, 0.5f), 0.5f);   // 芯の白熱色
        // 閃光と揺れ
        StartCoroutine(ScreenFlash(colW, 0.55f));
        StartCoroutine(ScreenShake(34f, 0.55f));
        // 多重衝撃波
        StartCoroutine(ShockExpand(pos, colW, 1.8f));
        StartCoroutine(ShockExpand(pos, color, 2.6f));
        StartCoroutine(ShockExpand(pos, color, 3.4f));
        // 外へ広がるリング
        for (int r = 0; r < 8; r++)
            StartCoroutine(RingWave(pos, pos + new Vector2(Random.Range(-260f, 260f), Random.Range(-200f, 200f)), r % 2 == 0 ? color : colW));
        // 飛散する火片
        SpawnBurst(pos, color, 60, 300f);
        SpawnBurst(pos, hot, 40, 210f);
        SpawnBurst(pos, colW, 26, 140f);
        // 膨張して消える火球（芯＋外殻）
        var ball = ProtoUI.CreateGlow("Explosion", _root, pos, new Vector2(120, 120), color); ball.raycastTarget = false;
        var core = ProtoUI.CreateGlow("ExplosionCore", _root, pos, new Vector2(70, 70), hot); core.raycastTarget = false;
        var brt = (RectTransform)ball.transform; var crt = (RectTransform)core.transform;
        float t = 0f, dur = 0.5f;
        while (t < dur)
        {
            t += Time.deltaTime; float p = t / dur;
            brt.localScale = Vector3.one * (0.4f + p * 3.2f);
            crt.localScale = Vector3.one * (0.3f + p * 2.4f);
            var cb = color; cb.a = 1f - p; ball.color = cb;
            var cc = hot; cc.a = 1f - p * 1.3f; core.color = cc;
            yield return null;
        }
        Destroy(ball.gameObject); Destroy(core.gameObject);
    }

    // 画面全体を揺らす（大技の迫力＆モーションの粗をごまかす）
    IEnumerator ScreenShake(float amp, float duration)
    {
        if (_root == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float d = amp * (1f - t / duration);   // 徐々に収まる
            _root.anchoredPosition = new Vector2(Random.Range(-d, d), Random.Range(-d, d));
            yield return null;
        }
        _root.anchoredPosition = Vector2.zero;   // 必ず原点へ戻す（端に別背景が見えるのを防ぐ）
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
// 手札カード：ホバーで上昇＋発光、クリックで発動
public class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    RectTransform _rt;         // 見た目カード（焼き込み画像のRawImage。通常は _home 直下で扇の角度に回転）
    RectTransform _home;       // 通常時の親（_handArea）
    RectTransform _hoverLayer; // ホバー中だけ退避する最前面レイヤー
    Vector2 _slotPos;          // カードの定位置
    Quaternion _homeRot;       // 扇の傾き（戻すときに復元。平面画像なので回転しても崩れない）
    Graphic _frame;            // 見た目（RawImage）。ホバーで少し明るく
    Color _base;
    bool _hovering;
    System.Action _onEnter, _onExit, _onClick;

    public void Setup(RectTransform rt, RectTransform home, RectTransform hoverLayer, Vector2 slotPos, Quaternion homeRot, Graphic frame, Color baseCol,
        System.Action onEnter, System.Action onExit, System.Action onClick)
    {
        _rt = rt; _home = home; _hoverLayer = hoverLayer; _slotPos = slotPos; _homeRot = homeRot; _frame = frame; _base = baseCol;
        _onEnter = onEnter; _onExit = onExit; _onClick = onClick;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (_rt == null || _hovering) return;
        _hovering = true;
        // 山より上へしっかり持ち上げ、まっすぐ拡大（最前面レイヤーへ退避）
        float hx = Mathf.Clamp(_slotPos.x, -560f, 560f);
        _rt.SetParent(_hoverLayer, false);
        _rt.anchoredPosition = new Vector2(hx, _slotPos.y + 290f);
        _rt.localRotation = Quaternion.identity;
        _rt.localScale = Vector3.one * 1.28f;
        if (_frame != null) _frame.color = new Color(1.15f, 1.15f, 1.15f, 1f); // 少し明るく
        _onEnter?.Invoke();
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (_rt == null || !_hovering) return;
        _hovering = false;
        // 通常の親へ戻し、扇の位置・傾きを復元（平面画像なので回転しても崩れない）
        if (_home != null) _rt.SetParent(_home, false);
        _rt.anchoredPosition = _slotPos;
        _rt.localRotation = _homeRot;
        _rt.localScale = Vector3.one;
        if (_frame != null) _frame.color = _base;
        _onExit?.Invoke();
    }

    public void OnPointerClick(PointerEventData e) => _onClick?.Invoke();
}
