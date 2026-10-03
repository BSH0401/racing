using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Racing.EditorTools
{
    // Builds the race scene (materials, track, cars, cameras, managers) and Windows player.
    public static class RacingSetup
    {
        const string Root = "Assets/_Project";
        const string MatDir = Root + "/Materials";
        const string TexDir = Root + "/Textures";
        const string ScenePath = Root + "/Scenes/Race.unity";
        const int MinimapLayer = 31;

        static readonly Vector3[] TrackPoints =
        {
            new Vector3(0f, 0f, -100f),
            new Vector3(0f, 0f, 150f),
            new Vector3(30f, 2f, 260f),
            new Vector3(120f, 4f, 310f),
            new Vector3(230f, 5f, 290f),
            new Vector3(280f, 4f, 210f),
            new Vector3(250f, 2f, 130f),
            new Vector3(300f, 1f, 60f),
            new Vector3(380f, 0f, 20f),
            new Vector3(400f, 0f, -80f),
            new Vector3(340f, 2f, -170f),
            new Vector3(230f, 3f, -200f),
            new Vector3(110f, 2f, -210f),
            new Vector3(30f, 0f, -190f),
        };

        static readonly (string name, Color color, float skill)[] Drivers =
        {
            ("Blaze", new Color(0.15f, 0.45f, 0.95f), 0.95f),
            ("Viper", new Color(0.1f, 0.75f, 0.3f), 0.93f),
            ("Nova", new Color(0.95f, 0.8f, 0.1f), 0.91f),
            ("Rook", new Color(0.6f, 0.25f, 0.85f), 0.89f),
            ("Ember", new Color(1f, 0.5f, 0.1f), 0.87f),
        };

        [MenuItem("Racing/Setup Scene")]
        public static void SetupScene()
        {
            foreach (var d in new[] { MatDir, TexDir, Root + "/Scenes" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();

            Time.fixedDeltaTime = 1f / 60f;

            var asphaltTex = NoiseTexture("Asphalt", 0.82f, 1f, 3);
            var grassTex = NoiseTexture("Grass", 0.8f, 1f, 5);

            var road = Mat("Road", new Color(0.28f, 0.28f, 0.3f), 0.25f, 0f, asphaltTex);
            var curbRed = Mat("CurbRed", new Color(0.8f, 0.1f, 0.1f), 0.3f);
            var white = Mat("White", new Color(0.95f, 0.95f, 0.95f), 0.3f);
            var grass = Mat("Grass", new Color(0.42f, 0.62f, 0.28f), 0.1f, 0f, grassTex);
            var ground = Mat("Ground", new Color(0.34f, 0.52f, 0.24f), 0.05f, 0f, grassTex);
            var wall = Mat("Wall", new Color(0.78f, 0.8f, 0.84f), 0.35f);
            var black = Mat("Black", new Color(0.05f, 0.05f, 0.06f), 0.3f);
            var gantry = Mat("Gantry", new Color(0.18f, 0.19f, 0.24f), 0.4f, 0.3f);
            var trunk = Mat("Trunk", new Color(0.36f, 0.24f, 0.14f), 0.1f);
            var leaves = Mat("Leaves", new Color(0.18f, 0.42f, 0.16f), 0.1f);
            var hills = Mat("Hills", new Color(0.36f, 0.48f, 0.34f), 0f);
            var stands = Mat("Stands", new Color(0.55f, 0.58f, 0.66f), 0.2f);
            var tire = Mat("Tire", new Color(0.08f, 0.08f, 0.08f), 0.2f);
            var glass = Mat("Glass", new Color(0.08f, 0.1f, 0.14f), 0.9f);
            var headlight = Mat("Headlight", new Color(1f, 0.95f, 0.75f), 0.8f, 0f, null, new Color(1f, 0.95f, 0.7f) * 2f);
            var taillight = Mat("Taillight", new Color(0.8f, 0.05f, 0.05f), 0.8f, 0f, null, new Color(1f, 0.05f, 0.05f) * 1.5f);

            var carPhysics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(MatDir + "/CarBody.physicMaterial");
            if (!carPhysics)
            {
                carPhysics = new PhysicsMaterial("CarBody");
                AssetDatabase.CreateAsset(carPhysics, MatDir + "/CarBody.physicMaterial");
            }
            carPhysics.dynamicFriction = 0.1f;
            carPhysics.staticFriction = 0.1f;
            carPhysics.bounciness = 0.15f;
            carPhysics.frictionCombine = PhysicsMaterialCombine.Minimum;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.27f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.7f, 0.79f, 0.9f);
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 1400f;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile)
            {
                var vol = new GameObject("PostProcess").AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }

            // Track.
            var trackGo = new GameObject("Track");
            var path = trackGo.AddComponent<TrackPath>();
            path.controlPoints = TrackPoints;
            var builder = trackGo.AddComponent<TrackBuilder>();
            builder.road = road;
            builder.curbRed = curbRed;
            builder.curbWhite = white;
            builder.line = white;
            builder.grass = grass;
            builder.wall = wall;
            builder.ground = ground;
            builder.checkerBlack = black;
            builder.gantry = gantry;
            builder.trunk = trunk;
            builder.leaves = leaves;
            builder.hills = hills;
            builder.stands = stands;
            builder.Build();

            // Cars.
            var parts = new CarParts { tire = tire, glass = glass, dark = black, headlight = headlight, taillight = taillight, physics = carPhysics };
            var racers = new Racer[Drivers.Length + 1];
            for (int i = 0; i < Drivers.Length; i++)
            {
                var d = Drivers[i];
                racers[i] = CreateCar(d.name, d.color, false, parts);
                racers[i].GetComponent<AIDriver>().skill = d.skill;
            }
            racers[Drivers.Length] = CreateCar("You", new Color(0.9f, 0.08f, 0.1f), true, parts);

            // Cameras.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2500f;
            cam.cullingMask = ~(1 << MinimapLayer);
            cam.depth = -1;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var chase = camGo.AddComponent<ChaseCamera>();
            chase.target = racers[Drivers.Length].GetComponent<Rigidbody>();

            var miniGo = new GameObject("Minimap Camera");
            var mini = miniGo.AddComponent<Camera>();
            mini.depth = 1;
            mini.clearFlags = CameraClearFlags.SolidColor;
            mini.backgroundColor = new Color(0.1f, 0.14f, 0.1f);
            var miniData = miniGo.AddComponent<UniversalAdditionalCameraData>();
            miniData.renderShadows = false;
            miniData.renderPostProcessing = false;
            miniGo.AddComponent<MinimapCamera>().track = path;

            // Managers.
            var rmGo = new GameObject("RaceManager");
            var rm = rmGo.AddComponent<RaceManager>();
            var hud = rmGo.AddComponent<RaceHUD>();
            hud.race = rm;
            rm.track = path;
            rm.racers = racers;
            rm.chaseCamera = chase;
            rm.hud = hud;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Racing";
            PlayerSettings.companyName = "BSH0401";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Racing] Scene set up: " + ScenePath + ", track length " + path.Length.ToString("F0") + " m");
        }

        class CarParts
        {
            public Material tire, glass, dark, headlight, taillight;
            public PhysicsMaterial physics;
        }

        static Racer CreateCar(string name, Color color, bool player, CarParts parts)
        {
            var paint = Mat("Paint_" + name, color, 0.75f, 0.2f);
            var marker = UnlitMat("Marker_" + name, color);

            var go = new GameObject(player ? "Car_Player" : "Car_" + name);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.25f, 0f);
            box.size = new Vector3(1.8f, 0.7f, 4.2f);
            box.sharedMaterial = parts.physics;

            Part(go, "Body", PrimitiveType.Cube, new Vector3(0f, 0.15f, 0f), new Vector3(1.8f, 0.5f, 4.2f), paint);
            Part(go, "Nose", PrimitiveType.Cube, new Vector3(0f, 0.05f, 2.05f), new Vector3(1.7f, 0.3f, 0.3f), paint);
            Part(go, "Cabin", PrimitiveType.Cube, new Vector3(0f, 0.62f, -0.35f), new Vector3(1.45f, 0.45f, 1.9f), parts.glass);
            Part(go, "Stripe", PrimitiveType.Cube, new Vector3(0f, 0.41f, 1.05f), new Vector3(0.45f, 0.02f, 2f), Mat("White", Color.white, 0.3f));
            Part(go, "WingL", PrimitiveType.Cube, new Vector3(-0.6f, 0.55f, -1.95f), new Vector3(0.08f, 0.35f, 0.25f), parts.dark);
            Part(go, "WingR", PrimitiveType.Cube, new Vector3(0.6f, 0.55f, -1.95f), new Vector3(0.08f, 0.35f, 0.25f), parts.dark);
            Part(go, "Wing", PrimitiveType.Cube, new Vector3(0f, 0.75f, -2f), new Vector3(1.8f, 0.07f, 0.45f), paint);
            Part(go, "HeadL", PrimitiveType.Cube, new Vector3(-0.6f, 0.2f, 2.1f), new Vector3(0.4f, 0.14f, 0.05f), parts.headlight);
            Part(go, "HeadR", PrimitiveType.Cube, new Vector3(0.6f, 0.2f, 2.1f), new Vector3(0.4f, 0.14f, 0.05f), parts.headlight);
            Part(go, "TailL", PrimitiveType.Cube, new Vector3(-0.6f, 0.25f, -2.11f), new Vector3(0.45f, 0.12f, 0.03f), parts.taillight);
            Part(go, "TailR", PrimitiveType.Cube, new Vector3(0.6f, 0.25f, -2.11f), new Vector3(0.45f, 0.12f, 0.03f), parts.taillight);

            var car = go.AddComponent<CarController>();
            string[] wheelNames = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject(wheelNames[i]).transform;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = car.wheelAnchors[i] - Vector3.up * (car.suspensionRest - 0.09f);
                var tireGo = Part(pivot.gameObject, "Tire", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.72f, 0.15f, 0.72f), parts.tire);
                tireGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Part(pivot.gameObject, "Hub", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.4f, 0.155f, 0.4f), parts.dark)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                car.wheelVisuals[i] = pivot;
            }

            // Cars live on Ignore Raycast so suspension rays skip them.
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            var mk = Part(go, "MinimapMarker", PrimitiveType.Sphere, new Vector3(0f, 40f, 0f), player ? new Vector3(22f, 1f, 22f) : new Vector3(15f, 1f, 15f), marker);
            mk.layer = MinimapLayer;

            var racer = go.AddComponent<Racer>();
            racer.racerName = name;
            racer.color = color;
            racer.isPlayer = player;
            go.AddComponent<AIDriver>();
            if (player) go.AddComponent<PlayerDriver>();
            go.AddComponent<AudioSource>();
            go.AddComponent<CarAudio>().listenerCar = player;
            return racer;
        }

        static GameObject Part(GameObject parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = name;
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(parent.transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            var mr = p.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return p;
        }

        static Material Mat(string name, Color color, float smoothness, float metallic = 0f, Texture2D tex = null, Color? emission = null)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (tex) m.SetTexture("_BaseMap", tex);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material UnlitMat(string name, Color color)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material GetOrCreate(string name, string shader)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m) return m;
            m = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        // Grayscale value-noise texture used to break up flat colours.
        static Texture2D NoiseTexture(string name, float min, float max, int seed)
        {
            string p = $"{TexDir}/{name}.png";
            if (!File.Exists(p))
            {
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                var rng = new System.Random(seed);
                var grid = new float[17, 17];
                for (int y = 0; y < 17; y++) for (int x = 0; x < 17; x++) grid[x, y] = (float)rng.NextDouble();
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float gx = x / 16f, gy = y / 16f;
                    int ix = (int)gx, iy = (int)gy;
                    float fx = gx - ix, fy = gy - iy;
                    float a = Mathf.Lerp(grid[ix % 16, iy % 16], grid[(ix + 1) % 16, iy % 16], fx);
                    float b = Mathf.Lerp(grid[ix % 16, (iy + 1) % 16], grid[(ix + 1) % 16, (iy + 1) % 16], fx);
                    float smooth = Mathf.Lerp(a, b, fy);
                    float grain = (float)rng.NextDouble();
                    float v = Mathf.Lerp(min, max, smooth * 0.6f + grain * 0.4f);
                    tex.SetPixel(x, y, new Color(v, v, v));
                }
                File.WriteAllBytes(p, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(p);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        [MenuItem("Racing/Build Windows")]
        public static void BuildWindows()
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Racing.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[Racing] Build result: " + report.summary.result + " size " + report.summary.totalSize);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        // Batch entry: Unity -batchmode -executeMethod Racing.EditorTools.RacingSetup.SetupAndBuild -quit
        public static void SetupAndBuild()
        {
            SetupScene();
            BuildWindows();
        }
    }
}
