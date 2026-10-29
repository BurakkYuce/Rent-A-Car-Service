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

/// <summary>Önizleme/içe aktarma satırı (kullanıcıya gösterilen karar). <paramref name="Hata"/> hata ya da gerekçe notu.</summary>
public sealed record HgsImportRow(
    int SatirNo, string? Plaka, DateTimeOffset? GecisZamani, string? GirisIstasyonu, string? CikisIstasyonu,
    decimal? Tutar, HgsImportRowStatus Durum, Guid? RentalId, string? SozlesmeNo, bool AracBulunamadi, string? Hata);

/// <summary>
/// Önizleme (yazmadan) ya da içe aktarma sonucu. Sayılar dosyanın TAMAMI içindir; <see cref="Satirlar"/> en çok
/// <see cref="HgsCrossingService.MaxPreviewRows"/> satırdır (hatalılar önce). <see cref="OlasiMukerrer"/>,
/// <see cref="Belirsiz"/>'in alt kümesidir (biçimi farklı dökümden gelen aynı geçiş şüphesi).
/// </summary>
public sealed record HgsImportOutcome(
    int Toplam, int Eslesen, int KiraDisi, int Belirsiz, int OlasiMukerrer, int AracBulunamadi, int Mukerrer, int Hatali,
    int Eklenen, decimal EslesenTutar, decimal KiraDisiTutar, IReadOnlyList<string> Uyarilar,
    IReadOnlyList<HgsImportRow> Satirlar, Guid? IceAktarimId);

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

/// <summary>Yansıtma sonucu. <see cref="Uygunsuz"/>: kira dışı / belirsiz / olası mükerrer / kirası artık uymayan.</summary>
public sealed record HgsReflectResult(
    int Yansitilan, int ZatenYansitilmis, int Uygunsuz, decimal ToplamGecis, decimal YansitilanTutar);

/// <summary>Filodaki araç (eşleştirme için yalnız kimlik + plaka).</summary>
public sealed record HgsVehicleRef(Guid Id, string Plaka);

/// <summary>Olası mükerrer kararı için geçişin özeti (DB'deki ya da yazılmak üzere olan).</summary>
public sealed record HgsCrossingRef(
    Guid Id, string Plate, DateTimeOffset At, decimal Amount, string StationKey, DateTimeOffset CreatedAtUtc,
    HgsCrossingStatus Status)
{
    public static HgsCrossingRef Of(HgsCrossing c)
        => new(c.Id, c.Plaka, c.GecisZamani, c.Tutar, c.IstasyonAnahtari, c.CreatedAtUtc, c.Durum);
}

/// <summary>Doğal anahtar + plaka→araç→kira çözümü + olası mükerrer kuralı (içe aktarma, yeniden eşleştirme ve
/// yansıtma AYNI kuralı kullanır).</summary>
public static class HgsResolution
{
    /// <summary>Olası mükerrer penceresi: aynı plaka + aynı tutar + |Δt| &lt; 60 sn (istasyondan bağımsız).</summary>
    public static readonly TimeSpan NearDuplicateWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Geniş olası mükerrer penceresi: aynı plaka + aynı tutar + AYNI (boş olmayan) istasyon anahtarı + |Δt| &lt; 6 sa
    /// (#391 R2 L1). Aynı geçiş iki dökümde farklı ZAMAN anlamıyla gelebilir: biri giriş, diğeri çıkış anı (ya da
    /// kullanıcının açıkça eşlediği banka işlem anı). Fark en çok bir otoyol yolculuğu kadardır; 6 saat, kapalı sistemde
    /// en uzun tek yolculuğu (Edirne–Ankara) kapsar. Bu pencere içinde aynı istasyondan aynı tutarla GERÇEK ikinci geçiş
    /// de Belirsiz kalır — bilinçli: tahminle iki kez borçlandırmak yerine incelemeye düşer.
    /// </summary>
    public static readonly TimeSpan SameStationWindow = TimeSpan.FromHours(6);

    /// <summary>Komşu arama/sorgu aralığı: iki pencerenin büyüğü.</summary>
    public static readonly TimeSpan LookAround = SameStationWindow;

    private static readonly string NoStationKey = HgsValueParser.StationKey(null, null);

