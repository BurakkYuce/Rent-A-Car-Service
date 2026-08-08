using Microsoft.EntityFrameworkCore;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Birden çok tüketicisi olan sorguların TEK doğruluk kaynağı (denetim O12a/O12b: vade-kaynak birleşimi ve
/// günlük tahsilat/filo metrikleri iki yerde bağımsız yazılmıştı; parite satır-numaralı yorumlarla korunuyordu —
/// tanım değişince pano/rapor ile bildirim/WhatsApp özeti sessizce ayrışırdı). db tenant-kapsamlı verilir
/// (factory VEYA raw+GUC) — helper kapsam eklemez.
/// </summary>
public static class OrtakSorgular
{
    /// <summary>Kira fatura fark-state'i (iade-netli faturalanan brüt + fark sayısı) — InvoiceRepository
    /// (manuel B2 yolu) ve DonemFaturaUretici (job B4) AYNI sorgudan geçer (tek kopya).</summary>
    public static async Task<(decimal FaturalananBrut, int FarkSayisi)> FarkStateAsync(
        AppDbContext db, Guid rentalId, CancellationToken ct = default)
    {
        var kiraFaturalari = await db.Invoices.AsNoTracking()
            .Where(i => (i.RentalId == rentalId || i.KaynakKiraId == rentalId)
                && i.Durum != Domain.Enums.InvoiceStatus.Iptal && !i.IadeMi)
            .Select(i => new { i.Id, i.GenelToplam })
            .ToListAsync(ct);
        var gross = kiraFaturalari.Sum(x => x.GenelToplam);
        var iadeGross = 0m;
        if (gross != 0m)
        {
            var ids = kiraFaturalari.Select(x => x.Id).ToList();
            iadeGross = await db.Invoices.AsNoTracking()
                .Where(i => i.IadeMi && i.KaynakFaturaId != null && ids.Contains(i.KaynakFaturaId.Value)
                    && i.Durum != Domain.Enums.InvoiceStatus.Iptal)
                .SumAsync(i => (decimal?)i.GenelToplam, ct) ?? 0m;
        }
        var farkSayisi = await db.Invoices.AsNoTracking()
            .CountAsync(i => i.KaynakKiraId == rentalId && i.Durum != Domain.Enums.InvoiceStatus.Iptal, ct);
        return (gross - iadeGross, farkSayisi);
    }

    /// <summary>Vade kaynakları birleşimi: sigorta (Kasko/Trafik) + ödenmemiş MTV + muayene.
    /// Vade panosu (RegulationRepository) ve bildirim job'ı (VadeBildirimUretici) AYNI listeyi kullanır.</summary>
    public static async Task<IReadOnlyList<VadeSource>> VadeKaynaklariAsync(AppDbContext db, CancellationToken ct = default)
    {
        var insurance = await db.InsurancePolicies.AsNoTracking()
            .Select(x => new VadeSource(x.VehicleId, x.Tip == InsuranceType.Kasko ? "Kasko" : "Trafik", x.Bitis))
            .ToListAsync(ct);
        var mtv = await db.MtvRecords.AsNoTracking()
            .Where(x => !x.Odendi)
            .Select(x => new VadeSource(x.VehicleId, "MTV", x.Vade))
            .ToListAsync(ct);
        var inspection = await db.InspectionRecords.AsNoTracking()
            .Select(x => new VadeSource(x.VehicleId, "Muayene", x.Bitis))
            .ToListAsync(ct);
        return [.. insurance, .. mtv, .. inspection];
    }

    /// <summary>Pencere içi tahsilat (Tip=Tahsilat, ters-kayıt hariç) — TL-BAZ Σ Amount×Rate (çok-döviz) + adet.
    /// Günlük faaliyet raporu (ReportRepository) ve WhatsApp operasyon özeti AYNI tanımı kullanır.
    /// Pencere: [from, toExclusive).</summary>
    public static async Task<(int Adet, decimal TutarTl)> TahsilatTlAsync(
        AppDbContext db, DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken ct = default)
    {
        var rows = await db.CashTransactions.AsNoTracking()
            .Where(c => c.Tip == CashTransactionType.Tahsilat && !c.TersKayitMi && c.Tarih >= from && c.Tarih < toExclusive)
            .Select(c => new { c.Amount.Amount, c.Amount.Rate })
            .ToListAsync(ct);
        return (rows.Count, rows.Sum(t => t.Amount * t.Rate));
    }

    /// <summary>Filo durum listesi (Durum sayımı tüketicide). Filo doluluk raporu ve WhatsApp özeti aynı kaynağı kullanır.</summary>
    public static Task<List<VehicleStatus>> VehicleDurumlariAsync(AppDbContext db, CancellationToken ct = default)
        => db.Vehicles.AsNoTracking().Select(v => v.Durum).ToListAsync(ct);

    /// <summary>FAZ 6.2 — Tut/Sat hamı: FiloAnaliz raw'ı (ReportRepository) ve FiloBildirimUretici AYNI
    /// sorgudan geçer (pencereleme iki yerde yazılıp sessizce ayrışmasın). Pencereler: son-12 =
    /// [now−12ay, ∞), önceki-12 = [now−24ay, now−12ay). Gider = defter (AccountType=Gider,
    /// AccountRef=araç, yön-imzalı Σ base); km = İptal-dışı, çıkış+dönüş km'li kiralar
    /// (efektif bitiş GercekDonus ?? BitTar).</summary>
    public static async Task<TutSatHamPaket> TutSatHamAsync(
        AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var son12Bas = now.AddMonths(-12);
        var onceki12Bas = now.AddMonths(-24);

        var araclar = (await db.Vehicles.AsNoTracking()
                .Select(v => new { v.Id, v.Plaka, v.Grup, v.IkinciElDeger }).ToListAsync(ct))
            .Select(v => new TutSatAracRow(v.Id, v.Plaka, v.Grup, v.IkinciElDeger)).ToList();

        var giderRaw = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Gider
                        && e.AccountRef != null && e.EntryDateUtc >= onceki12Bas)
            .Select(e => new { e.AccountRef, e.EntryDateUtc, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync(ct);
        var gider = giderRaw
            .GroupBy(x => x.AccountRef!.Value)
            .ToDictionary(g => g.Key, g => (
                G12: g.Where(x => x.EntryDateUtc >= son12Bas)
                    .Sum(x => (x.Direction == RentACar.Domain.Entities.LedgerDirection.Debit ? 1m : -1m) * x.A * x.R),
                GOnceki: g.Where(x => x.EntryDateUtc < son12Bas)
                    .Sum(x => (x.Direction == RentACar.Domain.Entities.LedgerDirection.Debit ? 1m : -1m) * x.A * x.R)));

        var kmByVeh = (await db.Rentals.AsNoTracking()
                .Where(r => r.Durum != RentalStatus.Iptal && r.CikisKm != null && r.DonusKm != null)
                .Select(r => new { r.VehicleId, Bit = r.GercekDonusTar ?? r.BitTar, r.CikisKm, r.DonusKm })
                .ToListAsync(ct))
            .GroupBy(k => k.VehicleId)
            .ToDictionary(g => g.Key, g => (
                Km12: g.Where(k => k.Bit >= son12Bas).Sum(k => k.DonusKm!.Value - k.CikisKm!.Value),
                KmOnceki: g.Where(k => k.Bit >= onceki12Bas && k.Bit < son12Bas).Sum(k => k.DonusKm!.Value - k.CikisKm!.Value)));

        var ham = araclar.Select(a =>
        {
            var g = gider.GetValueOrDefault(a.Id);
            var km = kmByVeh.GetValueOrDefault(a.Id);
            return new FiloTutSatRow(a.Id, g.G12, g.GOnceki, km.Km12, km.KmOnceki);
        }).ToList();
        return new TutSatHamPaket(ham, araclar);
    }

    /// <summary>FAZ 6.2 — periyodik bakım kalan-km satırları: rapor sayfası (ReportRepository) ve
    /// FiloBildirimUretici AYNI birleşimden geçer. İKİ kaynaktan MIN(KalanKm) (çift satır yok):
    /// (1) servis kaydındaki elle hedef (MAX SonrakiBakimKm); (2) Vehicle.SonBakimKm + ServisTanim.BakimKm
    /// (AracTipi↔Tip case-insensitive; çok tanımda EN KÜÇÜK aralık = en erken uyarı). Kaynağı olmayan
    /// araç "tanım yok" satırı (hedef null) — sessiz gizleme yok.</summary>
    /// <param name="filtre">
    /// FAZ-76 — OPSİYONEL rapor filtresi. null (varsayılan) = ESKİ DAVRANIŞ birebir; bu yüzden
    /// <c>FiloBildirimUretici</c>'nin parametresiz çağrısı DEĞİŞMEDEN çalışır. Filtre yalnız
    /// DARALTIR; bildirim üreticisi hiçbir zaman filtre geçmez → bildirim kapsamı aynı kalır.
    /// </param>
    public static async Task<IReadOnlyList<PeriyodikServisRow>> PeriyodikServisAsync(
        AppDbContext db, CancellationToken ct = default,
        RentACar.Application.Reporting.PeriyodikServisFilter? filtre = null)
    {
        var bakim = (await db.ServiceRecords.AsNoTracking()
            .Where(r => r.SonrakiBakimKm != null)
            .GroupBy(r => r.VehicleId)
            .Select(g => new { VehicleId = g.Key, Sonraki = g.Max(r => r.SonrakiBakimKm!.Value) })
            .ToListAsync(ct)).ToDictionary(b => b.VehicleId, b => b.Sonraki);

        var tanimlar = (await db.ServisTanimlari.AsNoTracking()
                .Where(t => t.Aktif && t.BakimKm > 0).Select(t => new { t.AracTipi, t.BakimKm }).ToListAsync(ct))
            .GroupBy(t => t.AracTipi.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(t => t.BakimKm), StringComparer.OrdinalIgnoreCase);

        var araclar = await db.Vehicles.AsNoTracking()
            .Select(v => new { v.Id, v.Plaka, v.Km, v.Tip, v.SonBakimKm,
                v.Marka, v.ModelYili, v.Yakit, v.Vites, v.Sube, Aktif = v.Durum != VehicleStatus.Pasif })
            .ToListAsync(ct);

        // FAZ-76 rapor kolonu: aracın SON servis kaydı (iptal hariç) — "ne zaman/kaç km'de
        // bakıma girdi" bilgisi. Bildirim üreticisi bu kolonu kullanmaz.
        var sonServis = (await db.ServiceRecords.AsNoTracking()
                .Where(r => r.Durum != ServisDurum.Iptal)
                .Select(r => new { r.VehicleId, r.GirisTarihi, r.GirisKm })
                .ToListAsync(ct))
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.GirisTarihi).First());

        return araclar.Select(v =>
            {
                int? servisHedef = bakim.TryGetValue(v.Id, out var s) ? s : null;
                int? otoHedef = v.SonBakimKm is int son && v.Tip is { } tip
                    && tanimlar.TryGetValue(tip.Trim(), out var aralik) ? son + aralik : null;

                var (hedef, kaynak) = (servisHedef, otoHedef) switch
                {
                    (int sv, int ot) => sv - v.Km <= ot - v.Km ? (sv, "Servis") : (ot, "Tanım"),
                    (int sv, null) => (sv, "Servis"),
                    (null, int ot) => (ot, "Tanım"),
                    _ => ((int?)null, (string?)null)
                };
                var ss = sonServis.GetValueOrDefault(v.Id);
                return new PeriyodikServisRow(v.Id, v.Plaka, v.Km, hedef, hedef - v.Km, kaynak,
                    v.Marka, v.Tip, v.ModelYili, v.Yakit?.ToString(), v.Vites?.ToString(), v.Sube,
                    ss?.GirisTarihi, ss?.GirisKm, v.Aktif);
            })
            .Where(r => Uygun(r, filtre))
            .OrderBy(r => r.KalanKm ?? int.MaxValue)
            .ToList();
    }

    /// <summary>FAZ-76 filtre uygunluğu. Filtre null ise HER satır geçer (eski davranış).</summary>
    private static bool Uygun(PeriyodikServisRow r, RentACar.Application.Reporting.PeriyodikServisFilter? f)
    {
        if (f is null) return true;
        if (f.Aktif is bool a && r.Aktif != a) return false;
        if (!string.IsNullOrWhiteSpace(f.Sube)
            && !RentACar.Application.Common.TurkishText.EqualsIgnoreTurkishCase(r.Sube, f.Sube)) return false;
        if (!string.IsNullOrWhiteSpace(f.Plaka))
        {
            // Plaka DB'de normalize saklanıyor → arama terimi de normalize (FAZ-63 dersi).
            var p = new string(f.Plaka.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            if (p.Length > 0 && !r.Plaka.Contains(p, StringComparison.OrdinalIgnoreCase)) return false;
        }
        // Uyarı eşiği: hedefi OLMAYAN araç ("tanım yok") elenmez — eksik tanım da bir uyarıdır.
        if (f.UyariEsigi is int esik && r.KalanKm is int kalan && kalan > esik) return false;
        return true;
    }
}
