using RentACar.Domain.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// Belge numarası biçim kuralları (saf birim — DB yok).
///
/// <para><b>Bağımsız oracle:</b> beklenen dizeler kullanıcının verdiği örnekten ve elle sayımdan
/// gelir, <see cref="DocumentNo"/>'nun çıktısından türetilmez.</para>
///
/// <para><b>Tip kodu testi bir SÖZLEŞMEDİR:</b> kod bir kez kesilmiş belgeye yazıldıktan sonra
/// değiştirilemez (numara mali kayıtta, PDF'te, paylaşım linkinde ve müşteri mesajında geçer).
/// Bu test kodları kilitler — değiştiren biri kırmızıya çarpar.</para>
/// </summary>
public sealed class BelgeNoTests
{
    private static readonly DateOnly Sample = new(2026, 8, 26);   // 26 Ağustos 2026

    [Fact]
    public void Kullanicinin_verdigi_ornek()
    {
        // Kullanıcının mesajındaki birebir örnek: "sözleşme no : 2026260801001"
        //   2026 | 26 (gün) | 08 (ay) | 01 (kira sözleşmesi) | 001 (günün ilk belgesi)
        Assert.Equal("2026260801001", DocumentNo.Format(DocumentNoType.KiraSozlesmesi, Sample, 1));
    }

    [Fact]
    public void Ayni_gun_ikinci_belge()
        => Assert.Equal("2026260801002", DocumentNo.Format(DocumentNoType.KiraSozlesmesi, Sample, 2));

    [Fact]
    public void Farkli_tip_ayni_gun_kendi_sirasindan_baslar()
        => Assert.Equal("2026260802001", DocumentNo.Format(DocumentNoType.Rezervasyon, Sample, 1));

    [Fact]
    public void Tek_haneli_gun_ve_ay_sifirla_doldurulur()
        // 1 Ocak 2027, tahsilat (05), 7. belge → 2027 01 01 05 007
        => Assert.Equal("2027010105007", DocumentNo.Format(DocumentNoType.Tahsilat, new DateOnly(2027, 1, 1), 7));

    [Fact]
    public void Numara_13_hane_ve_tamami_rakam()
    {
        var no = DocumentNo.Format(DocumentNoType.Gider, Sample, 42);
        Assert.Equal(13, no.Length);
        Assert.All(no, c => Assert.InRange(c, '0', '9'));
    }

    [Fact]
    public void Tasma_999_ustu_14_haneye_cikar()
    {
        // {sira:D3} MİNİMUM 3 hane demek; 1000 doğal olarak "1000" yazar. Elle sayıldı: 2026|26|08|01|1000
        Assert.Equal("20262608011000", DocumentNo.Format(DocumentNoType.KiraSozlesmesi, Sample, 1000));
        Assert.Equal(14, DocumentNo.Format(DocumentNoType.KiraSozlesmesi, Sample, 1000).Length);
    }

