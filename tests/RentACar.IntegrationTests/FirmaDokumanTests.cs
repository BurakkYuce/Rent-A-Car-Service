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
/// (10 MB) ve PDF imzası (<c>%PDF-</c>) testte SABİT yazılır, servisten/koddan türetilmez. Sınır
/// bir gün 20'ye çıkarılırsa bu testler kırmızıya döner; istenen budur.</para>
///
/// <para>Karıştırma uyarısı: <c>PlatformBelge</c> (RentPro→firma dağıtımı, RLS'siz platform tablosu)
/// ile bu tablo AYRI kavramlardır; burada test edilen tenant-owned + RLS'li olandır.</para>
/// </summary>
[Collection("postgres")]
public sealed class FirmaDokumanTests(PostgresFixture fx)
{
    // ELLE kurulmuş asgari PDF: "%PDF-1.4" + gövde. Magic byte 0x25 0x50 0x44 0x46 0x2D.
    private static byte[] Pdf(string kuyruk = "\n1 0 obj\n<<>>\nendobj\n%%EOF\n")
        => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4" + kuyruk);

    // PDF OLMAYAN dosya: uzantısı ve Content-Type'ı ".pdf" olsa bile içerik yalan söylüyor.
    private static readonly byte[] PdfDegil = "MZ\0Bu bir PDF değil, çalıştırılabilir dosya."u8.ToArray();

    private static FirmaDokumanInput Girdi(string baslik, byte[]? bytes = null,
        string? aciklama = null, string dosyaAdi = "Boş Sözleşme.pdf")
        => new(baslik, aciklama, dosyaAdi, bytes ?? Pdf());

    private static FirmaDokumanService Svc(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<FirmaDokumanService>();

    [Fact]
    public async Task Yukle_listele_indir_ve_sil_uctan_uca()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "umit");
        var svc = Svc(scope);

        var icerik = Pdf("\n% teslim formu\n%%EOF\n");
        var id = await svc.YukleAsync(Girdi("Teslim Formu", icerik, aciklama: "Şubede elle doldurulur"));

        var liste = await svc.ListeleAsync();
        var satir = Assert.Single(liste);
        Assert.Equal(id, satir.Id);
        Assert.Equal("Teslim Formu", satir.Baslik);
        Assert.Equal("Şubede elle doldurulur", satir.Aciklama);
        Assert.Equal(icerik.LongLength, satir.Boyut);      // ELLE: kaydedilen bayt sayısı = dosyanın kendisi
        Assert.Equal(1, satir.Sira);                       // ELLE: ilk belge 1 numaralı yuvaya oturur
        Assert.Equal("umit", satir.YukleyenKullanici);
        // Dosya adı ASCII'ye slug'lanır (Content-Disposition başlığı ham ASCII bekler): "Boş Sözleşme.pdf".
        Assert.Equal("bos-sozlesme.pdf", satir.DosyaAdi);

        var indirilen = await svc.IndirAsync(id);
        Assert.NotNull(indirilen);
        Assert.Equal(icerik, indirilen!.Bytes);            // bayt-bayt AYNI dosya geri geliyor
        Assert.Equal("application/pdf", indirilen.ContentType);
        Assert.Equal("bos-sozlesme.pdf", indirilen.DosyaAdi);

