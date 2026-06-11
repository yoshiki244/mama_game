using UnityEngine;
using UnityEngine.EventSystems;

// プリプロ版の起動スクリプト。
// 空のGameObjectにこれを付けて再生するだけで、カメラ設定・Canvas・全UIを自動生成する。
public class ProtoMain : MonoBehaviour
{
    public PanelModel Panel { get; private set; }
    public int Wave { get; private set; } = 1;
    public Canvas Canvas { get; private set; }

    BuildScreen _build;
    ProtoBattle _battle;
    UnityEngine.UI.Image _bgImg;

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

        _build = gameObject.AddComponent<BuildScreen>();
        _battle = gameObject.AddComponent<ProtoBattle>();
        _build.Init(this);
        _battle.Init(this);
    }

    void Start() => ShowBuild();

    public void ShowBuild()
    {
        _bgImg.enabled = false; // ビルド画面は暗い背景
        _battle.Hide();
        _build.Show();
    }

    public void StartBattle()
    {
        _bgImg.enabled = true; // バトルは自然背景
        _build.Hide();
        _battle.Begin();
    }

    public void OnBattleWon()
    {
        Wave++;
        ShowBuild();
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
