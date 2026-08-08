using Microsoft.EntityFrameworkCore;
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
                Amount = e.Amount.Amount, Rate = e.Amount.Rate
            })
            .ToListAsync(ct);

        return raw
            .Select(r => new LedgerRowDto(
                r.EntryDateUtc, r.AccountType, r.Direction, r.SourceType, r.Description, r.Amount * r.Rate))
            .ToList();
    }

    public async Task<IReadOnlyList<CariLedgerRowDto>> GetCariLedgerRowsAsync(
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
    public async Task<IReadOnlyList<CariKartDto>> GetCariKartlariAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking()
            .Select(c => new CariKartDto(
                c.Id, c.CepTel, c.Email, c.BankaAdi, c.Doviz,
                c.OzelCariTip, c.Sinif, c.Tip == CariType.Kurumsal, c.Pasif, c.VergiNo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExtreOzetiRowDto>> GetExtreOzetiRowsAsync(
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
            .OrderBy(x => x.i.VadeTarihi == null).ThenBy(x => x.i.VadeTarihi).ThenBy(x => x.i.No)
            .Take(limit)
            .Select(x => new
            {
                x.i.Id, x.i.No, x.i.Tarih, x.i.VadeTarihi, x.i.CariId, x.i.GenelToplam,
                x.i.Currency, x.i.Kur, x.i.IadeMi,
                CariAd = x.c == null ? null : (x.c.Tip == CariType.Bireysel
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

    public async Task<IReadOnlyList<TahsilatMutabakatRowDto>> GetTahsilatMutabakatRowsAsync(
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
                var plakaAra = a.ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.r.SozlesmeNo, $"%{a}%")
                              || (x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{plakaAra}%"))
                              || (x.c != null && x.c.Unvan != null && EF.Functions.ILike(x.c.Unvan, $"%{a}%"))
                              || (x.c != null && x.c.Ad != null && EF.Functions.ILike(x.c.Ad, $"%{a}%"))
                              || (x.c != null && x.c.Soyad != null && EF.Functions.ILike(x.c.Soyad, $"%{a}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 2000, 1, 20000);
        var kiralar = await q.OrderByDescending(x => x.r.BasTar).Take(limit)
            .Select(x => new
            {
                x.r.Id, x.r.SozlesmeNo, x.r.MusteriId, x.r.BasTar, x.r.Durum, x.r.Doviz,
                x.r.Tutar, x.r.DamgaVergisi, x.r.GenelToplam, x.r.Tahsilat,
                Plaka = x.v == null ? null : x.v.Plaka,
                MusteriAd = x.c == null ? null : (x.c.Tip == CariType.Bireysel
                    ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan)
            })
            .ToListAsync(ct);
        if (kiralar.Count == 0) return [];

        var ids = kiralar.Select(k => k.Id).ToList();

        // FATURALANAN (iade-netli, iptal hariç) — OrtakSorgular.FarkStateAsync ile AYNI kural,
        // burada küme-bazlı: kira başına tek tek sorgu N+1 olurdu.
        var faturalar = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && !i.IadeMi
                        && ((i.RentalId != null && ids.Contains(i.RentalId.Value))
                            || (i.KaynakKiraId != null && ids.Contains(i.KaynakKiraId.Value))))
            .Select(i => new { i.Id, i.GenelToplam, i.RentalId, i.KaynakKiraId })
            .ToListAsync(ct);
        var faturaKira = faturalar.ToDictionary(f => f.Id, f => f.RentalId ?? f.KaynakKiraId!.Value);
        var faturaIds = faturalar.Select(f => f.Id).ToList();
        var iadeler = faturaIds.Count == 0 ? [] : await db.Invoices.AsNoTracking()
            .Where(i => i.IadeMi && i.Durum != InvoiceStatus.Iptal
                        && i.KaynakFaturaId != null && faturaIds.Contains(i.KaynakFaturaId.Value))
            .Select(i => new { i.GenelToplam, Kaynak = i.KaynakFaturaId!.Value })
            .ToListAsync(ct);

        var faturalanan = kiralar.ToDictionary(k => k.Id, _ => 0m);
        foreach (var f in faturalar) faturalanan[faturaKira[f.Id]] += f.GenelToplam;
        foreach (var i in iadeler)
            if (faturaKira.TryGetValue(i.Kaynak, out var kid)) faturalanan[kid] -= i.GenelToplam;

        // DEFTER TAHSİLATI: kasa hareketlerinden yeniden toplanır. İşaret/döviz kuralı
        // CashRepository.RentalDelta'dan gelir — ikinci bir kopya yazılmaz.
        var hareketler = await db.CashTransactions.AsNoTracking()
            .Where(t => t.RentalId != null && ids.Contains(t.RentalId.Value))
            .ToListAsync(ct);
        var kiraDoviz = kiralar.ToDictionary(k => k.Id, k => k.Doviz);
        var defterTahsilat = kiralar.ToDictionary(k => k.Id, _ => 0m);
        foreach (var t in hareketler)
        {
            var kid = t.RentalId!.Value;
            try { defterTahsilat[kid] += CashRepository.RentalDelta(t, kiraDoviz[kid]); }
            catch (RentACar.Application.Common.ValidationException)
            {
                // Kira dövizinden FARKLI bir hareket: RentalDelta bunu reddeder. Raporun görevi
                // hatayı GÖSTERMEK, çökmek değil → katkısı 0 kalır ve satır "tutarsız" görünür.
            }
        }

        // Müşteri bakiyesi (defterden) — sözleşme bakiyesiyle karıştırılmasın diye ayrı kolon.
        var musteriIds = kiralar.Select(k => k.MusteriId).Distinct().ToList();
        var cariHareket = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef != null
                        && musteriIds.Contains(e.AccountRef.Value))
            .Select(e => new { e.AccountRef, e.Direction, Tutar = e.Amount.Amount * e.Amount.Rate })
            .ToListAsync(ct);
        var musteriBakiye = cariHareket
            .GroupBy(e => e.AccountRef!.Value)
            .ToDictionary(g => g.Key,
                g => g.Sum(e => e.Direction == LedgerDirection.Debit ? e.Tutar : -e.Tutar));

        var sonuc = kiralar.Select(k => new TahsilatMutabakatRowDto(
            k.Id, k.SozlesmeNo, k.Plaka, k.MusteriId,
            string.IsNullOrWhiteSpace(k.MusteriAd) ? "(bilinmeyen cari)" : k.MusteriAd!.Trim(),
            k.BasTar, k.Durum, k.Doviz ?? "TRY",
            k.Tutar, k.DamgaVergisi ?? 0m, k.GenelToplam,
            k.Tahsilat, defterTahsilat[k.Id], faturalanan[k.Id],
            musteriBakiye.GetValueOrDefault(k.MusteriId))).ToList();

        return filter?.YalnizTutarsiz == true ? sonuc.Where(x => x.Tutarsiz).ToList() : sonuc;
    }

    public async Task<IReadOnlyList<EkHizmetDetayRow>> GetEkHizmetDetayRowsAsync(
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
            join rez in db.Reservations.AsNoTracking() on r.ReservationId equals (Guid?)rez.Id into rg
            from rez in rg.DefaultIfEmpty()
            join t in db.EkHizmetTanimlari.AsNoTracking() on a.EkHizmetTanimId equals t.Id into tg
            from t in tg.DefaultIfEmpty()
            join p in db.Personeller.AsNoTracking() on a.PersonelId equals (Guid?)p.Id into pg
            from p in pg.DefaultIfEmpty()
            select new { a, r, v, c, rez, t, p };

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
                var plakaAra = a2.ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.a.Ad, $"%{a2}%")
                              || EF.Functions.ILike(x.r.SozlesmeNo, $"%{a2}%")
                              || (x.v != null && EF.Functions.ILike(x.v.Plaka, $"%{plakaAra}%"))
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
                MusteriAd = x.c == null ? null : (x.c.Tip == CariType.Bireysel
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
        var kiraIds = rows.Select(x => x.RentalId).Distinct().ToList();
        // Ters kayıt AYRI bir satırdır ve orijinali işaretlemez (TersAlinanId ile ona bakar).
        // Bu yüzden yalnız `!TersKayitMi` demek YETMEZ: iptal edilmiş bir tahsilat "ilk tahsilat"
        // olarak görünürdü. Hem ters-kayıt satırları hem TERS ALINMIŞ orijinaller elenir.
        var tersAlinanlar = db.CashTransactions.AsNoTracking()
            .Where(t => t.TersAlinanId != null).Select(t => t.TersAlinanId!.Value);
        var ilkTahsilat = (await db.CashTransactions.AsNoTracking()
                .Where(t => t.RentalId != null && kiraIds.Contains(t.RentalId.Value)
                            && t.Tip == CashTransactionType.Tahsilat && !t.TersKayitMi
                            && !tersAlinanlar.Contains(t.Id))
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
            ilkTahsilat.TryGetValue(x.RentalId, out var it) ? it : null,
            x.SistemKalemi)).ToList();
    }

    public async Task<KarsilastirmaliAnalizDto> GetKarsilastirmaliAnalizAsync(
        KarsilastirmaliAnalizFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var tablo = string.Equals(filter.Tablo, "Rezervasyon", StringComparison.OrdinalIgnoreCase)
            ? "Rezervasyon" : "Kira";
        var gunMu = string.Equals(filter.VeriTuru, "Gun", StringComparison.OrdinalIgnoreCase);
        var kirilim = filter.Kirilim switch
        {
            "RezKaynagi" => "RezKaynagi",
            "CikisNoktasi" => "CikisNoktasi",
            _ => "AracGrubu"
        };

        // Varsayılan pencere: son 12 ayın BAŞI (ayın 1'i) → bugün. Kullanıcı verirse o kullanılır.
        var bit = filter.Bit ?? DateTimeOffset.UtcNow;
        var bas = filter.Bas ?? new DateTimeOffset(
            new DateTime(bit.UtcDateTime.Year, bit.UtcDateTime.Month, 1).AddMonths(-11), TimeSpan.Zero);

        // Araç grubu kırılımı için plaka→grup eşlemesi gerekiyor (Vehicle.Grup METİN alanı).
        var aracGrup = kirilim == "AracGrubu"
            ? await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Grup })
                .ToDictionaryAsync(v => v.Id, v => v.Grup, ct)
            : [];

        List<(DateTimeOffset Tarih, string Kirilim, decimal Deger)> ham;
        if (tablo == "Rezervasyon")
        {
            var q = db.Reservations.AsNoTracking().Where(r => r.Durum != ReservationStatus.Iptal);
            q = q.Where(r => r.BasTar >= bas && r.BasTar <= bit);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
            }
            var rows = await q.Select(r => new { r.BasTar, r.Gun, r.Kaynak, r.CikisOfisi, r.VehicleId })
                .ToListAsync(ct);
            ham = rows.Select(r => (r.BasTar, Kirilim: kirilim switch
            {
                "RezKaynagi" => r.Kaynak,
                "CikisNoktasi" => r.CikisOfisi,
                _ => aracGrup.GetValueOrDefault(r.VehicleId)
            } ?? "", Deger: gunMu ? r.Gun : 1m)).ToList();
        }
        else
        {
            var q = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal);
            q = q.Where(r => r.BasTar >= bas && r.BasTar <= bit);
            if (!string.IsNullOrWhiteSpace(filter.Ofis))
            {
                var o = filter.Ofis.Trim();
                q = q.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
            }
            var rows = await q.Select(r => new { r.BasTar, r.Gun, r.Kaynak, r.CikisOfisi, r.VehicleId })
                .ToListAsync(ct);
            ham = rows.Select(r => (r.BasTar, Kirilim: kirilim switch
            {
                "RezKaynagi" => r.Kaynak,
                "CikisNoktasi" => r.CikisOfisi,
                _ => aracGrup.GetValueOrDefault(r.VehicleId)
            } ?? "", Deger: gunMu ? r.Gun : 1m)).ToList();
        }

        // Ay kolonları pencereden ÜRETİLİR (veriden değil): veri olmayan ay da kolon olarak görünür,
        // aksi hâlde "o ay hiç iş yok" bilgisi grid'den sessizce kaybolurdu.
        var aylar = new List<string>();
        var imlec = new DateTime(bas.UtcDateTime.Year, bas.UtcDateTime.Month, 1);
        var sonAy = new DateTime(bit.UtcDateTime.Year, bit.UtcDateTime.Month, 1);
        while (imlec <= sonAy && aylar.Count < 120)   // üst sınır: absürt aralıkta kolon patlamasın
        {
            aylar.Add(imlec.ToString("yyyy-MM"));
            imlec = imlec.AddMonths(1);
        }

        var satirlar = ham
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Kirilim) ? "(belirtilmemiş)" : x.Kirilim.Trim())
            .Select(g => new KarsilastirmaliSatirDto(
                g.Key,
                g.GroupBy(x => x.Tarih.UtcDateTime.ToString("yyyy-MM"))
                 .ToDictionary(a => a.Key, a => a.Sum(x => x.Deger))))
            .OrderByDescending(s => s.Toplam).ThenBy(s => s.Kirilim)
            .ToList();

        return new KarsilastirmaliAnalizDto(aylar, satirlar, tablo,
            gunMu ? "Gun" : "Adet", kirilim);
    }

    public async Task<IReadOnlyList<VehicleStatus>> GetVehicleStatusesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Tek doğruluk kaynağı (denetim O12b): WhatsApp operasyon özeti de AYNI kaynağı kullanır.
        return await OrtakSorgular.VehicleDurumlariAsync(db, ct);
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
    private const string Atanmamis = "(Atanmamış)";

    private static string Etiket(string? s) => string.IsNullOrWhiteSpace(s) ? Atanmamis : s.Trim();

    public async Task<IReadOnlyList<SigortaMuayeneRow>> GetSigortaMuayeneRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // TÜM araçlar (satılmış/pasif dahil): belge envanteri, kiralanabilirlik değil. Eksik belge
        // görünmediğinde rapor işe yaramaz.
        var araclar = await db.Vehicles.AsNoTracking().ToListAsync(ct);
        if (araclar.Count == 0) return [];

        // Araç başına EN GEÇ biten poliçe/muayene alınır (yenilenmiş belgede eski satır değil,
        // GEÇERLİ olan görünmeli).
        var policeler = await db.InsurancePolicies.AsNoTracking()
            .Select(p => new { p.VehicleId, p.Tip, p.Bitis }).ToListAsync(ct);
        var muayeneler = await db.InspectionRecords.AsNoTracking()
            .Select(m => new { m.VehicleId, m.Bitis }).ToListAsync(ct);
        // MTV: ÖDENMEMİŞ olanın en yakın vadesi; hepsi ödendiyse en geç vade (bilgi).
        var mtvler = await db.MtvRecords.AsNoTracking()
            .Select(m => new { m.VehicleId, m.Vade, m.Odendi }).ToListAsync(ct);

        DateTimeOffset? SonPolice(Guid vid, RentACar.Domain.Enums.InsuranceType tip)
            => policeler.Where(p => p.VehicleId == vid && p.Tip == tip)
                .Select(p => (DateTimeOffset?)p.Bitis).DefaultIfEmpty(null).Max();

        return araclar.Select(v =>
        {
            var mtvAcik = mtvler.Where(m => m.VehicleId == v.Id && !m.Odendi).ToList();
            var mtvHepsi = mtvler.Where(m => m.VehicleId == v.Id).ToList();
            DateTimeOffset? mtvVade = mtvAcik.Count > 0
                ? mtvAcik.Min(m => m.Vade)
                : mtvHepsi.Count > 0 ? mtvHepsi.Max(m => m.Vade) : null;

            return new SigortaMuayeneRow(
                v.Id, v.Plaka, v.Marka, v.Tip, v.ModelYili,
                v.Yakit?.ToString(), v.Vites?.ToString(), v.Sube, v.Grup,
                v.SasiNo, v.MotorNo, v.AracSahibi, v.BelgeNo, v.Kimde,
                SonPolice(v.Id, RentACar.Domain.Enums.InsuranceType.Trafik),
                SonPolice(v.Id, RentACar.Domain.Enums.InsuranceType.Kasko),
                muayeneler.Where(m => m.VehicleId == v.Id).Select(m => (DateTimeOffset?)m.Bitis).DefaultIfEmpty(null).Max(),
                mtvVade, mtvAcik.Count == 0 && mtvHepsi.Count > 0,
                v.ZIzni, v.ZIzniBitis, v.SeyrusiferBitis);
        })
        .OrderBy(r => r.Plaka, StringComparer.CurrentCulture)
        .ToList();
    }

    public async Task<FiloSubeHamPaket> GetFiloSubeHamAsync(
        DateTimeOffset pencereBas, DateTimeOffset pencereBit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var araclar = await db.Vehicles.AsNoTracking()
            .Select(v => new { v.Id, v.Sube, v.Durum, v.FiloDurum })
            .ToListAsync(ct);
        var subeAdi = araclar.ToDictionary(v => v.Id, v => Etiket(v.Sube));

        // Kira/rezervasyon/BAF ARACIN şubesine yazılır — sözleşmenin çıkış şubesine DEĞİL
        // (tek atıf kuralı; karışık atıf satırı kendi içinde tutarsız yapardı).
        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.VehicleId, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit >= pencereBas && r.BasTar <= pencereBit)
            .ToListAsync(ct);

        var rezervasyonlar = await db.Reservations.AsNoTracking()
            .Where(r => r.Durum != ReservationStatus.Iptal
                        && r.BasTar >= pencereBas && r.BasTar <= pencereBit)
            .Select(r => new { r.VehicleId, r.BasTar })
            .ToListAsync(ct);

        var baflar = await db.Baflar.AsNoTracking()
            .Where(b => b.Durum == BafDurum.Acik)
            .Select(b => b.VehicleId)
            .ToListAsync(ct);

        // Silinmiş araca bağlı satır sözlükte yoktur → "(Atanmamış)" kovasına düşer, sessizce kaybolmaz.
        string Ara(Guid id) => subeAdi.TryGetValue(id, out var s) ? s : Atanmamis;

        return new FiloSubeHamPaket(
            araclar.Select(v => new FiloAracHamRow(v.Id, Etiket(v.Sube), v.Durum, v.FiloDurum)).ToList(),
            kiralar.Select(r => new FiloKiraHamRow(Ara(r.VehicleId), r.BasTar, r.Bit)).ToList(),
            rezervasyonlar.Select(r => (Ara(r.VehicleId), r.BasTar)).ToList(),
            baflar.Select(Ara).ToList());
    }

    public async Task<DolulukAtifPaket> GetDolulukAtifAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var araclar = await db.Vehicles.AsNoTracking()
            .Select(v => new { v.Id, v.Sube, v.Grup })
            .ToListAsync(ct);
        var atif = araclar.ToDictionary(v => v.Id, v => (Sube: Etiket(v.Sube), Grup: Etiket(v.Grup)));

        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new { r.VehicleId, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit >= from && r.BasTar <= to)
            .ToListAsync(ct);

        // Rezervasyon: kiraya çevrilmiş olan HARİÇ — aksi hâlde aynı gün hem rezervasyon hem kira
        // olarak sayılır ve "Rez Doluluk" kira ile çift-sayılırdı.
        var rezler = await db.Reservations.AsNoTracking()
            .Where(r => r.Durum != ReservationStatus.Iptal && r.Durum != ReservationStatus.KirayaCevrildi)
            .Select(r => new { r.VehicleId, r.BasTar, r.BitTar, r.Kaynak })
            .Where(r => r.BitTar >= from && r.BasTar <= to)
            .ToListAsync(ct);

        (string Sube, string Grup) Ara(Guid id)
            => atif.TryGetValue(id, out var a) ? a : (Atanmamis, Atanmamis);

        return new DolulukAtifPaket(
            araclar.Select(v => new DolulukAracAtifRow(v.Id, Etiket(v.Sube), Etiket(v.Grup))).ToList(),
            kiralar.Select(r =>
            {
                var a = Ara(r.VehicleId);
                return new DolulukKiraAtifRow(r.BasTar, r.Bit, r.VehicleId, a.Sube, a.Grup);
            }).ToList(),
            rezler.Select(r =>
            {
                var a = Ara(r.VehicleId);
                return new DolulukRezAtifRow(r.BasTar, r.BitTar, r.VehicleId, a.Sube, a.Grup, Etiket(r.Kaynak));
            }).ToList());
    }

    public async Task<TahsilatFaturaDto> GetTahsilatFaturaAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Fatura: İptal hariç; base = GenelToplam × Kur (Kur düz kolon, bellek-içi çarpılır).
        var fq = db.Invoices.AsNoTracking().Where(i => i.Durum != InvoiceStatus.Iptal);
        if (from is { } ff) fq = fq.Where(i => i.Tarih >= ff);
        if (to is { } ft) fq = fq.Where(i => i.Tarih <= ft);
        var faturalar = await fq.Select(i => new { i.GenelToplam, i.Kur, i.IadeMi }).ToListAsync(ct);
        int faturaAdet = faturalar.Count;
        // İade faturası net toplamı DÜŞÜRÜR (mutabakat: fatura vs tahsilat doğru netleşsin).
        decimal faturaToplam = faturalar.Sum(f => f.GenelToplam * f.Kur * (f.IadeMi ? -1m : 1m));

        // Tahsilat: Tip=Tahsilat, ters kayıt hariç; base = Amount × Rate.
        var tq = db.CashTransactions.AsNoTracking()
            .Where(c => c.Tip == CashTransactionType.Tahsilat && !c.TersKayitMi);
        if (from is { } tf) tq = tq.Where(c => c.Tarih >= tf);
        if (to is { } tt) tq = tq.Where(c => c.Tarih <= tt);
        var tahsilatlar = await tq
            .Select(c => new { Amount = c.Amount.Amount, Rate = c.Amount.Rate }).ToListAsync(ct);
        int tahsilatAdet = tahsilatlar.Count;
        decimal tahsilatToplam = tahsilatlar.Sum(t => t.Amount * t.Rate);

        return new TahsilatFaturaDto(
            faturaAdet, faturaToplam, tahsilatAdet, tahsilatToplam, faturaToplam - tahsilatToplam);
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

        var q = db.ServiceRecords.AsNoTracking().Where(r => r.Durum == ServisDurum.Tamamlandi);
        if (from is { } f) q = q.Where(r => r.CikisTarihi >= f);
        if (to is { } t) q = q.Where(r => r.CikisTarihi <= t);

        var rows = await q
            .Select(r => new { r.VehicleId, r.Tip, r.ToplamIscilik })
            .ToListAsync(ct);

        var plaka = (await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);

        return rows
            .Select(r => new ServiceCostRowDto(
                r.VehicleId, plaka.TryGetValue(r.VehicleId, out var p) ? p : "(bilinmeyen araç)", r.Tip, r.ToplamIscilik))
            .ToList();
    }

    public async Task<IReadOnlyList<PeriyodikServisRow>> GetPeriyodikServisRowsAsync(
        PeriyodikServisFilter? filtre = null, CancellationToken ct = default)
    {
        // FAZ 6.2: birleşim OrtakSorgular'a taşındı — rapor sayfası ve FiloBildirimUretici (bakım-km
        // bildirimi) AYNI tanımı kullanır (O12a deseni; iki kopya sessizce ayrışmasın).
        // FAZ-76: filtre YALNIZ rapor yolundan geçer; üretici parametresiz çağırmaya devam eder.
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await OrtakSorgular.PeriyodikServisAsync(db, ct, filtre);
    }

    public async Task<IReadOnlyList<KmDetayRow>> GetKmDetayRowsAsync(
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
        var arac = (await db.Vehicles.AsNoTracking()
                .Select(v => new { v.Id, v.Plaka, v.Marka, v.Tip, v.Yakit, v.Vites }).ToListAsync(ct))
            .ToDictionary(v => v.Id);

        return rows
            .Select(r =>
            {
                var v = arac.GetValueOrDefault(r.VehicleId);
                return new KmDetayRow(
                    r.Id, r.SozlesmeNo, v?.Plaka ?? "(bilinmeyen araç)",
                    r.CikisKm!.Value, r.DonusKm!.Value, r.DonusKm!.Value - r.CikisKm!.Value,
                    r.KmLimit, r.FazlaKm, r.FazlaKmBedeli,
                    v?.Marka, v?.Tip, v?.Yakit?.ToString(), v?.Vites?.ToString(), r.BasTar, r.Bit);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RezervasyonKaynakRow>> GetRezervasyonKaynakRowsAsync(
        RezervasyonKaynakFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Reservations.AsNoTracking();

        // FAZ-76: tarih hangi alana uygulanacak — çıkış (varsayılan, eski davranış), dönüş ya da
        // kayıt tarihi. Eskiden yalnız BasTar vardı ve seçenek yoktu.
        if (filtre.Bas is { } f)
            q = filtre.TarihTipi switch
            {
                "Donus" => q.Where(r => r.BitTar >= f),
                "Kayit" => q.Where(r => r.CreatedAtUtc >= f),
                _ => q.Where(r => r.BasTar >= f)
            };
        if (filtre.Bit is { } t)
            q = filtre.TarihTipi switch
            {
                "Donus" => q.Where(r => r.BitTar <= t),
                "Kayit" => q.Where(r => r.CreatedAtUtc <= t),
                _ => q.Where(r => r.BasTar <= t)
            };

        if (!string.IsNullOrWhiteSpace(filtre.Ofis))
        {
            var o = filtre.Ofis.Trim();
            q = q.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
        }

        var rows = await q
            .Select(r => new { r.Kaynak, r.Gun, r.Tutar, r.Durum, r.VehicleId })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(filtre.Grup))
        {
            // Araç grubu rezervasyonda tutulmuyor → araçtan çözülür.
            var g = filtre.Grup.Trim();
            var grupIdler = (await db.Vehicles.AsNoTracking()
                    .Where(v => v.Grup != null && v.Grup.Trim() == g)
                    .Select(v => v.Id).ToListAsync(ct)).ToHashSet();
            rows = rows.Where(r => grupIdler.Contains(r.VehicleId)).ToList();
        }

        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Kaynak) ? "(belirtilmemiş)" : r.Kaynak!)
            .Select(g =>
            {
                // FAZ-76 DÜZELTME: İPTAL rezervasyonlar adet/gün/CİROYA dahil ediliyordu — bu bir
                // veri-doğruluğu hatasıydı (gerçekleşmemiş iş ciro sayılıyordu). Artık varsayılan
                // olarak DIŞARIDA; adedi ayrı kolonda görünür kalıyor ve istenirse dahil edilebiliyor.
                var sayilan = filtre.IptalleriDahilEt
                    ? g.ToList()
                    : g.Where(r => r.Durum != ReservationStatus.Iptal).ToList();
                return new RezervasyonKaynakRow(
                    g.Key, sayilan.Count, sayilan.Sum(r => r.Gun), sayilan.Sum(r => r.Tutar),
                    g.Count(r => r.Durum == ReservationStatus.Iptal));
            })
            .Where(r => r.Adet > 0 || r.IptalAdet > 0)
            .OrderByDescending(r => r.ToplamCiro)
            .ToList();
    }

    public async Task<IReadOnlyList<FaturaDonemRow>> GetFaturaDonemRowsAsync(
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
    /// FAZ-12 — araç durum-takip araç süzgeci (gün ve araç görünümü ORTAK kullanır; iki görünüm
    /// aynı araç kümesini anlatsın diye tek yerde). Tümü aracın KENDİ alanlarına bakar.
    /// </summary>
    private static IQueryable<Vehicle> AracDurumTakipSuz(IQueryable<Vehicle> q, AracDurumTakipFilter? f)
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

    public async Task<IReadOnlyList<AracDurumTakipRow>> GetAracDurumTakipRowsAsync(
        DateTimeOffset from, DateTimeOffset to, AracDurumTakipFilter? filtre = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // FAZ-76: opsiyonel şube süzgeci (FAZ-12'de araç sahibi/grup/SIPP/plakayla genişledi).
        // Filtre ARACIN alanlarına bakar ve kira/servis/BAF sayımlarının HEPSİ aynı araç kümesinden
        // gelir — karışık atıf satırı tutarsız yapardı (FAZ-77'de kurulan tek-atıf kuralı).
        var aracIdler = await AracDurumTakipSuz(db.Vehicles.AsNoTracking(), filtre)
            .Select(v => v.Id).ToListAsync(ct);
        var toplam = aracIdler.Count;
        if (toplam == 0) return [];
        var kume = aracIdler.ToHashSet();

        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal && kume.Contains(r.VehicleId))
            .Select(r => new { r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .ToListAsync(ct);

        // FAZ-76 DÜZELTME: İPTAL servis kayıtları "Bakım" günü olarak SAYILIYORDU. Diğer benzer
        // sorgularda bu filtre vardı; burada eksikti → iptal edilen bir servis aracı günlerce
        // bakımdaymış gibi gösteriyor ve "Boş" sayısını düşürüyordu.
        var servisler = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.Durum != ServisDurum.Iptal && kume.Contains(s.VehicleId))
            .Select(s => new { s.GirisTarihi, Cikis = s.CikisTarihi })
            .ToListAsync(ct);

        // BAF: açık tahsisler — BİLGİ kolonu, Bos hesabına girmez (bir araç hem kirada hem
        // tahsisli olabilir; çıkarsaydık çift düşüm yapardık).
        var baflar = await db.Baflar.AsNoTracking()
            .Where(b => b.Durum == BafDurum.Acik && kume.Contains(b.VehicleId))
            .Select(b => b.CreatedAtUtc)
            .ToListAsync(ct);

        var sonuc = new List<AracDurumTakipRow>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var dolu = kiralar.Count(k => k.BasTar.Date <= d && k.Bit.Date >= d);
            var bakim = servisler.Count(s => s.GirisTarihi.Date <= d && (s.Cikis ?? to).Date >= d);
            var bos = Math.Max(0, toplam - dolu - bakim);
            var baf = baflar.Count(b => b.Date <= d);
            sonuc.Add(new AracDurumTakipRow(new DateTimeOffset(d, TimeSpan.Zero), toplam, dolu, bakim, bos, baf));
        }
        return sonuc;
    }

    public async Task<IReadOnlyList<AracDurumTakipAracRow>> GetAracDurumTakipAracBazliRowsAsync(
        DateTimeOffset from, DateTimeOffset to, AracDurumTakipFilter? filtre = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var araclar = await AracDurumTakipSuz(db.Vehicles.AsNoTracking(), filtre)
            .Select(v => new { v.Id, v.Plaka, v.Sipp, v.Grup, v.Sube, v.AracSahibi })
            .ToListAsync(ct);
        if (araclar.Count == 0) return [];
        var kume = araclar.Select(a => a.Id).ToHashSet();

        var bas = from.Date;
        var bit = to.Date;
        if (bit < bas) return [];
        var toplamGun = (int)(bit - bas).TotalDays + 1;

        // Kira: İptal hariç; bitiş GERÇEK dönüş varsa odur (gün kırılımıyla BİREBİR aynı kural).
        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal && kume.Contains(r.VehicleId))
            .Select(r => new { r.VehicleId, r.BasTar, Bit = r.GercekDonusTar ?? r.BitTar })
            .ToListAsync(ct);

        // Servis: İptal hariç (FAZ-76 düzeltmesi); çıkışsız servis hâlâ devam ediyor → aralık sonuna dek.
        var servisler = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.Durum != ServisDurum.Iptal && kume.Contains(s.VehicleId))
            .Select(s => new { s.VehicleId, s.GirisTarihi, Cikis = s.CikisTarihi })
            .ToListAsync(ct);

        // BAF: gerçek ZİMMET ARALIĞI (çıkış → dönüş); dönmemişse aralık sonuna dek. İptal hariç.
        // NOT: gün kırılımındaki "Açık BAF" kolonu BAŞKA bir ölçüdür (o gün açık olan BAF KAYIT
        // sayısı); burada aracın kaç GÜN zimmette olduğu sayılıyor. İkisi bilerek ayrı.
        var baflar = await db.Baflar.AsNoTracking()
            .Where(b => b.Durum != BafDurum.Iptal && kume.Contains(b.VehicleId))
            .Select(b => new { b.VehicleId, b.CikisTarihi, Donus = b.DonusTarihi })
            .ToListAsync(ct);

        var kiraMap = kiralar.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Bas: x.BasTar.Date, Bit: x.Bit.Date)).ToList());
        var servisMap = servisler.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Bas: x.GirisTarihi.Date, Bit: (x.Cikis ?? to).Date)).ToList());
        var bafMap = baflar.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Bas: x.CikisTarihi.Date, Bit: (x.Donus ?? to).Date)).ToList());

        static bool Kapsar(List<(DateTime Bas, DateTime Bit)>? araliklar, DateTime g)
            => araliklar is not null && araliklar.Exists(a => a.Bas <= g && a.Bit >= g);

        var sonuc = new List<AracDurumTakipAracRow>(araclar.Count);
        foreach (var a in araclar)
        {
            kiraMap.TryGetValue(a.Id, out var kir);
            servisMap.TryGetValue(a.Id, out var srv);
            bafMap.TryGetValue(a.Id, out var baf);

            int dolu = 0, bakim = 0, bafGun = 0;
            for (var d = bas; d <= bit; d = d.AddDays(1))
            {
                // ÖNCELİK: Dolu > Bakım > Baf > Boş. Çakışan durumlarda gün TEK kovaya düşer;
                // böylece dört kovanın toplamı aralık gün sayısına EŞİT kalır (değişmez).
                if (Kapsar(kir, d)) dolu++;
                else if (Kapsar(srv, d)) bakim++;
                else if (Kapsar(baf, d)) bafGun++;
            }
            sonuc.Add(new AracDurumTakipAracRow(
                a.Id, a.Plaka, a.Sipp, a.Grup, a.Sube, a.AracSahibi,
                toplamGun, dolu, bakim, bafGun, toplamGun - dolu - bakim - bafGun));
        }

        // En çok boşta kalan üstte — canlının bu ekrandaki asıl sorusu ("hangi araç yatıyor").
        return sonuc
            .OrderByDescending(r => r.BosGun).ThenBy(r => r.Plaka, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AracGunlukDurumRow>> GetAracGunlukDurumRowsAsync(
        DateTimeOffset gun, AracGunlukDurumFilter? filtre = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var g = gun.Date;

        var q =
            from r in db.Rentals.AsNoTracking().Where(x => x.Durum != RentalStatus.Iptal)
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id
            join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id into cg
            from c in cg.DefaultIfEmpty()
            select new { r, v, c };

        if (filtre is not null)
        {
            if (!string.IsNullOrWhiteSpace(filtre.Plaka))
            {
                var p = filtre.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => EF.Functions.ILike(x.v.Plaka, $"%{p}%"));
            }
            if (!string.IsNullOrWhiteSpace(filtre.Grup))
            {
                var gr = filtre.Grup.Trim();
                q = q.Where(x => x.v.Grup != null && x.v.Grup.Trim() == gr);
            }
            if (!string.IsNullOrWhiteSpace(filtre.Sipp))
            {
                var sp = filtre.Sipp.Trim();
                q = q.Where(x => x.v.Sipp != null && x.v.Sipp.Trim() == sp);
            }
            if (!string.IsNullOrWhiteSpace(filtre.AracSahibi))
            {
                var s = filtre.AracSahibi.Trim();
                q = q.Where(x => x.v.AracSahibi != null && x.v.AracSahibi.Trim() == s);
            }
            if (!string.IsNullOrWhiteSpace(filtre.Ofis))
            {
                var o = filtre.Ofis.Trim();
                q = q.Where(x => x.r.CikisOfisi != null && x.r.CikisOfisi.Trim() == o);
            }
        }

        var ham = await q.Select(x => new
        {
            x.r.Id, x.r.SozlesmeNo, x.r.VehicleId, x.r.BasTar, x.r.BitTar, x.r.GercekDonusTar,
            x.r.Gun, x.r.Tutar, x.r.FazlaKmBedeli, x.r.YakitBedeli, x.r.UzatmaBedeli, x.r.KurSnapshot,
            x.r.CikisOfisi,
            x.v.Plaka, x.v.Sipp, x.v.Grup, x.v.AracSahibi,
            Musteri = x.c == null ? null : (x.c.Tip == CariType.Bireysel
                ? ((x.c.Ad ?? "") + " " + (x.c.Soyad ?? "")) : x.c.Unvan)
        }).ToListAsync(ct);

        // Aktiflik: gün kırılımının "Dolu" kovasıyla BİREBİR aynı (kapsayıcı, dönüş günü dahil).
        var aktif = ham.Where(x => x.BasTar.Date <= g && (x.GercekDonusTar ?? x.BitTar).Date >= g).ToList();
        if (aktif.Count == 0) return [];

        // Ek hizmet brütü kira başına — RentalAddOn tutarları BAZ PARADA saklanır (kur yok).
        var kiraIds = aktif.Select(x => x.Id).ToList();
        var ekHizmet = (await db.RentalAddOns.AsNoTracking()
                .Where(a => kiraIds.Contains(a.RentalId))
                .GroupBy(a => a.RentalId)
                .Select(gr => new { RentalId = gr.Key, Brut = gr.Sum(a => a.Toplam) })
                .ToListAsync(ct))
            .ToDictionary(x => x.RentalId, x => x.Brut);

        return aktif
            .Select(x =>
            {
                // Faturalanan gün sayısı bölendir. Gun alanı 0/eksikse takvim farkına düşülür
                // (en az 1 — sıfıra bölme yok).
                var bolen = x.Gun > 0 ? x.Gun
                    : Math.Max(1, (int)((x.GercekDonusTar ?? x.BitTar).Date - x.BasTar.Date).TotalDays);

                // Baz kira brütü kira DÖVİZİNDE tutulur → TL'ye KurSnapshot ile çevrilir.
                // Ek hizmet zaten TL; ikisi ayrı bölünür (bkz. AracGunlukDurumRow XML notu).
                var kiraTl = (x.Tutar + x.FazlaKmBedeli + x.YakitBedeli + x.UzatmaBedeli) * x.KurSnapshot;
                var hizmetTl = ekHizmet.TryGetValue(x.Id, out var h) ? h : 0m;

                var gunlukKira = decimal.Round(kiraTl / bolen, 2, MidpointRounding.AwayFromZero);
                var gunlukHizmet = decimal.Round(hizmetTl / bolen, 2, MidpointRounding.AwayFromZero);

                return new AracGunlukDurumRow(
                    x.VehicleId, x.Plaka, x.Sipp, x.Grup, x.AracSahibi,
                    x.Id, x.SozlesmeNo, string.IsNullOrWhiteSpace(x.Musteri) ? "(bilinmeyen cari)" : x.Musteri!.Trim(),
                    x.CikisOfisi, x.BasTar, x.GercekDonusTar ?? x.BitTar, bolen,
                    gunlukKira, gunlukHizmet, gunlukKira + gunlukHizmet);
            })
            .OrderByDescending(r => r.GunlukToplam).ThenBy(r => r.Plaka, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<MusteriSegmentRow>> GetMusteriSegmentRowsAsync(
        MusteriSegmentFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // FAZ-41 — süzgeç AGREGADAN ÖNCE kiralara uygulanır (pencere içi "ne yaptı" görünümü).
        var kiraQ = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal);
        if (filter is not null)
        {
            if (filter.Bas is { } bas) kiraQ = kiraQ.Where(r => r.BasTar >= bas);
            if (filter.Bit is { } bit) kiraQ = kiraQ.Where(r => r.BasTar <= bit);
            if (!string.IsNullOrWhiteSpace(filter.RezKaynak))
            {
                var k = filter.RezKaynak.Trim();
                kiraQ = kiraQ.Where(r => r.Kaynak != null && EF.Functions.ILike(r.Kaynak, k));
            }
            if (!string.IsNullOrWhiteSpace(filter.CikisOfis))
            {
                // METİN eşleşmesi (FK değil) — gerekçe MusteriSegmentFilter.CikisOfis XML notunda.
                var o = filter.CikisOfis.Trim();
                kiraQ = kiraQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == o);
            }
        }

        var grup = await kiraQ
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
        var hizmet = await (from a in db.RentalAddOns.AsNoTracking()
                            join r in kiraQ on a.RentalId equals r.Id
                            group a.Toplam * r.KurSnapshot by r.MusteriId into g
                            select new { MusteriId = g.Key, Toplam = g.Sum() })
            .ToListAsync(ct);
        var hizmetMap = hizmet.ToDictionary(x => x.MusteriId, x => x.Toplam);

        var cust = await db.Customers.AsNoTracking()
            .Select(c => new { c.Id, c.Tip, c.Ad, c.Soyad, c.Unvan, c.Email, c.CepTel, c.DogumTarihi })
            .ToListAsync(ct);
        var custMap = cust.ToDictionary(c => c.Id);

        var rows = grup
            .Select(g =>
            {
                custMap.TryGetValue(g.MusteriId, out var c);
                // Customer.DisplayName ile BİREBİR aynı kural (entity projeksiyonu yerine alan
                // projeksiyonu kullandığımız için burada tekrar yazılı; davranış değişmedi).
                var ad = c is null
                    ? "(bilinmeyen cari)"
                    : (c.Tip == CariType.Bireysel ? $"{c.Ad} {c.Soyad}".Trim() : (c.Unvan ?? string.Empty));

                return new MusteriSegmentRow(
                    g.MusteriId, ad, g.KiraSayisi, g.ToplamCiro, g.SonIslem,
                    g.ToplamCiro >= 10000m ? "VIP" : g.ToplamCiro > 0m ? "Standart" : "Pasif",
                    Mail: c?.Email, Tel: c?.CepTel,
                    OrtalamaKiraBedeli: g.KiraSayisi > 0 ? decimal.Round(g.ToplamCiro / g.KiraSayisi, 2, MidpointRounding.AwayFromZero) : 0m,
                    OrtalamaKm: g.KmAdet > 0 ? decimal.Round((decimal)(g.KmToplam ?? 0) / g.KmAdet, 2, MidpointRounding.AwayFromZero) : null,
                    DogumTarihi: c?.DogumTarihi,
                    IlkKiraZamani: g.IlkKira,
                    HizmetBedeli: hizmetMap.TryGetValue(g.MusteriId, out var h) ? h : 0m);
            });

        if (filter?.MinKiraSayisi is { } min) rows = rows.Where(r => r.KiraSayisi >= min);

        return rows.OrderByDescending(r => r.ToplamCiro).ToList();
    }

    public async Task<MusteriSegmentSecenekleri> GetMusteriSegmentSecenekleriAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Segment satırlarıyla AYNI kira kümesi (İptal hariç) ama SÜZGEÇSİZ — seçenek listesi
        // kendi seçimine göre daralırsa kullanıcı seçtiği filtreden geri dönemez.
        var q = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal);

        var kaynaklar = await q.Where(r => r.Kaynak != null && r.Kaynak != "")
            .Select(r => r.Kaynak!).Distinct().ToListAsync(ct);
        var ofisler = await q.Where(r => r.CikisOfisi != null && r.CikisOfisi != "")
            .Select(r => r.CikisOfisi!).Distinct().ToListAsync(ct);

        return new MusteriSegmentSecenekleri(
            kaynaklar.Select(x => x.Trim()).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture).ToList(),
            ofisler.Select(x => x.Trim()).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture).ToList());
    }

    public async Task<IReadOnlyList<PersonelCalismaRow>> GetPersonelCalismaRowsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var grup = await db.Baflar.AsNoTracking()
            .GroupBy(b => b.PersonelId)
            .Select(g => new { PersonelId = g.Key, TahsisSayisi = g.Count() })
            .ToListAsync(ct);

        var pers = (await db.Personeller.AsNoTracking().ToListAsync(ct))
            .ToDictionary(p => p.Id, p => $"{p.Ad} {p.Soyad}".Trim());

        return grup
            .Select(g => new PersonelCalismaRow(
                g.PersonelId, pers.TryGetValue(g.PersonelId, out var n) ? n : "(bilinmeyen personel)", g.TahsisSayisi))
            .OrderByDescending(r => r.TahsisSayisi)
            .ToList();
    }

    public async Task<GunlukFaaliyetDto> GetGunlukFaaliyetAsync(
        DateTimeOffset from, DateTimeOffset to, string? sube = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // FAZ-76 — şube süzgeci YALNIZ operasyon sayaçlarına (rezervasyon/kira/çıkış/dönüş)
        // uygulanır: bunlar CikisOfisi taşır. Tahsilat ve fatura şube boyutu TAŞIMAZ; onları
        // filtrelenmiş gibi göstermek yanlış olurdu, filtrelemeden bırakıp EKRANDA "şube kırılımı
        // yok" diye etiketliyoruz (karışık atıf yerine açık sınır).
        var rezQ = db.Reservations.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc <= to);
        var kiraYeniQ = db.Rentals.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc <= to);
        var cikisQ = db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal && r.BasTar >= from && r.BasTar <= to);
        var donusQ = db.Rentals.AsNoTracking()
            .Where(r => r.GercekDonusTar != null && r.GercekDonusTar >= from && r.GercekDonusTar <= to);

        if (!string.IsNullOrWhiteSpace(sube))
        {
            var sb = sube.Trim();
            rezQ = rezQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
            kiraYeniQ = kiraYeniQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
            cikisQ = cikisQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
            donusQ = donusQ.Where(r => r.CikisOfisi != null && r.CikisOfisi.Trim() == sb);
        }

        var yeniRez = await rezQ.CountAsync(ct);
        var yeniKira = await kiraYeniQ.CountAsync(ct);
        // Çıkış: o gün başlayan (İptal olmayan) kiralar. Dönüş: o gün gerçek dönüşü yapılan kiralar.
        var cikis = await cikisQ.CountAsync(ct);
        var donus = await donusQ.CountAsync(ct);

        // Tahsilat: TEK doğruluk kaynağı (denetim O12b — WhatsApp özeti aynı tanımı kullanır; TL-baz Σ Amount×Rate,
        // ters kayıt hariç). Pencere [from, to] kapalı → helper'a to+1tick (davranış birebir korunur).
        var (tahsilatAdet, tahsilatTutar) = await OrtakSorgular.TahsilatTlAsync(db, from, to.AddTicks(1), ct);

        // Fatura: İptal hariç; GenelToplam base zaten (Currency/Kur ayrı tutulur ama GenelToplam fatura
        // para birimindedir → günlük faaliyet sayacında brüt toplam olarak gösterilir).
        var faturalar = await db.Invoices.AsNoTracking()
            .Where(i => i.Durum != InvoiceStatus.Iptal && i.Tarih >= from && i.Tarih <= to)
            .Select(i => new { i.GenelToplam, i.Kur, i.IadeMi })
            .ToListAsync(ct);
        var faturaTutar = faturalar.Sum(f => f.GenelToplam * f.Kur * (f.IadeMi ? -1m : 1m)); // iade net'i düşürür

        return new GunlukFaaliyetDto(
            yeniRez, yeniKira, cikis, donus,
            tahsilatAdet, tahsilatTutar, faturalar.Count, faturaTutar);
    }

    public async Task<IReadOnlyList<KdvLineRowDto>> GetKdvLineRowsAsync(
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

    public async Task<IReadOnlyList<EkHizmetSalesRowDto>> GetEkHizmetSalesRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // İptal kiraların ek hizmetleri satış sayılmaz. Tarih: kalemin eklenme zamanı (CreatedAtUtc).
        var aktifKiraIds = db.Rentals.AsNoTracking().Where(r => r.Durum != RentalStatus.Iptal).Select(r => r.Id);
        var q = db.RentalAddOns.AsNoTracking().Where(a => aktifKiraIds.Contains(a.RentalId));
        if (from is { } f) q = q.Where(a => a.CreatedAtUtc >= f);
        if (to is { } t) q = q.Where(a => a.CreatedAtUtc <= t);

        // RentalAddOn tutarları baz para (TRY) olarak saklanır (Kur yok) → doğrudan kullanılır.
        return await q
            .Select(a => new EkHizmetSalesRowDto(a.Ad, a.Miktar, a.NetTutar, a.KdvTutar, a.Toplam, a.RentalId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EkHizmetAracSalesRow>> GetEkHizmetAracSalesRowsAsync(
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

    public async Task<IReadOnlyList<KarlilikSatirDto>> GetKarlilikRowsAsync(
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
        var giderRaw = await gq.Select(e => new { e.AccountRef, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);
        var giderByVeh = giderRaw.GroupBy(x => x.AccountRef ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.Sum(x => (x.Direction == LedgerDirection.Debit ? 1m : -1m) * x.A * x.R));

        // Gelir — HER İKİ yön (iade faturası Borç Gelir yazar → SignedBase ile netleşir).
        // SourceId(Fatura/FaturaIade) → Kira → Araç ile atfedilir; atfedilemeyen → Guid.Empty.
        var lq = db.AccountLedgerEntries.AsNoTracking()
            // PR-A: dönem kapanış fişi Gelir'i sıfırlayan iç virman (kaynak atfı yok → "(Atanmamış)") → HARİÇ.
            .Where(e => e.AccountType == LedgerAccountType.Gelir && e.SourceType != "DonemKapanis");
        if (from is { } ef) lq = lq.Where(e => e.EntryDateUtc >= ef);
        if (to is { } et) lq = lq.Where(e => e.EntryDateUtc <= et);
        var gelirRaw = await lq.Select(e => new { e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync(ct);

        // Atfetme haritaları: kaynak türüne göre araç çözümü. İade faturası RentalId=null taşır →
        // kira bağı KaynakFaturaId üzerinden (iki-hop): iade → kaynak fatura → RentalId ?? KaynakKiraId.
        // FARK faturası da RentalId=null taşır (kira-fatura unique index'ine çarpmasın) → kira bağı
        // KaynakKiraId'dedir (atıf düzeltmesi: fark + iade-of-fark geliri önceden "(Atanmamış)"a düşüyordu).
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
        var cezaToVeh = (await db.Penalties.AsNoTracking().Where(p => p.VehicleId != null || p.RentalId != null)
                .Select(p => new { p.Id, p.VehicleId, p.RentalId }).ToListAsync(ct))
            .Select(p => new
            {
                p.Id,
                VehicleId = p.VehicleId
                    ?? (p.RentalId is Guid prid && rentalToVeh.TryGetValue(prid, out var prv) ? prv : (Guid?)null)
            })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);
        var servisToVeh = (await db.ServiceRecords.AsNoTracking()
                .Select(s => new { s.Id, s.VehicleId }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.VehicleId);
        // Depozito iradı (FAZ 1.2): kira bağı → araç (kirasız irat Atanmamış'ta kalır).
        var iratToVeh = (await db.DepozitoIratlar.AsNoTracking().Where(d => d.RentalId != null)
                .Select(d => new { d.Id, RentalId = d.RentalId!.Value }).ToListAsync(ct))
            .Select(d => new { d.Id, VehicleId = rentalToVeh.TryGetValue(d.RentalId, out var iv) ? (Guid?)iv : null })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);
        // Dış hizmet komisyon geliri (FAZ 4.3): kayıt → kira → araç.
        var disHizmetToVeh = (await db.DisHizmetAlimlari.AsNoTracking()
                .Select(d => new { d.Id, d.RentalId }).ToListAsync(ct))
            .Select(d => new { d.Id, VehicleId = rentalToVeh.TryGetValue(d.RentalId, out var dv) ? (Guid?)dv : null })
            .Where(x => x.VehicleId != null)
            .ToDictionary(x => x.Id, x => x.VehicleId!.Value);

        var gelirByVeh = new Dictionary<Guid, decimal>();
        foreach (var e in gelirRaw)
        {
            // Fatura/FaturaIade→kira→araç (fark faturası dahil), AracSatis→satış→araç, Ceza→ceza→araç
            // (RentalId fallback'li), ServisYansitma→servis→araç. HGS (plaka-bazlı, kalıcı VehicleId yok) ve
            // manuel/kaynaksız gelir → (Atanmamış). (roadmap B2 adversarial + araç-karne atıf düzeltmesi.)
            var veh = Guid.Empty;
            switch (e.SourceType)
            {
                case "Fatura" or "FaturaIade" when RentalOf(e.SourceId) is Guid rid && rentalToVeh.TryGetValue(rid, out var vid):
                    veh = vid; break;
                case "AracSatis" when saleToVeh.TryGetValue(e.SourceId, out var sv):
                    veh = sv; break;
                case "Ceza" when cezaToVeh.TryGetValue(e.SourceId, out var cv):
                    veh = cv; break;
                case "ServisYansitma" when servisToVeh.TryGetValue(e.SourceId, out var srv):
                    veh = srv; break;
                case "DepozitoIrat" when iratToVeh.TryGetValue(e.SourceId, out var irv):
                    veh = irv; break;
                case "DisHizmet" when disHizmetToVeh.TryGetValue(e.SourceId, out var dhv): // FAZ 4.3
                    veh = dhv; break;
            }
            // İade Borç Gelir → negatif (kârı azaltır); normal Alacak Gelir → pozitif.
            var signed = (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R;
            gelirByVeh[veh] = gelirByVeh.GetValueOrDefault(veh) + signed;
        }

        var vehIds = giderByVeh.Keys.Concat(gelirByVeh.Keys).Where(k => k != Guid.Empty).Distinct().ToList();
        var dims = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Plaka, v.Sube, v.Grup, v.Segment }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => (v.Plaka, v.Sube, v.Grup, v.Segment));

        var rows = new List<KarlilikSatirDto>();
        foreach (var key in giderByVeh.Keys.Concat(gelirByVeh.Keys).Distinct())
        {
            var gelir = gelirByVeh.GetValueOrDefault(key);
            var gider = giderByVeh.GetValueOrDefault(key);
            if (key == Guid.Empty)
                rows.Add(new KarlilikSatirDto(null, "(Atanmamış)", null, null, null, gelir, gider, gelir - gider));
            else
            {
                var d = dims.TryGetValue(key, out var x) ? x : ("(bilinmeyen araç)", (string?)null, (string?)null, (string?)null);
                rows.Add(new KarlilikSatirDto(key, d.Item1, d.Item2, d.Item3, d.Item4, gelir, gider, gelir - gider));
            }
        }
        return rows;
    }

    public async Task<AracKarneRawDto> GetAracKarneRawAsync(
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
        var giderRawTum = (await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gider && e.AccountRef == vehicleId)
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct))
            // SIGNED base (FAZ 4.3): ters kayıt (Alacak Gider) negatif — iptal karneden de netleşir.
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId,
                A = (e.Direction == LedgerDirection.Debit ? 1m : -1m) * e.A, R = e.R })
            .ToList();
        var giderRaw = giderRawTum
            .Where(e => (from is not { } gf || e.EntryDateUtc >= gf) && (to is not { } gt || e.EntryDateUtc <= gt))
            .ToList();

        var policeler = await db.InsurancePolicies.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId).ToListAsync(ct);
        var sigortaTip = policeler.ToDictionary(x => x.Id, x => x.Tip);
        var giderKayitlari = await db.Expenses.AsNoTracking()
            .Where(x => x.VehicleId == vehicleId).ToListAsync(ct);
        var giderTip = giderKayitlari.ToDictionary(x => x.Id, x => x.Tip);

        string GiderKategori(string sourceType, Guid sourceId) => sourceType switch
        {
            "MtvOdeme" => "MTV",
            "DisHizmet" => "Dış Hizmet",
            "MuayeneOdeme" => "Muayene",
            "SigortaOdeme" => sigortaTip.TryGetValue(sourceId, out var t) && t == InsuranceType.Kasko
                ? "Kasko" : "Sigorta (Trafik)",
            "Gider" => giderTip.TryGetValue(sourceId, out var g) ? g switch
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
        var giderler = giderRaw
            .Select(e => new AracLedgerGiderRow(
                e.EntryDateUtc.UtcDateTime.Year, GiderKategori(e.SourceType, e.SourceId), e.A * e.R))
            .ToList();

        // ---- GELİR (defterden): GetKarlilikRowsAsync atıf kurallarının araç-scope'lu birebir kopyası
        // (parite testi kilitler). Kaynak id-kümeleri bu araca göre kurulur; atanamayan gelir karnede YOK.
        var kiralar = await db.Rentals.AsNoTracking().Where(r => r.VehicleId == vehicleId)
            .Select(r => new { r.Id, r.SozlesmeNo, r.BasTar, r.BitTar, r.GercekDonusTar, r.Durum, r.GenelToplam, r.CikisKm, r.DonusKm })
            .ToListAsync(ct);
        var rentalIds = kiralar.Select(r => r.Id).ToList();

        // Fatura bağı: RentalId (base) veya KaynakKiraId (fark) bu aracın kirasına işaret eder;
        // iade faturaları KaynakFaturaId ile bu kümeye iki-hop bağlanır.
        var dogrudanInvRows = await db.Invoices.AsNoTracking()
            .Where(i => (i.RentalId != null && rentalIds.Contains(i.RentalId.Value))
                     || (i.KaynakKiraId != null && rentalIds.Contains(i.KaynakKiraId.Value)))
            .Select(i => new { i.Id, i.RentalId, i.KaynakKiraId }).ToListAsync(ct);
        var dogrudanInv = dogrudanInvRows.Select(i => i.Id).ToList();
        var iadeInv = await db.Invoices.AsNoTracking()
            .Where(i => i.KaynakFaturaId != null && dogrudanInv.Contains(i.KaynakFaturaId.Value))
            .Select(i => i.Id).ToListAsync(ct);
        var invIds = dogrudanInv.Concat(iadeInv).ToHashSet();
        // Kira olayı DeftereYansir sinyali: kiranın parası deftere FATURA ile girer (iptal edilse bile
        // kesilmiş fatura defterde kalır — immutable). "İptal değil" durumuna değil buna bakılır (adversarial F3).
        var faturaliKiralar = dogrudanInvRows
            .Select(i => i.RentalId ?? i.KaynakKiraId!.Value).ToHashSet();

        var satislar = await db.VehicleSales.AsNoTracking().Where(s => s.VehicleId == vehicleId)
            .Select(s => new { s.Id, s.No, s.Tarih, s.GenelToplam, s.Durum }).ToListAsync(ct);
        var saleIds = satislar.Select(s => s.Id).ToHashSet();

        // Ceza: VehicleId öncelikli (Karlilik ile aynı) — VehicleId BAŞKA araca işaret ediyorsa buraya sayılmaz;
        // VehicleId=null + RentalId bu aracın kirası → fallback.
        var cezaIds = (await db.Penalties.AsNoTracking()
                .Where(p => p.VehicleId == vehicleId
                         || (p.VehicleId == null && p.RentalId != null && rentalIds.Contains(p.RentalId.Value)))
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var servisKayitlari = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId).ToListAsync(ct);
        var servisIds = servisKayitlari.Select(s => s.Id).ToHashSet();

        // Depozito iratları (FAZ 1.2): bu aracın kiralarına bağlı olanlar.
        var iratlar = await db.DepozitoIratlar.AsNoTracking()
            .Where(d => d.RentalId != null && rentalIds.Contains(d.RentalId.Value)).ToListAsync(ct);
        var iratIds = iratlar.Select(d => d.Id).ToHashSet();

        // Dış hizmet alımları (FAZ 4.3): komisyon geliri bu aracın kiralarına bağlı kayıtlardan.
        var disHizmetIds = (await db.DisHizmetAlimlari.AsNoTracking()
            .Where(d => rentalIds.Contains(d.RentalId)).Select(d => d.Id).ToListAsync(ct)).ToHashSet();

        var gelirRawTum = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gelir)
            .Select(e => new { e.EntryDateUtc, e.SourceType, e.SourceId, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct);
        var gelirRaw = gelirRawTum
            .Where(e => (from is not { } ef || e.EntryDateUtc >= ef) && (to is not { } et || e.EntryDateUtc <= et))
            .ToList();

        string? GelirKaynak(string st, Guid sid) => st switch
        {
            "Fatura" or "FaturaIade" when invIds.Contains(sid) => "Kira/Fatura",
            "AracSatis" when saleIds.Contains(sid) => "Araç Satışı",
            "Ceza" when cezaIds.Contains(sid) => "Ceza Yansıtma",
            "ServisYansitma" when servisIds.Contains(sid) => "Servis Yansıtma",
            "DepozitoIrat" when iratIds.Contains(sid) => "Depozito İradı",
            "DisHizmet" when disHizmetIds.Contains(sid) => "Dış Hizmet Komisyonu", // FAZ 4.3
            _ => null // başka araca/kaynağa ait ya da atanamayan → karnede yok
        };
        var gelirler = gelirRaw
            .Select(e => new { e, K = GelirKaynak(e.SourceType, e.SourceId) })
            .Where(x => x.K != null)
            .Select(x => new AracLedgerGelirRow(
                x.e.EntryDateUtc.UtcDateTime.Year, x.K!,
                (x.e.Direction == LedgerDirection.Credit ? 1m : -1m) * x.e.A * x.e.R))
            .ToList();
        // Ömür-boyu (pencereden bağımsız) toplamlar — KPI/amortisman paydaları bunlardan.
        var omurGider = giderRawTum.Sum(e => e.A * e.R);
        // FAZ 2.2 Tut/Sat hamı: son-12-ay / önceki-12-ay gider (defter) — now-göreli pencereler.
        var simdiUtc = DateTimeOffset.UtcNow;
        var son12Bas = simdiUtc.AddMonths(-12);
        var onceki12Bas = simdiUtc.AddMonths(-24);
        var gider12 = giderRawTum.Where(e => e.EntryDateUtc >= son12Bas).Sum(e => e.A * e.R);
        var giderOnceki12 = giderRawTum
            .Where(e => e.EntryDateUtc >= onceki12Bas && e.EntryDateUtc < son12Bas).Sum(e => e.A * e.R);
        var omurGelir = gelirRawTum
            .Where(e => GelirKaynak(e.SourceType, e.SourceId) != null)
            .Sum(e => (e.Direction == LedgerDirection.Credit ? 1m : -1m) * e.A * e.R);

        // ---- OLAYLAR ("neyi ne zaman") — kaynak varlıktan, BİLGİ amaçlı (tutar brüt/native; P&L'e toplanmaz).
        var mtvler = await db.MtvRecords.AsNoTracking().Where(m => m.VehicleId == vehicleId).ToListAsync(ct);
        var krediler = await db.AracKredileri.AsNoTracking().Where(k => k.VehicleId == vehicleId).ToListAsync(ct);
        var muayeneler = await db.InspectionRecords.AsNoTracking().Where(i => i.VehicleId == vehicleId).ToListAsync(ct);

        var olaylar = new List<AracOlayRow>();
        foreach (var p in policeler)
            olaylar.Add(new AracOlayRow(p.Baslangic,
                p.Tip == InsuranceType.Kasko ? "Kasko" : "Trafik Sigortası",
                $"Poliçe {p.PoliceNo ?? "-"} ({p.Firma ?? "-"}) — bitiş {p.Bitis:dd.MM.yyyy}" + (p.Odendi ? "" : " — ÖDENMEDİ"),
                p.Prim + p.ZeyilPrim, p.Odendi));
        foreach (var m in mtvler)
            olaylar.Add(new AracOlayRow(m.Vade, "MTV",
                $"Dönem {m.Donem}" + (m.Odendi ? "" : " — ÖDENMEDİ"), m.Tutar, m.Odendi));
        foreach (var i in muayeneler)
            olaylar.Add(new AracOlayRow(i.MuayeneTarihi, "Muayene",
                $"Geçerlilik {i.Bitis:dd.MM.yyyy}" + (i.Odendi ? "" : " — ÖDENMEDİ"), i.Ucret + i.Ceza, i.Odendi));
        foreach (var s in servisKayitlari)
            olaylar.Add(new AracOlayRow(s.GirisTarihi, $"Servis ({s.Tip})",
                $"{s.No} — km {s.GirisKm}→{(s.CikisKm?.ToString() ?? "-")} ({s.Durum})"
                + (s.Yansitildi ? $" — rücu {s.YansitilanTutar:N2}" : ""),
                s.ToplamIscilik, false)); // servis maliyeti deftere yazılmaz (mali belge değil)
        foreach (var kr in krediler)
            olaylar.Add(new AracOlayRow(kr.BaslangicTarihi, "Kredi",
                $"{kr.No} ({kr.BankaAdi}) — {kr.OdenenTaksit}/{kr.TaksitSayisi} taksit ({kr.Durum})",
                kr.KrediTutari, false)); // anapara defter dışı; taksit ÖDEMELERİ Gider(Finansman) olarak düşer
        foreach (var d in iratlar)
            olaylar.Add(new AracOlayRow(d.Tarih, "Depozito İradı",
                d.Aciklama ?? "İade edilmeyen depozito gelir yazıldı", d.Tutar, true));
        foreach (var x in giderKayitlari)
            olaylar.Add(new AracOlayRow(x.Tarih, $"Gider ({x.Tip})",
                $"{x.No}{(string.IsNullOrWhiteSpace(x.Aciklama) ? "" : " — " + x.Aciklama)}", x.GenelToplam, true));
        foreach (var s in satislar)
            olaylar.Add(new AracOlayRow(s.Tarih, "Satış",
                s.No + (s.Durum == SatisDurum.Iptal ? " — İPTAL" : ""), s.GenelToplam,
                s.Durum == SatisDurum.Tamamlandi));
        foreach (var r in kiralar)
            olaylar.Add(new AracOlayRow(r.BasTar, "Kira",
                $"{r.SozlesmeNo} — {r.BasTar:dd.MM.yyyy} → {(r.GercekDonusTar ?? r.BitTar):dd.MM.yyyy} ({r.Durum})"
                + (faturaliKiralar.Contains(r.Id) ? "" : " — faturalanmamış"),
                r.GenelToplam, faturaliKiralar.Contains(r.Id)));
        if (from is { } of) olaylar.RemoveAll(o => o.Tarih < of);
        if (to is { } ot) olaylar.RemoveAll(o => o.Tarih > ot);
        olaylar = olaylar.OrderByDescending(o => o.Tarih).ToList();

        // ---- KPI hamı: kira aralıkları (İptal hariç; efektif bitiş = GercekDonusTar ?? BitTar),
        // servis aralıkları (İptal hariç), katedilen km (çıkış+dönüş dolu kiralar).
        var aktifKiralar = kiralar.Where(r => r.Durum != RentalStatus.Iptal).ToList();
        var kiraAraliklari = aktifKiralar
            .Select(r => new DolulukKiraRowDto(r.BasTar, r.GercekDonusTar ?? r.BitTar)).ToList();
        var servisAraliklari = servisKayitlari.Where(s => s.Durum != ServisDurum.Iptal)
            .Select(s => new AracServisGunRow(s.GirisTarihi, s.CikisTarihi)).ToList();
        var katedilenKm = aktifKiralar.Where(r => r.CikisKm != null && r.DonusKm != null)
            .Sum(r => r.DonusKm!.Value - r.CikisKm!.Value);
        // FAZ 2.2: km pencereleri — efektif bitişi pencerede olan kiraların km'si.
        int KmPencere(DateTimeOffset bas, DateTimeOffset bit) => aktifKiralar
            .Where(r => r.CikisKm != null && r.DonusKm != null)
            .Where(r => (r.GercekDonusTar ?? r.BitTar) >= bas && (r.GercekDonusTar ?? r.BitTar) < bit)
            .Sum(r => r.DonusKm!.Value - r.CikisKm!.Value);
        var km12 = KmPencere(son12Bas, simdiUtc.AddDays(1));
        var kmOnceki12 = KmPencere(onceki12Bas, son12Bas);

        // FAZ 2.2 sınıf (Grup) ortalaması: grup araçlarının son-12-ay gider ÷ IkinciElDeger oranlarının
        // ortalaması (IkinciEl>0 olanlar; kendisi dahil). Grup yoksa null.
        decimal? grupOrt = null;
        if (!string.IsNullOrWhiteSpace(vehicle.Grup))
        {
            var grupAraclar = await db.Vehicles.AsNoTracking()
                .Where(v => v.Grup == vehicle.Grup && v.IkinciElDeger > 0)
                .Select(v => new { v.Id, v.IkinciElDeger }).ToListAsync(ct);
            if (grupAraclar.Count > 0)
            {
                var ids = grupAraclar.Select(g => (Guid?)g.Id).ToList();
                var grupGider = (await db.AccountLedgerEntries.AsNoTracking()
                        .Where(e => e.AccountType == LedgerAccountType.Gider
                                    && e.AccountRef != null && ids.Contains(e.AccountRef)
                                    && e.EntryDateUtc >= son12Bas)
                        .Select(e => new { e.AccountRef, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
                        .ToListAsync(ct))
                    .GroupBy(x => x.AccountRef!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => (x.Direction == LedgerDirection.Debit ? 1m : -1m) * x.A * x.R));
                var oranlar = grupAraclar
                    .Select(g => grupGider.GetValueOrDefault(g.Id) / g.IkinciElDeger!.Value).ToList();
                grupOrt = oranlar.Count > 0 ? oranlar.Average() : null;
            }
        }
        var sonSatis = satislar.Where(s => s.Durum == SatisDurum.Tamamlandi)
            .Select(s => (DateTimeOffset?)s.Tarih).DefaultIfEmpty(null).Max();

        // FAZ 2.5: km zaman serisi (Tarih artan — servis dönem-km farkını bu sıradan alır).
        var kmLoglari = await db.KmLoglari.AsNoTracking()
            .Where(k => k.VehicleId == vehicleId)
            .OrderBy(k => k.Tarih).ThenBy(k => k.Km)
            .Select(k => new AracKmLogRow(k.Tarih, k.Km))
            .ToListAsync(ct);

        return new AracKarneRawDto(vehicle, gelirler, giderler, olaylar,
            kiraAraliklari, servisAraliklari, aktifKiralar.Count, katedilenKm, sonSatis,
            omurGelir, omurGider,
            new FiloTutSatRow(vehicleId, gider12, giderOnceki12, km12, kmOnceki12), grupOrt, kmLoglari);
    }

    public async Task<FiloAnalizRawDto> GetFiloAnalizRawAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        // P&L: mevcut Karlilik atfı yeniden kullanılır (tek doğruluk kaynağı). Pencere verilmişse KPI
        // payları için ömür-boyu set AYRICA çekilir (karışık-payda dersi); verilmemişse aynı liste.
        var pencere = await GetKarlilikRowsAsync(from, to, ct);
        var omur = from is null && to is null ? pencere : await GetKarlilikRowsAsync(null, null, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var sonSatis = (await db.VehicleSales.AsNoTracking()
                .Where(s => s.Durum == SatisDurum.Tamamlandi)
                .GroupBy(s => s.VehicleId)
                .Select(g => new { VehicleId = g.Key, Tarih = g.Max(x => x.Tarih) })
                .ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x.Tarih);

        var araclar = (await db.Vehicles.AsNoTracking()
                .Select(v => new { v.Id, v.Plaka, v.Grup, v.Segment, v.Sube, v.AlimBedeli, v.AlimTarihi, v.FiloGirisTarih, v.FiloCikisTarih, v.Durum, v.IkinciElDeger })
                .ToListAsync(ct))
            .Select(v => new FiloAracRow(v.Id, v.Plaka, v.Grup, v.Segment, v.Sube,
                v.AlimBedeli, v.AlimTarihi, v.FiloGirisTarih, v.FiloCikisTarih,
                v.Durum, sonSatis.TryGetValue(v.Id, out var t) ? t : null, v.IkinciElDeger))
            .ToList();

        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentalStatus.Iptal)
            .Select(r => new FiloKiraRow(r.VehicleId, r.BasTar, r.GercekDonusTar ?? r.BitTar, r.CikisKm, r.DonusKm))
            .ToListAsync(ct);

        // FAZ 2.2 Tut/Sat hamı — FAZ 6.2: OrtakSorgular'a taşındı (FiloBildirimUretici ile TEK kaynak;
        // pencereleme iki yerde yazılıp sessizce ayrışmasın). Karne kartı == filo kolonu parite testi kilit.
        var tutSatHam = (await OrtakSorgular.TutSatHamAsync(db, DateTimeOffset.UtcNow, ct)).Ham;

        return new FiloAnalizRawDto(pencere, omur, araclar, kiralar, tutSatHam);
    }
}
