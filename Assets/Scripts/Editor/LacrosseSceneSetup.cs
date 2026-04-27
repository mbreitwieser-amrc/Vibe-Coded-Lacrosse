#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-click test scene generator.
/// Menu: Lacrosse ▶ Build Test Scene
///
/// Creates Assets/Scenes/TestScene.unity containing:
///   Managers, Field, Player (with stick hierarchy), Camera, Ball, two Goals, HUD.
///
/// All component cross-references are wired automatically.
/// </summary>
public static class LacrosseSceneSetup
{
    // ── Layer / Tag constants ─────────────────────────────────────────────────
    private const string LAYER_GROUND = "Ground";
    private const int    LAYER_GROUND_INDEX = 8;   // First user layer
    private const string TAG_BALL     = "Ball";

    // ── Paths ─────────────────────────────────────────────────────────────────
    private const string SCENE_PATH   = "Assets/Scenes/TestScene.unity";
    private const string PHYS_MAT_PATH = "Assets/Lacrosse_BallPhysics.asset";

    [MenuItem("Lacrosse/Build Test Scene", priority = 0)]
    public static void BuildTestScene()
    {
        // ── Confirm ───────────────────────────────────────────────────────────
        if (!EditorUtility.DisplayDialog("Build Test Scene",
            "This will create (or replace) Assets/Scenes/TestScene.unity.\nProceed?",
            "Build", "Cancel"))
            return;

        // ── New empty scene ───────────────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Layer & tag setup ─────────────────────────────────────────────────
        EnsureLayer(LAYER_GROUND, LAYER_GROUND_INDEX);
        EnsureTag(TAG_BALL);
        LayerMask groundMask = 1 << LAYER_GROUND_INDEX;

        // ── Physics material ──────────────────────────────────────────────────
        PhysicsMaterial ballMat = GetOrCreateBallMat();

        // ── Directional light ─────────────────────────────────────────────────
        var lightGO = new GameObject("Directional Light");
        var light   = lightGO.AddComponent<Light>();
        light.type  = LightType.Directional;
        light.intensity = 1.2f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ── Field ─────────────────────────────────────────────────────────────
        GameObject field = GameObject.CreatePrimitive(PrimitiveType.Plane);
        field.name = "Field";
        field.transform.localScale = new Vector3(10f, 1f, 18f);  // 100m x 180m
        field.layer = LAYER_GROUND_INDEX;
        SetColor(field, new Color(0.2f, 0.55f, 0.15f)); // Green grass

        // ── Managers ──────────────────────────────────────────────────────────
        GameObject managers = new GameObject("[Managers]");

        var gmGO = new GameObject("GameManager");
        gmGO.transform.SetParent(managers.transform);
        gmGO.AddComponent<GameManager>();

        var smGO = new GameObject("ScoreManager");
        smGO.transform.SetParent(managers.transform);
        smGO.AddComponent<ScoreManager>();

        var amGO = new GameObject("AudioManager");
        amGO.transform.SetParent(managers.transform);
        amGO.AddComponent<AudioManager>();

        // ── Ball ──────────────────────────────────────────────────────────────
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.tag  = TAG_BALL;
        // Spawn at field centre on the ground (ball radius = scale*0.5 = 0.0325)
        ball.transform.position   = new Vector3(0f, 0.033f, 0f);
        ball.transform.localScale = Vector3.one * 0.065f;  // Real lacrosse ball ~6.5cm
        SetColor(ball, Color.white);

        // Replace default SphereCollider — BallController will configure it
        var ballRb           = ball.AddComponent<Rigidbody>();
        ballRb.linearDamping = 0.08f;
        ballRb.angularDamping = 0.8f;
        var ballCtrl         = ball.AddComponent<BallController>();
        ballCtrl.ballPhysicsMaterial = ballMat;
        ball.GetComponent<SphereCollider>().material = ballMat;

        // ── Player ────────────────────────────────────────────────────────────
        // Root: invisible placeholder — actual visual is the capsule child
        GameObject player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0f, -5f);
        player.layer = 0; // Default

