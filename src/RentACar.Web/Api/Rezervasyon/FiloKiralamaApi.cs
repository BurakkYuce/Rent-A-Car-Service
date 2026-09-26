using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.FiloKiralamalar;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary>
/// <c>/api/ui/v1/filo-kiralama/*</c> — uzun dönem filo kiralama (F5.1). İş mantığı <see cref="FleetRentalService"/>'te;
/// DEFTER YAZMAZ (taksit planı salt-hesap). İzin OperationsWrite; iptal ayrıca OperationsDelete (Blazor dar izni).
/// <para><b>Şube kapsamı ARACIN şubesinden</b> (F5.1 adversarial M3): sözleşmenin kendi şube alanı yok; liste aracın
/// şubesiyle süzülür, tekil uçlar (detay/künye/tamamla/iptal) başka şubenin sözleşmesinde durumdan ÖNCE 403
/// <c>yetki_yok</c>, oluşturmada başka şubenin aracı 403. Blazor yolu değişmedi (servisin kapsamlı yolları yalnız burada).</para>
/// <para>Künye PUT'u tam değiştirmedir: zorunlu <c>surum</c>, uyuşmazlık 409 <c>cakisma</c>. Para/süre alanları künye
/// tipinde YOK (plan bu yoldan değişemez).</para>
/// </summary>
public static class FiloKiralamaApi
{
    private const string Kok = UiApiExtensions.V1 + "/filo-kiralama";

    public static RouteGroupBuilder MapFiloKiralamaApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/filo-kiralama").WithTags("Filo Kiralama").RequirePermission(Permission.OperationsWrite);
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/{id:guid}", Detay);
        g.MapPost("", Olustur).AlanlariEsle(YazmaKurallari);
        g.MapPut("/{id:guid}/kunye", Kunye).AlanlariEsle(YazmaKurallari);
        g.MapPost("/{id:guid}/tamamla", Tamamla);
        g.MapPost("/{id:guid}/iptal", Iptal).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Sözleşme bulunamadı.");

    private static readonly SortFieldMap<FiloListeSatiri> Harita = SortFieldMap<FiloListeSatiri>
        .Create(k => k.Id)
        .Alan("no", k => k.No).Alan("musteri", k => k.MusteriAd).Alan("plaka", k => k.Plaka)
        .Alan("basTar", k => k.BasTar).Alan("sureAy", k => k.SureAy).Alan("aylikUcret", k => k.AylikUcret)
        .Alan("genelToplam", k => k.GenelToplam).Alan("durum", k => k.Durum);

