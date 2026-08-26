using RentACar.Domain.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// Belge numarası biçim kuralları (saf birim — DB yok).
///
/// <para><b>Bağımsız oracle:</b> beklenen dizeler kullanıcının verdiği örnekten ve elle sayımdan
/// gelir, <see cref="BelgeNo"/>'nun çıktısından türetilmez.</para>
///
/// <para><b>Tip kodu testi bir SÖZLEŞMEDİR:</b> kod bir kez kesilmiş belgeye yazıldıktan sonra
/// değiştirilemez (numara mali kayıtta, PDF'te, paylaşım linkinde ve müşteri mesajında geçer).
/// Bu test kodları kilitler — değiştiren biri kırmızıya çarpar.</para>
/// </summary>
public sealed class BelgeNoTests
{
    private static readonly DateOnly Ornek = new(2026, 8, 26);   // 26 Ağustos 2026

    [Fact]
    public void Kullanicinin_verdigi_ornek()
    {
        // Kullanıcının mesajındaki birebir örnek: "sözleşme no : 2026260801001"
        //   2026 | 26 (gün) | 08 (ay) | 01 (kira sözleşmesi) | 001 (günün ilk belgesi)
        Assert.Equal("2026260801001", BelgeNo.Bicimle(BelgeNoTuru.KiraSozlesmesi, Ornek, 1));
    }

    [Fact]
    public void Ayni_gun_ikinci_belge()
        => Assert.Equal("2026260801002", BelgeNo.Bicimle(BelgeNoTuru.KiraSozlesmesi, Ornek, 2));

    [Fact]
    public void Farkli_tip_ayni_gun_kendi_sirasindan_baslar()
        => Assert.Equal("2026260802001", BelgeNo.Bicimle(BelgeNoTuru.Rezervasyon, Ornek, 1));

    [Fact]
    public void Tek_haneli_gun_ve_ay_sifirla_doldurulur()
        // 1 Ocak 2027, tahsilat (05), 7. belge → 2027 01 01 05 007
        => Assert.Equal("2027010105007", BelgeNo.Bicimle(BelgeNoTuru.Tahsilat, new DateOnly(2027, 1, 1), 7));

    [Fact]
    public void Numara_13_hane_ve_tamami_rakam()
    {
        var no = BelgeNo.Bicimle(BelgeNoTuru.Gider, Ornek, 42);
        Assert.Equal(13, no.Length);
        Assert.All(no, c => Assert.InRange(c, '0', '9'));
    }

    [Fact]
    public void Tasma_999_ustu_14_haneye_cikar()
    {
        // {sira:D3} MİNİMUM 3 hane demek; 1000 doğal olarak "1000" yazar. Elle sayıldı: 2026|26|08|01|1000
        Assert.Equal("20262608011000", BelgeNo.Bicimle(BelgeNoTuru.KiraSozlesmesi, Ornek, 1000));
        Assert.Equal(14, BelgeNo.Bicimle(BelgeNoTuru.KiraSozlesmesi, Ornek, 1000).Length);
    }

