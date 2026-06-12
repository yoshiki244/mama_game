using UnityEngine;

// BGMをコードから生成する（プリプロ用の簡易チップチューン）
// 矩形波のメロディ＋サイン波のベースをループ再生する
public static class ProtoAudio
{
    public static AudioClip CreateBgm()
    {
        const int sampleRate = 44100;
        const float noteDur = 0.30f;

        // のどかなフィールド曲風の16音ループ（C メジャー）
        float[] melody =
        {
            523.25f, 587.33f, 659.25f, 783.99f,   // ド レ ミ ソ
            659.25f, 587.33f, 523.25f, 392.00f,   // ミ レ ド ソ(低)
            440.00f, 523.25f, 587.33f, 659.25f,   // ラ ド レ ミ
            587.33f, 523.25f, 440.00f, 392.00f,   // レ ド ラ ソ(低)
        };
        // ベース（2音で1小節を支える）
        float[] bass = { 130.81f, 98.00f, 110.00f, 98.00f }; // C G(低) A(低) G(低)

        int samplesPerNote = (int)(sampleRate * noteDur);
        int total = samplesPerNote * melody.Length;
        var data = new float[total];

        for (int n = 0; n < melody.Length; n++)
        {
            float freq = melody[n];
            float bassFreq = bass[(n / 4) % bass.Length];
            for (int s = 0; s < samplesPerNote; s++)
            {
                int idx = n * samplesPerNote + s;
                float t = (float)idx / sampleRate;
                float env = Mathf.Exp(-3f * (s / (float)samplesPerNote)); // 音の減衰

                // メロディ: 矩形波（ファミコン風）
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.055f * env;
                // ベース: サイン波（柔らかく支える）
                float sine = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * 0.045f;

                data[idx] = square + sine;
            }
        }

        var clip = AudioClip.Create("ProtoBgm", total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // バトルBGM: 短調・速いテンポ・ドラム入りの緊迫感あるループ
    public static AudioClip CreateBattleBgm()
    {
        const int sampleRate = 44100;
        const float noteDur = 0.15f; // フィールドの2倍速

        // Aマイナーの駆け上がりリフ（32ステップ）
        float[] melody =
        {
            440.00f, 440.00f, 523.25f, 440.00f, 659.25f, 440.00f, 523.25f, 440.00f, // A A C A E A C A
            392.00f, 392.00f, 493.88f, 392.00f, 587.33f, 392.00f, 493.88f, 392.00f, // G G B G D G B G
            440.00f, 440.00f, 523.25f, 523.25f, 659.25f, 659.25f, 698.46f, 659.25f, // 駆け上がり
            587.33f, 523.25f, 493.88f, 523.25f, 440.00f, 440.00f, 329.63f, 440.00f, // 降りて締め
        };
        // 4小節ぶんのベース進行（A → G → A → E）
        float[] bass = { 110.00f, 98.00f, 110.00f, 82.41f };

        int samplesPerNote = (int)(sampleRate * noteDur);
        int total = samplesPerNote * melody.Length;
        var data = new float[total];
        var rng = new System.Random(3);

        for (int n = 0; n < melody.Length; n++)
        {
            float freq = melody[n];
            float bassFreq = bass[(n / 8) % bass.Length];
            bool kick = n % 4 == 0;  // 4つ打ちのキック
            bool hat = n % 2 == 1;   // 裏拍のハイハット

            for (int s = 0; s < samplesPerNote; s++)
            {
                int idx = n * samplesPerNote + s;
                float t = (float)idx / sampleRate;
                float p = s / (float)samplesPerNote;
                float env = Mathf.Exp(-4f * p);

                // メロディ: 矩形波（鋭く）
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.06f * env;
                // ベース: 矩形波で刻む（駆動感）
                float bassSq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * bassFreq * t)) * 0.05f * Mathf.Exp(-2f * p);

                float drum = 0f;
                if (kick && s < sampleRate * 0.05f)
                {
                    // キック: 低い周波数がさらに下がっていくドンッ
                    float kp = s / (sampleRate * 0.05f);
                    drum += Mathf.Sin(2f * Mathf.PI * (90f - 50f * kp) * t) * 0.12f * (1f - kp);
                }
                if (hat && s < sampleRate * 0.02f)
                {
                    // ハイハット: 短いノイズ
                    drum += ((float)rng.NextDouble() * 2f - 1f) * 0.035f * (1f - s / (sampleRate * 0.02f));
                }

                data[idx] = square + bassSq + drum;
            }
        }

        var clip = AudioClip.Create("ProtoBattleBgm", total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 敵の攻撃の風切り音（ヒュッ！）
    public static AudioClip CreateSwing()
    {
        const int sampleRate = 44100;
        const float dur = 0.22f;
        int total = (int)(sampleRate * dur);
        var data = new float[total];
        var rng = new System.Random(9);

        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total;
            float env = Mathf.Sin(p * Mathf.PI);
            env *= env; // 山なりの音量（スッと出てスッと消える）

            // 風のノイズ＋上昇するうなり
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.16f * env;
            float f = Mathf.Lerp(280f, 900f, p);
            float tone = Mathf.Sin(2f * Mathf.PI * f * (i / (float)sampleRate)) * 0.05f * env;
            data[i] = noise + tone;
        }

        var clip = AudioClip.Create("SwingSfx", total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ヒット効果音（tier 0=小技 〜 3=クリティカル。強いほど長く・低く・大きい）
    public static AudioClip CreateHitClip(int tier)
    {
        const int sampleRate = 44100;
        float dur = 0.12f + tier * 0.09f;
        float amp = 0.20f + tier * 0.09f;
        float startFreq = 240f + tier * 80f; // 高い音から
        int total = (int)(sampleRate * dur);
        var data = new float[total];
        var rng = new System.Random(tier + 1);

        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total;
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-6f * p);

            // 打撃ノイズ（バシッ）
            float noise = ((float)rng.NextDouble() * 2f - 1f) * amp * 0.6f * Mathf.Exp(-14f * p);
            // 下降する低音（ドゥン…と落ちる衝撃）
            float sweepFreq = Mathf.Lerp(startFreq, 45f, p);
            float thump = Mathf.Sin(2f * Mathf.PI * sweepFreq * t) * amp * env;

            data[i] = noise + thump;
        }

        var clip = AudioClip.Create($"HitSfx{tier}", total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
