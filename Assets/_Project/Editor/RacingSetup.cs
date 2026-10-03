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
        const int CityLayer = 30;

        // Street circuit drawn as a city-block polyline; corners are rounded by CornerFillets.
        // Starts at the bottom of the main straight (x = 0) and runs clockwise.
        static readonly Vector2[] StreetCorners =
        {
            new Vector2(0f, -180f),
            new Vector2(0f, 220f),
            new Vector2(120f, 220f),
            new Vector2(120f, 140f),
            new Vector2(260f, 140f),
            new Vector2(260f, 300f),
            new Vector2(420f, 300f),
            new Vector2(420f, -40f),
            new Vector2(300f, -40f),
            new Vector2(300f, -180f),
        };

        // Replaces each corner with points before/after it so the spline turns with a ~20 m radius
        // and stays straight between corners.
        static Vector3[] CornerFillets(Vector2[] corners, float r)
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            int n = corners.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 c = corners[i], prev = corners[(i - 1 + n) % n], next = corners[(i + 1) % n];
                Vector2 din = (c - prev).normalized, dout = (next - c).normalized;
                if (Vector2.Distance(prev, c) > r * 4f + 10f) pts.Add(V3(c - din * r * 2f));
                pts.Add(V3(c - din * r));
                pts.Add(V3(c + dout * r));
                if (Vector2.Distance(c, next) > r * 4f + 10f) pts.Add(V3(c + dout * r * 2f));
            }
            return pts.ToArray();
        }

        static Vector3 V3(Vector2 v) => new Vector3(v.x, 0f, v.y);

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
            var concreteTex = NoiseTexture("Concrete", 0.85f, 1f, 9);
            var (windowTex, windowLit) = WindowTextures("Windows", 11);

            var road = Mat("Road", new Color(0.28f, 0.28f, 0.3f), 0.25f, 0f, asphaltTex);
            var curbRed = Mat("CurbRed", new Color(0.8f, 0.1f, 0.1f), 0.3f);
            var white = Mat("White", new Color(0.95f, 0.95f, 0.95f), 0.3f);
            var ground = Mat("Ground", new Color(0.36f, 0.36f, 0.38f), 0.15f, 0f, asphaltTex);
            var sidewalk = Mat("Sidewalk", new Color(0.66f, 0.65f, 0.62f), 0.1f, 0f, concreteTex);
            var barrier = Mat("Barrier", new Color(0.86f, 0.86f, 0.84f), 0.2f, 0f, concreteTex);
            var adA = Mat("AdRed", new Color(0.85f, 0.15f, 0.12f), 0.4f);
            var adB = Mat("AdBlue", new Color(0.1f, 0.35f, 0.85f), 0.4f);
            var black = Mat("Black", new Color(0.05f, 0.05f, 0.06f), 0.3f);
            var gantry = Mat("Gantry", new Color(0.18f, 0.19f, 0.24f), 0.4f, 0.3f);
            var stands = Mat("Stands", new Color(0.55f, 0.58f, 0.66f), 0.2f);
            var roofMat = Mat("Roof", new Color(0.3f, 0.3f, 0.32f), 0.1f, 0f, concreteTex);
            var lampPole = Mat("LampPole", new Color(0.25f, 0.27f, 0.3f), 0.5f, 0.6f);
            var lampHead = Mat("LampHead", new Color(1f, 0.95f, 0.8f), 0.6f);
            EnableEmission(lampHead, null);
            var facades = new[]
            {
                Mat("FacadeConcrete", new Color(0.78f, 0.77f, 0.74f), 0.3f, 0f, windowTex),
                Mat("FacadeGlass", new Color(0.55f, 0.7f, 0.85f), 0.85f, 0.4f, windowTex),
                Mat("FacadeBrick", new Color(0.66f, 0.38f, 0.3f), 0.15f, 0f, windowTex),
                Mat("FacadeSand", new Color(0.86f, 0.78f, 0.6f), 0.2f, 0f, windowTex),
                Mat("FacadeSlate", new Color(0.42f, 0.46f, 0.52f), 0.5f, 0.2f, windowTex),
            };
            foreach (var f in facades) EnableEmission(f, windowLit);
            var skyDay = Skybox("SkyDay", new Color(0.5f, 0.5f, 0.5f), new Color(0.37f, 0.35f, 0.33f), 1.3f, 1f, 0.04f);
            var skyNight = Skybox("SkyNight", new Color(0.18f, 0.22f, 0.45f), new Color(0.02f, 0.02f, 0.03f), 0.12f, 0.45f, 0.02f);
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

            foreach (var rpPath in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
                if (!urp) continue;
                urp.shadowDistance = 160f;
                EditorUtility.SetDirty(urp);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -40f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = skyDay;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.27f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.77f, 0.84f);
            RenderSettings.fogStartDistance = 200f;
            RenderSettings.fogEndDistance = 1100f;

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
            path.controlPoints = CornerFillets(StreetCorners, 20f);
            path.startDistance = 120f;
            var builder = trackGo.AddComponent<TrackBuilder>();
            builder.road = road;
            builder.curbRed = curbRed;
            builder.curbWhite = white;
            builder.line = white;
            builder.sidewalk = sidewalk;
            builder.barrier = barrier;
            builder.adA = adA;
            builder.adB = adB;
            builder.ground = ground;
            builder.checkerBlack = black;
            builder.gantry = gantry;
            builder.stands = stands;
            builder.roof = roofMat;
            builder.lampPole = lampPole;
            builder.lampHead = lampHead;
            builder.facades = facades;
            builder.cityLayer = CityLayer;
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
            mini.cullingMask = ~(1 << CityLayer);
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

            var theme = rmGo.AddComponent<ThemeController>();
            theme.sun = sun;
            theme.track = builder;
            theme.cars = racers;
            theme.windowMaterials = facades;
            theme.lampHeadMaterial = lampHead;
            theme.day = new ThemeSettings
            {
                skybox = skyDay,
                sunColor = sun.color,
                sunIntensity = sun.intensity,
                sunEuler = sun.transform.eulerAngles,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                fogColor = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                windowGlow = Color.black,
                lampGlow = Color.black,
                lightsOn = false,
            };
            theme.night = new ThemeSettings
            {
                skybox = skyNight,
                sunColor = new Color(0.55f, 0.65f, 1f),
                sunIntensity = 0.22f,
                sunEuler = new Vector3(35f, 150f, 0f),
                ambientSky = new Color(0.1f, 0.12f, 0.22f),
                ambientEquator = new Color(0.08f, 0.08f, 0.12f),
                ambientGround = new Color(0.04f, 0.04f, 0.05f),
                fogColor = new Color(0.04f, 0.05f, 0.09f),
                fogStart = 120f,
                fogEnd = 800f,
                windowGlow = new Color(1.6f, 1.5f, 1.3f),
                lampGlow = new Color(1f, 0.85f, 0.6f) * 4f,
                lightsOn = true,
            };
            rm.theme = theme;
            rm.minimapCamera = mini;
            rmGo.AddComponent<MainMenu>().race = rm;

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

            // Body parts hang off a pivot the controller tilts for roll/pitch.
            var body = new GameObject("BodyVisual");
            body.transform.SetParent(go.transform, false);
            Part(body, "Body", PrimitiveType.Cube, new Vector3(0f, 0.15f, 0f), new Vector3(1.8f, 0.5f, 4.2f), paint);
            Part(body, "Nose", PrimitiveType.Cube, new Vector3(0f, 0.05f, 2.05f), new Vector3(1.7f, 0.3f, 0.3f), paint);
            Part(body, "Cabin", PrimitiveType.Cube, new Vector3(0f, 0.62f, -0.35f), new Vector3(1.45f, 0.45f, 1.9f), parts.glass);
            Part(body, "Stripe", PrimitiveType.Cube, new Vector3(0f, 0.41f, 1.05f), new Vector3(0.45f, 0.02f, 2f), Mat("White", Color.white, 0.3f));
            Part(body, "WingL", PrimitiveType.Cube, new Vector3(-0.6f, 0.55f, -1.95f), new Vector3(0.08f, 0.35f, 0.25f), parts.dark);
            Part(body, "WingR", PrimitiveType.Cube, new Vector3(0.6f, 0.55f, -1.95f), new Vector3(0.08f, 0.35f, 0.25f), parts.dark);
            Part(body, "Wing", PrimitiveType.Cube, new Vector3(0f, 0.75f, -2f), new Vector3(1.8f, 0.07f, 0.45f), paint);
            Part(body, "HeadL", PrimitiveType.Cube, new Vector3(-0.6f, 0.2f, 2.1f), new Vector3(0.4f, 0.14f, 0.05f), parts.headlight);
            Part(body, "HeadR", PrimitiveType.Cube, new Vector3(0.6f, 0.2f, 2.1f), new Vector3(0.4f, 0.14f, 0.05f), parts.headlight);
            Part(body, "TailL", PrimitiveType.Cube, new Vector3(-0.6f, 0.25f, -2.11f), new Vector3(0.45f, 0.12f, 0.03f), parts.taillight);
            Part(body, "TailR", PrimitiveType.Cube, new Vector3(0.6f, 0.25f, -2.11f), new Vector3(0.45f, 0.12f, 0.03f), parts.taillight);

            var car = go.AddComponent<CarController>();
            car.bodyVisual = body.transform;
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

        // Facade textures: 8x8 window cells (light frames, dark glass, a few warm windows) plus a
        // matching emission map where roughly 40% of windows are lit at night.
        static (Texture2D day, Texture2D lit) WindowTextures(string name, int seed)
        {
            string p = $"{TexDir}/{name}.png", pl = $"{TexDir}/{name}Lit.png";
            if (!File.Exists(p) || !File.Exists(pl))
            {
                const int size = 256, cells = 8, cell = size / cells;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                var lit = new Texture2D(size, size, TextureFormat.RGB24, false);
                var rng = new System.Random(seed);
                var litRng = new System.Random(seed + 1);
                for (int cy = 0; cy < cells; cy++)
                for (int cx = 0; cx < cells; cx++)
                {
                    float r = (float)rng.NextDouble();
                    float lr = (float)litRng.NextDouble();
                    Color glass = r < 0.12f ? new Color(0.95f, 0.85f, 0.55f) : Color.Lerp(new Color(0.12f, 0.15f, 0.2f), new Color(0.32f, 0.38f, 0.46f), r);
                    Color glow = r < 0.12f || lr < 0.32f ? Color.Lerp(new Color(1f, 0.78f, 0.45f), new Color(0.75f, 0.85f, 1f), lr) * (0.7f + 0.3f * lr) : Color.black;
                    for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        bool frame = x < 5 || x >= cell - 5 || y < 7 || y >= cell - 4;
                        float n = 0.93f + (float)rng.NextDouble() * 0.07f;
                        tex.SetPixel(cx * cell + x, cy * cell + y, frame ? new Color(n, n, n) : glass);
                        lit.SetPixel(cx * cell + x, cy * cell + y, frame ? Color.black : glow);
                    }
                }
                File.WriteAllBytes(p, tex.EncodeToPNG());
                File.WriteAllBytes(pl, lit.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(lit);
                AssetDatabase.ImportAsset(p);
                AssetDatabase.ImportAsset(pl);
            }
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(p), AssetDatabase.LoadAssetAtPath<Texture2D>(pl));
        }

        static Material Skybox(string name, Color tint, Color groundColor, float exposure, float atmosphere, float sunSize)
        {
            var m = GetOrCreate(name, "Skybox/Procedural");
            m.SetColor("_SkyTint", tint);
            m.SetColor("_GroundColor", groundColor);
            m.SetFloat("_Exposure", exposure);
            m.SetFloat("_AtmosphereThickness", atmosphere);
            m.SetFloat("_SunSize", sunSize);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Emission is driven at runtime by ThemeController; enable the keyword so the variant ships.
        static void EnableEmission(Material m, Texture2D map)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (map) m.SetTexture("_EmissionMap", map);
            m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
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
