// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using System.Reflection;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace WITG
{
    public static class WITGClient
    {
        public static void Initialize()
        {
            GameMain.LuaCs.Networking.Receive("WITG_InfoRes", args =>
            {
                IdentityNetworking.OnReceiveInfo(args);
            });

            GameMain.LuaCs.Networking.Receive("WITG_SelSuccess", args =>
            {
                var msgIn = (IReadMessage)args[0];
                int slot = msgIn.ReadInt32();
                bool hasChar = msgIn.ReadBoolean();

                if (GameMain.Client == null) return;

                IdentityData.Update(slot, [.. IdentityData.Cache.Values]);

                if (hasChar)
                    GameMain.Client.CharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName.ToIdentifier(), msgIn);

                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    IdentityUI.Refresh();
                    if (GameMain.GameSession?.IsRunning ?? false)
                    {
                        if (GameMain.Client.CharacterInfo?.PermanentlyDead ?? false)
                            RespawnManager.ShowDeathPromptIfNeeded(0.5f);
                        else
                            IdentityUI.ToggleFloatingPanel();
                    }
                    else
                    {
                        GameMain.NetLobbyScreen?.Select();
                        var updateMethod = typeof(NetLobbyScreen).GetMethod("UpdatePlayerFrame",
                            BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(CharacterInfo), typeof(bool)], null);
                        updateMethod?.Invoke(GameMain.NetLobbyScreen, [GameMain.Client.CharacterInfo, true]);
                    }
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

        [HarmonyPatch(nameof(NetLobbyScreen.SelectMode))]
        [HarmonyPrefix]
        public static void SelectModePrefix()
        {
#if DEBUG
            LuaCsLogger.LogMessage("[WITG] Switching campaign/mode. Clearing local cache.", Color.Gold);
#endif
            IdentityData.Clear();
        }
    }

    [HarmonyPatch(typeof(DeathPrompt))]
    public static class DeathPromptPatches
    {
        [HarmonyPatch("CreatePrompt")]
        [HarmonyPostfix]
        public static void CreatePromptPostfix(DeathPrompt __instance)
        {
            var deathPromptFrameField = typeof(DeathPrompt).GetField("deathPromptFrame",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (deathPromptFrameField?.GetValue(__instance) is not GUIFrame deathPromptFrame) return;

            if (deathPromptFrame.Children.FirstOrDefault(c => c is GUILayoutGroup) is not GUILayoutGroup content) return;

            if (content.Children.FirstOrDefault(c =>
                c is GUILayoutGroup { IsHorizontal: true }) is not GUILayoutGroup decisionContainer) return;

            var identityBtnContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f),
                decisionContainer.RectTransform));

            _ = new GUIButton(new RectTransform(Vector2.One, identityBtnContainer.RectTransform), TextSOS.Get("witg.changeidentity", "CHANGE IDENTITY"), style: "GUIButtonSmall")
            {
                OnClicked = (b, userdata) =>
                {
                    if (IdentityUI.FloatingPanel != null) IdentityUI.ToggleFloatingPanel();

                    IdentityUI.ToggleFloatingPanel();
                    return true;
                }
            };

            float share = 1.0f / decisionContainer.CountChildren;
            foreach (var child in decisionContainer.Children)
            {
                child.RectTransform.RelativeSize = new Vector2(share, 1f);
            }
        }
    }
}