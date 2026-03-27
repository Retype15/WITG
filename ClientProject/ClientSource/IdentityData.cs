// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using System.Collections.Generic;
using Barotrauma;

namespace WITG
{
    public static class IdentityData
    {
        public struct CharacterEntry
        {
            public string Name;
            public string Job;
            public int Slot;
            public bool IsDead;
        }

        public static readonly Dictionary<int, CharacterEntry> Cache = [];
        public static int ActiveSlot { get; private set; } = -1;

        public static void Update(int activeSlot, List<CharacterEntry> entries)
        {
            Cache.Clear();
            foreach (var entry in entries) Cache[entry.Slot] = entry;
            ActiveSlot = activeSlot;
            LuaCsLogger.LogMessage($"[WITG] Cache Updated. Active Slot: {ActiveSlot}");
        }

        public static void Clear()
        {
            Cache.Clear();
            ActiveSlot = 0;
        }
    }
}