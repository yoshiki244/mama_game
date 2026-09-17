// Goblin knife-attack animation builder.
// Same rig as GoblinIdleBuilder. Instead of waves, the motion is a list of key poses
// that are eased between and baked at 60 fps. The legs are solved with two-bone IK at
// every sample, so the feet only move where a pose asks them to.
//
// Arm directions are WORLD angles of the shoulder-to-wrist line, in degrees
// (0 = right, 90 = up). Clock face: 7 o'clock = -120, 9 = 180, 11 = 120, 5 = -60.
// They are unwrapped so the interpolation sweeps the intended way round.
//
//   bone_2 hips   bone_21 torso   bone_28 head
//   bone_25/26/27 right upper arm / forearm / hand+knife
//   bone_22/23/24 left  upper arm / forearm / hand
//   bone_15/16/17 left leg        bone_18/19/20 right leg
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GoblinAttackBuilder
{
    const string PsbPath        = "Assets/Sprites/Characters/goblin_rig.psb";
    const string IdleClipPath   = "Assets/Animations/goblin_Idle.anim";
    const string ClipPath       = "Assets/Animations/goblin_KnifeAttack.anim";
    const string ControllerPath = "Assets/Animations/goblin_Rig.controller";
    const string ScenePath      = "Assets/Scenes/goblin_anim_test.unity";
    const string TriggerName    = "Attack";

    const float SampleRate = 60f;
    const float StepLift   = 0.04f;   // feet clear the ground a little while the stance widens

    enum Ease { InOut, In, Out }

    // Everything except the arm angles is an offset from the idle pose at t = 0.
    // float.NaN on an arm angle means "the idle arm angle".
    struct Pose
    {
        public float time; public Ease ease;
        public float hipsX, hipsY, hipsRot, torso, head;
        public float rArm, rElbow, rKnife;
        public float lArm, lElbow, lWrist;
        public float footL, footR;
    }

    static Pose P(float time, Ease ease,
                  float hipsX, float hipsY, float hipsRot, float torso, float head,
                  float rArm, float rElbow, float rKnife,
                  float lArm, float lElbow, float lWrist,
                  float footL, float footR)
    {
        var p = new Pose();
        p.time = time; p.ease = ease;
        p.hipsX = hipsX; p.hipsY = hipsY; p.hipsRot = hipsRot; p.torso = torso; p.head = head;
        p.rArm = rArm; p.rElbow = rElbow; p.rKnife = rKnife;
        p.lArm = lArm; p.lElbow = lElbow; p.lWrist = lWrist;
        p.footL = footL; p.footR = footR;
        return p;
    }

    const float Idle = float.NaN;

    // The knife blade points this far from the hand bone. Measured off the artwork at
    // rest: grip (252, 559) to tip (132, 702) in PSD pixels = -130 degrees.
    const float KnifeArtAngle = -130f;

    // Sign conventions: + rotation = counter-clockwise on screen. The goblin faces left,
    // so + torso leans him forward and - leans him back. Right elbow bends with +,
    // left elbow bends with -. rKnife is the blade's WORLD angle (same clock mapping as
    // the arms): a value above the arm angle trails the swing, below it whips ahead.
    static readonly Pose[] Keys =
    {
        //  time  ease        hipsX  hipsY  hipRot torso  head   rArm   rElb  rKnife  lArm  lElb  lWri   footL  footR
        P(0.00f, Ease.InOut,  0.00f,  0.00f,  0f,   0f,   0f,   Idle,   0f,  Idle,   Idle,   0f,   0f,  0.00f,  0.00f),
        // 1-2) widen the stance, sink the hips, drop arm and knife to 7 o'clock, left hand to 5
        P(0.32f, Ease.InOut,  0.05f, -0.26f,  3f,   6f,  -3f,  -120f,  15f, -120f,  -60f, -10f,   8f,  0.08f, -0.16f),
        // held anticipation - a touch more coil, the blade cocks back slightly
        P(0.44f, Ease.Out,    0.06f, -0.29f,  3f,   7f,  -3f,  -116f,  17f, -112f,  -62f, -11f,   9f,  0.08f, -0.16f),
        // 3) strike up to 11 o'clock with the blade still trailing; torso leans back,
        //    left arm is flung back by the recoil
        P(0.56f, Ease.In,    -0.05f, -0.14f, -3f,  -6f,   4f,  -240f, -12f, -228f,  -20f,  14f, -10f,  0.08f, -0.16f),
        // follow-through: the arm carries past, the blade whips further
        P(0.66f, Ease.Out,   -0.07f, -0.12f, -4f,  -8f,   5f,  -252f, -15f, -264f,  -14f,  18f, -14f,  0.08f, -0.16f),
        // settle back onto 11 o'clock, blade in line
        P(0.86f, Ease.InOut, -0.05f, -0.15f, -3f,  -5f,   3f,  -240f, -10f, -240f,  -22f,  10f,  -8f,  0.08f, -0.16f),
        // 4) back to the idle pose (matches goblin_Idle at t = 0 exactly)
        P(1.45f, Ease.InOut,  0.00f,  0.00f,  0f,   0f,   0f,   Idle,   0f,  Idle,   Idle,   0f,   0f,  0.00f,  0.00f),
    };

    class Leg
    {
        public Transform thigh, shin, foot;
        public float l1, l2, bendSign, footWorldZ;
        public Vector3 ankleBind;
    }

    [MenuItem("MamaGame/BoneAnimTest/Build Goblin Knife Attack")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PsbPath);
        var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
        if (source == null || idleClip == null)
        {
            Debug.LogError("GoblinAttackBuilder: needs " + PsbPath + " and " + IdleClipPath + " (run Build Goblin Idle first).");
            return;
        }

        // Work on a throwaway instance so the scene object is never left posed.
        var work = (GameObject)Object.Instantiate(source);
        work.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var clip = Bake(work, idleClip);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) != null) AssetDatabase.DeleteAsset(ClipPath);
            AssetDatabase.CreateAsset(clip, ClipPath);
            WireController(clip, idleClip);
            WireScene();
            AssetDatabase.SaveAssets();
        }
        finally
        {
            Object.DestroyImmediate(work);
        }
    }

    static AnimationClip Bake(GameObject work, AnimationClip idleClip)
    {
        var root = work.transform;
        var bones = new Dictionary<string, Transform>();
        foreach (var t in work.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("bone_")) bones[t.name] = t;

        // Legs are measured at the bind pose, which is where the idle keeps the feet.
        var legs = new Leg[] { MakeLeg(bones, "bone_15", "bone_16", "bone_17"),
                               MakeLeg(bones, "bone_18", "bone_19", "bone_20") };

        // Idle pose at t = 0 is both the start and the end of the attack.
        idleClip.SampleAnimation(work, 0f);
        var idleEuler = new Dictionary<string, Vector3>();
        var idlePos   = new Dictionary<string, Vector3>();
        foreach (var kv in bones) { idleEuler[kv.Key] = kv.Value.localEulerAngles; idlePos[kv.Key] = kv.Value.localPosition; }
        float idleR = ArmAngle(bones["bone_25"], bones["bone_27"]);
        float idleL = ArmAngle(bones["bone_22"], bones["bone_24"]);
        // Offset from the hand bone's world Z to the blade direction. In the idle pose
        // the blade points at KnifeArtAngle by definition.
        float knifeOffset = KnifeArtAngle - bones["bone_27"].eulerAngles.z;
        float idleKnife = KnifeArtAngle;

        var recorded = new[] { "bone_2", "bone_21", "bone_28", "bone_25", "bone_26", "bone_27",
                               "bone_22", "bone_23", "bone_24",
                               "bone_15", "bone_16", "bone_17", "bone_18", "bone_19", "bone_20" };
        var zCurves = new Dictionary<string, AnimationCurve>();
        var prevZ = new Dictionary<string, float>();
        foreach (var n in recorded) zCurves[n] = new AnimationCurve();
        var hipsX = new AnimationCurve();
        var hipsY = new AnimationCurve();

        float length = Keys[Keys.Length - 1].time;
        int samples = Mathf.RoundToInt(length * SampleRate);
        float maxDrift = 0f, maxClamp = 0f;

        for (int i = 0; i <= samples; i++)
        {
            float t = length * i / samples;
            float lift;
            var p = Evaluate(t, idleR, idleL, idleKnife, out lift);

            foreach (var kv in bones)
            {
                kv.Value.localEulerAngles = idleEuler[kv.Key];
                kv.Value.localPosition = idlePos[kv.Key];
            }

            var hips = bones["bone_2"];
            hips.localPosition = idlePos["bone_2"] + new Vector3(p.hipsX, p.hipsY, 0f);
            AddZ(hips, idleEuler["bone_2"].z + p.hipsRot);
            AddZ(bones["bone_21"], idleEuler["bone_21"].z + p.torso);
            AddZ(bones["bone_28"], idleEuler["bone_28"].z + p.head);

            // Arms: set the elbow first, then swing the whole arm from the shoulder so the
            // shoulder-to-wrist line lands exactly on the requested clock angle.
            PoseArm(bones["bone_25"], bones["bone_26"], bones["bone_27"],
                    idleEuler["bone_26"].z + p.rElbow, idleEuler["bone_27"].z, p.rArm);
            // then turn the wrist so the blade itself points where the key asks
            var hand = bones["bone_27"];
            float blade = hand.eulerAngles.z + knifeOffset;
            AddZ(hand, hand.localEulerAngles.z + Mathf.DeltaAngle(blade, p.rKnife));
            PoseArm(bones["bone_22"], bones["bone_23"], bones["bone_24"],
                    idleEuler["bone_23"].z + p.lElbow, idleEuler["bone_24"].z + p.lWrist, p.lArm);

            // Legs: IK each ankle onto its (possibly widened) planted spot.
            var targets = new[] { legs[0].ankleBind + new Vector3(p.footL, lift, 0f),
                                  legs[1].ankleBind + new Vector3(p.footR, lift, 0f) };
            for (int k = 0; k < legs.Length; k++)
            {
                float clamp = SolveLeg(legs[k], ref targets[k]);
                maxClamp = Mathf.Max(maxClamp, clamp);
                maxDrift = Mathf.Max(maxDrift, Vector3.Distance(legs[k].foot.position, targets[k]));
            }

            foreach (var n in recorded)
            {
                float z = bones[n].localEulerAngles.z;
                if (prevZ.ContainsKey(n)) z = prevZ[n] + Mathf.DeltaAngle(prevZ[n], z);
                prevZ[n] = z;
                zCurves[n].AddKey(new Keyframe(t, z));
            }
            hipsX.AddKey(new Keyframe(t, hips.localPosition.x));
            hipsY.AddKey(new Keyframe(t, hips.localPosition.y));
        }

        var clip = new AnimationClip();
        clip.frameRate = SampleRate;
        foreach (var n in recorded)
        {
            var path = AnimationUtility.CalculateTransformPath(bones[n], root);
            var c = zCurves[n];
            for (int k = 0; k < c.length; k++) c.SmoothTangents(k, 0f);
            var e = idleEuler[n];
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.x", Constant(e.x, length));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.y", Constant(e.y, length));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z", c);
        }
        var hipsPath = AnimationUtility.CalculateTransformPath(bones["bone_2"], root);
        for (int k = 0; k < hipsX.length; k++) { hipsX.SmoothTangents(k, 0f); hipsY.SmoothTangents(k, 0f); }
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.x", hipsX);
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.y", hipsY);
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.z", Constant(idlePos["bone_2"].z, length));

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        Debug.Log("GoblinAttackBuilder: " + length + "s, " + (samples + 1) + " samples, idle arms R="
                  + idleR.ToString("F1") + " L=" + idleL.ToString("F1")
                  + ", max ankle drift " + (maxDrift * 100f).ToString("F3") + " px"
                  + ", max stance clamp " + (maxClamp * 100f).ToString("F2") + " px");
        return clip;
    }

    static Pose Evaluate(float t, float idleR, float idleL, float idleKnife, out float lift)
    {
        lift = 0f;
        int k = 1;
        while (k < Keys.Length - 1 && t > Keys[k].time) k++;
        var a = Resolve(Keys[k - 1], idleR, idleL, idleKnife);
        var b = Resolve(Keys[k], idleR, idleL, idleKnife);
        float span = b.time - a.time;
        float u = span > 0f ? Mathf.Clamp01((t - a.time) / span) : 1f;
        float e;
        switch (b.ease)
        {
            case Ease.In:  e = u * u; break;
            case Ease.Out: e = 1f - (1f - u) * (1f - u); break;
            default:       e = u * u * (3f - 2f * u); break;
        }

        // A foot that is moving this segment is lifted slightly so it steps, not slides.
        if (!Mathf.Approximately(a.footL, b.footL) || !Mathf.Approximately(a.footR, b.footR))
            lift = StepLift * Mathf.Sin(Mathf.PI * u);

        var r = new Pose();
        r.hipsX = Mathf.Lerp(a.hipsX, b.hipsX, e);   r.hipsY = Mathf.Lerp(a.hipsY, b.hipsY, e);
        r.hipsRot = Mathf.Lerp(a.hipsRot, b.hipsRot, e);
        r.torso = Mathf.Lerp(a.torso, b.torso, e);   r.head = Mathf.Lerp(a.head, b.head, e);
        r.rArm = Mathf.Lerp(a.rArm, b.rArm, e);      r.rElbow = Mathf.Lerp(a.rElbow, b.rElbow, e);
        r.rKnife = Mathf.Lerp(a.rKnife, b.rKnife, e);
        r.lArm = Mathf.Lerp(a.lArm, b.lArm, e);      r.lElbow = Mathf.Lerp(a.lElbow, b.lElbow, e);
        r.lWrist = Mathf.Lerp(a.lWrist, b.lWrist, e);
        r.footL = Mathf.Lerp(a.footL, b.footL, e);   r.footR = Mathf.Lerp(a.footR, b.footR, e);
        return r;
    }

    static Pose Resolve(Pose p, float idleR, float idleL, float idleKnife)
    {
        if (float.IsNaN(p.rArm)) p.rArm = idleR;
        if (float.IsNaN(p.lArm)) p.lArm = idleL;
        if (float.IsNaN(p.rKnife)) p.rKnife = idleKnife;
        return p;
    }

    static float ArmAngle(Transform shoulder, Transform wrist)
    {
        var d = wrist.position - shoulder.position;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    static void AddZ(Transform t, float localZ)
    {
        var e = t.localEulerAngles;
        t.localEulerAngles = new Vector3(e.x, e.y, localZ);
    }

    static void PoseArm(Transform upper, Transform fore, Transform hand, float elbowZ, float wristZ, float worldAngle)
    {
        AddZ(fore, elbowZ);
        AddZ(hand, wristZ);
        float current = ArmAngle(upper, hand);
        AddZ(upper, upper.localEulerAngles.z + Mathf.DeltaAngle(current, worldAngle));
    }

    static Leg MakeLeg(Dictionary<string, Transform> bones, string thigh, string shin, string foot)
    {
        var leg = new Leg();
        leg.thigh = bones[thigh]; leg.shin = bones[shin]; leg.foot = bones[foot];
        leg.l1 = Vector3.Distance(leg.thigh.position, leg.shin.position);
        leg.l2 = Vector3.Distance(leg.shin.position, leg.foot.position);
        leg.ankleBind = leg.foot.position;
        leg.footWorldZ = leg.foot.eulerAngles.z;
        Vector3 d = leg.ankleBind - leg.thigh.position;
        float baseAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        leg.bendSign = Mathf.DeltaAngle(baseAng, leg.thigh.eulerAngles.z) >= 0f ? 1f : -1f;
        return leg;
    }

    // Returns how far the target had to be pulled in because the leg could not reach it.
    static float SolveLeg(Leg leg, ref Vector3 target)
    {
        Vector3 P = leg.thigh.position;
        Vector3 d = target - P; d.z = 0f;
        float reach = leg.l1 + leg.l2 - 1e-3f;
        float clamp = 0f;
        if (d.magnitude > reach)
        {
            clamp = d.magnitude - reach;
            target = P + d.normalized * reach;
            d = target - P;
        }
        float dist = Mathf.Max(d.magnitude, Mathf.Abs(leg.l1 - leg.l2) + 1e-4f);
        float baseAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float cosA = (dist * dist + leg.l1 * leg.l1 - leg.l2 * leg.l2) / (2f * dist * leg.l1);
        float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;

        SetWorldZ(leg.thigh, baseAng + leg.bendSign * a);
        Vector3 d2 = target - leg.shin.position; d2.z = 0f;
        SetWorldZ(leg.shin, Mathf.Atan2(d2.y, d2.x) * Mathf.Rad2Deg);
        SetWorldZ(leg.foot, leg.footWorldZ);
        return clamp;
    }

    static void SetWorldZ(Transform t, float worldZ)
    {
        float parentZ = t.parent != null ? t.parent.eulerAngles.z : 0f;
        var e = t.localEulerAngles;
        t.localEulerAngles = new Vector3(e.x, e.y, worldZ - parentZ);
    }

    static AnimationCurve Constant(float v, float length)
    {
        var c = new AnimationCurve();
        c.AddKey(new Keyframe(0f, v));
        c.AddKey(new Keyframe(length, v));
        return c;
    }

    // Idle --(Attack trigger)--> KnifeAttack --(on finish)--> Idle
    static void WireController(AnimationClip clip, AnimationClip idleClip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        bool hasParam = false;
        foreach (var prm in controller.parameters) if (prm.name == TriggerName) hasParam = true;
        if (!hasParam) controller.AddParameter(TriggerName, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        AnimatorState idle = null, attack = null;
        foreach (var cs in sm.states)
        {
            if (cs.state.name == "Idle") idle = cs.state;
            if (cs.state.name == "KnifeAttack") attack = cs.state;
        }
        if (idle == null) { idle = sm.AddState("Idle"); idle.motion = idleClip; sm.defaultState = idle; }
        if (attack == null) attack = sm.AddState("KnifeAttack");
        attack.motion = clip;

        foreach (var tr in idle.transitions) if (tr.destinationState == attack) idle.RemoveTransition(tr);
        foreach (var tr in attack.transitions) attack.RemoveTransition(tr);

        var toAttack = idle.AddTransition(attack);
        toAttack.hasExitTime = false;
        toAttack.hasFixedDuration = true;
        toAttack.duration = 0.1f;   // blends in from wherever the idle loop happens to be
        toAttack.AddCondition(AnimatorConditionMode.If, 0f, TriggerName);

        var toIdle = attack.AddTransition(idle);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 1f;
        toIdle.hasFixedDuration = true;
        toIdle.duration = 0f;       // the attack already ends on the idle's first frame

        EditorUtility.SetDirty(controller);
    }

    static void WireScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var goblin = GameObject.Find("Goblin");
        if (goblin == null) { Debug.LogWarning("GoblinAttackBuilder: Goblin not found in " + ScenePath); return; }
        if (goblin.GetComponent<AnimTriggerTester>() == null)
        {
            var tester = goblin.AddComponent<AnimTriggerTester>();
            tester.triggerName = TriggerName;
        }
        // The raised knife reaches well above the head; frame for it.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.orthographicSize = 5.3f;
            cam.transform.position = new Vector3(0f, 5.4f, -10f);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
