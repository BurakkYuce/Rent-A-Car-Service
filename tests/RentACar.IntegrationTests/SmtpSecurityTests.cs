using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.1b güvenlik M3/M4 — SMTP göndericisinin ağ güvenliği: iç ağ/metadata/loopback reddi (DNS çözümünden sonra),
/// yalnız SMTP portları, ham istisna metni kullanıcıya dönmez, TLS'siz bağlantıda AUTH yapılmaz.
/// </summary>
public sealed class SmtpSecurityTests
{
    private static SmtpAyar Settings(string host, int port, string? user = "kullanici", bool ssl = false)
        => new(host, port, ssl, user, user is null ? null : "gizli-parola", "gonderen@ornek.com", "Gönderen");

    private static EpostaMesaj Message() => new("alici@ornek.com", "konu", "<p>gövde</p>");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.5")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a9fe:a9fe")]   // NAT64 içinde 169.254.169.254
    [InlineData("::7f00:1")]             // IPv4-uyumlu 127.0.0.1
    [InlineData("2002:0a00:0001::1")]    // 6to4 içinde 10.0.0.1
    [InlineData("fec0::1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.0.8")]
    public void Private_and_link_local_addresses_are_blocked(string ip)
        => Assert.True(SmtpEndpointGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("64:ff9b::808:808")]     // NAT64 içinde 8.8.8.8 — genel adres
    public void Public_addresses_are_allowed(string ip)
        => Assert.False(SmtpEndpointGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("127.0.0.1", 587)]
    [InlineData("localhost", 587)]
    [InlineData("169.254.169.254", 25)]
    [InlineData("10.0.0.1", 465)]
    public async Task Internal_targets_get_the_generic_message_without_connecting(string host, int port)
    {
        var r = await new MailKitEmailSender(NullLogger<MailKitEmailSender>.Instance).SendAsync(Settings(host, port), Message());
        Assert.False(r.Ok);
        Assert.Equal(MailKitEmailSender.GenericFailure, r.Hata);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(80)]
    [InlineData(6379)]
    public async Task Non_smtp_ports_are_rejected(int port)
    {
        var r = await new MailKitEmailSender(NullLogger<MailKitEmailSender>.Instance).SendAsync(Settings("smtp.ornek.com", port), Message());
        Assert.False(r.Ok);
        Assert.Contains("25, 465, 587", r.Hata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credentials_are_never_sent_over_an_unencrypted_connection()
    {
        // Sahte SMTP sunucusu STARTTLS sunmaz; gönderici AUTH komutunu HİÇ göndermemeli.
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, 2525);
            listener.Start();
        }
        catch (SocketException)
        {
            return; // port meşgul (paralel koşu) — davranış başka koşuda kilitlenir
        }

        var received = new StringBuilder();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (received) received.AppendLine(line);
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-fake");
                    await writer.WriteLineAsync("250 AUTH PLAIN LOGIN");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else await writer.WriteLineAsync("250 ok");
            }
        });

        try
        {
            var sender = new MailKitEmailSender(NullLogger<MailKitEmailSender>.Instance,
                (_, _) => Task.FromResult<IPAddress?>(IPAddress.Loopback));
            var r = await sender.SendAsync(Settings("smtp.fake.test", 2525), Message());
            Assert.False(r.Ok);
            Assert.Contains("TLS", r.Hata, StringComparison.Ordinal);
            await Task.WhenAny(server, Task.Delay(5000));
            lock (received)
            {
                Assert.DoesNotContain("AUTH", received.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("gizli-parola", received.ToString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
