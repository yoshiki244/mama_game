// Mama bone-animation prototype rig builder.
// Builds a cut-out joint hierarchy from the PSD Importer prefab and generates an idle loop.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MamaRigBuilder
{
    const string PsbPath        = "Assets/Sprites/Characters/mama_rig.psb";
    const string AnimFolder     = "Assets/Animations";
    const string ClipPath       = "Assets/Animations/mama_Idle.anim";
    const string ControllerPath = "Assets/Animations/mama_Rig.controller";
    const string ScenePath      = "Assets/Scenes/mama_anim_test.unity";

    // PSD canvas: 1038 x 1516, prefab origin = bottom-center, PPU = 100.
    const float CanvasW = 1038f, CanvasH = 1516f, Ppu = 100f;
    static Vector3 U(float px, float py)
    {
        return new Vector3((px - CanvasW * 0.5f) / Ppu, (CanvasH - py) / Ppu, 0f);
    }

    struct JointDef
    {
        public string name, parent;
        public float px, py;
        public int[] parts;      // sortingOrder of the sprite layers attached to this joint
        public JointDef(string n, string p, float x, float y, params int[] sortingOrders)
        { name = n; parent = p; px = x; py = y; parts = sortingOrders; }
    }

    // Layer sortingOrder map (from the PSD layer stack):
    // 17 hat-front  16 bangs     15 face    14 side-bangs  13 cape-front  12 arm-R  11 waist
    // 10 arm-L       9 chest      8 neck     7 leg-R        6 foot-R       5 leg-L    4 foot-L
    //  3 cape-back   2 hair-back  1 hat-back
    static readonly JointDef[] Joints =
    {
        new JointDef("Hips",       "",      519f,  885f, 11),
        new JointDef("LegL",       "Hips",  556f,  838f, 5),
        new JointDef("FootL",      "LegL",  548f, 1385f, 4),
        new JointDef("LegR",       "Hips",  468f,  832f, 7),
        new JointDef("FootR",      "LegR",  440f, 1390f, 6),
        new JointDef("Spine",      "Hips",  519f,  720f, 9),
        new JointDef("CapeBack",   "Spine", 519f,  470f, 3),
        new JointDef("CapeFront",  "Spine", 505f,  445f, 13),
        new JointDef("ArmL",       "Spine", 627f,  505f, 10),
        new JointDef("ArmR",       "Spine", 452f,  505f, 12),
        new JointDef("Neck",       "Spine", 515f,  445f, 8),
        new JointDef("Head",       "Neck",  515f,  385f, 15),
        new JointDef("Hat",        "Head",  505f,  340f, 17, 1),
        new JointDef("HairFront",  "Head",  556f,  220f, 16),
        new JointDef("HairFrontL", "Head",  600f,  250f, 14),
        new JointDef("HairBack",   "Head",  470f,  250f, 2),
    };

    struct Wave
    {
        public string joint;
        public int cycles;      // oscillations per clip loop
        public float amp;       // degrees
        public float phase;     // degrees
        public Wave(string j, int c, float a, float ph) { joint = j; cycles = c; amp = a; phase = ph; }
    }

    // Breathing runs at 2 cycles per loop; weight-shift and trailing cloth run at 1.
    // Phase offsets create the follow-through: hips lead, hair and cape trail.
    static readonly Wave[] RotWaves =
    {
        new Wave("Hips",         1,  0.7f,   0f),
        new Wave("Spine",        2, -1.1f,  25f),
        new Wave("LegL",         1,  0.25f, 15f),
        new Wave("LegR",         1, -0.25f, 15f),
        new Wave("FootL",        1, -0.25f, 15f),
        new Wave("FootR",        1,  0.25f, 15f),
        new Wave("ArmL",         2, -1.8f,  45f),
        new Wave("ArmR",         2,  1.5f,  55f),
        new Wave("Neck",         2,  0.7f,  60f),
        new Wave("Head",         2,  0.9f,  85f),
        new Wave("Hat",          1, -1.7f,  70f),
        new Wave("HairFront",    2, -1.2f,  70f),
        new Wave("HairFrontL",   1, -1.9f,  95f),
        new Wave("HairBack",     1,  2.4f,  85f),
        new Wave("CapeBack",     1,  2.8f, 100f),
        new Wave("CapeFront",    1, -2.5f, 115f),
    };

    const float ClipLength = 6f;
    const int Samples = 24;

    [MenuItem("MamaGame/BoneAnimTest/Build Rig + Idle")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PsbPath);
        if (source == null)
        {
            Debug.LogError("MamaRigBuilder: " + PsbPath + " not found.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SetupCamera();

        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "Mama";
        root.transform.position = Vector3.zero;

        // Index the imported layer objects by sorting order.
        var parts = new Dictionary<int, Transform>();
        var loose = new List<Transform>();
        foreach (Transform t in root.transform) loose.Add(t);
        foreach (var t in loose)
        {
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null) parts[sr.sortingOrder] = t;
        }

        var joints = new Dictionary<string, Transform>();
        foreach (var j in Joints)
        {
            var go = new GameObject(j.name);
            var parent = string.IsNullOrEmpty(j.parent) ? root.transform : joints[j.parent];
            go.transform.SetParent(parent, false);
            go.transform.position = U(j.px, j.py);
            joints[j.name] = go.transform;
        }

        // Re-parent each sprite under its joint, keeping its authored world position.
        foreach (var j in Joints)
        {
            foreach (var order in j.parts)
            {
                Transform part;
                if (!parts.TryGetValue(order, out part))
                {
                    Debug.LogWarning("MamaRigBuilder: no layer with sortingOrder " + order);
                    continue;
                }
                part.SetParent(joints[j.name], true);
            }
        }

        var clip = BuildIdleClip(root.transform, joints);
        var controller = BuildController(clip);
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = root;
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.in2DMode = true;
            view.Frame(new Bounds(new Vector3(0f, 7.6f, 0f), new Vector3(11f, 16.5f, 1f)), false);
        }

        Debug.Log("MamaRigBuilder: " + joints.Count + " joints, " + parts.Count
                  + " parts -> " + ScenePath);
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = 8.4f;
        cam.transform.position = new Vector3(0f, 7.5f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.42f, 0.45f, 0.5f);   // line art needs a mid-grey backdrop
    }

    static AnimationClip BuildIdleClip(Transform root, Dictionary<string, Transform> joints)
    {
        if (!AssetDatabase.IsValidFolder(AnimFolder))
            AssetDatabase.CreateFolder("Assets", "Animations");

        var clip = new AnimationClip();
        clip.frameRate = 30f;

        foreach (var w in RotWaves)
        {
            if (!joints.ContainsKey(w.joint)) continue;
            var path = AnimationUtility.CalculateTransformPath(joints[w.joint], root);
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z",
                          Sine(w.cycles, w.amp, w.phase, 0f));
        }

        // Vertical breathing on the hips; everything above inherits it.
        var hips = joints["Hips"];
        var hipsPath = AnimationUtility.CalculateTransformPath(hips, root);
        clip.SetCurve(hipsPath, typeof(Transform), "m_LocalPosition.y",
                      Sine(2, 0.035f, 90f, hips.localPosition.y));

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) != null)
            AssetDatabase.DeleteAsset(ClipPath);
        AssetDatabase.CreateAsset(clip, ClipPath);
        return clip;
    }

    static AnimationCurve Sine(int cycles, float amp, float phaseDeg, float bias)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i <= Samples; i++)
        {
            float t = ClipLength * i / Samples;
            float v = bias + amp * Mathf.Sin(2f * Mathf.PI * cycles * t / ClipLength + phaseDeg * Mathf.Deg2Rad);
            curve.AddKey(new Keyframe(t, v));
        }
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    static AnimatorController BuildController(AnimationClip clip)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var state = controller.layers[0].stateMachine.AddState("Idle");
        state.motion = clip;
        controller.layers[0].stateMachine.defaultState = state;
        return controller;
    }
}
