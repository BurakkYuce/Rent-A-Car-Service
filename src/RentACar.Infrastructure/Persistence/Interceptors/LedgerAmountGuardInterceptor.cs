using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Defter satırı baz tutarının SON SAVUNMASI (F8.1a adversarial M1): eklenen her <see cref="AccountLedgerEntry"/>
/// için |Amount × Rate| &lt; 10^15 olmalı (<c>numeric(19,4)</c> baz ölçeği). Kolonlar tutar ve kuru AYRI saklar;
/// çarpım hiçbir kolona yazılmadığı için veritabanı taşmayı yakalamaz. Otomatik çözülen (ör. 10^13'lük sabit) kurla
/// yazılan dev bir satır, sonradan her toplamada (bakiye, ekstre, mizan, kasa özeti) decimal taşmasıyla 500 üretir —
/// ve defter değiştirilemez olduğu için hasar kalıcıdır. DI ile kurulan her context (Blazor, /api/ui, harici API) ve
/// deftere yazan işlerin elle kurulan context'leri (DonemFaturaJob, VadeBildirimJob) bunu alır; ihlal yazımdan ÖNCE <see cref="ValidationException"/> (400) olur, hiçbir satır yazılmaz.
/// </summary>
public sealed class LedgerAmountGuardInterceptor : SaveChangesInterceptor
{
    /// <summary>Baz tutar üst sınırı (<c>numeric(19,4)</c>: 15 tam basamak).</summary>
    public const decimal BaseLimit = 1_000_000_000_000_000m;

    public const string Message = "Tutar × kur çok büyük (baz tutar sınırı aşıldı); işlem yazılmadı.";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static void Check(DbContext? db)
    {
        if (db is null) return;
        foreach (var e in db.ChangeTracker.Entries<AccountLedgerEntry>())
        {
            if (e.State != EntityState.Added) continue;
            if (!IsWithinLimit(e.Entity.Amount.Amount, e.Entity.Amount.Rate))
                throw new ValidationException(Message);
        }
    }

    /// <summary>Çarpım decimal'da taşsa bile (≈7,9·10^28) güvenli karşılaştırma.</summary>
    public static bool IsWithinLimit(decimal amount, decimal rate)
    {
        var a = Math.Abs(amount);
        var r = Math.Abs(rate);
        if (a == 0m || r == 0m) return true;
        // R2-L1: r < 1'de a × r ≤ a taşmaz; BaseLimit / r ise çok küçük r'de (< 1e-13) taşardı. r ≥ 1'de bölüm güvenli.
        try { return r < 1m ? a * r < BaseLimit : a < BaseLimit / r && a * r < BaseLimit; }
        catch (OverflowException) { return false; } // taşma = sınır aşımı

    }
}
