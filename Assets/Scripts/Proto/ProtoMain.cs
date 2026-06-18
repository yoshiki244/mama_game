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
    public PlayerStats Stats { get; private set; }
    public PanelModel Panel { get; private set; }
    public int Money { get; private set; }
    public int Expansions { get; private set; }   // 盤面拡張回数 0..5
    public int BoardSize => 5 + Expansions;        // 5×5 → 10×10
    public int MaxMana => (Cfg != null ? Cfg.baseMana : 3) + Expansions;
    public int Wave { get; private set; } = 1;
    public int CurrentDepth { get; set; } = 1; // 現在地の深度（マップの列番号）。報酬/ショップの抽選に使う

    public List<string> OwnedCardIds { get; private set; } = new List<string>();

    public Canvas Canvas { get; private set; }
    public bool BgmEnabled { get; private set; }

    // ---- 所持カード操作 ----
    public List<CardDef> OwnedCards()
    {
        var list = new List<CardDef>();
        if (Db == null) return list;
        foreach (var id in OwnedCardIds)
        {
            var c = Db.FindCard(id);
            if (c != null) list.Add(c);
        }
        return list;
    }

    public bool OwnsCard(string id) => OwnedCardIds.Contains(id);

    public bool AddCard(string id)
    {
        if (string.IsNullOrEmpty(id) || OwnedCardIds.Contains(id)) return false;
        if (Db.FindCard(id) == null) return false;
        OwnedCardIds.Add(id);
        return true;
    }

    // ---- 経済 ----
    public void AddMoney(int amount) => Money = Mathf.Max(0, Money + amount);

    public int NextExpansionCost()
        => (Cfg != null && Expansions < Cfg.expansionCosts.Length) ? Cfg.expansionCosts[Expansions] : -1;

    public bool CanExpand() => Expansions < 5 && NextExpansionCost() >= 0 && Money >= NextExpansionCost();

    public bool ExpandBoard()
    {
        if (!CanExpand()) return false;
        Money -= NextExpansionCost();
        Expansions++;
        Panel.Resize(BoardSize, BoardSize);
        return true;
    }

    public bool BuyCard(string id)
    {
        int price = Cfg != null ? Cfg.shopBuyPrice : 40;
        if (OwnedCardIds.Contains(id) || Money < price || Db.FindCard(id) == null) return false;
        Money -= price;
        OwnedCardIds.Add(id);
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
        Expansions = 0;
        Money = 0;
        Panel = new PanelModel(BoardSize, BoardSize);

        // 初期所持カード
        OwnedCardIds.Clear();
        if (Cfg != null && Cfg.initialOwned != null)
            foreach (var id in Cfg.initialOwned)
                if (Db != null && Db.FindCard(id) != null && !OwnedCardIds.Contains(id))
                    OwnedCardIds.Add(id);

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

    // セーブから状態を流し込む（ProtoSaveが呼ぶ）
    public void ApplyLoaded(int money, int expansions, List<string> owned)
    {
        Money = Mathf.Max(0, money);
        Expansions = Mathf.Clamp(expansions, 0, 5);
        Panel.Resize(BoardSize, BoardSize);
        if (owned != null && owned.Count > 0)
        {
            OwnedCardIds.Clear();
            foreach (var id in owned)
                if (Db != null && Db.FindCard(id) != null && !OwnedCardIds.Contains(id))
                    OwnedCardIds.Add(id);
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

    // ゲームオーバーから最初(Wave1・最初のマス)へやり直す
    public void RestartRun()
    {
        Stats = new PlayerStats(Cfg);
        Wave = 1;
        Money = 0;
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
    public void OnBattleWon()
    {
        Wave++;
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
