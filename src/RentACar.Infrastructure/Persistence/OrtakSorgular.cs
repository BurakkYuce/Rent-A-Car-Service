using Microsoft.EntityFrameworkCore;
using RentACar.Application.Regulation;
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
}
