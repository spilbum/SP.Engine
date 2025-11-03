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
        public string DnsName { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
    }

    public static class HostNetworkInfoProvider
    {
        private const string TokenPath = "api/token";
        private const string MetaPath = "meta-data/";
        private const int DefaultTimeoutMs = 1500;
        private static readonly Uri ImdsBase = new Uri("http://169.254.169.254/latest");

        public static bool TryGet(out HostNetworkInfo info, int timeoutMs = DefaultTimeoutMs)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs * 2);
                info = GetAsync(cts.Token).GetAwaiter().GetResult();
                return info != null && info.Env != NetworkEnv.Unknown;
            }
            catch
            {
                info = new HostNetworkInfo();
                return false;
            }
        }

        private static async Task<HostNetworkInfo> GetAsync(CancellationToken ct)
        {
            using var http = CreateHttpClient(DefaultTimeoutMs);
            var ec2 = await TryGetEc2Async(http, ct).ConfigureAwait(false);
            if (ec2 != null) return ec2;

            return await GetFromLocalAsync(http, ct).ConfigureAwait(false);
        }

        private static HttpClient CreateHttpClient(int timeoutMs)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            return client;
        }

        private static async Task<HostNetworkInfo> TryGetEc2Async(HttpClient http, CancellationToken ct)
        {
            try
            {
                using var tokenReq = new HttpRequestMessage(HttpMethod.Put, new Uri(ImdsBase, TokenPath));
                tokenReq.Headers.Add("X-aws-ec2-metadata-token-ttl-seconds", "60");

                using var tokenResp = await http.SendAsync(tokenReq, ct).ConfigureAwait(false);
                if (!tokenResp.IsSuccessStatusCode)
                    return null;

                var token = await tokenResp.Content.ReadAsStringAsync().ConfigureAwait(false);

                var az = await GetMeta("placement/availability-zone").ConfigureAwait(false);
                var region = !string.IsNullOrEmpty(az) && az.Length > 1 ? az[..^1] : string.Empty;
                var publicIpv4 = await GetMeta("public-ipv4").ConfigureAwait(false);
                var privateIpv4 = await GetMeta("local-ipv4").ConfigureAwait(false);
                var publicHostName = await GetMeta("public-hostname").ConfigureAwait(false);
                var instanceId = await GetMeta("instance-id").ConfigureAwait(false);

                if (string.IsNullOrEmpty(instanceId) && string.IsNullOrEmpty(privateIpv4))
                    return null;

                return new HostNetworkInfo
                {
                    Env = NetworkEnv.AwsEc2,
                    Region = region,
                    PublicIpAddress = publicIpv4 ?? string.Empty,
                    PrivateIpAddress = privateIpv4 ?? string.Empty,
                    DnsName = publicHostName ?? string.Empty,
                    InstanceId = instanceId ?? string.Empty
                };

                async Task<string> GetMeta(string path)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(ImdsBase, MetaPath + path));
                    req.Headers.Add("X-aws-ec2-metadata-token", token);
                    using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return string.Empty;
                    return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                return null;
            }
        }

        private static async Task<HostNetworkInfo> GetFromLocalAsync(HttpClient http, CancellationToken ct)
        {
            var info = new HostNetworkInfo { Env = NetworkEnv.Local, Region = "Local" };

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        var ipStr = ip.ToString();
                        if (IsPrivateIp(ip))
                        {
                            if (string.IsNullOrEmpty(info.PrivateIpAddress))
                                info.PrivateIpAddress = ipStr;
                        }
                        else if (!IPAddress.IsLoopback(ip) && !IsLinkLocal(ip))
                        {
                            if (string.IsNullOrEmpty(info.PublicIpAddress))
                                info.PublicIpAddress = ipStr;
                        }
                    }
                }

                if (string.IsNullOrEmpty(info.PublicIpAddress))
                    try
                    {
                        info.PublicIpAddress = await http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false);
                    }
                    catch
                    {
                        /* ignore */
                    }
            }
            catch
            {
                /* ignore */
            }

            return info;
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
