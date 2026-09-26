using RentACar.Application.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// Blog gövdesi ayrıştırma — <c>##</c>/<c>###</c> ara başlıkları + meta açıklama özeti.
/// Saf fonksiyonlar, DB gerektirmez. Beklenen değerler ELLE kurulur.
///
/// <para><b>Tasarım kilidi:</b> ayrıştırıcı HTML ÜRETMEZ, yalnız blok TÜRÜ döndürür. Razor bu türe
/// göre h2/h3/p etiketini seçer ve metni her hâlükârda encode eder. Tam Markdown desteklemek
/// (bağlantı, ham HTML) <c>MarkupString</c> gerektirir ve <c>IcerikMetni</c>'nin "XSS risk sınıfı
/// tasarım gereği yoktur" güvencesini bozardı — bu yüzden yalnız iki işaret desteklenir.</para>
/// </summary>
public sealed class IcerikMetniBlokTests
{
    [Fact]
    public void Iki_diyez_h2_uc_diyez_h3_uretir()
    {
        var blocks = ContentText.Blocks("Giriş cümlesi.\n\n## Ana Başlık\n\n### Alt Başlık\n\nSon söz.");

        Assert.Equal(4, blocks.Count);
        Assert.Equal(ContentText.BlockType.Paragraf, blocks[0].Tur);
        Assert.Equal("Giriş cümlesi.", blocks[0].Metin);
        Assert.Equal(ContentText.BlockType.Baslik2, blocks[1].Tur);
        Assert.Equal("Ana Başlık", blocks[1].Metin);       // işaretleyici AYIKLANIR
        Assert.Equal(ContentText.BlockType.Baslik3, blocks[2].Tur);
        Assert.Equal("Alt Başlık", blocks[2].Metin);
        Assert.Equal(ContentText.BlockType.Paragraf, blocks[3].Tur);
    }

    /// <summary>
    /// İşaret YALNIZ satır başında geçerli. Cümle ortasındaki "##" başlık üretirse yazının metni
    /// bozulur — okuyucu paragrafın yarısını başlık olarak görür.
    /// </summary>
    [Fact]
    public void Satir_ortasindaki_diyez_baslik_URETMEZ()
    {
        var block = Assert.Single(ContentText.Blocks("Fiyat ## dahil değildir."));
        Assert.Equal(ContentText.BlockType.Paragraf, block.Tur);
        Assert.Equal("Fiyat ## dahil değildir.", block.Metin);
    }

    /// <summary>Diyezden sonra boşluk YOKSA başlık değildir ("#etiket" gibi kullanımlar korunur).</summary>
    [Fact]
    public void Bosluksuz_diyez_baslik_URETMEZ()
    {
        var block = Assert.Single(ContentText.Blocks("##EtiketGibi"));
        Assert.Equal(ContentText.BlockType.Paragraf, block.Tur);
    }

    /// <summary>Aynı blok içindeki ardışık satırlar TEK paragrafta birleşir (yazarın niyeti).</summary>
    [Fact]
    public void Ayni_bloktaki_satirlar_tek_paragrafta_birlesir()
    {
        var block = Assert.Single(ContentText.Blocks("Birinci satır\nikinci satır"));
        Assert.Equal("Birinci satır ikinci satır", block.Metin);
    }

    [Fact]
    public void Bos_metin_bos_liste_doner()
    {
        Assert.Empty(ContentText.Blocks(null));
        Assert.Empty(ContentText.Blocks("   "));
    }

    /// <summary>
    /// Mevcut <see cref="ContentText.Paragraphs"/> DEĞİŞMEDİ — SSS cevabı ve içerik sayfası onu
    /// kullanmaya devam ediyor; ara başlık davranışı oraya sızmamalı.
    /// </summary>
    [Fact]
    public void Paragraflar_davranisi_KORUNUR()
    {
        var p = ContentText.Paragraphs("## Başlık gibi\n\nikinci");
        Assert.Equal(2, p.Count);
        Assert.Equal("## Başlık gibi", p[0]);   // işaretleyici AYIKLANMAZ (eski davranış)
    }

    // ---------------------------------------------------------------- meta açıklama özeti

    /// <summary>Özet işaretleyicileri ayıklar ve blokları düz metin olarak birleştirir.</summary>
    [Fact]
    public void Ozet_isaretleyicisiz_duz_metin_uretir()
        => Assert.Equal("Giriş. Ana Başlık Devamı.",
            ContentText.Summary("Giriş.\n\n## Ana Başlık\n\nDevamı."));

    /// <summary>
    /// Uzun metin KELİME ortasından kesilmez. Bağımsız oracle: 20 karakter sınırında
    /// "Antalya bölgesinde uzun dönem" metni "Antalya bölgesinde…" olmalı — "uzun" yarım kalmamalı.
    /// </summary>
    [Fact]
    public void Ozet_kelime_ortasindan_kesmez()
    {
        var summary = ContentText.Summary("Antalya bölgesinde uzun dönem araç kiralama", 20);
        Assert.Equal("Antalya bölgesinde…", summary);
        Assert.DoesNotContain("uzu…", summary);
    }

    [Fact]
    public void Ozet_sinirin_altindaki_metni_oldugu_gibi_doner()
        => Assert.Equal("Kısa yazı.", ContentText.Summary("Kısa yazı.", 160));

    [Fact]
    public void Ozet_bos_metinde_null_doner()
        => Assert.Null(ContentText.Summary("   "));

    // ---------------------------------------------------------------- kelime sayısı (JSON-LD wordCount)

    /// <summary>Ara başlık İŞARETLERİ sayıma girmez; başlığın KELİMELERİ girer (metnin parçası).
    /// Bağımsız oracle: "Giriş" + "Ana Başlık" (2) + "Son" = 4 kelime.</summary>
    [Fact]
    public void KelimeSayisi_isaretleyicileri_saymaz()
        => Assert.Equal(4, ContentText.WordCount("Giriş\n\n## Ana Başlık\n\nSon"));

    [Fact]
    public void KelimeSayisi_bos_metinde_sifir()
        => Assert.Equal(0, ContentText.WordCount(null));
}
