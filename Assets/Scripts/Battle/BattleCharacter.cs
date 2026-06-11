using UnityEngine;
using System;

public class BattleCharacter : MonoBehaviour
{
    [Header("Character Settings")]
    public string characterName = "MAMA";
    public int maxHP = 120;

    [Header("Runtime State")]
    public int currentHP;

    public event Action<int, int> OnHPChanged; // current, max
    public event Action OnDeath;

    public bool IsDead => currentHP <= 0;

    void Awake()
    {
        currentHP = maxHP;
    }

    public void TakeDamage(int amount)
    {
        currentHP = Mathf.Max(0, currentHP - amount);
        OnHPChanged?.Invoke(currentHP, maxHP);

        if (currentHP <= 0)
            OnDeath?.Invoke();
    }

    public void Heal(int amount)
    {
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        OnHPChanged?.Invoke(currentHP, maxHP);
    }

    public void ResetHP()
    {
        currentHP = maxHP;
        OnHPChanged?.Invoke(currentHP, maxHP);
    }
}