        // Body visual (Capsule)
        GameObject bodyVis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bodyVis.name = "Body";
        bodyVis.transform.SetParent(player.transform);
        bodyVis.transform.localPosition = new Vector3(0f, 1f, 0f);
        bodyVis.transform.localScale    = new Vector3(0.5f, 1f, 0.5f);
        // Remove the capsule's default collider — CharacterController covers collision
        Object.DestroyImmediate(bodyVis.GetComponent<CapsuleCollider>());
        SetColor(bodyVis, new Color(0.2f, 0.4f, 0.8f)); // Blue jersey

        // CharacterController on root (height=2, centre at y=1)
        var cc              = player.AddComponent<CharacterController>();
        cc.height           = 2f;
        cc.radius           = 0.35f;
        cc.center           = new Vector3(0f, 1f, 0f);
        cc.skinWidth        = 0.08f;
        cc.stepOffset       = 0.4f;
        cc.slopeLimit       = 45f;

        // ── StickHeadSocket child ─────────────────────────────────────────────
        GameObject socket = new GameObject("StickHeadSocket");
        socket.transform.SetParent(player.transform);
        socket.transform.localPosition = new Vector3(0.28f, 1.1f, 1.0f);

        // CupPhysics (adds its own SphereCollider trigger in Awake)
        var cup = socket.AddComponent<CupPhysics>();

        // Cup wall BoxColliders — physical containment (non-trigger).
        // These become part of the kinematic compound collider once
        // StickInputController.Awake() adds the kinematic Rigidbody.
        AddCupWall(socket, "CupWall_Back",  new Vector3( 0f,     0f,    -0.055f), new Vector3(0.12f, 0.06f, 0.015f));
        AddCupWall(socket, "CupWall_Left",  new Vector3(-0.055f, 0f,     0f),     new Vector3(0.015f, 0.06f, 0.10f));
        AddCupWall(socket, "CupWall_Right", new Vector3( 0.055f, 0f,     0f),     new Vector3(0.015f, 0.06f, 0.10f));

        // Stick shaft visual (Cylinder) — StickShaftVisual will detach and
        // reposition this every LateUpdate to point from grip to socket.
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "StickShaft";
        // Parent doesn't matter at runtime; StickShaftVisual.Awake() detaches.
        shaft.transform.SetParent(player.transform);
        shaft.transform.localPosition = new Vector3(0.18f, 1.1f, 0.5f);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.transform.localScale    = new Vector3(0.025f, 0.5f, 0.025f);
        Object.DestroyImmediate(shaft.GetComponent<CapsuleCollider>());
        SetColor(shaft, new Color(0.6f, 0.4f, 0.1f)); // Wood brown

        // Stick head visual (flattened sphere at socket position)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "StickHead";
        head.transform.SetParent(socket.transform);
        head.transform.localPosition = Vector3.zero;
        head.transform.localScale    = new Vector3(0.12f, 0.06f, 0.10f);
        Object.DestroyImmediate(head.GetComponent<SphereCollider>());
        SetColor(head, new Color(0.9f, 0.9f, 0.9f)); // White mesh

        // PlayerController
        var pc = player.AddComponent<PlayerController>();
        pc.groundMask = groundMask;

        // StickController
        player.AddComponent<StickController>();

        // StickInputController
        var sic             = player.AddComponent<StickInputController>();
        sic.stickHeadSocket = socket.transform;
        sic.playerBody      = player.transform;

        // Wire shaft visual — must be done after sic exists
        var shaftVisual       = shaft.AddComponent<StickShaftVisual>();
        shaftVisual.stickInput = sic;

        // ── Player Camera ─────────────────────────────────────────────────────
        GameObject camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(player.transform);
        camGO.transform.localPosition = Vector3.zero;

