using FaceSlapper.Battle;
using FaceSlapper.Core;
using FaceSlapper.Networking;
using FaceSlapper.Room;
using FaceSlapper.Weapon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TopDownCamera = FaceSlapper.Camera.TopDownCamera;
using Object = UnityEngine.Object;

namespace FaceSlapper.EditorTools
{
    /// <summary>
    /// 一键搭建工具：生成 Player/Weapon Prefab、注册可生成对象、
    /// 创建 Main 场景（网络管理器 + GameEntry + Room + 竞技场 + 出生点 + 相机）。
    /// 所有后端相关的组件（NetworkObject/桥接/传输层/注册）都经 Net.Backend 的
    /// 编辑器钩子完成——更换联机后端时本工具无需修改。
    /// 菜单: FaceSlapper/Setup All
    /// </summary>
    public static class FaceSlapperSetup
    {
        private const string PrefabDir = "Assets/Prefabs";
        private const string MaterialDir = "Assets/Materials";
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/Main.unity";
        private const ushort Port = 7770;

        [MenuItem("FaceSlapper/Setup All", priority = 0)]
        public static void SetupAll()
        {
            EnsureFolders();
            FaceSlapperArtSetup.EnsureArtAssets();

            GameObject playerPrefab = CreatePlayerPrefab();
            GameObject weaponPrefab = CreateWeaponPrefab();
            Net.Backend.EditorRegisterSpawnablePrefab(playerPrefab);
            Net.Backend.EditorRegisterSpawnablePrefab(weaponPrefab);
            CreateMainScene(playerPrefab, weaponPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FaceSlapper] 搭建完成！进入 Main 场景点击 Play，" +
                      "按 ` 打开 GM 命令行：Host() 开房，另一实例 Join(ip) 进房，StartGame() 开始，SpawnWeapon(0,0) 生成武器。");
        }

