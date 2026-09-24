using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RentACar.Application.TenantSettings;

namespace RentACar.Infrastructure.Integrations;

/// <summary>
/// F11.1b güvenlik M6 — asgari DNS TXT çözücü (UDP, RFC 1035). .NET yerleşik TXT sorgusu sunmaz; ek paket yerine
/// ~100 satırlık bağımlılıksız istemci. Sunucu: <c>Dns:TxtResolver</c> yapılandırması, yoksa işletim sisteminin DNS
/// sunucusu, o da yoksa 1.1.1.1. Hata/zaman aşımı → boş liste (istisna sızmaz; doğrulama "bulunamadı" der).
/// </summary>
public sealed class UdpDnsTxtResolver(ILogger<UdpDnsTxtResolver> log, IConfiguration? config = null) : IDnsTxtResolver
{
    private const ushort TypeTxt = 16;

    public async Task<IReadOnlyList<string>> ResolveTxtAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var server = Server();
            // Kriptografik sorgu kimliği + kaynak adres + soru eşleşmesi: sahte (spoof) yanıtla doğrulama geçilmesin.
            var id = (ushort)System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 65536);
            using var udp = new UdpClient(server.AddressFamily);
            var query = BuildQuery(id, name);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var endpoint = new IPEndPoint(server, 53);
            await udp.SendAsync(query, endpoint, timeout.Token);
            while (true)
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                if (!response.RemoteEndPoint.Equals(endpoint)) continue; // başka kaynaktan gelen paket yok sayılır
                return ParseTxt(response.Buffer, query);
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IndexOutOfRangeException or ArgumentException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogWarning(ex, "DNS TXT sorgusu başarısız ({Name}).", name);
            return [];
        }
    }

    private IPAddress Server()
    {
        if (IPAddress.TryParse(config?["Dns:TxtResolver"], out var configured)) return configured;
        var system = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().DnsAddresses)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        return system ?? IPAddress.Parse("1.1.1.1");
    }

    /// <summary>Tek sorulu, özyinelemeli (RD) TXT sorgusu.</summary>
    public static byte[] BuildQuery(ushort id, string name)
    {
        var buf = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("Geçersiz DNS etiketi.", nameof(name));
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }
        buf.AddRange([0, (byte)(TypeTxt >> 8), (byte)TypeTxt, 0, 1]); // kök etiketi + QTYPE=TXT + QCLASS=IN
        return buf.ToArray();
    }

    /// <summary>
    /// Yanıttaki TXT kayıtları (her kaydın karakter dizeleri birleştirilir). Kimlik uyuşmazsa, yanıt bayrağı (QR)
    /// yoksa, RCODE hata ise ya da soru bölümü gönderilen soruyla aynı değilse boş.
    /// </summary>
    public static IReadOnlyList<string> ParseTxt(byte[] msg, byte[] query)
    {
        if (msg.Length < 12 || msg[0] != query[0] || msg[1] != query[1]) return [];
        if ((msg[2] & 0x80) == 0 || (msg[3] & 0x0F) != 0) return [];
        var qd = (msg[4] << 8) | msg[5];
        var an = (msg[6] << 8) | msg[7];
        var questionLength = query.Length - 12;
        if (qd != 1 || msg.Length < 12 + questionLength
            || !msg.AsSpan(12, questionLength).SequenceEqual(query.AsSpan(12, questionLength))) return [];
        var pos = 12 + questionLength;
        var result = new List<string>();
        for (var i = 0; i < an; i++)
        {
            pos = SkipName(msg, pos);
            var type = (msg[pos] << 8) | msg[pos + 1];
            var rdLength = (msg[pos + 8] << 8) | msg[pos + 9];
            var rd = pos + 10;
            if (type == TypeTxt)
            {
                var sb = new StringBuilder();
                var p = rd;
                while (p < rd + rdLength)
                {
                    var len = msg[p];
                    sb.Append(Encoding.UTF8.GetString(msg, p + 1, len));
                    p += 1 + len;
                }
                result.Add(sb.ToString());
            }
            pos = rd + rdLength;
        }
        return result;
    }

    private static int SkipName(byte[] msg, int pos)
    {
        while (true)
        {
            var len = msg[pos];
            if (len == 0) return pos + 1;
            if ((len & 0xC0) == 0xC0) return pos + 2; // sıkıştırma işaretçisi
            pos += 1 + len;
        }
    }
}
