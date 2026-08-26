using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FaceSlapper.EditorTools
{
    /// <summary>
    /// 程序化美术资产生成器：
    /// - 脸部贴图（纯色底 + 两个黑色豆豆眼）
    /// - Toon Ramp 贴图（三阶硬边分阶）
    /// - 低模卡通手掌网格（放松/握持 × 左/右，代码拼盒子，无需外部建模工具）
    /// - 卡通材质（使用 FaceSlapper/ToonLit Shader）
    /// 全部由代码生成、可重复执行（幂等）。
    /// </summary>
    public static class FaceSlapperArtSetup
    {
        public const string ArtDir = "Assets/Art";
        public const string TextureDir = ArtDir + "/Textures";
        public const string MeshDir = ArtDir + "/Meshes";
        public const string MaterialDir = ArtDir + "/Materials";

        public const string FaceTexPath = TextureDir + "/FaceDots.png";
        public const string RampTexPath = TextureDir + "/ToonRamp.png";
        public const string BodyMatPath = MaterialDir + "/M_ToonBody.mat";
        public const string FaceMatPath = MaterialDir + "/M_ToonFace.mat";
        public const string HandMatPath = MaterialDir + "/M_ToonHand.mat";
        public const string WeaponMatPath = MaterialDir + "/M_ToonWeapon.mat";
        public const string GloveMatPath = MaterialDir + "/M_ToonGlove.mat";
        public const string SlimeMatPath = MaterialDir + "/M_ToonSlime.mat";
        public const string DotEyeMatPath = MaterialDir + "/dot_eye.mat";
        public const string EyeDotTexPath = TextureDir + "/EyeDotTex.png";
        public const string StarTexPath = TextureDir + "/StarTex.png";
        public const string StarTexRuntimePath = "Assets/Resources/Art/Textures/StarTex.png";
        public const string StarDecalMatPath = MaterialDir + "/star_decal.mat";

        [MenuItem("FaceSlapper/Generate Art Assets", priority = 2)]
        public static void GenerateArtAssets()
        {
            EnsureArtAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FaceSlapper] 美术资产生成完成（贴图/网格/材质）。");
        }

        /// <summary>确保全部美术资产存在（不存在才生成），供 Setup All 调用。</summary>
        public static void EnsureArtAssets()
        {
            EnsureFolders();
            EnsureFaceTexture();
            EnsureRampTexture();
            EnsureHandMeshes();
            EnsureEyeDotTexture();
            EnsureStarTexture();
            EnsureMaterials();
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(ArtDir)) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(TextureDir)) AssetDatabase.CreateFolder(ArtDir, "Textures");
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(ArtDir, "Meshes");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder(ArtDir, "Materials");
        }

        // ---------------- 贴图 ----------------

        private static void EnsureFaceTexture()
        {
            const int w = 256, h = 256;
            var pixels = new Color[w * h];
            Color bg = new Color(0.98f, 0.94f, 0.86f);   // 纯色米白底
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

            Color eye = new Color(0.07f, 0.07f, 0.09f);  // 黑色豆豆眼（竖椭圆）
            FillEllipse(pixels, w, h, 92, 136, 15, 24, eye);
            FillEllipse(pixels, w, h, 164, 136, 15, 24, eye);

            WritePng(pixels, w, h, FaceTexPath, FilterMode.Bilinear);
        }

        private static void FillEllipse(Color[] pixels, int w, int h, int cx, int cy, int rx, int ry, Color color)
        {
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= w) continue;
                    float dx = (x - cx) / (float)rx;
                    float dy = (y - cy) / (float)ry;
                    if (dx * dx + dy * dy <= 1f)
                        pixels[y * w + x] = color;
                }
            }
        }

        private static void EnsureRampTexture()
        {
            const int w = 128, h = 8;
            var pixels = new Color[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                // 三阶硬边分阶：暗部 / 中间调 / 亮部。
                float v = u < 0.45f ? 0.30f : (u < 0.62f ? 0.60f : 1.0f);
                for (int y = 0; y < h; y++)
                    pixels[y * w + x] = new Color(v, v, v);
            }
            WritePng(pixels, w, h, RampTexPath, FilterMode.Point); // Point 采样保证硬边
        }

        private static void WritePng(Color[] pixels, int w, int h, string path, FilterMode filterMode,
            bool alphaIsTransparency = false, TextureWrapMode wrapMode = TextureWrapMode.Repeat)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.filterMode = filterMode;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = alphaIsTransparency;
                importer.wrapMode = wrapMode;
                importer.SaveAndReimport();
            }
        }

        // ---------------- 手掌网格 ----------------

        public const string HandRelaxedRPath = MeshDir + "/HandRelaxed_R.asset";
        public const string HandRelaxedLPath = MeshDir + "/HandRelaxed_L.asset";
        public const string HandGripRPath = MeshDir + "/HandGrip_R.asset";
        public const string HandGripLPath = MeshDir + "/HandGrip_L.asset";

        private static void EnsureHandMeshes()
        {
            SaveMesh(BuildHand(false), HandRelaxedRPath);
            SaveMesh(BuildHand(true), HandGripRPath);
            SaveMesh(BuildMirrored(BuildHand(false)), HandRelaxedLPath);
            SaveMesh(BuildMirrored(BuildHand(true)), HandGripLPath);
        }

        private static void SaveMesh(MeshBuilder builder, string path)
        {
            Mesh mesh = builder.Build(Path.GetFileNameWithoutExtension(path));
            AssetDatabase.DeleteAsset(path); // 幂等：先删旧资产再建
            AssetDatabase.CreateAsset(mesh, path);
        }

        /// <summary>
        /// 拼一只右手低模手掌：掌心朝下手背朝上，手指朝 +Z，拇指在 -X（身体侧）。
        /// grip = true 时手指大幅弯曲呈握持状。
        /// </summary>
        private static MeshBuilder BuildHand(bool grip)
        {
            var b = new MeshBuilder();

            // 手掌
            b.AddBox(new Vector3(0f, 0f, 0.03f), new Vector3(0.20f, 0.075f, 0.20f), Quaternion.identity);

            // 四根手指（每根两节，远端略细）
            float curl1 = grip ? 70f : 15f;  // 第一节弯曲角
            float curl2 = grip ? 75f : 20f;  // 第二节追加弯曲角
            for (int i = 0; i < 4; i++)
            {
                float x = -0.0675f + i * 0.045f;
                Vector3 basePos = new Vector3(x, 0f, 0.12f);

                Quaternion r1 = Quaternion.Euler(curl1, 0f, 0f);
                Vector3 d1 = r1 * Vector3.forward;
                b.AddBox(basePos + d1 * 0.05f, new Vector3(0.038f, 0.042f, 0.10f), r1);

                Quaternion r2 = Quaternion.Euler(curl1 + curl2, 0f, 0f);
                Vector3 d2 = r2 * Vector3.forward;
                Vector3 tip1 = basePos + d1 * 0.10f;
                b.AddBox(tip1 + d2 * 0.045f, new Vector3(0.034f, 0.038f, 0.09f), r2);
            }

            // 拇指（两节，斜向前内）
            float thumbCurl = grip ? 35f : 8f;
            Vector3 thumbBase = new Vector3(-0.10f, 0f, 0.02f);

            Quaternion tr1 = Quaternion.Euler(thumbCurl, -50f, 0f);
            Vector3 td1 = tr1 * Vector3.forward;
            b.AddBox(thumbBase + td1 * 0.045f, new Vector3(0.045f, 0.045f, 0.09f), tr1);

            Quaternion tr2 = Quaternion.Euler(thumbCurl + (grip ? 40f : 12f), -50f, 0f);
            Vector3 td2 = tr2 * Vector3.forward;
            Vector3 thumbTip1 = thumbBase + td1 * 0.09f;
            b.AddBox(thumbTip1 + td2 * 0.04f, new Vector3(0.04f, 0.04f, 0.08f), tr2);

            return b;
        }

        /// <summary>把右手镜像成左手（X 取反 + 翻转三角形绕序 + 法线取反）。</summary>
        private static MeshBuilder BuildMirrored(MeshBuilder source)
        {
            source.MirrorX();
            return source;
        }

        // ---------------- 材质 ----------------

        private static void EnsureMaterials()
        {
            Shader toon = Shader.Find("FaceSlapper/ToonLit");
            if (toon == null)
            {
                Debug.LogError("[FaceSlapper] 找不到 FaceSlapper/ToonLit Shader。");
                return;
            }

            Texture2D ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(RampTexPath);
            Texture2D face = AssetDatabase.LoadAssetAtPath<Texture2D>(FaceTexPath);

            Material body = EnsureToonMaterial(BodyMatPath, toon);
            body.SetTexture("_RampMap", ramp);

            Material faceMat = EnsureToonMaterial(FaceMatPath, toon);
            faceMat.SetTexture("_BaseMap", face);
            faceMat.SetTexture("_RampMap", ramp);
            faceMat.SetFloat("_OutlineWidth", 0.6f);

            Material hand = EnsureToonMaterial(HandMatPath, toon);
            hand.SetTexture("_RampMap", ramp);

            Material weapon = EnsureToonMaterial(WeaponMatPath, toon);
            weapon.SetTexture("_RampMap", ramp);
            weapon.SetColor("_BaseColor", new Color(0.95f, 0.75f, 0.20f));

            // 拳套材质：经典红色。
            Material glove = EnsureToonMaterial(GloveMatPath, toon);
            glove.SetTexture("_RampMap", ramp);
            glove.SetColor("_BaseColor", new Color(0.80f, 0.15f, 0.15f));

            // 史莱姆材质：不透明果冻质感（硬边高光 + 边缘光 + 顶点抖动），
            // 用于角色外观——把角色身体的材质换成它即可。
            Shader slimeShader = Shader.Find("FaceSlapper/ToonSlime");
            if (slimeShader != null)
            {
                var slime = AssetDatabase.LoadAssetAtPath<Material>(SlimeMatPath);
                if (slime == null)
                {
                    slime = new Material(slimeShader) { name = "M_ToonSlime" };
                    AssetDatabase.CreateAsset(slime, SlimeMatPath);
                }
                if (slime.shader != slimeShader) slime.shader = slimeShader;
                slime.SetColor("_BaseColor", new Color(0.35f, 0.90f, 0.45f));   // 经典史莱姆绿
                slime.SetTexture("_RampMap", ramp);
                slime.SetColor("_SpecColor", Color.white);
                slime.SetFloat("_SpecSize", 0.06f);
                slime.SetFloat("_SpecStrength", 1.2f);
                slime.SetColor("_RimColor", new Color(0.60f, 1.00f, 0.70f));
                slime.SetFloat("_RimPower", 3f);
                slime.SetFloat("_RimStrength", 0.8f);
                slime.SetFloat("_WobbleAmp", 0.02f);
                slime.SetFloat("_WobbleFreq", 6f);
                slime.SetFloat("_WobbleSpeed", 5f);
                EditorUtility.SetDirty(slime);
            }
            else
            {
                Debug.LogError("[FaceSlapper] 找不到 FaceSlapper/ToonSlime Shader。");
            }

            // 豆豆眼贴花材质：形状由贴图 Alpha 决定，任意面片可用。
            Shader decal = Shader.Find("FaceSlapper/ToonDecal");
            if (decal != null)
            {
                var eyeMat = AssetDatabase.LoadAssetAtPath<Material>(DotEyeMatPath);
                if (eyeMat == null)
                {
                    eyeMat = new Material(decal) { name = "dot_eye" };
                    AssetDatabase.CreateAsset(eyeMat, DotEyeMatPath);
                }
                if (eyeMat.shader != decal) eyeMat.shader = decal;
                eyeMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(EyeDotTexPath));
                eyeMat.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(eyeMat);

                // 星星贴花材质：眩晕头顶星星用，黄色。
                var starMat = AssetDatabase.LoadAssetAtPath<Material>(StarDecalMatPath);
                if (starMat == null)
                {
                    starMat = new Material(decal) { name = "star_decal" };
                    AssetDatabase.CreateAsset(starMat, StarDecalMatPath);
                }
                if (starMat.shader != decal) starMat.shader = decal;
                starMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StarTexPath));
                starMat.SetColor("_BaseColor", new Color(1f, 0.9f, 0.2f));
                EditorUtility.SetDirty(starMat);
            }
            else
            {
                Debug.LogError("[FaceSlapper] 找不到 FaceSlapper/ToonDecal Shader。");
            }
        }

        /// <summary>生成眼睛贴图：透明底 + 黑色椭圆（带 2px 柔边，Alpha 决定形状）。</summary>
        private static void EnsureEyeDotTexture()
        {
            const int w = 128, h = 128;
            const float cx = 64f, cy = 64f, rx = 30f, ry = 46f;
            const float feather = 2f; // 边缘羽化像素数

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 到椭圆边界的归一化距离（<0 内部，>0 外部）。
                    float dx = (x - cx) / rx;
                    float dy = (y - cy) / ry;
                    float dist = (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
                    float alpha = Mathf.Clamp01(0.5f - dist / feather);
                    pixels[y * w + x] = new Color(0f, 0f, 0f, alpha);
                }
            }

            WritePng(pixels, w, h, EyeDotTexPath, FilterMode.Bilinear, alphaIsTransparency: true, wrapMode: TextureWrapMode.Clamp);
        }

        /// <summary>生成星星贴图：透明底 + 白色五角星（Alpha 决定形状，供眩晕表现用）。</summary>
        private static void EnsureStarTexture()
        {
            const int w = 128, h = 128;
            const float cx = 64f, cy = 64f;
            const float outer = 56f, inner = 23f;

            // 五角星轮廓（10 个顶点，尖角朝上）。
            var poly = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                float r = k % 2 == 0 ? outer : inner;
                // 注意贴图 Y 轴向下：用 -90° 起始让尖角朝上。
                float a = (-90f + k * 36f) * Mathf.Deg2Rad;
                poly[k] = new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a));
            }

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 射线法点-in-多边形，硬边即可（卡通风格）。
                    bool inside = false;
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    {
                        if ((poly[i].y > y) != (poly[j].y > y) &&
                            x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                            inside = !inside;
                    }
                    pixels[y * w + x] = new Color(1f, 1f, 1f, inside ? 1f : 0f);
                }
            }

            WritePng(pixels, w, h, StarTexPath, FilterMode.Bilinear, alphaIsTransparency: true, wrapMode: TextureWrapMode.Clamp);

            // 额外写一份到 Resources，供运行时（HitFeedbackComponent）Resources.Load。
            Directory.CreateDirectory(Path.GetDirectoryName(StarTexRuntimePath));
            WritePng(pixels, w, h, StarTexRuntimePath, FilterMode.Bilinear, alphaIsTransparency: true, wrapMode: TextureWrapMode.Clamp);
        }

        private static Material EnsureToonMaterial(string path, Shader toon)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(toon) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.shader != toon) mat.shader = toon;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------- 网格拼装工具 ----------------

        /// <summary>用 Unity 内置立方体（拓扑/法线保证正确）拼装低模网格。</summary>
        private class MeshBuilder
        {
            private static Mesh _cube;

            private readonly List<Vector3> _verts = new List<Vector3>(256);
            private readonly List<Vector3> _normals = new List<Vector3>(256);
            private readonly List<Vector2> _uvs = new List<Vector2>(256);
            private readonly List<int> _tris = new List<int>(512);

            private static Mesh Cube
            {
                get
                {
                    if (_cube == null)
                    {
                        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        _cube = go.GetComponent<MeshFilter>().sharedMesh;
                        Object.DestroyImmediate(go);
                    }
                    return _cube;
                }
            }

            /// <summary>添加一个盒子（center 中心，size 全尺寸，rot 旋转）。</summary>
            public void AddBox(Vector3 center, Vector3 size, Quaternion rot)
            {
                Vector3[] cv = Cube.vertices;
                Vector3[] cn = Cube.normals;
                Vector2[] cu = Cube.uv;
                int[] ct = Cube.triangles;

                int baseIndex = _verts.Count;
                for (int i = 0; i < cv.Length; i++)
                {
                    _verts.Add(rot * Vector3.Scale(cv[i], size) + center);
                    _normals.Add(rot * cn[i]);
                    _uvs.Add(cu[i]);
                }
                for (int i = 0; i < ct.Length; i++)
                    _tris.Add(baseIndex + ct[i]);
            }

            /// <summary>原地沿 X 镜像（顶点/法线取反 + 翻转绕序）。</summary>
            public void MirrorX()
            {
                for (int i = 0; i < _verts.Count; i++)
                {
                    _verts[i] = new Vector3(-_verts[i].x, _verts[i].y, _verts[i].z);
                    _normals[i] = new Vector3(-_normals[i].x, _normals[i].y, _normals[i].z);
                }
                for (int i = 0; i < _tris.Count; i += 3)
                    (_tris[i], _tris[i + 1]) = (_tris[i + 1], _tris[i]);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_verts);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
