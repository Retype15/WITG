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
            var net = LuaCsSetup.Instance.NetworkingService;

            net.Receive("WITG_InfoRes", message =>
            {
                WITGLogger.Log("[WITG] Client: Received info update message.");
                IdentityNetworking.OnReceiveInfo(message);
            });

            net.Receive("WITG_SelSuccess", msgIn =>
            {
                int slot = msgIn.ReadInt32();
                bool hasChar = msgIn.ReadBoolean();

                WITGLogger.Log($"[WITG] Client: Selection success received for Slot {slot} (HasInfo: {hasChar}).");

                if (GameMain.Client == null) return;

                IdentityData.Update(slot, [.. IdentityData.Cache.Values]);

                if (hasChar)
                    GameMain.Client.CharacterInfo = CharacterInfo.ClientRead(CharacterPrefab.HumanSpeciesName.ToIdentifier(), msgIn);
                else
                    GameMain.Client.CharacterInfo = null;

                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    IdentityUI.Refresh();
                    if (GameMain.GameSession?.IsRunning ?? false)
                    {
                        var selectedEntry = IdentityData.Cache.GetValueOrDefault(slot);

                        if (hasChar && !selectedEntry.IsPermanentlyDead)
                        {
                            // 1st case PJ lived
                            IdentityUI.ToggleFloatingPanel();
                            var deathPrompt = GameMain.GameSession.DeathPrompt;
                            if (deathPrompt != null)
                            {
                                deathPrompt.Close();
                                GameMain.GameSession.DeathPrompt = null;
                            }
                        }
                        else
                        {
                            // 2nd case PJ dead or no pj
                            IdentityUI.ToggleFloatingPanel();
                        }
                    }
                    else if (GameMain.NetLobbyScreen != null)
                    {
                        var lobby = GameMain.NetLobbyScreen;

                        lobby.SetCampaignCharacterInfo(GameMain.Client.CharacterInfo);
                        var updateMethod = typeof(NetLobbyScreen).GetMethod("UpdatePlayerFrame",
                            BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(CharacterInfo), typeof(bool)], null);

                        updateMethod?.Invoke(lobby, [GameMain.Client.CharacterInfo, true]);

                        GameMain.NetLobbyScreen.Select();
                    }
                });
            });
        }

        public static void Dispose()
        {
            // AA Not needs now.
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
            WITGLogger.Log("[WITG] Client: Switching campaign/mode. Clearing local cache.", Color.Gold);
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

            WITGLogger.Log("[WITG] Client: Injecting Change Identity button into DeathPrompt.");

            if (content.Children.FirstOrDefault(c =>
                c is GUILayoutGroup { IsHorizontal: true }) is not GUILayoutGroup decisionContainer) return;

            var identityBtnContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f),
                decisionContainer.RectTransform));

            var identityButton = new GUIButton(new RectTransform(Vector2.One, identityBtnContainer.RectTransform), TextSOS.Get("witg.changeidentity", "CHANGE IDENTITY"), style: "GUIButton")
            {
                OnClicked = (b, userdata) =>
                {
                    if (IdentityUI.FloatingPanel != null) IdentityUI.ToggleFloatingPanel();

                    IdentityUI.ToggleFloatingPanel();
                    return true;
                }
            };

            identityButton.FadeIn(wait: 5.0f, duration: 0.5f);

            float share = 1.0f / decisionContainer.CountChildren;
            foreach (var child in decisionContainer.Children)
            {
                child.RectTransform.RelativeSize = new Vector2(share, 1f);
            }
        }
    }
}