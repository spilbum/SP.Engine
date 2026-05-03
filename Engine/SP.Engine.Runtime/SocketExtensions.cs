using System;
using System.Diagnostics;
using System.Net.Sockets;
using SP.Core.Logging;

namespace SP.Engine.Runtime
{
    public static class SocketExtensions
    {
        public static void SafeClose(this Socket socket)
        {
            if (socket == null) return;

            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // ignored
            }

            try
            {
                socket.Close();
            }
            catch
            {
                // ignored
            }
        }
    }
}