    public const string NearDuplicateNote =
        "Olası mükerrer: aynı plaka ve tutarla 60 saniye içinde ya da aynı istasyonda 6 saat içinde başka bir geçiş var (farklı biçimli döküm); otomatik yansıtılmaz.";

    public const string UnknownPlateNote = "Plaka filoda bulunamadı (firma gideri).";

    /// <summary>
    /// Bellek içi doğal anahtar (DB'deki unique index'in kolonlarıyla birebir): plaka | UTC an (saniye) | istasyon |
    /// tutar (2 hane, Invariant). Aynı dosyadaki tekrarlar ve önizlemedeki "zaten var" bununla bulunur.
    /// </summary>
    public static string NaturalKey(string plate, DateTimeOffset atUtc, string stationKey, decimal amount)
        => string.Create(CultureInfo.InvariantCulture,
            $"{plate}|{atUtc.UtcDateTime:yyyyMMddHHmmss}|{stationKey}|{decimal.Round(amount, 2):0.00}");

    public static string NaturalKey(HgsCrossing c) => NaturalKey(c.Plaka, c.GecisZamani, c.IstasyonAnahtari, c.Tutar);

    /// <summary>
    /// Olası mükerrer (adversarial #391 M1): aynı fiziksel geçiş iki dökümde FARKLI yazılabilir — biri saniyeli
    /// (xlsx tarih-saat hücresi), diğeri saniyesiz (aynı dökümün "CSV olarak kaydet" çıktısı); biri giriş+çıkış
    /// istasyonlu, diğeri yalnız çıkışlı. Doğal anahtar (saniye + istasyon) bunları ayırır; bu kural yakalar.
    /// İstasyon anahtarı aynıysa pencere <see cref="SameStationWindow"/>'dur (farklı zaman anlamı, #391 R2 L1).
    /// </summary>
    public static bool AreNearDuplicates(HgsCrossingRef a, HgsCrossingRef b)
    {
        if (a.Id == b.Id || !string.Equals(a.Plate, b.Plate, StringComparison.Ordinal)
            || decimal.Round(a.Amount, 2) != decimal.Round(b.Amount, 2)) return false;
        var dt = (a.At - b.At).Duration();
        return dt < NearDuplicateWindow
               || (dt < SameStationWindow && a.StationKey != NoStationKey
                   && string.Equals(a.StationKey, b.StationKey, StringComparison.Ordinal));
    }

    /// <summary>Kümede "önce gelen" (asıl kabul edilen) satır: önce kaydedilen; aynı anda kaydedilenlerde (aynı dosya)
    /// erken an, sonra istasyon anahtarı, en son kimlik. İçe aktarma ve yeniden eşleştirme AYNI sırayı kullanır.</summary>
    public static int Precedence(HgsCrossingRef a, HgsCrossingRef b)
    {
        var c = a.CreatedAtUtc.CompareTo(b.CreatedAtUtc);
        if (c != 0) return c;
        c = a.At.CompareTo(b.At);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.StationKey, b.StationKey);
        return c != 0 ? c : a.Id.CompareTo(b.Id);
    }

    /// <summary>
    /// <paramref name="c"/> olası mükerrer kümesinin İKİNCİL üyesi mi? Kümede yansıtılmış bir satır ya da sırada
    /// önce gelen bir satır varsa evet — ikincil satır asla otomatik borçlandırılmaz (kümeden en çok bir geçiş).
    /// </summary>
    public static bool IsSecondary(HgsCrossingRef c, IEnumerable<HgsCrossingRef> others)
        => others.Any(d => AreNearDuplicates(c, d)
                           && (d.Status == HgsCrossingStatus.Yansitildi || Precedence(d, c) < 0));

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
            return (null, new HgsMatch(HgsCrossingStatus.KiraDisi, null, UnknownPlateNote));
        return (id, HgsCrossingMatcher.Match(at, windowsByVehicle[id]));
    }

    /// <summary>Eşleşmeyi olası mükerrer kararıyla birleştirir: ikincil satır Belirsiz olur (kira bağı bilgi için kalır).</summary>
    public static HgsMatch WithNearDuplicate(HgsMatch match, bool secondary)
        => secondary ? new HgsMatch(HgsCrossingStatus.Belirsiz, match.Rental, NearDuplicateNote) : match;
}
