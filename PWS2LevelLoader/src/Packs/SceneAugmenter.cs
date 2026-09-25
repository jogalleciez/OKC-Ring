using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppFuturLab.PW2;
using Il2CppFuturLab.PW2.TexturePainter;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace PWS2LevelLoader.Packs
{
    /// <summary>
    /// Pack jobs that run on one of the game's own scenes (e.g. OKC_STREETCAR on GAM_PW2_HS_Tram): when such a job is
    /// the one loading, hide what pack.json lists (a wash-target root's targets, named scene roots) and instantiate the
    /// pack's prefabs into the scene. Runs from Core.OnSceneWasLoaded, i.e. before the game's level pipeline gets to
    /// WashManager.InitializeScene, which is why the prefabs are loaded ahead of time (in the hub) and never waited on.
    /// A vanilla load of the same scene is left untouched: the loading job is read from LoadingLevelState.Job.
    /// </summary>
    internal static class SceneAugmenter
    {
        private static readonly Dictionary<string, AsyncOperationHandle<GameObject>> _prefabs = new Dictionary<string, AsyncOperationHandle<GameObject>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _prefabFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // what the last Apply switched off, re-checked once the level is running (the game may re-enable things)
        private static readonly List<Component> _hidden = new List<Component>();
        private static float _recheckAt = -1f;
        private static string _recheckScene;

        /// <summary>Start loading every scene-addition prefab whose pack catalog is ready. Cheap; call every frame.</summary>
        public static void PreloadPoll()
        {
            foreach (var pack in PackRegistry.Packs)
                foreach (var job in pack.Jobs)
                    foreach (var add in job.SceneAdditions)
                    {
                        var guid = add.PrefabGuid;
                        if (string.IsNullOrEmpty(guid) || _prefabs.ContainsKey(guid) || _prefabFailed.Contains(guid)) continue;
                        if (!PackRegistry.IsCatalogReady(pack.Id)) continue;
                        try
                        {
                            _prefabs[guid] = new AssetReferenceGameObject(guid).LoadAssetAsync();
                            Core.Log.Msg($"[augment] {job.UniqueName}: prefab {guid} load started");
                        }
                        catch (Exception ex) { _prefabFailed.Add(guid); Core.Log.Error($"[augment] {job.UniqueName}: prefab {guid} LoadAssetAsync threw {ex}"); }
                    }
        }

        /// <summary>The job the game is loading right now, or null (read from the main viewport's LoadingLevelState).</summary>
        public static GameJob LoadingJob(out string stateName)
        {
            stateName = "null";
            try
            {
                var state = PwsScreenManager.Instance?.MainViewport?.CurrentState;
                if (state == null) return null;
                try { stateName = state.GetIl2CppType().Name; } catch { stateName = "?"; }
                return state.TryCast<LoadingLevelState>()?.Job;
            }
            catch (Exception ex) { stateName = $"<{ex.GetType().Name}>"; return null; }
        }

        /// <summary>The pack job with scene augmentation for <paramref name="sceneName"/> that is loading now, if any.</summary>
        public static PackManifest.PackJob ActiveAugmentedJob(string sceneName)
        {
            var candidates = PackRegistry.Packs.SelectMany(p => p.Jobs).Where(j => j.AugmentsScene && j.SceneName == sceneName).ToList();
            if (candidates.Count == 0) return null;
            var job = LoadingJob(out var stateName);
            if (job == null) { Core.Log.Msg($"[augment] {sceneName}: no loading job readable (state {stateName}); leaving the scene alone"); return null; }
            var match = candidates.FirstOrDefault(j => j.UniqueId == job.m_uniqueId);
            if (match == null) Core.Log.Msg($"[augment] {sceneName}: loading job '{job.m_uniqueName}' is not a pack job; leaving the scene alone");
            return match;
        }

        /// <summary>The augmented pack job applied to <see cref="AppliedScene"/> by the last Apply (null for a vanilla load).</summary>
        public static PackManifest.PackJob AppliedJob { get; private set; }
        public static string AppliedScene { get; private set; }

        public static void Apply(string sceneName)
        {
            var job = ActiveAugmentedJob(sceneName);
            AppliedScene = sceneName; AppliedJob = job;
            if (job == null) return;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) { Core.Log.Warning($"[augment] {sceneName} is not loaded"); return; }

            _hidden.Clear();
            var roots = scene.GetRootGameObjects().Where(r => r != null).ToList();

            // 0. measure the volumes to clear before anything is switched off (a disabled renderer has no bounds)
            var volume = ContainVolume(job, roots, sceneName);

            // 1. hide: every WashTarget of the listed roots (the game flattens targets to the scene root at runtime,
            //    so walk the targets, not the root's children), the root objects themselves, and the named objects
            int washHidden = 0, namedHidden = 0;
            var washRoots = new HashSet<string>(job.HideWashRoots, StringComparer.Ordinal);
            if (washRoots.Count > 0)
            {
                foreach (var root in roots)
                    foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<WashTarget>(), true))
                    {
                        var wt = c?.TryCast<WashTarget>();
                        var owner = wt?.m_gameJobWashTargetRoot;
                        if (owner == null || !washRoots.Contains(owner.gameObject.name)) continue;
                        Hide(wt.gameObject); washHidden++;
                    }
                foreach (var root in roots.Where(r => washRoots.Contains(r.name))) Hide(root);
            }
            foreach (var root in roots)
                if (job.HideObjects.Any(p => NameMatches(root.name, p))) { Hide(root); namedHidden++; }
            int containedHidden = volume.HasValue ? HideContained(volume.Value, job, roots, sceneName) : 0;

            // 2. add
            int added = 0;
            foreach (var add in job.SceneAdditions)
            {
                if (!_prefabs.TryGetValue(add.PrefabGuid ?? "", out var h) || !h.IsDone || h.Status != AsyncOperationStatus.Succeeded || h.Result == null)
                {
                    var why = !_prefabs.ContainsKey(add.PrefabGuid ?? "") ? "never started" : !_prefabs[add.PrefabGuid].IsDone ? "still loading" : $"failed ({_prefabs[add.PrefabGuid].Status})";
                    Core.Log.Error($"[augment] {job.UniqueName}: prefab {add.PrefabGuid} not available ({why}); '{add.Name}' NOT added, the job's wash targets will be missing");
                    continue;
                }
                var go = UnityEngine.Object.Instantiate(h.Result);
                if (!string.IsNullOrEmpty(add.Name)) go.name = add.Name;
                SceneManager.MoveGameObjectToScene(go, scene);
                var (pos, euler) = Placement(add);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
                added++;
                Core.Log.Msg($"[augment] {job.UniqueName}: '{go.name}' placed at {pos} rot {euler} in {sceneName} ({go.GetComponentsInChildren(Il2CppType.Of<WashTarget>(), true).Length} wash target(s))");
            }

            Core.Log.Msg($"[augment] {job.UniqueName} on {sceneName}: hid {washHidden} wash target(s) of [{string.Join(", ", job.HideWashRoots)}] and {namedHidden} named root(s) and {containedHidden} object(s) inside [{string.Join(", ", job.HideContainedIn)}] " +
                         $"({_hidden.Count} renderers/colliders/lights off); added {added}/{job.SceneAdditions.Count} prefab(s)");
            _recheckScene = sceneName;
            _recheckAt = Time.realtimeSinceStartup + 10f;
        }

        /// <summary>Call every frame: ~10 s after Apply, re-disable anything the game switched back on, and say so.</summary>
        public static void Update()
        {
            if (_recheckAt < 0f || Time.realtimeSinceStartup < _recheckAt) return;
            _recheckAt = -1f;
            int reenabled = 0, gone = 0;
            foreach (var c in _hidden)
            {
                if (c == null) { gone++; continue; }
                if (!IsOn(c)) continue;
                reenabled++; SetOn(c, false);
            }
            Core.Log.Msg($"[augment] recheck {_recheckScene}: {_hidden.Count - gone} hidden component(s) still present, {reenabled} had been re-enabled (switched off again), {gone} destroyed");
        }

        // the volume is the targets' union AABB grown by this much (x/z sideways, y down, up: poles and roof gear)
        private const float ContainSide = 0.3f, ContainDown = 0.3f, ContainUp = 3f;

        /// <summary>
        /// Hides every renderer / collider / light that lies entirely inside the volume of a listed wash-target root,
        /// i.e. the parts of the object that are not wash targets (underside, lamps, steps, trolley pole). Ground,
        /// rails and wires run far past the object, so they are never fully inside. Logs each object it hides.
        /// </summary>
        private static Bounds? ContainVolume(PackManifest.PackJob job, List<GameObject> roots, string sceneName)
        {
            if (job.HideContainedIn.Count == 0) return null;
            var names = new HashSet<string>(job.HideContainedIn, StringComparer.Ordinal);
            bool any = false;
            var vol = new Bounds();
            foreach (var root in roots)
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<WashTarget>(), true))
                {
                    var wt = c?.TryCast<WashTarget>();
                    var owner = wt?.m_gameJobWashTargetRoot;
                    var mr = wt?.m_meshRenderer;
                    if (owner == null || mr == null || !names.Contains(owner.gameObject.name)) continue;
                    if (!any) { vol = mr.bounds; any = true; } else vol.Encapsulate(mr.bounds);
                }
            if (!any) { Core.Log.Warning($"[augment] {sceneName}: no wash targets under [{string.Join(", ", names)}]; nothing to clear"); return null; }
            var min = vol.min - new Vector3(ContainSide, ContainDown, ContainSide);
            var max = vol.max + new Vector3(ContainSide, ContainUp, ContainSide);
            vol.SetMinMax(min, max);
            return vol;
        }

        private static int HideContained(Bounds vol, PackManifest.PackJob job, List<GameObject> roots, string sceneName)
        {
            bool Inside(Bounds b) => vol.Contains(b.min) && vol.Contains(b.max);

            var hiddenGos = new HashSet<IntPtr>();
            var paths = new List<string>();
            void Take(Component c)
            {
                if (c == null || !IsOn(c)) return;
                Off(c);
                var go = c.gameObject;
                if (hiddenGos.Add(go.Pointer)) paths.Add(PathOf(go.transform));
            }
            // ambience that merely passes through the volume stays: anything on or under a particle system / VFX graph
            // (renderers, and colliders such as the falling leaves' killbox), and trigger colliders
            foreach (var root in roots)
            {
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) { var r = c.TryCast<Renderer>(); if (r != null && Inside(r.bounds) && !UnderEffect(r.transform)) Take(r); }
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) { var col = c.TryCast<Collider>(); if (col != null && col.enabled && !col.isTrigger && Inside(col.bounds) && !UnderEffect(col.transform)) Take(col); }
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) { var l = c.TryCast<Light>(); if (l != null && vol.Contains(l.transform.position)) Take(l); }
            }
            Core.Log.Msg($"[augment] {sceneName}: volume of [{string.Join(", ", job.HideContainedIn)}] = {vol.min}..{vol.max}; newly hidden inside it ({paths.Count}): {string.Join(", ", paths)}");
            return paths.Count;
        }

        private static bool UnderEffect(Transform t)
        {
            for (; t != null; t = t.parent)
                foreach (var c in t.GetComponents(Il2CppType.Of<Component>()))
                {
                    var n = c == null ? null : c.GetIl2CppType().Name;
                    if (n == "ParticleSystem" || n == "VisualEffect") return true;
                }
            return false;
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Hide(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) Off(c);
            foreach (var c in go.GetComponentsInChildren(Il2CppType.Of<Collider>(), true)) Off(c);
            foreach (var c in go.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) Off(c);
        }

        private static void Off(Component c)
        {
            if (c == null || !IsOn(c)) return;
            var light = c.TryCast<Light>();
            if (light != null && light.type == LightType.Directional) return;   // never the level's sun
            SetOn(c, false);
            _hidden.Add(c);
        }

        private static bool IsOn(Component c)
        {
            var r = c.TryCast<Renderer>(); if (r != null) return r.enabled;
            var col = c.TryCast<Collider>(); if (col != null) return col.enabled;
            var l = c.TryCast<Light>(); if (l != null) return l.enabled;
            return false;
        }

        private static void SetOn(Component c, bool on)
        {
            var r = c.TryCast<Renderer>(); if (r != null) { r.enabled = on; return; }
            var col = c.TryCast<Collider>(); if (col != null) { col.enabled = on; return; }
            var l = c.TryCast<Light>(); if (l != null) l.enabled = on;
        }

        private static bool NameMatches(string name, string pattern) =>
            pattern.EndsWith("*", StringComparison.Ordinal)
                ? name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)
                : string.Equals(name, pattern, StringComparison.Ordinal);

        /// <summary>pack.json placement, or the SceneAdditionOverride preference ("x,y,z[,yaw]") for tuning without a rebuild.</summary>
        private static (Vector3, Vector3) Placement(PackManifest.SceneAddition add)
        {
            var p = add.Position ?? new float[3];
            var e = add.EulerAngles ?? new float[3];
            var pos = new Vector3(p.ElementAtOrDefault(0), p.ElementAtOrDefault(1), p.ElementAtOrDefault(2));
            var euler = new Vector3(e.ElementAtOrDefault(0), e.ElementAtOrDefault(1), e.ElementAtOrDefault(2));
            var o = Core.SceneAdditionOverride?.Value;
            if (string.IsNullOrWhiteSpace(o)) return (pos, euler);
            var v = o.Split(',').Select(s => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? (float?)f : null).ToArray();
            if (v.Length < 3 || v.Take(3).Any(x => x == null)) { Core.Log.Warning($"[augment] SceneAdditionOverride '{o}' is not 'x,y,z[,yaw]'; ignored"); return (pos, euler); }
            pos = new Vector3(v[0].Value, v[1].Value, v[2].Value);
            if (v.Length > 3 && v[3] != null) euler = new Vector3(euler.x, v[3].Value, euler.z);
            Core.Log.Msg($"[augment] SceneAdditionOverride in effect: {pos} yaw {euler.y}");
            return (pos, euler);
        }
    }
}
