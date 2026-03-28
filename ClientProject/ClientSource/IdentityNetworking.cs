// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using Barotrauma;
using Barotrauma.Networking;

namespace WITG
{
    public static class IdentityNetworking
    {
        public static void RequestInfo()
        {
            if (GameMain.GameSession?.GameMode is not MultiPlayerCampaign || GameMain.GameSession.IsRunning) return;
            GameMain.LuaCs.Networking.Send(GameMain.LuaCs.Networking.Start("WITG_ReqInfo"), DeliveryMethod.Reliable);
        }

        public static void SelectSlot(int slot)
        {
            var msg = GameMain.LuaCs.Networking.Start("WITG_SelSlot");
            msg.WriteInt32(slot);
            GameMain.LuaCs.Networking.Send(msg, DeliveryMethod.Reliable);
        }

        public static void OnReceiveInfo(object[] args)
        {
            var msgIn = (IReadMessage)args[0];
            int count = msgIn.ReadInt32();

            LuaCsLogger.LogMessage($"[WITG] Received {count} characters from server.");

            var entries = new List<IdentityData.CharacterEntry>();
            for (int i = 0; i < count; i++)
            {
                entries.Add(new IdentityData.CharacterEntry
                {
                    Slot = msgIn.ReadInt32(),
                    Name = msgIn.ReadString(),
                    Job = msgIn.ReadString(),
                    IsWounded = msgIn.ReadBoolean(),
                    IsPermanentlyDead = msgIn.ReadBoolean()
                });
            }
            int activeSlot = msgIn.ReadInt32();
            IdentityData.Update(activeSlot, entries);
            CrossThread.RequestExecutionOnMainThread(() =>
            {
                IdentityUI.Refresh();
                typeof(IdentityUI).GetMethod("UpdateActionButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
            });
        }
        public static void SendDeleteSlot(int slot)
        {
            var msg = GameMain.LuaCs.Networking.Start("WITG_DeleteSlot");
            msg.WriteInt32(slot);
            GameMain.LuaCs.Networking.Send(msg, DeliveryMethod.Reliable);
        }
    }
}