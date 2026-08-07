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

    /// <summary>Verilen anı tam saniyeye kırpar.</summary>
    public static DateTimeOffset SaniyeyeHizala(this DateTimeOffset d)
        => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Offset);
}
