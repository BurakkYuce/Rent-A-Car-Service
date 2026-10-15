using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

/// <summary>Bir aracın bir kirasının zaman penceresi (eşleştirme girdisi).</summary>
/// <param name="Start">Teslim anı (BasTar), UTC.</param>
/// <param name="End">Dönüş anı (GercekDonusTar), UTC; açık kirada null = sınırsız.</param>
public sealed record HgsRentalWindow(Guid RentalId, Guid VehicleId, Guid CustomerId, string SozlesmeNo,
    DateTimeOffset Start, DateTimeOffset? End);

/// <summary>Eşleştirme sonucu.</summary>
public sealed record HgsMatch(HgsCrossingStatus Status, HgsRentalWindow? Rental);

/// <summary>
/// SAF eşleştirme kuralı (DB yok): geçiş anı <c>teslim ≤ an &lt; dönüş</c> olan kiraya bağlanır.
/// <list type="bullet">
/// <item><b>Yarı açık aralık:</b> teslim anındaki geçiş kiraya DAHİL, dönüş anındaki geçiş HARİÇ. Aynı aracın art arda
/// iki kirası (biri 10:00'da döner, diğeri 10:00'da çıkar) aynı anı ikisi birden talep edemez; 10:00 sonrakine düşer.</item>
/// <item><b>Açık kira</b> (dönüş yok): teslimden sonraki her an kiraya aittir — planlanan bitiş geçmiş olsa da araç hâlâ
/// müşteridedir.</item>
/// <item>Birden çok pencere aynı anı kapsıyorsa (veri anomalisi) <see cref="HgsCrossingStatus.Belirsiz"/>: hiçbir
/// müşteri tahminle borçlandırılmaz.</item>
/// <item>Pencere yoksa <see cref="HgsCrossingStatus.KiraDisi"/> (firma gideri).</item>
/// </list>
/// </summary>
public static class HgsCrossingMatcher
{
    public static HgsMatch Match(DateTimeOffset at, IEnumerable<HgsRentalWindow> windows)
    {
        HgsRentalWindow? found = null;
        foreach (var w in windows)
        {
            if (at < w.Start) continue;
            if (w.End is { } end && at >= end) continue;
            if (found is not null && found.RentalId != w.RentalId) return new HgsMatch(HgsCrossingStatus.Belirsiz, null);
            found = w;
        }
        return found is null
            ? new HgsMatch(HgsCrossingStatus.KiraDisi, null)
            : new HgsMatch(HgsCrossingStatus.Eslesti, found);
    }
}
