# mama_game チーム開発 運用ガイド

このプロジェクトの**バージョン管理（Git）の使い方**を全員で共有するための資料です。
迷ったらここを見てください。

---

## 0. 大前提

| 項目 | 内容 |
|------|------|
| 管理方法 | **Git ＋ GitHub**（Unity Version Control / Plastic は使いません） |
| リポジトリ | https://github.com/yoshiki244/mama_game |
| 使うツール | **GitHub Desktop**（クリック操作だけ・LFS自動）を推奨 |
| 開くシーン | `Assets/Scenes/proto.unity`（再生▶すると組み上がる） |

---

## 1. 個人ブランチで作業する（重要ルール）

**全員、自分専用のブランチで作業します。** 他人のブランチや main は直接いじりません。

| 人 | 使うブランチ |
|----|------------|
| yoshiki | `yoshiki` |
| ioki | `ioki` |
| mikumo | `mikumo` |
| （新メンバー） | 自分の名前のブランチを作る |

> なぜ？ … 同じブランチを2人で同時に触ると**衝突（コンフリクト）**が起きやすいため。
> 個人ブランチなら、各自マイペースで作業できます。

---

## 2. 最初の1回だけ（セットアップ）

1. **GitHub Desktop** をインストール（https://desktop.github.com/）
2. yoshiki に GitHub の **Collaborator 招待**をしてもらう（リポジトリ Settings → Collaborators）
3. GitHub Desktop で **Clone**：`yoshiki244/mama_game` を選ぶ
4. 上部 **Current Branch** → 自分のブランチを選ぶ（無ければ「New Branch」で作る）
5. Unity Hub で clone したフォルダを開く → `proto.unity` を▶

---

## 3. 毎日の作業の流れ

```
① 作業前：Pull（最新を取り込む）
② Unityで開発する
③ 区切りがついたら：Commit（保存）＋ Push（共有）
```

**GitHub Desktopでの操作：**
- **Pull** … メニュー Repository → Pull（または「Pull origin」ボタン）
- **Commit** … 左下にメッセージを書いて「Commit to （自分のブランチ）」
- **Push** … 「Push origin」ボタン

> 💡 こまめに Commit / Push しよう。ためると衝突や事故のもとです。

---

## 4. みんなの成果を合流させたいとき

各自の個人ブランチの成果を共有したくなったら、**main に集約**します。
（やり方が不安なら、まとめ役が GitHub 上で Pull Request を使うのが安全です）

- 手軽な方法：GitHub Desktopで Current Branch を main にして、自分のブランチを Merge → Push
- 安全な方法：GitHub サイトで Pull Request（`自分のブランチ → main`）を作る

> 合流は衝突しやすい作業なので、**不安なら相談してから**やりましょう。

---

## 5. よくあるトラブルと対処

### ブランチ切り替えで「○○ would be overwritten」エラー
Unityが自動で触ったファイル（画像など）が邪魔しています。**自分で作った変更でなければ捨ててOK**：
- GitHub Desktop：Changesタブで右クリック → **Discard all changes** → 切り替え
- コマンド派：`git checkout -f 切り替え先ブランチ`（強制切り替え）

### `pipo-nekonin001.png` / `URP.png` が毎回「変更」になる
Unityテンプレ付属のサンプル画像の差分です。**無害なので Discard で捨ててOK**。

### Git LFS（大きいファイル）について
フォント・画像・PDFは **Git LFS** で管理されています。GitHub Desktop なら自動で扱われるので意識不要。コマンド派は最初に1回 `git lfs install`。

### コンフリクト（衝突）が出た
あわてず、どの行を残すか選んで解決します。**自信がなければ手を止めて相談**してください（無理に進めると壊れます）。

---

## 6. やってはいけないこと

- ❌ Unity Version Control（Plastic）に繋ぎ直す（二重管理で事故ります）
- ❌ 他人のブランチを勝手に上書き push する
- ❌ 長時間 Pull せずに作業し続ける（衝突が膨らむ）
- ❌ `Library/` フォルダを手で消す/共有する（各自で自動生成されるもの。Gitにも入れません）

---

## 7. 困ったら

- Git操作・衝突・ブランチ整理などは、yoshiki に相談（AIサポートで解決できます）
- 「何をしたいか（例：友達の最新を取り込みたい）」を伝えれば、手順を出してもらえます

---

*このゲームのソース構成は `docs/ソースコード解説.pdf` / `ソースコード詳細解説.pdf` を参照。*
