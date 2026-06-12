using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// マップ画面: ポケモン式のマス目移動（上下左右1マスずつ）。
// 敵はマップ上に見えていて、ぶつかる（隣のマスへ進もうとする）とバトル開始。
// Bキー（またはボタン）でビルド画面（キャラのステータス）を開ける。
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
    const int EnemyCount = 6;                // マップ上の敵の数

    Vector2Int _gridPos;
    Image _playerImg;
    Sprite[][] _walkSprites;     // [向き(0正面/1背面/2左)][コマ(0/1)]
    int _facing;                 // 0=下 1=上 2=左 3=右

    class MapEnemy
    {
        public ProtoEnemy data;
        public Vector2Int gpos;
        public RectTransform rt;
        public float wanderTimer;
    }

    readonly List<MapEnemy> _enemies = new List<MapEnemy>();
    readonly HashSet<Vector2Int> _trees = new HashSet<Vector2Int>(); // 木のマス（通行不可）
    MapEnemy _engaged; // 今戦っている敵（勝ったらマップから消す）

    public void Init(ProtoMain main)
    {
        _main = main;
        BuildUI();
        for (int i = 0; i < EnemyCount; i++) SpawnEnemy();
        Hide();
    }

    public void Show()
    {
        _root.gameObject.SetActive(true);
        _busy = false;
        _moving = false;
    }

    public void Hide()
    {
        StopAllCoroutines();
        _moving = false;
        _root.gameObject.SetActive(false);
    }

    // バトル勝利時に呼ばれる: 倒した敵をマップから消し、新しい敵を補充
    public void OnEnemyDefeated()
    {
        if (_engaged != null)
        {
            Destroy(_engaged.rt.gameObject);
            _enemies.Remove(_engaged);
            _engaged = null;
            SpawnEnemy();
        }
    }

    // ==================== UI構築 ====================

    void BuildUI()
    {
        _root = ProtoUI.CreateFullScreen("MapScreen", _main.Canvas.transform);
        var backdrop = _root.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0.07f, 0.11f, 0.07f); // フィールド外周の暗い緑

        _gridPos = new Vector2Int(0, -2);

        // フィールドのレイアウトを決める（外周＝木の壁、内側にランダムな木）
        // テクスチャのタイル範囲: gx -10..10（21列）, gy -7..4（12行）
        const int texTilesX = 21, texTilesY = 12;
        _trees.Clear();
        var treeMap = new bool[texTilesX, texTilesY];

        // 外周リング（歩行範囲の外側）を木で囲う
        for (int gx = -10; gx <= 10; gx++)
        {
            for (int gy = -7; gy <= 4; gy++)
            {
                bool isBorder = gx <= GridMinX - 1 || gx >= GridMaxX + 1 || gy <= GridMinY - 1 || gy >= GridMaxY + 1;
                if (isBorder)
                    treeMap[gx + 10, 4 - gy] = true;
            }
        }

        // 内側にもランダムに木を植える（スタート地点の周囲は空ける）
        var rng = new System.Random(11);
        int planted = 0;
        while (planted < 12)
        {
            var g = new Vector2Int(rng.Next(GridMinX, GridMaxX + 1), rng.Next(GridMinY, GridMaxY + 1));
            if (_trees.Contains(g)) continue;
            if (Mathf.Abs(g.x - _gridPos.x) + Mathf.Abs(g.y - _gridPos.y) < 3) continue;
            _trees.Add(g);
            treeMap[g.x + 10, 4 - g.y] = true;
            planted++;
        }

        // 見下ろしフィールドの描画（タイル中心がグリッドと一致するよう配置）
        var field = ProtoUI.CreateRect("Field", _root);
        field.sizeDelta = new Vector2(texTilesX * TileSize, texTilesY * TileSize);
        field.anchoredPosition = new Vector2(0, -1.5f * TileSize - 30f);
        var fieldImg = field.gameObject.AddComponent<Image>();
        fieldImg.sprite = ProtoPixelArt.TopDownField(texTilesX, texTilesY, treeMap, 11);
        fieldImg.raycastTarget = false;

        // 歩行スプライトを4方向×2コマぶん用意（右向きは左向きのX反転で済ます）
        _walkSprites = new Sprite[3][];
        for (int d = 0; d < 3; d++)
            _walkSprites[d] = new[] { ProtoPixelArt.MapMama(d, 0), ProtoPixelArt.MapMama(d, 1) };

        // プレイヤー（足元に影＝接地感）
        _player = ProtoUI.CreateRect("Player", _root);
        _player.sizeDelta = new Vector2(78, 95);
        _player.anchoredPosition = GridToAnchored(_gridPos);
        AddShadow(_player, 46f);
        _playerImg = _player.gameObject.AddComponent<Image>();
        _playerImg.sprite = _walkSprites[0][0]; // 最初は正面向き
        _playerImg.preserveAspect = true;

        // 下部のUIバー（フィールドと分離した専用枠にヒントとメニューボタンを置く）
        var bottomBar = ProtoUI.CreatePanel("BottomBar", _root, new Vector2(0, -424), new Vector2(1700, 56),
            new Color(0.05f, 0.04f, 0.10f, 0.92f));
        bottomBar.raycastTarget = false;
        ProtoUI.CreatePanel("BottomBarLine", _root, new Vector2(0, -396), new Vector2(1700, 2),
            new Color(0.85f, 0.72f, 0.4f, 0.7f)).raycastTarget = false;

        ProtoUI.CreateText("Hint", _root,
            "WASD / 矢印キー = 移動　　敵にぶつかるとバトル！　　B = メニュー", 17,
            new Vector2(60, -424), new Vector2(1100, 30));
        ProtoUI.CreateButton("MenuBtn", _root, "メニュー", 18,
            new Vector2(-700, -424), new Vector2(150, 42),
            new Color(0.3f, 0.25f, 0.45f), () => _main.ShowMenu());
    }

    Vector2 GridToAnchored(Vector2Int g)
        => new Vector2(g.x * TileSize, g.y * TileSize - 30f);

    // 足元の影（楕円風）。これがあるだけで「地面に立っている」感が出る
    void AddShadow(RectTransform parent, float width)
    {
        var shadow = ProtoUI.CreateRect("Shadow", parent);
        shadow.anchoredPosition = new Vector2(0, -parent.sizeDelta.y / 2f + 4f);
        shadow.sizeDelta = new Vector2(width, 13f);
        shadow.localRotation = Quaternion.Euler(0, 0, 45); // ひし形→つぶれた楕円風
        shadow.localScale = new Vector3(1f, 0.35f, 1f);
        var img = shadow.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.28f);
        img.raycastTarget = false;
    }

    // ==================== 敵のスポーン ====================

    void SpawnEnemy()
    {
        var data = ProtoEnemies.RandomEnemy();

        // 空いているマスを探す（プレイヤーの周囲2マスは避ける）
        Vector2Int g;
        int guard = 0;
        do
        {
            g = new Vector2Int(Random.Range(GridMinX, GridMaxX + 1), Random.Range(GridMinY, GridMaxY + 1));
            guard++;
        } while ((IsOccupied(g) || _trees.Contains(g)
                  || Mathf.Abs(g.x - _gridPos.x) + Mathf.Abs(g.y - _gridPos.y) < 3) && guard < 100);

        var rt = ProtoUI.CreateRect($"Enemy_{data.id}", _root);
        rt.sizeDelta = data.mapSize;
        rt.anchoredPosition = GridToAnchored(g);
        AddShadow(rt, data.mapSize.x * 0.6f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = data.sprite;
        img.preserveAspect = true;

        _enemies.Add(new MapEnemy
        {
            data = data,
            gpos = g,
            rt = rt,
            wanderTimer = Random.Range(1.5f, 3.5f),
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
        if (_root == null || !_root.gameObject.activeSelf || _busy) return;

        UpdateEnemies();

        if (_moving) return;

        // 入力（上下左右のみ・斜め移動なし）
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

        // 進む向きにキャラを向ける（壁や敵にぶつかる場合でも向きだけは変わる）
        _facing = dir == Vector2Int.down ? 0 : dir == Vector2Int.up ? 1 : dir == Vector2Int.left ? 2 : 3;
        ApplyPlayerSprite(0);

        Vector2Int target = _gridPos + dir;
        if (target.x < GridMinX || target.x > GridMaxX || target.y < GridMinY || target.y > GridMaxY)
            return;
        if (_trees.Contains(target)) return; // 木のマスは通れない

        // 敵のマスへ進もうとした → バトル！
        var enemy = EnemyAt(target);
        if (enemy != null)
        {
            StartCoroutine(Encounter(enemy));
            return;
        }

        StartCoroutine(StepTo(target));
    }

    // 1マスぶん歩く（前半=足を開く、後半=足をそろえる の2コマアニメ）
    IEnumerator StepTo(Vector2Int target)
    {
        _moving = true;
        Vector2 from = GridToAnchored(_gridPos);
        Vector2 to = GridToAnchored(target);
        _gridPos = target;

        float t = 0f;
        const float dur = 0.18f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = t / dur;
            ApplyPlayerSprite(p < 0.5f ? 1 : 0); // 歩きのコマ切り替え
            _player.anchoredPosition = Vector2.Lerp(from, to, p);
            yield return null;
        }
        _player.anchoredPosition = to;
        ApplyPlayerSprite(0);
        _moving = false;
    }

    // 向きとコマに応じてスプライトを適用（右向きは左向きをX反転）
    void ApplyPlayerSprite(int frame)
    {
        int d = _facing == 3 ? 2 : _facing;
        _playerImg.sprite = _walkSprites[d][frame];
        _player.localScale = new Vector3(_facing == 3 ? -1f : 1f, 1f, 1f);
    }

    // 敵がときどき1マスうろつく
    void UpdateEnemies()
    {
        foreach (var e in _enemies)
        {
            e.wanderTimer -= Time.deltaTime;
            if (e.wanderTimer > 0f) continue;
            e.wanderTimer = Random.Range(1.5f, 3.5f);

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            Vector2Int target = e.gpos + dirs[Random.Range(0, 4)];
            if (target.x < GridMinX || target.x > GridMaxX || target.y < GridMinY || target.y > GridMaxY)
                continue;
            if (_trees.Contains(target)) continue; // 敵も木は通れない

            // プレイヤーのマスに踏み込んできたら向こうからエンカウント！
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
        Vector2 from = rt.anchoredPosition;
        float t = 0f;
        const float dur = 0.2f;
        while (t < dur)
        {
            t += Time.deltaTime;
            rt.anchoredPosition = Vector2.Lerp(from, to, t / dur);
            yield return null;
        }
        rt.anchoredPosition = to;
    }

    // エンカウント演出: 「！」が跳ねてからバトルへ
    IEnumerator Encounter(MapEnemy enemy)
    {
        _busy = true;
        _engaged = enemy;

        var alert = ProtoUI.CreateText("Alert", _root, "！", 64,
            _player.anchoredPosition + new Vector2(0, 90), new Vector2(80, 80),
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
