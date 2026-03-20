// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0079
#pragma warning disable IDE0290

using Barotrauma;
using HarmonyLib;
using System.Runtime.CompilerServices;

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
            LuaCsLogger.LogMessage($"[WITG] Shared: Initialized.");
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
            LuaCsLogger.LogMessage($"[WITG] Disposed.");
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
}

