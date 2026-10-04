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
        const string KenneyCars = Root + "/ThirdParty/Kenney/Cars/";
        const string AmbientCG = Root + "/ThirdParty/AmbientCG/";
        const string PolyHaven = Root + "/ThirdParty/PolyHaven/";

        // Photographed facades (ambientCG): folder, metres per texture repeat, smoothness, metallic.
        static readonly (string id, float tile, float smooth, float metal)[] Facades =
        {
            ("Facade001", 28f, 0.85f, 0.5f),
            ("Facade005", 30f, 0.85f, 0.5f),
            ("Facade006", 28f, 0.6f, 0.2f),
            ("Facade018A", 28f, 0.3f, 0f),
            ("Facade019A", 28f, 0.35f, 0f),
            ("Facade020B", 21f, 0.3f, 0f),
        };

        // Race route through the open city, as street-grid intersections (see CityLayout).
        // Starts on the long northbound avenue at x = 1 and loops clockwise over the big hill.
        static readonly Vector2Int[] RouteIntersections =
        {
            new Vector2Int(1, 1), new Vector2Int(1, 6), new Vector2Int(3, 6), new Vector2Int(3, 4),
            new Vector2Int(5, 4), new Vector2Int(5, 7), new Vector2Int(7, 7), new Vector2Int(7, 2),
            new Vector2Int(4, 2), new Vector2Int(4, 1),
        };

        static Vector2[] RouteCorners()
        {
            var pts = new Vector2[RouteIntersections.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = (Vector2)RouteIntersections[i] * CityLayout.Pitch;
            return pts;
        }

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

        // AI drivers and their Kenney Car Kit models; the player drives the sports sedan.
        static readonly (string name, string model, float skill)[] Drivers =
        {
            ("Blaze", "hatchback-sports", 0.95f),
            ("Viper", "sedan", 0.93f),
            ("Nova", "taxi", 0.91f),
            ("Rook", "police", 0.89f),
            ("Ember", "suv-luxury", 0.87f),
        };
        const string PlayerModel = "sedan-sports";

        [MenuItem("Racing/Setup Scene")]
        public static void SetupScene()
        {
            foreach (var d in new[] { MatDir, TexDir, Root + "/Scenes" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();

            Time.fixedDeltaTime = 1f / 60f;

            var asphaltTex = NoiseTexture("Asphalt", 0.82f, 1f, 3);
            var concreteTex = NoiseTexture("Concrete", 0.85f, 1f, 9);

            var grassTex = NoiseTexture("Grass", 0.8f, 1f, 5);
            var road = PbrMat("Road", "Asphalt025C", new Color(0.95f, 0.95f, 0.95f), 0.4f, 0f);
            var curbRed = Mat("CurbRed", new Color(0.8f, 0.1f, 0.1f), 0.3f);
            var white = Mat("White", new Color(0.95f, 0.95f, 0.95f), 0.3f);
            var yellow = Mat("YellowLine", new Color(0.95f, 0.75f, 0.1f), 0.3f);
            var ground = Mat("Ground", new Color(0.3f, 0.42f, 0.24f), 0.05f, 0f, grassTex);
            var grass = Mat("Grass", new Color(0.36f, 0.56f, 0.26f), 0.1f, 0f, grassTex);
            var sidewalk = PbrMat("Sidewalk", "PavingStones150", new Color(0.9f, 0.9f, 0.9f), 0.25f, 0f);
            sidewalk.SetTextureScale("_BaseMap", new Vector2(1f, 2f));
            var curbStone = Mat("CurbStone", new Color(0.62f, 0.61f, 0.58f), 0.15f, 0f, concreteTex);
            var barrier = Mat("Barrier", new Color(0.86f, 0.86f, 0.84f), 0.2f, 0f, concreteTex);
            var black = Mat("Black", new Color(0.05f, 0.05f, 0.06f), 0.3f);
            var gantry = Mat("Gantry", new Color(0.18f, 0.19f, 0.24f), 0.4f, 0.3f);
            var trunk = Mat("Trunk", new Color(0.36f, 0.24f, 0.14f), 0.1f);
            var leaves = Mat("Leaves", new Color(0.2f, 0.42f, 0.17f), 0.1f);
            var beam = TransparentMat("CheckpointBeam", new Color(1f, 0.8f, 0.1f, 0.35f));
            var routeLine = UnlitMat("RouteLine", new Color(1f, 0.78f, 0.1f));
            var blip = UnlitMat("CheckpointBlip", new Color(1f, 0.45f, 0.05f));
            var lampPole = Mat("LampPole", new Color(0.25f, 0.27f, 0.3f), 0.5f, 0.6f);
            var lampHead = Mat("LampHead", new Color(1f, 0.95f, 0.8f), 0.6f);
            EnableEmission(lampHead, null);
            var facadeMats = new Material[Facades.Length];
            var facadeTiles = new float[Facades.Length];
            for (int i = 0; i < Facades.Length; i++)
            {
                var f = Facades[i];
                facadeMats[i] = PbrMat("Facade_" + f.id, f.id, Color.white, f.smooth, f.metal);
                facadeTiles[i] = f.tile;
            }
            var roofMat = Mat("Roof", new Color(0.42f, 0.42f, 0.44f), 0.15f, 0f, concreteTex);
            var hydrantMat = PropMat("Prop_FireHydrant", "fire_hydrant", 0.45f, 0.3f);
            var trashMat = PropMat("Prop_TrashCan", "metal_trash_can", 0.5f, 0.6f);
            var barrierMat = PropMat("Prop_RoadBarrier", "concrete_road_barrier", 0.2f, 0f);

            var dayCube = Hdri("kloofendal_48d_partly_cloudy_puresky_2k.hdr");
            var nightCube = Hdri("rogland_clear_night_2k.hdr");
            var skyDay = CubeSkybox("SkyDay", dayCube, 1f, 0f);
            var skyNight = CubeSkybox("SkyNight", nightCube, 0.6f, 0f);

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
                urp.shadowDistance = 220f;
                var so = new SerializedObject(urp);
                so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
                so.FindProperty("m_ColorGradingMode").intValue = 1; // HDR grading
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.7f;
            sun.color = new Color(1f, 0.95f, 0.87f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -40f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = skyDay;
            // Image-based lighting from the HDRI: ambient from the sky, reflections from the cubemap.
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = dayCube;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.27f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.79f, 0.86f);
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 1300f;

            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = RealisticProfile();

            // Track.
            var trackGo = new GameObject("Track");
            var path = trackGo.AddComponent<TrackPath>();
            path.controlPoints = CornerFillets(RouteCorners(), 12f);
            path.roadHalfWidth = CityLayout.RoadHalf;
            path.startDistance = 120f;
            path.followTerrain = true;
            var builder = trackGo.AddComponent<TrackBuilder>();
            builder.road = road;
            builder.line = white;
            builder.yellowLine = yellow;
            builder.sidewalk = sidewalk;
            builder.grass = grass;
            builder.barrier = barrier;
            builder.farGround = ground;
            builder.checkerBlack = black;
            builder.gantry = gantry;
            builder.banner = curbRed;
            builder.trunk = trunk;
            builder.leaves = leaves;
            builder.facadeMaterials = facadeMats;
            builder.facadeTiles = facadeTiles;
            builder.roof = roofMat;
            builder.curbs = curbStone;
            builder.hydrant = Model(PolyHaven + "Props/fire_hydrant/fire_hydrant.fbx");
            builder.trashCan = Model(PolyHaven + "Props/metal_trash_can/metal_trash_can.fbx");
            builder.roadBarrier = Model(PolyHaven + "Props/concrete_road_barrier/concrete_road_barrier.fbx");
            builder.hydrantMaterial = hydrantMat;
            builder.trashCanMaterial = trashMat;
            builder.roadBarrierMaterial = barrierMat;
            builder.lampPole = lampPole;
            builder.lampHead = lampHead;
            builder.cityLayer = CityLayer;
            builder.Build();

            // Cars.
            var racers = new Racer[Drivers.Length + 1];
            for (int i = 0; i < Drivers.Length; i++)
            {
                var d = Drivers[i];
                racers[i] = CreateCar(d.name, d.model, false, carPhysics);
                racers[i].GetComponent<AIDriver>().skill = d.skill;
            }
            racers[Drivers.Length] = CreateCar("You", PlayerModel, true, carPhysics);

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
            mini.backgroundColor = new Color(0.08f, 0.09f, 0.1f);
            var miniData = miniGo.AddComponent<UniversalAdditionalCameraData>();
            miniData.renderShadows = false;
            miniData.renderPostProcessing = false;
            miniGo.AddComponent<MinimapCamera>().follow = racers[Drivers.Length].transform;

            // Managers.
            var rmGo = new GameObject("RaceManager");
            var rm = rmGo.AddComponent<RaceManager>();
            var hud = rmGo.AddComponent<RaceHUD>();
            hud.race = rm;
            rm.track = path;
            rm.racers = racers;
            rm.chaseCamera = chase;
            rm.hud = hud;
            rm.laps = 2;
            var routeDisplay = rmGo.AddComponent<RouteDisplay>();
            routeDisplay.race = rm;
            routeDisplay.beamMaterial = beam;
            routeDisplay.routeMaterial = routeLine;
            routeDisplay.blipMaterial = blip;
            routeDisplay.worldLayer = CityLayer;
            routeDisplay.minimapLayer = MinimapLayer;

            var theme = rmGo.AddComponent<ThemeController>();
            theme.sun = sun;
            theme.track = builder;
            theme.cars = racers;
            theme.windowMaterials = facadeMats;
            theme.lampHeadMaterial = lampHead;
            theme.day = new ThemeSettings
            {
                skybox = skyDay,
                reflection = dayCube,
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
                reflection = nightCube,
                ambientIntensity = 0.65f,
                reflectionIntensity = 0.5f,
                sunColor = new Color(0.55f, 0.65f, 1f),
                sunIntensity = 0.12f,
                sunEuler = new Vector3(35f, 150f, 0f),
                ambientSky = new Color(0.1f, 0.12f, 0.22f),
                ambientEquator = new Color(0.08f, 0.08f, 0.12f),
                ambientGround = new Color(0.04f, 0.04f, 0.05f),
                fogColor = new Color(0.04f, 0.05f, 0.09f),
                fogStart = 120f,
                fogEnd = 800f,
                windowGlow = new Color(1.4f, 1.35f, 1.25f),
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
            // D3D12 intermittently crashes in D3D12Core.dll while the player shuts down; D3D11 is stable.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.SaveAssets();
            Debug.Log("[Racing] Scene set up: " + ScenePath + ", route length " + path.Length.ToString("F0") + " m");
        }

        // Builds a racer from a Kenney Car Kit model: the body goes under a tilting pivot, the four wheel
        // nodes move to suspension pivots, and wheel size/positions and the collider come from the model.
        static Racer CreateCar(string name, string modelName, bool player, PhysicsMaterial physics)
        {
            var go = new GameObject(player ? "Car_Player" : "Car_" + name);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var body = new GameObject("BodyVisual");
            body.transform.SetParent(go.transform, false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyCars + modelName + ".fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(body.transform, false);

            var paint = Mat("CarPaint", Color.white, 0.72f, 0.25f, AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyCars + "Textures/colormap.png"));
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = paint;
            var bodyMesh = model.transform.Find("body").GetComponent<Renderer>();
            float scale = 4.3f / bodyMesh.bounds.size.z;
            model.transform.localScale = Vector3.one * scale;

            var car = go.AddComponent<CarController>();
            car.bodyVisual = body.transform;
            string[] nodes = { "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right" };
            var wheelNodes = new Transform[4];
            for (int i = 0; i < 4; i++) wheelNodes[i] = model.transform.Find(nodes[i]);
            float sag = car.suspensionRest - 0.09f;
            float wheelY = wheelNodes[0].localPosition.y * scale;
            model.transform.localPosition = new Vector3(0f, -sag - wheelY, 0f);
            car.wheelRadius = wheelNodes[0].GetComponent<Renderer>().bounds.extents.y;

            var anchors = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 lp = wheelNodes[i].localPosition * scale;
                anchors[i] = new Vector3(lp.x, 0f, lp.z);
                var pivot = new GameObject(nodes[i]).transform;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = anchors[i] - Vector3.up * sag;
                wheelNodes[i].SetParent(pivot, true);
                wheelNodes[i].localPosition = Vector3.zero;
                wheelNodes[i].localRotation = Quaternion.identity;
                car.wheelVisuals[i] = pivot;
            }
            car.wheelAnchors = anchors;

            // Collider from the body (and spoiler) bounds, with the car at the origin.
            var b = bodyMesh.bounds;
            foreach (var r in body.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center + Vector3.up * 0.05f;
            box.size = new Vector3(b.size.x * 0.95f, b.size.y * 0.8f, b.size.z * 0.97f);
            box.sharedMaterial = physics;

            // Cars live on Ignore Raycast so suspension rays skip them.
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            Color color = DominantColor(bodyMesh, KenneyCars + "Textures/colormap.png");
            var marker = UnlitMat("Marker_" + name, color);
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

        // Area-weighted most common saturated colour of a mesh on its palette texture (the car's paint).
        static Color DominantColor(Renderer renderer, string palettePath)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(palettePath));
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices;
            var uvs = mesh.uv;
            var tris = mesh.triangles;
            var weights = new System.Collections.Generic.Dictionary<int, float>();
            var sums = new System.Collections.Generic.Dictionary<int, Color>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                float area = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude;
                Vector2 uv = (uvs[a] + uvs[b] + uvs[c]) / 3f;
                Color col = tex.GetPixelBilinear(uv.x, uv.y);
                Color.RGBToHSV(col, out _, out float sat, out float val);
                if (sat < 0.3f || val < 0.25f) continue;
                int key = Mathf.RoundToInt(col.r * 7) * 64 + Mathf.RoundToInt(col.g * 7) * 8 + Mathf.RoundToInt(col.b * 7);
                weights.TryGetValue(key, out float w);
                weights[key] = w + area;
                sums.TryGetValue(key, out Color sum);
                sums[key] = sum + col * area;
            }
            Object.DestroyImmediate(tex);
            int best = -1;
            float bestW = 0f;
            foreach (var kv in weights) if (kv.Value > bestW) { bestW = kv.Value; best = kv.Key; }
            if (best < 0) return Color.white;
            Color avg = sums[best] / bestW;
            avg.a = 1f;
            return avg;
        }

        // PBR material from an ambientCG folder: Color, NormalGL and (if present) Emission maps.
        static Material PbrMat(string name, string id, Color tint, float smoothness, float metallic)
        {
            string dir = AmbientCG + id + "/" + id;
            var m = Mat(name, tint, smoothness, metallic, AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Color.jpg"));
            var normal = NormalMap(dir + "_NormalGL.jpg");
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_Emission.jpg");
            if (!emission && id.StartsWith("Facade")) emission = LitWindows(dir + "_Color.jpg", TexDir + "/" + id + "_LitWindows.png");
            if (emission) EnableEmission(m, emission);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material PropMat(string name, string id, float smoothness, float metallic)
        {
            string dir = PolyHaven + "Props/" + id + "/" + id;
            var m = Mat(name, Color.white, smoothness, metallic, AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_diff_1k.jpg"));
            var normal = NormalMap(dir + "_nor_gl_1k.exr");
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // Night-time window glow for a facade photo without an emission map: glass (the darker pixels)
        // inside a random ~35% of grid cells lights up warm or cool.
        static Texture2D LitWindows(string colorPath, string outPath)
        {
            if (!File.Exists(outPath))
            {
                var src = new Texture2D(2, 2);
                src.LoadImage(File.ReadAllBytes(colorPath));
                const int size = 1024, cellsX = 16, cellsY = 12;
                var lum = new float[size * size];
                var sorted = new float[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float l = src.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size).grayscale;
                    lum[y * size + x] = l;
                    sorted[y * size + x] = l;
                }
                System.Array.Sort(sorted);
                float glassBelow = sorted[sorted.Length * 45 / 100];
                var rng = new System.Random(colorPath.Length * 31);
                var cellGlow = new Color[cellsX * cellsY];
                for (int i = 0; i < cellGlow.Length; i++)
                {
                    double r = rng.NextDouble();
                    cellGlow[i] = r < 0.22 ? new Color(0.75f, 0.6f, 0.38f) : r < 0.32 ? new Color(0.55f, 0.62f, 0.7f) : Color.black;
                }
                var dst = new Texture2D(size, size, TextureFormat.RGB24, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float l = lum[y * size + x];
                    float glass = Mathf.Clamp01((glassBelow - l) / 0.15f + 0.5f);
                    Color c = cellGlow[(y * cellsY / size) * cellsX + x * cellsX / size];
                    dst.SetPixel(x, y, c * glass);
                }
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);
                AssetDatabase.ImportAsset(outPath);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        static Texture2D NormalMap(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!imp) return null;
            if (imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject Model(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        static Cubemap Hdri(string file)
        {
            string path = PolyHaven + "HDRI/" + file;
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp.textureShape != TextureImporterShape.TextureCube)
            {
                imp.textureShape = TextureImporterShape.TextureCube;
                imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }

        static Material CubeSkybox(string name, Cubemap cube, float exposure, float rotation)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m && m.shader.name != "Skybox/Cubemap") { AssetDatabase.DeleteAsset(p); m = null; }
            if (!m)
            {
                m = new Material(Shader.Find("Skybox/Cubemap")) { name = name };
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetTexture("_Tex", cube);
            m.SetFloat("_Exposure", exposure);
            m.SetFloat("_Rotation", rotation);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Filmic look: ACES tonemapping, light bloom, gentle contrast/saturation, vignette and motion blur.
        static VolumeProfile RealisticProfile()
        {
            string path = Root + "/Settings/RealisticProfile.asset";
            Directory.CreateDirectory(Root + "/Settings");
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0.25f);
            color.contrast.Override(12f);
            color.saturation.Override(6f);
            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            var blur = Add<MotionBlur>(profile);
            blur.intensity.Override(0.18f);
            var wb = Add<WhiteBalance>(profile);
            wb.temperature.Override(4f);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
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

        // Alpha-blended, double-sided unlit material (checkpoint beam).
        static Material TransparentMat(string name, Color color)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material UnlitMat(string name, Color color)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Cull", 0f);
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
