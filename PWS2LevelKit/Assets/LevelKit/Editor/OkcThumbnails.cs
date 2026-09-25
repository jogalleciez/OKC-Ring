// Renders the OKC level's job-menu thumbnails, preview, chat icon, loading screen and timelapse image from the open
// scene, in the formats the game's own jobs use (GAM_PW2_HW_Billboard sprites dump, 2026-09-23):
//   Thumbnail A (selected)    765 x 328, a slanted two-step tab with rounded right corners
//   Thumbnail B (deselected)  same shape, the image mapped onto a navy -> light-blue duotone
//   Preview                   1936 x 1184, rounded corners
//   Chat icon                 140 x 140, a disc with a white rim
//   Loading screen            1920 x 1080 (the game's are 3840 x 2160; UI scales), navy halftone vignette
//   Timelapse static          1782 x 976, plain
// The shapes and the tint are re-created procedurally; no game image is copied. Rendering needs a GPU, so the batch
// run that calls this must not pass -nographics.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PWS2LevelKit.Editor
{
    public static class OkcThumbnails
    {
        public const string ThumbA = "Thumbnail_OKC_A", ThumbB = "Thumbnail_OKC_B", Preview = "Preview_OKC",
                            Chat = "ChatIcon_OKC", Loading = "Loading_OKC", Timelapse = "TimelapseEnd_OKC";

        /// <summary>Renders every image into &lt;pack&gt;/UI, imports them as sprites and returns name -> asset GUID.</summary>
        public static Dictionary<string, string> Render(string packFolder, Vector3 south)
        {
            var ui = $"{packFolder}/UI";
            if (!AssetDatabase.IsValidFolder(ui)) AssetDatabase.CreateFolder(packFolder, "UI");
            var east = Vector3.Cross(Vector3.up, south).normalized * -1f; // right-hand side when looking from the street
            var camGo = new GameObject("OKC_ThumbnailCamera");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f;
                cam.clearFlags = CameraClearFlags.Skybox;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.renderShadows = true;

                // hero: from the street corner, ring right of centre like the game's thumbnails
                Pose(cam, south * 15f + east * 4f + Vector3.up * 2.4f, new Vector3(0, 1.5f, 0) - east * 2.2f, 30f);
                var hero = Shot(cam, 765, 328);
                var a = (Color32[])hero.Clone();
                ApplyMask(a, 765, 328, TabAlpha);
                var b = Duotone(hero); ApplyMask(b, 765, 328, TabAlpha);
                var guids = new Dictionary<string, string>
                {
                    [ThumbA] = Save(ui, ThumbA, a, 765, 328, compress: false),
                    [ThumbB] = Save(ui, ThumbB, b, 765, 328, compress: false),
                };

                Pose(cam, south * 12.5f + east * 2f + Vector3.up * 1.7f, new Vector3(0, 1.7f, 0) - east * 1f, 42f);
                var prev = Shot(cam, 1936, 1184);
                ApplyMask(prev, 1936, 1184, (x, y, w, h) => RoundRect(x, y, w, h, 14f));
                guids[Preview] = Save(ui, Preview, prev, 1936, 1184, compress: true);

                Pose(cam, south * 11f + Vector3.up * 2.2f, new Vector3(0, 1.2f, 0), 40f);
                var chat = Shot(cam, 140, 140);
                ChatDisc(chat, 140);
                guids[Chat] = Save(ui, Chat, chat, 140, 140, compress: false);

                Pose(cam, south * 8.5f + east * 6f + Vector3.up * 1.6f, new Vector3(0, 1.8f, 0) - east * 3f, 50f);
                var load = Shot(cam, 1920, 1080);
                Halftone(load, 1920, 1080);
                guids[Loading] = Save(ui, Loading, load, 1920, 1080, compress: true);

                Pose(cam, south * 14f - east * 6f + Vector3.up * 9f, new Vector3(0, 0.8f, 0), 45f);
                guids[Timelapse] = Save(ui, Timelapse, Shot(cam, 1782, 976), 1782, 976, compress: true);
                return guids;
            }
            finally { UnityEngine.Object.DestroyImmediate(camGo); }
        }

        static void Pose(Camera cam, Vector3 pos, Vector3 target, float vfov)
        {
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = vfov;
        }

        /// <summary>Renders at 2x and box-filters down; returns sRGB pixels, row 0 = bottom.</summary>
        static Color32[] Shot(Camera cam, int w, int h)
        {
            var big = new RenderTexture(w * 2, h * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                cam.aspect = (float)w / h;
                cam.targetTexture = big;
                cam.Render();
                cam.targetTexture = null;
                Graphics.Blit(big, small);
                RenderTexture.active = small;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                var px = tex.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tex);
                for (int i = 0; i < px.Length; i++) px[i].a = 255;
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                big.Release(); small.Release();
                UnityEngine.Object.DestroyImmediate(big); UnityEngine.Object.DestroyImmediate(small);
            }
        }

        // ---------------------------------------------------------------- shapes (x, y from the top-left, in pixels)

        /// <summary>The game's thumbnail tab: two stacked bands whose left edges slant down-left, right corners rounded.</summary>
        static float TabAlpha(float x, float y, float w, float h)
        {
            float sx = w / 765f, sy = h / 328f;
            float yy = y / sy;
            float left = (yy < 168f ? 97f - 0.494f * yy : 80f - 0.503f * (yy - 168f)) * sx;
            float a = Mathf.Clamp01(x - left + 0.5f);
            return a * RoundRect(x, y, w, h, 10f, roundLeft: false);
        }

        static float RoundRect(float x, float y, float w, float h, float r, bool roundLeft = true)
        {
            float cx = Mathf.Clamp(x, roundLeft ? r : 0f, w - r), cy = Mathf.Clamp(y, r, h - r);
            if (!roundLeft && x < w / 2) return 1f;
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01(r - d + 0.5f);
        }

        static void ApplyMask(Color32[] px, int w, int h, Func<float, float, float, float, float> alpha)
        {
            for (int row = 0; row < h; row++)
                for (int x = 0; x < w; x++)
                {
                    float y = h - 1 - row + 0.5f;
                    px[row * w + x].a = (byte)Mathf.RoundToInt(255f * alpha(x + 0.5f, y, w, h));
                }
        }

        /// <summary>Luminance onto the deselected-thumbnail blue ramp (measured from the game's A/B pair).</summary>
        static Color32[] Duotone(Color32[] src)
        {
            var stops = new (float t, Color c)[]
            {
                (0.00f, new Color32(2, 20, 64, 255)), (0.45f, new Color32(32, 68, 160, 255)),
                (0.75f, new Color32(100, 138, 214, 255)), (1.00f, new Color32(176, 198, 240, 255)),
            };
            var dst = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var p = src[i];
                float l = (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) / 255f;
                int k = 0;
                while (k < stops.Length - 2 && l > stops[k + 1].t) k++;
                float f = Mathf.InverseLerp(stops[k].t, stops[k + 1].t, l);
                var c = Color.Lerp(stops[k].c, stops[k + 1].c, f);
                dst[i] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), p.a);
            }
            return dst;
        }

        static void ChatDisc(Color32[] px, int s)
        {
            float c = s / 2f, rOut = c - 0.5f, rim = 5f;
            for (int row = 0; row < s; row++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x + 0.5f - c, dy = row + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    ref var p = ref px[row * s + x];
                    float inner = Mathf.Clamp01(rOut - rim - d + 0.5f);
                    p = Color32.Lerp(new Color32(255, 255, 255, 255), p, inner);
                    p.a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(rOut - d + 0.5f));
                }
        }

        /// <summary>Navy halftone dots growing towards the edges, over a soft navy vignette.</summary>
        static void Halftone(Color32[] px, int w, int h)
        {
            var navy = new Color(0.05f, 0.11f, 0.27f);
            const float cell = 22f;
            float cs = Mathf.Cos(Mathf.PI / 4), sn = Mathf.Sin(Mathf.PI / 4);
            for (int row = 0; row < h; row++)
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f, ny = (row + 0.5f) / h * 2f - 1f;
                    float edge = Mathf.Clamp01((Mathf.Sqrt(nx * nx * 1.1f + ny * ny * 0.5f) - 0.55f) / 0.6f);
                    float u = (x * cs - row * sn) / cell, v = (x * sn + row * cs) / cell;
                    float du = u - Mathf.Floor(u) - 0.5f, dv = v - Mathf.Floor(v) - 0.5f;
                    float d = Mathf.Sqrt(du * du + dv * dv) * cell;
                    float radius = edge * cell * 0.62f;
                    float dot = Mathf.Clamp01(radius - d + 0.5f);
                    float k = Mathf.Max(dot * 0.92f, edge * edge * 0.35f);
                    ref var p = ref px[row * w + x];
                    var c = Color.Lerp(p, navy, k);
                    p = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
                }
        }

        static string Save(string folder, string name, Color32[] px, int w, int h, bool compress)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            var path = $"{folder}/{name}.png";
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096;
            ti.textureCompression = compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
            Debug.Log($"OkcThumbnails: {path} {w}x{h}");
            return AssetDatabase.AssetPathToGUID(path);
        }
    }
}
