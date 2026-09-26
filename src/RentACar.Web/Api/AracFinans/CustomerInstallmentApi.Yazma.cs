using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.AracFinans;

public static partial class CustomerInstallmentApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string AlreadySaved = "Bu taksit zaten kaydedildi (No {0}, {1} {2}); yeni kayıt yazılmadı.";
    public const string KeyDifferentContent =
        "Bu işlem anahtarıyla başka içerikte bir taksit kaydı yazılmış (No {0}, {1} {2}); girdiğiniz kayıt YAZILMADI. Kayıtları kontrol edin.";

    /// <summary>Ortak giriş kuralları (oluştur + PUT): varlık, kapsam, sınırlar, TRY'de kur = 1.</summary>
    private static async Task<MusteriTaksitInput> InputAsync(MusteriTaksitIstegi i, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, ExchangeRateResolver exchangeRateResolver, CancellationToken ct)
    {
        VehicleFinanceShared.Amount(i.TaksitTutari, "taksitTutari", scale: 2); // servis 2 haneye yuvarlar — sessiz yuvarlama yerine red (L1)
        if (i.Vade is null) throw new ValidationException("Vade zorunludur.", "vade");
        VehicleFinanceShared.Text(i.Aciklama, 512, "aciklama");
        var status = F5Shared.EnumAdi<InstallmentStatus>(i.Durum, "durum") ?? InstallmentStatus.Bekliyor;
        var payment = F5Shared.Utc(i.OdemeTarihi);
        if (status == InstallmentStatus.Odendi) WithFields("odemeTarihi", () => DatePolicy.MoneyDate(payment, "Taksit ödeme"));
        var (currency, exchangeRate) = await CurrencyRateAsync(i.Doviz, i.Kur, F5Shared.Utc(i.Vade), exchangeRateResolver, ct);
        await ExistenceAsync(dbf, user, i.CariId, i.VehicleId, i.VehicleSaleId, ct);
        return new MusteriTaksitInput
        {
            CariId = i.CariId, VehicleId = IfEmpty(i.VehicleId), VehicleSaleId = IfEmpty(i.VehicleSaleId),
            Vade = F5Shared.Utc(i.Vade), TaksitTutari = i.TaksitTutari, Currency = currency, Kur = exchangeRate, Durum = status,
            OdemeTarihi = status == InstallmentStatus.Odendi ? payment : null, Aciklama = VehicleFinanceShared.Nz(i.Aciklama),
        };
    }

    private static async Task<(string Doviz, decimal Kur)> CurrencyRateAsync(string? currencyInput, decimal? exchangeRateInput,
        DateTimeOffset? date, ExchangeRateResolver exchangeRateResolver, CancellationToken ct)
    {
        var currency = VehicleFinanceShared.Currency(currencyInput);
        VehicleFinanceShared.Setup(exchangeRateInput, currency, scale: 4); // sınır + TRY'de kur = 1; kolon numeric(19,4)
        decimal exchangeRate = 1m;
        try { exchangeRate = await exchangeRateResolver.ResolveAsync(currency, exchangeRateInput, date, ct); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, "kur"); }
        return (currency, exchangeRate);
    }

    private static async Task ExistenceAsync(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, Guid customerId,
        Guid? vehicleId, Guid? vehicleSaleId, CancellationToken ct)
    {
        await VehicleFinanceShared.CustomerExistsAsync(dbf, customerId, "cariId", required: true, ct);
        await VehicleFinanceShared.VehicleWriteAsync(dbf, user, vehicleId, "vehicleId", required: false, ct);
        if (IfEmpty(vehicleSaleId) is { } s)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            if (!await db.VehicleSales.AsNoTracking().AnyAsync(x => x.Id == s, ct))
                throw new ValidationException("Araç satış kaydı bulunamadı.", "vehicleSaleId");
        }
    }

    private static Guid? IfEmpty(Guid? g) => g is { } x && x != Guid.Empty ? x : null;

    private static async Task<Created<MusteriTaksitOlusturYaniti>> Create(
        MusteriTaksitIstegi i, HttpContext http, CustomerInstallmentService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        if (await svc.GetAsync(key, ct) is { } m) throw UniqueExists(m, i); // (1) ÖNCE mevcut kayıt
        var input = await InputAsync(i, dbf, kullanici, kurCozucu, ct);
        input.IslemAnahtari = key;
        try { await svc.CreateAsync(input, ct); }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            if (await svc.GetAsync(key, ct) is { } y) throw UniqueExists(y, i);
            throw;
        }
        return TypedResults.Created($"{Root}/{key}", new MusteriTaksitOlusturYaniti(key));
    }

    private static DuplicateOperationException UniqueExists(MusteriTaksit m, MusteriTaksitIstegi i)
    {
        var currency = VehicleFinanceShared.Currency(i.Doviz); // yazımla AYNI normalizasyon (L1)
        var same = m.CariId == i.CariId && m.VehicleId == IfEmpty(i.VehicleId)
                   && m.TaksitTutari == decimal.Round(i.TaksitTutari, 2, MidpointRounding.AwayFromZero)
                   && m.Currency == currency && VehicleFinanceShared.SameInstant(m.Vade, F5Shared.Utc(i.Vade));
        return Existing(m.Id, m.Sira, m.TaksitTutari, m.Currency, same);
    }

    private static DuplicateOperationException Existing(Guid id, int order, decimal amount, string currency, bool same)
        => new(string.Format(Tr, same ? AlreadySaved : KeyDifferentContent, $"#{order}", amount.ToString("N2", Tr), currency),
            new MevcutIslem(id, $"#{order}", amount, currency, same));

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler korunur).</summary>
    private static void WithFields(string alan, Action validate)
    {
        try { validate(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, alan); }
    }
}
