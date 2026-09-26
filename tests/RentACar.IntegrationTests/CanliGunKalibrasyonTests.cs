using RentACar.Application.Bookings;

namespace RentACar.IntegrationTests;

/// <summary>
/// CANLI KALİBRASYON — gün sayısı. Beklenen değerler <b>canlı referans sistem'in kendi kodundan</b>
/// türetildi, bizim implementasyonumuzdan DEĞİL (CLAUDE.md §3 bağımsız oracle ilkesi).
///
/// <para><b>Kaynak</b> (2026-08-06 parite koşusu, `kiralama.aspx` istemci JS'i):</para>
/// <code>
/// // Hizmet_Gun_Bul — formda gün hesabını yapan fonksiyon (19 çağrı)
/// fark_gun = Math.floor(hours / 24);
/// if (fark_gun &lt; 1) fark_gun = 1;
/// Fark_Saat = calculateTimeDifference(...);   // DAKİKA duyarlı: bitH + bitM/60 − basH − basM/60
/// if (Fark_Saat &gt;= Saat_Farki_Hesap) fark_gun += 1;   // canlıda Saat_Farki_Hesap = 3
/// </code>
///
/// <para>Rapor bayatlar, test bayatlamaz: canlı eşiği değişirse bu testler kırmızıya döner ve
/// kalibrasyonun yenilenmesi gerektiğini söyler.</para>
/// </summary>
public sealed class CanliGunKalibrasyonTests
{
    private static readonly DateTimeOffset Bas =
        new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);   // saf matematik; DB/tarih politikası yok

    private static int Gun(int gunFarki, int bitSaat, int bitDakika = 0)
        => BookingMath.ComputeDays(Bas, Bas.AddDays(gunFarki)
            .AddHours(bitSaat - 10).AddMinutes(bitDakika));

    [Theory]
    // (gün farkı, bitiş saati, bitiş dakikası, canlının verdiği gün)
    [InlineData(3, 10, 0, 3)]    // tam 72sa → kalan 0 → 3
    [InlineData(3, 12, 59, 3)]   // kalan 2sa59dk < 3 → 3   (eski 2.9 eşiğinde 4 çıkıyordu)
    [InlineData(3, 12, 54, 3)]   // kalan 2sa54dk → pencerenin ALT ucu (eski eşikte 4)
    [InlineData(3, 13, 0, 4)]    // kalan TAM 3sa → eşiği BULUYOR → 4
    [InlineData(3, 13, 30, 4)]   // kalan 3sa30dk → 4
    [InlineData(5, 11, 10, 5)]   // kalan 1sa10dk → 5       (eski eşikte de 5)
    [InlineData(1, 12, 30, 1)]   // aynı gün 2sa30dk → 1
    [InlineData(0, 15, 0, 1)]    // aynı gün 5sa → en az 1 gün
    public void Canli_gun_sayisiyla_birebir(int gunFarki, int bitSaat, int bitDk, int beklenen)
        => Assert.Equal(beklenen, Gun(gunFarki, bitSaat, bitDk));

    [Fact]
    public void Esik_TAM_3_saat_pencerenin_iki_yani_ayrisir()
    {
        // Sınırın kendisi dahil (canlı `>=` kullanıyor).
        Assert.Equal(3, Gun(3, 12, 59));   // 2sa59dk → gün EKLENMEZ
        Assert.Equal(4, Gun(3, 13, 0));    // 3sa00dk → gün eklenir
    }

    [Fact]
    public void Eski_2_9_esiginin_fazla_faturaladigi_6_dakikalik_pencere_KAPANDI()
    {
        // 2sa54dk .. 2sa59dk arası: eski sabit (2.9) burada +1 gün ekliyordu, canlı eklemiyor.
        // Müşteriye bir günlük kira fazla yansıyordu.
        for (var dk = 54; dk <= 59; dk++)
            Assert.Equal(3, Gun(3, 12, dk));
    }
}
