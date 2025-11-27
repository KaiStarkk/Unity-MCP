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

namespace com.IvanMurzak.Unity.MCP.Editor
{
    [InitializeOnLoad]
    public static partial class Startup
    {
        // Session state key to track domain reload state (survives domain reload)
        private const string DomainReloadPendingKey = "UnityMcp_DomainReloadPending";

        static Startup()
        {
            // Check if we're in a domain reload scenario - if so, skip immediate connection
            // The OnAfterAssemblyReload handler will handle delayed reconnection
            bool isDomainReloadPending = SessionState.GetBool(DomainReloadPendingKey, false);

            UnityMcpPlugin.Instance.BuildMcpPluginIfNeeded();

            // Only connect immediately if this is NOT a domain reload scenario
            // During domain reload, OnAfterAssemblyReload will handle reconnection with proper delay
            if (!EnvironmentUtils.IsCi() && !isDomainReloadPending)
                UnityMcpPlugin.ConnectIfNeeded();

            Server.DownloadServerBinaryIfNeeded();

            if (Application.dataPath.Contains(" "))
                Debug.LogError("The project path contains spaces, which may cause issues during usage of AI Game Developer. Please consider the move the project to a folder without spaces.");

            SubscribeOnEditorEvents();

            // Initialize sub-systems
            LogUtils.EnsureSubscribed(); // log collector
            API.Tool_TestRunner.Init(); // test runner
        }
    }
}
