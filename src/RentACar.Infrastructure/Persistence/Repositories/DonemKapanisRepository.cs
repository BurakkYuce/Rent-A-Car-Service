using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dönem-sonu kapanış fişi ATOMİK yazıcısı (PR-A). Kapanış = oku(bakiye) → fiş kur → post → dönemi kilitle,
/// hepsi TEK transaction'da ve tenant başına <c>pg_advisory_xact_lock</c> ile SERİLEŞTİRİLMİŞ. Bu sayede
/// (adversarial BULGU 2/3 düzeltmesi):
///  • eşzamanlı iki kapanış (aynı/farklı tarih) çift saymaz — ikincisi ilkinin sıfırladığı GÜNCEL bakiyeyi
///    okur → yalnız delta (veya boş);
///  • kilit kaldırılıp geçmişe kayıt eklenip yeniden kapatılırsa yeni delta yakalanır (SourceId TAZE GUID →
///    çakışma yok, sessiz no-op yok);
///  • aynı istek iki kez → ikinci güncel bakiye 0 → boş fiş (intrinsik idempotency).
/// YALNIZ P&amp;L kapatılır (Gelir/Gider → DonemSonucu); KDV/Kasa/Banka/Cari (bilanço) dokunulmaz. Tutarlar
/// base-TL, 4 haneye yuvarlı; fiş DENGELİ kurulur (Σ SignedBase = 0 by construction). Değişmez defter: ekle-yalnız.
/// </summary>
public sealed class DonemKapanisRepository(IDbContextFactory<AppDbContext> factory) : IDonemKapanisRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task KapatAsync(DateTimeOffset kapanisTarihi, CancellationToken ct = default)
    {
        // Kapanış anı = kapanış gününün İSTANBUL gün sonu, UTC (o günün tüm kayıtları dahil). F8.1a adversarial M2:
        // kilit karşılaştırması (PeriodLock) İstanbul günüyle yapılır; fiş aynı günün sonunu kapatmalı.
        var closingDay = PeriodLock.LocalDay(kapanisTarihi);
        var kapanisAni = PeriodLock.DayEndUtc(closingDay);

        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await AdvisoryLockAsync(db, ct); // tenant başına kapanışı serileştir (oku-yaz-kilit yarışı yok)

            // "Zaten kapalı" guard'ı KİLİDİN İÇİNDE (adversarial V3-b bulgusu — ÇİFT SAYIM düzeltmesi):
            // servis katmanındaki aynı kontrol kilidin DIŞINDA olduğu için eşzamanlı iki kapanışta İKİSİ de
            // geçebiliyordu. Sonrasında GEÇ tarihli kapanış önce commit ederse, ERKEN tarihli olan bakiyeyi
            // `EntryDateUtc <= kendi kapanışAnı` ile okur → geç tarihli kapanış fişini GÖREMEZ (tarihi ileride)
            // → aynı geliri İKİNCİ kez kapatır (Gelir +1000, DonemSonucu −2000, iki DonemSonucu fişi).
            // Bakiye-delta savunması yalnız İLERİ tarih sırasında çalışır; geriye kapanışı burada reddediyoruz.
            var kilit = await db.DonemKilitleri.FirstOrDefaultAsync(ct);
            if (kilit?.KapanisTarihi is { } mevcut && closingDay <= PeriodLock.LocalDay(mevcut))
                throw new ValidationException(
                    $"Dönem zaten {PeriodLock.LocalDay(mevcut):yyyy-MM-dd} tarihine kapalı. Yeniden kapatmak için önce kilidi kaldırın.");

            // Güncel Gelir/Gider SignedBase bakiyeleri (önceki kapanışlar DAHİL → delta). Base = Amount×Rate.
            var rows = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => (e.AccountType == LedgerAccountType.Gelir || e.AccountType == LedgerAccountType.Gider)
                            && e.EntryDateUtc <= kapanisAni)
                .Select(e => new { e.AccountType, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
                .ToListAsync(ct);
            decimal Signed(LedgerAccountType t) => rows.Where(x => x.AccountType == t)
                .Sum(x => (x.Direction == LedgerDirection.Debit ? 1m : -1m) * x.A * x.R);

            // Sıfırlama hedefleri 4-haneye yuvarlı (defter Amount numeric(19,4)); DonemSonucu net'i absorbe eder.
            var gelirHedef = Math.Round(-Signed(LedgerAccountType.Gelir), 4, MidpointRounding.AwayFromZero);
            var giderHedef = Math.Round(-Signed(LedgerAccountType.Gider), 4, MidpointRounding.AwayFromZero);
            var sonucHedef = -(gelirHedef + giderHedef);

            var sourceId = Guid.NewGuid(); // TAZE — meşru yeniden-kapatma çakışmasın (serialization çift-postu önler)
            var entries = new List<AccountLedgerEntry>();
            void Ekle(LedgerAccountType tip, decimal hedefSignedBase)
            {
                if (hedefSignedBase == 0m) return;
                entries.Add(new AccountLedgerEntry
                {
                    EntryDateUtc = kapanisAni,
                    AccountType = tip,
                    AccountRef = null,
                    Direction = hedefSignedBase > 0m ? LedgerDirection.Debit : LedgerDirection.Credit,
                    Amount = new Money(Math.Abs(hedefSignedBase), "TRY", 1m),
                    Description = $"Dönem kapanışı {closingDay:yyyy-MM-dd}",
                    SourceType = "DonemKapanis",
                    SourceId = sourceId
                });
            }
            Ekle(LedgerAccountType.Gelir, gelirHedef);
            Ekle(LedgerAccountType.Gider, giderHedef);
            Ekle(LedgerAccountType.DonemSonucu, sonucHedef);

            // Denge güvencesi (by construction 0; defansif).
            if (entries.Sum(e => e.SignedBase) != 0m)
                throw new ValidationException("Dönem kapanış fişi dengesiz kuruldu.");
            db.AccountLedgerEntries.AddRange(entries);

            // Dönemi kilitle — AYNI transaction (fiş + kilit atomik). Tek satır/tenant (upsert; yukarıda okundu).
            if (kilit is null) { kilit = new DonemKilidi(); db.DonemKilitleri.Add(kilit); }
            kilit.KapanisTarihi = kapanisTarihi;
            kilit.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    /// <summary>Tenant başına kapanış advisory-kilidi (InvoiceRepository deseni) — transaction sonunda otomatik bırakılır.</summary>
    private static async Task AdvisoryLockAsync(AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"donem-kapanis:{db.TenantId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }
}
