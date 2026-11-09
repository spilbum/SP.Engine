using System;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Core
{
    public enum NetworkEnv
    {
        AwsEc2 = 0,
        Local = 1,
        Unknown = 2
    }

    public class HostNetworkInfo
    {
        public NetworkEnv Env { get; set; } = NetworkEnv.Unknown;
        public string Region { get; set; } = string.Empty;
        public string PublicIpAddress { get; set; } = string.Empty;
        public string PrivateIpAddress { get; set; } = string.Empty;
        public string DomainName { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
    }

    public static class HostNetworkInfoProvider
    {
        private const string TokenPath = "api/token";
        private const string MetaPath = "meta-data/";
        private static readonly Uri ImdsBase = new Uri("http://169.254.169.254/latest/");
        private static readonly TimeSpan ImdsProbeTimeout = TimeSpan.FromMilliseconds(300);

        private static readonly string[] PublicIpResolveUrls =
        {
            "https://checkip.amazonaws.com",
            "https://api.ipify.org"
        };

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private static volatile HostNetworkInfo _cachedInfo;

        public static bool TryGet(out HostNetworkInfo info, TimeSpan timeout)
        {
            return TryGet(out info, timeout, useCache: true);
        }

        public static bool TryGet(out HostNetworkInfo info, TimeSpan timeout, bool useCache)
        {
            if (useCache && _cachedInfo != null)
            {
                info = _cachedInfo;
                return true;
            }

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                info = GetAsync(cts.Token, useCache).GetAwaiter().GetResult();
                return info != null;
            }
            catch
            {
                info = null;
                return false;
            }
        }

        public static async Task<HostNetworkInfo> GetAsync(CancellationToken ct = default, bool useCache = true)
        {
            if (useCache && _cachedInfo != null)
                return _cachedInfo;

            var info = await TryGetEc2Async(SharedHttpClient, ct).ConfigureAwait(false)
                       ?? await GetFromLocalAsync(SharedHttpClient, ct).ConfigureAwait(false);

            if (info != null)
                _cachedInfo = info;

            return info;
        }

        public static void ClearCache()
        {
            _cachedInfo = null;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = false
            };
            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        private static async Task<HostNetworkInfo> TryGetEc2Async(HttpClient http, CancellationToken ct)
        {
            try
            {
                string token;
                using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    probeCts.CancelAfter(ImdsProbeTimeout);
                    using var tokenReq = new HttpRequestMessage(HttpMethod.Put, new Uri(ImdsBase, TokenPath));
                    tokenReq.Headers.Add("X-aws-ec2-metadata-token-ttl-seconds", "60");

                    using var tokenResp = await http.SendAsync(tokenReq, HttpCompletionOption.ResponseContentRead, probeCts.Token).ConfigureAwait(false);
                    if (!tokenResp.IsSuccessStatusCode)
                        return null;

                    token = (await tokenResp.Content.ReadAsStringAsync().ConfigureAwait(false))?.Trim();
                }

                if (string.IsNullOrEmpty(token))
                    return null;

                var regionTask = GetMetaAsync("placement/region");
                var azTask = GetMetaAsync("placement/availability-zone");
                var publicIpv4Task = GetMetaAsync("public-ipv4");
                var privateIpv4Task = GetMetaAsync("local-ipv4");
                var publicHostNameTask = GetMetaAsync("public-hostname");
                var instanceIdTask = GetMetaAsync("instance-id");

                await Task.WhenAll(regionTask, azTask, publicIpv4Task, privateIpv4Task, publicHostNameTask, instanceIdTask).ConfigureAwait(false);

                var region = regionTask.Result;
                var az = azTask.Result;
                if (string.IsNullOrEmpty(region) && !string.IsNullOrEmpty(az) && az.Length > 1)
                    region = az[..^1];

                var publicIpv4 = publicIpv4Task.Result;
                var privateIpv4 = privateIpv4Task.Result;
                var publicHostName = publicHostNameTask.Result;
                var instanceId = instanceIdTask.Result;

                if (string.IsNullOrEmpty(instanceId) && string.IsNullOrEmpty(privateIpv4))
                    return null;

                return new HostNetworkInfo
                {
                    Env = NetworkEnv.AwsEc2,
                    Region = region ?? string.Empty,
                    PublicIpAddress = publicIpv4 ?? string.Empty,
                    PrivateIpAddress = privateIpv4 ?? string.Empty,
                    DomainName = publicHostName ?? string.Empty,
                    InstanceId = instanceId ?? string.Empty
                };

                async Task<string> GetMetaAsync(string path)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(ImdsBase, MetaPath + path));
                        req.Headers.Add("X-aws-ec2-metadata-token", token);
                        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) return string.Empty;
                        return (await resp.Content.ReadAsStringAsync().ConfigureAwait(false))?.Trim() ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static async Task<HostNetworkInfo> GetFromLocalAsync(HttpClient http, CancellationToken ct)
        {
            var info = new HostNetworkInfo
            {
                Env = NetworkEnv.Local,
                Region = "Local"
            };

            try
            {
                info.DomainName = Dns.GetHostName();
            }
            catch
            {
                /* ignore */
            }

            try
            {
                // 1. OS 라우팅 테이블을 통해 외부 통신에 사용되는 주 IP 추출 (실제 패킷 전송 없음)
                if (TryGetOutboundIpByRouting(out var routedIp))
                {
                    if (IsPrivateIp(routedIp))
                        info.PrivateIpAddress = routedIp.ToString();
                    else if (!IPAddress.IsLoopback(routedIp) && !IsLinkLocal(routedIp))
                        info.PublicIpAddress = routedIp.ToString();
                }

                // 2. 인터페이스를 순회하며 누락된 사설/공인 IP 보완 (게이트웨이가 있는 주 인터페이스 우선)
                if (string.IsNullOrEmpty(info.PrivateIpAddress) || string.IsNullOrEmpty(info.PublicIpAddress))
                {
                    PopulateIpFromInterfaces(info);
                }

                // 3. 공인 IP가 아직 없다면 외부 서비스(타임아웃 적용)를 통해 조회
                if (string.IsNullOrEmpty(info.PublicIpAddress))
                {
                    info.PublicIpAddress = await ResolvePublicIpExternalAsync(http, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                /* ignore */
            }

            return info;
        }

        private static bool TryGetOutboundIpByRouting(out IPAddress address)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint &&
                    !IPAddress.IsLoopback(endPoint.Address) &&
                    !IsLinkLocal(endPoint.Address) &&
                    !IPAddress.Any.Equals(endPoint.Address))
                {
                    address = endPoint.Address;
                    return true;
                }
            }
            catch
            {
                /* ignore */
            }

            address = null;
            return false;
        }

        private static void PopulateIpFromInterfaces(HostNetworkInfo info)
        {
            string fallbackPrivateIp = null;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var ipProps = ni.GetIPProperties();
                var hasGateway = false;
                foreach (var gw in ipProps.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(gw.Address))
                    {
                        hasGateway = true;
                        break;
                    }
                }

                foreach (var ua in ipProps.UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(ip) || IsLinkLocal(ip))
                        continue;

                    var ipStr = ip.ToString();
                    if (IsPrivateIp(ip))
                    {
                        if (hasGateway && string.IsNullOrEmpty(info.PrivateIpAddress))
                            info.PrivateIpAddress = ipStr;
                        else if (string.IsNullOrEmpty(fallbackPrivateIp))
                            fallbackPrivateIp = ipStr;
                    }
                    else if (string.IsNullOrEmpty(info.PublicIpAddress))
                    {
                        info.PublicIpAddress = ipStr;
                    }
                }
            }

            if (string.IsNullOrEmpty(info.PrivateIpAddress) && !string.IsNullOrEmpty(fallbackPrivateIp))
                info.PrivateIpAddress = fallbackPrivateIp;
        }

        private static async Task<string> ResolvePublicIpExternalAsync(HttpClient http, CancellationToken ct)
        {
            foreach (var url in PublicIpResolveUrls)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    var raw = (await resp.Content.ReadAsStringAsync().ConfigureAwait(false))?.Trim();
                    if (!string.IsNullOrEmpty(raw) && IPAddress.TryParse(raw, out _))
                        return raw;
                }
                catch
                {
                    /* try next url */
                }
            }

            return string.Empty;
        }

        public static bool IsPrivateIp(string ip)
        {
            return IPAddress.TryParse(ip, out var addr) && IsPrivateIp(addr);
        }

        public static bool IsPrivateIp(IPAddress ip)
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    {
                        var b = ip.GetAddressBytes();
                        // 10.0.0.0/8
                        if (b[0] == 10) return true;
                        // 172.16.0.0/12
                        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                        // 192.168.0.0/16
                        if (b[0] == 192 && b[1] == 168) return true;
                        // CGNAT 100.64.0.0/10 (64~127)
                        if (b[0] == 100 && (b[1] & 0b1100_0000) == 0b0100_0000) return true;
                        return false;
                    }
                case AddressFamily.InterNetworkV6:
                    {
                        var b = ip.GetAddressBytes();
                        if ((b[0] & 0xFE) == 0xFC) return true;
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static bool IsLinkLocal(IPAddress ip)
        {
            switch (ip.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    {
                        var b = ip.GetAddressBytes();
                        return b[0] == 169 && b[1] == 254;
                    }
                case AddressFamily.InterNetworkV6:
                    {
                        var b = ip.GetAddressBytes();
                        return b.Length >= 2 && b[0] == 0xFE && (b[1] & 0xC0) == 0x80;
                    }
                default:
                    return false;
            }
        }
    }
}
