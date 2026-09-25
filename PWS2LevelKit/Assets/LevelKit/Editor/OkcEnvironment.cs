// OKC level environment: the plaza (scenery from build_okc_plaza.py) and a lighting rig that mirrors what the
// game's own levels carry at runtime (docs: recon of GAM_PW2_HW_Billboard's lighting dump, 2026-09-23):
//   an active Directional Light (1.5, warm white, soft shadows 0.85) set as RenderSettings.sun,
//   Trilight ambient, a skybox, a baked Reflection Probe also used as the custom default reflection,
//   and a global PostProcessingVolume on the Default layer (the player camera's volume mask is 1)
//   with Bloom + ColorAdjustments. No lightmaps: game renderers all have lightmapIndex -1.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PWS2LevelKit.Editor
{
    public static class OkcEnvironment
    {
        [Serializable] class PlazaInfo { public PlazaObject[] objects; public PlazaMaterial[] materials; }
        [Serializable] class PlazaObject { public string name, material, collider, layer; }
        [Serializable] class PlazaMaterial { public string name, texture; public float[] color; public float metallic, smoothness; public float[] metresPerRepeat; }

        public const string EnvRootName = "PEV_OKC_Plaza";
        public const string LightRootName = "PLT_OKC_TransformerScreen";
        const int NavigationCollisionLayer = 12;

        /// <summary>
        /// Imports export/OKC_Plaza.fbx + OKC_Plaza.json + plaza_tex/*.png and instantiates every piece into the
        /// open scene. Returns the horizontal unit direction from the ring towards the street (plaza "south").
        /// </summary>
        public static Vector3 BuildPlaza(string blenderExport, string sourceFolder, string packDataFolder)
        {
            var plazaSrc = $"{sourceFolder}/Plaza";
            EnsureFolder(plazaSrc);
            var fbxPath = $"{plazaSrc}/OKC_Plaza.fbx";
            File.Copy(Path.Combine(blenderExport, "OKC_Plaza.fbx"), Abs(fbxPath), true);
            foreach (var png in Directory.GetFiles(Path.Combine(blenderExport, "plaza_tex"), "*.png"))
                File.Copy(png, Abs($"{plazaSrc}/{Path.GetFileName(png)}"), true);
            var info = JsonUtility.FromJson<PlazaInfo>(File.ReadAllText(Path.Combine(blenderExport, "OKC_Plaza.json")));
            AssetDatabase.Refresh();

            var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            mi.globalScale = 1f; mi.useFileScale = true;
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false; mi.importLights = false;
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new Exception($"OkcEnvironment: {fbxPath} did not import");

            var mats = new Dictionary<string, Material>();
            foreach (var m in info.materials)
            {
                Texture2D tex = null;
                if (!string.IsNullOrEmpty(m.texture))
                {
                    var tp = $"{plazaSrc}/{m.texture}";
                    var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.mipmapEnabled = true;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.anisoLevel = 8;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                }
                var path = $"{packDataFolder}/MAT_{m.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", new Color(m.color[0], m.color[1], m.color[2], 1f));
                mat.SetFloat("_Metallic", m.metallic);
                mat.SetFloat("_Smoothness", m.smoothness);
                mat.SetTextureScale("_BaseMap", new Vector2(1f / m.metresPerRepeat[0], 1f / m.metresPerRepeat[1]));
                EditorUtility.SetDirty(mat);
                mats[m.name] = mat;
            }

            var root = new GameObject(EnvRootName);
            var filters = model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
            Vector3 streetCentre = Vector3.zero;
            foreach (var o in info.objects)
            {
                if (!filters.TryGetValue(o.name, out var src)) throw new Exception($"OkcEnvironment: {o.name} missing from OKC_Plaza.fbx");
                var go = new GameObject(o.name);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                go.transform.localScale = src.transform.lossyScale;
                var mesh = src.sharedMesh;
                if (o.collider == "box")
                {
                    go.layer = o.layer == "NavigationCollision" ? NavigationCollisionLayer : 0;
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = mesh.bounds.center; bc.size = mesh.bounds.size;
                    continue;
                }
                go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[o.material];
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                if (o.collider == "mesh") go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (o.name == "OKC_Plaza_Road") streetCentre = mr.bounds.center;
            }
            var south = new Vector3(streetCentre.x, 0, streetCentre.z);
            if (south.sqrMagnitude < 1e-4f) throw new Exception("OkcEnvironment: could not locate the road");
            south.Normalize();
            // snap to the nearest axis: the square is axis-aligned
            south = Mathf.Abs(south.x) > Mathf.Abs(south.z) ? new Vector3(Mathf.Sign(south.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(south.z));
            Debug.Log($"OkcEnvironment: plaza with {info.objects.Length} pieces, street direction {south}");
            return south;
        }

        /// <summary>Sun, sky, ambient, reflection probe and post volume, all under PLT_OKC_TransformerScreen.</summary>
        public static ReflectionProbe BuildLighting(string packDataFolder, Vector3 south)
        {
            var root = new GameObject(LightRootName);

            var sunGo = new GameObject("Directional Light");
            sunGo.transform.SetParent(root.transform, false);
            // light from over the camera's shoulder (the street side), 50 degrees up, a little from the east
            var yaw = Mathf.Atan2(-south.x, -south.z) * Mathf.Rad2Deg + 25f;
            sunGo.transform.rotation = Quaternion.Euler(50f, yaw, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.972f, 0.91f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            var sunData = sunGo.AddComponent<UniversalAdditionalLightData>();
            sunData.usePipelineSettings = true;

            // The game build has no built-in skybox shaders (a Skybox/Procedural material renders magenta in game), so
            // the sky is a dome: an inward-facing sphere with URP/Unlit and a generated equirectangular sky texture.
            BuildSkyDome(root.transform, packDataFolder, sunGo.transform.forward);
            RenderSettings.skybox = null;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientSkyColor = new Color(0.258f, 0.386f, 0.708f);
            RenderSettings.ambientEquatorColor = new Color(0.591f, 0.694f, 0.896f);
            RenderSettings.ambientGroundColor = new Color(0.62f, 0.55f, 0.45f);
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.reflectionBounces = 1;

            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.SetParent(root.transform, false);
            probeGo.transform.position = new Vector3(0f, 2.5f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.resolution = 128;
            probe.hdr = true;
            probe.size = new Vector3(90f, 45f, 90f);
            probe.importance = 1;

            var profilePath = $"{packDataFolder}/PPV_OKC.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var bloom = profile.Add<Bloom>(false);
            bloom.threshold.Override(2f);
            bloom.intensity.Override(0.1f);
            var color = profile.Add<ColorAdjustments>(false);
            color.postExposure.Override(0.1f);
            color.contrast.Override(15f);
            color.colorFilter.Override(new Color(1f, 0.933f, 0.882f));
            color.saturation.Override(6f);
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("PostProcessingVolume");
            volGo.transform.SetParent(root.transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.weight = 1f;
            vol.sharedProfile = profile;
            return probe;
        }

        /// <summary>Bakes the probe into the pack and makes it the scene's default (custom) reflection, as the game's levels do.</summary>
        public static void BakeReflections(ReflectionProbe probe, string packDataFolder)
        {
            var path = $"{packDataFolder}/ReflectionProbe_OKC.exr";
            if (!Lightmapping.BakeReflectionProbe(probe, path)) throw new Exception("OkcEnvironment: reflection probe bake failed (needs a GPU: do not pass -nographics)");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (cube == null) throw new Exception($"OkcEnvironment: {path} is not a cubemap");
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cube;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            Debug.Log($"OkcEnvironment: reflection probe baked to {path} ({cube.width}px)");
        }

        const float DomeRadius = 450f; // inside the game's TimeLapseCamera far plane (500 m)

        static void BuildSkyDome(Transform parent, string packDataFolder, Vector3 sunForward)
        {
            var texPath = $"{packDataFolder}/SKY_OKC_Dome.png";
            File.WriteAllBytes(Abs(texPath), SkyTexture(2048, 1024, -sunForward).EncodeToPNG());
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();

            var matPath = $"{packDataFolder}/MAT_OKC_SkyDome.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);

            var meshPath = $"{packDataFolder}/Mesh_OKC_SkyDome.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mesh = DomeMesh(DomeRadius, 64, 32);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("SkyDome");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
        }

        /// <summary>UV sphere seen from inside: u = longitude, v = latitude (0 = nadir, 1 = zenith).</summary>
        static Mesh DomeMesh(float r, int lon, int lat)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= lat; j++)
                for (int i = 0; i <= lon; i++)
                {
                    float u = (float)i / lon, v = (float)j / lat;
                    float phi = u * Mathf.PI * 2f, el = (v - 0.5f) * Mathf.PI;
                    verts.Add(new Vector3(Mathf.Cos(el) * Mathf.Sin(phi), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(phi)) * r);
                    uvs.Add(new Vector2(u, v));
                }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + 1, c = a + lon + 1, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d }); // clockwise seen from inside (Unity front faces are clockwise)
                }
            var m = new Mesh { name = "Mesh_OKC_SkyDome" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * r * 2f);
            return m;
        }

        /// <summary>Equirectangular daytime sky: horizon-to-zenith gradient, sun glow, soft clouds, hazy ground below.</summary>
        static Texture2D SkyTexture(int w, int h, Vector3 toSun)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            var px = new Color32[w * h];
            Color zenith = new Color(0.20f, 0.40f, 0.80f), horizon = new Color(0.60f, 0.74f, 0.93f), ground = new Color(0.60f, 0.62f, 0.64f);
            toSun.Normalize();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float phi = u * Mathf.PI * 2f, el = (v - 0.5f) * Mathf.PI;
                    var dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(phi), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(phi));
                    Color c;
                    if (el < 0f) c = Color.Lerp(horizon, ground, Mathf.Clamp01(-el / 0.15f));
                    else
                    {
                        float t = Mathf.Pow(Mathf.Clamp01(el / (Mathf.PI / 2f)), 0.35f);
                        c = Color.Lerp(horizon, zenith, t);
                        // clouds: tileable in u by blending the noise with its copy one period to the left
                        float s = 7f, cv = Mathf.Clamp01(el / 0.9f);
                        float n = (1f - u) * Fbm(u * s, cv * 5f) + u * Fbm((u - 1f) * s, cv * 5f);
                        float cloud = Mathf.SmoothStep(0.52f, 0.72f, n) * Mathf.Clamp01(el / 0.08f) * (1f - 0.5f * cv);
                        c = Color.Lerp(c, new Color(0.97f, 0.97f, 0.98f), cloud * 0.85f);
                    }
                    float sun = Mathf.Max(0f, Vector3.Dot(dir, toSun));
                    c += new Color(1f, 0.93f, 0.8f) * (Mathf.Pow(sun, 64f) * 0.6f + Mathf.Pow(sun, 8f) * 0.12f);
                    px[y * w + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), 255);
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, f = 1f;
            for (int o = 0; o < 5; o++) { sum += amp * Mathf.PerlinNoise(x * f + 17.3f * o, y * f + 5.1f * o); amp *= 0.5f; f *= 2.1f; }
            return sum / 0.97f;
        }

        static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
