using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppFuturLab.PW2;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// Look-and-presentation recon, part of every AutoDump (and F7..F9 style runs via RunAll):
    ///   lighting    what renders the level: RenderSettings, lights, reflection probes, URP volumes, probe volumes, cameras
    ///   screenshot  ScreenCapture of the frame (written at the end of the frame)
    ///   sprites     the current job's Location sprites (thumbnails, preview, loading screen), loaded through
    ///               Addressables and polled from Update (never WaitForCompletion), logged with their size and saved as PNG
    /// </summary>
    internal static partial class Dumper
    {
        // =============================================================== lighting

        private static void DumpLighting(string pathBase)
        {
            using var w = new StreamWriter(pathBase + ".txt", false, Encoding.UTF8);
            Header(w, "LIGHTING");

            w.WriteLine("=== RENDER SETTINGS (active scene)");
            w.WriteLine($"  ambientMode={Safe(() => RenderSettings.ambientMode.ToString())} ambientIntensity={Safe(() => RenderSettings.ambientIntensity.ToString("F3"))} ambientLight={Safe(() => RenderSettings.ambientLight.ToString())}");
            w.WriteLine($"  ambientSky={Safe(() => RenderSettings.ambientSkyColor.ToString())} equator={Safe(() => RenderSettings.ambientEquatorColor.ToString())} ground={Safe(() => RenderSettings.ambientGroundColor.ToString())}");
            w.WriteLine($"  skybox={Safe(() => RenderSettings.skybox == null ? "null" : $"'{RenderSettings.skybox.name}' [{RenderSettings.skybox.shader?.name}]")} sun={Safe(() => RenderSettings.sun == null ? "null" : $"'{RenderSettings.sun.name}' in {RenderSettings.sun.gameObject.scene.name}")}");
            w.WriteLine($"  fog={Safe(() => RenderSettings.fog.ToString())} mode={Safe(() => RenderSettings.fogMode.ToString())} color={Safe(() => RenderSettings.fogColor.ToString())} density={Safe(() => RenderSettings.fogDensity.ToString("F4"))} start={Safe(() => RenderSettings.fogStartDistance.ToString("F1"))} end={Safe(() => RenderSettings.fogEndDistance.ToString("F1"))}");
            w.WriteLine($"  reflectionMode={Safe(() => RenderSettings.defaultReflectionMode.ToString())} reflectionIntensity={Safe(() => RenderSettings.reflectionIntensity.ToString("F3"))} reflectionBounces={Safe(() => RenderSettings.reflectionBounces.ToString())} customReflection={Safe(() => RenderSettings.customReflectionTexture?.name)}");
            w.WriteLine($"  renderPipeline={Safe(() => GraphicsSettings.currentRenderPipeline?.name)} quality={Safe(() => QualitySettings.names[QualitySettings.GetQualityLevel()])} lightmaps={Safe(() => LightmapSettings.lightmaps.Length.ToString())}");
            try
            {
                var urp = GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>();
                if (urp != null)
                    w.WriteLine($"  URP: shadowDistance={urp.shadowDistance} cascades={urp.shadowCascadeCount} mainLightShadowRes={urp.mainLightShadowmapResolution} hdr={urp.supportsHDR} msaa={urp.msaaSampleCount} renderScale={urp.renderScale} additionalLights={urp.additionalLightsRenderingMode} lightProbeSystem={Safe(() => urp.lightProbeSystem.ToString())}");
            }
            catch (Exception ex) { w.WriteLine($"  URP asset: <{ex.GetType().Name}>"); }

            w.WriteLine(); w.WriteLine("=== LIGHTS");
            foreach (var l in FindAll<Light>().Where(InScene))
                w.WriteLine($"  {TransformPath(l.transform)} [{l.gameObject.scene.name}] active={l.gameObject.activeInHierarchy} enabled={l.enabled} type={l.type} color={l.color} temp={(l.useColorTemperature ? l.colorTemperature.ToString("F0") + "K" : "off")} intensity={l.intensity:F3} bounce={l.bounceIntensity:F2} shadows={l.shadows} strength={l.shadowStrength:F2} range={l.range:F1} euler={l.transform.eulerAngles} mask={l.cullingMask} renderMode={l.renderMode}");

            w.WriteLine(); w.WriteLine("=== REFLECTION PROBES");
            foreach (var p in FindAll<ReflectionProbe>().Where(InScene))
                w.WriteLine($"  {TransformPath(p.transform)} [{p.gameObject.scene.name}] active={p.gameObject.activeInHierarchy} enabled={p.enabled} mode={p.mode} refresh={p.refreshMode} res={p.resolution} hdr={p.hdr} importance={p.importance} intensity={p.intensity:F2} box={p.boxProjection} size={p.size} baked={Safe(() => p.bakedTexture?.name)} texture={Safe(() => p.texture?.name)}");

            w.WriteLine(); w.WriteLine("=== URP VOLUMES");
            foreach (var v in FindAll<Volume>().Where(InScene))
            {
                w.WriteLine($"  {TransformPath(v.transform)} [{v.gameObject.scene.name}] active={v.gameObject.activeInHierarchy} enabled={v.enabled} global={v.isGlobal} priority={v.priority} weight={v.weight:F2} blend={v.blendDistance:F1} layer={LayerMask.LayerToName(v.gameObject.layer)} profile={Safe(() => v.sharedProfile?.name)}");
                var comps = Safe(() => v.sharedProfile?.components);
                if (comps == null) continue;
                for (int i = 0; i < comps.Count; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    w.WriteLine($"    - {Safe(() => c.GetIl2CppType().Name)} active={c.active}");
                    DumpVolumeParameters(w, c);
                }
            }

            w.WriteLine(); w.WriteLine("=== PROBE VOLUMES");
            foreach (var pv in FindAll<ProbeVolume>().Where(InScene))
                w.WriteLine($"  {TransformPath(pv.transform)} [{pv.gameObject.scene.name}] active={pv.gameObject.activeInHierarchy} enabled={pv.enabled} mode={Safe(() => pv.mode.ToString())} size={Safe(() => pv.size.ToString())}");

            w.WriteLine(); w.WriteLine("=== CAMERAS");
            foreach (var c in FindAll<Camera>().Where(InScene))
            {
                w.WriteLine($"  {TransformPath(c.transform)} [{c.gameObject.scene.name}] active={c.gameObject.activeInHierarchy} enabled={c.enabled} depth={c.depth} clear={c.clearFlags} bg={c.backgroundColor} fov={c.fieldOfView:F1} near={c.nearClipPlane} far={c.farClipPlane} mask={c.cullingMask} target={Safe(() => c.targetTexture?.name)} pos={c.transform.position}");
                try
                {
                    var data = c.GetComponent(Il2CppType.Of<UniversalAdditionalCameraData>())?.TryCast<UniversalAdditionalCameraData>();
                    if (data != null)
                        w.WriteLine($"    URP: renderType={data.renderType} postProcessing={data.renderPostProcessing} aa={data.antialiasing} volumeMask={data.volumeLayerMask.value} volumeTrigger={Safe(() => data.volumeTrigger?.name)} renderShadows={data.renderShadows}");
                }
                catch (Exception ex) { w.WriteLine($"    URP data: <{ex.GetType().Name}>"); }
            }

            w.WriteLine(); w.WriteLine("=== LEVEL ENVIRONMENT ROOTS (PLT_/PEV_/PFX_/PNE_)");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var n = t.name;
                        if (n.StartsWith("PLT_") || n.StartsWith("PEV_PW2_HW") || n.StartsWith("PFX_") || n.StartsWith("PNE_"))
                            w.WriteLine($"  {TransformPath(t)} [{scene.name}] activeSelf={t.gameObject.activeSelf} activeInHierarchy={t.gameObject.activeInHierarchy} children={t.childCount}");
                    }
                }
            }
        }

        private static void DumpVolumeParameters(StreamWriter w, VolumeComponent c)
        {
            try
            {
                var ps = c.parameters;
                if (ps == null) return;
                for (int i = 0; i < ps.Count; i++)
                {
                    var p = ps[i];
                    if (p == null || !p.overrideState) continue;
                    w.WriteLine($"        {Safe(() => p.GetIl2CppType().Name)} = {Safe(() => p.ToString())}");
                }
            }
            catch (Exception ex) { w.WriteLine($"        parameters: <{ex.GetType().Name}: {ex.Message}>"); }
        }

        private static bool InScene(Component c)
        {
            try { return c != null && c.gameObject != null && c.gameObject.scene.IsValid(); } catch { return false; }
        }

        private static string TransformPath(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        // =============================================================== screenshot

        // ScreenCapture.CaptureScreenshot(string) throws MissingMethodException through the interop (string -> span
        // marshalling), so grab the frame as a texture at the end of the frame and encode it ourselves.
        private static void CaptureScreenshot(string pathBase)
        {
            MelonLoader.MelonCoroutines.Start(CaptureAtEndOfFrame(pathBase + ".png"));
        }

        private static System.Collections.IEnumerator CaptureAtEndOfFrame(string file)
        {
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex).ToArray());
                Core.Log.Msg($"Screenshot written: {Path.GetFileName(file)} ({tex.width}x{tex.height})");
            }
            catch (Exception ex) { Core.Log.Error($"Screenshot failed: {ex.GetType().Name}: {ex.Message}"); }
            finally { if (tex != null) Object.Destroy(tex); }
        }

        // =============================================================== sprites (async)

        private sealed class PendingSprite
        {
            public string Label, Key, PathBase;
            public AsyncOperationHandle<Sprite> Handle;
        }

        private static readonly List<PendingSprite> _pendingSprites = new List<PendingSprite>();
        private static StreamWriter _spriteLog;
        private static float _spritesStartedAt;

        /// <summary>Starts loading the current job's Location sprites; <see cref="PollSprites"/> writes them as they complete.</summary>
        private static void StartSpriteDump(string pathBase)
        {
            if (_pendingSprites.Count > 0) { Core.Log.Warning("Sprite dump already running"); return; }
            var loaded = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) loaded.Add(SceneManager.GetSceneAt(i).name);
            GameJob job = FindAll<GameJob>().FirstOrDefault(j => { var s = Safe(() => j.Location?.SceneName); return s != null && loaded.Contains(s); });

            _spriteLog = new StreamWriter(pathBase + ".txt", false, Encoding.UTF8);
            Header(_spriteLog, "SPRITES");
            if (job == null) { _spriteLog.WriteLine("no current job (hub?)"); _spriteLog.Dispose(); _spriteLog = null; return; }
            _spriteLog.WriteLine($"job {job.name}");
            var loc = job.Location;
            var refs = new List<(string, AssetReference)>
            {
                ("SelectedThumbnail", Safe(() => loc.SelectedThumbnailImageAsset)),
                ("DeselectedThumbnail", Safe(() => loc.DeselectedThumbnailImageAsset)),
                ("Preview", Safe(() => loc.PreviewImageAsset)),
                ("Chat", Safe(() => loc.ChatImageAsset)),
                ("LoadingScreen", Safe(() => loc.LoadingScreenImageAsset)),
                ("TimelapseStaticThumbnail", Safe(() => job.m_timelapseStaticThumbnail)),
            };
            _spritesStartedAt = Time.realtimeSinceStartup;
            foreach (var (label, r) in refs)
            {
                string guid = Safe(() => r?.AssetGUID), sub = Safe(() => r?.SubObjectName);
                if (string.IsNullOrEmpty(guid)) { _spriteLog.WriteLine($"{label}: no reference"); continue; }
                try
                {
                    // a fresh reference: the game's own may already hold a handle, and loading it twice throws
                    var fresh = new AssetReferenceSprite(guid);
                    if (!string.IsNullOrEmpty(sub)) fresh.SubObjectName = sub;
                    _pendingSprites.Add(new PendingSprite { Label = label, Key = string.IsNullOrEmpty(sub) ? guid : $"{guid}[{sub}]", PathBase = $"{pathBase}_{label}", Handle = fresh.LoadAssetAsync() });
                }
                catch (Exception ex) { _spriteLog.WriteLine($"{label}: {guid} LoadAssetAsync threw {ex.GetType().Name}: {ex.Message}"); }
            }
            if (_pendingSprites.Count == 0) { _spriteLog.Dispose(); _spriteLog = null; }
        }

        /// <summary>Call every frame; finishes the sprite dump started by <see cref="StartSpriteDump"/>.</summary>
        public static void PollSprites()
        {
            if (_pendingSprites.Count == 0) return;
            bool timeout = Time.realtimeSinceStartup - _spritesStartedAt > 20f;
            for (int i = _pendingSprites.Count - 1; i >= 0; i--)
            {
                var p = _pendingSprites[i];
                if (!timeout && !p.Handle.IsDone) continue;
                _pendingSprites.RemoveAt(i);
                try
                {
                    if (!p.Handle.IsDone) { _spriteLog.WriteLine($"{p.Label}: {p.Key} TIMEOUT"); continue; }
                    if (p.Handle.Status != AsyncOperationStatus.Succeeded || p.Handle.Result == null)
                    {
                        _spriteLog.WriteLine($"{p.Label}: {p.Key} status={p.Handle.Status} ex={Safe(() => p.Handle.OperationException?.Message)}");
                        continue;
                    }
                    var s = p.Handle.Result;
                    var tex = s.texture;
                    var r = s.textureRect;
                    _spriteLog.WriteLine($"{p.Label}: {p.Key} sprite='{s.name}' rect={s.rect} textureRect={r} texture='{tex?.name}' {tex?.width}x{tex?.height} format={tex?.format} ppu={s.pixelsPerUnit} pivot={s.pivot} border={s.border}");
                    if (tex != null)
                    {
                        File.WriteAllBytes(p.PathBase + ".png", ReadbackPng(tex, r, srgb: true));
                        _spriteLog.WriteLine($"    -> {Path.GetFileName(p.PathBase)}.png");
                    }
                }
                catch (Exception ex) { _spriteLog.WriteLine($"{p.Label}: {p.Key} readback failed {ex.GetType().Name}: {ex.Message}"); }
            }
            if (_pendingSprites.Count == 0)
            {
                _spriteLog.Dispose(); _spriteLog = null;
                Core.Log.Msg("Sprite dump complete");
            }
        }
    }
}
