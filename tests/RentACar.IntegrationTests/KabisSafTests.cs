using RentACar.Application.Kabis;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// KABİS saf kuralları (DB'siz): hata metni maskesi, geri çekilme politikası, yük kurucu doğrulamaları.
/// BAĞIMSIZ ORACLE: beklenen metinler/süreler elle yazıldı (1, 5, 15 … dakika; "***").
/// </summary>
public sealed class KabisSafTests
{
    [Fact]
    public void Maske_bilinen_degerleri_ve_uzun_rakam_dizilerini_gizler_kisa_kodlari_korur()
    {
        var s = KabisErrorText.Sanitize(
            "HATA 1000: 10000000146 kişisi (pasaport u98765432, tel 05321234567) bulunamadı",
            ["U98765432"]);
        Assert.Equal("HATA 1000: *** kişisi (pasaport ***, tel ***) bulunamadı", s);
    }

    [Fact]
    public void Maske_bos_metni_null_yapar_ve_uzunlugu_sinirlar()
    {
        Assert.Null(KabisErrorText.Sanitize("   "));
        var longText = KabisErrorText.Sanitize(new string('x', 2000))!;
        Assert.Equal(512, longText.Length);
        Assert.EndsWith("...", longText);
    }

    [Fact]
    public void Geri_cekilme_1_5_15_30_60_120_240_dakika_ve_8_denemede_tukenir()
    {
        Assert.Equal([1, 5, 15, 30, 60, 120, 240, 240],
            Enumerable.Range(1, 8).Select(a => (int)KabisRetryPolicy.Delay(a).TotalMinutes).ToArray());
        Assert.False(KabisRetryPolicy.Exhausted(7));
        Assert.True(KabisRetryPolicy.Exhausted(8));
    }

    private static KabisPartySource Person(CustomerType type = CustomerType.Bireysel, string? tc = "enc-tc",
        string? passport = null, string? title = null, string? ad = "Ali", string? soyad = "Kaya")
        => new(type, ad, soyad, title, type == CustomerType.Bireysel ? null : "1234567890",
            tc, null, passport, null, "enc-ehl", null, "B", null, null, "Türkiye", "05320000000", "Cad. 1", "İstanbul", "Kadıköy");

    private static KabisRentalSource Source(KabisPartySource renter, string plate = "34 ABC 123") => new(
        Guid.NewGuid(), "2026150901001", RentalStatus.Kirada, null, "Merkez", "Havalimanı",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3), null, 1000, null, plate, "Fiat Egea", renter, null, "Misafir", "Sürücü");

    // Sahte "şifre çözücü": cipher → düz değer.
    private static string? Unprotect(string? cipher) => cipher switch
    {
        "enc-tc" => "10000000146",
        "enc-bad-tc" => "1234",
        "enc-pass" => "U1234567",
        "enc-ehl" => "B123456",
        _ => null,
    };

    [Fact]
    public void Bireysel_kiraci_yuku_kimligi_bellekte_cozer_ve_maske_listesine_ekler()
    {
        var b = KabisPayloadBuilder.Rental(Source(Person()), DateTimeOffset.UtcNow, Unprotect);
        Assert.Null(b.Error);
        Assert.Equal("10000000146", b.Notice!.Kiralayan!.TcKimlik);
        Assert.Equal("B123456", b.Notice.Kiralayan.EhliyetNo);
        Assert.Equal("Kadıköy, İstanbul", b.Notice.Kiralayan.Adres!.Split(", ", 2)[1]);
        Assert.Equal("Misafir", b.Notice.Surucu!.Ad); // misafir 2. sürücü: ad/soyad, kimlik numarası YOK
        Assert.Null(b.Notice.Surucu.TcKimlik);
        Assert.Contains("10000000146", b.Secrets);
        Assert.Contains("B123456", b.Secrets);
        // Yük ToString'i kimlik taşımaz (log/hata yazdırımı).
        Assert.DoesNotContain("10000000146", b.Notice.ToString());
        Assert.DoesNotContain("10000000146", b.Notice.Kiralayan.ToString());
    }

    [Fact]
    public void Tc_ve_pasaport_yoksa_ya_da_tc_bozuksa_kalici_hata()
    {
        Assert.Contains("T.C. kimlik ya da pasaport",
            KabisPayloadBuilder.Rental(Source(Person(tc: null)), DateTimeOffset.UtcNow, Unprotect).Error);
        Assert.Contains("11 haneli",
            KabisPayloadBuilder.Rental(Source(Person(tc: "enc-bad-tc")), DateTimeOffset.UtcNow, Unprotect).Error);
        // Yabancı uyruklu: TC yok, pasaport var → geçerli.
        var foreign = KabisPayloadBuilder.Rental(Source(Person(tc: null, passport: "enc-pass")), DateTimeOffset.UtcNow, Unprotect);
        Assert.Null(foreign.Error);
        Assert.Equal("U1234567", foreign.Notice!.Kiralayan!.PasaportNo);
        Assert.Contains("Araç plakası eksik",
            KabisPayloadBuilder.Rental(Source(Person(), plate: " "), DateTimeOffset.UtcNow, Unprotect).Error);
    }

    [Fact]
    public void Kurumsal_kiracida_kisi_alani_gitmez_unvan_ve_vergi_no_gider()
    {
        var b = KabisPayloadBuilder.Rental(
            Source(Person(CustomerType.Kurumsal, tc: null, title: "Belediye Başkanlığı", ad: null, soyad: null)),
            DateTimeOffset.UtcNow, Unprotect);
        Assert.Null(b.Error);
        Assert.True(b.Notice!.KurumMu);
        Assert.Null(b.Notice.Kiralayan);
        Assert.Equal("Belediye Başkanlığı", b.Notice.KurumUnvan);
        Assert.Equal("1234567890", b.Notice.KurumVergiNo);
        Assert.Contains("1234567890", b.Secrets);
    }

    [Fact]
    public void Takvim_gunu_alanlari_istanbul_gunune_gore_okunur()
    {
        // 1990-05-17 yerel gece yarısı (+03:00) UTC'de 16 Mayıs 21:00'dir; gün 17 kalmalı.
        var local = new DateTimeOffset(1990, 5, 17, 0, 0, 0, TimeSpan.FromHours(3));
        var utcMidnight = new DateTimeOffset(1990, 5, 17, 0, 0, 0, TimeSpan.Zero);
        foreach (var d in new[] { local, utcMidnight })
        {
            var p = Person() with { DogumTarihi = d };
            var b = KabisPayloadBuilder.Rental(Source(p), DateTimeOffset.UtcNow, Unprotect);
            Assert.Equal(new DateOnly(1990, 5, 17), b.Notice!.Kiralayan!.DogumTarihi);
        }
    }
}
