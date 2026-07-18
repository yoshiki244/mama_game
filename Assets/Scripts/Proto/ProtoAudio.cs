using UnityEngine;

// BGMをコードから生成する（プリプロ用チップチューン・多声構成）
// ベース／メロディ／和音パッド／アルペジオ／ドラム＋エコーで世界観に合った曲を合成する
public static class ProtoAudio
{
    const int SR = 44100;

    // ---- シンセ部品 ----
    static float MidiF(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

    // パルス波（duty可変・ビブラート付き）：メロディ向き
    static void Pulse(float[] d, int start, int len, int midi, float amp, float duty, float decay, float vib = 0f)
    {
        if (midi <= 0) return;
        float freq = MidiF(midi), phase = 0f;
        for (int s = 0; s < len && start + s < d.Length; s++)
        {
            float p = s / (float)len;
            float v = vib > 0 ? 1f + vib * Mathf.Sin(2f * Mathf.PI * 5.5f * s / SR) : 1f;
            phase += freq * v / SR;
            float frac = phase - Mathf.Floor(phase);
            d[start + s] += (frac < duty ? 1f : -1f) * amp * Mathf.Exp(-decay * p);
        }
    }

    // 三角波：柔らかいベース向き（ファミコンのベース）
    static void Tri(float[] d, int start, int len, int midi, float amp, float decay)
    {
        if (midi <= 0) return;
        float freq = MidiF(midi), phase = 0f;
        for (int s = 0; s < len && start + s < d.Length; s++)
        {
            float p = s / (float)len;
            phase += freq / SR;
            float frac = phase - Mathf.Floor(phase);
            d[start + s] += (4f * Mathf.Abs(frac - 0.5f) - 1f) * amp * Mathf.Exp(-decay * p);
        }
    }

    // サインのパッド（アタック/リリース付きで持続）：和音の支え
    static void Pad(float[] d, int start, int len, int midi, float amp)
    {
        if (midi <= 0) return;
        PadHz(d, start, len, MidiF(midi), amp);
    }

    // 周波数指定版（デチューン＝わずかにずらした2音を重ねると「うなり」が生まれ、不穏さが出る）
    static void PadHz(float[] d, int start, int len, float freq, float amp)
    {
        for (int s = 0; s < len && start + s < d.Length; s++)
        {
            float p = s / (float)len;
            float env = Mathf.Clamp01(p / 0.08f) * Mathf.Clamp01((1f - p) / 0.15f);
            d[start + s] += Mathf.Sin(2f * Mathf.PI * freq * s / SR) * amp * env;
        }
    }

    static void Kick(float[] d, int start, float amp)
    {
        int len = (int)(SR * 0.09f);
        for (int s = 0; s < len && start + s < d.Length; s++)
        {
            float p = s / (float)len;
            float f = Mathf.Lerp(120f, 40f, p);
            d[start + s] += Mathf.Sin(2f * Mathf.PI * f * s / SR) * amp * (1f - p);
        }
    }

    static void Snare(float[] d, int start, float amp, System.Random rng)
    {
        int len = (int)(SR * 0.07f);
        for (int s = 0; s < len && start + s < d.Length; s++)
        {
            float p = s / (float)len;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            float body = Mathf.Sin(2f * Mathf.PI * 185f * s / SR) * 0.5f;
            d[start + s] += (noise * 0.7f + body) * amp * (1f - p);
        }
    }

    static void Hat(float[] d, int start, float amp, System.Random rng)
    {
        int len = (int)(SR * 0.022f);
        for (int s = 0; s < len && start + s < d.Length; s++)
            d[start + s] += ((float)rng.NextDouble() * 2f - 1f) * amp * (1f - s / (float)len);
    }

    // エコー（薄い残響で空間を作る）→ クリップ防止のクランプ
    static void Finish(float[] d, float delaySec, float fb)
    {
        int ds = (int)(SR * delaySec);
        for (int i = ds; i < d.Length; i++) d[i] += d[i - ds] * fb;
        for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i], -0.9f, 0.9f);
    }

    static AudioClip Bake(string name, float[] d)
    {
        var clip = AudioClip.Create(name, d.Length, 1, SR, false);
        clip.SetData(d, 0);
        return clip;
    }

    // ==================== フィールド曲「旅路」 ====================
    // Aマイナーの冒険曲。Am→F→C→G の王道進行を8小節、三角波ベース＋パルスメロディ＋和音パッド＋アルペジオ
    public static AudioClip CreateBgm()
    {
        const float step = 0.23f;
        int spn = (int)(SR * step);
        int steps = 64;                       // 8小節 × 8ステップ（約15秒ループ）
        var d = new float[spn * steps];
        var rng = new System.Random(7);

        int[] roots = { 45, 41, 48, 43, 45, 41, 40, 45 };          // A F C G / A F E A
        bool[] minor = { true, false, false, false, true, false, false, true }; // E は長三和音（V）
        int[] bp = { 0, 99, 0, 12, 99, 7, 0, 99 };                  // ベースの歩み（99=休符）

        int[] mel =
        {
            69,72,76,72, 69, 0,64,67,   // Am: 主題
            65,69,72,69, 65, 0,72,74,   // F : 主題の平行
            76,74,72,67, 64,67,72, 0,   // C : 下降で応答
            74,71,67,71, 74, 0,79,74,   // G : 跳ねて高音へ
            69, 0,69,71, 72,74,76, 0,   // Am: 駆け上がり
            77,76,74,72, 69, 0,72,69,   // F : 頂点から降りる
            71, 0,68,71, 76, 0,71,68,   // E : 緊張（G#）
            69, 0,64, 0, 69, 0, 0, 0,   // Am: 締め
        };

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;
            int root = roots[bar], third = root + (minor[bar] ? 3 : 4);

            if (bp[st] != 99) Tri(d, at, (int)(spn * 0.95f), root + bp[st], 0.075f, 1.2f);   // ベース
            Pulse(d, at, (int)(spn * 0.95f), mel[n], 0.05f, 0.25f, 2.2f, 0.004f);            // メロディ
            if (st == 0)                                                                      // 和音パッド（小節頭から持続）
            {
                Pad(d, at, spn * 8, root + 12, 0.020f);
                Pad(d, at, spn * 8, third + 12, 0.016f);
                Pad(d, at, spn * 8, root + 19, 0.016f);
            }
            int[] arp = { root + 24, third + 24, root + 31, third + 24 };                     // きらめきのアルペジオ
            Pulse(d, at, (int)(spn * 0.5f), arp[st % 4], 0.016f, 0.5f, 6f);
            if (st % 2 == 0) Hat(d, at, 0.012f, rng);                                          // 軽いリズム
            if (st == 0) Kick(d, at, 0.07f);
        }
        Finish(d, step * 1.5f, 0.22f);
        return Bake("ProtoBgm", d);
    }

    // ==================== バトル曲「白刃」 ====================
    // Eマイナー・3部構成の16小節（約17秒ループ）
    //   A(1-8小節) : 疾走リフ＋ロックドラム
    //   B(9-12小節): ブレイクダウン（静かに溜める・持続音とアルペジオ）
    //   C(13-16小節): クライマックス（1オクターブ上のリフ＋倍速ハット＋スネアフィル）
    public static AudioClip CreateBattleBgm()
    {
        const float step = 0.135f;
        int spn = (int)(SR * step);
        int steps = 128;   // 16小節
        var d = new float[spn * steps];
        var rng = new System.Random(3);

        int[] roots =
        {
            40, 40, 36, 38, 40, 36, 35, 40,   // A: E E C D / E C B E
            36, 38, 40, 35,                    // B: C D E B（溜め）
            40, 36, 38, 40,                    // C: E C D E（決着）
        };
        // メロディ譜面：0=休符 / -1=前の音を伸ばす（タイ）→ 小節ごとにリズムと音形を変える
        int[] mel =
        {
            // ---- A: リフ→シンコペ→駆け上がり→応答、と毎小節フレーズを変える ----
            76,-1,76,79, 76, 0,74,72,   // A1 頭を伸ばすギャロップ
             0,76, 0,79,  0,81,79,76,   // A2 裏拍のシンコペーション
            72,76,79,84, -1,-1,79,76,   // A3 アルペジオで駆け上がり長く鳴らす
            74, 0,78,74, 71,69,71,-1,   // A4 下降で応答
            64,-1,67,64, 71,-1,69,67,   // A5 低音域の「呼びかけ」
            84,-1,83,79, 76,-1,72,-1,   // A6 高音域の「応答」
            71,75,71,75, 78,-1,75,71,   // A7 半音のトリルで緊張
            76,-1,-1, 0, 74,76,79,81,   // A8 ブレイク→駆け上がりで次へ
            // ---- B: ブレイクダウン（白玉中心・間を活かす） ----
            72,-1,-1,-1, 76,-1,-1,-1,
            74,-1,-1,-1, 78,-1,-1,-1,
            79,-1,-1,-1, 76,-1,74,-1,
            75,-1,71,-1, 66,-1, 0, 0,
            // ---- C: クライマックス（16分ソロ・別物のフレーズ） ----
            76,79,81,83, 84,83,81,79,   // C1 高速ラン
            84,-1,79,84, 88,-1,84,79,   // C2 最高音E6のアクセント
            86,83,81,78, 74,78,81,-1,   // C3 カスケード下降
            83,81,79,76, -1,-1,76,-1,   // C4 決めて締め
        };

        // ベースの音形も小節ごとに変える（0=ルート,7=5度,12=オクターブ,99=休符）
        int[][] bassPat =
        {
            new[]{ 0,0,12,0, 0,12,0,7 },    // ギャロップ
            new[]{ 0,99,0,12, 0,99,7,12 },  // 跳ねる
        };
        int[] bassRun = { 0, 7, 12, 7, 0, 7, 12, 19 };  // C用の駆けるライン

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;
            int sec = bar < 8 ? 0 : bar < 12 ? 1 : 2;   // 0=A 1=B 2=C
            int root = roots[bar];

            // ---- ベース ----
            if (sec == 1) { if (st == 0) Tri(d, at, spn * 7, root, 0.07f, 0.3f); }          // 全音符で沈む
            else if (sec == 2) Tri(d, at, (int)(spn * 0.9f), root + bassRun[st], 0.085f, 1.5f); // 駆けるライン
            else
            {
                int bo = bassPat[bar % 2][st];
                if (bo != 99) Tri(d, at, (int)(spn * 0.9f), root + bo, 0.08f, 1.5f);
            }

            // ---- メロディ（タイ対応：-1が続くぶん音を伸ばす） ----
            if (mel[n] > 0)
            {
                int hold = 1;
                while (n + hold < steps && mel[n + hold] == -1) hold++;
                float mAmp = sec == 1 ? 0.045f : sec == 2 ? 0.06f : 0.055f;
                float duty = sec == 1 ? 0.5f : bar == 4 ? 0.5f : 0.25f;   // A5の低音は丸い音色で「別の楽器」感
                Pulse(d, at, (int)(spn * hold * 0.95f), mel[n], mAmp, duty, sec == 1 ? 1.0f : 2.2f, 0.005f);
                if (sec == 2) Pulse(d, at, (int)(spn * hold * 0.95f), mel[n] - 12, 0.028f, 0.25f, 2.2f); // Cは下オクターブ重ね
            }

            // ---- 和音・装飾 ----
            if (st == 0)
            {
                Pad(d, at, spn * 8, root + 12, sec == 1 ? 0.022f : 0.015f);
                Pad(d, at, spn * 8, root + 19, sec == 1 ? 0.018f : 0.013f);
            }
            if (sec == 1)   // B: きらめくアルペジオ
            {
                int[] arp = { root + 24, root + 27, root + 31, root + 36 };
                Pulse(d, at, (int)(spn * 0.5f), arp[st % 4], 0.018f, 0.5f, 6f);
            }

            // ---- ドラム（小節ごとにグルーヴを変える・フィル入り） ----
            if (sec == 0)
            {
                bool fillBar = bar == 3 || bar == 7;
                if (bar % 2 == 0) { if (st == 0 || st == 6) Kick(d, at, 0.13f); }
                else { if (st == 0 || st == 3) Kick(d, at, 0.13f); }        // 小節でキック位置を変える
                if (st == 4) Snare(d, at, 0.10f, rng);
                if (fillBar && st >= 5) Snare(d, at, 0.05f + 0.02f * (st - 5), rng);   // 4/8小節目はフィル
                Hat(d, at, st % 2 == 1 ? 0.020f : 0.010f, rng);
            }
            else if (sec == 1)
            {
                if (st == 0) Kick(d, at, 0.10f);
                if (st == 4) Hat(d, at, 0.014f, rng);
                if (bar == 11 && st >= 3) Snare(d, at, 0.04f + 0.02f * (st - 3), rng);  // 復帰前の長いロール
            }
            else
            {
                if (st == 0 || st == 3 || st == 6) Kick(d, at, 0.14f);
                if (st == 4) Snare(d, at, 0.11f, rng);
                Hat(d, at, 0.020f, rng);
                if (bar == 15 && st >= 4) Snare(d, at, 0.06f + 0.02f * (st - 4), rng);
            }
        }
        Finish(d, step * 2f, 0.18f);
        return Bake("ProtoBattleBgm", d);
    }

    // ==================== 中ボス曲「強襲」 ====================
    // Dハーモニックマイナーの攻撃的な行進ロック。雑魚曲より重く、ボス曲ほど絶望的でない「強敵」の緊張感
    //   A(1-8小節): ギャロップベース＋鋭いリフ＋戦闘的ドラム
    //   B(9-12小節): ハーフタイムの重い踏みつけ→駆け上がってループ
    public static AudioClip CreateMidBossBgm()
    {
        const float step = 0.15f;
        int spn = (int)(SR * step);
        int steps = 96;   // 12小節
        var d = new float[spn * steps];
        var rng = new System.Random(8);

        int[] roots = { 38, 38, 46, 48, 38, 46, 45, 38,   38, 41, 43, 45 };   // Dm Dm Bb C / Dm Bb A Dm → D F G A(上昇)
        int[] mel =
        {
            // ---- A: 鋭い襲撃リフ ----
            74,-1,74,77, 74,72,70,72,
            74,74, 0,74, 77,-1,81,79,
            70,-1,70,74, 77,-1,74,70,
            72,76,79,-1, 76,72,67,72,
            74,-1,77,-1, 81,-1,80,81,   // C#（導音）の緊張
            82,81,79,77, 74,-1,70,-1,
            73,-1,76,73, 69,-1,73,76,   // A(V)で威嚇
            74,-1,-1,74, 77,74,70,74,
            // ---- B: ハーフタイムの踏みつけ ----
            62,-1,-1,-1, 65,-1,62,-1,
            65,-1,-1,-1, 69,-1,65,-1,
            67,-1,-1,-1, 70,-1,72,-1,
            73,-1,76,-1, 80,-1,-1,-1,   // 導音で駆け上がりループへ
        };
        int[] gallop = { 0, 0, 12, 0, 0, 12, 0, 7 };

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;
            bool half = bar >= 8;
            int root = roots[bar];

            // ベース：Aはギャロップ、Bは重い4分踏み
            if (half) { if (st % 4 == 0) Tri(d, at, (int)(spn * 3.6f), root, 0.10f, 0.5f); }
            else Tri(d, at, (int)(spn * 0.9f), root + gallop[st], 0.085f, 1.6f);

            if (mel[n] > 0)
            {
                int hold = 1;
                while (n + hold < steps && mel[n + hold] == -1) hold++;
                Pulse(d, at, (int)(spn * hold * 0.92f), mel[n], half ? 0.06f : 0.055f, 0.25f, half ? 1.2f : 2.8f, 0.005f);
                if (half) Pulse(d, at, (int)(spn * hold * 0.92f), mel[n] - 12, 0.03f, 0.5f, 1.2f);   // Bは下オクターブで厚く
            }
            if (st == 0)
            {
                Pad(d, at, spn * 8, root + 12, 0.017f);
                Pad(d, at, spn * 8, root + 19, 0.015f);
            }

            // ドラム：Aは攻撃的、Bはハーフタイムで重く
            if (half)
            {
                if (st == 0) Kick(d, at, 0.16f);
                if (st == 4) Snare(d, at, 0.13f, rng);
                if (st % 2 == 0) Hat(d, at, 0.012f, rng);
                if (bar == 11 && st >= 4) Snare(d, at, 0.06f + 0.02f * (st - 4), rng);   // ループ前のフィル
            }
            else
            {
                if (st == 0 || st == 3 || st == 6) Kick(d, at, 0.13f);
                if (st == 4) Snare(d, at, 0.11f, rng);
                Hat(d, at, st % 2 == 1 ? 0.018f : 0.009f, rng);
                if (bar == 7 && st >= 5) Snare(d, at, 0.05f + 0.02f * (st - 5), rng);    // B部突入フィル
            }
        }
        Finish(d, step * 2f, 0.19f);
        return Bake("ProtoMidBossBgm", d);
    }

    // ==================== ボス曲「絶望」 ====================
    // Cフリジアンの葬送行進。12小節（約20秒）
    //   A(1-8小節) : 戦鼓の重い行進＋デチューン合唱のうなり＋低く這う旋律＋弔いの鐘
    //   B(9-12小節): ベースが半音ずつ上がるパニック→崩落してループ頭へ
    public static AudioClip CreateBossBgm()
    {
        const float step = 0.21f;   // 遅く重く
        int spn = (int)(SR * step);
        int steps = 96;             // 12小節
        var d = new float[spn * steps];
        var rng = new System.Random(5);

        // ベースの根音：A部はCを軸にDb(半音上)の衝撃を混ぜ、B部は半音ずつ這い上がる
        int[] roots = { 36, 36, 37, 36, 36, 44, 43, 36,   36, 37, 38, 39 };
        int[] mel =
        {
            // ---- A: 低く這う絶望の主題（長い音・半音の軋み） ----
            60,-1,-1,-1, 63,-1,60,-1,   // C4…Eb4 うめき
            61,-1,60,-1, 57,-1,-1,-1,   // Db4の軋み→A3へ沈む
            61,-1,-1,63, -1,-1,61,60,   // 半音でうねる
            60,-1,-1,-1,  0, 0,55,-1,   // 沈黙…G3が遠く
            60,-1,-1,-1, 63,-1,66,-1,   // 三全音F#4へ＝悪魔の音程
            68,-1,66,-1, 63,-1,60,-1,   // Ab4から崩れ落ちる
            67,-1,-1,-1, 62,-1,-1,-1,   // G4→D4 弔いの応答
            60,-1,-1,-1, -1,-1, 0, 0,   // 長く沈む
            // ---- B: 半音ずつ迫り上がるパニック ----
            60,61,63,64, 66,-1,63,-1,
            61,63,64,66, 68,-1,66,-1,
            63,64,66,68, 69,-1,72,-1,
            72,-1,71,-1, 66,-1,60,-1,   // 頂点から崩落
        };

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;
            bool panic = bar >= 8;
            int root = roots[bar];

            // ---- 地鳴りの低音（サブベース＋重いベース） ----
            if (st == 0) Tri(d, at, spn * 8, root - 12, 0.05f, 0.1f);                       // C1帯の地鳴り
            int[] doom = { 0, 99, 99, 0, 99, 99, 0, 99 };                                    // 付点の行進
            if (panic) Tri(d, at, (int)(spn * 0.9f), root + (st % 2) * 12, 0.09f, 1.2f);    // B部は刻んで焦らせる
            else if (doom[st] != 99) Tri(d, at, (int)(spn * 1.4f), root, 0.095f, 0.7f);

            // ---- デチューン合唱（わずかにずれた2音のうなり＝絶望感の核） ----
            if (st == 0)
            {
                float f1 = MidiF(root + 12);
                PadHz(d, at, spn * 8, f1, 0.026f);
                PadHz(d, at, spn * 8, f1 * 1.008f, 0.026f);   // +14セントのうなり
                float f2 = MidiF(root + 19);
                PadHz(d, at, spn * 8, f2, 0.018f);
                PadHz(d, at, spn * 8, f2 * 0.994f, 0.018f);
                if (!panic) Pad(d, at, spn * 8, root + 18, 0.010f);   // 三全音を薄く仕込む
            }

            // ---- 弔いの鐘（2小節ごと・A部のみ） ----
            if (!panic && bar % 2 == 0 && st == 0)
            {
                Tri(d, at, spn * 7, 60, 0.028f, 0.4f);
                Tri(d, at, spn * 7, 66, 0.012f, 0.4f);   // 鐘の中の三全音
            }

            // ---- 旋律（タイ対応） ----
            if (mel[n] > 0)
            {
                int hold = 1;
                while (n + hold < steps && mel[n + hold] == -1) hold++;
                float amp = panic ? 0.055f : 0.045f;
                Pulse(d, at, (int)(spn * hold * 0.93f), mel[n], amp, 0.5f, panic ? 2.5f : 0.8f, 0.010f);
                Pulse(d, at, (int)(spn * hold * 0.93f), mel[n] - 12, amp * 0.5f, 0.5f, panic ? 2.5f : 0.8f); // 低音の影
            }

            // ---- 戦鼓 ----
            if (panic)
            {
                Kick(d, at, 0.12f + 0.01f * (bar - 8));                     // 毎拍・迫るほど強く
                if (st == 4) Snare(d, at, 0.11f, rng);
                if (bar == 11 && st >= 2) Snare(d, at, 0.05f + 0.015f * st, rng);   // 崩落前のロール
            }
            else
            {
                if (st == 0) Kick(d, at, 0.16f);                            // ドォン…
                if (st == 3) Kick(d, at, 0.11f);                            // …ドン（付点の行進）
                if (st == 6) Snare(d, at, 0.07f, rng);
                if (bar == 7 && st >= 5) Snare(d, at, 0.05f + 0.02f * (st - 5), rng); // B部突入のフィル
            }
        }
        Finish(d, step * 2f, 0.26f);   // 深い残響で大聖堂のように
        return Bake("ProtoBossBgm", d);
    }

    // ==================== ショップ曲「商いの午後」 ====================
    // Gメジャーの陽気なスウィング。歩くベース＋跳ねるメロディで「安全地帯」の空気
    public static AudioClip CreateShopBgm()
    {
        const float step = 0.21f;
        int spn = (int)(SR * step);
        int steps = 64;
        var d = new float[spn * steps];
        var rng = new System.Random(11);

        int[] roots = { 43, 48, 43, 50, 43, 48, 50, 43 };   // G C G D / G C D G
        bool[] major = { true, true, true, true, true, true, true, true };
        int[] mel =
        {
            67, 0,71,72, 74,-1,71, 0,   // 陽気な主題
            76,-1,74,72, 71, 0,67, 0,
            67, 0,66,67, 71,-1,69,67,   // 半音のひょうきんな装飾
            69,-1,71,69, 66, 0,62, 0,
            67,71,74,-1, 79,-1,74,71,   // 明るく跳ね上がる
            76, 0,72,76, 79,-1,76,72,
            74,-1,72,71, 69,-1,66,69,   // 軽く着地へ
            67,-1,-1, 0, 62,64,66, 0,   // 締め＋おかわりの助走
        };
        int[] walk = { 0, 4, 7, 4 };   // 歩くベース（ルート→3度→5度→3度）

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;
            int root = roots[bar];

            if (st % 2 == 0) Tri(d, at, (int)(spn * 1.8f), root + walk[(st / 2) % 4], 0.07f, 0.8f); // 4分の歩くベース
            if (mel[n] > 0)
            {
                int hold = 1;
                while (n + hold < steps && mel[n + hold] == -1) hold++;
                Pulse(d, at, (int)(spn * hold * 0.9f), mel[n], 0.05f, 0.5f, 2.0f, 0.006f);  // 丸い音色で陽気に
            }
            if (st == 0)
            {
                Pad(d, at, spn * 8, root + 16, 0.014f);   // 長3度のあたたかい支え
                Pad(d, at, spn * 8, root + 19, 0.012f);
            }
            if (st % 2 == 1) Hat(d, at, 0.014f, rng);      // 裏拍のスウィング
            if (st == 0 || st == 4) Kick(d, at, 0.05f);    // ごく軽い鼓動
        }
        Finish(d, step * 1.5f, 0.20f);
        return Bake("ProtoShopBgm", d);
    }

    // ==================== 神聖樹曲「聖樹の祈り」 ====================
    // Dメジャー7thの浮遊感。ドラム無し・ハープ風アルペジオ＋長いパッドで神聖に
    public static AudioClip CreateTreeBgm()
    {
        const float step = 0.5f;
        int spn = (int)(SR * step);
        int steps = 32;   // ゆったり16秒
        var d = new float[spn * steps];

        int[] roots = { 50, 43, 45, 50 };   // D G A D
        int[][] chordTones =
        {
            new[]{ 0, 4, 7, 11 },   // メジャー7th
            new[]{ 0, 4, 7, 14 },   // add9
            new[]{ 0, 4, 7, 11 },
            new[]{ 0, 4, 7, 11 },
        };
        int[] mel =
        {
            74,-1,-1,78,  -1,-1,81,-1,   // ささやくような長い旋律
            79,-1,-1,74,  -1,-1,71,-1,
            73,-1,-1,76,  -1,-1,81,-1,
            78,-1,-1,74,  -1,-1,-1,-1,
        };

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;
            int root = roots[bar];
            var tones = chordTones[bar];

            if (st == 0)   // 温かいパッドの和音（小節まるごと持続）
            {
                Pad(d, at, spn * 8, root, 0.030f);
                Pad(d, at, spn * 8, root + tones[1], 0.022f);
                Pad(d, at, spn * 8, root + tones[2], 0.022f);
                Pad(d, at, spn * 8, root + tones[3] + 12, 0.014f);
            }
            // ハープ風アルペジオ（ゆっくり上っていく）
            Tri(d, at, (int)(spn * 1.6f), root + 12 + tones[st % 4] + (st >= 4 ? 12 : 0), 0.030f, 1.2f);
            // 高音のきらめき（まばらに）
            if (st == 3 || st == 6) Pulse(d, at, (int)(spn * 0.8f), root + 31, 0.010f, 0.5f, 4f, 0.01f);
            // 旋律
            if (mel[n] > 0)
            {
                int hold = 1;
                while (n + hold < steps && mel[n + hold] == -1) hold++;
                Pulse(d, at, (int)(spn * hold * 0.9f), mel[n], 0.028f, 0.5f, 0.6f, 0.008f);
            }
        }
        Finish(d, step * 1.2f, 0.30f);   // 深めの残響で神聖に
        return Bake("ProtoTreeBgm", d);
    }

    // ==================== 契約曲「悪魔の囁き」 ====================
    // 三全音のドローン＋弔いの鐘＋這うような半音旋律。心臓の鼓動だけが鳴る
    public static AudioClip CreateEvilBgm()
    {
        const float step = 0.46f;
        int spn = (int)(SR * step);
        int steps = 32;
        var d = new float[spn * steps];
        var rng = new System.Random(13);

        int[] mel =
        {
            60,-1,61,-1, 60,-1,59,-1,   // C-Db-C-B 半音で這う
             0, 0,63,-1, 61,-1,60,-1,
            60,-1,61,-1, 63,-1,66,-1,   // 三全音(F#)へ上がる不吉
            66,-1,63,-1, 61,-1,60,-1,
        };

        for (int n = 0; n < steps; n++)
        {
            int bar = n / 8, st = n % 8, at = n * spn;

            if (st == 0)   // ドローン：C最低音＋三全音F#が終始うなる
            {
                Pad(d, at, spn * 8, 36, 0.040f);        // C2
                Pad(d, at, spn * 8, 42, 0.022f);        // F#2（悪魔の音程）
                Pad(d, at, spn * 8, 48, 0.016f);
            }
            if (st == 0) { // 弔いの鐘（小節頭に長く減衰）
                Tri(d, at, spn * 6, 72, 0.030f, 0.5f);
                Tri(d, at, spn * 6, 79, 0.012f, 0.5f);
            }
            if (mel[n] > 0)   // 這う旋律
            {
                int hold = 1;
                while (n + hold < steps && mel[n + hold] == -1) hold++;
                Pulse(d, at, (int)(spn * hold * 0.92f), mel[n], 0.026f, 0.5f, 0.7f, 0.012f);
            }
            if (st == 0 || st == 1) Kick(d, at, st == 0 ? 0.09f : 0.06f);   // ドクン…と心臓の鼓動
            if (bar == 3 && st == 7) Snare(d, at, 0.03f, rng);              // かすかなノイズ
        }
        Finish(d, step * 1.3f, 0.32f);
        return Bake("ProtoEvilBgm", d);
    }

    // 嵐の山頂BGM: 短調のドローン＋半音のきしみで不穏に（ボスエリア用）
    public static AudioClip CreateStormBgm()
    {
        const int sampleRate = 44100;
        const float noteDur = 0.42f; // 重く遅いテンポ

        // Dマイナーの不穏なオスティナート（D-E♭の半音が緊張を生む）
        float[] melody =
        {
            293.66f, 311.13f, 293.66f, 220.00f,  // D Eb D A(低)
            293.66f, 311.13f, 349.23f, 311.13f,  // D Eb F Eb
            293.66f, 311.13f, 293.66f, 220.00f,
            174.61f, 185.00f, 174.61f, 146.83f,  // F3 Gb3 F3 D3（沈む）
        };
        float[] bass = { 73.42f, 73.42f, 69.30f, 73.42f }; // D2のドローン（時々半音下がる）

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
                float p = s / (float)samplesPerNote;
                float env = Mathf.Exp(-2.5f * p);

                // メロディ: 細い矩形波がきしむ
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * 0.04f * env;
                // ドローン: 低いサインがずっと唸る（減衰しない）
                float drone = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * 0.06f;
                // 5度上を薄く重ねて広がりを出す
                float fifth = Mathf.Sin(2f * Mathf.PI * bassFreq * 1.5f * t) * 0.025f;

                data[idx] = square + drone + fifth;
            }
        }

        var clip = AudioClip.Create("ProtoStormBgm", total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 雷鳴（バリッ！という亀裂音＋ゴロゴロと続く残響）
    public static AudioClip CreateThunder()
    {
        const int sampleRate = 44100;
        const float dur = 1.4f;
        int total = (int)(sampleRate * dur);
        var data = new float[total];
        var rng = new System.Random(13);

        float low = 0f; // ローパス用（ゴロゴロ感）
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total;
            float noise = (float)rng.NextDouble() * 2f - 1f;

            // 最初の0.1秒は鋭い亀裂音（バリッ）
            float crack = p < 0.07f ? noise * 0.5f * (1f - p / 0.07f) : 0f;

            // 残りは低くこもった轟き（ノイズをならして低音化）
            low = Mathf.Lerp(low, noise, 0.04f);
            float rumble = low * 0.55f * Mathf.Exp(-2.2f * p);

            data[i] = crack + rumble;
        }

        var clip = AudioClip.Create("ThunderSfx", total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // シナジー発動音（キラリラン♪ と駆け上がる祝福チャイム）
    public static AudioClip CreateSynergyChime()
    {
        const float dur = 0.55f;
        int total = (int)(SR * dur);
        var d = new float[total];
        float[] notes = { 783.99f, 987.77f, 1174.66f, 1567.98f };   // G5 B5 D6 G6
        int noteLen = total / 5;
        for (int n = 0; n < notes.Length; n++)
        {
            int start = n * (int)(noteLen * 0.8f);
            for (int s = 0; s < noteLen * 2 && start + s < total; s++)
            {
                float p = s / (float)(noteLen * 2);
                float env = Mathf.Exp(-4f * p);
                float t = s / (float)SR;
                d[start + s] += Mathf.Sin(2f * Mathf.PI * notes[n] * t) * 0.10f * env;
                d[start + s] += Mathf.Sin(2f * Mathf.PI * notes[n] * 2f * t) * 0.03f * env;   // 倍音でキラキラ
            }
        }
        for (int i = 0; i < total; i++) d[i] = Mathf.Clamp(d[i], -0.9f, 0.9f);
        return Bake("SynergyChime", d);
    }

    // 特殊マス起動音（ジャキーン♪ と力が宿る二段音）
    public static AudioClip CreateSpecialChime()
    {
        const float dur = 0.45f;
        int total = (int)(SR * dur);
        var d = new float[total];
        float[] notes = { 587.33f, 880f };   // D5 → A5（力強い五度跳躍）
        for (int n = 0; n < notes.Length; n++)
        {
            int start = n * (int)(SR * 0.10f);
            int len = (int)(SR * 0.32f);
            for (int s = 0; s < len && start + s < total; s++)
            {
                float p = s / (float)len;
                float env = Mathf.Exp(-5f * p);
                float t = s / (float)SR;
                d[start + s] += Mathf.Sin(2f * Mathf.PI * notes[n] * t) * 0.11f * env;
                d[start + s] += Mathf.Sign(Mathf.Sin(2f * Mathf.PI * notes[n] * 0.5f * t)) * 0.03f * env; // 低い矩形で芯を出す
            }
        }
        for (int i = 0; i < total; i++) d[i] = Mathf.Clamp(d[i], -0.9f, 0.9f);
        return Bake("SpecialChime", d);
    }

    // コイン獲得音（チャリン♪ 高く軽い二連ベル）
    public static AudioClip CreateCoinChime()
    {
        const float dur = 0.30f;
        int total = (int)(SR * dur);
        var d = new float[total];
        float[] notes = { 1318.51f, 1975.53f };   // E6 → B6（軽やかな五度）
        for (int n = 0; n < notes.Length; n++)
        {
            int start = n * (int)(SR * 0.06f);
            int len = (int)(SR * 0.22f);
            for (int s = 0; s < len && start + s < total; s++)
            {
                float p = s / (float)len;
                float env = Mathf.Exp(-7f * p);
                float t = s / (float)SR;
                d[start + s] += Mathf.Sin(2f * Mathf.PI * notes[n] * t) * 0.09f * env;
                d[start + s] += Mathf.Sin(2f * Mathf.PI * notes[n] * 2.01f * t) * 0.03f * env;   // わずかにずれた倍音で金属感
            }
        }
        for (int i = 0; i < total; i++) d[i] = Mathf.Clamp(d[i], -0.9f, 0.9f);
        return Bake("CoinChime", d);
    }

    // 呪い発動音（ズン……と沈む不協和音）
    public static AudioClip CreateCurseHit()
    {
        const float dur = 0.5f;
        int total = (int)(SR * dur);
        var d = new float[total];
        float[] notes = { 138.59f, 146.83f, 92.5f };   // C#3+D3の濁り＋低いF#2
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total;
            float env = Mathf.Exp(-3.5f * p);
            float t = i / (float)SR;
            float f0 = notes[0] * (1f - 0.15f * p);   // 音程が沈んでいく
            d[i] += Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.14f * env;
            d[i] += Mathf.Sin(2f * Mathf.PI * notes[1] * t) * 0.10f * env;
            d[i] += Mathf.Sign(Mathf.Sin(2f * Mathf.PI * notes[2] * t)) * 0.05f * env;
        }
        for (int i = 0; i < total; i++) d[i] = Mathf.Clamp(d[i], -0.9f, 0.9f);
        return Bake("CurseHit", d);
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

    // 通常攻撃の詠唱・放出音（キラッと上昇→シュッと放つ）
    public static AudioClip CreateMagicCast()
    {
        const int sampleRate = 44100; const float dur = 0.4f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        var rng = new System.Random(21);
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total; float t = (float)i / sampleRate;
            float env = Mathf.Sin(p * Mathf.PI); env *= env;
            float f = Mathf.Lerp(520f, 1200f, p);                        // 上昇するキラキラ
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) * 0.10f * env;
            float shimmer = Mathf.Sin(2f * Mathf.PI * f * 2.01f * t) * 0.05f * env;
            float air = ((float)rng.NextDouble() * 2f - 1f) * 0.06f * env;
            data[i] = tone + shimmer + air;
        }
        var clip = AudioClip.Create("MagicCast", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // 大技のタメ音（歪んだ重低音の唸り＋加速トレモロ＋上昇スクリーチ＋バチバチ放電。激しく盛り上がる）
    public static AudioClip CreateBigCharge()
    {
        const int sampleRate = 44100; const float dur = 1.9f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        var rng = new System.Random(33);
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total; float t = (float)i / sampleRate;
            float env = p * p * p * 0.6f + p * 0.4f;                       // 加速して大きく
            float f = Mathf.Lerp(45f, 340f, p * p);                        // 加速上昇する唸り
            float sub = Mathf.Sin(2f * Mathf.PI * (f * 0.5f) * t) * 0.20f * env;
            float low = Mathf.Sin(2f * Mathf.PI * f * t);
            // 歪ませてグォォという迫力に（ソフトクリップ）
            float growl = Mathf.Clamp(low * (1.6f + 2.5f * p), -1f, 1f) * 0.22f * env;
            float harm = (Mathf.Sin(2f * Mathf.PI * f * 1.5f * t) + Mathf.Sin(2f * Mathf.PI * f * 2f * t) + Mathf.Sin(2f * Mathf.PI * f * 3f * t)) * 0.05f * env;
            // 上昇するスクリーチ（金切り声のように張り詰める）
            float screech = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(600f, 3400f, p * p) * t) * 0.06f * (p * p);
            // 加速する激しいトレモロ
            float trem = Mathf.Sin(2f * Mathf.PI * (8f + p * p * 45f) * t) * 0.5f + 0.5f;
            // 溜まるほど増えるバチバチ放電
            float sparkGate = ((float)rng.NextDouble() < (0.02f + p * 0.10f)) ? 1f : 0f;
            float spark = ((float)rng.NextDouble() * 2f - 1f) * 0.35f * sparkGate * (0.3f + p);
            float air = ((float)rng.NextDouble() * 2f - 1f) * 0.05f * env;
            data[i] = Mathf.Clamp((sub + growl + harm) * (0.45f + 0.55f * trem) + screech + spark + air, -1f, 1f);
        }
        int fade = (int)(sampleRate * 0.05f);
        for (int i = 0; i < fade; i++) data[total - 1 - i] *= i / (float)fade;
        var clip = AudioClip.Create("BigCharge", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // 大技の放出・着弾音（ドゴォン！と長く尾を引く大爆発。二段階の炸裂＋轟き）
    public static AudioClip CreateBigRelease()
    {
        const int sampleRate = 44100; const float dur = 1.1f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        var rng = new System.Random(44);
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total; float t = (float)i / sampleRate;
            float env = Mathf.Exp(-3.2f * p);
            float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 28f, Mathf.Sqrt(p)) * t) * 0.40f * env;   // 落ちる主爆発
            float sub = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 22f, p) * t) * 0.22f * Mathf.Exp(-2f * p); // サブの押し
            float crack = ((float)rng.NextDouble() * 2f - 1f) * 0.5f * Mathf.Exp(-9f * p);                    // 初撃の破裂
            // 二段目の炸裂（少し遅れてもう一発）
            float p2 = Mathf.Clamp01((p - 0.14f) / 0.86f);
            float crack2 = ((float)rng.NextDouble() * 2f - 1f) * 0.32f * Mathf.Exp(-8f * p2) * (p > 0.14f ? 1f : 0f);
            float rumble = Mathf.Sin(2f * Mathf.PI * 48f * t) * 0.14f * Mathf.Exp(-1.8f * p);                 // 長い轟き
            data[i] = Mathf.Clamp(boom + sub + crack + crack2 + rumble, -1f, 1f);
        }
        int fade = (int)(sampleRate * 0.06f);
        for (int i = 0; i < fade; i++) data[total - 1 - i] *= i / (float)fade;
        var clip = AudioClip.Create("BigRelease", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // リーチ警報（パチンコ風「ウィンウィン」と迫り上がるサイレン）
    public static AudioClip CreateReachAlarm()
    {
        const int sampleRate = 44100; const float dur = 0.9f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total; float t = (float)i / sampleRate;
            float env = Mathf.Sin(p * Mathf.PI); env = Mathf.Sqrt(env);
            // うねるサイレン（2音間を往復しながら全体が上昇）
            float wob = Mathf.Sin(2f * Mathf.PI * 7f * t) * 0.5f + 0.5f;
            float f = Mathf.Lerp(620f, 980f, p) + wob * 180f;
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) * 0.16f * env;
            float harm = Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.05f * env;
            data[i] = tone + harm;
        }
        var clip = AudioClip.Create("ReachAlarm", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // 鼓動（ドクン…ドクン…という低い2拍。リーチ中の緊張感）
    public static AudioClip CreateHeartbeat()
    {
        const int sampleRate = 44100; const float dur = 0.55f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = (float)i / sampleRate;
            // 1拍目（強）と2拍目（弱）
            float e1 = Mathf.Exp(-18f * Mathf.Max(0f, t));
            float e2 = t > 0.22f ? Mathf.Exp(-18f * (t - 0.22f)) * 0.6f : 0f;
            float f = 55f;
            data[i] = (Mathf.Sin(2f * Mathf.PI * f * t) * e1 + Mathf.Sin(2f * Mathf.PI * f * (t - 0.22f)) * e2) * 0.4f;
        }
        var clip = AudioClip.Create("Heartbeat", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // 大当たりファンファーレ（駆け上がるアルペジオ＋キラキラ倍音。昇天感）
    public static AudioClip CreateJackpotFanfare()
    {
        const int sampleRate = 44100; const float dur = 1.5f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        // C-E-G-C-E-G-C と駆け上がる
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568.0f, 2093.0f };
        float step = dur / (notes.Length + 2);   // 最後の音を長めに
        for (int i = 0; i < total; i++)
        {
            float t = (float)i / sampleRate;
            int ni = Mathf.Min(notes.Length - 1, (int)(t / step));
            float lt = t - ni * step;                      // 音内の経過
            bool last = ni == notes.Length - 1;
            float env = Mathf.Exp(-(last ? 2.2f : 9f) * lt) * (last ? 1.2f : 1f);
            float f = notes[ni];
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) * 0.16f * env;
            float oct = Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.07f * env;
            float spark = Mathf.Sin(2f * Mathf.PI * f * 3f * t) * 0.04f * env;
            data[i] = Mathf.Clamp(tone + oct + spark, -1f, 1f);
        }
        int fade = (int)(sampleRate * 0.08f);
        for (int i = 0; i < fade; i++) data[total - 1 - i] *= i / (float)fade;
        var clip = AudioClip.Create("JackpotFanfare", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // コインシャワー（チャリンチャリンと降り注ぐ高音の連鎖）
    public static AudioClip CreateCoinShower()
    {
        const int sampleRate = 44100; const float dur = 1.4f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        var rng = new System.Random(77);
        // ランダムなタイミングで12枚のコイン音を重ねる
        for (int c = 0; c < 12; c++)
        {
            float start = (float)rng.NextDouble() * (dur - 0.25f);
            float f = 1900f + (float)rng.NextDouble() * 1400f;
            int s0 = (int)(start * sampleRate);
            int len = (int)(0.22f * sampleRate);
            for (int i = 0; i < len && s0 + i < total; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Exp(-14f * t);
                data[s0 + i] += (Mathf.Sin(2f * Mathf.PI * f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * f * 1.5f * t) * 0.3f) * 0.09f * env;
            }
        }
        for (int i = 0; i < total; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        var clip = AudioClip.Create("CoinShower", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // ガード音（金属的な「キィン！」という防御音）
    public static AudioClip CreateGuard()
    {
        const int sampleRate = 44100; const float dur = 0.35f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        var rng = new System.Random(51);
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total; float t = (float)i / sampleRate;
            float env = Mathf.Exp(-7f * p);
            float clang = ((float)rng.NextDouble() * 2f - 1f) * 0.35f * Mathf.Exp(-22f * p);   // 硬い当たり
            float ring = (Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1810f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 2650f * t) * 0.2f) * 0.14f * env;   // 金属の余韻
            data[i] = clang + ring;
        }
        var clip = AudioClip.Create("GuardSfx", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }

    // 回復音（やわらかな上昇アルペジオ＋澄んだ響き）
    public static AudioClip CreateHealCast()
    {
        const int sampleRate = 44100; const float dur = 0.9f;
        int total = (int)(sampleRate * dur); var data = new float[total];
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };   // C E G C（明るい）
        for (int i = 0; i < total; i++)
        {
            float p = i / (float)total; float t = (float)i / sampleRate;
            float env = Mathf.Sin(p * Mathf.PI) * 0.9f;
            int ni = Mathf.Min(notes.Length - 1, (int)(p * notes.Length * 1.2f));
            float f = notes[ni];
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) * 0.12f * env;
            float bell = Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.05f * env;
            data[i] = tone + bell;
        }
        var clip = AudioClip.Create("HealCast", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }
}
