using System.Net.Sockets;

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
