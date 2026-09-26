using Microsoft.EntityFrameworkCore;
using RentACar.Application.GelenEFaturalar;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Salt-okunur defter sorgusu. Verilen hesap türleri + tarih aralığındaki AccountLedgerEntry
/// satırlarını base tutarıyla (Amount×Rate) döndürür. Tenant izolasyonu query filter + RLS ile
/// otomatik. Karmaşık-tip (Money) alanları ham kolon olarak çekilir; base bellek-içi hesaplanır.
/// </summary>
public sealed class ReportRepository(IDbContextFactory<AppDbContext> factory) : IReportRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    /// <summary>
    /// FAZ-57 — kasa/banka hareket satırlarının BELGE künyesi.
    ///
    /// <para><b>Cari GENERİK çözülür:</b> belge türü başına ayrı okuma yapmak yerine, aynı
    /// <c>SourceId</c>'yi paylaşan dengeli kümenin <c>Cari</c>/<c>Depozito</c> bacağının
    /// <c>AccountRef</c>'i alınır. Yeni bir para yolu eklendiğinde (ceza ödemesi, dış hizmet…)
    /// bu liste kendiliğinden çalışır — bakımı unutulacak ikinci bir tür listesi doğmaz.</para>
    ///
    /// <para>Evrak no / şube / kanal belge türüne özgüdür ve yalnız KÜNYE taşıyan tablolardan
    /// okunur; hiçbiri para hesabına girmez.</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, HareketBelgeDto>> GetMovementDocumentsAsync(
        IReadOnlyCollection<Guid> sourceIds, CancellationToken ct = default)
    {
        if (sourceIds.Count == 0) return new Dictionary<Guid, HareketBelgeDto>();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ids = sourceIds.Distinct().ToList();

        // (a) Cari/Depozito bacağından cari kimliği (generik).
        var accountLeg = (await db.AccountLedgerEntries.AsNoTracking()
                .Where(e => ids.Contains(e.SourceId) && e.AccountRef != null
                    && (e.AccountType == LedgerAccountType.Cari || e.AccountType == LedgerAccountType.Depozito))
                .Select(e => new { e.SourceId, Ref = e.AccountRef!.Value })
                .ToListAsync(ct))
            .GroupBy(x => x.SourceId)
            .ToDictionary(g => g.Key, g => g.First().Ref);

        // Cari adı PII ÇÖZMEDEN: DisplayName girdileri (Unvan/Ad/Soyad) düz-metin kolonlar.
        var customerIds = accountLeg.Values.Distinct().ToList();
        var customerNames = (await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad }).ToListAsync(ct))
            .ToDictionary(c => c.Id,
                c => new Customer { Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad }.DisplayName);

        // (b) Belge künyeleri — her biri KENDİ tablosundan; tutar okunmaz.
        var cash = (await db.CashTransactions.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.No, t.Kanal }).ToListAsync(ct)).ToDictionary(x => x.Id);
        var expense = (await db.Expenses.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.EvrakNo, e.Sube }).ToListAsync(ct)).ToDictionary(x => x.Id);
        var invoice = (await db.Invoices.AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.No }).ToListAsync(ct)).ToDictionary(x => x.Id);
        var transfer = (await db.KasaVirmanBilgileri.AsNoTracking().Where(k => ids.Contains(k.Id))
            .Select(k => new { k.Id, k.MakbuzNo, k.Sube }).ToListAsync(ct)).ToDictionary(x => x.Id);

        var result = new Dictionary<Guid, HareketBelgeDto>(ids.Count);
        foreach (var id in ids)
        {
            Guid? customerId = accountLeg.TryGetValue(id, out var c) ? c : null;
            string? documentNo = null, branch = null, channel = null;
            if (cash.TryGetValue(id, out var k)) { documentNo = k.No; channel = k.Kanal; }
            else if (expense.TryGetValue(id, out var g)) { documentNo = g.EvrakNo; branch = g.Sube; }
            else if (invoice.TryGetValue(id, out var f)) { documentNo = f.No; }
            else if (transfer.TryGetValue(id, out var v)) { documentNo = v.MakbuzNo; branch = v.Sube; }

            result[id] = new HareketBelgeDto(
                customerId,
                customerId is { } ci ? customerNames.GetValueOrDefault(ci) : null,
                documentNo, branch, channel);
        }
        return result;
    }

    public async Task<IReadOnlyList<LedgerRowDto>> GetLedgerRowsAsync(
        IReadOnlyCollection<LedgerAccountType> accountTypes,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => accountTypes.Contains(e.AccountType));
        if (from is { } f) q = q.Where(e => e.EntryDateUtc >= f);
        if (to is { } t) q = q.Where(e => e.EntryDateUtc <= t);

        var raw = await q
            .Select(e => new
            {
                e.EntryDateUtc, e.AccountType, e.Direction, e.SourceType, e.Description,
                Amount = e.Amount.Amount, Rate = e.Amount.Rate, Doviz = e.Amount.Currency, e.AccountRef,
                e.SourceId
            })
            .ToListAsync(ct);

        return raw
            .Select(r => new LedgerRowDto(
                r.EntryDateUtc, r.AccountType, r.Direction, r.SourceType, r.Description, r.Amount * r.Rate,
                r.AccountRef, r.Amount, r.Doviz, r.SourceId))   // FAZ-50 hesap/native + FAZ-57 belge kimliği
            .ToList();
    }

    public async Task<IReadOnlyList<CariLedgerRowDto>> GetAccountLedgerRowsAsync(
        DateTimeOffset? asOf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Cari);
        if (asOf is { } a) q = q.Where(e => e.EntryDateUtc <= a);

        var raw = await q
            .Select(e => new { e.AccountRef, e.Direction, Amount = e.Amount.Amount, Rate = e.Amount.Rate, e.EntryDateUtc })
            .ToListAsync(ct);

        // Cari adları: DisplayName mapped değil (computed) → Customers bellek-içi çekilip eşlenir.
        var names = (await db.Customers.AsNoTracking().ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);

        return raw
            .Select(r =>
            {
                var id = r.AccountRef ?? Guid.Empty;
                return new CariLedgerRowDto(
                    id, names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "(bilinmeyen cari)",
                    r.Direction, r.Amount * r.Rate, r.EntryDateUtc);
            })
            .ToList();
    }

    /// <summary>
    /// FAZ-62 — cari kart bilgileri. PII kolonları (TC/ehliyet/pasaport) BİLİNÇLİ olarak
    /// çekilmiyor: bakiye raporunun onlara ihtiyacı yok ve çözme maliyeti/riski gereksiz.
    /// Telefon/e-posta şifreli değil (Customer'da düz kolonlar).
    /// </summary>
    public async Task<IReadOnlyList<CariKartDto>> GetAccountCardsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Select(c => new CariKartDto(
                c.Id, c.CepTel, c.Email, c.BankaAdi, c.Doviz,
                c.OzelCariTip, c.Sinif, c.Tip == CustomerType.Kurumsal, c.Pasif, c.VergiNo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExtreOzetiRowDto>> GetStatementSummaryRowsAsync(
        ExtreOzetiFilter? filter, DateTimeOffset asOf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Kira/araç bağı OPSİYONEL (manuel fatura kirasız) → LEFT JOIN. INNER olsaydı manuel
        // faturalar sessizce düşerdi. İPTAL faturalar hiç alınmaz (borç değil).
        var q =
            from i in db.Invoices.AsNoTracking().Where(x => x.Durum != InvoiceStatus.Iptal)
            join c in db.Customers.AsNoTracking() on i.CariId equals c.Id into cg
            from c in cg.DefaultIfEmpty()
            join r in db.Rentals.AsNoTracking() on i.RentalId equals (Guid?)r.Id into rg
            from r in rg.DefaultIfEmpty()
            join v in db.Vehicles.AsNoTracking() on (Guid?)r.VehicleId equals (Guid?)v.Id into vg
            from v in vg.DefaultIfEmpty()
            select new { i, c, r, v };

        if (filter is not null)
        {
            if (filter.CariId is { } cid) q = q.Where(x => x.i.CariId == cid);
            if (filter.Bas is { } b) q = q.Where(x => x.i.Tarih >= b);
            if (filter.Bit is { } t) q = q.Where(x => x.i.Tarih <= t);
            if (filter.YalnizGecikmis) q = q.Where(x => x.i.VadeTarihi != null && x.i.VadeTarihi < asOf);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(x => x.r != null && x.r.CikisOfisi != null && x.r.CikisOfisi.Trim() == o);
            }
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{p}%"));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 2000, 1, 20000);
        var rows = await q
            // Vadesi olanlar önce ve en erken vade üstte; vadesizler sona.
            .OrderBy(x => x.i.VadeTarihi == null).ThenBy(x => x.i.VadeTarihi).ThenBy(x => x.i.CreatedAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.i.Id, x.i.No, x.i.Tarih, x.i.VadeTarihi, x.i.CariId, x.i.GenelToplam,
                x.i.Currency, x.i.Kur, x.i.IadeMi,
                CariAd = x.c == null ? null : (x.c.Tip == CustomerType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan),
                Plaka = x.v == null ? null : x.v.Plaka,
                SozlesmeNo = x.r == null ? null : x.r.SozlesmeNo,
                CikisOfisi = x.r == null ? null : x.r.CikisOfisi
            })
            .ToListAsync(ct);

        return rows.Select(x => new ExtreOzetiRowDto(
            x.Id, x.No, x.Tarih, x.VadeTarihi, x.CariId,
            string.IsNullOrWhiteSpace(x.CariAd) ? "(bilinmeyen cari)" : x.CariAd!.Trim(),
            x.Plaka, x.SozlesmeNo, x.CikisOfisi,
            x.GenelToplam, x.Currency, x.Kur, x.IadeMi)).ToList();
    }

    public async Task<IReadOnlyList<TahsilatMutabakatRowDto>> GetCollectionReconciliationRowsAsync(
        TahsilatMutabakatFilter? filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q =
            from r in db.Rentals.AsNoTracking()
            join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id into cg
            from c in cg.DefaultIfEmpty()
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id into vg
            from v in vg.DefaultIfEmpty()
            select new { r, c, v };

        if (filter is not null)
        {
            if (filter.MusteriId is { } m) q = q.Where(x => x.r.MusteriId == m);
            if (filter.Durum is { } d) q = q.Where(x => x.r.Durum == d);
            if (filter.Bas is { } b) q = q.Where(x => x.r.BasTar >= b);
            if (filter.Bit is { } t) q = q.Where(x => x.r.BasTar <= t);
            if (string.Equals(filter.BakiyeDurumu, "acik", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => x.r.Bakiye != 0m);
            else if (string.Equals(filter.BakiyeDurumu, "kapali", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => x.r.Bakiye == 0m);
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                // Plaka DB'de normalize ("34AA01"); kullanıcı boşluklu yazabilir → iki biçim de denenir.
                var searchPlate = a.ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.r.SozlesmeNo, $"%{a}%")
                              || (x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{searchPlate}%"))
                              || (x.c != null && x.c.Unvan != null && EF.Functions.ILike(x.c.Unvan, $"%{a}%"))
                              || (x.c != null && x.c.Ad != null && EF.Functions.ILike(x.c.Ad, $"%{a}%"))
                              || (x.c != null && x.c.Soyad != null && EF.Functions.ILike(x.c.Soyad, $"%{a}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 2000, 1, 20000);
        var rentals = await q.OrderByDescending(x => x.r.BasTar).Take(limit)
            .Select(x => new
            {
                x.r.Id, x.r.SozlesmeNo, x.r.MusteriId, x.r.BasTar, x.r.Durum, x.r.Doviz,
                x.r.Tutar, x.r.DamgaVergisi, x.r.GenelToplam, x.r.Tahsilat,
                Plaka = x.v == null ? null : x.v.Plaka,
                MusteriAd = x.c == null ? null : (x.c.Tip == CustomerType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan)
            })
            .ToListAsync(ct);
        if (rentals.Count == 0) return [];

        var ids = rentals.Select(k => k.Id).ToList();

        // FATURALANAN (iade-netli, iptal hariç) — OrtakSorgular.FarkStateAsync ile AYNI kural,
        // burada küme-bazlı: kira başına tek tek sorgu N+1 olurdu.
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && !i.IadeMi
                        && ((i.RentalId != null && ids.Contains(i.RentalId.Value))
                            || (i.KaynakKiraId != null && ids.Contains(i.KaynakKiraId.Value))))
            .Select(i => new { i.Id, i.GenelToplam, i.RentalId, i.KaynakKiraId })
            .ToListAsync(ct);
        var invoiceRental = invoices.ToDictionary(f => f.Id, f => f.RentalId ?? f.KaynakKiraId!.Value);
        var invoiceIds = invoices.Select(f => f.Id).ToList();
        var refunds = invoiceIds.Count == 0 ? [] : await db.Invoices.AsNoTracking()
            .Where(i => i.IadeMi && i.Durum != InvoiceStatus.Iptal
                        && i.KaynakFaturaId != null && invoiceIds.Contains(i.KaynakFaturaId.Value))
            .Select(i => new { i.GenelToplam, Kaynak = i.KaynakFaturaId!.Value })
            .ToListAsync(ct);

        var invoiced = rentals.ToDictionary(k => k.Id, _ => 0m);
        foreach (var f in invoices) invoiced[invoiceRental[f.Id]] += f.GenelToplam;
        foreach (var i in refunds)
            if (invoiceRental.TryGetValue(i.Kaynak, out var kid)) invoiced[kid] -= i.GenelToplam;

        // DEFTER TAHSİLATI: kasa hareketlerinden yeniden toplanır. İşaret/döviz kuralı
        // CashRepository.RentalDelta'dan gelir — ikinci bir kopya yazılmaz.
        var movements = await db.CashTransactions.AsNoTracking()
            .Where(t => t.RentalId != null && ids.Contains(t.RentalId.Value))
            .ToListAsync(ct);
        var rentalCurrency = rentals.ToDictionary(k => k.Id, k => k.Doviz);
        var ledgerCollection = rentals.ToDictionary(k => k.Id, _ => 0m);
        foreach (var t in movements)
        {
            var kid = t.RentalId!.Value;
            try { ledgerCollection[kid] += CashRepository.RentalDelta(t, rentalCurrency[kid]); }
            catch (RentACar.Application.Common.ValidationException)
            {
                // Kira dövizinden FARKLI bir hareket: RentalDelta bunu reddeder. Raporun görevi
                // hatayı GÖSTERMEK, çökmek değil → katkısı 0 kalır ve satır "tutarsız" görünür.
            }
        }

        // Müşteri bakiyesi (defterden) — sözleşme bakiyesiyle karıştırılmasın diye ayrı kolon.
        var customerIds = rentals.Select(k => k.MusteriId).Distinct().ToList();
        var accountMovement = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef != null
                        && customerIds.Contains(e.AccountRef.Value))
            .Select(e => new { e.AccountRef, e.Direction, Tutar = e.Amount.Amount * e.Amount.Rate })
            .ToListAsync(ct);
        var customerBalance = accountMovement
            .GroupBy(e => e.AccountRef!.Value)
            .ToDictionary(g => g.Key,
                g => g.Sum(e => e.Direction == LedgerDirection.Debit ? e.Tutar : -e.Tutar));

        var result = rentals.Select(k => new TahsilatMutabakatRowDto(
            k.Id, k.SozlesmeNo, k.Plaka, k.MusteriId,
            string.IsNullOrWhiteSpace(k.MusteriAd) ? "(bilinmeyen cari)" : k.MusteriAd!.Trim(),
            k.BasTar, k.Durum, k.Doviz ?? "TRY",
            k.Tutar, k.DamgaVergisi ?? 0m, k.GenelToplam,
            k.Tahsilat, ledgerCollection[k.Id], invoiced[k.Id],
            customerBalance.GetValueOrDefault(k.MusteriId))).ToList();

        return filter?.YalnizTutarsiz == true ? result.Where(x => x.Tutarsiz).ToList() : result;
    }

    public async Task<IReadOnlyList<EkHizmetDetayRow>> GetAddOnDetailRowsAsync(
        EkHizmetDetayFilter? filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // İPTAL kiralar hariç — özet rapor da öyle davranıyor (iki görünüm ayrışmasın).
        var q =
            from a in db.RentalAddOns.AsNoTracking()
            join r in db.Rentals.AsNoTracking().Where(x => x.Durum != RentalStatus.Iptal)
                on a.RentalId equals r.Id
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id into vg
            from v in vg.DefaultIfEmpty()
            join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id into cg
            from c in cg.DefaultIfEmpty()
            join res in db.Reservations.AsNoTracking() on r.ReservationId equals (Guid?)res.Id into rg
            from res in rg.DefaultIfEmpty()
            join t in db.EkHizmetTanimlari.AsNoTracking() on a.EkHizmetTanimId equals t.Id into tg
            from t in tg.DefaultIfEmpty()
            join p in db.Personeller.AsNoTracking() on a.PersonelId equals (Guid?)p.Id into pg
            from p in pg.DefaultIfEmpty()
            select new { a, r, v, c, rez = res, t, p };

        if (filter is not null)
        {
            if (filter.Bas is { } b) q = q.Where(x => x.a.CreatedAtUtc >= b);
            if (filter.Bit is { } t2) q = q.Where(x => x.a.CreatedAtUtc <= t2);
            if (filter.PersonelId is { } pid) q = q.Where(x => x.a.PersonelId == pid);
            if (filter.SistemKalemleriniGizle)
                q = q.Where(x => x.t == null || !x.t.Kod.StartsWith("SYS-"));
            if (!string.IsNullOrWhiteSpace(filter.RezKaynagi))
            {
                var k = filter.RezKaynagi.Trim();
                q = q.Where(x => x.rez != null && x.rez.Kaynak != null && x.rez.Kaynak.Trim() == k);
            }
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(x => x.r.CikisOfisi != null && x.r.CikisOfisi.Trim() == o);
            }
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a2 = filter.Ara.Trim();
                var searchPlate = a2.ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.a.Ad, $"%{a2}%")
                              || EF.Functions.ILike(x.r.SozlesmeNo, $"%{a2}%")
                              || (x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{searchPlate}%"))
                              || (x.c != null && x.c.Unvan != null && EF.Functions.ILike(x.c.Unvan, $"%{a2}%"))
                              || (x.c != null && x.c.Ad != null && EF.Functions.ILike(x.c.Ad, $"%{a2}%"))
                              || (x.c != null && x.c.Soyad != null && EF.Functions.ILike(x.c.Soyad, $"%{a2}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 2000, 1, 20000);
        var rows = await q.OrderByDescending(x => x.a.CreatedAtUtc).Take(limit)
            .Select(x => new
            {
                AddOnId = x.a.Id, x.a.RentalId, x.r.SozlesmeNo, x.r.BasTar, x.r.BitTar,
                Plaka = x.v == null ? null : x.v.Plaka,
                MusteriAd = x.c == null ? null : (x.c.Tip == CustomerType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan),
                RezKaynagi = x.rez == null ? null : x.rez.Kaynak,
                x.r.CikisOfisi,
                x.a.Ad, x.a.Miktar, x.a.BirimNetFiyat, x.a.KdvOrani,
                x.a.NetTutar, x.a.KdvTutar, x.a.Toplam, x.a.CreatedAtUtc,
                Personel = x.p == null ? null : (x.p.Ad + " " + x.p.Soyad),
                SistemKalemi = x.t != null && x.t.Kod.StartsWith("SYS-")
            })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        // İLK TAHSİLAT: kiranın EN ERKEN tahsilatı. Kalem-bazlı tahsilat izi sistemde YOK —
        // bu yüzden kalemin değil KİRANIN ilk tahsilatıdır ve kolon başlığı da öyle der.
        var rentalIds = rows.Select(x => x.RentalId).Distinct().ToList();
        // Ters kayıt AYRI bir satırdır ve orijinali işaretlemez (TersAlinanId ile ona bakar).
        // Bu yüzden yalnız `!TersKayitMi` demek YETMEZ: iptal edilmiş bir tahsilat "ilk tahsilat"
        // olarak görünürdü. Hem ters-kayıt satırları hem TERS ALINMIŞ orijinaller elenir.
        var reversed = db.CashTransactions.AsNoTracking()
            .Where(t => t.TersAlinanId != null).Select(t => t.TersAlinanId!.Value);
        var firstCollection = (await db.CashTransactions.AsNoTracking()
                .Where(t => t.RentalId != null && rentalIds.Contains(t.RentalId.Value)
                            && t.Tip == CashTransactionType.Tahsilat && !t.TersKayitMi
                            && !reversed.Contains(t.Id))
                .Select(t => new { Kira = t.RentalId!.Value, t.Tarih, Tutar = t.Amount.Amount })
                .ToListAsync(ct))
            .GroupBy(t => t.Kira)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Tarih).First().Tutar);

        return rows.Select(x => new EkHizmetDetayRow(
            x.AddOnId, x.RentalId, x.SozlesmeNo, x.BasTar, x.BitTar,
            x.Plaka ?? "(bilinmeyen araç)",
            string.IsNullOrWhiteSpace(x.MusteriAd) ? "(bilinmeyen cari)" : x.MusteriAd!.Trim(),
            x.RezKaynagi, x.CikisOfisi,
            x.Ad, x.Miktar, x.BirimNetFiyat, x.KdvOrani, x.NetTutar, x.KdvTutar, x.Toplam,
            x.CreatedAtUtc, x.Personel,
            firstCollection.TryGetValue(x.RentalId, out var it) ? it : null,
            x.SistemKalemi)).ToList();
    }

    public async Task<KarsilastirmaliAnalizDto> GetComparativeAnalysisAsync(
        KarsilastirmaliAnalizFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var table = string.Equals(filter.Tablo, "Rezervasyon", StringComparison.OrdinalIgnoreCase)
            ? "Rezervasyon" : "Kira";
        var isDay = string.Equals(filter.VeriTuru, "Gun", StringComparison.OrdinalIgnoreCase);
        var breakdown = filter.Kirilim switch
        {
            "RezKaynagi" => "RezKaynagi",
            "CikisNoktasi" => "CikisNoktasi",
            _ => "AracGrubu"
        };

        // Varsayılan pencere: son 12 ayın BAŞI (ayın 1'i) → bugün. Kullanıcı verirse o kullanılır.
        var bit = filter.Bit ?? DateTimeOffset.UtcNow;
        var start = filter.Bas ?? new DateTimeOffset(
            new DateTime(bit.UtcDateTime.Year, bit.UtcDateTime.Month, 1).AddMonths(-11), TimeSpan.Zero);

        // Araç grubu kırılımı için plaka→grup eşlemesi gerekiyor (Vehicle.Grup METİN alanı).
        var vehicleGroup = breakdown == "AracGrubu"
            ? await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Grup })
                .ToDictionaryAsync(v => v.Id, v => v.Grup, ct)
            : [];

        List<(DateTimeOffset Tarih, string Kirilim, decimal Deger)> raw;
        if (table == "Rezervasyon")
        {
            var q = db.Reservations.AsNoTracking().Where(r => r.Durum != ReservationStatus.Iptal);
            q = q.Where(r => r.BasTar >= start && r.BasTar <= bit);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
            }
            var rows = await q.Select(r => new { r.BasTar, r.Gun, r.Kaynak, r.CikisOfisi, r.VehicleId })
                .ToListAsync(ct);
            raw = rows.Select(r => (r.BasTar, Kirilim: breakdown switch
            {
                "RezKaynagi" => r.Kaynak,
                "CikisNoktasi" => r.CikisOfisi,
                _ => vehicleGroup.GetValueOrDefault(r.VehicleId)
            } ?? "", Deger: isDay ? r.Gun : 1m)).ToList();
        }
        else
        {
            var q = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal);
            q = q.Where(r => r.BasTar >= start && r.BasTar <= bit);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
            }
            var rows = await q.Select(r => new { r.BasTar, r.Gun, r.Kaynak, r.CikisOfisi, r.VehicleId })
                .ToListAsync(ct);
            raw = rows.Select(r => (r.BasTar, Kirilim: breakdown switch
            {
                "RezKaynagi" => r.Kaynak,
                "CikisNoktasi" => r.CikisOfisi,
                _ => vehicleGroup.GetValueOrDefault(r.VehicleId)
            } ?? "", Deger: isDay ? r.Gun : 1m)).ToList();
        }

        // Ay kolonları pencereden ÜRETİLİR (veriden değil): veri olmayan ay da kolon olarak görünür,
        // aksi hâlde "o ay hiç iş yok" bilgisi grid'den sessizce kaybolurdu.
        var months = new List<string>();
        var cursor = new DateTime(start.UtcDateTime.Year, start.UtcDateTime.Month, 1);
        var lastMonth = new DateTime(bit.UtcDateTime.Year, bit.UtcDateTime.Month, 1);
        while (cursor <= lastMonth && months.Count < 120)   // üst sınır: absürt aralıkta kolon patlamasın
        {
            months.Add(cursor.ToString("yyyy-MM"));
            cursor = cursor.AddMonths(1);
        }

        var rowList = raw
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Kirilim) ? "(belirtilmemiş)" : x.Kirilim.Trim())
            .Select(g => new KarsilastirmaliSatirDto(
                g.Key,
                g.GroupBy(x => x.Tarih.UtcDateTime.ToString("yyyy-MM"))
                 .ToDictionary(a => a.Key, a => a.Sum(x => x.Deger))))
            .OrderByDescending(s => s.Toplam).ThenBy(s => s.Kirilim)
            .ToList();

        return new KarsilastirmaliAnalizDto(months, rowList, table,
            isDay ? "Gun" : "Adet", breakdown);
    }

    public async Task<IReadOnlyList<VehicleStatus>> GetVehicleStatusesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tek doğruluk kaynağı (denetim O12b): WhatsApp operasyon özeti de AYNI kaynağı kullanır.
        return await SharedQueries.VehicleStatusesAsync(db, ct);
    }

    public async Task<IReadOnlyList<DolulukKiraRowDto>> GetRentalIntervalsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // İptal hariç; efektif bitiş = gerçek dönüş ?? planlı bitiş. Dönemle çakışanlar:
        // efektifBitiş >= from AND Bas <= to. (?? → COALESCE; EF çevirir.)
        var raw = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit >= from && r.BasTar <= to)
            .ToListAsync(ct);

        return raw.Select(r => new DolulukKiraRowDto(r.BasTar, r.Bit)).ToList();
    }

    // ---- FAZ-77 — filo & doluluk kırılımı ----

    /// <summary>Atıf etiketi: boş/null şube-grup değerleri TEK kovada toplanır ki sayımlar kaybolmasın.</summary>
    private const string Unassigned = "(Atanmamış)";

    private static string Label(string? s) => string.IsNullOrWhiteSpace(s) ? Unassigned : s.Trim();

    public async Task<IReadOnlyList<SigortaMuayeneRow>> GetInsuranceInspectionRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // TÜM araçlar (satılmış/pasif dahil): belge envanteri, kiralanabilirlik değil. Eksik belge
        // görünmediğinde rapor işe yaramaz.
        var vehicles = await db.Vehicles.AsNoTracking().ToListAsync(ct);
        if (vehicles.Count == 0) return [];

        // Araç başına EN GEÇ biten poliçe/muayene alınır (yenilenmiş belgede eski satır değil,
        // GEÇERLİ olan görünmeli).
        var policies = await db.InsurancePolicies.AsNoTracking()
            .Select(p => new { p.VehicleId, p.Tip, p.Bitis }).ToListAsync(ct);
        var inspections = await db.InspectionRecords.AsNoTracking()
            .Select(m => new { m.VehicleId, m.Bitis }).ToListAsync(ct);
        // MTV: ÖDENMEMİŞ olanın en yakın vadesi; hepsi ödendiyse en geç vade (bilgi).
        var mtvs = await db.MtvRecords.AsNoTracking()
            .Select(m => new { m.VehicleId, m.Vade, m.Odendi }).ToListAsync(ct);

        DateTimeOffset? LastPolicy(Guid vid, RentACar.Domain.Enums.InsuranceType tip)
            => policies.Where(p => p.VehicleId == vid && p.Tip == tip)
                .Select(p => (DateTimeOffset?)p.Bitis).DefaultIfEmpty(null).Max();

        return vehicles.Select(v =>
        {
            var mtvOpen = mtvs.Where(m => m.VehicleId == v.Id && !m.Odendi).ToList();
            var mtvAll = mtvs.Where(m => m.VehicleId == v.Id).ToList();
            DateTimeOffset? mtvDue = mtvOpen.Count > 0
                ? mtvOpen.Min(m => m.Vade)
                : mtvAll.Count > 0 ? mtvAll.Max(m => m.Vade) : null;

            return new SigortaMuayeneRow(
                v.Id, v.Plaka, v.Marka, v.Tip, v.ModelYili,
                v.Yakit?.ToString(), v.Vites?.ToString(), v.Sube, v.Grup,
                v.SasiNo, v.MotorNo, v.AracSahibi, v.BelgeNo, v.Kimde,
                LastPolicy(v.Id, RentACar.Domain.Enums.InsuranceType.Trafik),
                LastPolicy(v.Id, RentACar.Domain.Enums.InsuranceType.Kasko),
                inspections.Where(m => m.VehicleId == v.Id).Select(m => (DateTimeOffset?)m.Bitis).DefaultIfEmpty(null).Max(),
                mtvDue, mtvOpen.Count == 0 && mtvAll.Count > 0,
                v.ZIzni, v.ZIzniBitis, v.SeyrusiferBitis);
        })
        .OrderBy(r => r.Plaka, StringComparer.CurrentCulture)
        .ToList();
    }

    public async Task<FiloSubeHamPaket> GetFleetBranchRawAsync(
        DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var vehicles = await db.Vehicles.AsNoTracking()
            .Select(v => new { v.Id, v.Sube, v.Durum, v.FiloDurum })
            .ToListAsync(ct);
        var branchDisplayName = vehicles.ToDictionary(v => v.Id, v => Label(v.Sube));

        // Kira/rezervasyon/BAF ARACIN şubesine yazılır — sözleşmenin çıkış şubesine DEĞİL
        // (tek atıf kuralı; karışık atıf satırı kendi içinde tutarsız yapardı).
        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.VehicleId, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit >= windowStart && r.BasTar <= windowEnd)
            .ToListAsync(ct);

        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.Durum != ReservationStatus.Iptal
                        && r.BasTar >= windowStart && r.BasTar <= windowEnd)
            .Select(r => new { r.VehicleId, r.BasTar })
            .ToListAsync(ct);

        var bafs = await db.Baflar.AsNoTracking()
            .Where(b => b.Durum == BafStatus.Acik)
            .Select(b => b.VehicleId)
            .ToListAsync(ct);

        // Silinmiş araca bağlı satır sözlükte yoktur → "(Atanmamış)" kovasına düşer, sessizce kaybolmaz.
        string Search(Guid id) => branchDisplayName.TryGetValue(id, out var s) ? s : Unassigned;

        return new FiloSubeHamPaket(
            vehicles.Select(v => new FiloAracHamRow(v.Id, Label(v.Sube), v.Durum, v.FiloDurum)).ToList(),
            rentals.Select(r => new FiloKiraHamRow(Search(r.VehicleId), r.BasTar, r.Bit)).ToList(),
            reservations.Select(r => (Search(r.VehicleId), r.BasTar)).ToList(),
            bafs.Select(Search).ToList());
    }

    public async Task<DolulukAtifPaket> GetOccupancyAttributionAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var vehicles = await db.Vehicles.AsNoTracking()
            .Select(v => new { v.Id, v.Sube, v.Grup })
            .ToListAsync(ct);
        var attribution = vehicles.ToDictionary(v => v.Id, v => (Sube: Label(v.Sube), Grup: Label(v.Grup)));

        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.VehicleId, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit >= from && r.BasTar <= to)
            .ToListAsync(ct);

        // Rezervasyon: kiraya çevrilmiş olan HARİÇ — aksi hâlde aynı gün hem rezervasyon hem kira
        // olarak sayılır ve "Rez Doluluk" kira ile çift-sayılırdı.
        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.Durum != ReservationStatus.Iptal && r.Durum != ReservationStatus.KirayaCevrildi)
            .Select(r => new { r.VehicleId, r.BasTar, r.BitTar, r.Kaynak })
            .Where(r => r.BitTar >= from && r.BasTar <= to)
            .ToListAsync(ct);

        (string Sube, string Grup) Search(Guid id)
            => attribution.TryGetValue(id, out var a) ? a : (Unassigned, Unassigned);

        return new DolulukAtifPaket(
            vehicles.Select(v => new DolulukAracAtifRow(v.Id, Label(v.Sube), Label(v.Grup))).ToList(),
            rentals.Select(r =>
            {
                var a = Search(r.VehicleId);
                return new DolulukKiraAtifRow(r.BasTar, r.Bit, r.VehicleId, a.Sube, a.Grup);
            }).ToList(),
            reservations.Select(r =>
            {
                var a = Search(r.VehicleId);
                return new DolulukRezAtifRow(r.BasTar, r.BitTar, r.VehicleId, a.Sube, a.Grup, Label(r.Kaynak));
            }).ToList());
    }

    public async Task<TahsilatFaturaDto> GetCollectionInvoiceAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Fatura: İptal hariç; base = GenelToplam × Kur (Kur düz kolon, bellek-içi çarpılır).
        var fq = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } ff) fq = fq.Where(i => i.Tarih >= ff);
        if (to is { } ft) fq = fq.Where(i => i.Tarih <= ft);
        var invoices = await fq.Select(i => new { i.GenelToplam, i.Kur, i.IadeMi }).ToListAsync(ct);
        int invoiceCount = invoices.Count;
        // İade faturası net toplamı DÜŞÜRÜR (mutabakat: fatura vs tahsilat doğru netleşsin).
        decimal invoiceTotal = invoices.Sum(f => f.GenelToplam * f.Kur * (f.IadeMi ? -1m : 1m));

        // Tahsilat: Tip=Tahsilat, ters kayıt hariç; base = Amount × Rate.
        var tq = db.CashTransactions.AsNoTracking()
            .Where(c => c.Tip == CashTransactionType.Tahsilat && !c.TersKayitMi);
        if (from is { } tf) tq = tq.Where(c => c.Tarih >= tf);
        if (to is { } tt) tq = tq.Where(c => c.Tarih <= tt);
        var collections = await tq
            .Select(c => new { Amount = c.Amount.Amount, Rate = c.Amount.Rate }).ToListAsync(ct);
        int collectionCount = collections.Count;
        decimal collectionTotal = collections.Sum(t => t.Amount * t.Rate);

        return new TahsilatFaturaDto(
            invoiceCount, invoiceTotal, collectionCount, collectionTotal, invoiceTotal - collectionTotal);
    }

    public async Task<int> GetActiveRentalCountAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking().CountAsync(r => r.Durum == RentalStatus.Kirada, ct);
    }

    public async Task<IReadOnlyList<ServiceCostRowDto>> GetServiceCostRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.ServiceRecords.AsNoTracking().Where(r => r.Durum == ServiceStatus.Tamamlandi);
        if (from is { } f) q = q.Where(r => r.CikisTarihi >= f);
        if (to is { } t) q = q.Where(r => r.CikisTarihi <= t);

        var rows = await q
            .Select(r => new { r.VehicleId, r.Tip, r.ToplamIscilik })
            .ToListAsync(ct);

        var plate = (await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);

        return rows
            .Select(r => new ServiceCostRowDto(
                r.VehicleId, plate.TryGetValue(r.VehicleId, out var p) ? p : "(bilinmeyen araç)", r.Tip, r.ToplamIscilik))
            .ToList();
    }

    public async Task<IReadOnlyList<PeriyodikServisRow>> GetPeriodicServiceRowsAsync(
        PeriyodikServisFilter? filter = null, CancellationToken ct = default)
    {
        // FAZ 6.2: birleşim OrtakSorgular'a taşındı — rapor sayfası ve FiloBildirimUretici (bakım-km
        // bildirimi) AYNI tanımı kullanır (O12a deseni; iki kopya sessizce ayrışmasın).
        // FAZ-76: filtre YALNIZ rapor yolundan geçer; üretici parametresiz çağırmaya devam eder.
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SharedQueries.PeriodicServiceAsync(db, ct, filter);
    }

    public async Task<IReadOnlyList<KmDetayRow>> GetKmDetailRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Rentals.AsNoTracking().Where(r => r.CikisKm != null && r.DonusKm != null);
        if (from is { } f) q = q.Where(r => r.BasTar >= f);
        if (to is { } t) q = q.Where(r => r.BasTar <= t);

        var rows = await q
            .Select(r => new { r.Id, r.SozlesmeNo, r.VehicleId, r.CikisKm, r.DonusKm, r.KmLimit,
                r.FazlaKm, r.FazlaKmBedeli, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .ToListAsync(ct);

        // FAZ-76: araç JOIN'i yalnız Plaka için yapılıyordu; künye kolonları eklendi.
        var vehicle = (await db.Vehicles.AsNoTracking()
                .Select(v => new { v.Id, v.Plaka, v.Marka, v.Tip, v.Yakit, v.Vites }).ToListAsync(ct))
            .ToDictionary(v => v.Id);

        return rows
            .Select(r =>
            {
                var v = vehicle.GetValueOrDefault(r.VehicleId);
                return new KmDetayRow(
                    r.Id, r.SozlesmeNo, v?.Plaka ?? "(bilinmeyen araç)",
                    r.CikisKm!.Value, r.DonusKm!.Value, r.DonusKm!.Value - r.CikisKm!.Value,
                    r.KmLimit, r.FazlaKm, r.FazlaKmBedeli,
                    v?.Marka, v?.Tip, v?.Yakit?.ToString(), v?.Vites?.ToString(), r.BasTar, r.Bit);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RezervasyonKaynakRow>> GetReservationSourceRowsAsync(
        RezervasyonKaynakFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Reservations.AsNoTracking();

        // FAZ-76: tarih hangi alana uygulanacak — çıkış (varsayılan, eski davranış), dönüş ya da
        // kayıt tarihi. Eskiden yalnız BasTar vardı ve seçenek yoktu.
        if (filter.Bas is { } f)
            q = filter.TarihTipi switch
            {
                "Donus" => q.Where(r => r.BitTar >= f),
                "Kayit" => q.Where(r => r.CreatedAtUtc >= f),
                _ => q.Where(r => r.BasTar >= f)
            };
        if (filter.Bit is { } t)
            q = filter.TarihTipi switch
            {
                "Donus" => q.Where(r => r.BitTar <= t),
                "Kayit" => q.Where(r => r.CreatedAtUtc <= t),
                _ => q.Where(r => r.BasTar <= t)
            };

        if (!string.IsNullOrWhiteSpace(filter.Ofis))
        {
            var o = filter.Ofis.Trim();
            q = q.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
        }

        var rows = await q
            .Select(r => new { r.Kaynak, r.Gun, r.Tutar, r.Durum, r.VehicleId })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(filter.Grup))
        {
            // Araç grubu rezervasyonda tutulmuyor → araçtan çözülür.
            var g = filter.Grup.Trim();
            var groupIds = (await db.Vehicles.AsNoTracking()
                    .Where(v => v.Grup != null && v.Grup.Trim() == g)
                    .Select(v => v.Id).ToListAsync(ct)).ToHashSet();
            rows = rows.Where(r => groupIds.Contains(r.VehicleId)).ToList();
        }

        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Kaynak) ? "(belirtilmemiş)" : r.Kaynak!)
            .Select(g =>
            {
                // FAZ-76 DÜZELTME: İPTAL rezervasyonlar adet/gün/CİROYA dahil ediliyordu — bu bir
                // veri-doğruluğu hatasıydı (gerçekleşmemiş iş ciro sayılıyordu). Artık varsayılan
                // olarak DIŞARIDA; adedi ayrı kolonda görünür kalıyor ve istenirse dahil edilebiliyor.
                var counted = filter.IptalleriDahilEt
                    ? g.ToList()
                    : g.Where(r => r.Durum != ReservationStatus.Iptal).ToList();
                return new RezervasyonKaynakRow(
                    g.Key, counted.Count, counted.Sum(r => r.Gun), counted.Sum(r => r.Tutar),
                    g.Count(r => r.Durum == ReservationStatus.Iptal));
            })
            .Where(r => r.Adet > 0 || r.IptalAdet > 0)
            .OrderByDescending(r => r.ToplamCiro)
            .ToList();
    }

    public async Task<IReadOnlyList<FaturaDonemRow>> GetInvoicePeriodRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Invoices.AsNoTracking();
        if (from is { } f) q = q.Where(i => i.Tarih >= f);
        if (to is { } t) q = q.Where(i => i.Tarih <= t);

        var rows = await q
            .Select(i => new { i.Id, i.No, i.Tarih, i.VadeTarihi, i.CariId, i.GenelToplam, i.Currency, i.Kur, i.Durum, i.IadeMi })
            .ToListAsync(ct);

        var cust = (await db.Customers.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id, c => c.DisplayName);

        return rows
            .OrderByDescending(i => i.Tarih)
            .Select(i => new FaturaDonemRow(
                i.Id, i.No, i.Tarih, i.VadeTarihi,
                cust.TryGetValue(i.CariId, out var n) ? n : "(bilinmeyen cari)",
                i.GenelToplam, i.Currency, i.Kur, i.Durum.ToString(), i.IadeMi))
            .ToList();
    }

    /// <summary>
    /// FAZ-53 — kira FATURALAMA DURUMU: dönemle KESİŞEN kiralar + her birinin faturalanıp
    /// faturalanmadığı. Fatura dönem raporunun tersi görünümü ("hangi kira eksik kaldı").
    ///
    /// <para><b>Dönem kuralı = KESİŞİM</b> (<c>BasTar &lt;= to &amp;&amp; BitTar &gt;= from</c>),
    /// başlangıç-tarihi eşitliği DEĞİL: aya sarkan bir kira ("15 Ocak – 15 Şubat") Şubat'ta da
    /// faturalanmamış olarak görünmelidir. Başlangıca bakan bir filtre onu Şubat listesinden
    /// düşürür ve fatura kaçağı sessizce gizlenirdi.</para>
    ///
    /// <para><b>Faturalanan kuralı <c>OrtakSorgular.FarkStateAsync</c> ile birebir aynı</b>
    /// (base <c>RentalId</c> + fark <c>KaynakKiraId</c>, İptal ve iade hariç) — ama N kira için
    /// N sorgu atmamak adına TOPLU (tek IN sorgusu) yazılmıştır; kuralın kendisi kopyalanmadı,
    /// kalıcı parite testiyle kilitli.</para>
    /// </summary>
    public async Task<IReadOnlyList<KiraFaturaDurumRow>> GetRentalInvoiceStatusRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, KiraFaturaDurumFilter? filter,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var f = filter ?? new KiraFaturaDurumFilter();

        var q = db.Rentals.AsNoTracking();
        if (from is { } start) q = q.Where(r => r.BitTar >= start);
        if (to is { } bit) q = q.Where(r => r.BasTar <= bit);
        if (f.SubeId is { } branchId && branchId != Guid.Empty)
        {
            // C4/C5 ŞABLON (BranchScope.InScope ile birebir): FK doluysa FK TEK BAŞINA karar verir;
            // FK'sı olmayan eski satırlarda ofis-metni = şube-adı yolu kalıcıdır.
            var branchName = (await db.Branches.AsNoTracking()
                .Where(b => b.Id == branchId).Select(b => b.Ad).FirstOrDefaultAsync(ct))?.Trim();
            q = q.Where(r => r.CikisSubeId == branchId
                || (r.CikisSubeId == null && branchName != null
                    && r.CikisOfisi != null && r.CikisOfisi.Trim() == branchName));
        }

        var rentals = await q
            .Select(r => new { r.Id, r.SozlesmeNo, r.MusteriId, r.VehicleId, r.BasTar, r.BitTar, r.Durum, r.CikisOfisi })
            .ToListAsync(ct);
        if (rentals.Count == 0) return [];

        var ids = rentals.Select(r => r.Id).ToList();

        // Kesilen (İptal/iade olmayan) faturalar — kira başına adet + brüt (base para).
        var issued = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && !i.IadeMi
                && ((i.RentalId != null && ids.Contains(i.RentalId.Value))
                    || (i.KaynakKiraId != null && ids.Contains(i.KaynakKiraId.Value))))
            .Select(i => new { i.Id, i.RentalId, i.KaynakKiraId, i.GenelToplam, i.Kur })
            .ToListAsync(ct);

        // İadeler kesilen faturaya KaynakFaturaId ile bağlı — tutar iade-netlenir (adet netlenmez:
        // "kaç fatura kesildi" ile "ne kadarı ayakta" ayrı sorulardır).
        var invoiceIds = issued.Select(x => x.Id).ToList();
        var refundByInvoice = new Dictionary<Guid, decimal>();
        if (invoiceIds.Count > 0)
        {
            var refunds = await db.Invoices.AsNoTracking()
                .Where(i => i.IadeMi && i.Durum != InvoiceStatus.Iptal
                    && i.KaynakFaturaId != null && invoiceIds.Contains(i.KaynakFaturaId.Value))
                .Select(i => new { i.KaynakFaturaId, i.GenelToplam, i.Kur })
                .ToListAsync(ct);
            refundByInvoice = refunds
                .GroupBy(i => i.KaynakFaturaId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.GenelToplam * x.Kur));
        }

        var count = new Dictionary<Guid, int>();
        var amount = new Dictionary<Guid, decimal>();
        foreach (var i in issued)
        {
            var rentalId = i.RentalId ?? i.KaynakKiraId!.Value;
            count[rentalId] = count.GetValueOrDefault(rentalId) + 1;
            amount[rentalId] = amount.GetValueOrDefault(rentalId)
                + (i.GenelToplam * i.Kur) - refundByInvoice.GetValueOrDefault(i.Id);
        }

        var custIds = rentals.Select(r => r.MusteriId).Distinct().ToList();
        var vehIds = rentals.Select(r => r.VehicleId).Distinct().ToList();
        // PII çözülmez: DisplayName girdileri düz-metin kolonlardan gelir (şifreli alanlara dokunulmaz).
        var custName = (await db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);
        var plate = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);

        var rows = rentals.Select(r =>
        {
            var a = count.GetValueOrDefault(r.Id);
            return new KiraFaturaDurumRow(
                r.Id, r.SozlesmeNo,
                plate.GetValueOrDefault(r.VehicleId) ?? "(bilinmeyen araç)",
                custName.GetValueOrDefault(r.MusteriId) ?? "(bilinmeyen cari)",
                r.BasTar, r.BitTar, r.Durum.ToString(),
                a > 0, a, amount.GetValueOrDefault(r.Id), r.CikisOfisi);
        });

        if (f.Faturalanan is { } fd) rows = rows.Where(r => r.Faturalanan == fd);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            // Serbest metin bellek-içi (kira listesindeki desen) — cari adı düz-metin kolonlardan
            // türetildiği için SQL'e itilmez. Plaka DB'de BOŞLUKSUZ saklanır: "34 AA 11" yazan da
            // bulsun diye terim ayrıca harf/rakama indirgenip denenir (yalnız GENİŞLETİR).
            var t = f.Q.Trim();
            var plateTerm = new string(t.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            rows = rows.Where(r =>
                r.Cari.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.SozlesmeNo.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.Plaka.Contains(t, StringComparison.OrdinalIgnoreCase)
                || (plateTerm.Length > 0 && r.Plaka.Contains(plateTerm, StringComparison.OrdinalIgnoreCase)));
        }

        // Faturalanmamışlar önce (raporun amacı onlar), sonra en yeni kira.
        return rows
            .OrderBy(r => r.Faturalanan)
            .ThenByDescending(r => r.BasTar)
            .ThenBy(r => r.SozlesmeNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// FAZ-12 — araç durum-takip araç süzgeci (gün ve araç görünümü ORTAK kullanır; iki görünüm
    /// aynı araç kümesini anlatsın diye tek yerde). Tümü aracın KENDİ alanlarına bakar.
    /// </summary>
    private static IQueryable<Vehicle> FilterVehicleStatusTracking(IQueryable<Vehicle> q, AracDurumTakipFilter? f)
    {
        if (f is null) return q;
        if (!string.IsNullOrWhiteSpace(f.Sube))
        {
            var sb = f.Sube.Trim();
            q = q.Where(v => v.Sube != null && v.Sube.Trim() == sb);
        }
        if (!string.IsNullOrWhiteSpace(f.AracSahibi))
        {
            var s = f.AracSahibi.Trim();
            q = q.Where(v => v.AracSahibi != null && v.AracSahibi.Trim() == s);
        }
        if (!string.IsNullOrWhiteSpace(f.Grup))
        {
            var g = f.Grup.Trim();
            q = q.Where(v => v.Grup != null && v.Grup.Trim() == g);
        }
        if (!string.IsNullOrWhiteSpace(f.Sipp))
        {
            var s = f.Sipp.Trim();
            q = q.Where(v => v.Sipp != null && v.Sipp.Trim() == s);
        }
        if (!string.IsNullOrWhiteSpace(f.Plaka))
        {
            // Plaka DB'de boşluksuz/büyük harf normalize saklanır — kullanıcının yazdığı da öyle aranır.
            var p = f.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
            q = q.Where(v => EF.Functions.ILike(v.Plaka, $"%{p}%"));
        }
        return q;
    }

    public async Task<IReadOnlyList<AracDurumTakipRow>> GetVehicleStatusTrackingRowsAsync(
        DateTimeOffset from, DateTimeOffset to, AracDurumTakipFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // FAZ-76: opsiyonel şube süzgeci (FAZ-12'de araç sahibi/grup/SIPP/plakayla genişledi).
        // Filtre ARACIN alanlarına bakar ve kira/servis/BAF sayımlarının HEPSİ aynı araç kümesinden
        // gelir — karışık atıf satırı tutarsız yapardı (FAZ-77'de kurulan tek-atıf kuralı).
        var vehicleIds = await FilterVehicleStatusTracking(db.Vehicles.AsNoTracking(), filter)
            .Select(v => v.Id).ToListAsync(ct);
        var total = vehicleIds.Count;
        if (total == 0) return [];
        var set = vehicleIds.ToHashSet();

        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal && set.Contains(r.VehicleId))
            .Select(r => new { r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .ToListAsync(ct);

        // FAZ-76 DÜZELTME: İPTAL servis kayıtları "Bakım" günü olarak SAYILIYORDU. Diğer benzer
        // sorgularda bu filtre vardı; burada eksikti → iptal edilen bir servis aracı günlerce
        // bakımdaymış gibi gösteriyor ve "Boş" sayısını düşürüyordu.
        // FAZ-16: REZERVE (planlanmış randevu) de aynı sebeple hariç — araç henüz servise girmedi.
        var services = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.Durum != ServiceStatus.Iptal && s.Durum != ServiceStatus.Rezerve && set.Contains(s.VehicleId))
            .Select(s => new { s.GirisTarihi, Cikis = s.CikisTarihi })
            .ToListAsync(ct);

        // BAF: açık tahsisler — BİLGİ kolonu, Bos hesabına girmez (bir araç hem kirada hem
        // tahsisli olabilir; çıkarsaydık çift düşüm yapardık).
        var bafs = await db.Baflar.AsNoTracking()
            .Where(b => b.Durum == BafStatus.Acik && set.Contains(b.VehicleId))
            .Select(b => b.CreatedAtUtc)
            .ToListAsync(ct);

        var result = new List<AracDurumTakipRow>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var filled = rentals.Count(k => k.BasTar.Date <= d && k.Bit.Date >= d);
            var maintenance = services.Count(s => s.GirisTarihi.Date <= d && (s.Cikis ?? to).Date >= d);
            var empty = Math.Max(0, total - filled - maintenance);
            var baf = bafs.Count(b => b.Date <= d);
            result.Add(new AracDurumTakipRow(new DateTimeOffset(d, TimeSpan.Zero), total, filled, maintenance, empty, baf));
        }
        return result;
    }

    public async Task<IReadOnlyList<AracDurumTakipAracRow>> GetVehicleStatusTrackingByVehicleRowsAsync(
        DateTimeOffset from, DateTimeOffset to, AracDurumTakipFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var vehicles = await FilterVehicleStatusTracking(db.Vehicles.AsNoTracking(), filter)
            .Select(v => new { v.Id, v.Plaka, v.Sipp, v.Grup, v.Sube, v.AracSahibi })
            .ToListAsync(ct);
        if (vehicles.Count == 0) return [];
        var set = vehicles.Select(a => a.Id).ToHashSet();

        var start = from.Date;
        var bit = to.Date;
        if (bit < start) return [];
        var totalDays = (int)(bit - start).TotalDays + 1;

        // Kira: İptal hariç; bitiş GERÇEK dönüş varsa odur (gün kırılımıyla BİREBİR aynı kural).
        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal && set.Contains(r.VehicleId))
            .Select(r => new { r.VehicleId, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .ToListAsync(ct);

        // Servis: İptal (FAZ-76) ve Rezerve (FAZ-16 — henüz gerçekleşmemiş randevu) hariç;
        // çıkışsız servis hâlâ devam ediyor → aralık sonuna dek.
        var services = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.Durum != ServiceStatus.Iptal && s.Durum != ServiceStatus.Rezerve && set.Contains(s.VehicleId))
            .Select(s => new { s.VehicleId, s.GirisTarihi, Cikis = s.CikisTarihi })
            .ToListAsync(ct);

        // BAF: gerçek ZİMMET ARALIĞI (çıkış → dönüş); dönmemişse aralık sonuna dek. İptal hariç.
        // NOT: gün kırılımındaki "Açık BAF" kolonu BAŞKA bir ölçüdür (o gün açık olan BAF KAYIT
        // sayısı); burada aracın kaç GÜN zimmette olduğu sayılıyor. İkisi bilerek ayrı.
        var bafs = await db.Baflar.AsNoTracking()
            .Where(b => b.Durum != BafStatus.Iptal && set.Contains(b.VehicleId))
            .Select(b => new { b.VehicleId, b.CikisTarihi, Donus = b.DonusTarihi })
            .ToListAsync(ct);

        var rentalMap = rentals.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Bas: x.BasTar.Date, Bit: x.Bit.Date)).ToList());
        var serviceMap = services.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Bas: x.GirisTarihi.Date, Bit: (x.Cikis ?? to).Date)).ToList());
        var bafMap = bafs.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Bas: x.CikisTarihi.Date, Bit: (x.Donus ?? to).Date)).ToList());

        static bool Covers(List<(DateTime Bas, DateTime Bit)>? ranges, DateTime g)
            => ranges is not null && ranges.Exists(a => a.Bas <= g && a.Bit >= g);

        var result = new List<AracDurumTakipAracRow>(vehicles.Count);
        foreach (var a in vehicles)
        {
            rentalMap.TryGetValue(a.Id, out var rent);
            serviceMap.TryGetValue(a.Id, out var srv);
            bafMap.TryGetValue(a.Id, out var baf);

            int filled = 0, maintenance = 0, bafDays = 0;
            for (var d = start; d <= bit; d = d.AddDays(1))
            {
                // ÖNCELİK: Dolu > Bakım > Baf > Boş. Çakışan durumlarda gün TEK kovaya düşer;
                // böylece dört kovanın toplamı aralık gün sayısına EŞİT kalır (değişmez).
                if (Covers(rent, d)) filled++;
                else if (Covers(srv, d)) maintenance++;
                else if (Covers(baf, d)) bafDays++;
            }
            result.Add(new AracDurumTakipAracRow(
                a.Id, a.Plaka, a.Sipp, a.Grup, a.Sube, a.AracSahibi,
                totalDays, filled, maintenance, bafDays, totalDays - filled - maintenance - bafDays));
        }

        // En çok boşta kalan üstte — canlının bu ekrandaki asıl sorusu ("hangi araç yatıyor").
        return result
            .OrderByDescending(r => r.BosGun).ThenBy(r => r.Plaka, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AracGunlukDurumRow>> GetVehicleDailyStatusRowsAsync(
        DateTimeOffset day, AracGunlukDurumFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var g = day.Date;

        var q =
            from r in db.Rentals.AsNoTracking().Where(x => x.Durum != RentalStatus.Iptal)
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id
            join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id into cg
            from c in cg.DefaultIfEmpty()
            select new { r, v, c };

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.v.Plaka, $"%{p}%"));
            }
            if (!string.IsNullOrWhiteSpace(filter.Grup))
            {
                var gr = filter.Grup.Trim();
                q = q.Where(x => x.v.Grup != null && x.v.Grup.Trim() == gr);
            }
            if (!string.IsNullOrWhiteSpace(filter.Sipp))
            {
                var sp = filter.Sipp.Trim();
                q = q.Where(x => x.v.Sipp != null && x.v.Sipp.Trim() == sp);
            }
            if (!string.IsNullOrWhiteSpace(filter.AracSahibi))
            {
                var s = filter.AracSahibi.Trim();
                q = q.Where(x => x.v.AracSahibi != null && x.v.AracSahibi.Trim() == s);
            }
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(x => x.r.CikisOfisi != null && x.r.CikisOfisi.Trim() == o);
            }
        }

        var raw = await q.Select(x => new
        {
            x.r.Id, x.r.SozlesmeNo, x.r.VehicleId, x.r.BasTar, x.r.BitTar, x.r.GercekDonusTar,
            x.r.Gun, x.r.Tutar, x.r.FazlaKmBedeli, x.r.YakitBedeli, x.r.UzatmaBedeli, x.r.KurSnapshot,
            x.r.CikisOfisi,
            x.v.Plaka, x.v.Sipp, x.v.Grup, x.v.AracSahibi,
            Musteri = x.c == null ? null : (x.c.Tip == CustomerType.Bireysel
                ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan)
        }).ToListAsync(ct);

        // Aktiflik: gün kırılımının "Dolu" kovasıyla BİREBİR aynı (kapsayıcı, dönüş günü dahil).
        var active = raw.Where(x => x.BasTar.Date <= g && (x.GercekDonusTar ?? x.BitTar).Date >= g).ToList();
        if (active.Count == 0) return [];

        // Ek hizmet brütü kira başına — RentalAddOn tutarları BAZ PARADA saklanır (kur yok).
        var rentalIds = active.Select(x => x.Id).ToList();
        var addOn = (await db.RentalAddOns.AsNoTracking()
                .Where(a => rentalIds.Contains(a.RentalId))
                .GroupBy(a => a.RentalId)
                .Select(gr => new { RentalId = gr.Key, Brut = gr.Sum(a => a.Toplam) })
                .ToListAsync(ct))
            .ToDictionary(x => x.RentalId, x => x.Brut);

        return active
            .Select(x =>
            {
                // Faturalanan gün sayısı bölendir. Gun alanı 0/eksikse takvim farkına düşülür
                // (en az 1 — sıfıra bölme yok).
                var divisor = x.Gun > 0 ? x.Gun
                    : Math.Max(1, (int)((x.GercekDonusTar ?? x.BitTar).Date - x.BasTar.Date).TotalDays);

                // Baz kira brütü kira DÖVİZİNDE tutulur → TL'ye KurSnapshot ile çevrilir.
                // Ek hizmet zaten TL; ikisi ayrı bölünür (bkz. AracGunlukDurumRow XML notu).
                var rentalTry = (x.Tutar + x.FazlaKmBedeli + x.YakitBedeli + x.UzatmaBedeli) * x.KurSnapshot;
                var serviceTry = addOn.TryGetValue(x.Id, out var h) ? h : 0m;

                var dailyRental = decimal.Round(rentalTry / divisor, 2, MidpointRounding.AwayFromZero);
                var dailyService = decimal.Round(serviceTry / divisor, 2, MidpointRounding.AwayFromZero);

                return new AracGunlukDurumRow(
                    x.VehicleId, x.Plaka, x.Sipp, x.Grup, x.AracSahibi,
                    x.Id, x.SozlesmeNo, string.IsNullOrWhiteSpace(x.Musteri) ? "(bilinmeyen cari)" : x.Musteri!.Trim(),
                    x.CikisOfisi, x.BasTar, x.GercekDonusTar ?? x.BitTar, divisor,
                    dailyRental, dailyService, dailyRental + dailyService);
            })
            .OrderByDescending(r => r.GunlukToplam).ThenBy(r => r.Plaka, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<MusteriSegmentRow>> GetCustomerSegmentRowsAsync(
        MusteriSegmentFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // FAZ-41 — süzgeç AGREGADAN ÖNCE kiralara uygulanır (pencere içi "ne yaptı" görünümü).
        var rentalQ = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal);
        if (filter is not null)
        {
            if (filter.Bas is { } start) rentalQ = rentalQ.Where(r => r.BasTar >= start);
            if (filter.Bit is { } bit) rentalQ = rentalQ.Where(r => r.BasTar <= bit);
            if (!string.IsNullOrWhiteSpace(filter.RezKaynak))
            {
                var k = filter.RezKaynak.Trim();
                rentalQ = rentalQ.Where(r => r.Kaynak != null && EF.Functions.ILike(r.Kaynak, k));
            }
            if (!string.IsNullOrWhiteSpace(filter.CikisOfis))
            {
                // METİN eşleşmesi (FK değil) — gerekçe MusteriSegmentFilter.CikisOfis XML notunda.
                var o = filter.CikisOfis.Trim();
                rentalQ = rentalQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
            }
        }

        var group = await rentalQ
            .GroupBy(r => r.MusteriId)
            .Select(g => new
            {
                MusteriId = g.Key,
                KiraSayisi = g.Count(),
                ToplamCiro = g.Sum(r => r.GenelToplam * r.KurSnapshot), // TL-baz (O5); VIP eşiği artık anlamlı
                SonIslem = g.Max(r => r.BasTar),
                IlkKira = g.Min(r => r.BasTar),
                // SQL SUM NULL'ları atlar: iki km'si de dolu olmayan kira paya girmez; payda ayrı sayılır.
                KmToplam = g.Sum(r => r.DonusKm - r.CikisKm),
                KmAdet = g.Count(r => r.CikisKm != null && r.DonusKm != null)
            })
            .ToListAsync(ct);

        // Ek hizmet (RentalAddOn) brütü — AYNI süzgeçten geçmiş kiralara bağlı kalemler, TL-baz
        // (kiranın KurSnapshot'ı ile; kalem kiranın dövizinde saklanır).
        var service = await (from a in db.RentalAddOns.AsNoTracking()
                            join r in rentalQ on a.RentalId equals r.Id
                            group a.Toplam * r.KurSnapshot by r.MusteriId into g
                            select new { MusteriId = g.Key, Toplam = g.Sum() })
            .ToListAsync(ct);
        var serviceMap = service.ToDictionary(x => x.MusteriId, x => x.Toplam);

        var cust = await db.Customers.AsNoTracking()
            .Select(c => new { c.Id, c.Tip, c.Ad, c.Soyad, c.Unvan, c.Email, c.CepTel, c.DogumTarihi, c.AnonimBelge })
            .ToListAsync(ct);
        var custMap = cust.ToDictionary(c => c.Id);

        var rows = group
            .Select(g =>
            {
                custMap.TryGetValue(g.MusteriId, out var c);
                // Customer.DisplayName ile BİREBİR aynı kural (entity projeksiyonu yerine alan
                // projeksiyonu kullandığımız için burada tekrar yazılı; davranış değişmedi).
                var name = c is null
                    ? "(bilinmeyen cari)"
                    : (c.Tip == CustomerType.Bireysel ? $"{c.Ad} {c.Soyad}".Trim() : (c.Unvan ?? string.Empty));

                return new MusteriSegmentRow(
                    g.MusteriId, name, g.KiraSayisi, g.ToplamCiro, g.SonIslem,
                    g.ToplamCiro >= 10000m ? "VIP" : g.ToplamCiro > 0m ? "Standart" : "Pasif",
                    Mail: c?.Email, Tel: c?.CepTel,
                    OrtalamaKiraBedeli: g.KiraSayisi > 0 ? decimal.Round(g.ToplamCiro / g.KiraSayisi, 2, MidpointRounding.AwayFromZero) : 0m,
                    OrtalamaKm: g.KmAdet > 0 ? decimal.Round((decimal)(g.KmToplam ?? 0) / g.KmAdet, 2, MidpointRounding.AwayFromZero) : null,
                    // r317 L2: doğum tarihi kimlik belgesi bilgisi — AnonimBelge'de hiçbir yüzeye (Blazor CRM analizi,
                    // /api/ui analiz, dışa aktarma) çıkmaz; maske kaynakta.
                    DogumTarihi: c is { AnonimBelge: false } ? c.DogumTarihi : null,
                    IlkKiraZamani: g.IlkKira,
                    HizmetBedeli: serviceMap.TryGetValue(g.MusteriId, out var h) ? h : 0m);
            });

        if (filter?.MinKiraSayisi is { } min) rows = rows.Where(r => r.KiraSayisi >= min);

        return rows.OrderByDescending(r => r.ToplamCiro).ToList();
    }

    public async Task<MusteriSegmentSecenekleri> GetCustomerSegmentOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Segment satırlarıyla AYNI kira kümesi (İptal hariç) ama SÜZGEÇSİZ — seçenek listesi
        // kendi seçimine göre daralırsa kullanıcı seçtiği filtreden geri dönemez.
        var q = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal);

        var sources = await q.Where(r => r.Kaynak != null && r.Kaynak != "")
            .Select(r => r.Kaynak!).Distinct().ToListAsync(ct);
        var offices = await q.Where(r => r.CikisOfisi != null && r.CikisOfisi != "")
            .Select(r => r.CikisOfisi!).Distinct().ToListAsync(ct);

        return new MusteriSegmentSecenekleri(
            sources.Select(x => x.Trim()).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture).ToList(),
            offices.Select(x => x.Trim()).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture).ToList());
    }

    public async Task<IReadOnlyList<PersonelCalismaRow>> GetPersonnelWorkRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var group = await db.Baflar.AsNoTracking()
            .GroupBy(b => b.PersonelId)
            .Select(g => new { PersonelId = g.Key, TahsisSayisi = g.Count() })
            .ToListAsync(ct);

        var pers = (await db.Personeller.AsNoTracking().ToListAsync(ct))
            .ToDictionary(p => p.Id, p => $"{p.Ad} {p.Soyad}".Trim());

        return group
            .Select(g => new PersonelCalismaRow(
                g.PersonelId, pers.TryGetValue(g.PersonelId, out var n) ? n : "(bilinmeyen personel)", g.TahsisSayisi))
            .OrderByDescending(r => r.TahsisSayisi)
            .ToList();
    }

    public async Task<GunlukFaaliyetDto> GetDailyActivityAsync(
        DateTimeOffset from, DateTimeOffset to, string? branch = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // FAZ-76 — şube süzgeci YALNIZ operasyon sayaçlarına (rezervasyon/kira/çıkış/dönüş)
        // uygulanır: bunlar CikisOfisi taşır. Tahsilat ve fatura şube boyutu TAŞIMAZ; onları
        // filtrelenmiş gibi göstermek yanlış olurdu, filtrelemeden bırakıp EKRANDA "şube kırılımı
        // yok" diye etiketliyoruz (karışık atıf yerine açık sınır).
        var resQ = db.Reservations.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc <= to);
        var rentalNewQ = db.Rentals.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc <= to);
        var pickupQ = db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal && r.BasTar >= from && r.BasTar <= to);
        var returnQ = db.Rentals.AsNoTracking()
            .Where(r => r.GercekDonusTar != null && r.GercekDonusTar >= from && r.GercekDonusTar <= to);

        if (!string.IsNullOrWhiteSpace(branch))
        {
            var sb = branch.Trim();
            resQ = resQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
            rentalNewQ = rentalNewQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
            pickupQ = pickupQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
            returnQ = returnQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
        }

        var newRes = await resQ.CountAsync(ct);
        var newRental = await rentalNewQ.CountAsync(ct);
        // Çıkış: o gün başlayan (İptal olmayan) kiralar. Dönüş: o gün gerçek dönüşü yapılan kiralar.
        var pickup = await pickupQ.CountAsync(ct);
        var returnInfo = await returnQ.CountAsync(ct);

        // Tahsilat: TEK doğruluk kaynağı (denetim O12b — WhatsApp özeti aynı tanımı kullanır; TL-baz Σ Amount×Rate,
        // ters kayıt hariç). Pencere [from, to] kapalı → helper'a to+1tick (davranış birebir korunur).
        var (collectionCount, collectionAmount) = await SharedQueries.CollectionTryAsync(db, from, to.AddTicks(1), ct);

        // Fatura: İptal hariç; GenelToplam base zaten (Currency/Kur ayrı tutulur ama GenelToplam fatura
        // para birimindedir → günlük faaliyet sayacında brüt toplam olarak gösterilir).
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && i.Tarih >= from && i.Tarih <= to)
            .Select(i => new { i.GenelToplam, i.Kur, i.IadeMi })
            .ToListAsync(ct);
        var invoiceAmount = invoices.Sum(f => f.GenelToplam * f.Kur * (f.IadeMi ? -1m : 1m)); // iade net'i düşürür

        return new GunlukFaaliyetDto(
            newRes, newRental, pickup, returnInfo,
            collectionCount, collectionAmount, invoices.Count, invoiceAmount);
    }

    public async Task<IReadOnlyList<KdvLineRowDto>> GetVatLineRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var inv = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } f) inv = inv.Where(i => i.Tarih >= f);
        if (to is { } t) inv = inv.Where(i => i.Tarih <= t);

        // Satır tutarları fatura para birimindedir → base para için Kur ile çarp (bellek-içi).
        // İade faturası satırları NEGATİF sayılır (IadeMi) → KDV oran-bazında netleşir.
        var raw = await (from l in db.InvoiceLines.AsNoTracking()
                         join i in inv on l.InvoiceId equals i.Id
                         select new { l.KdvOrani, l.SatirNet, l.SatirKdv, l.SatirToplam, i.Kur, i.IadeMi, InvoiceId = i.Id })
            .ToListAsync(ct);

        return raw
            .Select(r =>
            {
                var s = (r.IadeMi ? -1m : 1m) * r.Kur;
                return new KdvLineRowDto(r.KdvOrani, r.SatirNet * s, r.SatirKdv * s, r.SatirToplam * s, r.InvoiceId);
            })
            .ToList();
    }

    /// <summary>
    /// FAZ-53 — KDV GENİŞ format satırları: SATIR = belge, SÜTUN = KDV oranı. Satış tarafı kesilen
    /// <c>Invoice</c>/<c>InvoiceLine</c>'dan, alış tarafı (<paramref name="includePurchases"/>) gelen
    /// e-Faturanın FAZ-55 oran-kırılımı kolonlarından gelir.
    ///
    /// <para><b>Alış tarafı kuralları:</b>
    /// <list type="bullet">
    /// <item><b>Reddedilen fatura HARİÇ</b> — reddedilmiş belgenin KDV'si indirilemez.</item>
    /// <item><b>Kırılımı GİRİLMEMİŞ fatura HARİÇ</b> — belge toplamını "%20 kademesi" varsayarak
    /// dağıtmak uydurma beyan üretir; kırılım FAZ-55'te girilmeden satır rapora giremez.</item>
    /// <item><b>TRY olmayan fatura HARİÇ</b> ve sayılır: <c>GelenEFatura</c>'da KUR kolonu YOKTUR
    /// (belge kendi para biriminde saklanır), uydurma kurla base'e çevrilemez. Atlanan sayısı
    /// çağırana <c>AtlananDovizliAlis</c> olarak bildirilir — sessiz eksik toplam yasak.</item>
    /// </list></para>
    ///
    /// <para><b>Satış tarafı</b> mevcut <see cref="GetVatLineRowsAsync"/> ile AYNI işaret/kur
    /// sözleşmesini kullanır (İptal hariç; iade satırı NEGATİF; tutarlar × <c>Kur</c> ile base
    /// paraya çevrilir) — pivot ve geniş görünüm ayrışmasın diye. İki görünümün satış toplamlarının
    /// eşitliği kalıcı testle kilitlidir.</para>
    /// </summary>
    public async Task<(IReadOnlyList<KdvGenisSatirDto> Satirlar, int AtlananDovizliAlis)> GetVatExtendedRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, bool includePurchases, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var inv = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } f) inv = inv.Where(i => i.Tarih >= f);
        if (to is { } t) inv = inv.Where(i => i.Tarih <= t);

        var saleHeader = await inv
            .Select(i => new { i.Id, i.No, i.Tarih, i.CariId, i.Kur, i.IadeMi, i.Durum })
            .ToListAsync(ct);

        var rows = new List<KdvGenisSatirDto>();

        if (saleHeader.Count > 0)
        {
            var invIds = saleHeader.Select(x => x.Id).ToList();
            var lines = await db.InvoiceLines.AsNoTracking()
                .Where(l => invIds.Contains(l.InvoiceId))
                .Select(l => new { l.InvoiceId, l.KdvOrani, l.SatirNet, l.SatirKdv })
                .ToListAsync(ct);
            var byInvoice = lines.GroupBy(l => l.InvoiceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var custIds = saleHeader.Select(x => x.CariId).Distinct().ToList();
            var custName = (await db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToListAsync(ct))
                .ToDictionary(c => c.Id, c => c.DisplayName);

            foreach (var i in saleHeader)
            {
                // GetKdvLineRowsAsync ile birebir işaret/kur sözleşmesi.
                var s = (i.IadeMi ? -1m : 1m) * i.Kur;
                decimal n20 = 0, k20 = 0, n10 = 0, k10 = 0, n1 = 0, k1 = 0, n0 = 0, nd = 0, kd = 0;
                foreach (var l in byInvoice.GetValueOrDefault(i.Id) ?? [])
                {
                    var net = l.SatirNet * s; var vat = l.SatirKdv * s;
                    switch (l.KdvOrani)
                    {
                        case 0.20m: n20 += net; k20 += vat; break;
                        case 0.10m: n10 += net; k10 += vat; break;
                        case 0.01m: n1 += net; k1 += vat; break;
                        case 0m: n0 += net; kd += vat; break;   // %0'da KDV çıkmamalı; çıkarsa Diğer'e düşer
                        default: nd += net; kd += vat; break;   // %18/%8 gibi kademe-dışı oranlar kaybolmaz
                    }
                }
                rows.Add(new KdvGenisSatirDto(
                    i.Id, KdvGenisDto.TypeSale,
                    i.IadeMi ? $"{i.No} (iade)" : i.No, i.Tarih,
                    custName.GetValueOrDefault(i.CariId) ?? "(bilinmeyen cari)", i.Durum.ToString(),
                    n20, k20, n10, k10, n1, k1, n0, nd, kd,
                    n20 + n10 + n1 + n0 + nd, k20 + k10 + k1 + kd));
            }
        }

        var skipped = 0;
        if (includePurchases)
        {
            var gq = db.GelenEFaturalar.AsNoTracking()
                .Where(g => g.Durum != IncomingEInvoiceStatus.Reddedildi);
            if (from is { } f2) gq = gq.Where(g => g.Tarih >= f2);
            if (to is { } t2) gq = gq.Where(g => g.Tarih <= t2);

            var received = await gq.ToListAsync(ct);
            foreach (var g in received)
            {
                // Kırılım girilmemişse belge dağıtılamaz (uydurma kademe yasak) — rapora girmez.
                if (!IncomingEInvoiceVatBreakdown.HasBreakdown(g)) continue;
                if (!string.Equals(g.Currency?.Trim(), "TRY", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;   // kur kolonu yok → base'e çevrilemez
                    continue;
                }

                var n0g = g.Kdv0Matrah ?? 0m;
                rows.Add(new KdvGenisSatirDto(
                    g.Id, KdvGenisDto.TypePurchase, g.Ettn, g.Tarih,
                    string.IsNullOrWhiteSpace(g.GonderenUnvan) ? g.GonderenVkn : g.GonderenUnvan,
                    g.Durum.ToString(),
                    g.Kdv20Matrah ?? 0m, g.Kdv20 ?? 0m,
                    g.Kdv10Matrah ?? 0m, g.Kdv10 ?? 0m,
                    g.Kdv1Matrah ?? 0m, g.Kdv1 ?? 0m,
                    n0g, 0m, 0m,
                    (g.Kdv20Matrah ?? 0m) + (g.Kdv10Matrah ?? 0m) + (g.Kdv1Matrah ?? 0m) + n0g,
                    (g.Kdv20 ?? 0m) + (g.Kdv10 ?? 0m) + (g.Kdv1 ?? 0m)));
            }
        }

        return (rows
            .OrderBy(r => r.Tur, StringComparer.Ordinal)
            .ThenBy(r => r.Tarih)
            .ThenBy(r => r.No, StringComparer.Ordinal)
            .ToList(), skipped);
    }

    public async Task<IReadOnlyList<EkHizmetSalesRowDto>> GetAddOnSalesRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // İptal kiraların ek hizmetleri satış sayılmaz. Tarih: kalemin eklenme zamanı (CreatedAtUtc).
        var activeRentalIds = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal).Select(r => r.Id);
        var q = db.RentalAddOns.AsNoTracking().Where(a => activeRentalIds.Contains(a.RentalId));
        if (from is { } f) q = q.Where(a => a.CreatedAtUtc >= f);
        if (to is { } t) q = q.Where(a => a.CreatedAtUtc <= t);

        // RentalAddOn tutarları baz para (TRY) olarak saklanır (Kur yok) → doğrudan kullanılır.
        return await q
            .Select(a => new EkHizmetSalesRowDto(a.Ad, a.Miktar, a.NetTutar, a.KdvTutar, a.Toplam, a.RentalId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EkHizmetAracSalesRow>> GetAddOnVehicleSalesRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Pencere tanımı GetEkHizmetSalesRowsAsync ile BİREBİR aynı olmalı (İptal kira hariç,
        // tarih = kalem eklenme zamanı) — ayrışırsa pivot toplamı ad-bazlı özetten kayar.
        var q =
            from a in db.RentalAddOns.AsNoTracking()
            join r in db.Rentals.AsNoTracking().Where(x => x.Durum != RentalStatus.Iptal)
                on a.RentalId equals r.Id
            // Araç silinmişse satır KAYBOLMAMALI (tutar özette sayılıyor) → LEFT JOIN + "(bilinmeyen araç)".
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id into vg
            from v in vg.DefaultIfEmpty()
            select new { a, r, v };

        if (from is { } f) q = q.Where(x => x.a.CreatedAtUtc >= f);
        if (to is { } t) q = q.Where(x => x.a.CreatedAtUtc <= t);

        return await q
            .Select(x => new EkHizmetAracSalesRow(
                x.v == null ? null : (Guid?)x.v.Id,
                x.v == null ? "(bilinmeyen araç)" : x.v.Plaka,
                x.v == null ? null : x.v.Grup,
                x.v == null ? null : x.v.Sipp,
                x.a.Ad, x.a.NetTutar, x.a.KdvTutar, x.a.Toplam, x.a.RentalId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KarlilikSatirDto>> GetProfitabilityRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Gider — HER İKİ yön (FAZ 4.3: dış hizmet iptali TERS KAYITLA Alacak Gider yazar → SignedBase
        // ile netleşir; gelir tarafıyla simetrik). AccountRef = araç (null = genel gider). Base = Amount×Rate.
        var gq = db.AccountLedgerEntries.AsNoTracking()
            // PR-A: dönem kapanış fişi Gider'i sıfırlayan iç virman (AccountRef=null → "(Atanmamış)"a düşerdi) → HARİÇ.
            .Where(e => e.AccountType == LedgerAccountType.Gider && e.SourceType != "DonemKapanis");
        if (from is { } gf) gq = gq.Where(e => e.EntryDateUtc >= gf);
        if (to is { } gt) gq = gq.Where(e => e.EntryDateUtc <= gt);
        var expenseRaw = await gq.Select(e => new { e.AccountRef, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);
        var expenseByVehicle = expenseRaw.GroupBy(x => x.AccountRef ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.Sum(x => (x.Direction == LedgerDirection.Debit ? 1m : -1m) * x.A * x.R));

        // Gelir — HER İKİ yön (iade faturası Borç Gelir yazar → SignedBase ile netleşir).
        // SourceId(Fatura/FaturaIade) → Kira → Araç ile atfedilir; atfedilemeyen → Guid.Empty.
        var lq = db.AccountLedgerEntries.AsNoTracking()
            // PR-A: dönem kapanış fişi Gelir'i sıfırlayan iç virman (kaynak atfı yok → "(Atanmamış)") → HARİÇ.
            .Where(e => e.AccountType == LedgerAccountType.Gelir && e.SourceType != "DonemKapanis");
        if (from is { } ef) lq = lq.Where(e => e.EntryDateUtc >= ef);
        if (to is { } et) lq = lq.Where(e => e.EntryDateUtc <= et);
        var revenueRaw = await lq.Select(e => new { e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);

        // Atfetme: kaynak türü + SourceId → araç. FAZ-79'da AYNI çözücü KDV satırlarında da kullanılır
        // (bkz. GetKarlilikEkRawAsync) — atıf kuralı iki yerde yazılıp sessizce ayrışmasın.
        var attribution = await VehicleAttributionResolverAsync(db, ct);

        var revenueByVehicle = new Dictionary<Guid, decimal>();
        foreach (var e in revenueRaw)
        {
            var veh = attribution(e.SourceType, e.SourceId);
            // İade Borç Gelir → negatif (kârı azaltır); normal Alacak Gelir → pozitif.
            var signed = (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R;
            revenueByVehicle[veh] = revenueByVehicle.GetValueOrDefault(veh) + signed;
        }

        var vehIds = expenseByVehicle.Keys.Concat(revenueByVehicle.Keys).Where(k => k != Guid.Empty).Distinct().ToList();
        var dims = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Plaka, v.Sube, v.Grup, v.Segment }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => (v.Plaka, v.Sube, v.Grup, v.Segment));

        var rows = new List<KarlilikSatirDto>();
        foreach (var key in expenseByVehicle.Keys.Concat(revenueByVehicle.Keys).Distinct())
        {
            var revenue = revenueByVehicle.GetValueOrDefault(key);
            var expense = expenseByVehicle.GetValueOrDefault(key);
            if (key == Guid.Empty)
                rows.Add(new KarlilikSatirDto(null, "(Atanmamış)", null, null, null, revenue, expense, revenue - expense));
            else
            {
                var d = dims.TryGetValue(key, out var x) ? x : ("(bilinmeyen araç)", (string?)null, (string?)null, (string?)null);
                rows.Add(new KarlilikSatirDto(key, d.Item1, d.Item2, d.Item3, d.Item4, revenue, expense, revenue - expense));
            }
        }
        return rows;
    }

    /// <summary>
    /// Defter satırının (SourceType, SourceId) çiftini ARACA çözen fonksiyon — Karlilik gelir atfının
    /// TEK kaynağı (FAZ-79'da KDV referans kolonu da bunu kullanır; kural kopyalanmaz).
    ///
    /// <para>Fatura/FaturaIade→kira→araç (iade RentalId=null taşır → KaynakFaturaId iki-hop; FARK faturası
    /// da RentalId=null taşır → kira bağı KaynakKiraId'dedir), AracSatis→satış→araç, Ceza→ceza→araç
    /// (VehicleId yoksa RentalId fallback), ServisYansitma→servis→araç, DepozitoIrat/DisHizmet→kira→araç.
    /// HGS (plaka-bazlı, kalıcı VehicleId yok) ve manuel/kaynaksız kayıt → <c>Guid.Empty</c> =
    /// "(Atanmamış)".</para>
    /// </summary>
    private static async Task<Func<string, Guid, Guid>> VehicleAttributionResolverAsync(AppDbContext db, CancellationToken ct)
    {
        var invAll = await db.Invoices.AsNoTracking()
            .Select(i => new { i.Id, i.RentalId, i.KaynakFaturaId, i.KaynakKiraId }).ToListAsync(ct);
        var invById = invAll.ToDictionary(x => x.Id);
        Guid? RentalOf(Guid invId)
        {
            if (!invById.TryGetValue(invId, out var i)) return null;
            if (i.RentalId is Guid r) return r;
            if (i.KaynakKiraId is Guid kk) return kk; // fark faturası
            if (i.KaynakFaturaId is Guid k && invById.TryGetValue(k, out var s)) return s.RentalId ?? s.KaynakKiraId;
            return null;
        }
        var rentalToVeh = (await db.Rentals.AsNoTracking().Select(r => new { r.Id, r.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        var saleToVeh = (await db.VehicleSales.AsNoTracking().Select(s => new { s.Id, s.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        // Ceza: VehicleId doğrudan; yoksa RentalId → kira → araç fallback (araçsız+kirasız ceza atfedilemez).
        var penaltyToVehicle = (await db.Penalties.AsNoTracking().Where(p => p.VehicleId != null || p.RentalId != null)
                .Select(p => new { p.Id, p.VehicleId, p.RentalId }).ToListAsync(ct))
            .Select(p => new
            {
                p.Id,
                VehicleId = p.VehicleId
                    ?? (p.RentalId is Guid prid && rentalToVeh.TryGetValue(prid, out var prv) ? prv : (Guid?)null)
            })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);
        var serviceToVeh = (await db.ServiceRecords.AsNoTracking()
                .Select(s => new { s.Id, s.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        // Depozito iradı (FAZ 1.2): kira bağı → araç (kirasız irat Atanmamış'ta kalır).
        var forfeitToVehicle = (await db.DepozitoIratlar.AsNoTracking().Where(d => d.RentalId != null)
                .Select(d => new { d.Id, RentalId = d.RentalId!.Value }).ToListAsync(ct))
            .Select(d => new { d.Id, VehicleId = rentalToVeh.TryGetValue(d.RentalId, out var iv) ? (Guid?)iv : null })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);
        // Dış hizmet komisyon geliri (FAZ 4.3): kayıt → kira → araç.
        var outsourcedServiceToVehicle = (await db.DisHizmetAlimlari.AsNoTracking()
                .Select(d => new { d.Id, d.RentalId }).ToListAsync(ct))
            .Select(d => new { d.Id, VehicleId = rentalToVeh.TryGetValue(d.RentalId, out var dv) ? (Guid?)dv : null })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);

        return (sourceType, sourceId) => sourceType switch
        {
            "Fatura" or "FaturaIade" when RentalOf(sourceId) is Guid rid && rentalToVeh.TryGetValue(rid, out var vid) => vid,
            "AracSatis" when saleToVeh.TryGetValue(sourceId, out var sv) => sv,
            "Ceza" when penaltyToVehicle.TryGetValue(sourceId, out var cv) => cv,
            "ServisYansitma" when serviceToVeh.TryGetValue(sourceId, out var srv) => srv,
            "DepozitoIrat" when forfeitToVehicle.TryGetValue(sourceId, out var irv) => irv,
            "DisHizmet" when outsourcedServiceToVehicle.TryGetValue(sourceId, out var dhv) => dhv, // FAZ 4.3
            _ => Guid.Empty
        };
    }

    /// <summary>
    /// FAZ-79 — Karlılık satırının DEFTER-DIŞI zenginleştirme hamı. Buradan dönen HİÇBİR tutar
    /// Gelir/Gider/NetKar'a eklenmez; servis yalnız ayrı referans kolonlarına yazar.
    /// </summary>
    public async Task<KarlilikEkRawDto> GetProfitabilityExtraRawAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        // Ömür-boyu P&L satırları: KPI (Doluluk/RevPACD/ADR) paydaları sahiplik penceresidir; payı dönem
        // geliriyle karıştırmak KARIŞIK PAYDA olurdu (FiloAnaliz/Karne ile aynı ders). Pencere yoksa
        // ikinci sorgu atılmaz — çağıran zaten aynı listeyi kullanacak.
        var lifetime = from is null && to is null ? [] : await GetProfitabilityRowsAsync(null, null, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var lastSale = (await db.VehicleSales.AsNoTracking()
                .Where(s => s.Durum == SaleStatus.Tamamlandi)
                .GroupBy(s => s.VehicleId)
                .Select(g => new { VehicleId = g.Key, Tarih = g.Max(x => x.Tarih) })
                .ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x.Tarih);

        // Şube adı FK'den çözülür (FAZ 5-C5: FK doluysa FK karar verir); FK'sız eski satırda serbest metin.
        var branchNames = (await db.Branches.AsNoTracking().Select(b => new { b.Id, b.Ad }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Ad);
        // Grup SIPP'i: araç kartında SIPP boşsa araç grubundan miras (canlıda SIPP grup seviyesinde tutulur).
        var groupSipp = (await db.VehicleGroups.AsNoTracking()
                .Where(g => g.Sipp != null)
                .Select(g => new { g.Kod, g.Sipp }).ToListAsync(ct))
            .GroupBy(x => x.Kod.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().Sipp);

        var vehicles = (await db.Vehicles.AsNoTracking()
                .Select(v => new
                {
                    v.Id, v.Sipp, v.Grup, v.Sube, v.SubeId, v.AylikMaliyet, v.FiloYonetimMaliyeti,
                    v.AlimTarihi, v.FiloGirisTarih, v.FiloCikisTarih, v.Durum
                })
                .ToListAsync(ct))
            .Select(v =>
            {
                var groupCode = string.IsNullOrWhiteSpace(v.Grup) ? null : v.Grup.Trim().ToUpperInvariant();
                var sipp = v.Sipp;
                if (string.IsNullOrWhiteSpace(sipp) && groupCode is not null)
                    sipp = groupSipp.GetValueOrDefault(groupCode);
                var parking = v.SubeId is Guid sid && branchNames.TryGetValue(sid, out var name) ? name : v.Sube;
                return new KarlilikAracMetaRow(
                    v.Id, sipp, parking, groupCode, v.Sube,
                    v.AylikMaliyet, v.FiloYonetimMaliyeti,
                    v.AlimTarihi, v.FiloGirisTarih, v.FiloCikisTarih, v.Durum,
                    lastSale.TryGetValue(v.Id, out var t) ? t : null);
            })
            .ToList();

        // Kira metası — TUTAR TAŞIMAZ (bilinçli): doluluk günü + kaynak + müşteri. İptal hariç, efektif
        // bitiş (GercekDonusTar ?? BitTar) — FiloAnaliz ile aynı tanım.
        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new KarlilikKiraMetaRow(
                r.VehicleId, r.BasTar, r.GercekDonusTar ?? r.BitTar, r.Kaynak, r.MusteriId))
            .ToListAsync(ct);

        // KDV (referans): SATIŞ belgelerinin KDV'si, gelir atfının BİREBİR aynı çözücüsüyle araca bağlanır.
        // Gider/alış KDV'si (indirilecek) BİLİNÇLİ olarak dışarıda — "araç geliri KDV dahil" kolonuna
        // alış KDV'si karışsaydı sayı hiçbir şeyi ifade etmezdi.
        var vatQ = db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Kdv && e.SourceType != "DonemKapanis");
        if (from is { } kf) vatQ = vatQ.Where(e => e.EntryDateUtc >= kf);
        if (to is { } kt) vatQ = vatQ.Where(e => e.EntryDateUtc <= kt);
        var vatRaw = await vatQ
            .Select(e => new { e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct);
        var attribution = await VehicleAttributionResolverAsync(db, ct);
        var vatByVeh = new Dictionary<Guid, decimal>();
        foreach (var e in vatRaw.Where(x => SaleDocument.Contains(x.SourceType)))
        {
            var veh = attribution(e.SourceType, e.SourceId);
            var signed = (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R;
            vatByVeh[veh] = vatByVeh.GetValueOrDefault(veh) + signed;
        }
        var vatLines = vatByVeh
            .Select(x => new KarlilikKdvRow(x.Key == Guid.Empty ? null : x.Key, x.Value)).ToList();

        // Tarife matrisi HAM çekilir; onay/aktiflik/kapsam elemesi paylaşılan çözümleyicidedir.
        var tariffs = await db.RateMatrices.AsNoTracking().ToListAsync(ct);

        return new KarlilikEkRawDto(lifetime, vehicles, rentals, vatLines, tariffs);
    }

    /// <summary>KDV referans kolonuna giren SATIŞ belgesi kaynak türleri (gelir atfının kapsadığı küme).</summary>
    private static readonly HashSet<string> SaleDocument =
        new(StringComparer.Ordinal) { "Fatura", "FaturaIade", "AracSatis", "Ceza", "ServisYansitma", "DepozitoIrat", "DisHizmet" };

    public async Task<AracKarneRawDto> GetVehicleScorecardRawAsync(
        Guid vehicleId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Araç (query filter + RLS tenant-scope'lu) — yoksa/başka tenant'sa boş paket → servis null → 404.
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null)
            return new AracKarneRawDto(null, [], [], [], [], [], 0, 0, null, 0m, 0m,
                new FiloTutSatRow(vehicleId, 0m, 0m, 0, 0), null);

        // ---- GİDER (defterden): AccountRef = araç. Kategori etiketi kaynak varlıktan zenginleşir
        // (SigortaOdeme→poliçe Tip; Gider→Expense.Tip). Base = Amount×Rate (bellekte).
        // Tüm geçmiş çekilir; dönem penceresi BELLEKTE uygulanır — KPI için ömür-boyu toplamlar
        // aynı sorgudan türetilir (adversarial F2: KPI parası dönem filtresinden sızmasın).
        var expenseRawAll = (await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gider && e.AccountRef == vehicleId)
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct))
            // SIGNED base (FAZ 4.3): ters kayıt (Alacak Gider) negatif — iptal karneden de netleşir.
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId,
                A = (e.Direction == LedgerDirection.Debit ? 1m : -1m) * e.A, R = e.R })
            .ToList();
        var expenseRaw = expenseRawAll
            .Where(e => (from is not { } gf || e.EntryDateUtc >= gf) && (to is not { } gt || e.EntryDateUtc <= gt))
            .ToList();

        var policies = await db.InsurancePolicies.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId).ToListAsync(ct);
        var insuranceType = policies.ToDictionary(x => x.Id, x => x.Tip);
        var expenseRecords = await db.Expenses.AsNoTracking()
            .Where(x => x.VehicleId == vehicleId).ToListAsync(ct);
        var expenseType = expenseRecords.ToDictionary(x => x.Id, x => x.Tip);

        string ExpenseCategoryOf(string sourceType, Guid sourceId) => sourceType switch
        {
            "MtvOdeme" => "MTV",
            "DisHizmet" => "Dış Hizmet",
            "MuayeneOdeme" => "Muayene",
            "CezaOdeme" => "Trafik Cezası",   // FAZ-60 kalem bazlı ceza ödemesi (Borç Gider / Alacak Kasa-Banka)
            "SigortaOdeme" => insuranceType.TryGetValue(sourceId, out var t) && t == InsuranceType.Kasko
                ? "Kasko" : "Sigorta (Trafik)",
            "Gider" => expenseType.TryGetValue(sourceId, out var g) ? g switch
            {
                ExpenseType.Genel => "Genel Gider",
                ExpenseType.Arac => "Araç Gideri",
                ExpenseType.Personel => "Personel",
                ExpenseType.Sigorta => "Sigorta",
                ExpenseType.Mtv => "MTV",
                ExpenseType.Muayene => "Muayene",
                ExpenseType.Finansman => "Finansman",
                _ => "Diğer"
            } : "Diğer",
            _ => sourceType
        };
        var expenses = expenseRaw
            .Select(e => new AracLedgerGiderRow(
                e.EntryDateUtc.UtcDateTime.Year, ExpenseCategoryOf(e.SourceType, e.SourceId), e.A * e.R))
            .ToList();

        // ---- GELİR (defterden): GetKarlilikRowsAsync atıf kurallarının araç-scope'lu birebir kopyası
        // (parite testi kilitler). Kaynak id-kümeleri bu araca göre kurulur; atanamayan gelir karnede YOK.
        var rentals = await db.Rentals.AsNoTracking().Where(r => r.VehicleId == vehicleId)
            .Select(r => new { r.Id, r.SozlesmeNo, r.BasTar, r.BitTar, r.GercekDonusTar, r.Durum, r.GenelToplam, r.CikisKm, r.DonusKm })
            .ToListAsync(ct);
        var rentalIds = rentals.Select(r => r.Id).ToList();

        // Fatura bağı: RentalId (base) veya KaynakKiraId (fark) bu aracın kirasına işaret eder;
        // iade faturaları KaynakFaturaId ile bu kümeye iki-hop bağlanır.
        var directInvoiceRows = await db.Invoices.AsNoTracking()
            .Where(i => (i.RentalId != null && rentalIds.Contains(i.RentalId.Value))
                     || (i.KaynakKiraId != null && rentalIds.Contains(i.KaynakKiraId.Value)))
            .Select(i => new { i.Id, i.RentalId, i.KaynakKiraId }).ToListAsync(ct);
        var directInvoices = directInvoiceRows.Select(i => i.Id).ToList();
        var refundInvoice = await db.Invoices.AsNoTracking()
            .Where(i => i.KaynakFaturaId != null && directInvoices.Contains(i.KaynakFaturaId.Value))
            .Select(i => i.Id).ToListAsync(ct);
        var invIds = directInvoices.Concat(refundInvoice).ToHashSet();
        // Kira olayı DeftereYansir sinyali: kiranın parası deftere FATURA ile girer (iptal edilse bile
        // kesilmiş fatura defterde kalır — immutable). "İptal değil" durumuna değil buna bakılır (adversarial F3).
        var invoicedRentals = directInvoiceRows
            .Select(i => i.RentalId ?? i.KaynakKiraId!.Value).ToHashSet();

        var sales = await db.VehicleSales.AsNoTracking().Where(s => s.VehicleId == vehicleId)
            .Select(s => new { s.Id, s.No, s.Tarih, s.GenelToplam, s.Durum }).ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToHashSet();

        // Ceza: VehicleId öncelikli (Karlilik ile aynı) — VehicleId BAŞKA araca işaret ediyorsa buraya sayılmaz;
        // VehicleId=null + RentalId bu aracın kirası → fallback.
        var penaltyIds = (await db.Penalties.AsNoTracking()
                .Where(p => p.VehicleId == vehicleId
                         || (p.VehicleId == null && p.RentalId != null && rentalIds.Contains(p.RentalId.Value)))
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var serviceRecords = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId).ToListAsync(ct);
        var serviceIds = serviceRecords.Select(s => s.Id).ToHashSet();

        // Depozito iratları (FAZ 1.2): bu aracın kiralarına bağlı olanlar.
        var forfeits = await db.DepozitoIratlar.AsNoTracking()
            .Where(d => d.RentalId != null && rentalIds.Contains(d.RentalId.Value)).ToListAsync(ct);
        var forfeitIds = forfeits.Select(d => d.Id).ToHashSet();

        // Dış hizmet alımları (FAZ 4.3): komisyon geliri bu aracın kiralarına bağlı kayıtlardan.
        var outsourcedServiceIds = (await db.DisHizmetAlimlari.AsNoTracking()
            .Where(d => rentalIds.Contains(d.RentalId)).Select(d => d.Id).ToListAsync(ct)).ToHashSet();

        var revenueRawAll = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gelir)
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct);
        var revenueRaw = revenueRawAll
            .Where(e => (from is not { } ef || e.EntryDateUtc >= ef) && (to is not { } et || e.EntryDateUtc <= et))
            .ToList();

        string? RevenueSource(string st, Guid sid) => st switch
        {
            "Fatura" or "FaturaIade" when invIds.Contains(sid) => "Kira/Fatura",
            "AracSatis" when saleIds.Contains(sid) => "Araç Satışı",
            "Ceza" when penaltyIds.Contains(sid) => "Ceza Yansıtma",
            "ServisYansitma" when serviceIds.Contains(sid) => "Servis Yansıtma",
            "DepozitoIrat" when forfeitIds.Contains(sid) => "Depozito İradı",
            "DisHizmet" when outsourcedServiceIds.Contains(sid) => "Dış Hizmet Komisyonu", // FAZ 4.3
            _ => null // başka araca/kaynağa ait ya da atanamayan → karnede yok
        };
        var revenues = revenueRaw
            .Select(e => new { e, K = RevenueSource(e.SourceType, e.SourceId) })
            .Where(x => x.K != null)
            .Select(x => new AracLedgerGelirRow(
                x.e.EntryDateUtc.UtcDateTime.Year, x.K!,
                (x.e.Direction == LedgerDirection.Credit ? 1m : -1m) * x.e.A * x.e.R))
            .ToList();
        // Ömür-boyu (pencereden bağımsız) toplamlar — KPI/amortisman paydaları bunlardan.
        var lifetimeExpense = expenseRawAll.Sum(e => e.A * e.R);
        // FAZ 2.2 Tut/Sat hamı: son-12-ay / önceki-12-ay gider (defter) — now-göreli pencereler.
        var nowUtc = DateTimeOffset.UtcNow;
        var last12Start = nowUtc.AddMonths(-12);
        var previous12Start = nowUtc.AddMonths(-24);
        var expense12 = expenseRawAll.Where(e => e.EntryDateUtc >= last12Start).Sum(e => e.A * e.R);
        var expensePrevious12 = expenseRawAll
            .Where(e => e.EntryDateUtc >= previous12Start && e.EntryDateUtc < last12Start).Sum(e => e.A * e.R);
        var lifetimeRevenue = revenueRawAll
            .Where(e => RevenueSource(e.SourceType, e.SourceId) != null)
            .Sum(e => (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R);

        // ---- OLAYLAR ("neyi ne zaman") — kaynak varlıktan, BİLGİ amaçlı (tutar brüt/native; P&L'e toplanmaz).
        var mtvs = await db.MtvRecords.AsNoTracking().Where(m => m.VehicleId == vehicleId).ToListAsync(ct);
        var loans = await db.AracKredileri.AsNoTracking().Where(k => k.VehicleId == vehicleId).ToListAsync(ct);
        var inspections = await db.InspectionRecords.AsNoTracking().Where(i => i.VehicleId == vehicleId).ToListAsync(ct);

        var events = new List<AracOlayRow>();
        foreach (var p in policies)
            events.Add(new AracOlayRow(p.Baslangic,
                p.Tip == InsuranceType.Kasko ? "Kasko" : "Trafik Sigortası",
                $"Poliçe {p.PoliceNo ?? "-"} ({p.Firma ?? "-"}) — bitiş {p.Bitis:dd.MM.yyyy}" + (p.Odendi ? "" : " — ÖDENMEDİ"),
                p.Prim + p.ZeyilPrim, p.Odendi));
        foreach (var m in mtvs)
            events.Add(new AracOlayRow(m.Vade, "MTV",
                $"Dönem {m.Donem}" + (m.Odendi ? "" : " — ÖDENMEDİ"), m.Tutar, m.Odendi));
        foreach (var i in inspections)
            events.Add(new AracOlayRow(i.MuayeneTarihi, "Muayene",
                $"Geçerlilik {i.Bitis:dd.MM.yyyy}" + (i.Odendi ? "" : " — ÖDENMEDİ"), i.Ucret + i.Ceza, i.Odendi));
        foreach (var s in serviceRecords)
            events.Add(new AracOlayRow(s.GirisTarihi, $"Servis ({s.Tip})",
                $"{s.No} — km {s.GirisKm}→{(s.CikisKm?.ToString() ?? "-")} ({s.Durum})"
                + (s.Yansitildi ? $" — rücu {s.YansitilanTutar:N2}" : ""),
                s.ToplamIscilik, false)); // servis maliyeti deftere yazılmaz (mali belge değil)
        foreach (var kr in loans)
            events.Add(new AracOlayRow(kr.BaslangicTarihi, "Kredi",
                $"{kr.No} ({kr.BankaAdi}) — {kr.OdenenTaksit}/{kr.TaksitSayisi} taksit ({kr.Durum})",
                kr.KrediTutari, false)); // anapara defter dışı; taksit ÖDEMELERİ Gider(Finansman) olarak düşer
        foreach (var d in forfeits)
            events.Add(new AracOlayRow(d.Tarih, "Depozito İradı",
                d.Aciklama ?? "İade edilmeyen depozito gelir yazıldı", d.Tutar, true));
        foreach (var x in expenseRecords)
            events.Add(new AracOlayRow(x.Tarih, $"Gider ({x.Tip})",
                $"{x.No}{(string.IsNullOrWhiteSpace(x.Aciklama) ? "" : " — " + x.Aciklama)}", x.GenelToplam, true));
        foreach (var s in sales)
            events.Add(new AracOlayRow(s.Tarih, "Satış",
                s.No + (s.Durum == SaleStatus.Iptal ? " — İPTAL" : ""), s.GenelToplam,
                s.Durum == SaleStatus.Tamamlandi));
        foreach (var r in rentals)
            events.Add(new AracOlayRow(r.BasTar, "Kira",
                $"{r.SozlesmeNo} — {r.BasTar:dd.MM.yyyy} → {(r.GercekDonusTar ?? r.BitTar):dd.MM.yyyy} ({r.Durum})"
                + (invoicedRentals.Contains(r.Id) ? "" : " — faturalanmamış"),
                r.GenelToplam, invoicedRentals.Contains(r.Id)));
        if (from is { } of) events.RemoveAll(o => o.Tarih < of);
        if (to is { } ot) events.RemoveAll(o => o.Tarih > ot);
        events = events.OrderByDescending(o => o.Tarih).ToList();

        // ---- KPI hamı: kira aralıkları (İptal hariç; efektif bitiş = GercekDonusTar ?? BitTar),
        // servis aralıkları (İptal hariç), katedilen km (çıkış+dönüş dolu kiralar).
        var activeRentals = rentals.Where(r => r.Durum != RentalStatus.Iptal).ToList();
        var rentalRanges = activeRentals
            .Select(r => new DolulukKiraRowDto(r.BasTar, r.GercekDonusTar ?? r.BitTar)).ToList();
        // İptal (FAZ-76) ve Rezerve (FAZ-16: gerçekleşmemiş randevu) bakım günü SAYILMAZ.
        var serviceIntervals = serviceRecords
            .Where(s => s.Durum is not (ServiceStatus.Iptal or ServiceStatus.Rezerve))
            .Select(s => new AracServisGunRow(s.GirisTarihi, s.CikisTarihi)).ToList();
        var traveledKm = activeRentals.Where(r => r.CikisKm != null && r.DonusKm != null)
            .Sum(r => r.DonusKm!.Value - r.CikisKm!.Value);
        // FAZ 2.2: km pencereleri — efektif bitişi pencerede olan kiraların km'si.
        int KmWindow(DateTimeOffset start, DateTimeOffset bit) => activeRentals
            .Where(r => r.CikisKm != null && r.DonusKm != null)
            .Where(r => (r.GercekDonusTar ?? r.BitTar) >= start && (r.GercekDonusTar ?? r.BitTar) < bit)
            .Sum(r => r.DonusKm!.Value - r.CikisKm!.Value);
        var km12 = KmWindow(last12Start, nowUtc.AddDays(1));
        var kmPrevious12 = KmWindow(previous12Start, last12Start);

        // FAZ 2.2 sınıf (Grup) ortalaması: grup araçlarının son-12-ay gider ÷ IkinciElDeger oranlarının
        // ortalaması (IkinciEl>0 olanlar; kendisi dahil). Grup yoksa null.
        decimal? groupAvg = null;
        if (!string.IsNullOrWhiteSpace(vehicle.Grup))
        {
            var groupVehicles = await db.Vehicles.AsNoTracking()
                .Where(v => v.Grup == vehicle.Grup && v.IkinciElDeger > 0)
                .Select(v => new { v.Id, v.IkinciElDeger }).ToListAsync(ct);
            if (groupVehicles.Count > 0)
            {
                var ids = groupVehicles.Select(g => (Guid?)g.Id).ToList();
                var groupExpense = (await db.AccountLedgerEntries.AsNoTracking()
                        .Where(e => e.AccountType == LedgerAccountType.Gider
                                    && e.AccountRef != null && ids.Contains(e.AccountRef)
                                    && e.EntryDateUtc >= last12Start)
                        .Select(e => new { e.AccountRef, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
                        .ToListAsync(ct))
                    .GroupBy(x => x.AccountRef!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => (x.Direction == LedgerDirection.Debit ? 1m : -1m) * x.A * x.R));
                var rates = groupVehicles
                    .Select(g => groupExpense.GetValueOrDefault(g.Id) / g.IkinciElDeger!.Value).ToList();
                groupAvg = rates.Count > 0 ? rates.Average() : null;
            }
        }
        var lastSale = sales.Where(s => s.Durum == SaleStatus.Tamamlandi)
            .Select(s => (DateTimeOffset?)s.Tarih).DefaultIfEmpty(null).Max();

        // FAZ 2.5: km zaman serisi (Tarih artan — servis dönem-km farkını bu sıradan alır).
        var kmLogs = await db.KmLoglari.AsNoTracking()
            .Where(k => k.VehicleId == vehicleId)
            .OrderBy(k => k.Tarih).ThenBy(k => k.Km)
            .Select(k => new AracKmLogRow(k.Tarih, k.Km))
            .ToListAsync(ct);

        return new AracKarneRawDto(vehicle, revenues, expenses, events,
            rentalRanges, serviceIntervals, activeRentals.Count, traveledKm, lastSale,
            lifetimeRevenue, lifetimeExpense,
            new FiloTutSatRow(vehicleId, expense12, expensePrevious12, km12, kmPrevious12), groupAvg, kmLogs);
    }

    public async Task<FiloAnalizRawDto> GetFleetAnalysisRawAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        // P&L: mevcut Karlilik atfı yeniden kullanılır (tek doğruluk kaynağı). Pencere verilmişse KPI
        // payları için ömür-boyu set AYRICA çekilir (karışık-payda dersi); verilmemişse aynı liste.
        var window = await GetProfitabilityRowsAsync(from, to, ct);
        var lifetime = from is null && to is null ? window : await GetProfitabilityRowsAsync(null, null, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var lastSale = (await db.VehicleSales.AsNoTracking()
                .Where(s => s.Durum == SaleStatus.Tamamlandi)
                .GroupBy(s => s.VehicleId)
                .Select(g => new { VehicleId = g.Key, Tarih = g.Max(x => x.Tarih) })
                .ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x.Tarih);

        var vehicles = (await db.Vehicles.AsNoTracking()
                .Select(v => new { v.Id, v.Plaka, v.Grup, v.Segment, v.Sube, v.AlimBedeli, v.AlimTarihi, v.FiloGirisTarih, v.FiloCikisTarih, v.Durum, v.IkinciElDeger })
                .ToListAsync(ct))
            .Select(v => new FiloAracRow(v.Id, v.Plaka, v.Grup, v.Segment, v.Sube,
                v.AlimBedeli, v.AlimTarihi, v.FiloGirisTarih, v.FiloCikisTarih,
                v.Durum, lastSale.TryGetValue(v.Id, out var t) ? t : null, v.IkinciElDeger))
            .ToList();

        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new FiloKiraRow(r.VehicleId, r.BasTar, r.GercekDonusTar ?? r.BitTar, r.CikisKm, r.DonusKm))
            .ToListAsync(ct);

        // FAZ 2.2 Tut/Sat hamı — FAZ 6.2: OrtakSorgular'a taşındı (FiloBildirimUretici ile TEK kaynak;
        // pencereleme iki yerde yazılıp sessizce ayrışmasın). Karne kartı == filo kolonu parite testi kilit.
        var holdSellRaw = (await SharedQueries.HoldSellRawAsync(db, DateTimeOffset.UtcNow, ct)).Ham;

        return new FiloAnalizRawDto(window, lifetime, vehicles, rentals, holdSellRaw);
    }
}
