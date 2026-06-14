# CLAUDE.md — mama_game 開発ガイド

このファイルは Claude Code（および開発メンバー）がこのリポジトリを編集する際の道しるべです。
作業前にこれを読めば「どこを触ればよいか」が分かります。変更で構造が変わったら、このファイルも更新してください。

---

## 1. ゲーム概要
『Slay the Spire』×『Backpack Battles』をモデルにした、ローグライク育成カードバトル（仮称 Project M）。
中心となる独自性は **スキルパネル（盤面）にピースを配置し、占有マス比で手札の出現確率が決まる** こと。

コアループ：
```
マップ(横スクロール) → エネミーマスで戦闘 / イベントでお金 / 精神樹で盤面拡張・ピース購入
   → 戦闘勝利でお金＋ピース3択 → ビルド画面で盤面に配置 → 次のマスへ → 最深部のボス撃破でクリア
```

主要メカニクス：
- **マナ制戦闘**：毎ターン手札5枚配布、マナ予算内でカード使用、「ターン終了」で敵の行動。
- **マナ**：最大マナ = 3 + 盤面拡張回数（5×5=3 … 10×10=8）。カードのマナ = `Ceil(マス数/10)`。
- **盤面**：初期5×5、精神樹で最大10×10まで拡張。出現確率 = ピースのマス数 ÷ 盤面マス数。空白マス=通常攻撃。
- **点滅順番当て**：効果（`BlinkOnUse` を持つアタック、または「点滅」スキルで直前にprime）でのみ発動。
  光った順番をタップ再現。採点はLCSベース、正答率0%→0倍 / 80%→1.0倍 / 100%→1.25倍。
- **深度**：マップの列番号。カードは `minDepth`〜`maxDepth` の範囲でのみ報酬/ショップに出現（**隠しパラメータ**、画面非表示）。

---

## 2. プロジェクト構成と動かし方
- Unity **6000.4.9f1**。エントリーシーンは `Assets/Scenes/proto.unity`（空GameObject＋`ProtoMain` のみ）。
- ▶再生すると `ProtoMain.Awake` がカメラ・Canvas・全UIを **コードから生成** する（手作業のシーン構築なし）。
- **初回/データ作り直し時のみ**：メニュー `MamaGame > コンテンツ(SO)を生成` を実行 →
  `Assets/Resources/GameData/ContentDatabase.asset` ほかカード/敵の .asset を生成。
  通常はコミット済みの生成データがあるのでそのまま動く。
- 操作：マップ=ノードをクリックで進む / B=メニュー、戦闘=カードクリック＋「ターン終了」、ビルド=左クリ配置/右クリ撤去/ドラッグ移動/Rで回転。

---

## 3. アーキテクチャの要点
**「データはScriptableObject、ランタイムはコード生成」** の二層構造。

- **データ層**（`Assets/Scripts/Data/`）：カード・敵・設定を ScriptableObject 化し、Unity Inspector で編集・追加できる。
- **ランタイム層**（`Assets/Scripts/Proto/`）：画面とロジック。UIは `ProtoUI` のヘルパーでコード生成、
  絵は `ProtoPixelArt`（ドット絵を文字マップから生成）、音は `ProtoAudio`（波形生成）。画像/音声アセットは基本不要。
- データは `ProtoMain` が `Resources.Load<ContentDatabase>("GameData/ContentDatabase")` で読み込む（シーン手配線なし）。

データの流れは一方向：**データ(SO) → ProtoMain → 各画面**。

---

## 4. ファイル早見表

### Assets/Scripts/Data/（ScriptableObject）
| ファイル | 役割 |
|---|---|
| `CardDef.cs` | カード定義。`shapeRows`(文字マップ"XX.")・`kind`(Attack/Skill)・`power`・`effects`・`manaCostOverride`・`minDepth/maxDepth`。`Shape`/`Size`/`ManaCost` を算出 |
| `EnemyDef.cs` | 敵定義。HP・攻撃・`EnemySpriteKey`(ProtoPixelArtの絵)・`moneyReward`・`attacks`。`PickAttack()` |
| `GameConfig.cs` | 全体調整値。プレイヤーHP/攻撃・baseMana・手札枚数・拡張コスト・ショップ・点滅速度・初期所持カード |
| `ContentDatabase.cs` | カード/敵/Configを束ねる。`FindCard/FindEnemy`・`RandomCards(n,exclude,depth)`(深度フィルタ) |

### Assets/Scripts/Editor/
| `ContentGenerator.cs` | `MamaGame > コンテンツ(SO)を生成` メニュー。本ガイドのカード/敵/設定の .asset を一括生成 |

