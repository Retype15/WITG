// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0079
#pragma warning disable IDE0290

namespace WITG
{
    public partial class Plugin
    {
        public static void InitClient()
        {
            WITGClient.Initialize();
        }

        public static void DisposeClient()
        {
            WITGClient.Dispose();
        }
    }
}