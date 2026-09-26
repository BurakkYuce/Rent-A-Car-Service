using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.FinansHub;

/// <summary>
/// <c>/api/ui/v1/finans/*</c> — F8.1a finans ekranları (1. yarı, PARA): kasa hub, nakit işlem, bakiye düzeltme,
/// cari virman, tek cari/çok cari toplu tahsilat, toplu gider, depozito, cari ekstre, otomatik tahsilat, dönem
/// kapanışı, kurlar. İş mantığı MEVCUT servislerde (Blazor karşılıklarıyla aynı yol); burada yalnız uç kuralları:
/// izin, kapsam, giriş sınırları (<c>errors[alan]</c>), idempotency anahtarı, KVKK görünen ad.
/// <para>Tahsilat/ödeme/depozito al/irat F4.4 uçlarıdır (<see cref="FinansApi"/>) — burada KOPYALANMAZ.</para>
/// <para><b>Idempotency (docs/api/idempotency-envanteri.md):</b> para yazan her uç işlem başına
/// <c>Idempotency-Key</c> ister (<see cref="IdempotencyBasligi.ZorunluAnahtar"/>; yoksa 400) — yapısal satırlar
/// (E08 ters kayıt, E20 otomatik tahsilat, E36 dönem kapanışı) hariç. Anahtarın sonucu envanter satırıdır:
/// E06/E07/E10/E11/E13 aynı içerik 200 aynı id, farklı içerik 409; E03/E05/E22 409.</para>
/// </summary>
public static partial class FinanceHubApi
{
    public static RouteGroupBuilder MapFinanceHubApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/finans").WithTags("Finans Hub");

        // Okuma: kurlar Blazor'da yalnız [Authorize] (her rol) — parite: üç izinden biri.
        var anyRead = g.MapGroup("").RequireAnyPermission(
            Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        // Cari ekstre bakiye + tüm hareketleri (başka şubenin sözleşme no'ları dahil) gösterir → cari detaydaki
        // bakiye ile AYNI kapı: FinanceWrite ∨ ViewReports (Karar (5), 2026-09-25; Blazor paritesinden bilinçli sapma).
        var financeRead = g.MapGroup("").RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        var write = g.MapGroup("").RequirePermission(Permission.FinanceWrite);

        MapCash(write);
        MapCustomer(write, financeRead);
        MapDeposit(write);
        MapPeriod(write);
        MapRates(write, anyRead);
        return g;
    }

    internal static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    // ------------------------------------------------------------------ giriş sınırları

    /// <summary>Tutar en çok 4 ondalık (<c>numeric(19,4)</c>): fazlası SESSİZCE yuvarlanmaz, 400.</summary>
    internal static void AmountScale(decimal value, string field, int maxDecimals = 4)
    {
        if (decimal.Round(value, maxDecimals) != value)
            throw new ValidationException($"En çok {maxDecimals} ondalık basamak girilebilir.", field);
    }

    /// <summary>Kur en çok 6 ondalık (<c>numeric(19,6)</c>).</summary>
    internal static void RateScale(decimal? rate, string field = "kur")
    {
        if (rate is { } r && decimal.Round(r, 6) != r)
            throw new ValidationException("Kur en çok 6 ondalık basamak olabilir.", field);
    }

    /// <summary>Para girdisinin ortak kuralları (F4.4 yardımcıları + ölçek): pozitif, kolona sığan tutar; ISO döviz;
    /// pozitif, sığan kur; baz (tutar × kur) sınırı. Normalize döviz kodunu döner.</summary>
    internal static string MoneyInput(decimal amount, string? currency, decimal? rate, string amountField = "tutar")
    {
        FinansApi.Tutar(amount, amountField);
        AmountScale(amount, amountField);
        var code = FinansApi.Doviz(currency);
        FinansApi.Kur(rate);
        RateScale(rate);
        FinansApi.BazSiniri(amount, rate, amountField);
        return code;
    }

    /// <summary>
    /// F8.1a adversarial M1: kur boş bırakıldıysa servisin ÇÖZECEĞİ kuru (TRY=1; firma sabit kuru → TCMB) önceden çözüp
    /// baz sınırını (tutar × kur &lt; 10^15) ONA da uygular. Önce sınır yalnız açık kurda denetleniyordu; dev bir sabit
    /// kurla yazılan satır sonraki her toplamada taşma (500) üretiyordu. Açık kur <see cref="MoneyInput"/>'ta denetlendi.
    /// Kur çözülemezse servisle aynı 400 (<c>kur</c> alanı).
    /// </summary>
    internal static async Task ResolvedBaseLimitAsync(
        RentACar.Application.Kur.ExchangeRateResolver rates, decimal amount, string currency, decimal? rate, DateTimeOffset? date,
        CancellationToken ct, string field = "tutar")
    {
        if (rate is not null) return;
        decimal resolved = 0m;
        try { resolved = await rates.ResolveAsync(currency, null, date, ct); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, "kur"); }
        if (!RentACar.Infrastructure.Persistence.Interceptors.LedgerAmountGuardInterceptor.IsWithinLimit(amount, resolved))
            throw new ValidationException("Tutar × kur çok büyük.", field);
    }

    /// <summary>İsteğe bağlı metin: boş → null, aksi Trim; kolon uzunluğu aşılırsa alan hatası.</summary>
    internal static string? Text(string? value, int max, string field)
    {
        FinansApi.Metin(value, max, field);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Cari kiracıda var mı (RLS + tenant filtresi; PII çözülmez). Yok/başka kiracı → 400 alan hatası.</summary>
    internal static async Task CustomerMustExistAsync(
        IDbContextFactory<AppDbContext> f, Guid id, string field, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ValidationException("Cari seçilmelidir.", field);
        await using var db = await f.CreateDbContextAsync(ct);
        if (!await db.Customers.AsNoTracking().AnyAsync(c => c.Id == id, ct))
            throw new ValidationException("Cari bulunamadı.", field);
    }

    /// <summary>DB'ye giden an UTC (Npgsql timestamptz; DEVIR §5).</summary>
    internal static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();
}
