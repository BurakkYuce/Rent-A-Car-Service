using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.FirmaDokumanlar;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Firmanın KENDİ yüklediği PDF dokümanları (/dokumanlar).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler ELLE kurulur — sınır sayısı (10), boyut eşiği
/// (3 MB) ve PDF imzası (<c>%PDF-</c>) testte SABİT yazılır, servisten/koddan türetilmez. Sınır
/// bir gün 20'ye çıkarılırsa bu testler kırmızıya döner; istenen budur.</para>
///
/// <para>Karıştırma uyarısı: <c>PlatformBelge</c> (RentPro→firma dağıtımı, RLS'siz platform tablosu)
/// ile bu tablo AYRI kavramlardır; burada test edilen tenant-owned + RLS'li olandır.</para>
/// </summary>
[Collection("postgres")]
public sealed class FirmaDokumanTests(PostgresFixture fx)
{
    // ELLE kurulmuş asgari PDF: "%PDF-1.4" + gövde. Magic byte 0x25 0x50 0x44 0x46 0x2D.
    private static byte[] Pdf(string queue = "\n1 0 obj\n<<>>\nendobj\n%%EOF\n")
        => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4" + queue);

    // PDF OLMAYAN dosya: uzantısı ve Content-Type'ı ".pdf" olsa bile içerik yalan söylüyor.
    private static readonly byte[] NotPdf = "MZ\0Bu bir PDF değil, çalıştırılabilir dosya."u8.ToArray();

    private static FirmaDokumanInput Input(string title, byte[]? bytes = null,
        string? description = null, string fileName = "Boş Sözleşme.pdf")
        => new(title, description, fileName, bytes ?? Pdf());

    private static CompanyFileService Svc(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CompanyFileService>();

    [Fact]
    public async Task Yukle_listele_indir_ve_sil_uctan_uca()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "umit");
        var svc = Svc(scope);

        var content = Pdf("\n% teslim formu\n%%EOF\n");
        var id = await svc.UploadAsync(Input("Teslim Formu", content, description: "Şubede elle doldurulur"));

        var list = await svc.ListAsync();
        var row = Assert.Single(list);
        Assert.Equal(id, row.Id);
        Assert.Equal("Teslim Formu", row.Baslik);
        Assert.Equal("Şubede elle doldurulur", row.Aciklama);
        Assert.Equal(content.LongLength, row.Boyut);      // ELLE: kaydedilen bayt sayısı = dosyanın kendisi
        Assert.Equal(1, row.Sira);                       // ELLE: ilk belge 1 numaralı yuvaya oturur
        Assert.Equal("umit", row.YukleyenKullanici);
        // Dosya adı ASCII'ye slug'lanır (Content-Disposition başlığı ham ASCII bekler): "Boş Sözleşme.pdf".
        Assert.Equal("bos-sozlesme.pdf", row.DosyaAdi);

        var downloaded = await svc.DownloadAsync(id);
        Assert.NotNull(downloaded);
        Assert.Equal(content, downloaded!.Bytes);            // bayt-bayt AYNI dosya geri geliyor
        Assert.Equal("application/pdf", downloaded.ContentType);
        Assert.Equal("bos-sozlesme.pdf", downloaded.DosyaAdi);

