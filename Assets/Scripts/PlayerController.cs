using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;
    private Rigidbody2D rb;
    private Vector2 movement;
    private Animator animator;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        movement = Vector2.zero;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            movement.y = 1;
        else if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            movement.y = -1;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            movement.x = -1;
        else if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            movement.x = 1;

        // 斜め移動を防ぐ
        if (movement.x != 0) movement.y = 0;

        // Animatorに移動方向を伝える
        animator.SetFloat("MoveX", movement.x);
        animator.SetFloat("MoveY", movement.y);
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + movement * moveSpeed * Time.fixedDeltaTime);
    }
}