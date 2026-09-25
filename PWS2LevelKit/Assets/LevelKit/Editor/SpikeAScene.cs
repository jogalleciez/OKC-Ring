// Spike A: does a scene built with stub FuturLab components deserialise onto the real game types?
// Creates Assets/Packs/SpikeA with a scene holding a cube that carries stub UniqueId + WashTarget
// (pointing at a stub WashTargetData asset), a GameJobWashTargetRoot, and a CharacterSpawnPosition.
// PWS2LevelLoader's F5 key loads the pack's catalog, loads the scene additively and logs what it finds.
using System.IO;
using FuturLab;
using FuturLab.PW2;
using FuturLab.PW2.TexturePainter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PWS2LevelKit.Editor
{
    public static class SpikeAScene
    {
        public const string Pack = "SpikeA";
        public const string SceneName = "GAM_SPIKEA_Cube";
        public const int CubeId = 4242;
        public const int RootId = 4200;

        [MenuItem("PWS2/Spikes/A: create scene + build pack")]
        public static void CreateAndBuild()
        {
            Create();
            PackBuilder.Build(Pack, PackBuilder.DefaultDist);
        }

        /// <summary>Command line: -executeMethod PWS2LevelKit.Editor.SpikeAScene.CreateAndBuildFromCommandLine</summary>
        public static void CreateAndBuildFromCommandLine()
        {
            try
            {
                Create();
                EditorApplication.Exit(PackBuilder.Build(Pack, PackBuilder.DefaultDist) ? 0 : 1);
            }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void Create()
        {
            var folder = $"{PackBuilder.PacksRoot}/{Pack}";
            EnsureFolder(PackBuilder.PacksRoot); EnsureFolder(folder); EnsureFolder(folder + "/Scenes"); EnsureFolder(folder + "/Data");

            // data asset referenced by the WashTarget
            var wtdPath = $"{folder}/Data/WTD_SpikeA_Cube.asset";
            var wtd = AssetDatabase.LoadAssetAtPath<WashTargetData>(wtdPath);
            if (wtd == null) { wtd = ScriptableObject.CreateInstance<WashTargetData>(); AssetDatabase.CreateAsset(wtd, wtdPath); }
            wtd.SuggestedControlMapResolution = 64;
            wtd.ControlMapAreaInUse = new RectInt(0, 0, 64, 64);
            wtd.ControlMapTrimmedRect = new RectInt(0, 0, 64, 64);
            wtd.SurfaceCleanThreshold = 0.1f;
            EditorUtility.SetDirty(wtd);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.localScale = new Vector3(4, 1, 4);

            var root = new GameObject("PWS_SPIKEA_Root");
            root.layer = 8; // Washable
            var rootUid = root.AddComponent<UniqueId>(); rootUid.m_id = RootId;
            var gjRoot = root.AddComponent<GameJobWashTargetRoot>(); gjRoot.m_uniqueId = rootUid;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube"; cube.layer = 8;
            cube.transform.SetParent(root.transform, false);
            cube.transform.position = new Vector3(0, 0.5f, 3);
            var uid = cube.AddComponent<UniqueId>(); uid.m_id = CubeId;
            var wt = cube.AddComponent<WashTarget>();
            wt.m_transform = cube.transform;
            wt.m_uniqueId = uid;
            wt.m_washTargetData = wtd;
            wt.m_gameJobWashTargetRoot = gjRoot;
            wt.m_meshRenderer = cube.GetComponent<MeshRenderer>();
            cube.AddComponent<UVMetricsExtra>();

            var spawn = new GameObject("CharacterSpawnPosition");
            spawn.layer = 23; // SpawnPositions
            spawn.transform.position = new Vector3(0, 0, -3);
            var sp = spawn.AddComponent<CharacterSpawnPosition>(); sp.m_type = CharacterSpawnPositionType.Main;
            var cap = spawn.AddComponent<CapsuleCollider>(); cap.radius = 0.5f; cap.height = 1.4f; cap.center = new Vector3(0, 0.7f, 0); cap.isTrigger = true;

            var scenePath = $"{folder}/Scenes/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            File.WriteAllText(Path.Combine(Application.dataPath, "..", folder, "pack.json"),
                "{\n  \"id\": \"SpikeA\",\n  \"displayName\": \"Spike A (stub components)\",\n  \"version\": \"0.0.1\",\n  \"gameVersion\": \"" + PackBuilder.GameVersion + "\",\n  \"catalog\": \"catalog.bin\",\n  \"spike\": { \"scene\": \"" + SceneName + "\", \"cubeUniqueId\": " + CubeId + ", \"rootUniqueId\": " + RootId + " }\n}\n");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"SpikeA: scene {scenePath} and data written");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
