using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// 本設の起動スクリプト。空のGameObjectにこれを付けて再生するだけで全UIを自動生成する。
// 単一キャラ（MAMA）。データは Resources の ContentDatabase から読み込む。
public class ProtoMain : MonoBehaviour
{
    // ---- データ ----
    public ContentDatabase Db { get; private set; }
    public GameConfig Cfg => Db != null ? Db.config : null;

    // ---- プレイヤー状態 ----
    public const int GridDim = 10;        // 盤面グリッド（10×10＝最大100マス）
    public const int InitialCols = 6;     // 初期解放 横6
    public const int InitialRows = 5;     // 初期解放 縦5 （6×5＝30マス）
    public const int MaxCells = 100;      // 最大マス数

    public PlayerStats Stats { get; private set; }
    public PanelModel Panel { get; private set; }
    public int Money { get; private set; }
    public int CellStock { get; private set; }     // 入手済み・未配置のマス数
    public int CurrentHP { get; private set; }     // 戦闘をまたいで継続するHP
    public int BoardCells => Panel != null ? Panel.UnlockedCount() : 0; // 現在の解放マス数
    public int MaxMana => BoardCells / 10 + (Equipped == EquipKind.ManaPendant ? 1 : 0)
        + (CornersUnlocked >= 4 ? GameBalance.CornerMana : 0); // マナ＝盤面÷10＋装備＋四隅の加護Lv4

    // ---- 四隅の加護：解放した隅の数だけ累積ボーナス ----
    // Lv1: 攻撃威力+5% / Lv2: 毎ターン開始ブロック+3 / Lv3: 毎ターンHP+2回復 / Lv4: 最大マナ+1
    public int CornersUnlocked
    {
        get
        {
            if (Panel == null) return 0;
            int n = 0, w = Panel.W - 1, h = Panel.H - 1;
            if (Panel.IsUnlocked(0, 0)) n++;
            if (Panel.IsUnlocked(w, 0)) n++;
            if (Panel.IsUnlocked(0, h)) n++;
            if (Panel.IsUnlocked(w, h)) n++;
            return n;
        }
    }
    public EquipKind Equipped { get; private set; } = EquipKind.None; // 装備（1つだけ）
    // 生命のペンダントで最大HP+10%
    public int MaxHP => Stats == null ? 0 : Stats.MaxHP + (Equipped == EquipKind.LifePendant ? Mathf.RoundToInt(Stats.MaxHP * 0.10f) : 0);

    // 難易度による開始時の最大HP（EASY〜HARD:70 / VERY HARD:60 / MASTER:50）
    int AscensionBaseHP() => Ascension >= 4 ? 50 : Ascension >= 3 ? 60 : 70;
    public void SetEquip(EquipKind e)
    {
        int before = MaxHP;
        Equipped = e;
        int after = MaxHP;
        if (after > before) CurrentHP += after - before; // 最大HP増加分はそのまま回復
        CurrentHP = Mathf.Clamp(CurrentHP, 0, MaxHP);
    }
    // 代金を払って装備（持ち替え＝既存があれば上書き）。price=0で無料。
    public void SpendAndEquip(EquipKind e, int price)
    {
        if (e == EquipKind.None) return;
        Money = Mathf.Max(0, Money - Mathf.Max(0, price));
        SetEquip(e);
    }
    public int Wave { get; private set; } = 1;
    public int CurrentDepth { get; set; } = 1; // 現在地の深度（マップの列番号）。報酬/ショップの抽選に使う

    // ---- メタ進行（アセンション／シード） ----
    public int Ascension { get; private set; }      // このランの難度
    public int MapSeed { get; private set; }        // 現在マップの生成シード（セーブで再現）
    public void NewMapSeed() => MapSeed = Random.Range(1, int.MaxValue);

    // ---- 演出速度（周回の快適性。Time.timeScaleで一括制御） ----
    public float GameSpeed { get; private set; } = 1f;
    public void SetGameSpeed(float s, bool save = true)
    {
        GameSpeed = Mathf.Clamp(s, 1f, 2f);
        Time.timeScale = GameSpeed;
        if (save) PlayerPrefs.SetFloat("gamespeed", GameSpeed);
    }

