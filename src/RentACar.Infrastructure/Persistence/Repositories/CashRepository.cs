using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Nakit işlem + defter kalıcılığı. PostAsync: No tahsisi + belge + dengeli defter
/// kümesi + (kira) Tahsilat/Bakiye → TEK transaction. Dengelilik (Σ borç = Σ alacak,
/// base) burada da doğrulanır (yapısal invariant guard).
/// </summary>
public sealed class CashRepository(IDbContextFactory<AppDbContext> factory) : ICashRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().OrderByDescending(t => t.Tarih).ToListAsync(ct);
    }

    /// <summary>
    /// FAZ-67 — süzgeçli nakit işlem listesi (canlı <c>nakit_islem_ara.aspx</c>).
    ///
    /// <para>Cari adı/özel kodu PII ÇÖZÜLMEDEN okunur: <c>DisplayName</c> girdileri (Unvan/Ad/Soyad)
    /// ve <c>OzelKod</c> düz-metin kolonlardır. Ad araması bellek-içi ve ORDINAL yapılır — SQL'e
    /// <c>lower()</c> olarak itmek karşılaştırmayı iki ayrı kültüre böler ve Türkçe I/İ çiftinde
    /// sessizce eşleşmez (rezervasyon tarafında öğrenilen ders).</para>
    /// </summary>
    public async Task<IReadOnlyList<NakitIslemSatirDto>> SearchIslemlerAsync(
        CashFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var f = filter ?? new CashFilter();
        var limit = Math.Clamp(f.EnFazla, 1, 5000);

        var q = db.CashTransactions.AsNoTracking();
        if (f.Tip is { } tip) q = q.Where(t => t.Tip == tip);
        if (f.Bas is { } b) q = q.Where(t => t.Tarih >= b);
        if (f.Bit is { } bt) q = q.Where(t => t.Tarih <= bt);
        if (f.Hesap is { } h) q = q.Where(t => t.KarsiHesap == h);
        if (f.HesapId is { } hid)
            q = hid == Guid.Empty ? q.Where(t => t.HesapId == null) : q.Where(t => t.HesapId == hid);
        if (!string.IsNullOrWhiteSpace(f.Kanal))
        {
            var k = f.Kanal.Trim();
            q = q.Where(t => t.Kanal == k);
        }

        var islemler = await q.OrderByDescending(t => t.Tarih).Take(limit).ToListAsync(ct);
        if (islemler.Count == 0) return [];

        var cariIdler = islemler.Select(t => t.CariId).Distinct().ToList();
        var cariler = (await db.Customers.AsNoTracking().Where(c => cariIdler.Contains(c.Id))
                .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad, c.OzelKod }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => (
                Ad: new Customer { Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad }.DisplayName,
                Kod: c.OzelKod));

        IEnumerable<NakitIslemSatirDto> satirlar = islemler.Select(t =>
        {
            var c = cariler.TryGetValue(t.CariId, out var v) ? v : (Ad: "—", Kod: (string?)null);
            return new NakitIslemSatirDto(t, c.Ad, c.Kod);
        });

        if (!string.IsNullOrWhiteSpace(f.Ara))
        {
            var a = f.Ara.Trim();
            satirlar = satirlar.Where(x =>
                x.Islem.No.Contains(a, StringComparison.OrdinalIgnoreCase)
                || x.CariAd.Contains(a, StringComparison.OrdinalIgnoreCase)
                || (x.CariKod?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Islem.Aciklama?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return [.. satirlar];
    }

    public async Task<CashTransaction?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<bool> HasReversalAsync(Guid originalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().AnyAsync(t => t.TersAlinanId == originalId, ct);
    }

    public async Task<bool> IslemAnahtariVarMiAsync(Guid islemAnahtari, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CashTransactions.AsNoTracking().AnyAsync(t => t.IslemAnahtari == islemAnahtari, ct);
    }

    /// <summary>F1.4 mükerrer mesajı — servis ön-kontrolü, kilit-içi kontrol ve kısıt yolu AYNI metni verir.</summary>
    internal const string MukerrerMesaji = "Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).";

    /// <summary>
    /// Kira tahsilat deltası — KİRA DÖVİZİNDE (K2 fix). Yön = Tip(Tahsilat:+/Ödeme:−) × TersKayitMi(−).
    /// TRY kira → TL-baz (AmountInBase, mevcut davranış). FX kira → tahsilat AYNI dövizde zorunlu (ham Amount);
    /// farklı döviz karışık-birim Bakiye üretirdi (1000 EUR kira + TL-baz delta → −34.000 "alacak") → red.
    /// </summary>
    /// <summary>
    /// Bir kasa hareketinin kira Tahsilat alanına etkisi. FAZ-68: mutabakat raporu da BU metodu
    /// çağırır — işaret/döviz kuralının ikinci bir kopyası çıkmasın (kopya çıksaydı rapor ile
    /// sözleşme satırı sessizce ayrışabilirdi).
    /// </summary>
    internal static decimal RentalDelta(CashTransaction tx, string? kiraDoviz)
    {
        var yon = (tx.Tip == CashTransactionType.Tahsilat ? 1m : -1m) * (tx.TersKayitMi ? -1m : 1m);
        var kira = RentACar.Application.Kur.KurService.NormalizeKod(kiraDoviz);
        if (kira == "TRY") return yon * tx.Amount.AmountInBase;
        if (RentACar.Application.Kur.KurService.NormalizeKod(tx.Amount.Currency) != kira)
            throw new ValidationException($"Kira dövizi {kira}; tahsilat/iade aynı dövizde girilmelidir.");
        return yon * tx.Amount.Amount;
    }

    /// <summary>Kira Tahsilat/Bakiye'yi ATOMİK SQL ile günceller (O1 fix: eşzamanlı tahsilatta kayıp yok;
    /// SET sağ tarafı ESKİ satır değerini okur → += yarışsız). Aynı transaction içinde çağrılır; RLS geçerli.</summary>
    // FAZ 4.2-B4: DonemFaturaUretici (job) kira Tahsilat/Bakiye deltasını da BU metottan uygular (tek kopya).
    internal static async Task ApplyRentalDeltaAsync(AppDbContext db, CashTransaction tx, CancellationToken ct)
    {
        if (tx.RentalId is not Guid rentalId) return;
        var rental = await db.Rentals.AsNoTracking()
            .Where(r => r.Id == rentalId).Select(r => new { r.Doviz }).FirstOrDefaultAsync(ct);
        if (rental is null) return;
        var delta = RentalDelta(tx, rental.Doviz);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""Rentals"" SET
                ""Tahsilat"" = ""Tahsilat"" + {delta},
                ""Bakiye"" = ""GenelToplam"" - (""Tahsilat"" + {delta}),
                ""UpdatedAtUtc"" = {DateTimeOffset.UtcNow}
            WHERE ""Id"" = {rentalId}", ct);
    }

    public async Task PostAsync(
        CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        // Dengelilik guard: Σ Borç(base) == Σ Alacak(base).
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            tx.No = await BelgeNoUretici.UretAsync(db, db.TenantId,
                tx.Tip == CashTransactionType.Odeme ? BelgeNoTuru.Tediye : BelgeNoTuru.Tahsilat, ct);
            db.CashTransactions.Add(tx);
            db.AccountLedgerEntries.AddRange(entries);
            await ApplyRentalDeltaAsync(db, tx, ct); // atomik SQL += (O1); kira dövizi doğrulanır (K2)

            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Kısmi unique index çakışması: ya aynı işlemin ikinci ters kaydı ya da aynı IslemAnahtari ile
                // çift-submit (adversarial M5) → her iki halde idempotent reddet.
                await dbTx.RollbackAsync(ct);
                RentACar.Application.Observability.RacarMetrics.LedgerIdempotentRejected(); // metrik: idempotent red
                // F1.4: ters kayıt yarışında mesaj servis ön-kontrolüyle BİREBİR aynı (tip de aynı: Mukerrer)
                // → sonuç sıralı/eşzamanlı ayrımına bağlı değil.
                var ikinciTers = (ex.InnerException as PostgresException)?.ConstraintName == IdempotencyKisiti.IkinciTersKayit;
                throw IdempotencyKisiti.Red(ex, ikinciTers
                    ? "Bu işlem zaten ters kaydedilmiş."
                    : MukerrerMesaji);
            }
        }, ct);
    }

    public async Task<Dictionary<Guid, Guid>> FaturaKiralariAsync(
        IReadOnlyCollection<Guid> faturaIds, CancellationToken ct = default)
    {
        if (faturaIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Invoices.AsNoTracking()
            .Where(i => faturaIds.Contains(i.Id) && i.RentalId != null)
            .Select(i => new { i.Id, RentalId = i.RentalId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.RentalId, ct);
    }

    public async Task<Dictionary<Guid, decimal>> GetTahsisToplamlariAsync(
        IReadOnlyCollection<Guid> ledgerEntryIds, CancellationToken ct = default)
    {
        if (ledgerEntryIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KapatmaTahsisleri.AsNoTracking()
            .Where(t => ledgerEntryIds.Contains(t.LedgerEntryId))
            .GroupBy(t => t.LedgerEntryId)
            .Select(g => new { g.Key, Toplam = g.Sum(x => x.KapatilanBaz) })
            .ToDictionaryAsync(x => x.Key, x => x.Toplam, ct);
    }

    public async Task PostCariKapatmaAsync(
        Guid cariId, CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries,
        IReadOnlyList<KapatmaTahsis> tahsisler, CancellationToken ct = default)
    {
        // Dengelilik guard (PostAsync deseni).
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");
        if (tahsisler.Count == 0)
            throw new ValidationException("Kapatma en az bir tahsis içermelidir.");

        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            // TOCTOU çiti (adversarial H2): (tenant, cari) danışma kilidi — bu carinin kapatma
            // işlemleri tx sonuna dek SIRALANIR. Kontroller kilidin ARKASINDA yapılır; kilitsiz
            // sürümde 8 eşzamanlı kapatma çiti geçip bakiyeyi −7000'e düşürmüştü.
            await KapatmaKilitAsync(db, cariId, ct);

            // F1.4 — ANAHTAR ÖNCE: aynı anahtarlı ikinci gönderim, tahsis/bakiye çitlerinden ÖNCE mükerrer
            // sayılır. Aksi halde ilk gönderim kalemi TAMAMEN kapattıysa ikinci "zaten kapatılmış" (400),
            // kısmen kapattıysa kısıt (409) alıyordu — sonuç tutara bağlıydı.
            if (tx.IslemAnahtari is Guid anahtar &&
                await db.CashTransactions.AsNoTracking().AnyAsync(t => t.IslemAnahtari == anahtar, ct))
                throw new MukerrerIslemException(MukerrerMesaji);

            // 1) TAHSİS ÇİTİ (asıl çit): bir borç satırına tahsis edilen toplam, o satırın baz
            //    tutarını AŞAMAZ. Aynı kalemi ikinci kez kapatmayı engelleyen budur — bakiye çiti
            //    tek başına yetmiyordu (adversarial H1).
            var hedefIds = tahsisler.Select(t => t.LedgerEntryId).Distinct().ToList();
            var satirlar = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => hedefIds.Contains(e.Id))
                .Select(e => new { e.Id, e.AccountType, e.AccountRef, e.Direction,
                                   A = e.Amount.Amount, R = e.Amount.Rate })
                .ToListAsync(ct);
            var mevcut = await db.KapatmaTahsisleri.AsNoTracking()
                .Where(t => hedefIds.Contains(t.LedgerEntryId))
                .GroupBy(t => t.LedgerEntryId)
                .Select(g => new { g.Key, Toplam = g.Sum(x => x.KapatilanBaz) })
                .ToDictionaryAsync(x => x.Key, x => x.Toplam, ct);

            foreach (var grup in tahsisler.GroupBy(t => t.LedgerEntryId))
            {
                var satir = satirlar.FirstOrDefault(s => s.Id == grup.Key)
                    ?? throw new ValidationException("Kapatılacak kalem bulunamadı.");
                if (satir.AccountType != LedgerAccountType.Cari || satir.AccountRef != cariId)
                    throw new ValidationException("Kapatılacak kalem bu cariye ait değil.");
                if (satir.Direction != LedgerDirection.Debit)
                    throw new ValidationException("Yalnız BORÇ kalemleri kapatılabilir.");

                var satirBaz = satir.A * satir.R;
                var oncekiler = mevcut.TryGetValue(grup.Key, out var t0) ? t0 : 0m;
                var yeni = grup.Sum(x => x.KapatilanBaz);
                // Kuruş toleransı: satır bazı 4-6 haneli kurla türetilir, tahsis kuruşa yuvarlanır.
                if (oncekiler + yeni > satirBaz + 0.005m)
                    throw new ValidationException(
                        $"Kalem zaten kapatılmış ya da tutar kalemi aşıyor (kalem {satirBaz:N2}, önceki {oncekiler:N2}, istenen {yeni:N2}).");
            }

            // 2) BAKİYE ÇİTİ (ikincil): yuvarlanmamış karşılaştırma (adversarial M3 — iki tarafı
            //    ayrı ayrı yukarı yuvarlamak bakiyeyi 0,0044 aşırıyordu).
            var cariSatirlar = await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == cariId)
                .Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
                .ToListAsync(ct);
            var bakiye = cariSatirlar.Sum(r => r.Direction == LedgerDirection.Debit ? r.A * r.R : -(r.A * r.R));
            var tahsilTutar = tx.Amount.AmountInBase;
            if (bakiye <= 0m)
                throw new ValidationException("Carinin kapatılacak borcu yok (bakiye borçlu değil).");
            if (tahsilTutar > bakiye)
                throw new ValidationException(
                    $"Tahsil edilecek tutar ({tahsilTutar:N2}) carinin güncel borcunu ({bakiye:N2}) aşıyor.");

            tx.No = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.Tahsilat, ct);
            db.CashTransactions.Add(tx);
            db.AccountLedgerEntries.AddRange(entries);
            db.KapatmaTahsisleri.AddRange(tahsisler);
            await ApplyRentalDeltaAsync(db, tx, ct);   // kira bağlıysa Tahsilat/Bakiye atomik güncellenir

            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await dbTx.RollbackAsync(ct);
                RentACar.Application.Observability.RacarMetrics.LedgerIdempotentRejected();
                throw IdempotencyKisiti.Red(ex, MukerrerMesaji);
            }
        }, ct);
    }

    /// <summary>Kapatma serileştirme kilidi — depozito desenıyle aynı (pg_advisory_xact_lock).</summary>
    private static async Task KapatmaKilitAsync(AppDbContext db, Guid cariId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"kapatma:{db.TenantId}:{cariId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task PostDepozitoIslemAsync(
        Guid cariId, bool kontrolEt, DepozitoIrat? izKaydi,
        IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        // Dengelilik guard (PostAsync deseni).
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Defter dengesiz: borç {debit} ≠ alacak {credit}.");

        await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            // TOCTOU çiti (adversarial 1.2 Medium): (tenant, cari) danışma kilidi — bu carinin depozito
            // işlemleri tx sonuna dek sıralanır; bakiye kontrolü kilidin ARKASINDA yapılır → eşzamanlı
            // iki iade/irat toplamı tutulanı aşamaz.
            await DepozitoKilitAsync(db, cariId, ct);

            // F1.4 — ANAHTAR ÖNCE (kilidin arkasında): bu (SourceType, SourceId) kümesi bu kiracıda zaten
            // yazılmışsa çift gönderimdir → I3 sözleşmesiyle AYNI sessiz idempotent no-op. Bakiye çitinden
            // ÖNCE bakılır; yoksa tutulanın TAMAMINI iade eden bir gönderimin tekrarı "tutulanı aşamaz"
            // (400) alıyor, kısmi iadenin tekrarı sessiz geçiyordu — sonuç tutara bağlıydı. Anahtarsız
            // çağrıda SourceId taze Guid'dir, bu sorgu hiçbir şey bulmaz.
            var kumeSid = entries[0].SourceId;
            var kumeSt = entries[0].SourceType;
            if (await db.AccountLedgerEntries.AsNoTracking()
                    .AnyAsync(e => e.SourceType == kumeSt && e.SourceId == kumeSid, ct))
            {
                await dbTx.RollbackAsync(ct);
                return;
            }

            if (kontrolEt)
            {
                var rows = await db.AccountLedgerEntries.AsNoTracking()
                    .Where(e => e.AccountType == LedgerAccountType.Depozito && e.AccountRef == cariId)
                    .Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
                    .ToListAsync(ct);
                var tutulan = rows.Sum(r => r.Direction == LedgerDirection.Credit ? r.A * r.R : -(r.A * r.R));
                var cikan = entries
                    .Where(e => e.AccountType == LedgerAccountType.Depozito && e.Direction == LedgerDirection.Debit)
                    .Sum(e => e.Amount.AmountInBase);
                if (cikan > tutulan)
                    throw new ValidationException($"İşlem tutarı ({cikan}) tutulan depozitoyu ({tutulan}) aşamaz.");
            }

            if (izKaydi is not null)
            {
                // Kira bağı çiti: verilen kira BU carinin olmalı (yanlış araca gelir atfı engellenir).
                if (izKaydi.RentalId is { } rid)
                {
                    var musteri = await db.Rentals.Where(r => r.Id == rid)
                        .Select(r => (Guid?)r.MusteriId).FirstOrDefaultAsync(ct)
                        ?? throw new ValidationException("İrat için verilen kira bulunamadı.");
                    if (musteri != izKaydi.CariId)
                        throw new ValidationException("İrat kirası bu cariye ait değil.");
                }
                db.DepozitoIratlar.Add(izKaydi);
            }
            db.AccountLedgerEntries.AddRange(entries);
            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await dbTx.RollbackAsync(ct);
                // TENANT-GÖRÜNÜR teyit (adversarial 1.2 Low): aynı kayıt bu tenant'ta varsa gerçek
                // çift-submit → sessiz idempotent no-op (I3). Görünmüyorsa (çapraz-tenant PK çakışması)
                // sessiz yutmak geliri kaybettirir → net red.
                var sid = entries[0].SourceId;
                var st = entries[0].SourceType;
                var gorunur = izKaydi is not null
                    ? await db.DepozitoIratlar.AsNoTracking().AnyAsync(d => d.Id == izKaydi.Id, ct)
                    : await db.AccountLedgerEntries.AsNoTracking()
                        .AnyAsync(e => e.SourceType == st && e.SourceId == sid, ct);
                if (!gorunur)
                    throw new ValidationException("İşlem anahtarı başka bir kayıtla çakıştı — yeni anahtarla tekrar deneyin.");
            }
        }, ct);
    }

    /// <summary>(tenant, cari) kapsamlı pg_advisory_xact_lock — tx bitince otomatik bırakılır.</summary>
    private static async Task DepozitoKilitAsync(AppDbContext db, Guid cariId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"depozito:{db.TenantId}:{cariId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task PostBatchAsync(IReadOnlyList<CashPosting> items, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new ValidationException("Toplu işlem en az bir satır içermelidir.");

        // Her satır dengeli olmalı (yapısal invariant).
        foreach (var it in items)
        {
            var d = it.Entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var c = it.Entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            if (d != c) throw new ValidationException($"Defter dengesiz: borç {d} ≠ alacak {c}.");
        }

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var dbTx = await db.Database.BeginTransactionAsync(ct);

            // ATOMİK: tüm satırlar TEK transaction'da. No'lar boşluksuz; rollback olursa sıra geri alınır.
            foreach (var it in items)
            {
                it.Tx.No = await BelgeNoUretici.UretAsync(db, db.TenantId,
                    it.Tx.Tip == CashTransactionType.Odeme ? BelgeNoTuru.Tediye : BelgeNoTuru.Tahsilat, ct);
                db.CashTransactions.Add(it.Tx);
                db.AccountLedgerEntries.AddRange(it.Entries);
                await ApplyRentalDeltaAsync(db, it.Tx, ct); // atomik += (O1) + kira dövizi doğrulama (K2)
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // İşlem anahtarı çakışması (aynı toplu işlem yeniden gönderildi) → TÜM batch geri alınır (idempotent).
                await dbTx.RollbackAsync(ct);
                throw IdempotencyKisiti.Red(ex, "Bu toplu işlem zaten kaydedilmiş.");
            }
        }, ct);
    }

    public async Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Σ (Borç +AmountInBase, Alacak −AmountInBase). AmountInBase = Amount * Rate.
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == cariId)
            .Select(e => new { e.Direction, e.Amount })
            .ToListAsync(ct);
        return rows.Sum(r => r.Direction == LedgerDirection.Debit ? r.Amount.AmountInBase : -r.Amount.AmountInBase);
    }

    public async Task<decimal> GetDepozitoBakiyeAsync(Guid cariId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Depozito yükümlülük: Alacak (al) +AmountInBase, Borç (iade/mahsup) −AmountInBase → elde tutulan.
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Depozito && e.AccountRef == cariId)
            .Select(e => new { e.Direction, e.Amount })
            .ToListAsync(ct);
        return rows.Sum(r => r.Direction == LedgerDirection.Credit ? r.Amount.AmountInBase : -r.Amount.AmountInBase);
    }

    /// <summary>
    /// FAZ-59 — cari virman geçmişi. Künye tablosu sürücüdür; TUTAR defterin DEBIT bacağından
    /// okunur (künye para taşımaz → listedeki rakam ile carinin ekstresi ayrışamaz).
    ///
    /// <para>Künyesi olmayan ESKİ virmanlar (bu faz öncesi yazılmış defter satırları) listede
    /// GÖRÜNMEZ — künye tablosu o kayıtlar için hiç doldurulmadı. Bunları geriye dönük üretmek
    /// vade/makbuz/şube alanlarını UYDURMAK olurdu; boş künyeyle listelemek de "bilgi girilmemiş"
    /// ile "kayıt eski" ayrımını kaybettirirdi. Ekranda bu durum açıkça yazılıdır.</para>
    /// </summary>
    /// <summary>
    /// FAZ-50 adversarial M7 — künye SALT-YAZILIR kalmasın: kullanıcı formda doldurduğu Makbuz No
    /// ve İşlem Şubesi'ni bir daha göremiyordu. Tutar DEFTERDEN (Debit bacağı) okunur; künye
    /// para taşımaz (tek kaynak kuralı).
    /// </summary>
    /// <summary>
    /// FAZ-50 adversarial M7 + FAZ-58 — kasa/banka virman geçmişi.
    ///
    /// <para><b>DEFTER ÖNCELİKLİ (FAZ-58 düzeltmesi):</b> sorgu künye tablosundan DEĞİL
    /// <c>AccountLedgerEntry</c>'den başlar. Künyeden başlayan ilk sürüm, künye tablosu FAZ-50'de
    /// açıldığı için <b>ondan önceki bütün virmanları sessizce gizliyordu</b>. Defter otoritedir;
    /// künye (makbuz no / şube / işlemi yapan) varsa eklenir, yoksa satır künyesiz görünür.</para>
    ///
    /// <para>Tutar Debit (hedef) bacağından okunur — künye para taşımaz.</para>
    /// </summary>
    public async Task<IReadOnlyList<KasaVirmanSatirDto>> ListKasaVirmanlarAsync(
        KasaVirmanFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var f = filter ?? new KasaVirmanFilter();
        var limit = Math.Clamp(f.EnFazla, 1, 2000);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Virman");
        if (f.Bas is { } b) q = q.Where(e => e.EntryDateUtc >= b);
        if (f.Bit is { } t) q = q.Where(e => e.EntryDateUtc <= t);

        var bacaklar = await q
            .Select(e => new
            {
                e.SourceId, e.EntryDateUtc, e.AccountType, e.AccountRef, e.Direction,
                Tutar = e.Amount.Amount, e.Amount.Currency, e.Amount.Rate, e.Description
            })
            .ToListAsync(ct);
        if (bacaklar.Count == 0) return [];

        // Hesap süzgeci: kaynak VEYA hedef tarafı o hesap olan virmanlar.
        var gruplar = bacaklar.GroupBy(x => x.SourceId).ToList();
        if (f.HesapId is { } hid)
            gruplar = [.. gruplar.Where(g => g.Any(x => x.AccountRef == hid))];

        var idler = gruplar.Select(g => g.Key).ToList();
        var kunyeler = (await db.KasaVirmanBilgileri.AsNoTracking()
            .Where(k => idler.Contains(k.Id)).ToListAsync(ct)).ToDictionary(k => k.Id);

        var satirlar = new List<KasaVirmanSatirDto>(gruplar.Count);
        foreach (var g in gruplar)
        {
            // Dengeli çift: Debit = hedef (para giren), Credit = kaynak (para çıkan).
            var hedef = g.FirstOrDefault(x => x.Direction == LedgerDirection.Debit);
            var kaynak = g.FirstOrDefault(x => x.Direction == LedgerDirection.Credit);
            if (hedef is null || kaynak is null) continue;   // yarım küme olamaz (LedgerPoster dengeyi zorlar)

            kunyeler.TryGetValue(g.Key, out var k);
            satirlar.Add(new KasaVirmanSatirDto(
                g.Key, k?.Tarih ?? hedef.EntryDateUtc,
                kaynak.AccountType, hedef.AccountType,
                kaynak.AccountRef, hedef.AccountRef,
                hedef.Tutar, hedef.Currency, hedef.Rate,
                k?.MakbuzNo, k?.Sube, k?.IslemYapan, k?.Aciklama ?? hedef.Description,
                KunyeVar: k is not null));
        }

        if (!string.IsNullOrWhiteSpace(f.Ara))
        {
            var a = f.Ara.Trim();
            satirlar = [.. satirlar.Where(x =>
                (x.MakbuzNo?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Sube?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Aciklama?.Contains(a, StringComparison.OrdinalIgnoreCase) ?? false))];
        }

        return [.. satirlar.OrderByDescending(x => x.Tarih).ThenBy(x => x.Id).Take(limit)];
    }

    public async Task<IReadOnlyList<CariVirmanSatirDto>> ListCariVirmanlarAsync(
        CariVirmanFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.CariVirmanBilgileri.AsNoTracking();
        if (filter is not null)
        {
            if (filter.CariId is { } c) q = q.Where(x => x.KaynakCariId == c || x.HedefCariId == c);
            if (filter.Bas is { } b) q = q.Where(x => x.Tarih >= b);
            if (filter.Bit is { } t) q = q.Where(x => x.Tarih <= t);
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                q = q.Where(x => (x.MakbuzNo != null && EF.Functions.ILike(x.MakbuzNo, $"%{a}%"))
                              || (x.Aciklama != null && EF.Functions.ILike(x.Aciklama, $"%{a}%"))
                              || (x.Sube != null && EF.Functions.ILike(x.Sube, $"%{a}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 1000, 1, 10000);
        var kunyeler = await q.OrderByDescending(x => x.Tarih).Take(limit).ToListAsync(ct);
        if (kunyeler.Count == 0) return [];

        var idler = kunyeler.Select(k => k.Id).ToList();
        // Tutar DEFTERDEN: virmanın Debit bacağı (hedef cariye giren tutar) işlemin tutarıdır.
        var tutarlar = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "CariVirman" && e.Direction == LedgerDirection.Debit
                        && idler.Contains(e.SourceId))
            .Select(e => new { e.SourceId, e.Amount.Amount, e.Amount.Currency, e.Amount.Rate })
            .ToListAsync(ct);
        var tutarMap = tutarlar.GroupBy(x => x.SourceId)
            .ToDictionary(g => g.Key, g => g.First());

        var cariIds = kunyeler.SelectMany(k => new[] { k.KaynakCariId, k.HedefCariId }).Distinct().ToList();
        var adlar = (await db.Customers.AsNoTracking().Where(c => cariIds.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);
        string Ad(Guid id) => adlar.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)
            ? n : "(bilinmeyen cari)";

        return kunyeler.Select(k =>
        {
            tutarMap.TryGetValue(k.Id, out var t);
            return new CariVirmanSatirDto(
                k.Id, k.Tarih, k.Vade,
                k.KaynakCariId, Ad(k.KaynakCariId), k.HedefCariId, Ad(k.HedefCariId),
                t?.Amount ?? 0m, t?.Currency ?? "TRY", t?.Rate ?? 1m,
                k.MakbuzNo, k.Sube, k.IslemYapan, k.Aciklama);
        }).ToList();
    }

    public async Task<CariEkstreSonuc> GetCariStatementAsync(
        Guid cariId, CariEkstreFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var taban = db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == cariId);

        if (filter is null || filter.BosMu)
            return new CariEkstreSonuc(0m, await taban.OrderBy(e => e.EntryDateUtc).ToListAsync(ct));

        // Tarih DIŞI daraltmalar hem görünen satırlara hem DEVİR'e uygulanır: devir "aynı süzgeçten
        // geçen önceki hareketlerin toplamı" olmalı, yoksa yürüyen bakiye tutmaz.
        var suzulmus = taban;

        if (!string.IsNullOrWhiteSpace(filter.Doviz))
        {
            var d = filter.Doviz.Trim().ToUpperInvariant();
            suzulmus = suzulmus.Where(e => e.Amount.Currency.ToUpper() == d);
        }
        if (!string.IsNullOrWhiteSpace(filter.SourceType))
        {
            var st = filter.SourceType.Trim();
            suzulmus = suzulmus.Where(e => e.SourceType == st);
        }
        if (filter.KiraDurum is { } durum)
        {
            // Kira bağı DEFTERDE YOK: tahsilat/ödeme satırının SourceId'si CashTransaction'ı
            // gösterir, kira bağı orada (RentalId). Kira bağı olmayan satırlar (fatura, ceza,
            // araç satışı…) bu filtre açıkken listeden düşer — kasıtlı.
            var kiraIds = db.Rentals.Where(r => r.Durum == durum).Select(r => r.Id);
            var txIds = db.CashTransactions
                .Where(t => t.RentalId != null && kiraIds.Contains(t.RentalId.Value))
                .Select(t => t.Id);
            suzulmus = suzulmus.Where(e => txIds.Contains(e.SourceId));
        }

        var devir = 0m;
        if (filter.Bas is { } bas)
        {
            // SignedBase türetilmiş (mapped değil) → SQL'de yazılamaz; Debit/Credit ayrı toplanır.
            var oncekiler = suzulmus.Where(e => e.EntryDateUtc < bas);
            var borc = await oncekiler.Where(e => e.Direction == LedgerDirection.Debit)
                .SumAsync(e => (decimal?)(e.Amount.Amount * e.Amount.Rate), ct) ?? 0m;
            var alacak = await oncekiler.Where(e => e.Direction == LedgerDirection.Credit)
                .SumAsync(e => (decimal?)(e.Amount.Amount * e.Amount.Rate), ct) ?? 0m;
            devir = borc - alacak;

            suzulmus = suzulmus.Where(e => e.EntryDateUtc >= bas);
        }
        if (filter.Bit is { } bit) suzulmus = suzulmus.Where(e => e.EntryDateUtc <= bit);

        return new CariEkstreSonuc(devir, await suzulmus.OrderBy(e => e.EntryDateUtc).ToListAsync(ct));
    }

    public async Task<Dictionary<Guid, int>> GetRentalIslemSayilariAsync(
        IReadOnlyCollection<Guid> rentalIds, CancellationToken ct = default)
    {
        if (rentalIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Ters kayıtlar DAHİL sayılır: sayaç monoton artar → ters-kayıt-sonrası yeniden-tahsilat
        // yeni anahtar üretir (Tahsilat-toplamı eski değere dönebilirdi — o yüzden toplam değil SAYI).
        return await db.CashTransactions.AsNoTracking()
            .Where(t => t.RentalId != null && rentalIds.Contains(t.RentalId.Value))
            .GroupBy(t => t.RentalId!.Value)
            .Select(g => new { g.Key, Adet = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Adet, ct);
    }
}
