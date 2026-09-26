using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Penalties;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Ceza kalıcılığı. Yansıtma satır kilidiyle (FOR UPDATE) idempotenttir.</summary>
public sealed class PenaltyRepository(IDbContextFactory<AppDbContext> factory) : IPenaltyRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Penalty>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Penalties.AsNoTracking().OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Penalty?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Penalties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<PenaltySatir>> ListLinesAsync(Guid penaltyId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PenaltySatirlari.AsNoTracking()
            .Where(s => s.PenaltyId == penaltyId).OrderBy(s => s.Sira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PenaltyOdeme>> ListPaymentsAsync(Guid penaltyId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PenaltyOdemeleri.AsNoTracking()
            .Where(o => o.PenaltyId == penaltyId)
            .OrderBy(o => o.SatirId).ThenBy(o => o.Sira).ToListAsync(ct);
    }

    /// <summary>
    /// FAZ-60 filtreli liste. Kolon değerleri (plaka / müşteri / sözleşme / fatura / kaynak)
    /// ilişkili tablolardan ÇÖZÜLÜR; hiçbir para alanı burada yeniden hesaplanmaz.
    /// </summary>
    public async Task<IReadOnlyList<PenaltyRow>> ListRowsAsync(PenaltyFilter? filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var f = filter ?? new PenaltyFilter();

        var q = db.Penalties.AsNoTracking().AsQueryable();
        if (f.Durum is PenaltyStatus d) q = q.Where(p => p.Durum == d);
        if (!string.IsNullOrWhiteSpace(f.MakbuzNo))
        {
            var mk = f.MakbuzNo.Trim();
            q = q.Where(p => p.MakbuzNo != null && EF.Functions.ILike(p.MakbuzNo, $"%{mk}%"));
        }
        if (!string.IsNullOrWhiteSpace(f.IslemSube))
        {
            var sb = f.IslemSube.Trim();
            q = q.Where(p => p.IslemSube != null && p.IslemSube.ToLower() == sb.ToLower());
        }
        if (f.Bas is DateTimeOffset start) q = q.Where(p => p.TebligTarihi >= start);
        // Üst sınır GÜN DAHİL: çağıran gün başlangıcını verir, burada +1 gün açık aralık.
        if (f.Bit is DateTimeOffset bit) q = q.Where(p => p.TebligTarihi < bit.AddDays(1));
        // Ödeme durumu TUTARDAN türetilir (ayrı kolon yok → ayrışma imkânsız).
        q = f.OdemeDurum switch
        {
            PenaltyPaymentStatus.Odenmemis => q.Where(p => p.OdenenTutar <= 0m),
            PenaltyPaymentStatus.Kismi => q.Where(p => p.OdenenTutar > 0m && p.Kalan > 0m),
            PenaltyPaymentStatus.Odendi => q.Where(p => p.OdenenTutar > 0m && p.Kalan <= 0m),
            _ => q
        };

        var list = await q.OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);

        var vehIds = list.Where(p => p.VehicleId is not null).Select(p => p.VehicleId!.Value).Distinct().ToList();
        var customerIds = list.Where(p => p.CariId is not null).Select(p => p.CariId!.Value).Distinct().ToList();
        var rentalIds = list.Where(p => p.RentalId is not null).Select(p => p.RentalId!.Value).Distinct().ToList();

        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToDictionaryAsync(v => v.Id, v => v.Plaka, ct);
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToListAsync(ct);
        var custMap = customers.ToDictionary(c => c.Id);
        var rentals = await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.Id))
            .Select(r => new { r.Id, r.SozlesmeNo, r.Kaynak }).ToDictionaryAsync(r => r.Id, ct);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId != null && rentalIds.Contains(i.RentalId!.Value))
            .Select(i => new { i.Id, i.RentalId, i.No, i.Tarih }).ToListAsync(ct);
        var invMap = invoices.GroupBy(i => i.RentalId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Tarih).First());

        var penaltyIds = list.Select(p => p.Id).ToList();
        var rowList = (await db.PenaltySatirlari.AsNoTracking()
                .Where(s => penaltyIds.Contains(s.PenaltyId)).OrderBy(s => s.Sira).ToListAsync(ct))
            .GroupBy(s => s.PenaltyId).ToDictionary(g => g.Key, g => (IReadOnlyList<PenaltySatir>)g.ToList());
        var payments = (await db.PenaltyOdemeleri.AsNoTracking()
                .Where(o => penaltyIds.Contains(o.PenaltyId)).OrderBy(o => o.Sira).ToListAsync(ct))
            .GroupBy(o => o.PenaltyId).ToDictionary(g => g.Key, g => (IReadOnlyList<PenaltyOdeme>)g.ToList());

        var rows = new List<PenaltyRow>(list.Count);
        foreach (var p in list)
        {
            var plate = p.VehicleId is Guid vid && vehicles.TryGetValue(vid, out var pl) ? pl : null;
            Customer? cust = p.CariId is Guid cid && custMap.TryGetValue(cid, out var c) ? c : null;
            var rental = p.RentalId is Guid rid && rentals.TryGetValue(rid, out var r) ? r : null;
            var fat = p.RentalId is Guid rid2 && invMap.TryGetValue(rid2, out var i) ? i : null;

            // Plaka / müşteri süzgeçleri BELLEKTE (çözülmüş değer üzerinden) — kullanıcı
            // ekranda gördüğü metinle arar.
            if (!string.IsNullOrWhiteSpace(f.Plaka)
                && (plate is null || plate.Replace(" ", "").Contains(f.Plaka.Trim().Replace(" ", ""), StringComparison.OrdinalIgnoreCase) is false))
                continue;
            if (!string.IsNullOrWhiteSpace(f.Musteri))
            {
                var t = f.Musteri.Trim();
                var matched = cust is not null && (
                    cust.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || (cust.Email?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (cust.SiraNo?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
                if (!matched) continue;
            }

            rows.Add(new PenaltyRow(
                p, plate, cust?.DisplayName, cust?.Email,
                rental?.SozlesmeNo, rental?.Kaynak, fat?.No, fat?.Tarih,
                rowList.TryGetValue(p.Id, out var sl) ? sl : [],
                payments.TryGetValue(p.Id, out var ol) ? ol : []));
        }
        return rows;
    }

    public async Task<IReadOnlyList<Penalty>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Penalties.AsNoTracking()
            .Where(p => p.RentalId == rentalId)
            .OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task CreateAsync(Penalty penalty, IReadOnlyList<PenaltySatir> rows, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            penalty.No = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.Ceza, ct);
            db.Penalties.Add(penalty);
            foreach (var s in rows) { s.PenaltyId = penalty.Id; db.PenaltySatirlari.Add(s); }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Penalty> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var p = await db.Penalties.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return false;
        apply(p);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateLockedAsync(Guid id, Action<Penalty> apply, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(db, id, ct); // yansıtma/ödemeyle aynı sıra: danışma → satır
            var penalty = await db.Penalties
                .FromSqlRaw("SELECT * FROM \"Penalties\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (penalty is null) return false;
            apply(penalty); // kilit altındaki GÜNCEL satırla denetler (fırlatırsa tx geri alınır)
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    public async Task<bool> ReflectAsync(
        Guid id, Func<Penalty, IReadOnlyList<AccountLedgerEntry>> buildEntries, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // #286 adversarial M1: ödeme ve iptalle AYNI danışma kilidi, aynı sırada (danışma → satır).
            await LockAsync(db, id, ct);
            // Satır kilidi: eşzamanlı yansıtmalar serileşir → çift yansıtma olmaz (idempotent).
            var penalty = await db.Penalties
                .FromSqlRaw("SELECT * FROM \"Penalties\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (penalty is null) return false;
            // F1.4: servis ön-kontrolüyle AYNI istisna — eskiden yarışı kaybeden ikinci yansıtma burada
            // sessizce false dönüyor (uç "başarılı" gösteriyordu), sıralı ikinci ise 400 alıyordu.
            if (penalty.Durum != PenaltyStatus.Yeni)
                throw new ValidationException("Yalnız 'Yeni' durumundaki ceza yansıtılabilir.");

            var entries = buildEntries(penalty);
            var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            if (debit != credit)
                throw new ValidationException($"Ceza yansıtma defteri dengesiz: borç {debit} ≠ alacak {credit}.");

            penalty.Durum = PenaltyStatus.Yansitildi;
            penalty.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.AccountLedgerEntries.AddRange(entries);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    // ---------------- FAZ-60 kısmi ödeme (PARA) ----------------

    private const string PaymentDuplicateMessage = "Bu ceza ödemesi zaten kaydedilmiş (çift gönderim).";

    public async Task<CezaOdemeSonuc> PostPaymentAsync(
        Guid penaltyId, Guid lineId,
        Func<Penalty, PenaltySatir, decimal, int, (PenaltyOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default, Guid? operationKey = null)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // (1) DANIŞMA KİLİDİ — cezanın TAMAMI (tüm kalemleri) için tek kilit. Kalem başına
            // kilitleseydik "başlık toplamı" iki kalemin eşzamanlı ödemesinde yarışırdı.
            await LockAsync(db, penaltyId, ct);

            // F1.4 — ANAHTAR ÖNCE (kilidin arkasında): aynı anahtarlı ikinci gönderim, kalan kontrolünden
            // ÖNCE mükerrer sayılır. Yoksa ilk ödeme kalemi TAMAMEN kapattıysa ikinci "ödenecek bakiye yok"
            // (400), kısmen kapattıysa kısıt (409) alıyordu — sonuç tutara bağlıydı.
            if (operationKey is Guid key && key != Guid.Empty &&
                await db.PenaltyOdemeleri.AsNoTracking().AnyAsync(o => o.IslemAnahtari == key, ct))
                throw new DuplicateOperationException(PaymentDuplicateMessage);

            // #286 adversarial M1: başlık satırı da kilitlenir (FOR UPDATE) — iptal/yansıtma aynı sırayla
            // (danışma → satır) kilitlendiği için Durum kilit altında GÜNCEL okunur; kilitsiz okunup ezilen
            // Durum (iptal→ödeme sonrası "Kismi") artık oluşamaz.
            var penalty = await db.Penalties
                .FromSqlRaw("SELECT * FROM \"Penalties\" WHERE \"Id\" = {0} FOR UPDATE", penaltyId)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("Ceza bulunamadı.");
            if (penalty.Durum == PenaltyStatus.Iptal) throw new ValidationException("İptal ceza ödenemez.");

            var row = await db.PenaltySatirlari.FirstOrDefaultAsync(s => s.Id == lineId, ct)
                ?? throw new ValidationException("Ceza kalemi bulunamadı.");
            // Kalem BAŞKA cezaya aitse reddet (crafted POST → yanlış cezanın bakiyesi düşerdi).
            if (row.PenaltyId != penaltyId)
                throw new ValidationException("Ceza kalemi bu cezaya ait değil.");

            // (2) KALAN YETKİLİ KAYNAKTAN: ödeme satırları toplanır. Önbellek kolonu (satir.Odenen)
            // ile MAX alınır — iki nedenle:
            //   (a) migration backfill'i eski "Odendi" cezaları Odenen=Tutar olarak işaretledi ama
            //       o ödemelerin PenaltyOdeme satırı YOK (o zaman böyle bir tablo yoktu); toplam
            //       tek başına alınsaydı kapanmış eski ceza YENİDEN ödenebilirdi (çift ödeme).
            //   (b) önbellek bir şekilde bozulursa MAX daima GÜVENLİ yöne (daha az kalan) sapar;
            //       aşırı ödeme üretemez.
            var paidRecord = await db.PenaltyOdemeleri.Where(o => o.SatirId == lineId)
                .SumAsync(o => (decimal?)o.Tutar, ct) ?? 0m;
            var paid = Math.Max(Round(paidRecord), Round(row.Odenen));
            var remaining = Round(row.Tutar - paid);
            if (remaining <= 0m) throw new ValidationException("Bu ceza kaleminde ödenecek bakiye yok.");

            var order = await db.PenaltyOdemeleri.CountAsync(o => o.SatirId == lineId, ct) + 1;
            var (payment, entries) = posting(penalty, row, remaining, order);

            // (3) AŞIM ÇİTİ + denge — kilidin ARKASINDA, yazmayla AYNI transaction'da.
            if (payment.Tutar <= 0m) throw new ValidationException("Ödeme tutarı pozitif olmalıdır.");
            if (payment.Tutar > remaining)
                throw new ValidationException($"Ödeme kalan bakiyeyi aşamaz (kalan {remaining}).");
            CheckBalanced(entries, payment.Tutar);

            // (4) Kalem güncelle.
            row.Odenen = Round(paid + payment.Tutar);
            row.Kalan = Round(row.Tutar - row.Odenen);
            if (row.Kalan < 0m) throw new ValidationException("Kalan negatife düşemez.");
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            payment.KalanSonrasi = row.Kalan;
            payment.PenaltyId = penaltyId;
            payment.SatirId = lineId;
            payment.Sira = order;

            // (5) BAŞLIK toplamları SATIRLARDAN yeniden hesaplanır (fark yürümez).
            var otherPaid = await db.PenaltySatirlari
                .Where(s => s.PenaltyId == penaltyId && s.Id != lineId)
                .SumAsync(s => (decimal?)s.Odenen, ct) ?? 0m;
            var totalAmount = await db.PenaltySatirlari
                .Where(s => s.PenaltyId == penaltyId)
                .SumAsync(s => (decimal?)s.Tutar, ct) ?? 0m;
            penalty.Tutar = Round(totalAmount);
            penalty.OdenenTutar = Round(otherPaid + row.Odenen);
            penalty.Kalan = Round(penalty.Tutar - penalty.OdenenTutar);
            if (penalty.Kalan < 0m) throw new ValidationException("Ceza kalanı negatife düşemez.");
            penalty.OdenmeTarihi = payment.Tarih;
            penalty.Durum = penalty.Kalan <= 0m ? PenaltyStatus.Odendi : PenaltyStatus.Kismi;
            penalty.UpdatedAtUtc = DateTimeOffset.UtcNow;

            db.PenaltyOdemeleri.Add(payment);
            db.AccountLedgerEntries.AddRange(entries);

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Deterministik anahtar / IslemAnahtari / defter kısmi index'i: çift gönderim →
                // HER ŞEY geri alınır (tek tx), bakiye DEĞİŞMEZ.
                await tx.RollbackAsync(ct);
                throw IdempotencyConstraint.Red(ex, PaymentDuplicateMessage);
            }

            return new CezaOdemeSonuc(payment.Id, lineId, order, payment.Tutar, row.Kalan, penalty.Kalan, penalty.Durum);
        }, ct);
    }

    /// <summary>
    /// Ceza kapsamlı <c>pg_advisory_xact_lock</c> — transaction bitince otomatik bırakılır.
    /// Anahtar sabiti InvariantCulture ile üretilir (kültüre bağlı GUID/format sürprizi yok).
    /// </summary>
    private static async Task LockAsync(AppDbContext db, Guid penaltyId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = string.Create(CultureInfo.InvariantCulture, $"ceza:{db.TenantId}:{penaltyId}");
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>Satır bazında yuvarlama — numeric(19,4) kolon hassasiyetiyle aynı.</summary>
    private static decimal Round(decimal x) => decimal.Round(x, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Her kısmi adım KENDİ İÇİNDE dengeli olmalı. Denge tek başına yetmez: boş küme de
    /// (0 == 0) dengelidir ve defter YAZILMADAN bakiye düşerdi — bu yüzden borç toplamının
    /// ödeme tutarına eşitliği de burada zorlanır (FAZ-14 adversarial L6 dersi).
    /// </summary>
    private static void CheckBalanced(IReadOnlyList<AccountLedgerEntry> entries, decimal amount)
    {
        if (entries.Count == 0)
            throw new ValidationException("Ceza ödemesi defter kaydı olmadan yazılamaz.");
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Ceza ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");
        var debitNative = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount);
        if (debitNative != amount)
            throw new ValidationException($"Ceza ödemesi defterle uyuşmuyor: ödeme {amount} ≠ defter borcu {debitNative}.");
    }
}