    // ---- ラン統計（リザルト表示用） ----
    public int StatTotalDamage { get; private set; }
    public int StatMaxHit { get; private set; }
    public void AddDamageStat(int dmg)
    {
        if (dmg <= 0) return;
        StatTotalDamage += dmg;
        if (dmg > StatMaxHit) StatMaxHit = dmg;
    }
    public void ResetRunStats() { StatTotalDamage = 0; StatMaxHit = 0; }
    // アセンション倍率
    public float EnemyHpMul => 1f + 0.08f * Ascension;
    public float EnemyDmgMul => 1f + 0.05f * Ascension;
    public float ShopPriceMul => Ascension >= 3 ? 1.25f : 1f;

    public List<string> OwnedCardIds { get; private set; } = new List<string>();      // 所持したことのあるカードid（種類）
    public Dictionary<string, int> CardStock { get; private set; } = new Dictionary<string, int>(); // 未配置の在庫数

    // ---- 悪魔の契約（このラン中ずっと続く呪い） ----
    public int CursedSeals { get; private set; }      // 歪みの契約：毎戦闘この数だけ歪みマスが確定発生
    public bool PainContract { get; private set; }    // 痛みの契約：カード使用ごとにHP-1、毎ターンマナ+1
    public bool DemonHeart { get; private set; }      // 悪魔の心臓：HPが半分以下で攻撃+30%
    public void AddCursedSeal() => CursedSeals++;
    public void SetPainContract() => PainContract = true;
    public void SetDemonHeart() => DemonHeart = true;

    // ---- 大型契約（ビルドの方向性ごと変える取引） ----
    public bool SoulVessel { get; private set; }      // 魂の器：最大HP半減・毎ターン手札+2
    public bool Gluttony { get; private set; }        // 暴食の契約：最大マナ+2・毎ターン開始HP-3
    public bool Berserk { get; private set; }         // 破壊神の腕：攻撃威力1.5倍・被ダメージ+25%
    public void SetSoulVessel() => SoulVessel = true;
    public void SetGluttony() => Gluttony = true;
    public void SetBerserk() => Berserk = true;

    // 盤面のランダムな通常マスを1つ呪いマスにする（血の刻印）。成功でtrue
    public bool CurseRandomCell()
    {
        if (Panel == null) return false;
        var cells = Panel.GetUnlockedCells().FindAll(c => Panel.KindAt(c.x, c.y) == CellKind.Normal);
        if (cells.Count == 0) return false;
        var c = cells[Random.Range(0, cells.Count)];
        Panel.SetKind(c.x, c.y, CellKind.Curse);
        return true;
    }

    public Canvas Canvas { get; private set; }
    public bool BgmEnabled { get; private set; }

    // ---- 所持カード操作 ----
    // 在庫が1以上あるカード（ビルドのトレイ表示用）
    public List<CardDef> OwnedCards()
    {
        var list = new List<CardDef>();
        if (Db == null) return list;
        foreach (var id in OwnedCardIds)
        {
            if (OwnedCount(id) <= 0) continue;
            var c = Db.FindCard(id);
            if (c != null) list.Add(c);
        }
        return list;
    }

    public bool OwnsCard(string id) => OwnedCardIds.Contains(id);
    public int OwnedCount(string id) => (id != null && CardStock.TryGetValue(id, out var n)) ? n : 0;

    // カード入手（在庫+1）
    public bool AddCard(string id)
    {
        if (string.IsNullOrEmpty(id) || Db.FindCard(id) == null) return false;
        if (!OwnedCardIds.Contains(id)) OwnedCardIds.Add(id);
        CardStock[id] = OwnedCount(id) + 1;
        ProtoUnlocks.MarkDiscovered(id);   // 図鑑に永続登録
        return true;
    }

    // 盤面に配置＝在庫消費
    public bool ConsumeCard(string id)
    {
        if (OwnedCount(id) <= 0) return false;
        CardStock[id] = OwnedCount(id) - 1;
        return true;
    }

