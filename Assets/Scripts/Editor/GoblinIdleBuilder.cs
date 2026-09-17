// Goblin battle-idle animation builder.
// Drives the SpriteSkin bones baked into goblin_rig.psb by the Skinning Editor.
//
// The legs are NOT keyframed by hand. The hips move, and a two-bone IK solve runs
// at every sample to put each ankle back on its bind-pose spot, so the feet stay
// nailed to the ground while the thighs and shins take up the slack. The foot bones
// hold their bind-pose WORLD rotation, so the soles never tilt.
//
// Bone names are the importer defaults on purpose: keeping them means the clip
// survives a re-Apply of the rig in the Skinning Editor.
//
//   bone_2            hips / root
//   bone_15/16/17     left  thigh / shin / foot
//   bone_18/19/20     right thigh / shin / foot
//   bone_21           torso
//   bone_22/23/24     left  upper arm / forearm / hand
//   bone_25/26/27     right upper arm / forearm / hand (knife)
//   bone_28           head
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GoblinIdleBuilder
{
    const string PsbPath        = "Assets/Sprites/Characters/goblin_rig.psb";
    const string AnimFolder     = "Assets/Animations";
    const string ClipPath       = "Assets/Animations/goblin_Idle.anim";
    const string ControllerPath = "Assets/Animations/goblin_Rig.controller";
    const string ScenePath      = "Assets/Scenes/goblin_anim_test.unity";

    // Canvas 1536x1024, PPU 100, prefab origin = bottom-center of the document.
    const float RootOffsetX = 2.68f;

    const float ClipLength = 7.2f;   // slow and heavy - he is braced, not fidgeting
    const int   Samples    = 48;

    const string HipsBone = "bone_2";

    struct Wave
    {
        public string bone;
        public float cycles, amp, phase;
        public Wave(string b, float c, float a, float ph) { bone = b; cycles = c; amp = a; phase = ph; }
    }

    // The left leg is drawn almost frontal and almost straight, so any knee bend on it
    // reads badly. Pinning its hip socket moves the pelvis' effective pivot onto that
    // socket: the left leg then barely has to solve, and the pelvis swing is taken up
    // by the right leg instead. 1 = left leg completely still, 0 = both legs share it.
    const float LeftHipPin = 0.88f;
    const string LeftThighBone = "bone_15";

    // Upper body only. Several entries on one bone are summed.
    static readonly Wave[] RotWaves =
    {
        new Wave(HipsBone,  1f,  4.0f,   0f),   // weight shift - pivots about the left hip

        new Wave("bone_21", 2f, -2.2f,  30f),   // torso breathing, counter to the hips

        new Wave("bone_28", 2f,  2.4f,  70f),   // head sway
        new Wave("bone_28", 3f,  0.7f,   0f),   // slow scan on top

        new Wave("bone_22", 2f, -2.6f,  50f),   // left arm trails the torso
        new Wave("bone_23", 2f,  3.0f,  80f),
        new Wave("bone_24", 2f, -2.2f, 110f),

        new Wave("bone_25", 2f,  2.2f,  45f),   // knife arm
        new Wave("bone_26", 2f, -2.8f,  75f),
        new Wave("bone_27", 2f,  2.6f, 105f),
    };

    // Hips translation, in world units.
    // The vertical bob is off: it forced the near-straight left leg to buckle, and a
    // braced goblin does not need it. Set the amp above zero to bring it back - it is
    // DOWN-ONLY, because the left leg has no slack to extend into.
    static readonly Wave HipsPosY = new Wave(HipsBone, 2f, 0.000f, 90f);
    static readonly Wave HipsPosX = new Wave(HipsBone, 1f, 0.030f,  0f);

    class Leg
    {
        public Transform thigh, shin, foot;
        public float l1, l2, bendSign;
        public Vector3 ankleTarget;    // world, fixed for the whole clip
        public Vector3 hipSocketBind;  // world, where this leg's thigh starts at rest
        public float footWorldZ;       // world, fixed for the whole clip
    }

    static readonly string[][] LegBones =
    {
        new[] { "bone_15", "bone_16", "bone_17" },
        new[] { "bone_18", "bone_19", "bone_20" },
    };

    [MenuItem("MamaGame/BoneAnimTest/Build Goblin Idle")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PsbPath);
        if (source == null) { Debug.LogError("GoblinIdleBuilder: " + PsbPath + " not found."); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SetupCamera();

        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "Goblin";
        root.transform.position = new Vector3(RootOffsetX, 0f, 0f);

        var bones = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("bone_")) bones[t.name] = t;

        var clip = BakeClip(root.transform, bones);
        var controller = BuildController(clip);
        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = root;
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.in2DMode = true;
            view.Frame(new Bounds(new Vector3(0f, 4.4f, 0f), new Vector3(9f, 7.5f, 1f)), false);
        }
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = 5.3f;                            // leaves headroom for the raised knife
        cam.transform.position = new Vector3(0f, 5.4f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.30f, 0.33f, 0.36f);
    }

    static float Eval(List<Wave> waves, float t, float bias)
    {
        float v = bias;
        if (waves != null)
            foreach (var w in waves)
                v += w.amp * Mathf.Sin(2f * Mathf.PI * w.cycles * t / ClipLength + w.phase * Mathf.Deg2Rad);
        return v;
    }

    static void SetWorldZ(Transform t, float worldZ)
    {
        float parentZ = t.parent != null ? t.parent.eulerAngles.z : 0f;
        var e = t.localEulerAngles;
        t.localEulerAngles = new Vector3(e.x, e.y, worldZ - parentZ);
    }

    static AnimationClip BakeClip(Transform root, Dictionary<string, Transform> bones)
    {
        if (!AssetDatabase.IsValidFolder(AnimFolder)) AssetDatabase.CreateFolder("Assets", "Animations");

        var hips = bones[HipsBone];

        // --- capture the bind pose -------------------------------------------------
        var bindEuler = new Dictionary<string, Vector3>();
        var bindPos   = new Dictionary<string, Vector3>();
        foreach (var kv in bones)
        {
            bindEuler[kv.Key] = kv.Value.localEulerAngles;
            bindPos[kv.Key]   = kv.Value.localPosition;
        }

        var legs = new List<Leg>();
        foreach (var names in LegBones)
        {
            if (!bones.ContainsKey(names[0]) || !bones.ContainsKey(names[1]) || !bones.ContainsKey(names[2]))
            { Debug.LogWarning("GoblinIdleBuilder: leg chain missing - " + string.Join("/", names)); continue; }

            var leg = new Leg();
            leg.thigh = bones[names[0]];
            leg.shin  = bones[names[1]];
            leg.foot  = bones[names[2]];
            leg.l1 = Vector3.Distance(leg.thigh.position, leg.shin.position);
            leg.l2 = Vector3.Distance(leg.shin.position, leg.foot.position);
            leg.ankleTarget   = leg.foot.position;
            leg.hipSocketBind = leg.thigh.position;
            leg.footWorldZ    = leg.foot.eulerAngles.z;

            // Which way does the knee already bend? Keep that side.
            Vector3 d = leg.ankleTarget - leg.thigh.position;
            float baseAng  = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float thighAng = leg.thigh.eulerAngles.z;
            leg.bendSign = Mathf.DeltaAngle(baseAng, thighAng) >= 0f ? 1f : -1f;
            legs.Add(leg);
        }

        Leg leftLeg = null;
        foreach (var leg in legs) if (leg.thigh.name == LeftThighBone) leftLeg = leg;

        // --- group the upper-body waves -------------------------------------------
        var perBone = new Dictionary<string, List<Wave>>();
        foreach (var w in RotWaves)
        {
            if (!perBone.ContainsKey(w.bone)) perBone[w.bone] = new List<Wave>();
            perBone[w.bone].Add(w);
        }

        // Every bone whose Z we write out: the waved ones plus all six leg bones.
        var recorded = new List<string>(perBone.Keys);
        foreach (var names in LegBones)
            foreach (var n in names)
                if (bones.ContainsKey(n) && !recorded.Contains(n)) recorded.Add(n);

        var zCurves = new Dictionary<string, AnimationCurve>();
        var prevZ   = new Dictionary<string, float>();
        foreach (var n in recorded) zCurves[n] = new AnimationCurve();

        var hipsX = new AnimationCurve();
        var hipsY = new AnimationCurve();
        float maxErr = 0f;

        var hipsWavesX = new List<Wave>(); hipsWavesX.Add(HipsPosX);

        // --- sample ----------------------------------------------------------------
        for (int i = 0; i <= Samples; i++)
        {
            float t = ClipLength * i / Samples;

            // reset to bind pose so each sample is independent
            foreach (var kv in bones)
            {
                kv.Value.localEulerAngles = bindEuler[kv.Key];
                kv.Value.localPosition    = bindPos[kv.Key];
            }

            // hips translation - sway is symmetric, the bob only ever sinks
            var hp = bindPos[HipsBone];
            hp.x = Eval(hipsWavesX, t, hp.x);
            hp.y += HipsPosY.amp * 0.5f *
                    (Mathf.Sin(2f * Mathf.PI * HipsPosY.cycles * t / ClipLength
                               + HipsPosY.phase * Mathf.Deg2Rad) - 1f);
            hips.localPosition = hp;

            // rotations from the wave table (hips, torso, head, arms)
            foreach (var kv in perBone)
            {
                if (!bones.ContainsKey(kv.Key)) continue;
                var b = bones[kv.Key];
                var e = bindEuler[kv.Key];
                b.localEulerAngles = new Vector3(e.x, e.y, Eval(kv.Value, t, e.z));
            }

            // Slide the whole pelvis so the left hip socket stays put. This turns the
            // pelvis swing into a rotation about that socket, so the left leg hardly
            // has to solve at all and the right leg takes the motion.
            if (leftLeg != null && LeftHipPin > 0f)
            {
                Vector3 slip = leftLeg.hipSocketBind - leftLeg.thigh.position; slip.z = 0f;
                hips.localPosition += slip * LeftHipPin;
            }

            // Pull the hips back if a leg cannot reach its planted foot. Runs after the
            // hips rotation, which also shifts where the thighs start. Without this the
            // IK silently clamps and the foot slides instead.
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (var leg in legs)
                {
                    Vector3 gap = leg.ankleTarget - leg.thigh.position; gap.z = 0f;
                    float over = gap.magnitude - (leg.l1 + leg.l2 - 1e-3f);
                    if (over > 0f) hips.localPosition += gap.normalized * over;
                }
            }

            // legs: pin each ankle back onto its bind-pose spot
            foreach (var leg in legs)
            {
                Vector3 P = leg.thigh.position;
                Vector3 d = leg.ankleTarget - P; d.z = 0f;
                float dist = Mathf.Clamp(d.magnitude,
                                         Mathf.Abs(leg.l1 - leg.l2) + 1e-4f,
                                         leg.l1 + leg.l2 - 1e-4f);
                float baseAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float cosA = (dist * dist + leg.l1 * leg.l1 - leg.l2 * leg.l2) / (2f * dist * leg.l1);
                float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;

                SetWorldZ(leg.thigh, baseAng + leg.bendSign * a);
                Vector3 d2 = leg.ankleTarget - leg.shin.position; d2.z = 0f;   // shin.position == knee
                SetWorldZ(leg.shin, Mathf.Atan2(d2.y, d2.x) * Mathf.Rad2Deg);
                SetWorldZ(leg.foot, leg.footWorldZ);                            // sole stays flat

                maxErr = Mathf.Max(maxErr, Vector3.Distance(leg.foot.position, leg.ankleTarget));
            }

            // read the result back off the hierarchy
            foreach (var n in recorded)
            {
                float z = bones[n].localEulerAngles.z;
                if (prevZ.ContainsKey(n)) z = prevZ[n] + Mathf.DeltaAngle(prevZ[n], z);  // keep curves continuous
                prevZ[n] = z;
                zCurves[n].AddKey(new Keyframe(t, z));
            }
            hipsX.AddKey(new Keyframe(t, hips.localPosition.x));
            hipsY.AddKey(new Keyframe(t, hips.localPosition.y));
        }

        // restore the bind pose in the scene
        foreach (var kv in bones)
        {
            kv.Value.localEulerAngles = bindEuler[kv.Key];
            kv.Value.localPosition    = bindPos[kv.Key];
        }

        // --- write the clip --------------------------------------------------------
        var clip = new AnimationClip();
        clip.frameRate = 30f;

        foreach (var n in recorded)
        {
            var path = AnimationUtility.CalculateTransformPath(bones[n], root);
            var c = zCurves[n];
            for (int k = 0; k < c.length; k++) c.SmoothTangents(k, 0f);
            var e = bindEuler[n];
            // All three euler components must be written, or the missing ones snap to zero.
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.x", Constant(e.x));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.y", Constant(e.y));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z", c);
        }

        var hipsPath = AnimationUtility.CalculateTransformPath(hips, root);
        for (int k = 0; k < hipsX.length; k++) hipsX.SmoothTangents(k, 0f);
        for (int k = 0; k < hipsY.length; k++) hipsY.SmoothTangents(k, 0f);
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.x", hipsX);
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.y", hipsY);
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.z", Constant(bindPos[HipsBone].z));

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) != null) AssetDatabase.DeleteAsset(ClipPath);
        AssetDatabase.CreateAsset(clip, ClipPath);

        Debug.Log("GoblinIdleBuilder: " + ClipLength + "s / " + recorded.Count + " bones, "
                  + legs.Count + " legs IK-pinned, max ankle drift = "
                  + (maxErr * 100f).ToString("F3") + " px");
        return clip;
    }

    static AnimationCurve Constant(float v)
    {
        var c = new AnimationCurve();
        c.AddKey(new Keyframe(0f, v));
        c.AddKey(new Keyframe(ClipLength, v));
        return c;
    }

    // Reuses the controller if it exists so states added by other builders (Attack, ...)
    // survive an Idle rebuild.
    static AnimatorController BuildController(AnimationClip clip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        AnimatorState state = null;
        foreach (var cs in sm.states) if (cs.state.name == "Idle") state = cs.state;
        if (state == null) state = sm.AddState("Idle");
        state.motion = clip;
        sm.defaultState = state;
        EditorUtility.SetDirty(controller);
        return controller;
    }
}
