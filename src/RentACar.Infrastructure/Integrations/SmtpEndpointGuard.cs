using System.Net;
using System.Net.Sockets;

namespace RentACar.Infrastructure.Integrations;

/// <summary>
/// F11.1b güvenlik M4 — kiracının yazdığı SMTP adresi sunucumuzun ağından çıkış yaptırır (SSRF / port tarama).
/// Kural: port yalnız SMTP portları; host DNS ile çözülür ve çözülen adreslerden BİRİ bile iç ağ / loopback /
/// link-local (bulut metadata 169.254.169.254 dahil) ise reddedilir. Bağlantı çözülen ve denetlenen IP'ye açılır
/// (DNS yeniden bağlama ile denetim ile bağlantı arasında adres değişemez); TLS için host adı ayrıca verilir.
/// </summary>
public static class SmtpEndpointGuard
{
    public static readonly IReadOnlySet<int> AllowedPorts = new HashSet<int> { 25, 465, 587, 2525 };

    /// <summary>İç ağ/özel amaçlı adres mi (saf, test edilebilir).</summary>
    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
            return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0 || b[0] == 10 || b[0] == 127
                   || (b[0] == 169 && b[1] == 254)
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0.0/24 (IETF protokol atamaları)
                   || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18.0.0/15 (kıyaslama ağı)
                   || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // CGNAT
                   || b[0] >= 224;                                 // çoklu yayın + ayrılmış
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            // Gömülü IPv4 taşıyan biçimler IPv4 kuralından geçer (iç adres v6 kılığında kaçmasın).
            if (EmbeddedIPv4(b) is { } v4) return IsBlocked(v4);
            return (b[0] & 0xFE) == 0xFC                    // fc00::/7 (ULA)
                   || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) // fe80::/10
                   || (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0) // fec0::/10 (eski site-local)
                   || b[0] == 0xFF;                           // çoklu yayın
        }
        return true;
    }

    /// <summary>NAT64 (64:ff9b::/96), IPv4-uyumlu (::/96) ve 6to4 (2002::/16) adreslerindeki IPv4.</summary>
    private static IPAddress? EmbeddedIPv4(byte[] b)
    {
        static bool Zero(byte[] x, int from, int to) { for (var i = from; i < to; i++) if (x[i] != 0) return false; return true; }
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && Zero(b, 4, 12))
            return new IPAddress(b[12..16]);
        if (Zero(b, 0, 12))
            return new IPAddress(b[12..16]);
        if (b[0] == 0x20 && b[1] == 0x02)
            return new IPAddress(b[2..6]);
        return null;
    }

    /// <summary>Host'u çözer; adreslerden biri bile yasaksa ya da hiç adres yoksa <c>null</c>.</summary>
    public static async Task<IPAddress?> ResolveAllowedAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
            addresses = [literal];
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct);
            }
            catch (SocketException)
            {
                return null;
            }
        }
        if (addresses.Length == 0 || addresses.Any(IsBlocked)) return null;
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
    }
}
