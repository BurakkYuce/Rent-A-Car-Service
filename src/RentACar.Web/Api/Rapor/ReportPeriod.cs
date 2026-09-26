using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Rapor;

/// <summary>
/// F10.1 — ortak rapor şablonunun DÖNEM sözleşmesi. Her rapor ucu tarihleri AYNI biçimde alır:
/// <c>bas</c>/<c>bit</c> = takvim günü (<c>yyyy-MM-dd</c>), iki uç da DAHİL, gün İSTANBUL günüdür.
/// <list type="bullet">
/// <item><b>Sınır:</b> yıl 1900–2100 dışı → 400 <c>errors[bas|bit]</c> (Npgsql/numeric taşması ve anlamsız
/// sorgu yerine alan hatası). <c>bas &gt; bit</c> → 400 <c>errors[bit]</c>.</item>
/// <item><b>Gün sayısı tavanı</b> (<see cref="Validate"/>'in <c>maxDays</c>'i): gün-kırılımlı raporlar
/// (araç durum takip) gün × araç satırı üretir; geniş aralık DoS'tur → 400 <c>errors[bit]</c>.</item>
/// <item><b>İki dönüşüm</b> — servisin semantiğine göre uç seçer:
///   <see cref="FromUtc"/>/<see cref="ToUtc"/> = İstanbul gününün gerçek UTC anları (an-süzgeçli servisler: defter,
///   fatura, kira tarihleri); <see cref="Anchor"/> = günün UTC gece yarısı (servis girdiyi <c>.UtcDateTime.Date</c>
///   ile TAKVİM GÜNÜNE indiriyorsa — İstanbul gece yarısı 21:00Z önceki güne düşerdi).</item>
/// </list>
/// Blazor ekranları günü UTC gece yarısıyla kuruyordu; API İstanbul gününü kullanır (gün sınırındaki 3 saatlik
/// kayma bilinçli düzeltmedir, parite testleri gün ortası kayıtla kurulur).
/// </summary>
public sealed record ReportPeriod(DateOnly? Bas, DateOnly? Bit)
{
    public const int MinYear = 1900;
    public const int MaxYear = 2100;

    /// <summary>Doğrulanmış dönem. <paramref name="maxDays"/> verilirse iki uç dolu olduğunda gün sayısı sınırlanır.</summary>
    public static ReportPeriod Validate(DateOnly? bas, DateOnly? bit, int? maxDays = null,
        string basField = "bas", string bitField = "bit")
    {
        Year(bas, basField);
        Year(bit, bitField);
        if (bas is { } b && bit is { } t)
        {
            if (b > t) throw new ValidationException("Bitiş tarihi başlangıçtan önce olamaz.", bitField);
            if (maxDays is { } max && t.DayNumber - b.DayNumber + 1 > max)
                throw new ValidationException($"Tarih aralığı en fazla {max} gün olabilir.", bitField);
        }
        return new ReportPeriod(bas, bit);
    }

    /// <summary>Tek gün parametresi (günlük faaliyet, araç günlük durum, yaşlandırma tarihi) için aynı yıl sınırı.</summary>
    public static DateOnly? ValidateDay(DateOnly? day, string field)
    {
        Year(day, field);
        return day;
    }

    private static void Year(DateOnly? d, string field)
    {
        if (d is { } x && (x.Year < MinYear || x.Year > MaxYear))
            throw new ValidationException($"Tarih {MinYear}–{MaxYear} yılları arasında olmalıdır.", field);
    }

    /// <summary>Başlangıç gününün İstanbul gece yarısı (UTC an).</summary>
    public DateTimeOffset? FromUtc => Bas is { } b ? F5Ortak.GunBasi(b) : null;

    /// <summary>Bitiş gününün SONU (ertesi İstanbul gece yarısı − 1 µs; Postgres çözünürlüğü µs).</summary>
    public DateTimeOffset? ToUtc => Bit is { } t ? F5Ortak.GunBasi(t.AddDays(1)).AddMicroseconds(-1) : null;

    /// <summary>Takvim günü çıpası: günün UTC gece yarısı (servis <c>.UtcDateTime.Date</c> ile güne indiriyorsa).</summary>
    public static DateTimeOffset Anchor(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public DateTimeOffset? FromAnchor => Bas is { } b ? Anchor(b) : null;

    /// <summary>Bitiş günü çıpası + gün sonu (UTC takvim günü semantiğiyle çalışan an-süzgeçleri için).</summary>
    public DateTimeOffset? ToAnchorEnd => Bit is { } t ? Anchor(t).AddDays(1).AddMicroseconds(-1) : null;

    /// <summary>Bugünün İstanbul günü (varsayılan pencereler için).</summary>
    public static DateOnly Today => TenantGun.Gun(DateTimeOffset.UtcNow);

    /// <summary>Yanıttaki dönem yankısı.</summary>
    public ReportPeriodDto ToDto() => new(Bas, Bit);
}

/// <summary>Dönem parametreleri (sorgu). Her dönemli rapor ucu bunu <c>[AsParameters]</c> ile alır.</summary>
public sealed class ReportPeriodQuery
{
    /// <summary>Başlangıç günü (dahil), <c>yyyy-MM-dd</c>, İstanbul günü.</summary>
    [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }

    /// <summary>Bitiş günü (dahil), <c>yyyy-MM-dd</c>, İstanbul günü.</summary>
    [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }

    public ReportPeriod Validate(int? maxDays = null) => ReportPeriod.Validate(Bas, Bit, maxDays);
}

/// <summary>Sayfalama/sıralama parametreleri (sorgu). Boyut en fazla 200 (<see cref="ListeIstegi"/>).</summary>
public sealed class ReportPageQuery
{
    [FromQuery(Name = "sayfa")] public int? Sayfa { get; set; }
    [FromQuery(Name = "boyut")] public int? Boyut { get; set; }
    /// <summary><c>alan</c> (artan) ya da <c>-alan</c> (azalan); raporun beyaz listesi dışı alan 400.</summary>
    [FromQuery(Name = "sirala")] public string? Sirala { get; set; }

    /// <summary>(Sayfa − 1) × 200 int'i taşımasın.</summary>
    private const int MaxPage = 1_000_000;

    public Sayfa<T> Apply<T>(IReadOnlyList<T> rows, SortFieldMap<T> map)
        => F5Ortak.Sayfala(rows, map, Math.Min(Sayfa ?? 1, MaxPage), Boyut, Sirala);
}

/// <summary>Yanıttaki dönem (istenen günler; boş = sınırsız).</summary>
public sealed record ReportPeriodDto(DateOnly? Bas, DateOnly? Bit);
