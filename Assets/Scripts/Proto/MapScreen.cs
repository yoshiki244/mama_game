using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// マップ画面: ポケモン式のマス目移動。3つのエリアを縦に移動する。
//   エリア1「草原」    … 弱い敵。ここでレベル上げ
//   エリア2「風雨の森」… 雨が降る暗い森。強めの敵＋中ボス（キングゴーレム）
//   エリア3「嵐の山頂」… 岩場と階段。頂上にボスのドラゴン（高レベル）
// 上端中央のひらけた道から次のエリアへ、下端から前のエリアへ戻れる。
public class MapScreen : MonoBehaviour
{
    ProtoMain _main;
    RectTransform _root;
    RectTransform _player;

    bool _busy;    // エンカウント演出中
    bool _moving;  // 1マス移動アニメ中

    // グリッド設定
    const float TileSize = 72f;
    const int GridMinX = -9, GridMaxX = 9;   // 横19マス
    const int GridMinY = -5, GridMaxY = 3;   // 縦9マス
    const int TexTilesX = 21, TexTilesY = 12;

    // エリア設定
    int _area;
    static readonly string[] AreaNames = { "草原", "風雨の森", "嵐の山頂" };
    static readonly int[] AreaEnemyCount = { 6, 5, 2 };

    Vector2Int _gridPos;
    Image _playerImg;
    Sprite[][] _walkSprites;     // [向き(0正面/1背面/2左)][コマ(0/1)]
    int _facing;                 // 0=下 1=上 2=左 3=右

    Image _fieldImg;
    Image _darkOverlay;
    TMPro.TextMeshProUGUI _areaLabel;

    // 雨（エリア2・3で降る）
    RectTransform _rainContainer;
    readonly List<RectTransform> _rainStreaks = new List<RectTransform>();

    // 雷（ボスエリアのみ）
    Image _lightningFlash;
    float _lightningTimer;
    AudioSource _sfx;
    AudioClip _thunderClip;

    // 仲間の隊列追従（プレイヤーの過去位置を辿る）
    class Follower
    {
        public RectTransform rt;
        public Image img;
        public Sprite[][] sprites;
        public Vector2Int gpos;
    }
    readonly List<Follower> _followers = new List<Follower>();
    readonly List<Vector2Int> _trail = new List<Vector2Int>();

    class MapEnemy
    {
        public ProtoEnemy data;
        public Vector2Int gpos;
        public RectTransform rt;
        public float wanderTimer;
        public bool isBoss; // ボスはうろつかない・倒しても補充されない
    }

