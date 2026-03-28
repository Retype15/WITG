// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using System.Reflection;
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
            LuaCsLogger.LogMessage("[WITG-Server] Initializing server-side slot management.");

            GameMain.LuaCs.Networking.Receive("WITG_ReqInfo", args =>
            {

                LuaCsLogger.LogMessage("[WITG-Server] Received request for info.");

                var client = (Client)args[1];

                if (client == null || client.Connection == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;

                //var msgIn = (IReadMessage)args[0];

                var msgOut = GameMain.LuaCs.Networking.Start("WITG_InfoRes");

                var allData = campaign.characterData.Where(cd =>
                    cd.AccountId.TryUnwrap(out var accId) &&
                    client.AccountId.TryUnwrap(out var clientId) &&
                    accId == clientId).ToList();

                LuaCsLogger.LogMessage($"[WITG-Server] Sending {allData.Count} slots to {client.Name}");

                msgOut.WriteInt32(allData.Count);
                foreach (var data in allData)
                {
                    msgOut.WriteInt32(GetSlot(data)); // Slot
                    msgOut.WriteString(data.Name ?? "Unknown"); // Name
                    string jobName = data.CharacterInfo?.Job?.Name.Value ?? "No Job";
                    msgOut.WriteString(jobName); // Job
                    msgOut.WriteBoolean(data.CharacterInfo?.CauseOfDeath != null); // IsWounded
                    msgOut.WriteBoolean(data.CharacterInfo?.PermanentlyDead ?? false); // IsPermanentlyDead
                }
                msgOut.WriteInt32(GetActiveSlot(client)); // ActiveSlot
                GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
            });

            GameMain.LuaCs.Networking.Receive("WITG_SelSlot", args =>
            {
                LuaCsLogger.LogMessage("[WITG-Server] Received request to select slot.");

                var msgIn = (IReadMessage)args[0];
                var client = (Client)args[1];
                if (client == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;

                if (GameMain.GameSession?.IsRunning == true && client.Character != null && !client.Character.IsDead) return;

                int targetSlot = msgIn.ReadInt32();
                WITGServer.ClientActiveSlots[client] = targetSlot;

                var targetData = campaign.characterData.Find(cd =>
                    cd.AccountId.TryUnwrap(out var id) && client.AccountId.TryUnwrap(out var cid) && id == cid && GetSlot(cd) == targetSlot);

                if (GameMain.GameSession?.IsRunning == true)
                {
                    if (client.Character != null) { client.Character.SetOwnerClient(null); client.Character = null; }

                    client.CharacterInfo = targetData?.CharacterInfo;
                    client.SpectateOnly = client.CharacterInfo == null || client.CharacterInfo.PermanentlyDead;
                    client.WaitForNextRoundRespawn = false;

                    if (client.CharacterInfo != null)
                    {
                        Character? existingLiveCharacter = Character.CharacterList.FirstOrDefault(c => c.Info == client.CharacterInfo && !c.IsDead);
                        if (existingLiveCharacter != null)
                        {
                            client.Character = existingLiveCharacter;
                            existingLiveCharacter.SetOwnerClient(client);
                            client.SpectateOnly = false;
                        }
                    }

                    if (targetData != null)
                    {
                        if (campaign.SetClientCharacterData(client) is CharacterCampaignData characterData)
                        {
                            SetSlot(characterData, targetSlot);
                            characterData.HasSpawned = targetData.HasSpawned;
                        }
                    }
                }
                else
                {
                    client.CharacterInfo = targetData?.CharacterInfo;
                }

                campaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.CharacterInfo);

                var msgOut = GameMain.LuaCs.Networking.Start("WITG_SelSuccess");
                msgOut.WriteInt32(targetSlot);

                var finalData = campaign.characterData.Find(cd =>
                    cd.AccountId.TryUnwrap(out var id) && client.AccountId.TryUnwrap(out var cid) && id == cid && GetSlot(cd) == targetSlot);

                msgOut.WriteBoolean(finalData != null);
                finalData?.CharacterInfo.ServerWrite(msgOut);

                GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
                //WITGServer.SyncClientRoster(client);

                LuaCsLogger.LogMessage($"[WITG-Server] Client {client.Name} selected slot {targetSlot}.");
            });

            GameMain.LuaCs.Networking.Receive("WITG_DeleteSlot", args =>
            {
                LuaCsLogger.LogMessage("[WITG-Server] Received request to delete slot.");

                var msgIn = (IReadMessage)args[0];
                var client = (Client)args[1];
                if (client == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;

                int slotToDelete = msgIn.ReadInt32();

                if (slotToDelete == 0)
                {
                    LuaCsLogger.LogMessage($"[WITG-Server] Client {client.Name} tried to delete slot 0. Request denied.");
                    return;
                }

                var charData = campaign.characterData.Find(cd =>
                    cd.AccountId.TryUnwrap(out var id) &&
                    client.AccountId.TryUnwrap(out var cid) &&
                    id == cid &&
                    WITGServer.GetSlot(cd) == slotToDelete);

                if (charData != null)
                {
                    var altCharacter = Character.CharacterList.FirstOrDefault(c => c.Info == charData.CharacterInfo);
                    altCharacter?.DespawnNow();

                    if (charData.CharacterInfo != null)
                    {
                        campaign.CrewManager.RemoveCharacterInfo(charData.CharacterInfo);
                    }

                    campaign.characterData.Remove(charData);

                    LuaCsLogger.LogMessage($"[WITG] Server: Slot {slotToDelete} deleted for {client.Name}.");

                    if (WITGServer.GetActiveSlot(client) == slotToDelete)
                    {
                        WITGServer.ClientActiveSlots[client] = 0;
                        client.Character = null;
                        client.CharacterInfo = null;
                        client.SpectateOnly = true;
                        campaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.CharacterInfo);

                        var primaryData = campaign.characterData.Find(cd => cd.AccountId.TryUnwrap(out var id) && client.AccountId.TryUnwrap(out var cid) && id == cid && WITGServer.GetSlot(cd) == 0);
                        if (primaryData != null) client.CharacterInfo = primaryData.CharacterInfo;
                    }

                    var msgOut = GameMain.LuaCs.Networking.Start("WITG_InfoRes");
                    var myChars = campaign.characterData.Where(cd => cd.AccountId.TryUnwrap(out var id) && client.AccountId.TryUnwrap(out var cid) && id == cid).ToList();
                    msgOut.WriteInt32(myChars.Count);
                    foreach (var cd in myChars)
                    {
                        msgOut.WriteInt32(WITGServer.GetSlot(cd));
                        msgOut.WriteString(cd.Name ?? "Unknown");
                        msgOut.WriteString(cd.CharacterInfo?.Job?.Name.Value ?? "No Job");
                        msgOut.WriteBoolean(cd.CharacterInfo?.CauseOfDeath != null);
                        msgOut.WriteBoolean(cd.CharacterInfo?.PermanentlyDead ?? false);
                    }
                    msgOut.WriteInt32(WITGServer.GetActiveSlot(client));
                    GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
                }
            });

            LuaCsLogger.LogMessage("[WITG-Server] Server-side slot management initialized.");
        }

        public static void SyncClientRoster(Client client)
        {
            if (client == null || GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) return;

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
                msgOut.WriteBoolean(data.CharacterInfo?.CauseOfDeath != null);
                msgOut.WriteBoolean(data.CharacterInfo?.PermanentlyDead ?? false);
            }
            msgOut.WriteInt32(GetActiveSlot(client));
            GameMain.LuaCs.Networking.Send(msgOut, client.Connection);
        }

        public static IEnumerable<CharacterCampaignData> DeserializeAlts(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) yield break;
            XElement root;
            try { root = XElement.Parse(data); } catch { yield break; }

            foreach (var el in root.Elements("CharacterCampaignData"))
            {
                var cd = new CharacterCampaignData(el);
                yield return cd;
            }
        }

        public static void Dispose()
        {
            GameMain.LuaCs.Networking.Remove("WITG_SelSuccess");
            GameMain.LuaCs.Networking.Remove("WITG_DeleteSlot");
            ClientActiveSlots.Clear();
        }
    }

    // MARK: Harmony Patches

    [HarmonyPatch(typeof(CharacterCampaignData))]
    public static class CharacterCampaignDataPatches
    {
        [HarmonyPatch(MethodType.Constructor, [typeof(XElement)])]
        [HarmonyPostfix]
        public static void CtorXMLPostfix(CharacterCampaignData __instance, XElement element)
        {
            string accIdStr = element.GetAttributeString("accountid", "");

            if (accIdStr.StartsWith("UNAUTHENTICATED_WITG_"))
            {
                string[] parts = accIdStr.Split('_', 4);
                if (parts.Length == 4 && int.TryParse(parts[2], out int slot))
                {
                    WITGServer.SetSlot(__instance, slot);

                    var realIdOption = Barotrauma.Networking.AccountId.Parse(parts[3]);
                    HarmonyLib.AccessTools.Field(typeof(CharacterCampaignData), nameof(CharacterCampaignData.AccountId))
                        .SetValue(__instance, realIdOption);
                    return;
                }
            }

            int legacySlot = element.GetAttributeInt("witg_slot", 0);
            if (legacySlot > 0)
            {
                WITGServer.SetSlot(__instance, legacySlot);

                string witgAccountId = element.GetAttributeString("witg_accountid", "");
                if (!string.IsNullOrEmpty(witgAccountId))
                {
                    var parsedIdOption = Barotrauma.Networking.AccountId.Parse(witgAccountId);
                    HarmonyLib.AccessTools.Field(typeof(CharacterCampaignData), nameof(CharacterCampaignData.AccountId))
                        .SetValue(__instance, parsedIdOption);
                }

                string witgAddress = element.GetAttributeString("witg_address", "");
                if (!string.IsNullOrEmpty(witgAddress))
                {
                    var parsedAddressOption = Barotrauma.Networking.Address.Parse(witgAddress);
                    HarmonyLib.AccessTools.Field(typeof(CharacterCampaignData), nameof(CharacterCampaignData.ClientAddress))
                        .SetValue(__instance, parsedAddressOption.Fallback(new Barotrauma.Networking.UnknownAddress()));
                }
            }
        }

        [HarmonyPatch(MethodType.Constructor, [typeof(Client)])]
        [HarmonyPostfix]
        public static void CtorClientPostfix(CharacterCampaignData __instance, Client client) =>
            WITGServer.SetSlot(__instance, WITGServer.GetActiveSlot(client));

        [HarmonyPatch(nameof(CharacterCampaignData.Save))]
        [HarmonyPostfix]
        public static void SavePostfix(CharacterCampaignData __instance, XElement __result)
        {
            int slot = WITGServer.GetSlot(__instance);
            if (slot > 0)
            {
                if (__instance.AccountId.TryUnwrap(out var accountId))
                {
                    string realIdStr = accountId.StringRepresentation;
                    __result.SetAttributeValue("accountid", $"UNAUTHENTICATED_WITG_{slot}_{realIdStr}");
                }
            }
        }

        [HarmonyPatch(nameof(CharacterCampaignData.IsDuplicate))]
        [HarmonyPrefix]
        public static bool IsDuplicatePrefix(CharacterCampaignData __instance, CharacterCampaignData other, ref bool __result)
        {
            if (WITGServer.GetSlot(__instance) != WITGServer.GetSlot(other))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(nameof(CharacterCampaignData.MatchesClient))]
        [HarmonyPrefix]
        public static bool MatchesClientPrefix(CharacterCampaignData __instance, Client client, ref bool __result)
        {
            if (client == null) return true;

            if (WITGServer.GetSlot(__instance) != WITGServer.GetActiveSlot(client))
            {
                __result = false;
                return false;
            }

            if (__instance.AccountId.TryUnwrap(out var accId) && client.AccountId.TryUnwrap(out var clientId))
            {
                __result = accId == clientId;
                return false;
            }

            return true;
        }
    }

    // On Testing...
    /* [HarmonyPatch(typeof(CharacterCampaignData), nameof(CharacterCampaignData.SpawnInventoryItems))]
    public static class FixSpawnInventoryCrash
    {
        [HarmonyPrefix]
        public static bool Prefix(CharacterCampaignData __instance, Character character)
        {
            if (HarmonyLib.AccessTools.Field(typeof(CharacterCampaignData), "itemData").GetValue(__instance) is not XElement)
            {
                LuaCsLogger.LogMessage($"[WITG-Safe] {character.Name} has no inventory data. Giving default items.", Microsoft.Xna.Framework.Color.Orange);
                character.GiveJobItems(GameMain.GameSession?.GameMode is PvPMode);
                return false;
            }
            return true;
        }
    } */

    [HarmonyPatch(typeof(MultiPlayerCampaign))]
    public static class MultiPlayerCampaignPatches
    {
        private static readonly Identifier MetadataKey = "witg_roster_data".ToIdentifier();

        [HarmonyPatch(nameof(MultiPlayerCampaign.Load))]
        [HarmonyPostfix]
        public static void LoadPostfix(MultiPlayerCampaign __instance)
        {
            if (__instance.CampaignMetadata == null) return;

            string serializedData = __instance.CampaignMetadata.GetString(MetadataKey, "");
            if (!string.IsNullOrWhiteSpace(serializedData))
            {
                XElement root = XElement.Parse(serializedData);
                int migratedCount = 0;

                foreach (var el in root.Elements("CharacterCampaignData"))
                {
                    var cd = new CharacterCampaignData(el);
                    int slot = el.GetAttributeInt("witg_slot", -1);

                    if (slot <= 0)
                    {
                        for (int s = 1; s < 4; s++)
                        {
                            if (!__instance.characterData.Any(d => d.AccountId == cd.AccountId && WITGServer.GetSlot(d) == s))
                            {
                                slot = s;
                                break;
                            }
                        }
                    }

                    if (slot > 0)
                    {
                        WITGServer.SetSlot(cd, slot);
                        __instance.characterData.Add(cd);
                        migratedCount++;
                    }
                }

                var dataField = typeof(CampaignMetadata).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
                if (dataField?.GetValue(__instance.CampaignMetadata) is Dictionary<Identifier, object> internalDict)
                {
                    internalDict.Remove(MetadataKey);
                    LuaCsLogger.LogMessage($"[WITG] Cleanup: Migrated {migratedCount} identities and removed old metadata.");
                }
            }
        }
    }

    [HarmonyPatch(typeof(Character))]
    public static class CharacterDeathSyncPatch
    {
        [HarmonyPatch("KillProjSpecific")]
        [HarmonyPostfix]
        public static void Postfix(Character __instance)
        {
            if (GameMain.Server == null) return;
            Client? owner = GameMain.Server.ConnectedClients?.FirstOrDefault(c => c.Character == __instance);
            if (owner != null) WITGServer.SyncClientRoster(owner);
        }
    }
}