        [MenuItem("FaceSlapper/Create Prefabs", priority = 1)]
        public static void CreatePrefabsOnly()
        {
            EnsureFolders();
            GameObject playerPrefab = CreatePlayerPrefab();
            GameObject weaponPrefab = CreateWeaponPrefab();
            Net.Backend.EditorRegisterSpawnablePrefab(playerPrefab);
            Net.Backend.EditorRegisterSpawnablePrefab(weaponPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[FaceSlapper] Prefab 已生成并注册。");
        }

        // ---------------- Prefab ----------------

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        private static GameObject CreatePlayerPrefab()
        {
            var root = new GameObject("Player");
            try
            {
                Rigidbody rb = root.AddComponent<Rigidbody>();
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

                // 球形主角：球体碰撞体（半径 0.6，枢轴在脚底）。
                var collider = root.AddComponent<SphereCollider>();
                collider.center = new Vector3(0f, 0.6f, 0f);
                collider.radius = 0.6f;

                Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(FaceSlapperArtSetup.BodyMatPath);
                Material eyeMat = AssetDatabase.LoadAssetAtPath<Material>(FaceSlapperArtSetup.DotEyeMatPath);
                Material handMat = AssetDatabase.LoadAssetAtPath<Material>(FaceSlapperArtSetup.HandMatPath);

                // 身体（球）。
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                body.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
                body.GetComponent<Renderer>().sharedMaterial = bodyMat;

                // 眼睛（纯黑贴花网格，直接贴在球面上，朝向球面外法线）。
                Vector3 sphereCenter = new Vector3(0f, 0.6f, 0f);
                CreateEye(root.transform, "EyeL", new Vector3(-0.13f, 0.72f, 0.575f), sphereCenter, eyeMat);
                CreateEye(root.transform, "EyeR", new Vector3(0.13f, 0.72f, 0.575f), sphereCenter, eyeMat);

                // 两侧手掌（放松 + 握持两套网格，HandVisuals 负责切换）。
                var hands = new GameObject("Hands");
                hands.transform.SetParent(root.transform, false);
                var tintRenderers = new System.Collections.Generic.List<Renderer>
                {
                    body.GetComponent<Renderer>(),
                };
                BuildHand(hands.transform, "HandR", new Vector3(0.70f, 0.55f, 0.05f),
                    FaceSlapperArtSetup.HandRelaxedRPath, FaceSlapperArtSetup.HandGripRPath, handMat, tintRenderers);
                BuildHand(hands.transform, "HandL", new Vector3(-0.70f, 0.55f, 0.05f),
                    FaceSlapperArtSetup.HandRelaxedLPath, FaceSlapperArtSetup.HandGripLPath, handMat, tintRenderers);

                // 武器挂点（右手掌心）。
                var socket = new GameObject("HandSocket");
                socket.transform.SetParent(root.transform, false);
                socket.transform.localPosition = new Vector3(0.70f, 0.55f, 0.12f);

                // 战斗脚本（NetBehaviour 会自动带上 NetObject）。
                var identity = root.AddComponent<NetworkIdentity>();
                SetRendererArray(identity, "_tintRenderers", tintRenderers);
                root.AddComponent<Movement>();
                root.AddComponent<AbilityComponent>();
                root.AddComponent<HitbackAbility>();
                root.AddComponent<PickWeaponAbility>();
                root.AddComponent<SpeedUpAbility>();
                root.AddComponent<HandVisuals>();

                // 后端无关的位置同步 + 后端专属组件（NetworkObject/桥接等）。
                root.AddComponent<NetTransformSync>();
                Net.Backend.EditorEnsurePrefabComponents(root);

                string path = PrefabDir + "/Player.prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[FaceSlapper] 玩家 Prefab: {path}");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>在球面上放一只贴花眼睛（任意面片 + 贴图 Alpha 定形，法线沿球心朝外）。</summary>
        private static void CreateEye(Transform root, string name, Vector3 localPos, Vector3 sphereCenter, Material mat)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Quad);
            eye.name = name;
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.transform.SetParent(root, false);
            eye.transform.localPosition = localPos;
            // Quad 默认朝 -Z，旋转 180° 使其正面沿球面外法线。
            eye.transform.localRotation = Quaternion.LookRotation((localPos - sphereCenter).normalized) * Quaternion.Euler(0f, 180f, 0f);
            eye.transform.localScale = new Vector3(0.10f, 0.16f, 1f);
            eye.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>构建一只手（放松网格默认激活，握持网格默认隐藏）。</summary>
        private static void BuildHand(Transform parent, string name, Vector3 localPos,
            string relaxedMeshPath, string gripMeshPath, Material mat,
            System.Collections.Generic.List<Renderer> tintRenderers)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPos;

            var relaxed = new GameObject("Relaxed");
            relaxed.transform.SetParent(anchor.transform, false);
            relaxed.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(relaxedMeshPath);
            Renderer relaxedRenderer = relaxed.AddComponent<MeshRenderer>();
            relaxedRenderer.sharedMaterial = mat;

            var grip = new GameObject("Grip");
            grip.transform.SetParent(anchor.transform, false);
            grip.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(gripMeshPath);
            Renderer gripRenderer = grip.AddComponent<MeshRenderer>();
            gripRenderer.sharedMaterial = mat;
            grip.SetActive(false);

            tintRenderers.Add(relaxedRenderer);
            tintRenderers.Add(gripRenderer);
        }

        private static GameObject CreateWeaponPrefab()
        {
            var root = new GameObject("SlapperWeapon");
            try
            {
                Rigidbody rb = root.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.35f, 0.55f);
                collider.size = new Vector3(0.16f, 0.7f, 1.3f);

                // 视觉体（拍子）。
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.35f, 0.55f);
                visual.transform.localScale = new Vector3(0.12f, 0.7f, 1.3f);
                visual.GetComponent<Renderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(FaceSlapperArtSetup.WeaponMatPath);

