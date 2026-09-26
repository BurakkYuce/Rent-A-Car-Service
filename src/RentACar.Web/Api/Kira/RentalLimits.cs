using RentACar.Application.Common;

namespace RentACar.Web.Api.Kira;

/// <summary>
/// F4.1 adversarial L3 — kira uçlarının GİRDİ sınırları (uç katmanı). Amaç iş kuralı değil taşma çiti:
/// <c>numeric(19,4)</c> en fazla 15 tam hane taşır; aşırı büyük tutar (ör. 1e17) ya da tutar × gün çarpımı
/// DB'de 22003 (numeric overflow) üretip 500 dönüyordu, uzun metin 22001 ile aynı. Burada 400 + <c>errors[alan]</c>.
/// <list type="bullet">
/// <item><b>Tutar üst sınırı <see cref="MaxAmount"/> (100.000.000):</b> canlı hesap motorunun
/// (<c>KiraHesapService.MaxGunlukUcret</c>) sınırıyla AYNI — önizleme ile kayıt aynı girdiyi kabul eder.
/// En kötü çarpım: 1e8 × 1.827 gün (5 yıl, TarihPolitikasi.KiraEnUzunYil) × 1,2 KDV ≈ 2,2e11 &lt; 1e15.</item>
/// <item><b>Oranlar</b> (komisyon %, sonra öde %): 0–100 (kolon numeric(9,4)).</item>
/// <item><b>Metin</b>: kolon uzunlukları (servis Lim'i olmayan alanlar — kiralama/faturalama tipi, fiyat türü,
/// döviz, hızlı müşteri alanları).</item>
/// </list>
/// Negatiflik gibi İŞ kuralları servistedir (burada yalnız mutlak büyüklük).
/// </summary>
public static class RentalLimits
{
    public const decimal MaxAmount = 100_000_000m;

    public static void Amount(decimal? value, string alan, string label)
    {
        if (value is { } d && (d > MaxAmount || d < -MaxAmount))
            throw new ValidationException($"{label} en fazla {MaxAmount:N0} olabilir.", alan);
    }

    public static void Rate(decimal? value, string alan, string label)
    {
        if (value is { } d && (d < 0m || d > 100m))
            throw new ValidationException($"{label} 0 ile 100 arasında olmalıdır (%).", alan);
    }

    public static void Text(string? value, int maximum, string alan, string label)
    {
        if (value is { } s && s.Trim().Length > maximum)
            throw new ValidationException($"{label} en fazla {maximum} karakter olabilir.", alan);
    }

    public static void Create(KiraOlusturIstegi i)
    {
        Amount(i.GunlukUcret, "gunlukUcret", "Günlük ücret");
        Amount(i.Provizyon, "provizyon", "Provizyon");
        Amount(i.Depozito, "depozito", "Depozito");
        Amount(i.KomisyonTutar, "komisyonTutar", "Komisyon tutarı");
        Amount(i.DropUcreti, "dropUcreti", "Drop ücreti");
        Amount(i.OpsiyonNet, "opsiyonNet", "Opsiyon net");
        Amount(i.DamgaVergisi, "damgaVergisi", "Damga vergisi");
        Rate(i.KomisyonOran, "komisyonOran", "Komisyon oranı");
        Rate(i.SonraOdeOran, "sonraOdeOran", "Sonra öde oranı");
        Text(i.KiralamaTuru, 64, "kiralamaTuru", "Kiralama türü");
        Text(i.FaturalamaTipi, 64, "faturalamaTipi", "Faturalama tipi");
        Text(i.FiyatTuru, 64, "fiyatTuru", "Fiyat türü");
        Text(i.Doviz, 8, "doviz", "Döviz");
        foreach (var e in i.EkHizmetler ?? [])
            if (e.Miktar is { } m && m > 100_000m)
                throw new ValidationException("Ek hizmet miktarı gerçekçi değil (100.000 üstü).", "ekHizmetler");
    }

    public static void Update(KiraGuncelleIstegi i)
    {
        Amount(i.FazlaKmUcret, "fazlaKmUcret", "Fazla km ücreti");
        Amount(i.YakitBirimUcret, "yakitBirimUcret", "Yakıt birim ücreti");
        Amount(i.Provizyon, "provizyon", "Provizyon");
        Amount(i.Depozito, "depozito", "Depozito");
        Amount(i.KomisyonTutar, "komisyonTutar", "Komisyon tutarı");
        Amount(i.DropUcreti, "dropUcreti", "Drop ücreti");
        Amount(i.OpsiyonNet, "opsiyonNet", "Opsiyon net");
        Amount(i.DamgaVergisi, "damgaVergisi", "Damga vergisi");
        Rate(i.KomisyonOran, "komisyonOran", "Komisyon oranı");
        Rate(i.SonraOdeOran, "sonraOdeOran", "Sonra öde oranı");
    }

    /// <summary>Hızlı müşteri: Customer kolon uzunlukları (CustomerConfigs).</summary>
    public static void Customer(MusteriHizliIstegi i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        Text(i.Soyad, 128, "soyad", "Soyad");
        Text(i.Unvan, 256, "unvan", "Ünvan");
        Text(i.TcKimlik, 11, "tcKimlik", "TC Kimlik No");
        Text(i.CepTel, 32, "cepTel", "Cep telefonu");
        Text(i.Email, 256, "email", "E-posta");
        Text(i.Il, 64, "il", "İl");
        Text(i.Ilce, 64, "ilce", "İlçe");
        Text(i.EhliyetNo, 32, "ehliyetNo", "Ehliyet no");
        Text(i.EhliyetSinifi, 16, "ehliyetSinifi", "Ehliyet sınıfı");
        Text(i.EhliyetYeri, 64, "ehliyetYeri", "Ehliyet yeri");
    }
}
