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

    public async Task<IReadOnlyList<PenaltySatir>> ListSatirAsync(Guid penaltyId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PenaltySatirlari.AsNoTracking()
            .Where(s => s.PenaltyId == penaltyId).OrderBy(s => s.Sira).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PenaltyOdeme>> ListOdemeAsync(Guid penaltyId, CancellationToken ct = default)
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
        if (f.Durum is CezaDurum d) q = q.Where(p => p.Durum == d);
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
        if (f.Bas is DateTimeOffset bas) q = q.Where(p => p.TebligTarihi >= bas);
        // Üst sınır GÜN DAHİL: çağıran gün başlangıcını verir, burada +1 gün açık aralık.
        if (f.Bit is DateTimeOffset bit) q = q.Where(p => p.TebligTarihi < bit.AddDays(1));
        // Ödeme durumu TUTARDAN türetilir (ayrı kolon yok → ayrışma imkânsız).
        q = f.OdemeDurum switch
        {
            CezaOdemeDurum.Odenmemis => q.Where(p => p.OdenenTutar <= 0m),
            CezaOdemeDurum.Kismi => q.Where(p => p.OdenenTutar > 0m && p.Kalan > 0m),
            CezaOdemeDurum.Odendi => q.Where(p => p.OdenenTutar > 0m && p.Kalan <= 0m),
            _ => q
        };

        var list = await q.OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);

        var vehIds = list.Where(p => p.VehicleId is not null).Select(p => p.VehicleId!.Value).Distinct().ToList();
        var cariIds = list.Where(p => p.CariId is not null).Select(p => p.CariId!.Value).Distinct().ToList();
        var kiraIds = list.Where(p => p.RentalId is not null).Select(p => p.RentalId!.Value).Distinct().ToList();

        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToDictionaryAsync(v => v.Id, v => v.Plaka, ct);
        var customers = await db.Customers.AsNoTracking().Where(c => cariIds.Contains(c.Id)).ToListAsync(ct);
        var custMap = customers.ToDictionary(c => c.Id);
        var rentals = await db.Rentals.AsNoTracking().Where(r => kiraIds.Contains(r.Id))
            .Select(r => new { r.Id, r.SozlesmeNo, r.Kaynak }).ToDictionaryAsync(r => r.Id, ct);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId != null && kiraIds.Contains(i.RentalId!.Value))
            .Select(i => new { i.Id, i.RentalId, i.No, i.Tarih }).ToListAsync(ct);
        var invMap = invoices.GroupBy(i => i.RentalId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Tarih).First());

        var penaltyIds = list.Select(p => p.Id).ToList();
        var satirlar = (await db.PenaltySatirlari.AsNoTracking()
                .Where(s => penaltyIds.Contains(s.PenaltyId)).OrderBy(s => s.Sira).ToListAsync(ct))
            .GroupBy(s => s.PenaltyId).ToDictionary(g => g.Key, g => (IReadOnlyList<PenaltySatir>)g.ToList());
        var odemeler = (await db.PenaltyOdemeleri.AsNoTracking()
                .Where(o => penaltyIds.Contains(o.PenaltyId)).OrderBy(o => o.Sira).ToListAsync(ct))
            .GroupBy(o => o.PenaltyId).ToDictionary(g => g.Key, g => (IReadOnlyList<PenaltyOdeme>)g.ToList());

        var rows = new List<PenaltyRow>(list.Count);
        foreach (var p in list)
        {
            var plaka = p.VehicleId is Guid vid && vehicles.TryGetValue(vid, out var pl) ? pl : null;
            Customer? cust = p.CariId is Guid cid && custMap.TryGetValue(cid, out var c) ? c : null;
            var kira = p.RentalId is Guid rid && rentals.TryGetValue(rid, out var r) ? r : null;
            var fat = p.RentalId is Guid rid2 && invMap.TryGetValue(rid2, out var i) ? i : null;

            // Plaka / müşteri süzgeçleri BELLEKTE (çözülmüş değer üzerinden) — kullanıcı
            // ekranda gördüğü metinle arar.
            if (!string.IsNullOrWhiteSpace(f.Plaka)
                && (plaka is null || plaka.Replace(" ", "").Contains(f.Plaka.Trim().Replace(" ", ""), StringComparison.OrdinalIgnoreCase) is false))
                continue;
            if (!string.IsNullOrWhiteSpace(f.Musteri))
            {
                var t = f.Musteri.Trim();
                var eslesti = cust is not null && (
                    cust.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || (cust.Email?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (cust.SiraNo?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
                if (!eslesti) continue;
            }

            rows.Add(new PenaltyRow(
                p, plaka, cust?.DisplayName, cust?.Email,
                kira?.SozlesmeNo, kira?.Kaynak, fat?.No, fat?.Tarih,
                satirlar.TryGetValue(p.Id, out var sl) ? sl : [],
                odemeler.TryGetValue(p.Id, out var ol) ? ol : []));
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

    public async Task CreateAsync(Penalty penalty, IReadOnlyList<PenaltySatir> satirlar, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            penalty.No = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.Ceza, ct);
            db.Penalties.Add(penalty);
            foreach (var s in satirlar) { s.PenaltyId = penalty.Id; db.PenaltySatirlari.Add(s); }
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

    public async Task<bool> ReflectAsync(
        Guid id, Func<Penalty, IReadOnlyList<AccountLedgerEntry>> buildEntries, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Satır kilidi: eşzamanlı yansıtmalar serileşir → çift yansıtma olmaz (idempotent).
            var penalty = await db.Penalties
                .FromSqlRaw("SELECT * FROM \"Penalties\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (penalty is null) return false;
            if (penalty.Durum != CezaDurum.Yeni) return false; // zaten yansıtılmış/işlenmiş

            var entries = buildEntries(penalty);
            var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            if (debit != credit)
                throw new ValidationException($"Ceza yansıtma defteri dengesiz: borç {debit} ≠ alacak {credit}.");

            penalty.Durum = CezaDurum.Yansitildi;
            penalty.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.AccountLedgerEntries.AddRange(entries);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    // ---------------- FAZ-60 kısmi ödeme (PARA) ----------------

    public async Task<CezaOdemeSonuc> PostOdemeAsync(
        Guid penaltyId, Guid satirId,
        Func<Penalty, PenaltySatir, decimal, int, (PenaltyOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () =>
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // (1) DANIŞMA KİLİDİ — cezanın TAMAMI (tüm kalemleri) için tek kilit. Kalem başına
            // kilitleseydik "başlık toplamı" iki kalemin eşzamanlı ödemesinde yarışırdı.
            await KilitAsync(db, penaltyId, ct);

            var ceza = await db.Penalties.FirstOrDefaultAsync(p => p.Id == penaltyId, ct)
                ?? throw new ValidationException("Ceza bulunamadı.");
            if (ceza.Durum == CezaDurum.Iptal) throw new ValidationException("İptal ceza ödenemez.");

            var satir = await db.PenaltySatirlari.FirstOrDefaultAsync(s => s.Id == satirId, ct)
                ?? throw new ValidationException("Ceza kalemi bulunamadı.");
            // Kalem BAŞKA cezaya aitse reddet (crafted POST → yanlış cezanın bakiyesi düşerdi).
            if (satir.PenaltyId != penaltyId)
                throw new ValidationException("Ceza kalemi bu cezaya ait değil.");

            // (2) KALAN YETKİLİ KAYNAKTAN: ödeme satırları toplanır. Önbellek kolonu (satir.Odenen)
            // ile MAX alınır — iki nedenle:
            //   (a) migration backfill'i eski "Odendi" cezaları Odenen=Tutar olarak işaretledi ama
            //       o ödemelerin PenaltyOdeme satırı YOK (o zaman böyle bir tablo yoktu); toplam
            //       tek başına alınsaydı kapanmış eski ceza YENİDEN ödenebilirdi (çift ödeme).
            //   (b) önbellek bir şekilde bozulursa MAX daima GÜVENLİ yöne (daha az kalan) sapar;
            //       aşırı ödeme üretemez.
            var odenmisKayit = await db.PenaltyOdemeleri.Where(o => o.SatirId == satirId)
                .SumAsync(o => (decimal?)o.Tutar, ct) ?? 0m;
            var odenmis = Math.Max(Yuvarla(odenmisKayit), Yuvarla(satir.Odenen));
            var kalan = Yuvarla(satir.Tutar - odenmis);
            if (kalan <= 0m) throw new ValidationException("Bu ceza kaleminde ödenecek bakiye yok.");

            var sira = await db.PenaltyOdemeleri.CountAsync(o => o.SatirId == satirId, ct) + 1;
            var (odeme, entries) = posting(ceza, satir, kalan, sira);

            // (3) AŞIM ÇİTİ + denge — kilidin ARKASINDA, yazmayla AYNI transaction'da.
            if (odeme.Tutar <= 0m) throw new ValidationException("Ödeme tutarı pozitif olmalıdır.");
            if (odeme.Tutar > kalan)
                throw new ValidationException($"Ödeme kalan bakiyeyi aşamaz (kalan {kalan}).");
            DengeKontrol(entries, odeme.Tutar);

            // (4) Kalem güncelle.
            satir.Odenen = Yuvarla(odenmis + odeme.Tutar);
            satir.Kalan = Yuvarla(satir.Tutar - satir.Odenen);
            if (satir.Kalan < 0m) throw new ValidationException("Kalan negatife düşemez.");
            satir.UpdatedAtUtc = DateTimeOffset.UtcNow;
            odeme.KalanSonrasi = satir.Kalan;
            odeme.PenaltyId = penaltyId;
            odeme.SatirId = satirId;
            odeme.Sira = sira;

            // (5) BAŞLIK toplamları SATIRLARDAN yeniden hesaplanır (fark yürümez).
            var digerOdenen = await db.PenaltySatirlari
                .Where(s => s.PenaltyId == penaltyId && s.Id != satirId)
                .SumAsync(s => (decimal?)s.Odenen, ct) ?? 0m;
            var toplamTutar = await db.PenaltySatirlari
                .Where(s => s.PenaltyId == penaltyId)
                .SumAsync(s => (decimal?)s.Tutar, ct) ?? 0m;
            ceza.Tutar = Yuvarla(toplamTutar);
            ceza.OdenenTutar = Yuvarla(digerOdenen + satir.Odenen);
            ceza.Kalan = Yuvarla(ceza.Tutar - ceza.OdenenTutar);
            if (ceza.Kalan < 0m) throw new ValidationException("Ceza kalanı negatife düşemez.");
            ceza.OdenmeTarihi = odeme.Tarih;
            ceza.Durum = ceza.Kalan <= 0m ? CezaDurum.Odendi : CezaDurum.Kismi;
            ceza.UpdatedAtUtc = DateTimeOffset.UtcNow;

            db.PenaltyOdemeleri.Add(odeme);
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
                throw new ValidationException("Bu ceza ödemesi zaten kaydedilmiş (çift gönderim).");
            }

            return new CezaOdemeSonuc(odeme.Id, satirId, sira, odeme.Tutar, satir.Kalan, ceza.Kalan, ceza.Durum);
        }, ct);
    }

    /// <summary>
    /// Ceza kapsamlı <c>pg_advisory_xact_lock</c> — transaction bitince otomatik bırakılır.
    /// Anahtar sabiti InvariantCulture ile üretilir (kültüre bağlı GUID/format sürprizi yok).
    /// </summary>
    private static async Task KilitAsync(AppDbContext db, Guid penaltyId, CancellationToken ct)
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
    private static decimal Yuvarla(decimal x) => decimal.Round(x, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Her kısmi adım KENDİ İÇİNDE dengeli olmalı. Denge tek başına yetmez: boş küme de
    /// (0 == 0) dengelidir ve defter YAZILMADAN bakiye düşerdi — bu yüzden borç toplamının
    /// ödeme tutarına eşitliği de burada zorlanır (FAZ-14 adversarial L6 dersi).
    /// </summary>
    private static void DengeKontrol(IReadOnlyList<AccountLedgerEntry> entries, decimal tutar)
    {
        if (entries.Count == 0)
            throw new ValidationException("Ceza ödemesi defter kaydı olmadan yazılamaz.");
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Ceza ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");
        var borcNative = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount);
        if (borcNative != tutar)
            throw new ValidationException($"Ceza ödemesi defterle uyuşmuyor: ödeme {tutar} ≠ defter borcu {borcNative}.");
    }
}