        var cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane  = 0.1f;
        cam.farClipPlane   = 500f;
        cam.fieldOfView    = 80f;
        camGO.AddComponent<AudioListener>();

        var fpc               = camGO.AddComponent<FirstPersonCamera>();
        fpc.playerBody        = player.transform;
        fpc.stickHeadSocket   = socket.transform;
        // fpc.ball is auto-found in Start()

        // Give PlayerController and StickInputController the camera reference
        pc.cameraTransform  = camGO.transform;
        sic.cameraController = fpc;

        // ── Goals ─────────────────────────────────────────────────────────────
        float fieldHalfLength = 45f;  // Half of the 90m pitch length (plane scale 18 = 180 units / 2)
        // The plane scale 18 in Z = 18 * 10 = 180 world units
        CreateGoal("HomeGoal",  new Vector3(0f, 0f, -fieldHalfLength), Team.Away, ball.transform);
        CreateGoal("AwayGoal",  new Vector3(0f, 0f,  fieldHalfLength), Team.Home, ball.transform);

        // ── HUD Canvas ────────────────────────────────────────────────────────
        var hud = CreateHUD(ball.GetComponent<BallController>());
        _ = hud; // suppress unused warning

        // ── Ensure Scenes folder ──────────────────────────────────────────────
        if (!Directory.Exists("Assets/Scenes"))
        {
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
        }

        // ── Save ──────────────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AssetDatabase.Refresh();

