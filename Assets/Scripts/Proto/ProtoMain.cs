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
    public int MaxMana => BoardCells / 10 + (Equipped == EquipKind.ManaPendant ? 1 : 0); // マナ＝盤面÷10＋装備
    public EquipKind Equipped { get; private set; } = EquipKind.None; // 装備（1つだけ）
    // 生命のペンダントで最大HP+10%
    public int MaxHP => Stats == null ? 0 : Stats.MaxHP + (Equipped == EquipKind.LifePendant ? Mathf.RoundToInt(Stats.MaxHP * 0.10f) : 0);
    public void SetEquip(EquipKind e) { Equipped = e; if (CurrentHP > MaxHP) CurrentHP = MaxHP; }
    // ショップで装備購入（1つだけ所持・所持金が足りれば）
    public bool BuyEquip(EquipKind e)
    {
        if (e == EquipKind.None || Equipped != EquipKind.None || Money < EquipInfo.ShopPrice) return false;
        Money -= EquipInfo.ShopPrice;
        SetEquip(e);
        return true;
    }
    public int Wave { get; private set; } = 1;
    public int CurrentDepth { get; set; } = 1; // 現在地の深度（マップの列番号）。報酬/ショップの抽選に使う

    public List<string> OwnedCardIds { get; private set; } = new List<string>();      // 所持したことのあるカードid（種類）
    public Dictionary<string, int> CardStock { get; private set; } = new Dictionary<string, int>(); // 未配置の在庫数

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
    public void AddMoney(int amount) => Money = Mathf.Max(0, Money + amount);

    // ---- HP（戦闘間で継続） ----
    public void SetCurrentHP(int hp) => CurrentHP = Mathf.Clamp(hp, 0, Stats != null ? MaxHP : hp);
    public void HealFull() { if (Stats != null) CurrentHP = MaxHP; }

    // ---- 盤面マス入手・解放 ----
    public void AwardCells(int n) { if (n > 0) CellStock += n; }

    // 契約：最大HPを10払って マスストック+1（最大HPは最低10まで）
    public bool ContractTradeHpForCell()
    {
        if (Stats == null || Stats.MaxHP - 10 < 10) return false;
        Stats.MaxHP -= 10;
        if (CurrentHP > MaxHP) CurrentHP = MaxHP;
        CellStock += 1;
        return true;
    }

    // ビルド画面でロック中のマスを解放（マスストックを1消費・最大100まで）
    public bool UnlockCell(int x, int y)
    {
        if (CellStock <= 0) return false;
        if (Panel.UnlockedCount() >= MaxCells) return false;
        if (!Panel.Unlock(x, y)) return false;
        CellStock--;
        return true;
    }

    // 配置セッション開始時の状態(keep)まで戻し、その間に解放したマスをマスストックへ払い戻す
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

    // ---- 画面 ----
    BuildScreen _build;
    ProtoBattle _battle;
    MapScreen _map;
    MenuScreen _menu;
    UnityEngine.UI.Image _bgImg;
    AudioSource _bgmSource;
    AudioClip _fieldBgm, _battleBgm, _bossBgm;

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

        // プレイヤー初期化
        Stats = new PlayerStats(Cfg);
        Equipped = EquipKind.None;
        CurrentHP = MaxHP;
        Money = 0;
        CellStock = 0;
        Panel = new PanelModel(GridDim, GridDim);
        Panel.UnlockInitial(InitialCols, InitialRows);

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

        // BGM
        AudioListener.volume = PlayerPrefs.GetFloat("volume", 0.8f);
        _fieldBgm = ProtoAudio.CreateBgm();
        _battleBgm = ProtoAudio.CreateBattleBgm();
        _bossBgm = ProtoAudio.CreateBossBgm();
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.clip = _fieldBgm;
        _bgmSource.loop = true;
        SetBgmEnabled(PlayerPrefs.GetInt("bgm", 1) == 1);
    }

    void Start() => ShowMap();

    // 初期所持カードを在庫1ずつで設定
    void InitInitialCards()
    {
        OwnedCardIds.Clear(); CardStock.Clear();
        if (Cfg != null && Cfg.initialOwned != null)
            foreach (var id in Cfg.initialOwned)
                if (Db != null && Db.FindCard(id) != null) AddCard(id);
    }

    // セーブから状態を流し込む（ProtoSaveが呼ぶ）
    public void ApplyLoaded(int money, int cellStock, List<string> owned, List<int> counts, List<Vector2Int> unlocked, int equip = 0)
    {
        Money = Mathf.Max(0, money);
        CellStock = Mathf.Max(0, cellStock);
        Equipped = System.Enum.IsDefined(typeof(EquipKind), equip) ? (EquipKind)equip : EquipKind.None;
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
            }
        }
    }

    void PlayBgm(AudioClip clip)
    {
        if (_bgmSource == null || clip == null) return;
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
        _bgmSource.clip = clip;
        if (BgmEnabled) _bgmSource.Play();
    }

    public void PlayMapBgm(int area) => PlayBgm(_fieldBgm);

    public void SetBgmEnabled(bool enabled)
    {
        BgmEnabled = enabled;
        PlayerPrefs.SetInt("bgm", enabled ? 1 : 0);
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
        Stats = new PlayerStats(Cfg);
        Equipped = EquipKind.None;   // 装備もリセット
        CurrentHP = MaxHP;
        Wave = 1;
        Money = 0;
        CellStock = 0;
        Panel = new PanelModel(GridDim, GridDim);
        Panel.UnlockInitial(InitialCols, InitialRows);
        InitInitialCards();
        ProtoSave.Clear();   // セーブも消去（次回起動でも初期状態に）
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
        PlayBgm(enemy != null && enemy.levelOffset > 0 ? _bossBgm : _battleBgm);
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
