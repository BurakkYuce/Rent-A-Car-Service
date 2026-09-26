using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kaydedilmiş filo maliyet teklifi (FAZ-74; canlı <c>maliyet_hesaplama_ara.aspx</c>).
///
/// <para><b>DEFTERE YAZMAZ — MALİ BELGE DEĞİLDİR.</b> Bu bir simülasyon/planlama çıktısıdır;
/// gelir, gider, cari bakiye ya da araç karnesi rakamlarını HİÇBİR şekilde etkilemez. Gerçek para
/// hareketi Kasa/Banka tahsilat-ödeme akışından geçer (KARARLAR.md "GENEL POLİTİKA — yeni tutar
/// alanları deftere yazmaz"). Bu yüzden değişmezlik trigger'ı da yoktur: teklif düzeltilebilir ve
/// silinebilir olmalıdır.</para>
///
/// <para><b>SNAPSHOT ZORUNLU:</b> hem GİRDİ hem SONUÇ alanları satıra yazılır. Yalnız girdiyi
/// saklayıp sonucu her okumada yeniden hesaplamak, formül/varsayılan değiştiği gün geçmiş
/// tekliflerin sessizce kaymasına yol açardı (KdvOranSnapshot deseniyle aynı prensip).</para>
/// </summary>
public class MaliyetTeklifi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Boşluksuz sıra no (tenant başına): <c>MT-000001</c>.</summary>
    public string KayitNo { get; set; } = string.Empty;

    public string Baslik { get; set; } = string.Empty;
    /// <summary>Serbest metin plaka/araç künyesi (teklif aşamasında araç henüz olmayabilir).</summary>
    public string? Plaka { get; set; }
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Teklifin verildiği cari (opsiyonel).</summary>
    public Guid? CariId { get; set; }
    /// <summary>Teklifi hazırlayan personel (opsiyonel).</summary>
    public Guid? HazirlayanId { get; set; }

    public string? Aciklama { get; set; }

    // ---------------- GİRDİ SNAPSHOT ----------------
    public decimal AlisBedeli { get; set; }
    public decimal ResidualYuzde { get; set; }
    public int SureAy { get; set; }
    public decimal FaizOran { get; set; }
    public decimal KkdfOran { get; set; }
    public decimal BsmvOran { get; set; }
    public decimal DamgaOran { get; set; }
    public decimal KarMarji { get; set; }
    public decimal KdvOran { get; set; }
    public decimal EnflasyonOran { get; set; }
    public LoanCalculationMethod KrediHesaplamaSekli { get; set; } = LoanCalculationMethod.EsitTaksitli;
    public int AracSayisi { get; set; } = 1;

    // Gider kalemleri (girdi snapshot'ı) — dökümü yeniden üretmeye yeter.
    public decimal KaskoYillik { get; set; }
    public decimal TrafikSigortasiYillik { get; set; }
    public decimal MtvYillik { get; set; }
    public decimal BakimYillik { get; set; }
    public decimal LastikYillik { get; set; }
    public decimal LastikKisYillik { get; set; }
    public decimal AracTakipYillik { get; set; }
    public decimal TescilPlakaYillik { get; set; }
    public decimal MuayeneEmisyonYillik { get; set; }
    public decimal YedekAracYillik { get; set; }
    public decimal YonetimGideriAylik { get; set; }
    public decimal AylikGider { get; set; }
    public decimal BankaDosyaDigerMasraf { get; set; }

    // ---------------- SONUÇ SNAPSHOT (araç başına) ----------------
    public decimal ResidualDeger { get; set; }
    public decimal NetAmortisman { get; set; }
    public decimal FinansmanFaiz { get; set; }
    public decimal FinansmanVergi { get; set; }
    public decimal Damga { get; set; }
    public decimal ToplamGider { get; set; }
    public decimal ToplamMaliyet { get; set; }
    public decimal BasaBasAylik { get; set; }
    public decimal Kar { get; set; }
    public decimal TeklifNet { get; set; }
    public decimal TeklifAylikNet { get; set; }
    public decimal TeklifKdvli { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Filo toplamları TÜRETİLİR (kolon değil) — araç-başı snapshot × adet. Kolona yazılsaydı
    // adet ile toplamın birbirini tutmadığı satırlar üretilebilirdi.
    public decimal FiloToplamMaliyet => ToplamMaliyet * AracSayisi;
    public decimal FiloTeklifAylikNet => TeklifAylikNet * AracSayisi;
    public decimal FiloTeklifKdvli => TeklifKdvli * AracSayisi;
}
