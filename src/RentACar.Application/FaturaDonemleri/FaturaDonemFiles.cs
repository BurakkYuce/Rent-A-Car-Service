using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FaturaDonemleri;

public interface IFaturaDonemRepository
{
    Task<IReadOnlyList<FaturaDonemi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Planı TEK transaction'da yeniler: verilen rental'ın PLANLANDİ satırları silinir,
    /// <paramref name="yeniPlanlar"/> eklenir. Kesildi/Atlandi satırlara DOKUNULMAZ (çağıran onların
    /// sıralarını yeni listeden çıkarmıştır).</summary>
    Task ReplacePlannedAsync(Guid rentalId, IReadOnlyList<FaturaDonemi> yeniPlanlar, CancellationToken ct = default);
}

/// <summary>Dönem önizleme satırı (B1): plan satırı + pro-rata tahakkuk (salt hesap; B2 kesimde
/// cap/fark mekanizması ayrıca devreye girer).</summary>
public sealed record FaturaDonemOnizleme(
    int DonemSira, DateTimeOffset DonemBas, DateTimeOffset DonemBit,
    FaturaDonemDurum Durum, decimal Tahakkuk, Guid? InvoiceId, decimal? KesilenTutar);

/// <summary>
/// Periyodik faturalama dönem PLANI (FAZ 4.2-B1; parasız — deftere/faturaya dokunmaz).
/// UYGUNLUK: KiralamaTuru "Uzun Kiralama"/"Aylık" (gerçek değer listesi; "Uzun Dönem" diye değer
/// YOK) VEYA Gun >= 28. Dönem tarihleri BasTar'ın GÜN-OF-AY ÇIPASIYLA üretilir (her dönem
/// BasTar.AddMonths(i) — 31 Oca çıpası 28 Şub'a kırpılır ama Mart'ta 31'e döner); son dönem
/// BitTar'da biter. Tahakkuk = GÜN-BAZLI pro-rata (son dönem = KALAN-YÖNTEMİ: Tutar − Σ önceki →
/// yuvarlama kayması yapısal SIFIR; Σ tahakkuk == Tutar invaryantı). Yalnız PLANLANDİ satırlar
/// yeniden üretilir; Kesildi/Atlandi sıralar korunur (uzatma planı büyütür, kesilmişe dokunmaz).
/// </summary>
public sealed class FaturaDonemPlanService(
    IFaturaDonemRepository repository,
    Bookings.IBookingRepository bookings,
    ICurrentUser currentUser)
{
    public static bool UygunMu(RentalContract c) =>
        string.Equals(c.KiralamaTuru?.Trim(), "Uzun Kiralama", StringComparison.OrdinalIgnoreCase)
        || string.Equals(c.KiralamaTuru?.Trim(), "Aylık", StringComparison.OrdinalIgnoreCase)
        || c.Gun >= 28;

    /// <summary>Ay-çıpalı dönem aralıkları: [Bas+i ay, min(Bas+(i+1) ay, Bit)); Bit'e ulaşınca durur.</summary>
    public static IReadOnlyList<(DateTimeOffset Bas, DateTimeOffset Bit)> DonemAraliklari(
        DateTimeOffset bas, DateTimeOffset bit)
    {
        var donemler = new List<(DateTimeOffset, DateTimeOffset)>();
        for (var i = 0; ; i++)
        {
            var dBas = bas.AddMonths(i);          // origin'den AddMonths → gün-of-ay çıpası korunur
            if (dBas >= bit) break;
            var dBit = bas.AddMonths(i + 1);
            donemler.Add((dBas, dBit < bit ? dBit : bit));
            if (dBit >= bit) break;
        }
        return donemler;
    }

    /// <summary>GÜN-BAZLI pro-rata tahakkuk; SON dönem kalan-yöntemi (Σ == tutar, kuruş-birebir).</summary>
    public static IReadOnlyList<decimal> ProRataAccrual(decimal tutar, IReadOnlyList<int> donemGunleri)
    {
        var toplamGun = donemGunleri.Sum();
        if (toplamGun <= 0 || donemGunleri.Count == 0) return [];
        var sonuc = new decimal[donemGunleri.Count];
        decimal dagitilan = 0m;
        for (var i = 0; i < donemGunleri.Count - 1; i++)
        {
            sonuc[i] = Math.Round(tutar * donemGunleri[i] / toplamGun, 2, MidpointRounding.AwayFromZero);
            dagitilan += sonuc[i];
        }
        sonuc[^1] = tutar - dagitilan; // kalan-yöntemi
        return sonuc;
    }

    /// <summary>Planı kurar/yeniler (create + uzatma sonrası çağrılır). Uygun değilse mevcut Planlandi
    /// satırlarını temizler (Kesildi/Atlandi kalır). İDEMPOTENT.</summary>
    public async Task EnsurePlanAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null) return;

        var mevcut = await repository.ListForRentalAsync(rentalId, ct);
        var korunanSiralar = mevcut
            .Where(d => d.Durum != FaturaDonemDurum.Planlandi)
            .Select(d => d.DonemSira).ToHashSet();

        var yeniPlanlar = new List<FaturaDonemi>();
        if (UygunMu(c) && c.Durum != RentalStatus.Iptal)
        {
            var araliklar = DonemAraliklari(c.BasTar, c.BitTar);
            for (var i = 0; i < araliklar.Count; i++)
            {
                var sira = i + 1;
                if (korunanSiralar.Contains(sira)) continue; // Kesildi/Atlandi — dokunma
                yeniPlanlar.Add(new FaturaDonemi
                {
                    RentalId = rentalId, DonemSira = sira,
                    DonemBas = araliklar[i].Bas, DonemBit = araliklar[i].Bit
                });
            }
        }
        await repository.ReplacePlannedAsync(rentalId, yeniPlanlar, ct);
    }

    /// <summary>Dönem listesi + pro-rata tahakkuk önizlemesi (UI alt-sekmesi / job matematiğiyle ortak).</summary>
    public async Task<IReadOnlyList<FaturaDonemOnizleme>> PreviewAsync(Guid rentalId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var c = await bookings.FindRentalAsync(rentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        var satirlar = (await repository.ListForRentalAsync(rentalId, ct))
            .OrderBy(d => d.DonemSira).ToList();
        if (satirlar.Count == 0) return [];

        var gunler = satirlar
            .Select(d => Math.Max(1, (d.DonemBit.UtcDateTime.Date - d.DonemBas.UtcDateTime.Date).Days))
            .ToList();
        var tahakkuklar = ProRataAccrual(c.Tutar, gunler);
        return satirlar.Select((d, i) => new FaturaDonemOnizleme(
            d.DonemSira, d.DonemBas, d.DonemBit, d.Durum, tahakkuklar[i], d.InvoiceId, d.KesilenTutar)).ToList();
    }
}