    // 盤面から撤去＝在庫に戻す
    public void ReturnCard(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!OwnedCardIds.Contains(id)) OwnedCardIds.Add(id);
        CardStock[id] = OwnedCount(id) + 1;
    }

    // ---- 経済 ----
    public void AddMoney(int amount)
    {
        if (amount > 0 && Equipped == EquipKind.GaneshaPendant) amount *= 2; // ガネーシャ：入手コイン2倍
        Money = Mathf.Max(0, Money + amount);
    }

    // ---- HP（戦闘間で継続） ----
    public void SetCurrentHP(int hp) => CurrentHP = Mathf.Clamp(hp, 0, Stats != null ? MaxHP : hp);
    public void HealFull() { if (Stats != null) CurrentHP = MaxHP; }

    // ---- 盤面マス入手・解放 ----
    public void AwardCells(int n)
    {
        if (n > 0 && Equipped == EquipKind.CellPendant) n *= 2; // マス増強：入手ストックマス2倍
        if (n > 0) CellStock += n;
    }

    // 契約：最大HPを10払って ストックマス+1（最大HPは最低10まで）
    public bool ContractTradeHpForCell()
    {
        if (Stats == null || Stats.MaxHP - 10 < 10) return false;
        Stats.MaxHP -= 10;
        if (CurrentHP > MaxHP) CurrentHP = MaxHP;
        CellStock += 1;
        return true;
    }

    // ビルド画面でロック中のマスを解放（ストックマスを1消費・最大100まで）
    public bool UnlockCell(int x, int y)
    {
        if (CellStock <= 0) return false;
        if (Panel.UnlockedCount() >= MaxCells) return false;
        if (!Panel.Unlock(x, y)) return false;
        CellStock--;
        RollSpecialCell(x, y);
        return true;
    }

    // デバッグ用：ストックマスを消費せずに解放（ビルド画面のデバッグモード）
    public bool DebugUnlockCell(int x, int y)
    {
        if (Panel.UnlockedCount() >= MaxCells) return false;
        if (!Panel.Unlock(x, y)) return false;
        RollSpecialCell(x, y);
        return true;
    }

    // 盤面の4隅は確定で特殊マス（4種を1つずつシャッフルして配置）
    // 隅は解放コストが高い（遠い）ぶん、確実なご褒美として機能する
    void SetupCornerSpecials()
    {
        var kinds = new List<CellKind> { CellKind.Power, CellKind.Gold, CellKind.Resonance, CellKind.Curse };
        for (int i = kinds.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (kinds[i], kinds[j]) = (kinds[j], kinds[i]); }
        int w = Panel.W - 1, h = Panel.H - 1;
        Panel.SetKind(0, 0, kinds[0]);
        Panel.SetKind(w, 0, kinds[1]);
        Panel.SetKind(0, h, kinds[2]);
        Panel.SetKind(w, h, kinds[3]);
    }

    // 新しく解放したマスは18%で特殊マスになる（強化/黄金/共鳴/呪い）
    void RollSpecialCell(int x, int y)
    {
        if (Panel.KindAt(x, y) != CellKind.Normal) return;   // 既に特殊なら維持
        if (Random.value >= GameBalance.SpecialRollChance) return;
        float r = Random.value * 100f;
        var k = r < GameBalance.RollPower ? CellKind.Power
              : r < GameBalance.RollPower + GameBalance.RollGold ? CellKind.Gold
              : r < GameBalance.RollPower + GameBalance.RollGold + GameBalance.RollResonance ? CellKind.Resonance
              : CellKind.Curse;
        Panel.SetKind(x, y, k);
    }

    // 配置セッション開始時の状態(keep)まで戻し、その間に解放したマスをストックマスへ払い戻す
    public void ResetToBaseline(List<Vector2Int> keep)
    {
        int before = Panel.UnlockedCount();
        Panel.RelockAll();
        if (keep != null) foreach (var c in keep) Panel.Unlock(c.x, c.y);
        Panel.RemovePlacementsOnLocked();
        int after = Panel.UnlockedCount();
        CellStock += Mathf.Max(0, before - after);
    }

    public bool BuyCard(string id)
    {
        int price = Cfg != null ? Cfg.shopBuyPrice : 40;
        if (OwnedCardIds.Contains(id) || Money < price || Db.FindCard(id) == null) return false;
        Money -= price;
        AddCard(id);   // 在庫+1
        return true;
    }

    public void SetWave(int wave) => Wave = wave;

    // ---- 盤面の隣接シナジー＋形状シナジー ----
    // 隣接：同じ種別のピースが隣接するほどボーナス。攻撃=威力% / 防御=開始ブロック / 回復=毎ターン回復 / スキル=毎ターンマナ
    // 形状：行コンプリート=毎ターン手札+1（最大+2）／列コンプリート=毎ターンブロック+4／コア（完全包囲）=そのカードの威力+50%
    public struct Synergy { public int attackPct, block, regen, mana, atkC, defC, healC, skillC, draw, rows, cols, cores; }

    public Synergy ComputeSynergy()
    {
        var s = new Synergy();
        if (Panel == null) return s;
        int w = Panel.W, h = Panel.H;
        // 右・上の隣接だけ見て二重カウントを防ぐ
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!Panel.IsUnlocked(x, y)) continue;
                var a = Panel.GetAt(x, y);
                if (a == null || a.card == null) continue;
                CountEdge(a, x, y, x + 1, y, ref s);
                CountEdge(a, x, y, x, y + 1, ref s);
            }
        // 共鳴のペンダント：隣接シナジーを1.5倍で数える
        float adjMult = Equipped == EquipKind.EchoPendant ? 1.5f : 1f;
        s.attackPct = Mathf.Min(Mathf.RoundToInt(s.atkC * 4 * adjMult), 60);
        s.block = Mathf.RoundToInt(s.defC * 3 * adjMult);
        s.regen = Mathf.RoundToInt(s.healC * 2 * adjMult);
        s.mana = Mathf.RoundToInt(s.skillC / 2f * adjMult);

        // 形状シナジー（構築のペンダントで行/列の効果2倍）
        int shapeMult = Equipped == EquipKind.ArchitectPendant ? 2 : 1;
        s.rows = Panel.CompletedRows();
        s.cols = Panel.CompletedCols();
        s.cores = Panel.CoreCount();
        s.draw = Mathf.Min(s.rows * shapeMult, 2 * shapeMult);             // 行コンプリート：毎ターン手札+1（最大+2、構築で+2/最大+4）
        s.block += s.cols * GameBalance.ColCompleteBlock * shapeMult;      // 列コンプリート：毎ターンブロック
        return s;
    }

    void CountEdge(PanelModel.Placement a, int ax, int ay, int nx, int ny, ref Synergy s)
    {
        if (!Panel.IsUnlocked(nx, ny)) return;
        var b = Panel.GetAt(nx, ny);
        if (b == null || b.card == null || b == a) return;               // 別ピース同士のみ
        if (a.card.Category != b.card.Category) return;                  // 同じ種別の接続だけ
        // 共鳴マス：接続のどちらかが共鳴マスなら2倍で数える
        int inc = (Panel.KindAt(ax, ay) == CellKind.Resonance || Panel.KindAt(nx, ny) == CellKind.Resonance) ? GameBalance.ResonanceMult : 1;
        switch (a.card.Category)
        {
            case CardKind.Attack: s.atkC += inc; break;
            case CardKind.Defense: s.defC += inc; break;
            case CardKind.Heal: s.healC += inc; break;
            default: s.skillC += inc; break;
        }
    }

    // ---- カード成長（効果+20%・名前に＋） ----
    public Dictionary<string, int> GrowthLevels { get; private set; } = new Dictionary<string, int>();

    // 鍛冶で強化できるのは 攻撃／防御／回復 のカードのみ（スキルは不可）
    public static bool IsForgeable(CardDef c) => c != null && c.Category != CardKind.Skill;

    // 防御・回復で「強化対象」とする効果種別
    static readonly HashSet<CardEffectType> DefenseBoost = new HashSet<CardEffectType>
    { CardEffectType.Protect, CardEffectType.Block, CardEffectType.Thorns, CardEffectType.BlockRegen,
      CardEffectType.Reflect, CardEffectType.GuardTurns, CardEffectType.Counter };
    static readonly HashSet<CardEffectType> HealBoost = new HashSet<CardEffectType>
    { CardEffectType.Heal, CardEffectType.HealPercent, CardEffectType.Regen, CardEffectType.HealMissing, CardEffectType.HealOverflowBlock };

    // 1段階成長させる（GrowthLevelsは触らない内部処理）
    // 攻撃＝威力、防御＝軽減/ブロック量、回復＝回復量 の「その数値だけ」を+20%（単純強化）
    void DoGrow(string id)
    {
        var card = Db != null ? Db.FindCard(id) : null;
        if (card == null) return;
        var cat = card.Category;
        var clone = ScriptableObject.Instantiate(card);
        clone.id = id;
        clone.displayName = card.displayName + "＋";

        // 攻撃：威力だけ+20%
        clone.power = cat == CardKind.Attack ? Mathf.RoundToInt(card.power * 1.2f) : card.power;

        // 防御／回復：該当する効果の amount だけ+20%
        if (card.effects != null)
        {
            var boost = cat == CardKind.Heal ? HealBoost : cat == CardKind.Defense ? DefenseBoost : null;
            clone.effects = new CardEffect[card.effects.Length];
            for (int i = 0; i < card.effects.Length; i++)
            {
                var e = card.effects[i];
                int amt = (boost != null && boost.Contains(e.type)) ? Mathf.RoundToInt(e.amount * 1.2f) : e.amount;
                clone.effects[i] = new CardEffect { type = e.type, amount = amt, duration = e.duration };
            }
        }
        Db.OverrideCard(id, clone);
        // 既存の盤面配置の参照も差し替え
        if (Panel != null)
            foreach (var p in Panel.Placements)
                if (p.card != null && p.card.id == id) p.card = clone;
    }

    // 所持カードを1枚成長（プレイヤー操作）。成功でtrue
    public bool GrowCard(string id)
    {
        if (Db == null || Db.FindCard(id) == null) return false;
        DoGrow(id);
        GrowthLevels[id] = (GrowthLevels.TryGetValue(id, out var l) ? l : 0) + 1;
        return true;
    }

    // ロード時に成長段階を再適用
    public void ReapplyGrowth()
    {
        foreach (var kv in GrowthLevels)
            for (int i = 0; i < kv.Value; i++) DoGrow(kv.Key);
    }

    // ---- 画面 ----
    BuildScreen _build;
    ProtoBattle _battle;
    MapScreen _map;
    MenuScreen _menu;
    UnityEngine.UI.Image _bgImg;
    AudioSource _bgmSource;
    AudioClip _fieldBgm, _battleBgm, _midBossBgm, _bossBgm, _shopBgm, _treeBgm, _evilBgm;

    void Awake()
    {
        // データ読み込み
        Db = Resources.Load<ContentDatabase>("GameData/ContentDatabase");
        if (Db == null)
            Debug.LogError("[ProtoMain] ContentDatabase が見つかりません。メニュー『MamaGame > コンテンツ(SO)を生成』を実行してください。");

        SetupCamera();
        Canvas = ProtoUI.CreateCanvas();
        EnsureEventSystem();

        // バトル用の自然背景
        var bgRt = ProtoUI.CreateFullScreen("Background", Canvas.transform);
        _bgImg = bgRt.gameObject.AddComponent<UnityEngine.UI.Image>();
        _bgImg.sprite = ProtoPixelArt.NatureBackground();
        _bgImg.color = new Color(0.85f, 0.9f, 0.9f);
        _bgImg.raycastTarget = false;

        // メタ進行：アセンション適用・シード確定（マップ生成前に）
        Ascension = ProtoUnlocks.Ascension;
        NewMapSeed();

        // プレイヤー初期化
        Db?.ClearOverrides(); GrowthLevels.Clear();
        Stats = new PlayerStats(Cfg);
        Stats.MaxHP = AscensionBaseHP();   // 難易度で最大HPを設定（VERY HARD/MASTERは低い）
        Equipped = EquipKind.None;
        CurrentHP = MaxHP;   // 開始は満タン（難易度ペナルティは最大HPで表現）
        Money = 0;
        CellStock = 0;
        Panel = new PanelModel(GridDim, GridDim);
        Panel.UnlockInitial(InitialCols, InitialRows);
        SetupCornerSpecials();   // 4隅は確定で特殊マス

        // 初期所持カード
        InitInitialCards();

        // 画面生成
        _build = gameObject.AddComponent<BuildScreen>();
        _battle = gameObject.AddComponent<ProtoBattle>();
        _map = gameObject.AddComponent<MapScreen>();
        _menu = gameObject.AddComponent<MenuScreen>();
        _build.Init(this);
        _battle.Init(this);
        _map.Init(this);
        _menu.Init(this);

        // セーブ復元（あれば）
        ProtoSave.Load(this);

        // BGM：Assets/Resources に音源ファイルがあればそれを優先（bgm_field / bgm_battle / bgm_boss）
        // 無ければ従来の生成音にフォールバック
        AudioListener.volume = PlayerPrefs.GetFloat("volume", 0.8f);
        _fieldBgm = Resources.Load<AudioClip>("bgm_field") ?? ProtoAudio.CreateBgm();
        _battleBgm = Resources.Load<AudioClip>("bgm_battle") ?? ProtoAudio.CreateBattleBgm();
        _midBossBgm = Resources.Load<AudioClip>("bgm_midboss") ?? ProtoAudio.CreateMidBossBgm();
        _bossBgm = Resources.Load<AudioClip>("bgm_boss") ?? ProtoAudio.CreateBossBgm();
        _shopBgm = Resources.Load<AudioClip>("bgm_shop") ?? ProtoAudio.CreateShopBgm();
        _treeBgm = Resources.Load<AudioClip>("bgm_tree") ?? ProtoAudio.CreateTreeBgm();
        _evilBgm = Resources.Load<AudioClip>("bgm_contract") ?? ProtoAudio.CreateEvilBgm();
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.clip = _fieldBgm;
        _bgmSource.loop = true;
        SetBgmEnabled(PlayerPrefs.GetInt("bgm", 1) == 1);
        SetGameSpeed(PlayerPrefs.GetFloat("gamespeed", 1f));
        ResetRunStats();
    }

    void Start() => ShowTitle();

    // ==================== タイトル画面 ====================
    GameObject _titleGO;
    void ShowTitle()
    {
        _battle.Hide(); _build.Hide(); _map.Hide(); _menu.Hide();
        _bgImg.enabled = false;

        var rt = ProtoUI.CreateFullScreen("Title", Canvas.transform);
        _titleGO = rt.gameObject;
        var bg = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        bg.color = Color.black;   // 背景は真っ黒

        var title = ProtoUI.CreateText("TitleLogo", rt, "Project M（仮）", 72, new Vector2(0, 180), new Vector2(1200, 110), ProtoUI.Gold);
        ProtoUI.StyleTitle(title, ProtoUI.Gold, 10f);

        bool hasSave = ProtoSave.HasSave();
        ProtoUI.CreateGoldButton("TNew", rt, "最初から", 26, new Vector2(0, -20), new Vector2(340, 72),
            new Color(0.35f, 0.3f, 0.55f, 0.98f), () =>
            {
                // セーブがあるときは誤操作でデータが消えないよう確認を挟む
                if (hasSave) ShowTitleNewGameConfirm(rt);
                else { Destroy(_titleGO); _titleGO = null; RestartRun(); }
            });
        if (hasSave)
            ProtoUI.CreateGoldButton("TContinue", rt, "続きから", 26, new Vector2(0, -120), new Vector2(340, 72),
                new Color(0.30f, 0.45f, 0.32f, 0.98f), () => { Destroy(_titleGO); _titleGO = null; ShowMap(); });
        ProtoUI.CreateText("TVer", rt, $"ver {Application.version}", 15,
            new Vector2(0, -380), new Vector2(900, 24), new Color(0.6f, 0.62f, 0.75f));
    }

    // タイトルの「最初から」確認（セーブ消去の警告）
    void ShowTitleNewGameConfirm(RectTransform parent)
    {
        var ov = ProtoUI.CreateFullScreen("TitleConfirm", parent);
        ov.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, 0.75f);
        ProtoUI.CreateFramedPanel("TCBox", ov, Vector2.zero, new Vector2(640, 300),
            new Color(0.10f, 0.08f, 0.16f, 0.98f), new Color(0.85f, 0.72f, 0.4f, 0.9f));
        ProtoUI.CreateText("TCMsg", ov, "最初から始めますか？", 28, new Vector2(0, 70), new Vector2(600, 40), Color.white);
        ProtoUI.CreateText("TCSub", ov, "セーブデータ（カード・装備・進行）はすべて消去されます", 18,
            new Vector2(0, 24), new Vector2(600, 30), new Color(1f, 0.6f, 0.6f));
        ProtoUI.CreateGoldButton("TCYes", ov, "消して始める", 22, new Vector2(-150, -80), new Vector2(250, 64),
            new Color(0.62f, 0.16f, 0.16f, 0.98f), () => { Destroy(_titleGO); _titleGO = null; RestartRun(); });
        ProtoUI.CreateGoldButton("TCNo", ov, "やめる", 22, new Vector2(150, -80), new Vector2(250, 64),
            new Color(0.3f, 0.3f, 0.4f, 0.98f), () => Destroy(ov.gameObject));
    }

    // 初期所持カードを在庫1ずつで設定
    void InitInitialCards()
    {
        OwnedCardIds.Clear(); CardStock.Clear();
        if (Cfg != null && Cfg.initialOwned != null)
            foreach (var id in Cfg.initialOwned)
                if (Db != null && Db.FindCard(id) != null) AddCard(id);
    }

    // セーブから状態を流し込む（ProtoSaveが呼ぶ）
    public void ApplyLoaded(int money, int cellStock, List<string> owned, List<int> counts, List<Vector2Int> unlocked, int equip = 0,
        List<string> growIds = null, List<int> growLevels = null)
    {
        Money = Mathf.Max(0, money);
        CellStock = Mathf.Max(0, cellStock);
        Equipped = System.Enum.IsDefined(typeof(EquipKind), equip) ? (EquipKind)equip : EquipKind.None;
        // カード成長を復元（盤面配置の復元より前に適用）
        Db?.ClearOverrides(); GrowthLevels.Clear();
        if (growIds != null && growLevels != null)
            for (int i = 0; i < growIds.Count && i < growLevels.Count; i++)
                if (!string.IsNullOrEmpty(growIds[i]) && growLevels[i] > 0) GrowthLevels[growIds[i]] = growLevels[i];
        ReapplyGrowth();
        if (unlocked != null && unlocked.Count > 0)
            foreach (var c in unlocked) Panel.Unlock(c.x, c.y);   // 解放マスを復元
        if (owned != null && owned.Count > 0)
        {
            OwnedCardIds.Clear(); CardStock.Clear();
            for (int i = 0; i < owned.Count; i++)
            {
                var id = owned[i];
                if (Db == null || Db.FindCard(id) == null) continue;
                if (!OwnedCardIds.Contains(id)) OwnedCardIds.Add(id);
                CardStock[id] = (counts != null && i < counts.Count) ? Mathf.Max(0, counts[i]) : 1;
                ProtoUnlocks.MarkDiscovered(id);   // ロード復元分も図鑑に登録
            }
        }
    }

    // マップの踏破状況をセーブ用に取得（MapScreenへ委譲）
    public void CaptureMap(List<int> cleared, out int curNode)
    {
        curNode = -1;
        if (_map != null) _map.CaptureRun(cleared, out curNode);
    }

    // ランの途中状態を復元（ProtoSaveが呼ぶ・v5以降）
    public void ApplyLoadedRun(int wave, int curHP, int maxHP, int depth, int ascension, int mapSeed, List<int> cleared, int curNode)
    {
        Wave = Mathf.Max(1, wave);
        CurrentDepth = Mathf.Max(1, depth);
        Ascension = Mathf.Clamp(ascension, 0, ProtoUnlocks.MaxAscension);
        if (maxHP > 0 && Stats != null) Stats.MaxHP = maxHP;   // 契約/神聖樹で増減した最大HPを復元
        if (curHP >= 0) CurrentHP = Mathf.Clamp(curHP, 1, MaxHP);
        if (mapSeed != 0)
        {
            MapSeed = mapSeed;
            _map?.RestoreRun(cleared, curNode);   // 同じマップを再生成して踏破状況を反映
        }
    }

    // オートセーブ（マス到達・イベント解決ごとにMapScreenが呼ぶ）
    public void AutoSaveRun() => ProtoSave.Save(this);

    void PlayBgm(AudioClip clip)
    {
        if (_bgmSource == null || clip == null) return;
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
        _bgmSource.clip = clip;
        if (BgmEnabled) _bgmSource.Play();
    }

    public void PlayMapBgm(int area) => PlayBgm(_fieldBgm);
    public void PlayShopBgm() => PlayBgm(_shopBgm);
    public void PlayTreeBgm() => PlayBgm(_treeBgm);
    public void PlayEvilBgm() => PlayBgm(_evilBgm);

    public void SetBgmEnabled(bool enabled, bool save = true)
    {
        BgmEnabled = enabled;
        if (save) PlayerPrefs.SetInt("bgm", enabled ? 1 : 0);
        if (_bgmSource == null) return;   // 初期化前（起動時のHide経由）は状態だけ保持
        if (enabled && !_bgmSource.isPlaying) _bgmSource.Play();
        else if (!enabled && _bgmSource.isPlaying) _bgmSource.Stop();
    }

    public void ShowMenu()
    {
        _bgImg.enabled = false;
        _battle.Hide(); _build.Hide(); _map.Hide();
        _menu.Show();
    }

    public void ShowMap()
    {
        _bgImg.enabled = false;
        _battle.Hide(); _build.Hide(); _menu.Hide();
        _map.Show();
    }

    // 最初から（すべてリセット：ステータス・Wave・お金・盤面・拡張・所持カード）
    public void RestartRun()
    {
        Ascension = ProtoUnlocks.Ascension;   // 「最初から」で選択中アセンションを反映
        NewMapSeed();
        ResetRunStats();
        Db?.ClearOverrides(); GrowthLevels.Clear();   // 成長もリセット
        CursedSeals = 0; PainContract = false; DemonHeart = false;   // 契約の呪いもリセット
        SoulVessel = false; Gluttony = false; Berserk = false;       // 大型契約もリセット
        Stats = new PlayerStats(Cfg);
        Stats.MaxHP = AscensionBaseHP();   // 難易度で最大HPを設定（VERY HARD/MASTERは低い）
        Equipped = EquipKind.None;   // 装備もリセット
        CurrentHP = MaxHP;   // 開始は満タン（難易度ペナルティは最大HPで表現）
        Wave = 1;
        Money = 0;
        CellStock = 0;
        Panel = new PanelModel(GridDim, GridDim);
        Panel.UnlockInitial(InitialCols, InitialRows);
        SetupCornerSpecials();   // 4隅は確定で特殊マス
        InitInitialCards();
        ProtoSave.Clear();   // セーブも消去（次回起動でも初期状態に）
        ProtoUnlocks.ClearDiscovered();   // 図鑑の発見記録もリセット
        foreach (var id in OwnedCardIds) ProtoUnlocks.MarkDiscovered(id);   // 初期カードだけ図鑑に登録し直す
        _map.ResetRun();
        ShowMap();
    }

    // ゲーム終了
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void ShowBuild()
    {
        _bgImg.enabled = false;
        _battle.Hide(); _map.Hide(); _menu.Hide();
        _build.Show();
    }

    EnemyDef _pendingEnemy;
    public EnemyDef CurrentEnemy => _pendingEnemy;

    public void StartBattle(EnemyDef enemy)
    {
        _pendingEnemy = enemy;
        _bgImg.enabled = true;
        _build.Hide(); _map.Hide(); _menu.Hide();
        _battle.Begin(enemy);
        // BGMは3系統：ボス（Wave末の3体）／中ボス／雑魚
        var bgm = _battleBgm;
        if (enemy != null)
        {
            if (enemy.id == "dragon" || enemy.id.StartsWith("boss_")) bgm = _bossBgm;
            else if (enemy.id.StartsWith("midboss_")) bgm = _midBossBgm;
        }
        PlayBgm(bgm);
    }

    // 戦闘勝利（報酬処理はProtoBattle側で完了済み）→ マップへ
    // ※Waveは「ボス撃破時のみ」MapScreen.OnEnemyDefeated内で加算する
    public void OnBattleWon()
    {
        _map.OnEnemyDefeated();
        ShowMap();
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.04f, 0.10f);
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }
}