    [Fact]
    public void Sifir_veya_negatif_sira_reddedilir()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentNo.Format(DocumentNoType.Gider, Sample, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentNo.Format(DocumentNoType.Gider, Sample, -1));
    }

    [Fact]
    public void Sayac_anahtari_tip_ve_gunu_tasir()
    {
        // Anahtar yyyyMMdd taşır (numaradaki yyyyddMM'den FARKLI): anahtar insan için değil,
        // gün başına tek satır üretmek için — sıralanabilir olması tercih edilir.
        Assert.Equal("01:20260826", DocumentNo.CounterKey(DocumentNoType.KiraSozlesmesi, Sample));
        Assert.Equal("05:20260826", DocumentNo.CounterKey(DocumentNoType.Tahsilat, Sample));
        Assert.Equal("17:20270101", DocumentNo.CounterKey(DocumentNoType.MaliyetTeklifi, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Sayac_anahtari_TenantSequences_Name_kolonuna_sigar()
        => Assert.True(DocumentNo.CounterKey(DocumentNoType.MaliyetTeklifi, Sample).Length <= 64);

    // ───────────────────────── Tip kodu sözleşmesi ─────────────────────────

    [Theory]
    [InlineData(DocumentNoType.KiraSozlesmesi, 1)]
    [InlineData(DocumentNoType.Rezervasyon, 2)]
    [InlineData(DocumentNoType.Teklif, 3)]
    [InlineData(DocumentNoType.Fatura, 4)]
    [InlineData(DocumentNoType.Tahsilat, 5)]
    [InlineData(DocumentNoType.Tediye, 6)]
    [InlineData(DocumentNoType.Gider, 7)]
    [InlineData(DocumentNoType.Ceza, 8)]
    [InlineData(DocumentNoType.ServisKaydi, 9)]
    [InlineData(DocumentNoType.DisHizmet, 10)]
    [InlineData(DocumentNoType.HasarDosyasi, 11)]
    [InlineData(DocumentNoType.Baf, 12)]
    [InlineData(DocumentNoType.AracSiparis, 13)]
    [InlineData(DocumentNoType.AracSatis, 14)]
    [InlineData(DocumentNoType.AracKredi, 15)]
    [InlineData(DocumentNoType.FiloKiralama, 16)]
    [InlineData(DocumentNoType.MaliyetTeklifi, 17)]
    public void Tip_kodlari_KALICI(DocumentNoType type, int expectedCode)
        => Assert.Equal(expectedCode, (int)type);

    [Fact]
    public void Tip_kodlari_benzersiz_ve_gecerli_aralikta()
    {
        var codes = Enum.GetValues<DocumentNoType>().Select(t => (int)t).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, k => Assert.InRange(k, 1, 99));
    }

    [Fact]
    public void HasarDosyasi_ve_Baf_ayri_kod_alir()
        // Eski düzende ikisi de "BAF-" üretiyordu (ayrı sayaç, aynı görünen numara).
        => Assert.NotEqual((int)DocumentNoType.HasarDosyasi, (int)DocumentNoType.Baf);

    // ───────────────────────── Fatura: GİB formatı ─────────────────────────

    [Fact]
    public void Fatura_GIB_formati_16_hane()
    {
        // Mevzuat: seri(3) + yıl(4) + sıra(9). Elle sayıldı: RNT + 2026 + 000000001
        var no = DocumentNo.FormatInvoice("RNT", 2026, 1);
        Assert.Equal("RNT2026000000001", no);
        Assert.Equal(16, no.Length);
    }

    [Fact]
    public void Fatura_sirasi_dokuz_haneye_doldurulur()
        => Assert.Equal("ABC2026000012345", DocumentNo.FormatInvoice("ABC", 2026, 12345));

    [Fact]
    public void Fatura_sayac_anahtari_seri_ve_yil_basina()
    {
        // Sıra HER YIL 1'den başlar ve her seri kendi içinde ilerler → anahtar ikisini de taşımalı.
        Assert.Equal("F:RNT:2026", DocumentNo.InvoiceCounterKey("RNT", 2026));
        Assert.NotEqual(DocumentNo.InvoiceCounterKey("RNT", 2026), DocumentNo.InvoiceCounterKey("RNT", 2027));
        Assert.NotEqual(DocumentNo.InvoiceCounterKey("RNT", 2026), DocumentNo.InvoiceCounterKey("ABC", 2026));
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
    public void Seri_kodu_dogrulamasi(string? series, bool valid)
        => Assert.Equal(valid, DocumentNo.IsSeriesValid(series));

    [Fact]
    public void Gecersiz_seri_ile_fatura_numarasi_uretilmez()
        => Assert.Throws<ArgumentException>(() => DocumentNo.FormatInvoice("rnt", 2026, 1));

    // ───────────────────────── Eski/yeni birlikte yaşar ─────────────────────────

    [Fact]
    public void Eski_format_yeni_formatla_ASLA_cakismaz()
    {
        // Eski numaralar HARF içerir (KS-000001, FT-000042); genel yeni desen TAMAMI RAKAM.
        // İki uzay kesişemediği için (TenantId, No) unique indeksleri güvende → geriye dönük
        // yeniden numaralandırma gerekmez (ki rc_prevent_mutation zaten buna izin vermezdi).
        string[] oldOnes = ["KS-000001", "RZ-000042", "FT-000108", "TH-000007", "BAF-000003"];
        Assert.All(oldOnes, e => Assert.Contains(e, c => char.IsLetter(c)));

        foreach (var type in Enum.GetValues<DocumentNoType>())
        {
            if (type == DocumentNoType.Fatura) continue;               // fatura GİB formatında (harf içerir)
            var newItem = DocumentNo.Format(type, Sample, 1);
            Assert.DoesNotContain(newItem, char.IsLetter);
            Assert.DoesNotContain(newItem, oldOnes);
        }
    }

    [Fact]
    public void Fatura_numarasi_eski_FT_formatiyla_karismaz()
    {
        // Fatura yeni formatta da harf içeriyor → "tamamı rakam" ayrımı burada işlemez.
        // Ayrım şu: eski numaralar '-' taşır, GİB formatı taşımaz ve tam 16 hanedir.
        var newItem = DocumentNo.FormatInvoice("RNT", 2026, 1);
        Assert.DoesNotContain('-', newItem);
        Assert.Contains('-', "FT-000108");
    }
}