    readonly List<MapEnemy> _enemies = new List<MapEnemy>();
    readonly HashSet<Vector2Int> _trees = new HashSet<Vector2Int>(); // 通行不可マス
    MapEnemy _engaged;

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        LoadArea(0, spawnAt: new Vector2Int(0, -2));
        Hide();
    }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        _busy = false;
        _moving = false;
        RefreshFollowers();
        _main.PlayMapBgm(_area);
    }

    public void Hide()
    {
        StopAllCoroutines();
        _moving = false;
        _root.gameObject.SetActive(false);
    }

    // バトル勝利時: 倒した敵を消す。ボスは補充しない
    public void OnEnemyDefeated()
    {
        if (_engaged != null)
        {
            bool wasBoss = _engaged.isBoss;
            Destroy(_engaged.rt.gameObject);
            _enemies.Remove(_engaged);
            _engaged = null;
            if (!wasBoss) SpawnRandomEnemy();
        }
    }

    // ==================== UI構築 ====================

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MapScreen", _main.Canvas.transform);
        var backdrop = _root.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0.07f, 0.11f, 0.07f);

        _gridPos = new Vector2Int(0, -2);

        // フィールド画像（中身はLoadAreaで差し替える）
        var field = ProtoUI.CreateRect("Field", _root);
        field.sizeDelta = new Vector2(TexTilesX * TileSize, TexTilesY * TileSize);
        field.anchoredPosition = new Vector2(0, -1.5f * TileSize - 30f);
        _fieldImg = field.gameObject.AddComponent<Image>();
        _fieldImg.raycastTarget = false;

        // 歩行スプライト
        _walkSprites = new Sprite[3][];
        for (int d = 0; d < 3; d++)
            _walkSprites[d] = new[] { ProtoPixelArt.MapMama(d, 0), ProtoPixelArt.MapMama(d, 1) };

        // プレイヤー
        _player = ProtoUI.CreateRect("Player", _root);
        _player.sizeDelta = new Vector2(78, 95);
        _player.anchoredPosition = GridToAnchored(_gridPos);
        AddShadow(_player, 46f);
        _playerImg = _player.gameObject.AddComponent<Image>();
        _playerImg.sprite = _walkSprites[0][0];
        _playerImg.preserveAspect = true;

        // 暗さオーバーレイ（荒れたエリアほど暗くなる）
        var overlayRt = ProtoUI.CreateFullScreen("DarkOverlay", _root);
        _darkOverlay = overlayRt.gameObject.AddComponent<Image>();
        _darkOverlay.color = Color.clear;
        _darkOverlay.raycastTarget = false;

        // 雨（細い線を降らせる）
        _rainContainer = ProtoUI.CreateFullScreen("Rain", _root);
        for (int i = 0; i < 50; i++)
        {
            var streak = ProtoUI.CreateRect($"Drop{i}", _rainContainer);
            streak.sizeDelta = new Vector2(2, 22);
            streak.localRotation = Quaternion.Euler(0, 0, 8f); // 風で少し斜めに
            streak.anchoredPosition = new Vector2(Random.Range(-820f, 820f), Random.Range(-470f, 470f));
            var img = streak.gameObject.AddComponent<Image>();
            img.color = new Color(0.72f, 0.82f, 1f, 0.38f);
            img.raycastTarget = false;
            _rainStreaks.Add(streak);
        }
        _rainContainer.gameObject.SetActive(false);

        // 雷の画面フラッシュ（ボスエリア用。普段は透明）
        var flashRt = ProtoUI.CreateFullScreen("LightningFlash", _root);
        _lightningFlash = flashRt.gameObject.AddComponent<Image>();
        _lightningFlash.color = Color.clear;
        _lightningFlash.raycastTarget = false;

        // 雷鳴の音源
        _sfx = gameObject.AddComponent<AudioSource>();
        _thunderClip = ProtoAudio.CreateThunder();

        // 下部のUIバー
        var bottomBar = ProtoUI.CreatePanel("BottomBar", _root, new Vector2(0, -424), new Vector2(1700, 56),
            new Color(0.05f, 0.04f, 0.10f, 0.92f));
        bottomBar.raycastTarget = false;
        ProtoUI.CreatePanel("BottomBarLine", _root, new Vector2(0, -396), new Vector2(1700, 2),
            new Color(0.85f, 0.72f, 0.4f, 0.7f)).raycastTarget = false;

        ProtoUI.CreateText("Hint", _root,
            "WASD / 矢印キー = 移動　　上端のひらけた道 = 次のエリアへ　　B = メニュー", 17,
            new Vector2(60, -424), new Vector2(1100, 30));
        ProtoUI.CreateButton("MenuBtn", _root, "メニュー", 18,
            new Vector2(-700, -424), new Vector2(150, 42),
            new Color(0.3f, 0.25f, 0.45f), () => _main.ShowMenu());

        // エリア名表示（左上）
        var labelBg = ProtoUI.CreatePanel("AreaLabelBg", _root, new Vector2(-660, 415), new Vector2(260, 44),
            new Color(0.05f, 0.04f, 0.10f, 0.85f));
        labelBg.raycastTarget = false;
        _areaLabel = ProtoUI.CreateText("AreaLabel", _root, "", 19, new Vector2(-660, 415), new Vector2(250, 36));
        ProtoUI.StyleTitle(_areaLabel, ProtoUI.Gold, 2f);
    }

    // ==================== エリアの構築 ====================

    void LoadArea(int area, Vector2Int spawnAt)
    {
        _area = area;
        _gridPos = spawnAt;

        // 敵を全消去
        foreach (var e in _enemies) Destroy(e.rt.gameObject);
        _enemies.Clear();
        _engaged = null;

        // 通行不可マスとフィールドの絵を作る
        _trees.Clear();
        var treeMap = new bool[TexTilesX, TexTilesY];
        bool[,] pathMap = null;

        bool topOpen = area < 2;    // 上端の出口（次のエリアへ）
        bool bottomOpen = area > 0; // 下端の出口（前のエリアへ）

        // 外周の壁（出口部分はひらけている）
        for (int gx = -10; gx <= 10; gx++)
        {
            for (int gy = -7; gy <= 4; gy++)
            {
                bool isBorder = gx <= GridMinX - 1 || gx >= GridMaxX + 1 || gy <= GridMinY - 1 || gy >= GridMaxY + 1;
                if (!isBorder) continue;
                bool inTopGate = topOpen && gy >= GridMaxY + 1 && Mathf.Abs(gx) <= 1;
                bool inBottomGate = bottomOpen && gy <= GridMinY - 1 && Mathf.Abs(gx) <= 1;
                if (inTopGate || inBottomGate) continue;
                treeMap[gx + 10, 4 - gy] = true;
            }
        }

        // 内側の木/岩（スタート地点と出口前は空ける）
        var rng = new System.Random(11 + area * 31);
        int wantTrees = area == 2 ? 8 : area == 1 ? 14 : 12;
        int planted = 0, guard = 0;
        while (planted < wantTrees && guard++ < 300)
        {
            var g = new Vector2Int(rng.Next(GridMinX, GridMaxX + 1), rng.Next(GridMinY, GridMaxY + 1));
            if (_trees.Contains(g)) continue;
            if (Mathf.Abs(g.x - spawnAt.x) + Mathf.Abs(g.y - spawnAt.y) < 3) continue;
            if (Mathf.Abs(g.x) <= 1) continue; // 中央の通り道（出口ルート）は塞がない
            _trees.Add(g);
            treeMap[g.x + 10, 4 - g.y] = true;
            planted++;
        }

        // 山頂エリア: 頂上へ続く石の階段
        if (area == 2)
        {
            pathMap = new bool[TexTilesX, TexTilesY];
            for (int gy = -1; gy <= 4; gy++)
                for (int gx = -1; gx <= 1; gx++)
                    pathMap[gx + 10, 4 - gy] = true;
        }

        _fieldImg.sprite = ProtoPixelArt.TopDownField(TexTilesX, TexTilesY, treeMap, 11 + area * 31, area, pathMap);

        // 環境表現: エリアが進むほど暗く、雨が降る
        _darkOverlay.color = area == 0 ? Color.clear
            : area == 1 ? new Color(0.02f, 0.02f, 0.10f, 0.22f)
            : new Color(0.03f, 0.03f, 0.12f, 0.32f);
        _rainContainer.gameObject.SetActive(area >= 1);

        _areaLabel.text = $"エリア{area + 1}　{AreaNames[area]}";
        _lightningTimer = Random.Range(2f, 5f);
        _main.PlayMapBgm(area); // 山頂では緊迫したBGMに

        // 通常の敵を配置
        for (int i = 0; i < AreaEnemyCount[area]; i++) SpawnRandomEnemy();

        // ボス配置
        if (area == 1)
            SpawnEnemyAt(ProtoEnemies.Find("oni"), new Vector2Int(0, 1), isBoss: true);  // 中ボス: 鬼
        if (area == 2)
            SpawnEnemyAt(ProtoEnemies.Find("dragon"), new Vector2Int(0, GridMaxY), isBoss: true); // 階段の頂上にドラゴン

        // プレイヤーと隊列を配置し直す
        _player.anchoredPosition = GridToAnchored(_gridPos);
        _trail.Clear();
        foreach (var f in _followers)
        {
            f.gpos = _gridPos;
            f.rt.anchoredPosition = GridToAnchored(_gridPos);
        }
    }

    // エリア間の移動
    void TransitionArea(int newArea, bool enterFromBottom)
    {
        var spawn = enterFromBottom ? new Vector2Int(0, GridMinY) : new Vector2Int(0, GridMaxY);
        LoadArea(newArea, spawn);
    }

    // パーティ2人目以降の追従キャラを生成
    void RefreshFollowers()
    {
        foreach (var f in _followers) Destroy(f.rt.gameObject);
        _followers.Clear();
        _trail.Clear();

        var party = _main.Party;
        for (int i = 1; i < party.Count; i++)
        {
            var m = party[i];
            var sprites = new Sprite[3][];
            for (int d = 0; d < 3; d++)
                sprites[d] = new[] { m.MapSprite(d, 0), m.MapSprite(d, 1) };

            var rt = ProtoUI.CreateRect($"Follower_{m.name}", _root);
            rt.sizeDelta = new Vector2(78, 95);
            rt.anchoredPosition = GridToAnchored(_gridPos);
            AddShadow(rt, 46f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprites[0][0];
            img.preserveAspect = true;
            img.raycastTarget = false;

            _followers.Add(new Follower { rt = rt, img = img, sprites = sprites, gpos = _gridPos });
            rt.SetSiblingIndex(_player.GetSiblingIndex());
        }
    }

    Vector2 GridToAnchored(Vector2Int g)
        => new Vector2(g.x * TileSize, g.y * TileSize - 30f);

    // 足元の影（楕円風）
    void AddShadow(RectTransform parent, float width)
    {
        var shadow = ProtoUI.CreateRect("Shadow", parent);
        shadow.anchoredPosition = new Vector2(0, -parent.sizeDelta.y / 2f + 4f);
        shadow.sizeDelta = new Vector2(width, 13f);
        shadow.localRotation = Quaternion.Euler(0, 0, 45);
        shadow.localScale = new Vector3(1f, 0.35f, 1f);
        var img = shadow.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.28f);
        img.raycastTarget = false;
    }

    // ==================== 敵のスポーン ====================

    void SpawnRandomEnemy()
    {
        var data = ProtoEnemies.RandomEnemy(_area);

        Vector2Int g;
        int guard = 0;
        do
        {
            g = new Vector2Int(Random.Range(GridMinX, GridMaxX + 1), Random.Range(GridMinY, GridMaxY + 1));
            guard++;
        } while ((IsOccupied(g) || _trees.Contains(g)
                  || Mathf.Abs(g.x - _gridPos.x) + Mathf.Abs(g.y - _gridPos.y) < 3) && guard < 100);

        SpawnEnemyAt(data, g, isBoss: false);
    }

    void SpawnEnemyAt(ProtoEnemy data, Vector2Int g, bool isBoss)
    {
        var rt = ProtoUI.CreateRect($"Enemy_{data.id}", _root);
        rt.sizeDelta = isBoss ? data.mapSize * 1.25f : data.mapSize; // ボスはひと回り大きく
        rt.anchoredPosition = GridToAnchored(g);
        AddShadow(rt, rt.sizeDelta.x * 0.6f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = data.sprite;
        img.preserveAspect = true;
        rt.SetSiblingIndex(_player.GetSiblingIndex()); // プレイヤーやオーバーレイより後ろに

        _enemies.Add(new MapEnemy
        {
            data = data,
            gpos = g,
            rt = rt,
            wanderTimer = Random.Range(1.5f, 3.5f),
            isBoss = isBoss,
        });
    }

    bool IsOccupied(Vector2Int g)
    {
        foreach (var e in _enemies)
            if (e.gpos == g) return true;
        return g == _gridPos;
    }

    MapEnemy EnemyAt(Vector2Int g)
    {
        foreach (var e in _enemies)
            if (e.gpos == g) return e;
        return null;
    }

    // ==================== 更新 ====================

    void Update()
    {
        if (_root == null || !_root.gameObject.activeSelf) return;

        UpdateRain(); // 雨はいつでも降り続ける
        UpdateLightning(); // ボスエリアでは雷が落ちる

        if (_busy) return;

        UpdateEnemies();

        if (_moving) return;

        // 入力（上下左右のみ）
        Vector2Int dir = Vector2Int.zero;
        bool buildKey = false;

#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.bKey.wasPressedThisFrame) buildKey = true;
            else if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir = Vector2Int.up;
            else if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir = Vector2Int.down;
            else if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir = Vector2Int.left;
            else if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir = Vector2Int.right;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!buildKey && Input.GetKeyDown(KeyCode.B)) buildKey = true;
        if (dir == Vector2Int.zero)
        {
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) dir = Vector2Int.up;
            else if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) dir = Vector2Int.down;
            else if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) dir = Vector2Int.left;
            else if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) dir = Vector2Int.right;
        }
