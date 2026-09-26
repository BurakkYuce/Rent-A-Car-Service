using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-3: araç fotoğraf galerisi. BAĞIMSIZ ORACLE — beklenen değerler elle kurulmuş senaryodan
/// (magic-byte sabitleri, EXIF fixture'ın bilinen boyutu) türetilir, servis kodundan değil.
/// </summary>
[Collection("postgres")]
public sealed class VehiclePhotoTests(PostgresFixture fx)
{
    // 10x8 düz renkli minik gerçek görseller (Pillow ile üretildi) — magic-byte + gerçek decode ikisi de geçerli.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAAIAAoDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDk6KKK8I/Vj//Z");
    private static readonly byte[] TinyWebP = Convert.FromBase64String(
        "UklGRjoAAABXRUJQVlA4IC4AAACwAQCdASoKAAgAAUAmJaACdLoABDAAAP7vUS/xbSOhTIf/cHH/YOP+wcfumAAA");
    private static readonly byte[] NotAnImage = "bu bir görsel değil, düz metin"u8.ToArray();

    private static async Task<Guid> SeedVehicleAsync(TestHost host, Guid tenantId)
    {
        using var scope = host.ScopeFor(tenantId);
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        return await vehicles.CreateAsync(new VehicleInput
        { Plaka = "34PH" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), Durum = VehicleStatus.Musait });
    }

    [Fact]
    public async Task Yukleme_sira_atar_ikinci_yukleme_bir_artirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        await svc.AddAsync(vehicleId, TinyPng);
        await svc.AddAsync(vehicleId, TinyJpeg);

        var list = await svc.ListMetaAsync(vehicleId);
        Assert.Equal(2, list.Count);
        Assert.Equal(0, list[0].Sira);
        Assert.Equal(1, list[1].Sira);
        Assert.Equal("image/png", list[0].ContentType);
        Assert.Equal("image/jpeg", list[1].ContentType);
    }

    [Fact]
    public async Task Gecersiz_magic_byte_reddedilir_mesaj_formatlari_belirtir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.AddAsync(vehicleId, NotAnImage));
        Assert.Contains("PNG", ex.Message);
        Assert.Contains("JPEG", ex.Message);
        Assert.Contains("WebP", ex.Message);
    }

    [Fact]
    public async Task Gecerli_webp_kabul_edilir_logo_yolu_webp_kabul_etmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        await svc.AddAsync(vehicleId, TinyWebP);

        var list = await svc.ListMetaAsync(vehicleId);
        Assert.Single(list);
        Assert.Equal("image/webp", list[0].ContentType);

        // Regresyon: logo yolu (TenantSettingsService.SetLogoAsync üzerinden GorselMi→ImageValidation
        // birleştirmesi) hâlâ yalnız Png/Jpeg kabul eder — WebP'ye genişletilmedi (QuestPDF riski).
        Assert.Equal(ImageKind.WebP, ImageValidation.Detect(TinyWebP));
        Assert.DoesNotContain(ImageValidation.Detect(TinyWebP), new[] { ImageKind.Png, ImageKind.Jpeg });
    }

    [Fact]
    public async Task Silme_sonrasi_bosluk_kabul_move_deger_bazli_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        await svc.AddAsync(vehicleId, TinyPng);   // Sira 0
        await svc.AddAsync(vehicleId, TinyJpeg);  // Sira 1
        await svc.AddAsync(vehicleId, TinyWebP);  // Sira 2

        var beforeDelete = await svc.ListMetaAsync(vehicleId);
        await svc.DeleteAsync(vehicleId, beforeDelete[1].Id); // ortadakini sil → 0, 2 kalır (boşluk)

        var afterDelete = await svc.ListMetaAsync(vehicleId);
        Assert.Equal(2, afterDelete.Count);
        Assert.Equal(0, afterDelete[0].Sira);
        Assert.Equal(2, afterDelete[1].Sira); // boşluk KORUNUR, yeniden numaralandırılmaz

        // MoveAsync DEĞER-bazlı komşu bulmalı (index değil) — Sira 0 ve 2 arasında boşluk olmasına rağmen
        // "aşağı" ilk satırı ikinci satırla (Sira farkı 2 olsa da) doğru takas etmeli.
        var firstId = afterDelete[0].Id;
        await svc.MoveAsync(vehicleId, firstId, +1);
        var afterMove = await svc.ListMetaAsync(vehicleId);
        Assert.Equal(firstId, afterMove[1].Id); // artık ikinci sırada
        Assert.Equal(0, afterMove[0].Sira);
        Assert.Equal(2, afterMove[1].Sira);

        // Sınırda no-op: son fotoğrafı aşağı taşımaya çalış — hata fırlatmamalı, sıra değişmemeli.
        var lastId = afterMove[1].Id;
        await svc.MoveAsync(vehicleId, lastId, +1);
        var afterNoop = await svc.ListMetaAsync(vehicleId);
        Assert.Equal(lastId, afterNoop[1].Id);
    }

    [Fact]
    public async Task Yirmi_bir_fotograf_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        for (var i = 0; i < 20; i++) await svc.AddAsync(vehicleId, TinyPng);

        await Assert.ThrowsAsync<ValidationException>(() => svc.AddAsync(vehicleId, TinyPng));
    }

    [Fact]
    public async Task Buyuk_dosya_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();

        // PNG magic-byte + 2 MB'ı aşan dolgu — Detect() ilk 4 byte'a bakar, boyut kontrolü ayrı.
        var big = new byte[2 * 1024 * 1024 + 1];
        TinyPng.CopyTo(big, 0);
        await Assert.ThrowsAsync<ValidationException>(() => svc.AddAsync(vehicleId, big));
    }

    [Fact]
    public async Task Thumbnail_uretilir_exif_oryantasyonu_dogru_uygulanir()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "exif-rot90.jpg");
        var exifBytes = await File.ReadAllBytesAsync(fixturePath);

        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        await svc.AddAsync(vehicleId, exifBytes);

        var list = await svc.ListMetaAsync(vehicleId);
        var thumb = await svc.GetThumbAsync(vehicleId, list[0].Id);
        Assert.NotNull(thumb);
        Assert.Equal("image/jpeg", thumb!.ContentType);

        // BAĞIMSIZ ORACLE: fixture 800×600 landscape + EXIF Orientation=6 (RightTop, 90° dikey çekim).
        // Oryantasyon uygulanınca efektif 600×800 portrait; maxEdge=400 ile uzun kenar 800→400 ölçeklenir
        // → beklenen thumb TAM OLARAK 300×400 (elle hesaplandı, koddan değil).
        var (w, h) = ReadJpegDimensions(thumb.Bytes);
        Assert.Equal(300, w);
        Assert.Equal(400, h);
    }

    [Fact]
    public async Task Kapak_en_kucuk_siradir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        await svc.AddAsync(vehicleId, TinyPng);
        await svc.AddAsync(vehicleId, TinyJpeg);

        var list = await svc.ListMetaAsync(vehicleId);
        var cover = list.MinBy(p => p.Sira)!;
        Assert.Equal(list[0].Id, cover.Id); // ListMetaAsync zaten Sira'ya göre sıralı döner
    }

    [Fact]
    public async Task Arac_silinince_fotograflari_cascade_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        using (var scope = host.ScopeFor(tenantId))
        {
            var svc = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
            await svc.AddAsync(vehicleId, TinyPng);
            var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
            await vehicles.DeleteAsync(vehicleId);
        }

        // Owner-context ile RLS'i atlayıp ham satır sayımı — yetim bytea kalmadığını doğrula.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var count = await db.VehiclePhotos.IgnoreQueryFilters().CountAsync(p => p.VehicleId == vehicleId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Okuma_operationswrite_gerektirmez_operator_de_erisebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        Guid vehicleId;
        Guid photoId;
        using (var adminScope = host.ScopeFor(tenantId, role: UserRole.Admin))
        {
            var vehicles = adminScope.ServiceProvider.GetRequiredService<VehicleService>();
            vehicleId = await vehicles.CreateAsync(new VehicleInput { Plaka = "34OP" + Guid.NewGuid().ToString("N")[..4], Durum = VehicleStatus.Musait });
            var svc = adminScope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
            await svc.AddAsync(vehicleId, TinyPng);
            photoId = (await svc.ListMetaAsync(vehicleId))[0].Id;
        }

        // Operatör (OperationsWrite'a SAHİP OLMAYAN bir rol varsayımıyla değil — asıl nokta: okuma
        // servisinde PermissionGuard.Require ÇAĞRILMIYOR, yalnız BranchScope kontrol ediliyor).
        using var opScope = host.ScopeFor(tenantId, role: UserRole.Operator);
        var readSvc = opScope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var content = await readSvc.GetBytesAsync(vehicleId, photoId);
        Assert.NotNull(content);
    }

    private static (int Width, int Height) ReadJpegDimensions(byte[] jpeg)
    {
        // Minimal JPEG SOF marker tarayıcı (bağımsız oracle — SkiaSharp'a değil ham JPEG yapısına bakar).
        for (var i = 2; i < jpeg.Length - 9;)
        {
            if (jpeg[i] != 0xFF) { i++; continue; }
            var marker = jpeg[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = (jpeg[i + 5] << 8) | jpeg[i + 6];
                var width = (jpeg[i + 7] << 8) | jpeg[i + 8];
                return (width, height);
            }
            var len = (jpeg[i + 2] << 8) | jpeg[i + 3];
            i += 2 + len;
        }
        throw new InvalidOperationException("SOF marker bulunamadı.");
    }
}
