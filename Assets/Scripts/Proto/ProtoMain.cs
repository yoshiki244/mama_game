using UnityEngine;
using UnityEngine.EventSystems;

// プリプロ版の起動スクリプト。
// 空のGameObjectにこれを付けて再生するだけで、カメラ設定・Canvas・全UIを自動生成する。
public class ProtoMain : MonoBehaviour
{
    // メンバーごとの個別ステータス（MemberStats[i] = Party[i] のもの）
    public System.Collections.Generic.List<PlayerStats> MemberStats { get; private set; }
        = new System.Collections.Generic.List<PlayerStats>();

    // 互換用: リーダー（MAMA）のステータス
    public PlayerStats Stats => MemberStats[0];
    public int Wave { get; private set; } = 1;
    public Canvas Canvas { get; private set; }
    public bool BgmEnabled { get; private set; }

    // ===== プロト検証用の戦闘設定（PlayerPrefsで永続化）=====
    public bool ChainEnabled { get; private set; }   // 通電連鎖 ON/OFF
    public bool ChainCorner { get; private set; }    // true=頂点対角接（ブロックス式）/ false=辺隣接
    public bool FlashEnabled { get; private set; }   // 順次点滅チャレンジ ON/OFF
    public int  FlashThreshold { get; private set; } // 順次点滅の発動マス数（10 or 15）
    public float ChainCritMult { get; private set; } // 通電クリティカル倍率
    public float FlashCritMult { get; private set; } // 順次点滅クリティカル倍率

    // パーティ（1人目はMAMA固定、最大3人）
    public System.Collections.Generic.List<PartyMember> Party { get; private set; }
        = new System.Collections.Generic.List<PartyMember>();

    // メンバーごとの盤面（形がキャラで違う）。Panels[i] = Party[i] の盤面
    public System.Collections.Generic.List<PanelModel> Panels { get; private set; }
        = new System.Collections.Generic.List<PanelModel>();

    public bool AddPartyMember()
    {
        if (Party.Count >= ProtoParty.MaxMembers) return false;
        Party.Add(ProtoParty.Roster[Party.Count]);
        Panels.Add(new PanelModel(ProtoParty.BoardMask(Panels.Count)));
        MemberStats.Add(new PlayerStats()); // 新加入はLv1から
        PlayerPrefs.SetInt("party", Party.Count);
        return true;
    }

    public bool RemovePartyMember()
    {
        if (Party.Count <= 1) return false; // リーダーは外せない
        Party.RemoveAt(Party.Count - 1);
        Panels.RemoveAt(Panels.Count - 1);
        MemberStats.RemoveAt(MemberStats.Count - 1);
        PlayerPrefs.SetInt("party", Party.Count);
        return true;
    }

    public void SetChainEnabled(bool on)   { ChainEnabled = on; PlayerPrefs.SetInt("chain", on ? 1 : 0); }
    public void SetChainCorner(bool on)    { ChainCorner = on;  PlayerPrefs.SetInt("chainCorner", on ? 1 : 0); }
    public void SetFlashEnabled(bool on)   { FlashEnabled = on; PlayerPrefs.SetInt("flash", on ? 1 : 0); }
    public void SetFlashThreshold(int n)   { FlashThreshold = n; PlayerPrefs.SetInt("flashTh", n); }
    public void SetChainCritMult(float m)  { ChainCritMult = Mathf.Clamp(m, 1.0f, 2.0f); PlayerPrefs.SetFloat("chainMult", ChainCritMult); }
    public void SetFlashCritMult(float m)  { FlashCritMult = Mathf.Clamp(m, 1.0f, 2.0f); PlayerPrefs.SetFloat("flashMult", FlashCritMult); }

    BuildScreen _build;
    ProtoBattle _battle;
    MapScreen _map;
    MenuScreen _menu;
    UnityEngine.UI.Image _bgImg;
    AudioSource _bgmSource;
    AudioClip _fieldBgm, _battleBgm, _stormBgm, _bossBgm;

    public void SetWave(int wave) => Wave = wave;

    // 互換用: リーダー（MAMA）の盤面
    public PanelModel Panel => Panels[0];

