// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;
using System;
using System.Reflection;

namespace WITG
{
    public static class WITGClient
    {
        public static void Initialize()
        {
            GameMain.LuaCs.Networking.Receive("WITG_InfoRes", IdentityNetworking.OnReceiveInfo);

            GameMain.LuaCs.Networking.Receive("WITG_SelSuccess", args =>
            {
                var msgIn = (IReadMessage)args[0];
                int slot = msgIn.ReadInt32();
                bool hasChar = msgIn.ReadBoolean();

                if (GameMain.Client == null) return;

                GameMain.Client.CharacterInfo = null;
                typeof(NetLobbyScreen).GetField("campaignCharacterInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(GameMain.NetLobbyScreen, null);

                if (hasChar)
                    GameMain.Client.CharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName.ToIdentifier(), msgIn);

                CrossThread.RequestExecutionOnMainThread(() => {
                    GameMain.NetLobbyScreen?.Select();
                    var update = typeof(NetLobbyScreen).GetMethod("UpdatePlayerFrame", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(CharacterInfo), typeof(bool)], null);
                    update?.Invoke(GameMain.NetLobbyScreen, [GameMain.Client.CharacterInfo, true]);
                    IdentityNetworking.RequestInfo();
                });
            });
        }

        public static void Dispose()
        {
            GameMain.LuaCs.Networking.Remove("WITG_InfoRes");
            GameMain.LuaCs.Networking.Remove("WITG_SelSuccess");
        }
    }

    [HarmonyPatch(typeof(NetLobbyScreen))]
    public static class NetLobbyPatches
    {
        [HarmonyPatch(nameof(NetLobbyScreen.Select))]
        [HarmonyPostfix]
        public static void PostSelect(NetLobbyScreen __instance)
        {
            IdentityUI.Initialize(__instance);
            IdentityNetworking.RequestInfo();
        }
    }

    /*[HarmonyPatch(typeof(MultiPlayerCampaignSetupUI))]
    public static class CampaignSetupPatches
    {
        [HarmonyPatch("LoadClicked")]
        [HarmonyPrefix]
        public static bool OnLoadClicked(GUIButton button, object obj)
        {
            LuaCsLogger.LogMessage("[WITG] Intercepting Campaign Load. Resetting data.");
            IdentityData.Clear();
            return true;
        }
    }*/
}