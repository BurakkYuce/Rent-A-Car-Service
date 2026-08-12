using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// Export tarih kuralı — <b>YEREL TAKVİM GÜNÜ</b> (<see cref="ExportTarih"/>). DB gerektirmez.
///
/// <para><b>Neden bu test var:</b> tarih alanları forma yerel gün olarak girilir ve
/// <c>FormParse.Date</c> UTC'ye çevirir (01.03 00:00 +03 → 28.02 21:00 UTC). Export'lar bir dönem
/// ham UTC yazıyordu → indirilen dosyada <b>bir gün geri</b> görünüyordu. Hata canlı duman
/// testinde bulunmuştu; kuralı tutan bir test YOKTU, yani sessizce geri dönebilirdi.</para>
///
/// <para><b>Saat diliminden bağımsızlık:</b> CI UTC'de koşar; orada yerel gün = UTC gün olduğu için
/// <c>.LocalDateTime</c>'a doğrudan bakan bir iddia kendiliğinden geçer ve hiçbir şey kanıtlamaz.
/// Bu yüzden <see cref="ExportTarih.Gun"/> saat dilimini parametre olarak alır ve testler
/// İstanbul (+03) diliminde SABİT beklentilerle koşar — makineden bağımsız gerçek kilit.</para>
/// </summary>
public sealed class ExportTarihKuraliTests
{
    /// <summary>İstanbul; CI/konteyner tzdata'sında IANA adı, Windows'ta ayrı ad → ikisi de denenir.</summary>
    private static readonly TimeZoneInfo Istanbul = Bul("Europe/Istanbul", "Turkey Standard Time");

    private static TimeZoneInfo Bul(params string[] adlar)
    {
        foreach (var a in adlar)
            if (TimeZoneInfo.TryFindSystemTimeZoneById(a, out var tz)) return tz;
        // Son çare: sabit +03 (İstanbul 2016'dan beri yaz saati uygulamıyor — gün sınırı aynı).
        return TimeZoneInfo.CreateCustomTimeZone("test-+03", TimeSpan.FromHours(3), "+03", "+03");
    }

    [Fact]
    public void Gece_yarisina_yakin_UTC_damgasi_YEREL_gune_dusme()
    {
        // Kullanıcı 1 Mart girdi → FormParse.Date 28 Şubat 21:00 UTC sakladı.
        var saklanan = new DateTimeOffset(2026, 2, 28, 21, 0, 0, TimeSpan.Zero);

        // Beklenen ELLE: +03'te 1 Mart 00:00 → "2026-03-01". (Eski hatalı davranış: "2026-02-28".)
        Assert.Equal("2026-03-01", ExportTarih.Gun(saklanan, Istanbul));
        Assert.Equal("2026-03-01", ExportTarih.Hucre(saklanan, Istanbul).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Gun_ortasi_damga_kaymaz()
    {
        var oglen = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026-06-15", ExportTarih.Gun(oglen, Istanbul));
    }

    [Fact]
    public void Null_tarih_null_doner()
    {
        Assert.Null(ExportTarih.Gun(null, Istanbul));
    }

    /// <summary>
    /// Katalogların kuralı GERÇEKTEN kullandığı: aynı damga hem katalogdan hem kuraldan geçirilir.
    /// Biri ham UTC'ye dönerse (regresyon) bu iddia, yerel dilim UTC olmayan her makinede kırılır.
    /// </summary>
    [Fact]
    public void Katalog_ciktisi_kuralla_AYNI_gunu_yazar()
    {
        var damga = new DateTimeOffset(2026, 2, 28, 21, 0, 0, TimeSpan.Zero);
        var arac = new Vehicle
        {
            Plaka = "34 TZ 01", Durum = VehicleStatus.Musait,
            TescilTarihi = damga, AlimTarihi = damga
        };

        var t = ListExportCatalog.Araclar([arac]);
        var tescilIdx = t.Headers.ToList().IndexOf("Tescil Tarihi");
        var alimIdx = t.Headers.ToList().IndexOf("Alım Tarihi");
        Assert.True(tescilIdx >= 0 && alimIdx >= 0);

        var beklenen = ExportTarih.Gun(damga);   // üretim yolu = makinenin yerel dilimi
        Assert.Equal(beklenen, t.Rows[0][tescilIdx]);
        Assert.Equal(beklenen, t.Rows[0][alimIdx]);
    }
}
