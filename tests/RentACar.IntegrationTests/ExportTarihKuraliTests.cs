using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// Export tarih kuralı — <b>YEREL TAKVİM GÜNÜ</b> (<see cref="ExportDate"/>). DB gerektirmez.
///
/// <para><b>Neden bu test var:</b> tarih alanları forma yerel gün olarak girilir ve
/// <c>FormParse.Date</c> UTC'ye çevirir (01.03 00:00 +03 → 28.02 21:00 UTC). Export'lar bir dönem
/// ham UTC yazıyordu → indirilen dosyada <b>bir gün geri</b> görünüyordu. Hata canlı duman
/// testinde bulunmuştu; kuralı tutan bir test YOKTU, yani sessizce geri dönebilirdi.</para>
///
/// <para><b>Saat diliminden bağımsızlık:</b> CI UTC'de koşar; orada yerel gün = UTC gün olduğu için
/// <c>.LocalDateTime</c>'a doğrudan bakan bir iddia kendiliğinden geçer ve hiçbir şey kanıtlamaz.
/// Bu yüzden <see cref="ExportDate.Day"/> saat dilimini parametre olarak alır ve testler
/// İstanbul (+03) diliminde SABİT beklentilerle koşar — makineden bağımsız gerçek kilit.</para>
/// </summary>
public sealed class ExportTarihKuraliTests
{
    /// <summary>İstanbul; CI/konteyner tzdata'sında IANA adı, Windows'ta ayrı ad → ikisi de denenir.</summary>
    private static readonly TimeZoneInfo Istanbul = Find("Europe/Istanbul", "Turkey Standard Time");

    private static TimeZoneInfo Find(params string[] names)
    {
        foreach (var a in names)
            if (TimeZoneInfo.TryFindSystemTimeZoneById(a, out var tz)) return tz;
        // Son çare: sabit +03 (İstanbul 2016'dan beri yaz saati uygulamıyor — gün sınırı aynı).
        return TimeZoneInfo.CreateCustomTimeZone("test-+03", TimeSpan.FromHours(3), "+03", "+03");
    }

    [Fact]
    public void Gece_yarisina_yakin_UTC_damgasi_YEREL_gune_dusme()
    {
        // Kullanıcı 1 Mart girdi → FormParse.Date 28 Şubat 21:00 UTC sakladı.
        var stored = new DateTimeOffset(2026, 2, 28, 21, 0, 0, TimeSpan.Zero);

        // Beklenen ELLE: +03'te 1 Mart 00:00 → "2026-03-01". (Eski hatalı davranış: "2026-02-28".)
        Assert.Equal("2026-03-01", ExportDate.Day(stored, Istanbul));
        Assert.Equal("2026-03-01", ExportDate.Cell(stored, Istanbul).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Gun_ortasi_damga_kaymaz()
    {
        var noon = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026-06-15", ExportDate.Day(noon, Istanbul));
    }

    [Fact]
    public void Null_tarih_null_doner()
    {
        Assert.Null(ExportDate.Day(null, Istanbul));
    }

    /// <summary>
    /// Katalogların kuralı GERÇEKTEN kullandığı: aynı damga hem katalogdan hem kuraldan geçirilir.
    /// Biri ham UTC'ye dönerse (regresyon) bu iddia, yerel dilim UTC olmayan her makinede kırılır.
    /// </summary>
    [Fact]
    public void Katalog_ciktisi_kuralla_AYNI_gunu_yazar()
    {
        var stamp = new DateTimeOffset(2026, 2, 28, 21, 0, 0, TimeSpan.Zero);
        var vehicle = new Vehicle
        {
            Plaka = "34 TZ 01", Durum = VehicleStatus.Musait,
            TescilTarihi = stamp, AlimTarihi = stamp
        };

        var t = ListExportCatalog.Vehicles([vehicle]);
        var registrationIdx = t.Headers.ToList().IndexOf("Tescil Tarihi");
        var purchaseIdx = t.Headers.ToList().IndexOf("Alım Tarihi");
        Assert.True(registrationIdx >= 0 && purchaseIdx >= 0);

        var expected = ExportDate.Day(stamp);   // üretim yolu = makinenin yerel dilimi
        Assert.Equal(expected, t.Rows[0][registrationIdx]);
        Assert.Equal(expected, t.Rows[0][purchaseIdx]);
    }
}
