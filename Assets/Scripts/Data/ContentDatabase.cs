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

    // 報酬・ショップ用：所持しておらず、深度条件(minDepth<=depth)を満たすカードからランダムにn種
    public List<CardDef> RandomCards(int n, ICollection<string> exclude = null, int depth = int.MaxValue)
    {
        var pool = new List<CardDef>();
        foreach (var c in cards)
            if (c != null
                && c.minDepth <= depth
                && (c.maxDepth <= 0 || depth <= c.maxDepth) // maxDepth<=0は上限なし
                && (exclude == null || !exclude.Contains(c.id)))
                pool.Add(c);
        // シャッフルして先頭n枚
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        if (pool.Count > n) pool.RemoveRange(n, pool.Count - n);
        return pool;
    }
}
