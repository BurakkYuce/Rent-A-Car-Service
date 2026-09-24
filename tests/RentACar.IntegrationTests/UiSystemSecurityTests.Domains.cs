using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Integrations;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    // M6 — özel alan adı yalnız kiracıya özel DNS TXT belirteciyle etkinleşir.
    [Fact]
    public async Task Custom_domain_activates_only_with_the_tenants_txt_token()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var host = "www." + Random("dm") + ".com";

        var added = await Json(await Send(admin, HttpMethod.Post, V1 + "/ayarlar/domainler", new { host }));
        var row = added.GetProperty("domainler").EnumerateArray().Single(d => d.GetProperty("host").GetString() == host);
        Assert.Equal("_racar-verify." + host, row.GetProperty("dogrulamaKaydi").GetString());
        var token = row.GetProperty("dogrulamaDegeri").GetString()!;

        // Kayıt yayınlanmamış → 400, kayıt beklemede kalır.
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/ayarlar/domainler/dogrula", new { host }),
            HttpStatusCode.BadRequest, "dogrulama", "host");
        // Yanlış belirteç → 400.
        FakeDnsTxtResolver.Instance.Publish("_racar-verify." + host, "racar-baska-bir-belirtec");
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/ayarlar/domainler/dogrula", new { host }),
            HttpStatusCode.BadRequest, "dogrulama", "host");
        Assert.Equal(TenantDomainStatus.PendingVerification, await StatusAsync(host));

        FakeDnsTxtResolver.Instance.Publish("_racar-verify." + host, "v=spf1 -all", "\"" + token + "\"");
        var verified = await Json(await Send(admin, HttpMethod.Post, V1 + "/ayarlar/domainler/dogrula", new { host }));
        Assert.Equal(TenantDomainStatus.Active, await StatusAsync(host));
        var after = verified.GetProperty("domainler").EnumerateArray().Single(d => d.GetProperty("host").GetString() == host);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, after.GetProperty("dogrulamaDegeri").ValueKind);
    }

    [Fact]
    public async Task Expired_pending_domain_cannot_be_verified()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var host = "www." + Random("ex") + ".com";
        var added = await Json(await Send(admin, HttpMethod.Post, V1 + "/ayarlar/domainler", new { host }));
        var token = added.GetProperty("domainler").EnumerateArray().Single(d => d.GetProperty("host").GetString() == host)
            .GetProperty("dogrulamaDegeri").GetString()!;
        await using (var owner = OwnerDb())
            await owner.TenantDomains.Where(d => d.Host == host)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedAtUtc, DateTimeOffset.UtcNow.AddDays(-8)));
        FakeDnsTxtResolver.Instance.Publish("_racar-verify." + host, token);
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/ayarlar/domainler/dogrula", new { host }),
            HttpStatusCode.BadRequest, "dogrulama", "host");
        Assert.Equal(TenantDomainStatus.PendingVerification, await StatusAsync(host));
    }

    [Fact]
    public void Dns_txt_response_is_parsed_including_compression_and_multiple_strings()
    {
        var query = UdpDnsTxtResolver.BuildQuery(0x1234, "_racar-verify.ornek.com");
        // Yanıt = sorgu başlığı (QR + 1 cevap) + soru + cevap (ad sıkıştırma işaretçisi 0xC00C, TXT, iki dize).
        var response = new List<byte>(query);
        response[2] = 0x81; response[3] = 0x80; response[7] = 1;
        byte[] part1 = "racar-ab"u8.ToArray(), part2 = "cd"u8.ToArray();
        var rdata = new List<byte> { (byte)part1.Length };
        rdata.AddRange(part1);
        rdata.Add((byte)part2.Length);
        rdata.AddRange(part2);
        response.AddRange([0xC0, 0x0C, 0, 16, 0, 1, 0, 0, 0, 60, 0, (byte)rdata.Count]);
        response.AddRange(rdata);

        Assert.Equal(["racar-abcd"], UdpDnsTxtResolver.ParseTxt(response.ToArray(), query));
        // Başka sorgunun yanıtı (kimlik farklı), başka ad için soru, QR bayraksız ve hata RCODE'lu yanıt → boş.
        Assert.Empty(UdpDnsTxtResolver.ParseTxt(response.ToArray(), UdpDnsTxtResolver.BuildQuery(0x9999, "_racar-verify.ornek.com")));
        Assert.Empty(UdpDnsTxtResolver.ParseTxt(response.ToArray(), UdpDnsTxtResolver.BuildQuery(0x1234, "_racar-verify.baska.com")));
        var noQr = response.ToArray(); noQr[2] = 0x01;
        Assert.Empty(UdpDnsTxtResolver.ParseTxt(noQr, query));
        var nxDomain = response.ToArray(); nxDomain[3] = 0x83;
        Assert.Empty(UdpDnsTxtResolver.ParseTxt(nxDomain, query));
    }

    private AppDbContext OwnerDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    private async Task<TenantDomainStatus> StatusAsync(string host)
    {
        await using var owner = OwnerDb();
        return await owner.TenantDomains.AsNoTracking().Where(d => d.Host == host).Select(d => d.Status).SingleAsync();
    }
}