        Debug.Log("[LacrosseSceneSetup] Scene saved to " + SCENE_PATH);
        EditorUtility.DisplayDialog("Done",
            "Test scene built and saved to:\n" + SCENE_PATH +
            "\n\nIMPORTANT: Add a NavMesh Surface to Field and bake it before testing AI.\n" +
            "Press Play — the game starts in WaitingToStart state. Click 'Start' to begin.",
            "OK");
    }

    // ── Goal builder ──────────────────────────────────────────────────────────

    private static void CreateGoal(string name, Vector3 pos, Team scoringTeam, Transform ballTransform)
    {
        GameObject root = new GameObject(name);
        root.transform.position = pos;
        if (scoringTeam == Team.Away) root.transform.rotation = Quaternion.identity;

        float postHeight = 1.83f;  // Standard lacrosse goal: 6ft
        float goalWidth  = 1.83f;
        float goalDepth  = 0.9f;

        // Post visuals (Cylinders, no colliders needed — trigger handles detection)
        AddGoalPost(root, "PostLeft",  new Vector3(-goalWidth * 0.5f, postHeight * 0.5f, 0f), postHeight);
        AddGoalPost(root, "PostRight", new Vector3( goalWidth * 0.5f, postHeight * 0.5f, 0f), postHeight);
        AddGoalPost(root, "Crossbar",  new Vector3(0f, postHeight, 0f), goalWidth, true);
        // Back posts (depth)
        AddGoalPost(root, "BackLeft",  new Vector3(-goalWidth * 0.5f, postHeight * 0.5f, -goalDepth), postHeight);
        AddGoalPost(root, "BackRight", new Vector3( goalWidth * 0.5f, postHeight * 0.5f, -goalDepth), postHeight);
        // Back bar
        AddGoalPost(root, "BackBar",   new Vector3(0f, postHeight, -goalDepth), goalWidth, true);

        // Net back (flat quad so you can see it)
        GameObject net = GameObject.CreatePrimitive(PrimitiveType.Cube);
        net.name = "Net";
        net.transform.SetParent(root.transform);
        net.transform.localPosition = new Vector3(0f, postHeight * 0.5f, -goalDepth * 0.5f);
        net.transform.localScale    = new Vector3(goalWidth, postHeight, 0.02f);
        Object.DestroyImmediate(net.GetComponent<BoxCollider>());
        SetColor(net, new Color(1f, 1f, 1f, 0.3f));

        // GoalTrigger volume
        GameObject triggerGO = new GameObject("GoalTrigger");
        triggerGO.transform.SetParent(root.transform);
        triggerGO.transform.localPosition = new Vector3(0f, postHeight * 0.5f, -goalDepth * 0.4f);

        var triggerCol          = triggerGO.AddComponent<BoxCollider>();
        triggerCol.isTrigger    = true;
        triggerCol.size         = new Vector3(goalWidth, postHeight, goalDepth * 0.8f);

        var gt              = triggerGO.AddComponent<GoalTrigger>();
        gt.scoringTeam      = scoringTeam;

        // Ball respawn point
        GameObject respawn = new GameObject("BallRespawn");
        respawn.transform.SetParent(root.transform);
        respawn.transform.localPosition = new Vector3(0f, 0.5f, 3f);
        gt.ballRespawnPoint = respawn.transform;
    }

    private static void AddGoalPost(GameObject parent, string postName,
                                    Vector3 localPos, float size, bool horizontal = false)
    {
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = postName;
        post.transform.SetParent(parent.transform);
        post.transform.localPosition = localPos;

        if (horizontal)
        {
            post.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            post.transform.localScale    = new Vector3(0.05f, size * 0.5f, 0.05f);
        }
        else
        {
            post.transform.localScale = new Vector3(0.05f, size * 0.5f, 0.05f);
        }

        Object.DestroyImmediate(post.GetComponent<CapsuleCollider>());
        SetColor(post, Color.white);
    }

    // ── Cup wall helper ───────────────────────────────────────────────────────

    private static void AddCupWall(GameObject parent, string wallName,
                                   Vector3 localPos, Vector3 size)
    {
        GameObject wall = new GameObject(wallName);
        wall.transform.SetParent(parent.transform);
        wall.transform.localPosition = localPos;

        var col  = wall.AddComponent<BoxCollider>();
        col.size = size;
    }

    // ── HUD builder ───────────────────────────────────────────────────────────

    private static HUDController CreateHUD(BallController ballRef)
    {
        GameObject canvasGO = new GameObject("HUD_Canvas");
        var canvas          = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var hud = canvasGO.AddComponent<HUDController>();

        // Score labels
        hud.homeScoreText = AddTMPText(canvasGO, "HomeScore", "0",
            new Vector2(-200f, -30f), new Vector2(120f, 60f), 48, TextAlignmentOptions.Center);
        hud.awayScoreText = AddTMPText(canvasGO, "AwayScore", "0",
            new Vector2( 200f, -30f), new Vector2(120f, 60f), 48, TextAlignmentOptions.Center);
        hud.homeTeamLabel = AddTMPText(canvasGO, "HomeLabel", "HOME",
            new Vector2(-200f, 20f), new Vector2(120f, 36f), 18, TextAlignmentOptions.Center);
        hud.awayTeamLabel = AddTMPText(canvasGO, "AwayLabel", "AWAY",
            new Vector2( 200f, 20f), new Vector2(120f, 36f), 18, TextAlignmentOptions.Center);

        // Timers
        hud.gameTimerText = AddTMPText(canvasGO, "GameTimer", "4:00",
            new Vector2(0f, -25f), new Vector2(160f, 50f), 36, TextAlignmentOptions.Center);
        hud.shotClockText = AddTMPText(canvasGO, "ShotClock", "60",
            new Vector2(0f, -65f), new Vector2(80f, 36f), 24, TextAlignmentOptions.Center);
        hud.halfText      = AddTMPText(canvasGO, "HalfText", "HALF 1",
            new Vector2(0f, 10f), new Vector2(120f, 30f), 14, TextAlignmentOptions.Center);

        // Stamina bar background
        GameObject staminaBg = new GameObject("StaminaBar_BG");
        staminaBg.transform.SetParent(canvasGO.transform, false);
        var bgImg      = staminaBg.AddComponent<Image>();
        bgImg.color    = new Color(0f, 0f, 0f, 0.5f);
        var bgRect     = staminaBg.GetComponent<RectTransform>();
        bgRect.anchorMin  = new Vector2(0f, 0f);
        bgRect.anchorMax  = new Vector2(0f, 0f);
        bgRect.pivot      = new Vector2(0f, 0f);
        bgRect.anchoredPosition = new Vector2(20f, 20f);
        bgRect.sizeDelta  = new Vector2(200f, 16f);

        // Panels (minimalist — just coloured overlays, wire text manually if needed)
        hud.startPanel    = CreatePanel(canvasGO, "StartPanel",
            "PRESS START", new Color(0,0,0,0.7f));
        hud.pausePanel    = CreatePanel(canvasGO, "PausePanel",
            "PAUSED", new Color(0,0,0,0.7f));
        hud.halftimePanel = CreatePanel(canvasGO, "HalftimePanel",
            "HALFTIME", new Color(0,0,0,0.7f));
        hud.gameOverPanel = CreatePanel(canvasGO, "GameOverPanel",
            "GAME OVER", new Color(0,0,0,0.8f));

        // Game over result text
        hud.gameOverResultText = AddTMPText(hud.gameOverPanel, "GameOverResult", "",
            new Vector2(0f, -40f), new Vector2(400f, 60f), 52, TextAlignmentOptions.Center);

        return hud;
    }

    private static TMP_Text AddTMPText(GameObject parent, string goName, string defaultText,
        Vector2 anchoredPos, Vector2 sizeDelta, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject go  = new GameObject(goName);
        go.transform.SetParent(parent.transform, false);

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text      = defaultText;
        txt.fontSize  = fontSize;
        txt.alignment = alignment;
        txt.color     = Color.white;

        var rect             = go.GetComponent<RectTransform>();
        rect.anchorMin       = new Vector2(0.5f, 1f);
        rect.anchorMax       = new Vector2(0.5f, 1f);
        rect.pivot           = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta        = sizeDelta;

        return txt;
    }

    private static GameObject CreatePanel(GameObject parent, string goName,
        string label, Color bg)
    {
        GameObject panel   = new GameObject(goName);
        panel.transform.SetParent(parent.transform, false);
        panel.SetActive(false);

        var img    = panel.AddComponent<Image>();
        img.color  = bg;

        var rect       = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // Label in panel centre
        AddTMPText(panel, "PanelLabel", label,
            Vector2.zero, new Vector2(600f, 80f), 64, TextAlignmentOptions.Center);

        return panel;
    }

    // ── Physics material ──────────────────────────────────────────────────────

    private static PhysicsMaterial GetOrCreateBallMat()
    {
        var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PHYS_MAT_PATH);
        if (existing != null) return existing;

        var mat               = new PhysicsMaterial("Lacrosse_BallPhysics");
        mat.bounciness        = 0.5f;
        mat.dynamicFriction   = 0.3f;
        mat.staticFriction    = 0.35f;
        mat.bounceCombine     = PhysicsMaterialCombine.Average;
        mat.frictionCombine   = PhysicsMaterialCombine.Average;

        AssetDatabase.CreateAsset(mat, PHYS_MAT_PATH);
        return mat;
    }

    // ── Colour helper ─────────────────────────────────────────────────────────

    private static void SetColor(GameObject go, Color color)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        var mat    = new Material(Shader.Find("Universal Render Pipeline/Lit") ??
                                  Shader.Find("Standard"));
        mat.color  = color;
        r.sharedMaterial = mat;
    }

    // ── Layer / tag helpers ───────────────────────────────────────────────────

    private static void EnsureLayer(string layerName, int index)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "ProjectSettings/TagManager.asset"));
        var layers = tagManager.FindProperty("layers");
        var slot   = layers.GetArrayElementAtIndex(index);
        if (string.IsNullOrEmpty(slot.stringValue))
        {
            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
        }
    }

    private static void EnsureTag(string tagName)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "ProjectSettings/TagManager.asset"));
        var tags = tagManager.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tagName) return;

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
        tagManager.ApplyModifiedProperties();
    }
}
#endif