    private static async Task<Ok<Sayfa<FiloListeSatiri>>> Liste(
        FleetRentalService filo, IDbContextFactory<AppDbContext> dbf, Guid? musteriId, string? plaka, string? ara,
        string? durum, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var liste = await filo.ListScopedAsync(new FiloKiralamaFilter
        {
            MusteriId = musteriId, Plaka = F5Ortak.Nz(plaka), Ara = F5Ortak.Nz(ara),
            Durum = F5Ortak.EnumAdi<FleetRentalStatus>(durum, "durum"), Bas = min, Bit = max,
        }, ct);
        var cariler = await F5Ortak.CarilerAsync(dbf, liste.Select(k => k.MusteriId), ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, liste.Select(k => k.VehicleId), ct);
        var satirlar = liste.Select(k => new FiloListeSatiri(k.Id, k.No, k.SozlesmeNo, k.MusteriId,
            F5Ortak.CariAdi(cariler, k.MusteriId), k.VehicleId, F5Ortak.Plaka(plakalar, k.VehicleId), k.BasTar, k.SureAy,
            k.AylikUcret, FleetRentalService.InstallmentPlan(k).GenelToplam, k.Currency, k.SatisTemsilcisi, k.Kaynak,
            k.VadeGun, k.Durum.ToString())).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(satirlar, Harita, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Detay(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
        => await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    private static async Task<FiloKiralamaDto?> DtoAsync(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        var surum = await depo.VersionAsync(id, ct); // alanlardan ÖNCE
        var k = await filo.GetComprehensiveAsync(id, ct); // kapsam dışı → 403 (içerik sızmaz)
        if (k is null) return null;
        var cariler = await F5Ortak.CarilerAsync(dbf, [k.MusteriId], ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, [k.VehicleId], ct);
        var aktif = k.Durum == FleetRentalStatus.Aktif;
        var y = new FiloYetkileri(aktif, aktif, aktif && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete));
        return FiloKiralamaDto.From(k, surum, F5Ortak.CariAdi(cariler, k.MusteriId), F5Ortak.Plaka(plakalar, k.VehicleId), y);
    }

    private static readonly (string, string)[] YazmaKurallari =
    [
        ("Müşteri seçilmelidir", "musteriId"), ("Araç seçilmelidir", "vehicleId"),
        ("Süre (ay)", "sureAy"), ("Aylık ücret", "aylikUcret"), ("KDV oranı", "kdvOrani"), ("Kur pozitif", "kur"),
        ("Damga vergisi", "damgaVergisi"), ("Vade günü", "vadeGun"), ("Kilometre negatif", "cikisKm"),
        ("KM limiti", "toplamKmLimiti"), ("Toplam KM", "toplamKm"),
    ];

    /// <summary>varchar uzunlukları (FiloKiralamaConfig) — 22001 yerine 400 + alan.</summary>
    private static void Metinler(string? satis, string? fatura, string? makbuz, string? dosya, string? sozNo,
        string? fiyat, string? kaynak, string? aciklama)
    {
        Sinirlar.Metin(satis, 128, "satisTemsilcisi", "Satış temsilcisi");
        Sinirlar.Metin(fatura, 32, "faturaTuru", "Fatura türü");
        Sinirlar.Metin(makbuz, 32, "makbuzNo", "Makbuz no");
        Sinirlar.Metin(dosya, 32, "dosyaNo", "Dosya no");
        Sinirlar.Metin(sozNo, 32, "sozlesmeNo", "Sözleşme no");
        Sinirlar.Metin(fiyat, 32, "fiyatTuru", "Fiyat türü");
        Sinirlar.Metin(kaynak, 64, "kaynak", "Kaynak");
        Sinirlar.Metin(aciklama, 512, "aciklama", "Açıklama");
    }

    /// <summary>Kur üst sınırı (numeric(19,6) → 13 tam hane; makul tavan).</summary>
    public const decimal EnFazlaKur = 1_000_000m;

    private static async Task<Created<FiloOlusturYaniti>> Olustur(
        FiloKiralamaIstegi i, FleetRentalService filo, ICustomerRepository musteriler, IVehicleRepository araclar,
        CancellationToken ct)
    {
        Sinirlar.Tutar(i.AylikUcret, "aylikUcret", "Aylık ücret");
        Sinirlar.Tutar(i.DamgaVergisi, "damgaVergisi", "Damga vergisi");
        if (i.Kur is { } kr && kr > EnFazlaKur)
            throw new ValidationException($"Kur en fazla {EnFazlaKur:N0} olabilir.", "kur");
        var doviz = F5Ortak.Nz(i.Doviz)?.ToUpperInvariant() ?? "TRY";
        if (doviz.Length != 3 || !doviz.All(char.IsAsciiLetter))
            throw new ValidationException("Döviz 3 harfli ISO kodu olmalıdır (ör. TRY, EUR).", "doviz");
        Metinler(i.SatisTemsilcisi, i.FaturaTuru, i.MakbuzNo, i.DosyaNo, i.SozlesmeNo, i.FiyatTuru, i.Kaynak, i.Aciklama);
        await BookingPartyCheck.RequireAsync(musteriler, araclar, i.MusteriId, i.VehicleId, ct);
        var id = await filo.CreateComprehensiveAsync(new FiloKiralamaInput
        {
            MusteriId = i.MusteriId, VehicleId = i.VehicleId, BasTar = F5Ortak.Utc(i.BasTar), SureAy = i.SureAy ?? 0,
            AylikUcret = i.AylikUcret ?? 0m, KdvOrani = i.KdvOrani ?? 0.20m, Doviz = doviz, Kur = i.Kur ?? 1m,
            ToplamKmLimiti = i.ToplamKmLimiti, DamgaVergisi = i.DamgaVergisi, Aciklama = F5Ortak.Nz(i.Aciklama),
            SatisTemsilcisi = F5Ortak.Nz(i.SatisTemsilcisi), FaturaTuru = F5Ortak.Nz(i.FaturaTuru),
            SozlesmeTarihi = F5Ortak.Utc(i.SozlesmeTarihi), ImzaTarih = F5Ortak.Utc(i.ImzaTarih),
            MakbuzNo = F5Ortak.Nz(i.MakbuzNo), DosyaNo = F5Ortak.Nz(i.DosyaNo), SozlesmeNo = F5Ortak.Nz(i.SozlesmeNo),
            VadeGun = i.VadeGun, FiyatTuru = F5Ortak.Nz(i.FiyatTuru), Kaynak = F5Ortak.Nz(i.Kaynak),
            CikisKm = i.CikisKm, ToplamKm = i.ToplamKm,
        }, ct);
        var no = (await filo.GetAsync(id, ct))?.No ?? "";
        return TypedResults.Created($"{Kok}/{id}", new FiloOlusturYaniti(id, no));
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Kunye(
        Guid id, FiloKunyeIstegi i, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await filo.GetComprehensiveAsync(id, ct) is null) return Bulunamadi(); // kapsam durumdan ÖNCE
        if (string.IsNullOrWhiteSpace(i.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        Metinler(i.SatisTemsilcisi, i.FaturaTuru, i.MakbuzNo, i.DosyaNo, i.SozlesmeNo, i.FiyatTuru, i.Kaynak, i.Aciklama);
        if (!await filo.UpdateMetaAsync(id, new FiloKiralamaMetaInput
        {
            SatisTemsilcisi = i.SatisTemsilcisi, FaturaTuru = i.FaturaTuru, SozlesmeTarihi = F5Ortak.Utc(i.SozlesmeTarihi),
            ImzaTarih = F5Ortak.Utc(i.ImzaTarih), MakbuzNo = i.MakbuzNo, DosyaNo = i.DosyaNo, SozlesmeNo = i.SozlesmeNo,
            VadeGun = i.VadeGun, FiyatTuru = i.FiyatTuru, Kaynak = i.Kaynak, CikisKm = i.CikisKm, ToplamKm = i.ToplamKm,
            ToplamKmLimiti = i.ToplamKmLimiti, Aciklama = i.Aciklama,
        }, i.Surum, ct)) return Bulunamadi();
        return await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Tamamla(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (await filo.GetComprehensiveAsync(id, ct) is null) return Bulunamadi(); // kapsam durumdan ÖNCE
        if (!await filo.CompleteAsync(id, ct)) return Bulunamadi();
        return await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Iptal(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (await filo.GetComprehensiveAsync(id, ct) is null) return Bulunamadi(); // kapsam durumdan ÖNCE
        if (!await filo.CancelAsync(id, ct)) return Bulunamadi();
        return await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }
}
