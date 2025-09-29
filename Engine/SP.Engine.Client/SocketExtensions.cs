using System;
using System.Net;
using System.Net.Sockets;

namespace SP.Engine.Client
{ 
    public delegate void ConnectCallback(Socket socket, object state, SocketAsyncEventArgs e, Exception error);

    public static class SocketExtensions
    {
        private sealed class ConnectToken
        {
            public object State;
            public ConnectCallback Callback;
        }

        private sealed class DnsConnectState
        {
            public IPAddress[] Addresses;
            public int NextAddressIndex;
            public int Port;
            public Socket Socket;
            public object State;
            public ConnectCallback Callback;
        }
        
        public static void ResolveAndConnectAsync(this EndPoint remoteEndPoint, ConnectCallback callback, object state)
        {
            switch (remoteEndPoint)
            {
                case IPEndPoint ipEndPoint:
                    // IP 기반 연결
                    var e = CreateSocketAsyncEventArgs(remoteEndPoint, callback, state);
                    var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    var b = socket.ConnectAsync(e);
                    break;
                case DnsEndPoint dnsEndPoint:
                    // DNS 기반 연결
                    Dns.BeginGetHostAddresses(dnsEndPoint.Host, OnGetHostAddresses,
                        new DnsConnectState
                        {
                            Port = dnsEndPoint.Port,
                            State = state,
                            Callback = callback,
                        });
                    break;
            }
        }

        private static void OnGetHostAddresses(IAsyncResult result)
        {
            var dnsConnectState = result.AsyncState as DnsConnectState;
            var callback = dnsConnectState?.Callback;
            if (callback == null)
                return;

            IPAddress[] addresses;

            try
            {
                addresses = Dns.EndGetHostAddresses(result);
            }
            catch (Exception ex)
            {
                callback(null, dnsConnectState.State, null, ex);
                return;
            }

            if (addresses == null || addresses.Length == 0)
            {
                callback(null, dnsConnectState.State, null, new Exception("addresses is empty"));
                return;
            }

            dnsConnectState.Addresses = addresses;
            dnsConnectState.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var address = GetNextAddress(dnsConnectState, out var socket);
            if (address == null || socket == null)
            {
                callback(null, dnsConnectState.State, null, new Exception("address or socket is null"));
                return;
            }

            var e = new SocketAsyncEventArgs();
            e.Completed += OnConnectCompleted;
            var ipEndPoint = new IPEndPoint(address, dnsConnectState.Port);
            e.RemoteEndPoint = ipEndPoint;
            e.UserToken = dnsConnectState;

            if (!socket.ConnectAsync(e))
                OnConnectCompleted(socket, e);
        }

        private static IPAddress GetNextAddress(DnsConnectState state, out Socket attemptsSocket)
        {
            IPAddress address = null;
            attemptsSocket = null;

            var currentIndex = state.NextAddressIndex;
            var addresses = state.Addresses;
            if (null == addresses)
                return null;

            while (attemptsSocket == null)
            {
                if (currentIndex >= addresses.Length)
                    return null;

                address = addresses[currentIndex++];
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    attemptsSocket = state.Socket;
            }

            state.NextAddressIndex = currentIndex;
            return address;
        }

        private static SocketAsyncEventArgs CreateSocketAsyncEventArgs(EndPoint remoteEndPoint, ConnectCallback callback, object state)
        {
            var e = new SocketAsyncEventArgs();
            e.RemoteEndPoint = remoteEndPoint;
            e.Completed += OnConnectCompleted;
            e.UserToken = new ConnectToken { State = state, Callback = callback };
            return e;
        }

        private static void OnConnectCompleted(object sender, SocketAsyncEventArgs e)
        {
            e.Completed -= OnConnectCompleted;
            var token = (ConnectToken)e.UserToken;
            e.UserToken = null;
            
            if (e.SocketError == SocketError.Success)
                token.Callback.Invoke(sender as Socket, token.State, e, null);
            else
                token.Callback.Invoke(null, token.State, e, new SocketException((int)e.SocketError));
        }
    }
    
}
