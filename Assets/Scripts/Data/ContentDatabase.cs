using UnityEngine;
using System.Collections.Generic;

// 全コンテンツ（カード・敵・設定）を束ねるデータベース。
// ProtoMain が Resources.Load<ContentDatabase>("GameData/ContentDatabase") で読み込む。
[CreateAssetMenu(fileName = "ContentDatabase", menuName = "MamaGame/ContentDatabase")]
public class ContentDatabase : ScriptableObject
{
    public GameConfig config;
    public CardDef normalAttack;          // 空白マス＝通常攻撃用のカード定義
    public List<CardDef> cards = new List<CardDef>();
    public List<EnemyDef> enemies = new List<EnemyDef>();

    // 実行時の上書き（カード成長など。アセットには保存されない）
    readonly Dictionary<string, CardDef> _overrides = new Dictionary<string, CardDef>();
    public void OverrideCard(string id, CardDef c) { if (!string.IsNullOrEmpty(id) && c != null) _overrides[id] = c; }
    public void ClearOverrides() => _overrides.Clear();

    public CardDef FindCard(string id)
        => (_overrides.TryGetValue(id, out var o) ? o : null) ?? cards.Find(c => c != null && c.id == id);
    public EnemyDef FindEnemy(string id) => enemies.Find(e => e != null && e.id == id);

    // 報酬・ショップ用：所持しておらず、深度条件(minDepth<=depth)とアンロック段階(unlockTier<=clears)を満たすカードからランダムにn種
    public List<CardDef> RandomCards(int n, ICollection<string> exclude = null, int depth = int.MaxValue, int clears = int.MaxValue, bool uniform = false)
    {
        var pool = new List<CardDef>();
        foreach (var c in cards)
            if (c != null
                && c.minDepth <= depth
                && (c.maxDepth <= 0 || depth <= c.maxDepth) // maxDepth<=0は上限なし
                && c.unlockTier <= clears                    // アンロック段階
                && (exclude == null || !exclude.Contains(c.id)))
                pool.Add(c);

        // レアリティで加重抽選（コモン6 / アンコモン3 / レア1）・重複なしでn枚
        // uniform=true なら重みを無視して完全ランダム（デバッグ用）
        var picked = new List<CardDef>();
        while (picked.Count < n && pool.Count > 0)
        {
            int idx;
            if (uniform) idx = Random.Range(0, pool.Count);
            else
            {
                float total = 0f;
                foreach (var c in pool) total += RarityWeight(c);
                float r = Random.value * total;
                idx = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++) { r -= RarityWeight(pool[i]); if (r <= 0f) { idx = i; break; } }
            }
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return picked;
    }

    static float RarityWeight(CardDef c) => c.rarity >= 2 ? 1f : c.rarity == 1 ? 3f : 6f;
}