### Assets/Scripts/Proto/（ランタイム）
| ファイル | 役割 |
|---|---|
| `ProtoMain.cs` | 司令塔。起動・画面切替・DB読込・プレイヤー状態（HP/お金/盤面拡張/所持カード/最大マナ/深度）・経済(AddMoney/ExpandBoard/BuyCard/AddCard) |
| `PanelModel.cs` | 盤面データ。可変W×H(5〜10)、配置判定・回転・`Resize`・`BuildDeck`(確率母集団)・`CountByCard` |
| `BuildScreen.cs` | ビルド画面。盤面描画・ホバープレビュー配置・所持カード一覧(スクロール)・出現率表示 |
| `ProtoBattle.cs` | 戦闘全部。マナ制ターン進行・カード効果リゾルバ・点滅ゲーム(LCS採点)・敵AI・勝敗・報酬3択・各種演出 |
| `MapScreen.cs` | 横スクロールのノードマップ。エネミー/イベント/精神樹(5:3:1,樹5以上)・分岐収束・精神樹ショップ・クリア演出 |
| `MenuScreen.cs` | メニュー。ステータス/ビルド/設定(音量・BGM)/セーブ/閉じる |
| `ProtoSave.cs` | セーブ/ロード(PlayerPrefs+JSON)。お金・拡張・所持カード・盤面配置 |
| `PlayerStats.cs` | プレイヤー基礎値(HP/攻撃)。成長は盤面とピースに一本化（レベル/EXPなし） |
| `ProtoUI.cs` | UI部品の共通生成(パネル/文字/ボタン/ゲージ/スクロール)。`CellClickHandler` も同梱 |
| `ProtoPixelArt.cs` | ドット絵生成（MAMA・敵5種・背景）。文字マップを書き換えて絵を編集 |
| `ProtoAudio.cs` | BGM/効果音を波形から生成 |

---

## 5. 「こうしたい」逆引き
| やりたいこと | どこを触る |
|---|---|
| カードの威力・形状・効果・マナ・出現深度を変える | 各 `Assets/GameData/Cards/card_*.asset` を Inspector で編集 |
| カードを新規追加 | `ContentGenerator.cs` にエントリ追加→メニュー再生成、または .asset を直接複製して `ContentDatabase` の cards に登録 |
| 敵のHP・攻撃・報酬額を変える | `Assets/GameData/Enemies/enemy_*.asset` を Inspector で編集 |
| 全体バランス（マナ・拡張コスト・手札枚数・点滅速度・初期所持） | `Assets/Resources/GameData/GameConfig.asset` |
| カード効果の種類を増やす | `CardDef.cs` の `CardEffectType` に追加 → `ProtoBattle.ApplyCardEffects` に処理を追加 |
| 戦闘の進行・ダメージ計算 | `ProtoBattle.cs`（`PlayCard`/`ResolveAttack`/`EnemyTurn`/`EnemyAttackSequence`） |
| 点滅ゲームのルール・採点 | `ProtoBattle.cs`（`RunChallenge`/`ScoreBlink`/`Lcs`）。速度は GameConfig.blinkTimeScale |
| マップの形・マス比率・敵配置 | `MapScreen.cs`（`BuildMap`/`cycle`配列/`EnsureMinSpiritTrees`/`EnemyForColumn`） |
| 精神樹ショップ・盤面拡張 | `MapScreen.cs`(`OpenShop`) と `ProtoMain.cs`(`ExpandBoard`/`BuyCard`) |
| 敵/キャラのドット絵 | `ProtoPixelArt.cs` の各メソッドの文字マップ |

---

## 6. 規約・注意点
- **新しいカード/敵/数値は極力 ScriptableObject(.asset) 側で**。コードのハードコードは避ける（編集可能性が本設の核）。
- 深度（minDepth/maxDepth）は **プレイヤーに見せない隠しパラメータ**。ゲーム内UIに表示しないこと。
- アタックカードは **最低5マス**（点滅ゲーム成立のため）。スキルは任意サイズ。
- 点滅などの明滅演出は **光過敏性に配慮**（明滅は控えめ・短時間）。
- UIは座標を画面中央(0,0)基準で配置（`ProtoUI` 既定アンカーは中央）。スクロール領域は anchor/pivot に注意。
- `Assets/Resources/GameData/` と `Assets/GameData/` は生成データ。基本はコミット済みのものを使う（pull後そのまま動く）。

---

## 7. チーム開発（Git運用）
- リモート: `yoshiki244/mama_game`。主ブランチ `main`、各自の作業ブランチ（`ioki` など）。
- 推奨フロー：作業ブランチで実装→動作確認→`main` にマージ。マージ前に `main` を取り込んでおく。
- Unity設定は Asset Serialization=Force Text（シーン/プレハブがマージ可能）。`Library/` 等は `.gitignore` 済み。
- 詳しい運用ガイドは `docs/skill.md` を参照。
