using System;
using Il2CppFuturLab.PW2;
using UnityEngine;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// Polls the main viewport's game-state machine every frame and logs every state change with a timestamp.
    /// No Harmony involved: the hub reload loop after job registration leaves nothing in the game's own logs,
    /// so this is the cheapest way to see which state sends the game back to BootScene.
    /// </summary>
    internal static class StateTracer
    {
        private static IntPtr _lastState = IntPtr.Zero, _lastRoot = IntPtr.Zero;
        private static float _lastChangeAt;
        private static int _failures;

        public static void Update()
        {
            if (_failures > 20) return;
            try
            {
                var sm = PwsScreenManager.Instance;
                if (sm == null) return;
                var vp = sm.MainViewport;
                if (vp == null) return;
                var state = vp.CurrentState;
                var root = vp.CurrentRootState;
                var sp = state == null ? IntPtr.Zero : state.Pointer;
                var rp = root == null ? IntPtr.Zero : root.Pointer;
                if (sp != _lastState || rp != _lastRoot)
                {
                    var now = Time.realtimeSinceStartup;
                    Core.Log.Msg($"[state] t={now:0.00}s (+{now - _lastChangeAt:0.00}s) state={Name(state)} root={Name(root)}");
                    _lastChangeAt = now; _lastState = sp; _lastRoot = rp;
                }
                WatchLoadingTask(state);
            }
            catch (Exception ex)
            {
                if (++_failures <= 3) Core.Log.Warning($"StateTracer: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static IntPtr _lastTask = IntPtr.Zero;
        private static bool _taskReported;
        private static string _lastSegment;

        /// <summary>
        /// A loading state exposes its Load() task; if the game swallows the exception and shows a generic
        /// popup, the task's AggregateException (with the IL2CPP stack) is still readable here.
        /// </summary>
        private static void WatchLoadingTask(PwsGameStateBase state)
        {
            var loading = state?.TryCast<LoadingStateBase>();
            if (loading == null) return;
            var task = loading.Task;
            if (task != null)
            {
                if (task.Pointer != _lastTask)
                {
                    _lastTask = task.Pointer; _taskReported = false;
                    Core.Log.Msg($"[state] {Name(state)} loading task started");
                }
                if (!_taskReported && task.IsCompleted)
                {
                    _taskReported = true;
                    Core.Log.Msg($"[state] {Name(state)} loading task completed: faulted={task.IsFaulted} canceled={task.IsCanceled}");
                    if (task.IsFaulted)
                    {
                        string text;
                        try { text = task.Exception?.ToString() ?? "null"; } catch (Exception ex) { text = $"<{ex.GetType().Name}: {ex.Message}>"; }
                        Core.Log.Error($"[state] loading task exception:\n{text}");
                    }
                }
            }
            try
            {
                var segs = loading.m_progressSegments;
                if (segs == null || segs.Count == 0) return;
                var seg = segs[segs.Count - 1];
                var text = $"{segs.Count}:{seg?.InfoText}";
                if (text != _lastSegment) { _lastSegment = text; Core.Log.Msg($"[state] progress segment {text} progress={loading.Progress:0.00}"); }
            }
            catch { /* segments are optional diagnostics */ }
        }

        private static string Name(PwsGameStateBase s)
        {
            if (s == null) return "null";
            try { return s.GetIl2CppType().Name; } catch { return "?"; }
        }
    }
}
