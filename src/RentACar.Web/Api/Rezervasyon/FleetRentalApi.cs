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
public static class FleetRentalApi
{
    private const string Root = UiApiExtensions.V1 + "/filo-kiralama";

    public static RouteGroupBuilder MapFleetRentalApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/filo-kiralama").WithTags("Filo Kiralama").RequirePermission(Permission.OperationsWrite);
        g.MapGet("", GetList).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create).MapFields(WriteRules);
        g.MapPut("/{id:guid}/kunye", Profile).MapFields(WriteRules);
        g.MapPost("/{id:guid}/tamamla", Complete);
        g.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Sözleşme bulunamadı.");

    private static readonly SortFieldMap<FiloListeSatiri> Map = SortFieldMap<FiloListeSatiri>
        .Create(k => k.Id)
        .Alan("no", k => k.No).Alan("musteri", k => k.MusteriAd).Alan("plaka", k => k.Plaka)
        .Alan("basTar", k => k.BasTar).Alan("sureAy", k => k.SureAy).Alan("aylikUcret", k => k.AylikUcret)
        .Alan("genelToplam", k => k.GenelToplam).Alan("durum", k => k.Durum);

    private static async Task<Ok<Sayfa<FiloListeSatiri>>> GetList(
        FleetRentalService filo, IDbContextFactory<AppDbContext> dbf, Guid? musteriId, string? plaka, string? ara,
        string? durum, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var (min, max) = F5Shared.DayRange(bas, bit);
        var list = await filo.ListScopedAsync(new FiloKiralamaFilter
        {
            MusteriId = musteriId, Plaka = F5Shared.Nz(plaka), Ara = F5Shared.Nz(ara),
            Durum = F5Shared.EnumAdi<FleetRentalStatus>(durum, "durum"), Bas = min, Bit = max,
        }, ct);
        var customers = await F5Shared.CustomersAsync(dbf, list.Select(k => k.MusteriId), ct);
        var plates = await F5Shared.PlatesAsync(dbf, list.Select(k => k.VehicleId), ct);
        var rows = list.Select(k => new FiloListeSatiri(k.Id, k.No, k.SozlesmeNo, k.MusteriId,
            F5Shared.CustomerName(customers, k.MusteriId), k.VehicleId, F5Shared.Plate(plates, k.VehicleId), k.BasTar, k.SureAy,
            k.AylikUcret, FleetRentalService.InstallmentPlan(k).GenelToplam, k.Currency, k.SatisTemsilcisi, k.Kaynak,
            k.VadeGun, k.Durum.ToString())).ToList();
        return TypedResults.Ok(F5Shared.Paginate(rows, Map, sayfa, boyut, sirala));
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Detail(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
        => await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();

    private static async Task<FiloKiralamaDto?> DtoAsync(
        Guid id, HttpContext http, FleetRentalService fleet, IFleetRentalRepository store, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        var version = await store.VersionAsync(id, ct); // alanlardan ÖNCE
        var k = await fleet.GetComprehensiveAsync(id, ct); // kapsam dışı → 403 (içerik sızmaz)
        if (k is null) return null;
        var customers = await F5Shared.CustomersAsync(dbf, [k.MusteriId], ct);
        var plates = await F5Shared.PlatesAsync(dbf, [k.VehicleId], ct);
        var active = k.Durum == FleetRentalStatus.Aktif;
        var y = new FiloYetkileri(active, active, active && AuthExtensions.HasPermission(http.User, Permission.OperationsDelete));
        return FiloKiralamaDto.From(k, version, F5Shared.CustomerName(customers, k.MusteriId), F5Shared.Plate(plates, k.VehicleId), y);
    }

    private static readonly (string, string)[] WriteRules =
    [
        ("Müşteri seçilmelidir", "musteriId"), ("Araç seçilmelidir", "vehicleId"),
        ("Süre (ay)", "sureAy"), ("Aylık ücret", "aylikUcret"), ("KDV oranı", "kdvOrani"), ("Kur pozitif", "kur"),
        ("Damga vergisi", "damgaVergisi"), ("Vade günü", "vadeGun"), ("Kilometre negatif", "cikisKm"),
        ("KM limiti", "toplamKmLimiti"), ("Toplam KM", "toplamKm"),
    ];

    /// <summary>varchar uzunlukları (FiloKiralamaConfig) — 22001 yerine 400 + alan.</summary>
    private static void Texts(string? sale, string? invoice, string? receipt, string? file, string? sozNo,
        string? price, string? source, string? description)
    {
        RentalLimits.Text(sale, 128, "satisTemsilcisi", "Satış temsilcisi");
        RentalLimits.Text(invoice, 32, "faturaTuru", "Fatura türü");
        RentalLimits.Text(receipt, 32, "makbuzNo", "Makbuz no");
        RentalLimits.Text(file, 32, "dosyaNo", "Dosya no");
        RentalLimits.Text(sozNo, 32, "sozlesmeNo", "Sözleşme no");
        RentalLimits.Text(price, 32, "fiyatTuru", "Fiyat türü");
        RentalLimits.Text(source, 64, "kaynak", "Kaynak");
        RentalLimits.Text(description, 512, "aciklama", "Açıklama");
    }

    /// <summary>Kur üst sınırı (numeric(19,6) → 13 tam hane; makul tavan).</summary>
    public const decimal MaxRate = 1_000_000m;

    private static async Task<Created<FiloOlusturYaniti>> Create(
        FiloKiralamaIstegi i, FleetRentalService filo, ICustomerRepository musteriler, IVehicleRepository araclar,
        CancellationToken ct)
    {
        RentalLimits.Amount(i.AylikUcret, "aylikUcret", "Aylık ücret");
        RentalLimits.Amount(i.DamgaVergisi, "damgaVergisi", "Damga vergisi");
        if (i.Kur is { } kr && kr > MaxRate)
            throw new ValidationException($"Kur en fazla {MaxRate:N0} olabilir.", "kur");
        var currency = F5Shared.Nz(i.Doviz)?.ToUpperInvariant() ?? "TRY";
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new ValidationException("Döviz 3 harfli ISO kodu olmalıdır (ör. TRY, EUR).", "doviz");
        Texts(i.SatisTemsilcisi, i.FaturaTuru, i.MakbuzNo, i.DosyaNo, i.SozlesmeNo, i.FiyatTuru, i.Kaynak, i.Aciklama);
        await BookingPartyCheck.RequireAsync(musteriler, araclar, i.MusteriId, i.VehicleId, ct);
        var id = await filo.CreateComprehensiveAsync(new FiloKiralamaInput
        {
            MusteriId = i.MusteriId, VehicleId = i.VehicleId, BasTar = F5Shared.Utc(i.BasTar), SureAy = i.SureAy ?? 0,
            AylikUcret = i.AylikUcret ?? 0m, KdvOrani = i.KdvOrani ?? 0.20m, Doviz = currency, Kur = i.Kur ?? 1m,
            ToplamKmLimiti = i.ToplamKmLimiti, DamgaVergisi = i.DamgaVergisi, Aciklama = F5Shared.Nz(i.Aciklama),
            SatisTemsilcisi = F5Shared.Nz(i.SatisTemsilcisi), FaturaTuru = F5Shared.Nz(i.FaturaTuru),
            SozlesmeTarihi = F5Shared.Utc(i.SozlesmeTarihi), ImzaTarih = F5Shared.Utc(i.ImzaTarih),
            MakbuzNo = F5Shared.Nz(i.MakbuzNo), DosyaNo = F5Shared.Nz(i.DosyaNo), SozlesmeNo = F5Shared.Nz(i.SozlesmeNo),
            VadeGun = i.VadeGun, FiyatTuru = F5Shared.Nz(i.FiyatTuru), Kaynak = F5Shared.Nz(i.Kaynak),
            CikisKm = i.CikisKm, ToplamKm = i.ToplamKm,
        }, ct);
        var no = (await filo.GetAsync(id, ct))?.No ?? "";
        return TypedResults.Created($"{Root}/{id}", new FiloOlusturYaniti(id, no));
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Profile(
        Guid id, FiloKunyeIstegi i, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await filo.GetComprehensiveAsync(id, ct) is null) return NotFoundProblem(); // kapsam durumdan ÖNCE
        if (string.IsNullOrWhiteSpace(i.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        Texts(i.SatisTemsilcisi, i.FaturaTuru, i.MakbuzNo, i.DosyaNo, i.SozlesmeNo, i.FiyatTuru, i.Kaynak, i.Aciklama);
        if (!await filo.UpdateMetaAsync(id, new FiloKiralamaMetaInput
        {
            SatisTemsilcisi = i.SatisTemsilcisi, FaturaTuru = i.FaturaTuru, SozlesmeTarihi = F5Shared.Utc(i.SozlesmeTarihi),
            ImzaTarih = F5Shared.Utc(i.ImzaTarih), MakbuzNo = i.MakbuzNo, DosyaNo = i.DosyaNo, SozlesmeNo = i.SozlesmeNo,
            VadeGun = i.VadeGun, FiyatTuru = i.FiyatTuru, Kaynak = i.Kaynak, CikisKm = i.CikisKm, ToplamKm = i.ToplamKm,
            ToplamKmLimiti = i.ToplamKmLimiti, Aciklama = i.Aciklama,
        }, i.Surum, ct)) return NotFoundProblem();
        return await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Complete(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (await filo.GetComprehensiveAsync(id, ct) is null) return NotFoundProblem(); // kapsam durumdan ÖNCE
        if (!await filo.CompleteAsync(id, ct)) return NotFoundProblem();
        return await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<FiloKiralamaDto>, ProblemHttpResult>> Cancel(
        Guid id, HttpContext http, FleetRentalService filo, IFleetRentalRepository depo, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (await filo.GetComprehensiveAsync(id, ct) is null) return NotFoundProblem(); // kapsam durumdan ÖNCE
        if (!await filo.CancelAsync(id, ct)) return NotFoundProblem();
        return await DtoAsync(id, http, filo, depo, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }
}
