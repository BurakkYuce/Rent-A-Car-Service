using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Common;
using RentACar.Application.Currencies;
using RentACar.Application.Kur;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Kur;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Kurlar (Blazor <c>/kurlar</c>): TCMB günlük kurları, firma sabit kurları, çevirim, TCMB yenileme. Sabit kur
/// para çözümünü etkiler (KurService önce sabit kura bakar) → yazma FinanceWrite; PUT iyimser eşzamanlılıkla.</summary>
public static partial class FinanceHubApi
{
    private static void MapRates(RouteGroupBuilder write, RouteGroupBuilder anyRead)
    {
        anyRead.MapGet("/kurlar", GetRates);
        anyRead.MapGet("/kurlar/cevir", ConvertAmount);
        write.MapPost("/kurlar/yenile", RefreshRates);
        write.MapPost("/kurlar/sabit", CreateFixedRate);
        write.MapPut("/kurlar/sabit/{id:guid}", UpdateFixedRate);
        write.MapDelete("/kurlar/sabit/{id:guid}", DeleteFixedRate);
    }

    private static async Task<Ok<RatesScreen>> GetRates(
        ExchangeRateService rates, FixedExchangeRateService fixedRates, CurrencyService currencies, CancellationToken ct)
    {
        var today = await rates.TodayRatesAsync(ct);
        // 2026-09-25: satır ve sürüm tutarlı çift (sürüm, satır, sürüm — #313 ShiftApi deseni). Önceden liste okunup
        // sürümler sonra okunuyordu: aradaki yazım yeni sürümü eski alanlarla eşleştiriyordu (TOCTOU).
        var fixedList = await fixedRates.ListWithVersionsAsync(ct);
        var activeCodes = fixedList.Where(p => p.Row.Aktif).Select(p => p.Row.Kod).ToHashSet(StringComparer.Ordinal);
        var items = fixedList.Select(p => new FixedRate(p.Row.Id, p.Row.Kod, p.Row.Kur, Day(p.Row.BasTar), Day(p.Row.BitTar),
            p.Row.Aktif, p.Version)).ToList();
        var codes = (await currencies.ListActiveAsync(ct)).Select(c => c.Kod)
            .Concat(today.Select(k => k.Kod)).Append("TRY")
            .Select(ExchangeRateService.NormalizeCode).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
        return TypedResults.Ok(new RatesScreen(
            [.. today.Select(k => new CbrtRate(k.Kod, k.Ad, k.Birim, k.ForexAlis, k.ForexSatis, k.EfektifAlis, k.EfektifSatis,
                DateOnly.FromDateTime(k.Tarih.UtcDateTime), activeCodes.Contains(k.Kod)))],
            items, codes));
    }

    /// <summary>Bilgi amaçlı çevirim (TL bazı üzerinden; yuvarlamaz). Kur bulunamazsa 400.</summary>
    private static async Task<Ok<ConversionResult>> ConvertAmount(
        decimal tutar, string? kaynak, string? hedef, ExchangeRateService rates, CancellationToken ct)
    {
        if (Math.Abs(tutar) >= FinanceOpsApi.AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", "tutar");
        var from = FinanceOpsApi.NormalizeCurrency(kaynak);
        string to = "";
        FinanceOpsApi.WithFields("hedef", () => to = ExchangeRateService.NormalizeCodeStrict(string.IsNullOrWhiteSpace(hedef) ? "TRY" : hedef));
        decimal result = 0m;
        try { result = await rates.ConvertAsync(tutar, from, to, ct: ct); }
        catch (ValidationException ex) when (ex.Alan is null) { throw new ValidationException(ex.Message, "kaynak"); }
        // L2: tutar × kaynak kuru / hedef kuru decimal'ı taşabilir (ör. çok küçük hedef kur) — 500 değil 400.
        catch (OverflowException) { throw new ValidationException("Çevrilen tutar çok büyük.", "tutar"); }
        return TypedResults.Ok(new ConversionResult(tutar, from, to, result));
    }

    /// <summary>TCMB'den çek (paylaşımlı tablo; 30 dk içinde tekrar çekilmez → "guncel").</summary>
    private static async Task<Ok<RatesRefreshResult>> RefreshRates(TcmbExchangeRateService tcmb, CancellationToken ct)
    {
        var n = await tcmb.RefreshAsync(ct: ct);
        return TypedResults.Ok(n switch
        {
            > 0 => new RatesRefreshResult("guncellendi", n),
            -1 => new RatesRefreshResult("guncel", 0),
            _ => new RatesRefreshResult("basarisiz", 0),
        });
    }

    private static async Task<Ok<CashOperationResult>> CreateFixedRate(
        FixedRateCreateRequest req, FixedExchangeRateService fixedRates, CancellationToken ct)
    {
        FixedRateInput(req.Kur, req.BasTar, req.BitTar);
        var id = await fixedRates.CreateAsync(new SabitKurInput
        {
            Kod = req.Kod ?? "", Kur = req.Kur, BasTar = Midnight(req.BasTar), BitTar = Midnight(req.BitTar), Aktif = req.Aktif,
        }, ct);
        return TypedResults.Ok(new CashOperationResult(id));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateFixedRate(
        Guid id, FixedRateUpdateRequest req, FixedExchangeRateService fixedRates, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden yükleyin.", "surum");
        FixedRateInput(req.Kur, req.BasTar, req.BitTar);
        var ok = await fixedRates.UpdateAsync(id, new SabitKurInput
        {
            Kur = req.Kur, BasTar = Midnight(req.BasTar), BitTar = Midnight(req.BitTar), Aktif = req.Aktif,
        }, req.Surum, ct);
        return ok ? TypedResults.NoContent() : F5Shared.NotFound("Sabit kur bulunamadı.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteFixedRate(
        Guid id, FixedExchangeRateService fixedRates, CancellationToken ct)
        => await fixedRates.DeleteAsync(id, ct) ? TypedResults.NoContent() : F5Shared.NotFound("Sabit kur bulunamadı.");

    /// <summary>Sabit kur <c>numeric(19,6)</c>: pozitif, 6 ondalık, kolona sığan; pencere makul yıllarda.</summary>
    private static void FixedRateInput(decimal rate, DateOnly? start, DateOnly? end)
    {
        if (rate <= 0m) throw new ValidationException("Sabit kur 0'dan büyük olmalı.", "kur");
        if (rate > FixedExchangeRateService.MaxRate) throw new ValidationException(FixedExchangeRateService.MaxRateMessage, "kur");
        RateScale(rate);
        foreach (var (d, field) in new[] { (start, "basTar"), (end, "bitTar") })
            if (d is { Year: < 2000 or > 2100 })
                throw new ValidationException("Tarih 2000 ile 2100 arasında olmalıdır.", field);
    }

    /// <summary>Takvim günü → o günün UTC başı (servis pencereyi gün-DAHİL genişletir; Blazor ucuyla aynı).</summary>
    private static DateTimeOffset? Midnight(DateOnly? day)
        => day is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    private static DateOnly? Day(DateTimeOffset? value) => value is { } v ? DateOnly.FromDateTime(v.UtcDateTime) : null;
}
