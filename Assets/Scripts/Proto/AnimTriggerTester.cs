using UnityEngine;
using UnityEngine.InputSystem;

// Test-scene helper: fires an Animator trigger on Space, and optionally on a timer
// so an animation can be watched hands-free.
[RequireComponent(typeof(Animator))]
public class AnimTriggerTester : MonoBehaviour
{
    public string triggerName = "Attack";
    [Tooltip("Seconds between automatic triggers. 0 = Space key only.")]
    public float autoInterval = 3f;

    Animator animator;
    float timer;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) Fire();

        if (autoInterval <= 0f) return;
        timer += Time.deltaTime;
        if (timer >= autoInterval)
        {
            timer = 0f;
            Fire();
        }
    }

    void Fire()
    {
        animator.SetTrigger(triggerName);
    }
}
