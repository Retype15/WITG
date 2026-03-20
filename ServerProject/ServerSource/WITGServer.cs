// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;

namespace WITG
{
    public static class WITGServer
    {
        public class SlotData { public int Slot = 0; }
        private readonly static ConditionalWeakTable<CharacterCampaignData, SlotData> DataSlots = [];
        private readonly static Dictionary<Client, int> ClientActiveSlots = [];

        public static int GetSlot(CharacterCampaignData data) =>
            DataSlots.TryGetValue(data, out var sd) ? sd.Slot : 0;

        public static void SetSlot(CharacterCampaignData data, int slot) =>
            DataSlots.GetOrCreateValue(data).Slot = slot;

        public static int GetActiveSlot(Client client) =>
            ClientActiveSlots.TryGetValue(client, out int slot) ? slot : 0;

        public static void Initialize()
        {
            GameMain.LuaCs.Networking.Receive("WITG_ReqInfo", args =>
            {
                var client = (Client)args[1];

                if (client == null || client.Connection == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;

                //var msgIn = (IReadMessage)args[0];

                var msgOut = GameMain.LuaCs.Networking.Start("WITG_InfoRes");

                var allData = campaign.characterData.Where(cd =>
                    cd.AccountId.TryUnwrap(out var accId) &&
                    client.AccountId.TryUnwrap(out var clientId) &&
                    accId == clientId).ToList();

                msgOut.WriteInt32(allData.Count);
                foreach (var data in allData)
                {
                    msgOut.WriteInt32(GetSlot(data));
                    msgOut.WriteString(data.Name ?? "Unknown");
                    msgOut.WriteString(data.CharacterInfo?.Job?.Name.Value ?? "No Job");
                }
                msgOut.WriteInt32(GetActiveSlot(client));
                GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
            });

            GameMain.LuaCs.Networking.Receive("WITG_SelSlot", args =>
            {
                var msgIn = (IReadMessage)args[0];
                var client = (Client)args[1];

                if (client == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;
                if (GameMain.GameSession?.IsRunning == true) return;

                int targetSlot = msgIn.ReadInt32();

                WITGServer.ClientActiveSlots[client] = targetSlot;

                var matchingData = campaign.characterData.Find(cd =>
                    cd.AccountId.TryUnwrap(out var accId) &&
                    client.AccountId.TryUnwrap(out var clientId) &&
                    accId == clientId &&
                    WITGServer.GetSlot(cd) == targetSlot);

                bool hasCharacter = matchingData != null;

                var msgOut = GameMain.LuaCs.Networking.Start("WITG_SelSuccess");
                msgOut.WriteInt32(targetSlot);
                msgOut.WriteBoolean(hasCharacter);

                if (hasCharacter)
                {
#pragma warning disable CS8602
                    matchingData.CharacterInfo.ServerWrite(msgOut);
#pragma warning restore CS8602
                    client.CharacterInfo = matchingData.CharacterInfo;
                }
                else
                {
                    client.CharacterInfo = null;
                }

                campaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.CharacterInfo);
                GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
            });

            GameMain.LuaCs.Networking.Receive("WITG_DeleteSlot", args =>
            {
                var msgIn = (IReadMessage)args[0];
                var client = (Client)args[1];
                if (client == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;

                int slotToDelete = msgIn.ReadInt32();

                if (slotToDelete == 0)
                {
                    LuaCsLogger.LogMessage($"[WITG-SECURITY] Client {client.Name} tried to delete slot 0. Request denied.");
                    return;
                }

                var charData = campaign.characterData.Find(cd =>
                    cd.AccountId.TryUnwrap(out var id) &&
                    client.AccountId.TryUnwrap(out var cid) &&
                    id == cid &&
                    WITGServer.GetSlot(cd) == slotToDelete);

                if (charData != null)
                {
                    campaign.characterData.Remove(charData);
                    LuaCsLogger.LogMessage($"[WITG] Server: Slot {slotToDelete} deleted for {client.Name}.");

                    if (WITGServer.GetActiveSlot(client) == slotToDelete)
                    {
                        client.CharacterInfo = null;
                        campaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.CharacterInfo);
                    }

                    var msgOut = GameMain.LuaCs.Networking.Start("WITG_InfoRes");
                    var myChars = campaign.characterData.Where(cd => cd.AccountId.TryUnwrap(out var id) && client.AccountId.TryUnwrap(out var cid) && id == cid).ToList();
                    msgOut.WriteInt32(myChars.Count);
                    foreach (var cd in myChars)
                    {
                        msgOut.WriteInt32(WITGServer.GetSlot(cd));
                        msgOut.WriteString(cd.Name ?? "Unknown");
                        msgOut.WriteString(cd.CharacterInfo?.Job?.Name.Value ?? "No Job");
                    }
                    msgOut.WriteInt32(WITGServer.GetActiveSlot(client));
                    GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
                }
            });
        }

        public static void Dispose() => ClientActiveSlots.Clear();
    }

    [HarmonyPatch(typeof(CharacterCampaignData))]
    public static class CharacterCampaignDataPatches
    {
        [HarmonyPatch(MethodType.Constructor, [typeof(XElement)])]
        [HarmonyPostfix]
        public static void CtorXMLPostfix(CharacterCampaignData __instance, XElement element) =>
            WITGServer.SetSlot(__instance, element.GetAttributeInt("witg_slot", 0));

        [HarmonyPatch(MethodType.Constructor, [typeof(Client)])]
        [HarmonyPostfix]
        public static void CtorClientPostfix(CharacterCampaignData __instance, Client client) =>
            WITGServer.SetSlot(__instance, WITGServer.GetActiveSlot(client));

        [HarmonyPatch(nameof(CharacterCampaignData.Save))]
        [HarmonyPostfix]
        public static void SavePostfix(CharacterCampaignData __instance, ref XElement __result)
        {
            int slot = WITGServer.GetSlot(__instance);
            if (slot > 0) __result.Add(new XAttribute("witg_slot", slot));
        }

        [HarmonyPatch(nameof(CharacterCampaignData.MatchesClient))]
        [HarmonyPostfix]
        public static void MatchesClientPostfix(CharacterCampaignData __instance, Client client, ref bool __result)
        {
            if (!__result) return;
            if (WITGServer.GetSlot(__instance) != WITGServer.GetActiveSlot(client)) __result = false;
        }

        [HarmonyPatch(nameof(CharacterCampaignData.IsDuplicate))]
        [HarmonyPostfix]
        public static void IsDuplicatePostfix(CharacterCampaignData __instance, CharacterCampaignData other, ref bool __result)
        {
            if (__result && WITGServer.GetSlot(__instance) != WITGServer.GetSlot(other)) __result = false;
        }

        [HarmonyPatch(nameof(CharacterCampaignData.MatchesClient))]
        public static class MatchesClientPatch
        {
            [HarmonyPostfix]
            public static void Postfix(CharacterCampaignData __instance, Client client, ref bool __result)
            {
                if (!__result) return;

                int savedSlot = WITGServer.GetSlot(__instance);
                int activeSlot = WITGServer.GetActiveSlot(client);

                if (savedSlot != activeSlot)
                {
                    __result = false;
                }
            }
        }
    }
}