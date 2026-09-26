using RentACar.Domain.Enums;

namespace RentACar.Application.RentalRules;

/// <summary>Kiralama kuralı oluştur/güncelle giriş modeli. Kapsam/indirim alanları opsiyonel.</summary>
public sealed class RentalRuleInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public string? AracGrupKod { get; set; }

    public int? MinGun { get; set; }
    public int? MaxGun { get; set; }

    public decimal? Iskonto { get; set; }
    public decimal? HaftaSonuFarkOran { get; set; } // roadmap G3
    public decimal? SonraOdeOran { get; set; }
    public int? HediyeGun { get; set; }
    public bool KampanyaMi { get; set; }
    public string? KampanyaKodu { get; set; }

    /// <summary>Müşteri segmenti kapsamı (FAZ 3.A2; Customer.Sinif ile eşleşir, null = herkes).</summary>
    public string? MusteriSegment { get; set; }

    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }

    public string? SartMetni { get; set; }

    // ---- FAZ-46 ----
    /// <summary>Talebin YAPILDIĞI tarih aralığı (Gecerlilik* kiralama tarihine bakar — farklı kavram).</summary>
    public DateTimeOffset? TalepBas { get; set; }
    public DateTimeOffset? TalepBit { get; set; }
    public PromotionType? PromosyonTuru { get; set; }
    public CouponValidity? KuponGecerlilik { get; set; }
    public CalculationType? HesaplamaTipi { get; set; }
    public bool HizliIslem { get; set; }
    /// <summary>Virgülle ayrılmış 0-6 gün listesi (0=Pazar). Servis normalize eder.</summary>
    public string? HaftaGunKisiti { get; set; }
    /// <summary>FAZ-73 — kampanya arama sınıflandırması. BİLGİ ALANI: fiyat motoruna girmez.</summary>
    public RuleDateType TarihTipi { get; set; } = RuleDateType.Rezervasyon;

    /// <summary>
    /// FAZ-73 — 5 durumlu yaşam döngüsü. <c>null</c> → <see cref="Aktif"/> bayrağından TÜRETİLİR
    /// (true→Aktif, false→Pasif): eski çağıranlar (ve yalnız "aktif mi" bilen yollar) hiç
    /// değişmeden çalışır. Dolu geldiğinde tam tersi geçerlidir — <see cref="Aktif"/> bu değerden
    /// türetilir (<c>Aktif == (KampanyaDurum == Aktif)</c>). İki alan tek noktada senkronlanır.
    /// </summary>
    public CampaignStatus? KampanyaDurum { get; set; }

    public bool Aktif { get; set; } = true;
}

/// <summary>
/// FAZ-73 — kampanya/kural arama filtresi (canlı kampanya_ara.aspx). Yalnız GÖRÜNÜMÜ daraltır;
/// fiyat motoru bu filtreyi hiç görmez (motor <see cref="RentalRuleService.ListActiveAsync"/>
/// kullanır). Tüm alanlar opsiyonel — boş filtre = tam liste.
/// </summary>
public sealed class RentalRuleFilter
{
    /// <summary>Kod/ad içinde geçen metin (harf duyarsız).</summary>
    public string? Terim { get; set; }
    public CampaignStatus? Durum { get; set; }
    public RuleDateType? TarihTipi { get; set; }
    public bool? KampanyaMi { get; set; }
    public string? Kanal { get; set; }
    /// <summary>Geçerlilik aralığı ÇAKIŞMASI: kuralın [Bas,Bit] aralığı verilen aralıkla kesişiyorsa
    /// eşleşir (açık uçlar sonsuz sayılır). "Bu tarihlerde geçerli kampanyalar" sorusunun karşılığı.</summary>
    public DateTimeOffset? GecerliBas { get; set; }
    public DateTimeOffset? GecerliBit { get; set; }
}