#endif

        if (buildKey)
        {
            _main.ShowMenu();
            return;
        }
        if (dir == Vector2Int.zero) return;

        _facing = dir == Vector2Int.down ? 0 : dir == Vector2Int.up ? 1 : dir == Vector2Int.left ? 2 : 3;
        ApplyPlayerSprite(0);

        Vector2Int target = _gridPos + dir;

        // 出口判定: 上端中央から次のエリアへ / 下端中央から前のエリアへ
        if (target.y > GridMaxY)
        {
            if (_area < 2 && Mathf.Abs(_gridPos.x) <= 1)
                TransitionArea(_area + 1, enterFromBottom: true);
            return;
        }
        if (target.y < GridMinY)
        {
            if (_area > 0 && Mathf.Abs(_gridPos.x) <= 1)
                TransitionArea(_area - 1, enterFromBottom: false);
            return;
        }

        if (target.x < GridMinX || target.x > GridMaxX) return;
        if (_trees.Contains(target)) return;

        var enemy = EnemyAt(target);
        if (enemy != null)
        {
            StartCoroutine(Encounter(enemy));
            return;
        }

        StartCoroutine(StepTo(target));
    }

    // ボスエリアの雷: 数秒おきに稲妻が落ち、画面が一瞬光って雷鳴が轟く
    void UpdateLightning()
    {
        if (_area < 2) return;
        _lightningTimer -= Time.deltaTime;
        if (_lightningTimer > 0f) return;
        _lightningTimer = Random.Range(3.5f, 8f); // 次の雷まで
        StartCoroutine(LightningStrike());
    }

    IEnumerator LightningStrike()
    {
        // 稲妻の形: 上空からジグザグに落ちる細い光の線分
        float x = Random.Range(-600f, 600f);
        float y = 470f;
        var segments = new List<Image>();
        int count = Random.Range(4, 7);
        for (int i = 0; i < count; i++)
        {
            float nextX = x + Random.Range(-70f, 70f);
            float nextY = y - Random.Range(80f, 140f);
            var seg = ProtoUI.CreateRect("Bolt" + i, _root);
            Vector2 mid = new Vector2((x + nextX) / 2f, (y + nextY) / 2f);
            Vector2 d = new Vector2(nextX - x, nextY - y);
            seg.anchoredPosition = mid;
            seg.sizeDelta = new Vector2(4f, d.magnitude);
            seg.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
            var img = seg.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 0.85f, 0.95f);
            img.raycastTarget = false;
            segments.Add(img);
            x = nextX; y = nextY;
        }

        // 雷鳴＋画面が一瞬白く（単発フラッシュ・短時間。光過敏性に配慮）
        _sfx.PlayOneShot(_thunderClip);
        float t = 0f;
        const float dur = 0.28f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = t / dur;
            _lightningFlash.color = new Color(1f, 1f, 0.95f, Mathf.Lerp(0.32f, 0f, p));
            foreach (var img in segments)
                if (img != null) img.color = new Color(1f, 1f, 0.85f, 0.95f * (1f - p));
            yield return null;
        }
        _lightningFlash.color = Color.clear;
        foreach (var img in segments)
            if (img != null) Destroy(img.gameObject);
    }

    // 雨のアニメーション（落ちて、下に消えたら上に戻る）
    void UpdateRain()
    {
        if (!_rainContainer.gameObject.activeSelf) return;
        float dt = Time.deltaTime;
        foreach (var s in _rainStreaks)
        {
            var p = s.anchoredPosition;
            p.y -= 950f * dt;
            p.x -= 130f * dt; // 風に流される
            if (p.y < -480f)
            {
                p.y = 480f;
                p.x = Random.Range(-820f, 880f);
            }
            s.anchoredPosition = p;
        }
    }

    // 1マスぶん歩く
    IEnumerator StepTo(Vector2Int target)
    {
        _moving = true;

        _trail.Insert(0, _gridPos);
        if (_trail.Count > _followers.Count) _trail.RemoveRange(_followers.Count, _trail.Count - _followers.Count);
        for (int i = 0; i < _followers.Count && i < _trail.Count; i++)
        {
            var f = _followers[i];
            if (f.gpos != _trail[i])
            {
                Vector2Int delta = _trail[i] - f.gpos;
                int fdir = delta.y < 0 ? 0 : delta.y > 0 ? 1 : delta.x < 0 ? 2 : 3;
                f.img.sprite = f.sprites[fdir == 3 ? 2 : fdir][1];
                f.rt.localScale = new Vector3(fdir == 3 ? -1f : 1f, 1f, 1f);
                f.gpos = _trail[i];
                StartCoroutine(SlideFollower(f, GridToAnchored(_trail[i]), fdir));
            }
        }

        Vector2 from = GridToAnchored(_gridPos);
        Vector2 to = GridToAnchored(target);
        _gridPos = target;

        float t = 0f;
        const float dur = 0.18f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = t / dur;
            ApplyPlayerSprite(p < 0.5f ? 1 : 0);
            _player.anchoredPosition = Vector2.Lerp(from, to, p);
            yield return null;
        }
        _player.anchoredPosition = to;
        ApplyPlayerSprite(0);
        _moving = false;
    }

    IEnumerator SlideFollower(Follower f, Vector2 to, int fdir)
    {
        if (f.rt == null) yield break;
        Vector2 from = f.rt.anchoredPosition;
        float t = 0f;
        const float dur = 0.18f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (f.rt == null) yield break; // パーティ再編成で破棄済みなら中断
            f.rt.anchoredPosition = Vector2.Lerp(from, to, t / dur);
            yield return null;
        }
        if (f.rt == null) yield break;
        f.rt.anchoredPosition = to;
        f.img.sprite = f.sprites[fdir == 3 ? 2 : fdir][0];
    }

    void ApplyPlayerSprite(int frame)
    {
        int d = _facing == 3 ? 2 : _facing;
        _playerImg.sprite = _walkSprites[d][frame];
        _player.localScale = new Vector3(_facing == 3 ? -1f : 1f, 1f, 1f);
    }

    // 敵がときどき1マスうろつく（ボスは動かない）
    void UpdateEnemies()
    {
        foreach (var e in _enemies)
        {
            if (e.isBoss) continue;
            e.wanderTimer -= Time.deltaTime;
            if (e.wanderTimer > 0f) continue;
            e.wanderTimer = Random.Range(1.5f, 3.5f);

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            Vector2Int target = e.gpos + dirs[Random.Range(0, 4)];
            if (target.x < GridMinX || target.x > GridMaxX || target.y < GridMinY || target.y > GridMaxY)
                continue;
            if (_trees.Contains(target)) continue;

            if (target == _gridPos)
            {
                if (!_busy) StartCoroutine(Encounter(e));
                continue;
            }
            if (EnemyAt(target) != null) continue;

            e.gpos = target;
            StartCoroutine(SlideEnemy(e.rt, GridToAnchored(target)));
        }
    }

    IEnumerator SlideEnemy(RectTransform rt, Vector2 to)
    {
        if (rt == null) yield break;
        Vector2 from = rt.anchoredPosition;
        float t = 0f;
        const float dur = 0.2f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (rt == null) yield break; // エリア移動や撃破で破棄済みなら中断
            rt.anchoredPosition = Vector2.Lerp(from, to, t / dur);
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = to;
    }

    // エンカウント演出
    IEnumerator Encounter(MapEnemy enemy)
    {
        _busy = true;
        _engaged = enemy;

        var alert = ProtoUI.CreateText("Alert", _root, enemy.isBoss ? "！！" : "！", 64,
            _player.anchoredPosition + new Vector2(0, 90), new Vector2(140, 80),
            new Color(1f, 0.3f, 0.3f));
        alert.fontStyle = TMPro.FontStyles.Bold;

        var rt = alert.rectTransform;
        Vector2 basePos = rt.anchoredPosition;
        float t = 0f;
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            rt.anchoredPosition = basePos + new Vector2(0, Mathf.Abs(Mathf.Sin(t * 10f)) * 20f);
            yield return null;
        }

        Destroy(alert.gameObject);
        _main.StartBattle(enemy.data);
    }
}
