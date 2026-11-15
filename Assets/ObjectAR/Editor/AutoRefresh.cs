using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ObjectAR.EditorTools
{
    /// <summary>
    /// Interactive editor only: every ~2s, checks whether the git checkout moved
    /// (HEAD / refs/heads/main / ORIG_HEAD) and calls AssetDatabase.Refresh() so that
    /// `git fetch && git reset --hard origin/main` from tools/win.sh is picked up without focusing Unity.
    /// Also runs commands written to &lt;project&gt;/ObjectAR.trigger: setup | scene | refresh.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoRefresh
    {
        const double Interval = 2.0;
        const string SigKey = "ObjectAR.AutoRefresh.GitSig";
        static readonly string Root = Path.GetDirectoryName(Application.dataPath);
        static readonly string GitDir = Path.Combine(Root, ".git");
        static readonly string TriggerPath = Path.Combine(Root, "ObjectAR.trigger");
        static double s_Next;

        static AutoRefresh()
        {
            if (Application.isBatchMode) return;
            if (string.IsNullOrEmpty(SessionState.GetString(SigKey, "")))
                SessionState.SetString(SigKey, GitSignature());
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < s_Next) return;
            s_Next = now + Interval;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                var sig = GitSignature();
                if (sig != SessionState.GetString(SigKey, ""))
                {
                    SessionState.SetString(SigKey, sig);
                    Debug.Log("[ObjectAR] AutoRefresh: git checkout changed → AssetDatabase.Refresh()");
                    AssetDatabase.Refresh();
                    return; // let import/compile settle before handling triggers
                }

                if (File.Exists(TriggerPath))
                {
                    var cmd = File.ReadAllText(TriggerPath).Trim().ToLowerInvariant();
                    File.Delete(TriggerPath);
                    RunCommand(cmd);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void RunCommand(string cmd)
        {
            Debug.Log($"[ObjectAR] AutoRefresh trigger: '{cmd}'");
            switch (cmd)
            {
                case "setup": ProjectSetup.Run(); break;
                case "scene": SceneBuilder.Build(); break;
                case "refresh": AssetDatabase.Refresh(); break;
                default: Debug.LogWarning($"[ObjectAR] Unknown trigger command '{cmd}' (expected setup|scene|refresh)"); break;
            }
        }

        static string GitSignature()
        {
            if (!Directory.Exists(GitDir)) return "nogit";
            string Stamp(string rel)
            {
                var p = Path.Combine(GitDir, rel);
                return File.Exists(p) ? File.GetLastWriteTimeUtc(p).Ticks + ":" + SafeRead(p) : "-";
            }
            return string.Join("|", Stamp("HEAD"), Stamp("refs/heads/main"), Stamp("ORIG_HEAD"));
        }

        static string SafeRead(string p)
        {
            try { return File.ReadAllText(p).Trim(); } catch { return "?"; }
        }
    }
}