        Assert.True(await svc.DeleteAsync(id));
        Assert.Empty(await svc.ListAsync());
        Assert.Null(await svc.DownloadAsync(id));
        Assert.False(await svc.DeleteAsync(id));              // ikinci silme sessizce false (idempotent)
    }

    [Fact]
    public async Task Onbir_inci_yukleme_reddedilir_ve_silinen_yuva_yeniden_kullanilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant);
        var svc = Svc(scope);

        // ELLE: sınır 10. Onuncuya kadar sorunsuz.
        var ids = new List<Guid>();
        for (var i = 1; i <= 10; i++)
            ids.Add(await svc.UploadAsync(Input($"Belge {i}")));

        Assert.Equal(10, (await svc.ListAsync()).Count);

        // 11. yükleme REDDEDİLİR (UI'da formu gizlemek yetmez — bu servis kararı).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(Input("Belge 11")));
        Assert.Contains("10", ex.Message);
        Assert.Equal(10, (await svc.ListAsync()).Count);

        // Ortadaki bir belge silinince yuvası SERBEST kalır → yeni yükleme o yuvaya oturur.
        // (Aksi halde 10 kez yükle-sil yapan firma bir daha belge ekleyemezdi.)
        var deletedOrder = (await svc.ListAsync()).First(s => s.Baslik == "Belge 4").Sira;
        Assert.Equal(4, deletedOrder); // ELLE: dördüncü yüklenen dördüncü yuvadadır
        Assert.True(await svc.DeleteAsync(ids[3]));

        var newId = await svc.UploadAsync(Input("Yerine Gelen"));
        var newItem = (await svc.ListAsync()).Single(s => s.Id == newId);
        Assert.Equal(4, newItem.Sira);
        Assert.Equal(10, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Pdf_olmayan_dosya_magic_byte_ile_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant);
        var svc = Svc(scope);

        // Uzantı ".pdf", ama İÇERİK PDF değil → red. Uzantı ve Content-Type İSTEMCİDEN gelir.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.UploadAsync(Input("Sahte PDF", NotPdf, fileName: "sozlesme.pdf")));
        Assert.Equal("Yalnız PDF yüklenebilir.", ex.Message);

        // İmzanın SON baytı bozuksa da red: "%PDF" var ama "-" yok (0x2D).
        var missingSignature = System.Text.Encoding.ASCII.GetBytes("%PDF1.4 gövde");
        await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(Input("Eksik imza", missingSignature)));

        // Boş dosya da red.
        await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(Input("Boş", [])));

        Assert.Empty(await svc.ListAsync()); // hiçbiri kaydedilmedi
    }

    [Fact]
    public async Task Boyut_siniri_3_mb_ustu_reddedilir_altinda_kabul_edilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant);
        var svc = Svc(scope);

        // ELLE: sınır 3 MB = 3 * 1024 * 1024 = 3.145.728 bayt (kullanıcı kararı, 2026-08-17).
        const int threeMb = 3 * 1024 * 1024;

        // Bir bayt FAZLASI → red (geçerli PDF imzasıyla; hata boyut hatası olmalı, "PDF değil" değil).
        var large = new byte[threeMb + 1];
        Pdf("").CopyTo(large, 0);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(Input("Çok büyük", large)));
        Assert.Equal("Dosya en fazla 3 MB olabilir.", ex.Message);

        // TAM sınır (3.145.728 bayt) → KABUL (sınır dahil).
        var fullLimit = new byte[threeMb];
        Pdf("").CopyTo(fullLimit, 0);
        var id = await svc.UploadAsync(Input("Tam sınır", fullLimit));
        Assert.Equal(threeMb, (await svc.ListAsync()).Single().Boyut);

        var downloaded = await svc.DownloadAsync(id);
        Assert.Equal(threeMb, downloaded!.Bytes.Length);
    }

    [Fact]
    public async Task Yetki_operationswrite_olmayan_yukleyemez_ve_silemez_ama_indirebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        // Admin bir belge koyar.
        Guid id;
        using (var admin = host.ScopeFor(tenant))
            id = await Svc(admin).UploadAsync(Input("Ruhsat Örneği"));

        // Muhasebe rolünde OperationsWrite YOKTUR (matris: FinanceWrite + ViewReports).
        using var accounting = host.ScopeFor(tenant, Guid.NewGuid(), "muhasebeci", UserRole.Muhasebe);
        var svc = Svc(accounting);

        await Assert.ThrowsAsync<NoPermissionException>(() => svc.UploadAsync(Input("Muhasebenin belgesi")));
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.DeleteAsync(id));

        // Ama OKUMA/İNDİRME serbest: sahada çıktı alması gereken her personel erişebilmeli.
        Assert.Single(await svc.ListAsync());
        Assert.NotNull(await svc.DownloadAsync(id));

        // Rolsüz (oturumsuz) bağlam yazamaz — guard "izin yok"ta kapanır.
        using var anonymous = host.ScopeFor(tenant, role: null);
        await Assert.ThrowsAsync<NoPermissionException>(() => Svc(anonymous).UploadAsync(Input("Anonim")));
    }

    [Fact]
    public async Task Tenant_izolasyonu_racar_app_ile()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Guid aId;
        using (var sa = host.ScopeFor(a))
            aId = await Svc(sa).UploadAsync(Input("A'nın gizli sözleşmesi", Pdf("\n% A gizli\n%%EOF\n")));

        using (var sb = host.ScopeFor(b))
        {
            var svc = Svc(sb);
            Assert.Empty(await svc.ListAsync());          // A'nın belgesi listede YOK
            Assert.Null(await svc.DownloadAsync(aId));          // ID bilinse bile İNDİRİLEMEZ
            Assert.False(await svc.DeleteAsync(aId));           // silinemez de

            // B kendi belgesini yükleyince 1 numaralı yuvadan başlar (yuva tenant başına).
            await svc.UploadAsync(Input("B'nin belgesi"));
            Assert.Equal(1, Assert.Single(await svc.ListAsync()).Sira);
        }

        // A'nın belgesi hâlâ yerinde ve bozulmamış.
        using (var sa2 = host.ScopeFor(a))
            Assert.Equal("A'nın gizli sözleşmesi", Assert.Single(await Svc(sa2).ListAsync()).Baslik);

        // ---- HAM RLS (racar_app, NOBYPASSRLS): uygulama katmanı devre dışıyken de izolasyon var mı? ----
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();

        await using (var kat = new NpgsqlCommand(
            "select relrowsecurity, relforcerowsecurity from pg_class where relname = 'FirmaDokumanlari'", conn))
        await using (var r = await kat.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            Assert.True(r.GetBoolean(0), "ENABLE ROW LEVEL SECURITY yok");
            Assert.True(r.GetBoolean(1), "FORCE ROW LEVEL SECURITY yok");
        }

        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn))
        {
            set.Parameters.AddWithValue("t", b.ToString());
            await set.ExecuteScalarAsync();
        }
        await using (var say = new NpgsqlCommand(
            "select count(*) from \"FirmaDokumanlari\" where \"TenantId\" = @a", conn))
        {
            say.Parameters.AddWithValue("a", a);
            Assert.Equal(0L, (long)(await say.ExecuteScalarAsync())!);
        }
        await using (var remove = new NpgsqlCommand(
            "delete from \"FirmaDokumanlari\" where \"TenantId\" = @a", conn))
        {
            remove.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await remove.ExecuteNonQueryAsync()); // B'nin GUC'uyla A'nın satırı SİLİNEMEZ
        }
    }

    [Fact]
    public async Task Yuva_araligi_veritabaninda_da_kapali_11_satir_yazilamaz()
    {
        // Sınır yalnız serviste değil, DB'de de dayatılıyor mu? Servis atlanıp doğrudan SQL ile
        // 11. yuva yazılmaya çalışılır — CHECK reddetmeli. (Yarış durumunda sayımın atlanması
        // hâlinde son savunma budur.)
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn))
        {
            set.Parameters.AddWithValue("t", tenant.ToString());
            await set.ExecuteScalarAsync();
        }

        await using var ins = new NpgsqlCommand(
            "insert into \"FirmaDokumanlari\" (\"Id\",\"TenantId\",\"Baslik\",\"DosyaAdi\",\"Bytes\"," +
            "\"Boyut\",\"ContentType\",\"Sira\",\"CreatedAtUtc\") " +
            "values (@id, @t, 'kacak', 'kacak.pdf', @b, 8, 'application/pdf', 11, now())", conn);
        ins.Parameters.AddWithValue("id", Guid.NewGuid());
        ins.Parameters.AddWithValue("t", tenant);
        ins.Parameters.AddWithValue("b", Pdf(""));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ins.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }
}
