namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Testlerde kullanılan zaman tabanı.
///
/// <para><b>Neden var:</b> PostgreSQL <c>timestamptz</c> MİKROSANİYE saklar. Linux'ta
/// <c>DateTimeOffset.UtcNow</c> 100ns çözünürlükte tick üretir → DB'ye yazılan değer geri
/// okunduğunda KIRPILMIŞ olur ve <c>Assert.Equal(yazılan, okunan)</c> patlar. macOS'ta üretilen
/// değer zaten ~µs olduğu için aynı test lokalde YEŞİL, CI'da KIRMIZI görünür — bu tuzağa
/// tekrar tekrar düşüldü (bkz. birkaç test dosyasındaki yerel "whole-second" kopyaları).</para>
///
/// <para>Çözüm: tarih tabanını TAM SANİYEYE hizala; kırpma-farkı yapısal olarak sıfırlanır.
/// Yeni testlerde <c>DateTimeOffset.UtcNow</c> yerine bunu kullanın.</para>
/// </summary>
public static class TestZaman
{
    /// <summary>Tam saniyeye hizalı UTC "şimdi" (PG round-trip'i kayıpsız).</summary>
    public static DateTimeOffset Simdi()
    {
        var n = DateTimeOffset.UtcNow;
        return new DateTimeOffset(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, TimeSpan.Zero);
    }

    /// <summary>
    /// Bugünden <paramref name="gun"/> gün sonra, UTC <paramref name="saat"/>:00 (tam saniye).
    ///
    /// <para><b>Rezervasyon ve teklif testleri SABİT TARİH KULLANAMAZ.</b> <c>TarihPolitikasi.RezervasyonBaslangic</c>
    /// başlangıcı "dünden eski" ve "1 yıldan ileri" ise reddeder; sabit bir tarih bu pencerenin içinden
    /// takvimle birlikte ÇIKAR. 2026-09-21'de 2026-09-01'e sabitli 4 test ("Rezervasyon geçmiş tarihe
    /// alınamaz.") hiçbir kod değişmeden kırmızıya döndü; 2026-11-01'e sabitli kabul testi de Kasım'da
    /// dönecekti. <c>TestTarihBombasiTests</c> bu kuralı kaynak taramasıyla kilitler.</para>
    /// </summary>
    public static DateTimeOffset GunSonra(int gun, int saat = 9)
        => new(DateTime.UtcNow.Date.AddDays(gun).AddHours(saat), TimeSpan.Zero);

    /// <summary>Verilen anı tam saniyeye kırpar.</summary>
    public static DateTimeOffset SaniyeyeHizala(this DateTimeOffset d)
        => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Offset);
}
