using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ProjectBootstrap
{
    // Installs the Phase 0 package set (PLAN.md §6) in one UPM resolution pass, headless-safe.
    // Run WITHOUT -quit — the request completes on later editor ticks and this class exits itself:
    //   Unity.exe -batchmode -projectPath <path> -executeMethod ProjectBootstrap.PackageInstaller.Install -logFile -
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "com.unity.shadergraph",             // CharacterToon shaders (also a URP dependency; pinned directly)
            "com.unity.cinemachine",             // creator & world cameras
            "com.unity.animation.rigging",       // look-at, head/eye tracking
            "com.unity.addressables",            // on-demand part loading (Phase 4)
            "com.unity.nuget.newtonsoft-json",   // DNA serialization (Game.Characters)
        };

        // Template defaults we don't use: Visual Scripting, and the Unity Version Control plugin (we use git).
        static readonly string[] PackagesToRemove =
        {
            "com.unity.visualscripting",
            "com.unity.collab-proxy",
        };

        const double TimeoutSeconds = 600;

        static AddAndRemoveRequest _request;
        static double _deadline;

        public static void Install()
        {
            Debug.Log($"[PackageInstaller] Adding: {string.Join(", ", PackagesToAdd)}; removing: {string.Join(", ", PackagesToRemove)}");
            _request = Client.AddAndRemove(packagesToAdd: PackagesToAdd, packagesToRemove: PackagesToRemove);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (_request == null) return;

            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("[PackageInstaller] Timed out waiting for UPM.");
                    EditorApplication.Exit(2);
                }
                return;
            }

            EditorApplication.update -= Poll;

            if (_request.Status == StatusCode.Success)
            {
                var names = _request.Result.Select(p => $"{p.name}@{p.version}");
                Debug.Log($"[PackageInstaller] Resolved: {string.Join(", ", names)}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[PackageInstaller] Failed: {_request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
