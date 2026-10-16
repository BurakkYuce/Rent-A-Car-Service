using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

/// <summary>Bir aracın bir kirasının zaman penceresi (eşleştirme girdisi).</summary>
/// <param name="Start">Teslim anı (BasTar), UTC.</param>
/// <param name="End">Dönüş anı (GercekDonusTar), UTC; açık kirada null = sınırsız.</param>
/// <param name="Delivered">Araç müşteriye çıktı mı: açık kirada çıkış KM girilmiş olmalı; kapanmış kira teslim edilmiştir.</param>
public sealed record HgsRentalWindow(Guid RentalId, Guid VehicleId, Guid CustomerId, string SozlesmeNo,
    DateTimeOffset Start, DateTimeOffset? End, bool Delivered = true);

/// <summary>Eşleştirme sonucu. <paramref name="Note"/> yalnız Belirsiz/KiraDisi gerekçesi.</summary>
public sealed record HgsMatch(HgsCrossingStatus Status, HgsRentalWindow? Rental, string? Note = null);

/// <summary>
/// SAF eşleştirme kuralı (DB yok): geçiş anı <c>teslim ≤ an &lt; dönüş</c> olan kiraya bağlanır.
/// <list type="bullet">
/// <item><b>Yarı açık aralık:</b> teslim anındaki geçiş kiraya DAHİL, dönüş anındaki geçiş HARİÇ. Aynı aracın art arda
/// iki kirası (biri 10:00'da döner, diğeri 10:00'da çıkar) aynı anı ikisi birden talep edemez; 10:00 sonrakine düşer.</item>
/// <item><b>Açık kira</b> (dönüş yok): teslimden sonraki her an kiraya aittir — planlanan bitiş geçmiş olsa da araç hâlâ
/// müşteridedir.</item>
/// <item><b>Teslim edilmemiş açık kira</b> (çıkış KM yok; adversarial #391 L3): araç henüz müşteriye çıkmamış olabilir
/// (personel teslimata götürüyor) → <see cref="HgsCrossingStatus.Belirsiz"/>; teslim girilip yeniden eşleştirilince
/// kiraya bağlanır.</item>
/// <item>Birden çok pencere aynı anı kapsıyorsa (veri anomalisi) <see cref="HgsCrossingStatus.Belirsiz"/>: hiçbir
/// müşteri tahminle borçlandırılmaz.</item>
/// <item>Pencere yoksa <see cref="HgsCrossingStatus.KiraDisi"/> (firma gideri).</item>
/// </list>
/// </summary>
public static class HgsCrossingMatcher
{
    public const string ManyRentalsNote = "Birden çok kira bu anı kapsıyor; kira kayıtlarını düzeltip yeniden eşleştirin.";
    public const string UndeliveredNote = "Kira henüz teslim edilmemiş (çıkış KM yok); teslim girilince yeniden eşleştirin.";

    public static HgsMatch Match(DateTimeOffset at, IEnumerable<HgsRentalWindow> windows)
    {
        HgsRentalWindow? found = null;
        foreach (var w in windows)
        {
            if (at < w.Start) continue;
            if (w.End is { } end && at >= end) continue;
            if (found is not null && found.RentalId != w.RentalId)
                return new HgsMatch(HgsCrossingStatus.Belirsiz, null, ManyRentalsNote);
            found = w;
        }
        if (found is null) return new HgsMatch(HgsCrossingStatus.KiraDisi, null);
        return found.Delivered
            ? new HgsMatch(HgsCrossingStatus.Eslesti, found)
            : new HgsMatch(HgsCrossingStatus.Belirsiz, found, UndeliveredNote);
    }
}