        Assert.True(await svc.SilAsync(id));
        Assert.Empty(await svc.ListeleAsync());
        Assert.Null(await svc.IndirAsync(id));
        Assert.False(await svc.SilAsync(id));              // ikinci silme sessizce false (idempotent)
    }

    [Fact]
    public async Task Onbir_inci_yukleme_reddedilir_ve_silinen_yuva_yeniden_kullanilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant);
        var svc = Svc(scope);

        // ELLE: sınır 10. Onuncuya kadar sorunsuz.
        var idler = new List<Guid>();
        for (var i = 1; i <= 10; i++)
            idler.Add(await svc.YukleAsync(Girdi($"Belge {i}")));

        Assert.Equal(10, (await svc.ListeleAsync()).Count);

        // 11. yükleme REDDEDİLİR (UI'da formu gizlemek yetmez — bu servis kararı).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.YukleAsync(Girdi("Belge 11")));
        Assert.Contains("10", ex.Message);
        Assert.Equal(10, (await svc.ListeleAsync()).Count);

        // Ortadaki bir belge silinince yuvası SERBEST kalır → yeni yükleme o yuvaya oturur.
        // (Aksi halde 10 kez yükle-sil yapan firma bir daha belge ekleyemezdi.)
        var silinenSira = (await svc.ListeleAsync()).First(s => s.Baslik == "Belge 4").Sira;
        Assert.Equal(4, silinenSira); // ELLE: dördüncü yüklenen dördüncü yuvadadır
        Assert.True(await svc.SilAsync(idler[3]));

        var yeniId = await svc.YukleAsync(Girdi("Yerine Gelen"));
        var yeni = (await svc.ListeleAsync()).Single(s => s.Id == yeniId);
        Assert.Equal(4, yeni.Sira);
        Assert.Equal(10, (await svc.ListeleAsync()).Count);
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
            () => svc.YukleAsync(Girdi("Sahte PDF", PdfDegil, dosyaAdi: "sozlesme.pdf")));
        Assert.Equal("Yalnız PDF yüklenebilir.", ex.Message);

        // İmzanın SON baytı bozuksa da red: "%PDF" var ama "-" yok (0x2D).
        var eksikImza = System.Text.Encoding.ASCII.GetBytes("%PDF1.4 gövde");
        await Assert.ThrowsAsync<ValidationException>(() => svc.YukleAsync(Girdi("Eksik imza", eksikImza)));

        // Boş dosya da red.
        await Assert.ThrowsAsync<ValidationException>(() => svc.YukleAsync(Girdi("Boş", [])));

        Assert.Empty(await svc.ListeleAsync()); // hiçbiri kaydedilmedi
    }

    [Fact]
    public async Task Boyut_siniri_10_mb_ustu_reddedilir_altinda_kabul_edilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant);
        var svc = Svc(scope);

        // ELLE: sınır 10 MB = 10 * 1024 * 1024 = 10.485.760 bayt.
        const int onMb = 10 * 1024 * 1024;

        // Bir bayt FAZLASI → red (geçerli PDF imzasıyla; hata boyut hatası olmalı, "PDF değil" değil).
        var buyuk = new byte[onMb + 1];
        Pdf("").CopyTo(buyuk, 0);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.YukleAsync(Girdi("Çok büyük", buyuk)));
        Assert.Equal("Dosya en fazla 10 MB olabilir.", ex.Message);

        // TAM sınır (10.485.760 bayt) → KABUL (sınır dahil).
        var tamSinir = new byte[onMb];
        Pdf("").CopyTo(tamSinir, 0);
        var id = await svc.YukleAsync(Girdi("Tam sınır", tamSinir));
        Assert.Equal(onMb, (await svc.ListeleAsync()).Single().Boyut);

        var indirilen = await svc.IndirAsync(id);
        Assert.Equal(onMb, indirilen!.Bytes.Length);
    }

    [Fact]
    public async Task Yetki_operationswrite_olmayan_yukleyemez_ve_silemez_ama_indirebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        // Admin bir belge koyar.
        Guid id;
        using (var admin = host.ScopeFor(tenant))
            id = await Svc(admin).YukleAsync(Girdi("Ruhsat Örneği"));

        // Muhasebe rolünde OperationsWrite YOKTUR (matris: FinanceWrite + ViewReports).
        using var muhasebe = host.ScopeFor(tenant, Guid.NewGuid(), "muhasebeci", UserRole.Muhasebe);
        var svc = Svc(muhasebe);

        await Assert.ThrowsAsync<ValidationException>(() => svc.YukleAsync(Girdi("Muhasebenin belgesi")));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SilAsync(id));

        // Ama OKUMA/İNDİRME serbest: sahada çıktı alması gereken her personel erişebilmeli.
        Assert.Single(await svc.ListeleAsync());
        Assert.NotNull(await svc.IndirAsync(id));

        // Rolsüz (oturumsuz) bağlam yazamaz — guard "izin yok"ta kapanır.
        using var anonim = host.ScopeFor(tenant, role: null);
        await Assert.ThrowsAsync<ValidationException>(() => Svc(anonim).YukleAsync(Girdi("Anonim")));
    }

    [Fact]
    public async Task Tenant_izolasyonu_racar_app_ile()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Guid aId;
        using (var sa = host.ScopeFor(a))
            aId = await Svc(sa).YukleAsync(Girdi("A'nın gizli sözleşmesi", Pdf("\n% A gizli\n%%EOF\n")));

        using (var sb = host.ScopeFor(b))
        {
            var svc = Svc(sb);
            Assert.Empty(await svc.ListeleAsync());          // A'nın belgesi listede YOK
            Assert.Null(await svc.IndirAsync(aId));          // ID bilinse bile İNDİRİLEMEZ
            Assert.False(await svc.SilAsync(aId));           // silinemez de

            // B kendi belgesini yükleyince 1 numaralı yuvadan başlar (yuva tenant başına).
            await svc.YukleAsync(Girdi("B'nin belgesi"));
            Assert.Equal(1, Assert.Single(await svc.ListeleAsync()).Sira);
        }

        // A'nın belgesi hâlâ yerinde ve bozulmamış.
        using (var sa2 = host.ScopeFor(a))
            Assert.Equal("A'nın gizli sözleşmesi", Assert.Single(await Svc(sa2).ListeleAsync()).Baslik);

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
        await using (var sil = new NpgsqlCommand(
            "delete from \"FirmaDokumanlari\" where \"TenantId\" = @a", conn))
        {
            sil.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await sil.ExecuteNonQueryAsync()); // B'nin GUC'uyla A'nın satırı SİLİNEMEZ
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
