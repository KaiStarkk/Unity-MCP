/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/Unity-MCP)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using com.IvanMurzak.Unity.MCP.Runtime.Utils;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;

namespace com.IvanMurzak.Unity.MCP.Editor
{
    public static partial class Startup
    {
        // Delay before reconnecting after domain reload (in milliseconds)
        // This gives the server time to clear stale invocations from the previous connection
        private const int DomainReloadReconnectDelayMs = 500;

        static void SubscribeOnEditorEvents()
        {
            Application.unloading += OnApplicationUnloading;
            Application.quitting += OnApplicationQuitting;

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

            // Handle Play mode state changes to ensure reconnection after exiting Play mode
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        static void OnApplicationUnloading()
        {
            if (UnityMcpPlugin.HasInstance)
            {
                UnityMcpPlugin.Instance.LogInfo("{method} triggered", typeof(Startup), nameof(OnApplicationUnloading));
                UnityMcpPlugin.Instance.DisconnectImmediate();
            }
            else
            {
                Debug.Log($"{nameof(Startup)} {nameof(OnApplicationUnloading)} triggered: No UnityMcpPlugin instance to disconnect.");
            }
        }
        static void OnApplicationQuitting()
        {
            if (UnityMcpPlugin.HasInstance)
            {
                UnityMcpPlugin.Instance.LogInfo("{method} triggered", typeof(Startup), nameof(OnApplicationQuitting));
                UnityMcpPlugin.Instance.DisconnectImmediate();
            }
            else
            {
                Debug.Log($"{nameof(Startup)} {nameof(OnApplicationQuitting)} triggered: No UnityMcpPlugin instance to disconnect.");
            }
        }
        static void OnBeforeAssemblyReload()
        {
            // Set flag to indicate domain reload is pending - this prevents the static constructor
            // from immediately reconnecting, which would cause "Failed to bind arguments" errors
            // because SignalR handlers are not yet properly registered after domain reload
            SessionState.SetBool(DomainReloadPendingKey, true);

            if (UnityMcpPlugin.HasInstance)
            {
                UnityMcpPlugin.Instance.LogInfo("{method} triggered", typeof(Startup), nameof(OnBeforeAssemblyReload));
                UnityMcpPlugin.Instance.DisconnectImmediate();

                // Fully dispose the plugin to ensure clean state after reload
                UnityMcpPlugin.StaticDispose();
            }
            else
            {
                Debug.Log($"{nameof(Startup)} {nameof(OnBeforeAssemblyReload)} triggered: No UnityMcpPlugin instance to disconnect.");
            }
        }
        static void OnAfterAssemblyReload()
        {
            var connectionAllowed = EnvironmentUtils.IsCi() == false;

            UnityMcpPlugin.Instance.LogInfo($"{nameof(OnAfterAssemblyReload)} triggered - BuildAndStart with {nameof(connectionAllowed)}: {connectionAllowed}",
                typeof(Startup));

            UnityMcpPlugin.Instance.BuildMcpPluginIfNeeded();

            if (connectionAllowed)
            {
                // Use delayed connection to allow the server to clear stale invocations
                // and ensure SignalR handlers are fully registered before receiving messages
                EditorApplication.delayCall += () => DelayedReconnectAfterDomainReload();
            }
            else
            {
                // Clear the domain reload flag since we're not reconnecting
                SessionState.SetBool(DomainReloadPendingKey, false);
            }
        }

        static async void DelayedReconnectAfterDomainReload()
        {
            // Wait for a short delay to let any stale server invocations expire
            await Task.Delay(DomainReloadReconnectDelayMs);

            // Clear the domain reload pending flag
            SessionState.SetBool(DomainReloadPendingKey, false);

            // Skip if compilation started during the delay
            if (EditorApplication.isCompiling)
            {
                UnityMcpPlugin.Instance.LogTrace("Skipping reconnection - compilation in progress", typeof(Startup));
                return;
            }

            UnityMcpPlugin.Instance.LogInfo("Initiating delayed reconnection after domain reload", typeof(Startup));
            UnityMcpPlugin.Instance.BuildMcpPluginIfNeeded();
            UnityMcpPlugin.ConnectIfNeeded();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!UnityMcpPlugin.HasInstance)
                Debug.LogWarning($"{nameof(Startup)} {nameof(OnPlayModeStateChanged)} triggered: No UnityMcpPlugin instance available. State: {state}.");

            // Log Play mode state changes for debugging
            UnityMcpPlugin.Instance.LogInfo($"Play mode state changed: {state}", typeof(Startup));

            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    // Unity is about to exit Play mode - connection may be lost
                    // The OnBeforeReload will handle disconnection if domain reload occurs
                    UnityMcpPlugin.Instance.LogTrace($"Exiting Play mode - connection may be affected by domain reload", typeof(Startup));
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // Unity has returned to Edit mode - ensure connection is re-established
                    // if the configuration expects it to be connected
                    UnityMcpPlugin.Instance.LogTrace($"Entered Edit mode - KeepConnected: {UnityMcpPlugin.KeepConnected}, IsCi: {EnvironmentUtils.IsCi()}.",
                        typeof(Startup));

                    if (EnvironmentUtils.IsCi())
                    {
                        UnityMcpPlugin.Instance.LogTrace($"Skipping reconnection in CI environment.", typeof(Startup));
                        break;
                    }

                    UnityMcpPlugin.Instance.LogTrace($"Scheduling reconnection after Play mode exit.", typeof(Startup));

                    // Small delay to ensure Unity is fully settled in Edit mode
                    EditorApplication.delayCall += () =>
                    {
                        UnityMcpPlugin.Instance.LogTrace($"Initiating delayed reconnection after Play mode exit.", typeof(Startup));

                        UnityMcpPlugin.Instance.BuildMcpPluginIfNeeded();
                        UnityMcpPlugin.ConnectIfNeeded();
                    };

                    // No delay, immediate reconnection for the case if Unity Editor in background
                    // (has no focus)
                    UnityMcpPlugin.Instance.LogTrace($"Initiating reconnection after Play mode exit.", typeof(Startup));

                    UnityMcpPlugin.Instance.BuildMcpPluginIfNeeded();
                    UnityMcpPlugin.ConnectIfNeeded();
                    break;

                case PlayModeStateChange.ExitingEditMode:
                    UnityMcpPlugin.Instance.LogTrace($"Exiting Edit mode to enter Play mode.", typeof(Startup));
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    UnityMcpPlugin.Instance.LogTrace($"Entered Play mode.", typeof(Startup));
                    break;
            }
        }
    }
}
