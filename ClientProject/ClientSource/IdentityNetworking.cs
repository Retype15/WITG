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
            var entries = new List<IdentityData.CharacterEntry>();
            for (int i = 0; i < count; i++)
            {
                entries.Add(new IdentityData.CharacterEntry
                {
                    Slot = msgIn.ReadInt32(),
                    Name = msgIn.ReadString(),
                    Job = msgIn.ReadString()
                });
            }
            IdentityData.Update(msgIn.ReadInt32(), entries);
            CrossThread.RequestExecutionOnMainThread(() => IdentityUI.Refresh());
        }
        public static void SendDeleteSlot(int slot)
        {
            var msg = GameMain.LuaCs.Networking.Start("WITG_DeleteSlot");
            msg.WriteInt32(slot);
            GameMain.LuaCs.Networking.Send(msg, DeliveryMethod.Reliable);
        }
    }
}