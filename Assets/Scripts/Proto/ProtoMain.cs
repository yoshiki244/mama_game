using UnityEngine;
using UnityEngine.EventSystems;

// プリプロ版の起動スクリプト。
// 空のGameObjectにこれを付けて再生するだけで、カメラ設定・Canvas・全UIを自動生成する。
public class ProtoMain : MonoBehaviour
{
    public PanelModel Panel { get; private set; }
    public PlayerStats Stats { get; private set; }
    public int Wave { get; private set; } = 1;
    public Canvas Canvas { get; private set; }
    public bool BgmEnabled { get; private set; }

    BuildScreen _build;
    ProtoBattle _battle;
    MapScreen _map;
    MenuScreen _menu;
    UnityEngine.UI.Image _bgImg;
    AudioSource _bgmSource;
    AudioClip _fieldBgm, _battleBgm;

    public void SetWave(int wave) => Wave = wave;

    void Awake()
    {
        Panel = new PanelModel(10, 10);

        SetupCamera();
        Canvas = ProtoUI.CreateCanvas();
        EnsureEventSystem();

        // 自然背景（最初に作る=一番後ろに描画される）。バトル画面でのみ表示
        var bgRt = ProtoUI.CreateFullScreen("Background", Canvas.transform);
        _bgImg = bgRt.gameObject.AddComponent<UnityEngine.UI.Image>();
        _bgImg.sprite = ProtoPixelArt.NatureBackground();
        _bgImg.color = new Color(0.85f, 0.9f, 0.9f); // 少し落ち着かせてUIを読みやすく
        _bgImg.raycastTarget = false;

        Stats = new PlayerStats();

        _build = gameObject.AddComponent<BuildScreen>();
        _battle = gameObject.AddComponent<ProtoBattle>();
        _map = gameObject.AddComponent<MapScreen>();
        _menu = gameObject.AddComponent<MenuScreen>();
        _build.Init(this);
        _battle.Init(this);
        _map.Init(this);
        _menu.Init(this);

        // セーブデータがあれば復元（ステータス・Wave・盤面）
        ProtoSave.Load(this);

        // BGM（コード生成チップチューン）と音量の復元
        AudioListener.volume = PlayerPrefs.GetFloat("volume", 0.8f);
        _fieldBgm = ProtoAudio.CreateBgm();
        _battleBgm = ProtoAudio.CreateBattleBgm();
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.clip = _fieldBgm;
        _bgmSource.loop = true;
        SetBgmEnabled(PlayerPrefs.GetInt("bgm", 1) == 1);
    }

    // 画面に合わせてBGMを切り替える（同じ曲なら何もしない）
    void PlayBgm(AudioClip clip)
    {
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
        _bgmSource.clip = clip;
        if (BgmEnabled) _bgmSource.Play();
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
        _map.Show();
        PlayBgm(_fieldBgm);
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
        PlayBgm(_battleBgm); // 迫力のあるバトル曲へ
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
