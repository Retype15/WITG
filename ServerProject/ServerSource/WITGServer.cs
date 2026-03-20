// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

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
        private static readonly Identifier MetadataKey = "witg_roster_data".ToIdentifier();
        private readonly static ConditionalWeakTable<CharacterCampaignData, SlotData> DataSlots = [];
        private readonly static Dictionary<Client, int> ClientActiveSlots = [];

        public static int GetSlot(CharacterCampaignData data) =>
            DataSlots.TryGetValue(data, out var sd) ? sd.Slot : 0;

        public static void SetSlot(CharacterCampaignData data, int slot) =>
            DataSlots.GetOrCreateValue(data).Slot = slot;

        public static int GetActiveSlot(Client client) =>
            ClientActiveSlots.TryGetValue(client, out int slot) ? slot : 0;

        public static void ResetAllActiveSlots() =>
            ClientActiveSlots.Clear();

        public static void Initialize()
        {
            LuaCsLogger.LogMessage("[WITG] Initializing server-side slot management.");

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

                LuaCsLogger.LogMessage($"[WITG] Sending {allData.Count} slots to {client.Name}");

                msgOut.WriteInt32(allData.Count);
                foreach (var data in allData)
                {
                    msgOut.WriteInt32(GetSlot(data));
                    msgOut.WriteString(data.Name ?? "Unknown");
                    string jobName = data.CharacterInfo?.Job?.Name.Value ?? "No Job";
                    msgOut.WriteString(jobName);
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

            LuaCsLogger.LogMessage("[WITG] Server-side slot management initialized.");
        }

        public static string SerializeAlts(IEnumerable<CharacterCampaignData> alts)
        {
            XElement root = new("WITG_Roster");
            foreach (var alt in alts)
            {
                XElement altXml = alt.Save();
                altXml.SetAttributeValue("witg_slot", GetSlot(alt));
                root.Add(altXml);
            }
            return root.ToString(SaveOptions.DisableFormatting);
        }

        public static IEnumerable<CharacterCampaignData> DeserializeAlts(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) yield break;
            XElement root;
            try { root = XElement.Parse(data); } catch { yield break; }

            foreach (var el in root.Elements("CharacterCampaignData"))
            {
                var cd = new CharacterCampaignData(el);
                SetSlot(cd, el.GetAttributeInt("witg_slot", 0));
                yield return cd;
            }
        }

        public static void Dispose() => ClientActiveSlots.Clear();
    }

    // MARK: Harmony Patches

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
        public static void SavePostfix(CharacterCampaignData __instance, XElement __result)
        {
            int slot = WITGServer.GetSlot(__instance);
            if (slot > 0) __result.SetAttributeValue("witg_slot", slot);
        }

        [HarmonyPatch(nameof(CharacterCampaignData.MatchesClient))]
        [HarmonyPostfix]
        public static void MatchesClientPostfix(CharacterCampaignData __instance, Client client, ref bool __result)
        {
            if (!__result || client == null) return;
            if (WITGServer.GetSlot(__instance) != WITGServer.GetActiveSlot(client)) __result = false;
        }

        [HarmonyPatch(nameof(CharacterCampaignData.IsDuplicate))]
        [HarmonyPostfix]
        public static void IsDuplicatePostfix(CharacterCampaignData __instance, CharacterCampaignData other, ref bool __result)
        {
            if (__result && WITGServer.GetSlot(__instance) != WITGServer.GetSlot(other)) __result = false;
        }
    }

    [HarmonyPatch(typeof(MultiPlayerCampaign))]
    public static class MultiPlayerCampaignPatches
    {
        private static readonly Identifier MetadataKey = "witg_roster_data".ToIdentifier();
        private static readonly List<CharacterCampaignData> ShieldedAlts = [];

        [HarmonyPatch(nameof(MultiPlayerCampaign.Load))]
        [HarmonyPostfix]
        public static void LoadPostfix(MultiPlayerCampaign __instance)
        {
            if (__instance.CampaignMetadata == null) return;

            string serializedData = __instance.CampaignMetadata.GetString(MetadataKey, "");
            var restoredAlts = WITGServer.DeserializeAlts(serializedData).ToList();

            foreach (var alt in restoredAlts)
            {
                if (!__instance.characterData.Any(c => c.AccountId == alt.AccountId && WITGServer.GetSlot(c) == WITGServer.GetSlot(alt)))
                {
                    __instance.characterData.Add(alt);
                }
            }
#if DEBUG
            if (restoredAlts.Count > 0)
                LuaCsLogger.LogMessage($"[WITG] {restoredAlts.Count} identities in metadata.");
#endif
        }

        [HarmonyPatch(nameof(MultiPlayerCampaign.Save))]
        [HarmonyPrefix]
        public static void SavePrefix(MultiPlayerCampaign __instance)
        {
            ShieldedAlts.Clear();
            var allAlts = __instance.characterData.Where(cd => WITGServer.GetSlot(cd) > 0).ToList();

            string data = WITGServer.SerializeAlts(allAlts);
            __instance.CampaignMetadata.SetValue(MetadataKey, data);

            foreach (var alt in allAlts)
            {
                ShieldedAlts.Add(alt);
                __instance.characterData.Remove(alt);
#if DEBUG
                LuaCsLogger.LogMessage($"[WITG] Removed {alt.Name} from characterData.");
#endif
            }

        }

        [HarmonyPatch(nameof(MultiPlayerCampaign.Save))]
        [HarmonyPostfix]
        public static void SavePostfix(MultiPlayerCampaign __instance)
        {
            foreach (var alt in ShieldedAlts)
            {
                if (!__instance.characterData.Contains(alt))
                {
                    __instance.characterData.Add(alt);
#if DEBUG
                    LuaCsLogger.LogMessage($"[WITG] Added {alt.Name} back to characterData.");
#endif
                }
            }
            ShieldedAlts.Clear();
        }

        [HarmonyPatch(nameof(MultiPlayerCampaign.StartCampaignSetup))]
        [HarmonyPostfix]
        public static void StartCampaignSetupPostfix()
        {
#if DEBUG
            LuaCsLogger.LogMessage("[WITG] New campaign session. Resetting active slots.");
#endif
            WITGServer.ResetAllActiveSlots();
        }
    }
}