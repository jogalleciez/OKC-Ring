using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PWS2LevelLoader.Packs
{
    /// <summary>
    /// pack.json: what a content pack folder (Mods/PWS2LevelLoader/packs/&lt;id&gt;/) declares about itself.
    /// Produced by PWS2LevelKit's build tooling, read by the loader at boot.
    /// </summary>
    public sealed class PackManifest
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("gameVersion")] public string GameVersion { get; set; }
        [JsonPropertyName("catalog")] public string Catalog { get; set; } = "catalog.bin";
        /// <summary>Addressables GUID of the pack's JobPackManifest asset (null for spike packs without jobs).</summary>
        [JsonPropertyName("jobPackManifestGuid")] public string JobPackManifestGuid { get; set; }
        [JsonPropertyName("jobs")] public List<PackJob> Jobs { get; set; } = new List<PackJob>();
        /// <summary>Extra string-table entries (key -> English text) the pack needs, e.g. wash task names.</summary>
        [JsonPropertyName("strings")] public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();

        [JsonIgnore] public string Folder { get; set; }
        [JsonIgnore] public string CatalogPath => Path.Combine(Folder, Catalog ?? "catalog.bin");

        public sealed class PackJob
        {
            [JsonPropertyName("uniqueName")] public string UniqueName { get; set; }
            [JsonPropertyName("uniqueId")] public int UniqueId { get; set; }
            /// <summary>Scene the job loads (pack scenes get WashRenderSettings + the wash shader from the loader at load time).</summary>
            [JsonPropertyName("sceneName")] public string SceneName { get; set; }
            [JsonPropertyName("title")] public string Title { get; set; }
            [JsonPropertyName("address")] public string Address { get; set; }
            [JsonPropertyName("description")] public string Description { get; set; }
            [JsonPropertyName("freePlayName")] public string FreePlayName { get; set; }
            [JsonPropertyName("storyName")] public string StoryName { get; set; }
            [JsonPropertyName("thumbnailSelectedGuid")] public string ThumbnailSelectedGuid { get; set; }
            [JsonPropertyName("thumbnailDeselectedGuid")] public string ThumbnailDeselectedGuid { get; set; }

            // A job on one of the game's own scenes (see SceneAugmenter): applied only when this job is the one loading.
            /// <summary>Prefabs from this pack instantiated into the job's scene before the game initialises its wash data.</summary>
            [JsonPropertyName("sceneAdditions")] public List<SceneAddition> SceneAdditions { get; set; } = new List<SceneAddition>();
            /// <summary>GameJobWashTargetRoot names whose wash targets (wherever they sit in the hierarchy) are hidden.</summary>
            [JsonPropertyName("hideWashRoots")] public List<string> HideWashRoots { get; set; } = new List<string>();
            /// <summary>Scene root object names to hide; a trailing '*' matches a prefix.</summary>
            [JsonPropertyName("hideObjects")] public List<string> HideObjects { get; set; } = new List<string>();
            /// <summary>
            /// GameJobWashTargetRoot names whose volume (union of their targets' bounds) is cleared: every renderer,
            /// collider and light lying entirely inside it is hidden too (the non-washable parts of a vehicle).
            /// </summary>
            [JsonPropertyName("hideContainedIn")] public List<string> HideContainedIn { get; set; } = new List<string>();

            [JsonIgnore] public bool AugmentsScene => SceneAdditions.Count > 0 || HideWashRoots.Count > 0 || HideObjects.Count > 0 || HideContainedIn.Count > 0;

            /// <summary>Table entries the game derives from the unique name (see docs/phase0-findings.md §3).</summary>
            public IEnumerable<KeyValuePair<string, string>> DerivedStrings()
            {
                var n = (UniqueName ?? "").ToUpperInvariant();
                var title = Title ?? UniqueName;
                yield return new KeyValuePair<string, string>($"SCRIPT_CAREER_{n}_TITLE", title);
                yield return new KeyValuePair<string, string>($"SCRIPT_CAREER_{n}_TITLE_FREEPLAY", FreePlayName ?? $"{title} (Free Play)");
                yield return new KeyValuePair<string, string>($"SCRIPT_CAREER_{n}_DESCRIPTION", Description ?? title);
                yield return new KeyValuePair<string, string>($"SCRIPT_CAREER_{n}_ADDRESS", Address ?? "");
                yield return new KeyValuePair<string, string>($"HOMEBASE_{n}_FREEPLAY", FreePlayName ?? $"{title} (Free Play)");
                yield return new KeyValuePair<string, string>($"HOMEBASE_{n}_STORY", StoryName ?? title);
                yield return new KeyValuePair<string, string>($"STAGE_{n}_", title);
            }
        }

        public sealed class SceneAddition
        {
            /// <summary>Addressables GUID of the prefab in this pack's catalog.</summary>
            [JsonPropertyName("prefabGuid")] public string PrefabGuid { get; set; }
            /// <summary>Name for the instance (drops "(Clone)"; match the name the ids were hashed with).</summary>
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("position")] public float[] Position { get; set; }
            [JsonPropertyName("eulerAngles")] public float[] EulerAngles { get; set; }
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static PackManifest Load(string packJsonPath)
        {
            var m = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(packJsonPath), Options) ?? throw new InvalidDataException("empty pack.json");
            m.Folder = Path.GetDirectoryName(packJsonPath);
            if (string.IsNullOrWhiteSpace(m.Id)) m.Id = Path.GetFileName(m.Folder);
            return m;
        }
    }
}