    void Awake()
    {

        SetupCamera();
        Canvas = ProtoUI.CreateCanvas();
        EnsureEventSystem();

        // 自然背景（最初に作る=一番後ろに描画される）。バトル画面でのみ表示
        var bgRt = ProtoUI.CreateFullScreen("Background", Canvas.transform);
        _bgImg = bgRt.gameObject.AddComponent<UnityEngine.UI.Image>();
        _bgImg.sprite = ProtoPixelArt.NatureBackground();
        _bgImg.color = new Color(0.85f, 0.9f, 0.9f); // 少し落ち着かせてUIを読みやすく
        _bgImg.raycastTarget = false;


        _build = gameObject.AddComponent<BuildScreen>();
        _battle = gameObject.AddComponent<ProtoBattle>();
        _map = gameObject.AddComponent<MapScreen>();
        _menu = gameObject.AddComponent<MenuScreen>();
        _build.Init(this);
        _battle.Init(this);
        _map.Init(this);
        _menu.Init(this);

        // 設定の復元（プロト検証用の戦闘設定）
        ChainEnabled = PlayerPrefs.GetInt("chain", 1) == 1;
        ChainCorner  = PlayerPrefs.GetInt("chainCorner", 0) == 1;
        FlashEnabled = PlayerPrefs.GetInt("flash", 1) == 1;
        FlashThreshold = PlayerPrefs.GetInt("flashTh", 10);
        ChainCritMult = PlayerPrefs.GetFloat("chainMult", 1.3f);
        FlashCritMult = PlayerPrefs.GetFloat("flashMult", 1.3f);

        // パーティの復元（最低1人=MAMA）。盤面もメンバーごとの形で生成
        // ※セーブの盤面復元より先にやる必要がある
        int partyCount = Mathf.Clamp(PlayerPrefs.GetInt("party", 1), 1, ProtoParty.MaxMembers);
        for (int i = 0; i < partyCount; i++)
        {
            Party.Add(ProtoParty.Roster[i]);
            Panels.Add(new PanelModel(ProtoParty.BoardMask(i)));
            MemberStats.Add(new PlayerStats());
        }

        // セーブデータがあれば復元（ステータス・Wave・盤面）
        ProtoSave.Load(this);

        // BGM（コード生成チップチューン）と音量の復元
        AudioListener.volume = PlayerPrefs.GetFloat("volume", 0.8f);
        _fieldBgm = ProtoAudio.CreateBgm();
        _battleBgm = ProtoAudio.CreateBattleBgm();
        _stormBgm = ProtoAudio.CreateStormBgm();
        _bossBgm = ProtoAudio.CreateBossBgm();
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.clip = _fieldBgm;
        _bgmSource.loop = true;
        SetBgmEnabled(PlayerPrefs.GetInt("bgm", 1) == 1);
    }

    // 画面に合わせてBGMを切り替える（同じ曲なら何もしない）
    void PlayBgm(AudioClip clip)
    {
        if (_bgmSource == null || clip == null) return; // 起動初期化中はまだBGMが無い
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
        _bgmSource.clip = clip;
        if (BgmEnabled) _bgmSource.Play();
    }

    // マップのエリアに応じたBGM（嵐の山頂=緊迫した曲）。MapScreenから呼ばれる
    public void PlayMapBgm(int area)
    {
        PlayBgm(area >= 2 ? _stormBgm : _fieldBgm);
    }

    void Start() => ShowMap();

    public void SetBgmEnabled(bool enabled)
    {
        BgmEnabled = enabled;
        PlayerPrefs.SetInt("bgm", enabled ? 1 : 0);
        if (enabled && !_bgmSource.isPlaying) _bgmSource.Play();
        else if (!enabled && _bgmSource.isPlaying) _bgmSource.Stop();
    }

    // メニュー画面（マップから開く）
    public void ShowMenu()
    {
        _bgImg.enabled = false;
        _battle.Hide();
        _build.Hide();
        _map.Hide();
        _menu.Show();
    }

    // マップ画面（ゲームのホーム。ここから歩いてエンカウント）
    public void ShowMap()
    {
        _bgImg.enabled = false; // マップは自前の背景を持つ
        _battle.Hide();
        _build.Hide();
        _menu.Hide();
        _map.Show(); // 中で現在エリアに応じたBGM（草原=フィールド/山頂=嵐）を再生する
    }

    // ビルド画面（メニューから開く）
    public void ShowBuild()
    {
        _bgImg.enabled = false;
        _battle.Hide();
        _map.Hide();
        _menu.Hide();
        _build.Show();
    }

    // バトル開始（マップでぶつかった敵のデータを渡す）
    public void StartBattle(ProtoEnemy enemy)
    {
        _bgImg.enabled = true; // バトルは自然背景
        _build.Hide();
        _map.Hide();
        _battle.Begin(enemy);
        // ボス（鬼・ドラゴンなど levelOffset を持つ強敵）はシリアスな専用BGM
        PlayBgm(enemy.levelOffset > 0 ? _bossBgm : _battleBgm);
    }

    public void OnBattleWon()
    {
        Wave++;
        _map.OnEnemyDefeated(); // 倒した敵をマップから消して補充
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