    [Fact]
    public void Sifir_veya_negatif_sira_reddedilir()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BelgeNo.Bicimle(BelgeNoTuru.Gider, Ornek, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BelgeNo.Bicimle(BelgeNoTuru.Gider, Ornek, -1));
    }

    [Fact]
    public void Sayac_anahtari_tip_ve_gunu_tasir()
    {
        // Anahtar yyyyMMdd taşır (numaradaki yyyyddMM'den FARKLI): anahtar insan için değil,
        // gün başına tek satır üretmek için — sıralanabilir olması tercih edilir.
        Assert.Equal("01:20260826", BelgeNo.SayacAnahtari(BelgeNoTuru.KiraSozlesmesi, Ornek));
        Assert.Equal("05:20260826", BelgeNo.SayacAnahtari(BelgeNoTuru.Tahsilat, Ornek));
        Assert.Equal("17:20270101", BelgeNo.SayacAnahtari(BelgeNoTuru.MaliyetTeklifi, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Sayac_anahtari_TenantSequences_Name_kolonuna_sigar()
        => Assert.True(BelgeNo.SayacAnahtari(BelgeNoTuru.MaliyetTeklifi, Ornek).Length <= 64);

    // ───────────────────────── Tip kodu sözleşmesi ─────────────────────────

    [Theory]
    [InlineData(BelgeNoTuru.KiraSozlesmesi, 1)]
    [InlineData(BelgeNoTuru.Rezervasyon, 2)]
    [InlineData(BelgeNoTuru.Teklif, 3)]
    [InlineData(BelgeNoTuru.Fatura, 4)]
    [InlineData(BelgeNoTuru.Tahsilat, 5)]
    [InlineData(BelgeNoTuru.Tediye, 6)]
    [InlineData(BelgeNoTuru.Gider, 7)]
    [InlineData(BelgeNoTuru.Ceza, 8)]
    [InlineData(BelgeNoTuru.ServisKaydi, 9)]
    [InlineData(BelgeNoTuru.DisHizmet, 10)]
    [InlineData(BelgeNoTuru.HasarDosyasi, 11)]
    [InlineData(BelgeNoTuru.Baf, 12)]
    [InlineData(BelgeNoTuru.AracSiparis, 13)]
    [InlineData(BelgeNoTuru.AracSatis, 14)]
    [InlineData(BelgeNoTuru.AracKredi, 15)]
    [InlineData(BelgeNoTuru.FiloKiralama, 16)]
    [InlineData(BelgeNoTuru.MaliyetTeklifi, 17)]
    public void Tip_kodlari_KALICI(BelgeNoTuru tur, int beklenenKod)
        => Assert.Equal(beklenenKod, (int)tur);

    [Fact]
    public void Tip_kodlari_benzersiz_ve_gecerli_aralikta()
    {
        var kodlar = Enum.GetValues<BelgeNoTuru>().Select(t => (int)t).ToList();
        Assert.Equal(kodlar.Count, kodlar.Distinct().Count());
        Assert.All(kodlar, k => Assert.InRange(k, 1, 99));
    }

    [Fact]
    public void HasarDosyasi_ve_Baf_ayri_kod_alir()
        // Eski düzende ikisi de "BAF-" üretiyordu (ayrı sayaç, aynı görünen numara).
        => Assert.NotEqual((int)BelgeNoTuru.HasarDosyasi, (int)BelgeNoTuru.Baf);

    // ───────────────────────── Fatura: GİB formatı ─────────────────────────

    [Fact]
    public void Fatura_GIB_formati_16_hane()
    {
        // Mevzuat: seri(3) + yıl(4) + sıra(9). Elle sayıldı: RNT + 2026 + 000000001
        var no = BelgeNo.FaturaBicimle("RNT", 2026, 1);
        Assert.Equal("RNT2026000000001", no);
        Assert.Equal(16, no.Length);
    }

    [Fact]
    public void Fatura_sirasi_dokuz_haneye_doldurulur()
        => Assert.Equal("ABC2026000012345", BelgeNo.FaturaBicimle("ABC", 2026, 12345));

    [Fact]
    public void Fatura_sayac_anahtari_seri_ve_yil_basina()
    {
        // Sıra HER YIL 1'den başlar ve her seri kendi içinde ilerler → anahtar ikisini de taşımalı.
        Assert.Equal("F:RNT:2026", BelgeNo.FaturaSayacAnahtari("RNT", 2026));
        Assert.NotEqual(BelgeNo.FaturaSayacAnahtari("RNT", 2026), BelgeNo.FaturaSayacAnahtari("RNT", 2027));
        Assert.NotEqual(BelgeNo.FaturaSayacAnahtari("RNT", 2026), BelgeNo.FaturaSayacAnahtari("ABC", 2026));
    }

    [Theory]
    [InlineData("RNT", true)]
    [InlineData("A1B", true)]
    [InlineData("123", true)]
    [InlineData("RN", false)]        // kısa
    [InlineData("RNTX", false)]      // uzun
    [InlineData("rnt", false)]       // küçük harf
    [InlineData("RN-", false)]       // sembol
    [InlineData("RNŞ", false)]       // Türkçe karakter — GİB kabul etmez
    [InlineData("İRN", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Seri_kodu_dogrulamasi(string? seri, bool gecerli)
        => Assert.Equal(gecerli, BelgeNo.SeriGecerliMi(seri));

    [Fact]
    public void Gecersiz_seri_ile_fatura_numarasi_uretilmez()
        => Assert.Throws<ArgumentException>(() => BelgeNo.FaturaBicimle("rnt", 2026, 1));

    // ───────────────────────── Eski/yeni birlikte yaşar ─────────────────────────

    [Fact]
    public void Eski_format_yeni_formatla_ASLA_cakismaz()
    {
        // Eski numaralar HARF içerir (KS-000001, FT-000042); genel yeni desen TAMAMI RAKAM.
        // İki uzay kesişemediği için (TenantId, No) unique indeksleri güvende → geriye dönük
        // yeniden numaralandırma gerekmez (ki rc_prevent_mutation zaten buna izin vermezdi).
        string[] eskiler = ["KS-000001", "RZ-000042", "FT-000108", "TH-000007", "BAF-000003"];
        Assert.All(eskiler, e => Assert.Contains(e, c => char.IsLetter(c)));

        foreach (var tur in Enum.GetValues<BelgeNoTuru>())
        {
            if (tur == BelgeNoTuru.Fatura) continue;               // fatura GİB formatında (harf içerir)
            var yeni = BelgeNo.Bicimle(tur, Ornek, 1);
            Assert.DoesNotContain(yeni, char.IsLetter);
            Assert.DoesNotContain(yeni, eskiler);
        }
    }

    [Fact]
    public void Fatura_numarasi_eski_FT_formatiyla_karismaz()
    {
        // Fatura yeni formatta da harf içeriyor → "tamamı rakam" ayrımı burada işlemez.
        // Ayrım şu: eski numaralar '-' taşır, GİB formatı taşımaz ve tam 16 hanedir.
        var yeni = BelgeNo.FaturaBicimle("RNT", 2026, 1);
        Assert.DoesNotContain('-', yeni);
        Assert.Contains('-', "FT-000108");
    }
}
