// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

namespace WITG
{
    public partial class Plugin
    {
        public static void InitServer()
        {
            WITGServer.Initialize();
        }

        public static void DisposeServer()
        {
            WITGServer.Dispose();
        }
    }
}