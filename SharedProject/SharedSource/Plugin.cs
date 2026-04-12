// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0079
#pragma warning disable IDE0290

using Barotrauma;
using HarmonyLib;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Barotrauma.LuaCs;

[assembly: IgnoresAccessChecksTo("Barotrauma")]
[assembly: IgnoresAccessChecksTo("DedicatedServer")]
[assembly: IgnoresAccessChecksTo("BarotraumaCore")]

namespace WITG
{
    public partial class Plugin : IAssemblyPlugin
    {
        private Harmony? harmony;

        public void Initialize()
        {
            harmony = new Harmony("com.retype15.witg");
            harmony.PatchAll();

#if CLIENT
            InitClient();
#elif SERVER
            InitServer();
#endif
            WITGLogger.Log($"[WITG] Shared: Initialized.");
        }

        public void OnLoadCompleted() { }
        public void PreInitPatching() { }

        public void Dispose()
        {
            harmony?.UnpatchSelf();
#if CLIENT
            DisposeClient();
#elif SERVER
            DisposeServer();
#endif
            GC.SuppressFinalize(this);
            WITGLogger.Log($"[WITG] Disposed.");
        }
    }

    public static class TextSOS
    {
        public static LocalizedString Get(string key, string fallback = "")
        {
            var text = TextManager.Get(key);

            if (!string.IsNullOrEmpty(fallback))
            {
#if DEBUG
                return text.Fallback("[NT]" + fallback); // NT=NOT-TRANSLATED
#else
                return text.Fallback(fallback);
#endif
            }
            return text;
        }
    }

    public static class WITGLogger
    {
        [Conditional("DEBUG")]
        public static void Log(string message) => LuaCsLogger.LogMessage(message);

        [Conditional("DEBUG")]
        public static void Log(string message, Color color) => LuaCsLogger.LogMessage(message, color);

        [Conditional("DEBUG")]
        public static void Error(string message) => LuaCsLogger.LogError(message);
    }
}

