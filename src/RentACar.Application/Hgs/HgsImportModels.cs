using System.Globalization;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

/// <summary>
/// Dosyadan okunmuş HAM satır (sütun eşlemesi Web katmanında yapılır; değerler metin). <see cref="SatirNo"/> dosya
/// satırıdır (başlık 1. satır → veri 2'den başlar).
/// </summary>
public sealed record HgsRawRow(int SatirNo)
{
    public string? Plaka { get; init; }
    public string? EtiketNo { get; init; }
    public string? AracSinifi { get; init; }
    public string? GirisIstasyonu { get; init; }
    public string? CikisIstasyonu { get; init; }
    public string? GirisZamani { get; init; }
    public string? GecisZamani { get; init; }
    public string? GecisSaati { get; init; }
    public string? Tutar { get; init; }
}

/// <summary>Önizleme/içe aktarma satır durumu.</summary>
public enum HgsImportRowStatus { Eslesti, KiraDisi, Belirsiz, Mukerrer, Hatali }

/// <summary>Önizleme/içe aktarma satırı (kullanıcıya gösterilen karar).</summary>
public sealed record HgsImportRow(
    int SatirNo, string? Plaka, DateTimeOffset? GecisZamani, string? GirisIstasyonu, string? CikisIstasyonu,
    decimal? Tutar, HgsImportRowStatus Durum, Guid? RentalId, string? SozlesmeNo, bool AracBulunamadi, string? Hata);

/// <summary>
/// Önizleme (yazmadan) ya da içe aktarma sonucu. Sayılar dosyanın TAMAMI içindir; <see cref="Satirlar"/> en çok
/// <see cref="HgsCrossingService.MaxPreviewRows"/> satırdır (hatalılar önce).
/// </summary>
public sealed record HgsImportOutcome(
    int Toplam, int Eslesen, int KiraDisi, int Belirsiz, int AracBulunamadi, int Mukerrer, int Hatali, int Eklenen,
    decimal EslesenTutar, decimal KiraDisiTutar, IReadOnlyList<string> Uyarilar, IReadOnlyList<HgsImportRow> Satirlar,
    Guid? IceAktarimId);

/// <summary>Liste süzgeci (hepsi isteğe bağlı). <see cref="Bit"/> dahil değil (üst sınır, UTC).</summary>
public sealed record HgsCrossingFilter
{
    public string? Plaka { get; init; }
    public DateTimeOffset? Bas { get; init; }
    public DateTimeOffset? Bit { get; init; }
    public HgsCrossingStatus? Durum { get; init; }
    public Guid? RentalId { get; init; }
    public Guid? IceAktarimId { get; init; }
}

/// <summary>Yansıtma sonucu. <see cref="Uygunsuz"/>: kira dışı / belirsiz / kirası artık uymayan (yeniden eşleştirin).</summary>
public sealed record HgsReflectResult(
    int Yansitilan, int ZatenYansitilmis, int Uygunsuz, decimal ToplamGecis, decimal YansitilanTutar);

/// <summary>Filodaki araç (eşleştirme için yalnız kimlik + plaka).</summary>
public sealed record HgsVehicleRef(Guid Id, string Plaka);

/// <summary>Doğal anahtar + plaka→araç→kira çözümü (içe aktarma ve yeniden eşleştirme AYNI kuralı kullanır).</summary>
public static class HgsResolution
{
    /// <summary>
    /// Bellek içi doğal anahtar (DB'deki unique index'in kolonlarıyla birebir): plaka | UTC an (saniye) | istasyon |
    /// tutar (2 hane, Invariant). Aynı dosyadaki tekrarlar ve önizlemedeki "zaten var" bununla bulunur.
    /// </summary>
    public static string NaturalKey(string plate, DateTimeOffset atUtc, string stationKey, decimal amount)
        => string.Create(CultureInfo.InvariantCulture,
            $"{plate}|{atUtc.UtcDateTime:yyyyMMddHHmmss}|{stationKey}|{decimal.Round(amount, 2):0.00}");

    public static string NaturalKey(HgsCrossing c) => NaturalKey(c.Plaka, c.GecisZamani, c.IstasyonAnahtari, c.Tutar);

    /// <summary>Plaka anahtarı → araç (aynı anahtara iki araç düşerse eşleştirme yapılmaz: null).</summary>
    public static Dictionary<string, Guid?> VehicleIndex(IEnumerable<HgsVehicleRef> vehicles)
    {
        var index = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        foreach (var v in vehicles)
        {
            var key = HgsValueParser.PlateMatchKey(v.Plaka);
            if (key.Length == 0) continue;
            index[key] = index.ContainsKey(key) ? null : v.Id;
        }
        return index;
    }

    /// <summary>Plaka + an → (araç, eşleşme). Araç yoksa ya da plaka iki araca düşüyorsa KiraDisi, araç null.</summary>
    public static (Guid? VehicleId, HgsMatch Match) Resolve(
        string plate, DateTimeOffset at, IReadOnlyDictionary<string, Guid?> vehicles,
        ILookup<Guid, HgsRentalWindow> windowsByVehicle)
    {
        if (!vehicles.TryGetValue(plate, out var vehicle) || vehicle is not { } id)
            return (null, new HgsMatch(HgsCrossingStatus.KiraDisi, null));
        return (id, HgsCrossingMatcher.Match(at, windowsByVehicle[id]));
    }
}
