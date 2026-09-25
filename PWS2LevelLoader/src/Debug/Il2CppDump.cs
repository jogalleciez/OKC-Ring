using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// Reflective dumper for IL2CPP proxy objects. Walks the public instance properties that the
    /// interop generator exposes for game types (namespace Il2CppFuturLab*), recursing into nested
    /// game objects/arrays/lists up to a depth, and summarising everything else (Unity objects by
    /// name, AssetReferences by GUID, LocalizedStrings by table/key). Used by the debug keys only.
    /// </summary>
    internal sealed class Il2CppDump
    {
        private readonly TextWriter _w;
        private readonly int _maxDepth;
        private readonly int _maxElems;
        private readonly HashSet<IntPtr> _visited = new HashSet<IntPtr>();

        /// <summary>Type names (short) that are only summarised unless they are the root of a Dump call.</summary>
        public HashSet<string> ShallowTypes { get; } = new HashSet<string>(StringComparer.Ordinal);

        public Il2CppDump(TextWriter w, int maxDepth = 6, int maxElems = 64)
        {
            _w = w; _maxDepth = maxDepth; _maxElems = maxElems;
        }

        public void Dump(string label, object value, bool expandRoot = true)
        {
            _visited.Clear();
            Write(label, value, 0, "", expandRoot);
        }

        // ------------------------------------------------------------------ core

        private void Write(string label, object v, int depth, string indent, bool forceExpand = false)
        {
            string summary;
            try { summary = Summarize(v); }
            catch (Exception ex) { summary = $"<summary threw {ex.GetType().Name}: {ex.Message}>"; }
            _w.WriteLine($"{indent}{label} = {summary}");

            if (v == null || depth >= _maxDepth) return;

            try
            {
                if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return;

                if (v is Il2CppArrayBase arr)
                {
                    WriteElements((IEnumerable)arr, arr.Length, depth, indent);
                    return;
                }
                var t = v.GetType();
                if (IsIl2CppList(t))
                {
                    int count = (int)t.GetProperty("Count").GetValue(v);
                    var getItem = t.GetMethod("get_Item", new[] { typeof(int) });
                    var items = new List<object>();
                    for (int i = 0; i < Math.Min(count, _maxElems); i++) items.Add(getItem.Invoke(v, new object[] { i }));
                    WriteElements(items, count, depth, indent);
                    return;
                }
                if (v is Il2CppObjectBase il2)
                {
                    if (!forceExpand && !IsGameType(t)) return;
                    if (!forceExpand && ShallowTypes.Contains(t.Name)) return;
                    if (!_visited.Add(il2.Pointer)) { _w.WriteLine($"{indent}  (already dumped above)"); return; }
                    WriteMembers(v, t, depth, indent + "  ");
                }
            }
            catch (Exception ex)
            {
                _w.WriteLine($"{indent}  <expand threw {ex.GetType().Name}: {ex.Message}>");
            }
        }

        private void WriteElements(IEnumerable items, int total, int depth, string indent)
        {
            int i = 0;
            foreach (var item in items)
            {
                if (i >= _maxElems) { _w.WriteLine($"{indent}  ... {total - i} more"); break; }
                Write($"[{i}]", item, depth + 1, indent + "  ");
                i++;
            }
        }

        private void WriteMembers(object v, Type t, int depth, string indent)
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                if (!IsGameType(p.DeclaringType)) continue;
                if (p.PropertyType == typeof(IntPtr)) continue;
                if (IsSkippedType(p.PropertyType)) { _w.WriteLine($"{indent}{p.Name} : <skipped {Short(p.PropertyType)}>"); continue; }

                object val;
                try { val = p.GetValue(v); }
                catch (TargetInvocationException tie) { _w.WriteLine($"{indent}{p.Name} = <threw {tie.InnerException?.GetType().Name}: {tie.InnerException?.Message}>"); continue; }
                catch (Exception ex) { _w.WriteLine($"{indent}{p.Name} = <threw {ex.GetType().Name}>"); continue; }

                Write(p.Name, val, depth + 1, indent);
            }
        }

        // ------------------------------------------------------------------ summaries

        public static string Summarize(object v)
        {
            if (v == null) return "null";
            if (v is string s) return $"\"{s}\"";
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is decimal) return v.ToString();

            if (v is Il2CppSystem.Type il2Type) return $"Type {il2Type.FullName}";

            if (v is AssetReference ar)
            {
                string key = null;
                try { key = ar.RuntimeKey?.ToString(); } catch { }
                string sub = null;
                try { sub = ar.SubObjectName; } catch { }
                return $"{Short(t)} guid={ar.AssetGUID} key={key} sub={sub}";
            }
            if (v is LocalizedReference lr)
            {
                string table = null, key = null, keyId = null, text = null;
                try { var tr = lr.TableReference; table = tr.TableCollectionName; } catch { }
                try { var er = lr.TableEntryReference; key = er.Key; keyId = er.KeyId.ToString(); } catch { }
                if (v is LocalizedString ls && !string.IsNullOrEmpty(table))
                {
                    try { text = ls.GetLocalizedString(); } catch (Exception ex) { text = $"<{ex.GetType().Name}>"; }
                }
                return $"{Short(t)} table={table} key={key} keyId={keyId} text={(text == null ? "null" : "\"" + text + "\"")}";
            }
            if (v is UnityEngine.Object uo)
            {
                string name;
                try { name = uo == null ? "<destroyed/null>" : uo.name; } catch { name = "<name threw>"; }
                string il2Name = null;
                try { il2Name = uo.GetIl2CppType().FullName; } catch { }
                return $"{Short(t)} '{name}'" + (il2Name != null && il2Name != t.FullName.Replace("Il2Cpp", "") ? $" (il2cpp {il2Name})" : "");
            }
            if (v is Il2CppArrayBase arr) return $"{Short(t)}[{arr.Length}]";
            if (IsIl2CppList(t) || IsIl2CppDictionary(t))
            {
                int count = -1;
                try { count = (int)t.GetProperty("Count").GetValue(v); } catch { }
                return $"{Short(t)} count={count}";
            }
            if (v is Il2CppObjectBase)
            {
                string str = null;
                if (!IsGameType(t)) { try { str = ((Il2CppSystem.Object)v).ToString(); } catch { } }
                return Short(t) + (str != null && str != t.FullName.Replace("Il2Cpp", "") ? $" \"{str}\"" : "");
            }
            if (t.IsValueType)
            {
                if (v is Vector2 || v is Vector3 || v is Vector4 || v is Quaternion || v is Color || v is Color32
                    || v is Rect || v is RectInt || v is Bounds || v is Vector2Int || v is Vector3Int)
                    return v.ToString();
                return StructSummary(v, t);
            }
            return v.ToString();
        }

        private static string StructSummary(object v, Type t)
        {
            var parts = new List<string>();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(IntPtr)) continue;
                object fv; try { fv = f.GetValue(v); } catch { continue; }
                parts.Add($"{f.Name}={Inline(fv)}");
            }
            if (parts.Count == 0)
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0 || p.PropertyType == typeof(IntPtr)) continue;
                    if (IsSkippedType(p.PropertyType)) continue;
                    object pv; try { pv = p.GetValue(v); } catch { continue; }
                    parts.Add($"{p.Name}={Inline(pv)}");
                    if (parts.Count >= 12) break;
                }
            }
            return $"{Short(t)} {{ {string.Join(", ", parts)} }}";
        }

        private static string Inline(object v)
        {
            if (v == null) return "null";
            if (v is string s) return $"\"{s}\"";
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum) return v.ToString();
            if (v is UnityEngine.Object uo) { try { return $"'{(uo == null ? "null" : uo.name)}'"; } catch { return "<obj>"; } }
            if (v is Il2CppObjectBase) return Short(t);
            return v.ToString();
        }

        // ------------------------------------------------------------------ type helpers

        public static bool IsGameType(Type t) => t != null && t.FullName != null && t.FullName.StartsWith("Il2CppFuturLab", StringComparison.Ordinal);

        private const string ListPrefix = "Il2CppSystem.Collections.Generic.List`1";
        private static readonly string[] DictPrefixes =
        {
            "Il2CppSystem.Collections.Generic.Dictionary`2",
            "Il2CppSystem.Collections.Generic.HashSet`1",
            "Il2CppSystem.Collections.Generic.SortedDictionary`2",
        };

        private static bool IsIl2CppList(Type t) => t.FullName != null && t.FullName.StartsWith(ListPrefix, StringComparison.Ordinal);

        private static bool IsIl2CppDictionary(Type t)
        {
            if (t.FullName == null) return false;
            foreach (var p in DictPrefixes) if (t.FullName.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Outer type names (generic arity stripped) whose values are never expanded or read.</summary>
        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Action", "Func", "Delegate", "MulticastDelegate", "Task", "ValueTask", "Awaitable", "Awaiter", "TaskAwaiter",
            "CancellationToken", "Span", "ReadOnlySpan", "IEnumerable", "IEnumerator", "IReadOnlyList", "IReadOnlyCollection",
            "IReadOnlyDictionary", "AsyncOperationHandle", "IList", "ICollection", "IDictionary", "UnityEvent", "Coroutine",
            "NativeArray", "IObjectResolver", "IProgress", "CallbackArray", "IntPtr",
        };

        private static bool IsSkippedType(Type t)
        {
            var name = t.IsGenericType ? t.GetGenericTypeDefinition().Name : t.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            if (SkipNames.Contains(name)) return true;
            if (typeof(Delegate).IsAssignableFrom(t)) return true;
            var ns = t.Namespace ?? "";
            return ns.StartsWith("System.Threading", StringComparison.Ordinal) || ns.StartsWith("Il2CppSystem.Threading", StringComparison.Ordinal);
        }

        public static string Short(Type t)
        {
            if (t == null) return "?";
            var n = t.Name;
            if (t.IsGenericType)
            {
                var args = t.GetGenericArguments();
                var names = new string[args.Length];
                for (int i = 0; i < args.Length; i++) names[i] = Short(args[i]);
                n = n.Split('`')[0] + "<" + string.Join(",", names) + ">";
            }
            return n;
        }
    }
}