                // 攻击判定点（拍子尖端）。
                var tip = new GameObject("Tip");
                tip.transform.SetParent(root.transform, false);
                tip.transform.localPosition = new Vector3(0f, 0.35f, 1.3f);

                var weapon = root.AddComponent<SlapperWeapon>();
                SetObjectReference(weapon, "_tip", tip.transform);

                root.AddComponent<NetTransformSync>();
                Net.Backend.EditorEnsurePrefabComponents(root);

                string path = PrefabDir + "/SlapperWeapon.prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[FaceSlapper] 武器 Prefab: {path}");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------- 场景 ----------------

        private static void CreateMainScene(GameObject playerPrefab, GameObject weaponPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 网络管理器（由后端创建/配置，如 FishNet NetworkManager + Tugboat）。
            Net.Backend.EditorEnsureManagerInScene(Port);

            // 游戏引导器。
            var entryGo = new GameObject("GameEntry");
            entryGo.AddComponent<GameEntry>();

            // 房间（场景网络对象）。
            var roomGo = new GameObject("Room");
            var room = roomGo.AddComponent<RoomComponent>();
            Net.Backend.EditorEnsurePrefabComponents(roomGo);
            SetObjectReference(room, "_playerPrefab", playerPrefab.GetComponent<NetObject>());
            SetObjectReference(room, "_weaponPrefab", weaponPrefab.GetComponent<NetObject>());

            CreateArena();
            CreateSpawnPoints();
            CreateCamera();
            CreateLight();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[FaceSlapper] 场景已保存: {ScenePath}");
        }

        private static void CreateArena()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f); // 30m x 30m
            ground.GetComponent<Renderer>().sharedMaterial =
                EnsureMaterial("GroundMat", new Color(0.30f, 0.42f, 0.34f));

            Material wallMat = EnsureMaterial("WallMat", new Color(0.55f, 0.50f, 0.45f));
            CreateWall("WallN", new Vector3(0f, 0.75f, 15f), new Vector3(31f, 1.5f, 1f), wallMat);
            CreateWall("WallS", new Vector3(0f, 0.75f, -15f), new Vector3(31f, 1.5f, 1f), wallMat);
            CreateWall("WallE", new Vector3(15f, 0.75f, 0f), new Vector3(1f, 1.5f, 31f), wallMat);
            CreateWall("WallW", new Vector3(-15f, 0.75f, 0f), new Vector3(1f, 1.5f, 31f), wallMat);
        }

        private static void CreateWall(string name, Vector3 position, Vector3 scale, Material mat)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void CreateSpawnPoints()
        {
            var root = new GameObject("SpawnPoints");
            Vector3[] positions =
            {
                new Vector3(4f, 0.05f, 4f),
                new Vector3(-4f, 0.05f, 4f),
                new Vector3(4f, 0.05f, -4f),
                new Vector3(-4f, 0.05f, -4f),
            };
            foreach (Vector3 pos in positions)
            {
                var point = new GameObject("SpawnPoint");
                point.transform.SetParent(root.transform, false);
                point.transform.position = pos;
                point.AddComponent<PlayerSpawnPoints>();
            }
        }

        private static void CreateCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 14f, -10f);
            camGo.transform.rotation = Quaternion.Euler(56f, 0f, 0f);
            camGo.AddComponent<UnityEngine.Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<TopDownCamera>();
        }

        private static void CreateLight()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // ---------------- 工具 ----------------

        private static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>通过 SerializedObject 给 private [SerializeField] 渲染器数组赋值。</summary>
        private static void SetRendererArray(Object target, string fieldName, System.Collections.Generic.List<Renderer> renderers)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[FaceSlapper] 字段 {fieldName} 在 {target.GetType().Name} 上未找到。");
                return;
            }
            prop.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>通过 SerializedObject 给 private [SerializeField] 字段赋对象引用。</summary>
        private static void SetObjectReference(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[FaceSlapper] 字段 {fieldName} 在 {target.GetType().Name} 上未找到。");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
