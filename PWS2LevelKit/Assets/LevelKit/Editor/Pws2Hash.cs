// PowerWash Simulator 2 identity hashes (docs/phase0-findings.md §1):
//   GameContent.m_uniqueId = Hash(m_uniqueName)
//   UniqueId.m_id          = Hash("[SceneName]Root.Child.Grandchild")
// Implemented explicitly: it is the classic .NET Framework 32-bit string hash, which Mono/Unity does not guarantee.
namespace PWS2LevelKit.Editor
{
    public static class Pws2Hash
    {
        public static int String(string s)
        {
            if (s == null) return 0;
            uint h1 = 5381, h2 = 5381;
            for (int i = 0; i < s.Length; i += 2)
            {
                h1 = ((h1 << 5) + h1) ^ s[i];
                if (i + 1 < s.Length) h2 = ((h2 << 5) + h2) ^ s[i + 1];
            }
            return unchecked((int)(h1 + h2 * 1566083941u));
        }

        /// <summary>UniqueId.FullName for a scene object: "[Scene]Root.Child" (dotted transform path).</summary>
        public static string ObjectFullName(string sceneName, UnityEngine.Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "." + path;
            return "[" + sceneName + "]" + path;
        }

        public static int ObjectId(string sceneName, UnityEngine.Transform t) => String(ObjectFullName(sceneName, t));
    }
}
