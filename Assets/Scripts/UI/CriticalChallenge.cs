using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// 「何マス光った？」の入力チャレンジを管理するUI
public class CriticalChallenge : MonoBehaviour
{
    [Header("UI References")]
    public GameObject challengePanel;     // 入力パネル全体（普段は非表示）
    public TextMeshProUGUI promptText;    // 「何マス光った？」の文字
    public TMP_InputField inputField;     // 数字入力欄
    public Slider timerSlider;            // 残り時間バー

    // 回答結果（-1 = 時間切れ／未回答）
    public int Answer { get; private set; } = -1;

    private bool _submitted;

    void Awake()
    {
        challengePanel.SetActive(false);
        inputField.onSubmit.AddListener(OnSubmit);
    }

    void OnSubmit(string text)
    {
        if (int.TryParse(text, out int value))
        {
            Answer = value;
            _submitted = true;
        }
        else
        {
            // 数字以外が入っていたら空にしてやり直し
            inputField.text = "";
            inputField.ActivateInputField();
        }
    }

    // timeLimit 秒の入力チャレンジを実行する（コルーチンとして待てる）
    public IEnumerator Run(float timeLimit)
    {
        Answer = -1;
        _submitted = false;

        challengePanel.SetActive(true);
        promptText.text = "何マス光った？（数字を入力してEnter）";
        inputField.text = "";
        inputField.ActivateInputField();

        timerSlider.maxValue = timeLimit;

        float remaining = timeLimit;
        while (remaining > 0f && !_submitted)
        {
            remaining -= Time.deltaTime;
            timerSlider.value = remaining;
            yield return null;
        }

        challengePanel.SetActive(false);
    }
}
