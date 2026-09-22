using RentACar.Application.Common;

namespace RentACar.Web.Api.Kira;

/// <summary>
/// F4.1 adversarial L3 — kira uçlarının GİRDİ sınırları (uç katmanı). Amaç iş kuralı değil taşma çiti:
/// <c>numeric(19,4)</c> en fazla 15 tam hane taşır; aşırı büyük tutar (ör. 1e17) ya da tutar × gün çarpımı
/// DB'de 22003 (numeric overflow) üretip 500 dönüyordu, uzun metin 22001 ile aynı. Burada 400 + <c>errors[alan]</c>.
/// <list type="bullet">
/// <item><b>Tutar üst sınırı <see cref="EnFazlaTutar"/> (100.000.000):</b> canlı hesap motorunun
/// (<c>KiraHesapService.MaxGunlukUcret</c>) sınırıyla AYNI — önizleme ile kayıt aynı girdiyi kabul eder.
/// En kötü çarpım: 1e8 × 1.827 gün (5 yıl, TarihPolitikasi.KiraEnUzunYil) × 1,2 KDV ≈ 2,2e11 &lt; 1e15.</item>
/// <item><b>Oranlar</b> (komisyon %, sonra öde %): 0–100 (kolon numeric(9,4)).</item>
/// <item><b>Metin</b>: kolon uzunlukları (servis Lim'i olmayan alanlar — kiralama/faturalama tipi, fiyat türü,
/// döviz, hızlı müşteri alanları).</item>
/// </list>
/// Negatiflik gibi İŞ kuralları servistedir (burada yalnız mutlak büyüklük).
/// </summary>
public static class Sinirlar
{
    public const decimal EnFazlaTutar = 100_000_000m;

    public static void Tutar(decimal? deger, string alan, string etiket)
    {
        if (deger is { } d && (d > EnFazlaTutar || d < -EnFazlaTutar))
            throw new ValidationException($"{etiket} en fazla {EnFazlaTutar:N0} olabilir.", alan);
    }

    public static void Oran(decimal? deger, string alan, string etiket)
    {
        if (deger is { } d && (d < 0m || d > 100m))
            throw new ValidationException($"{etiket} 0 ile 100 arasında olmalıdır (%).", alan);
    }

    public static void Metin(string? deger, int enFazla, string alan, string etiket)
    {
        if (deger is { } s && s.Trim().Length > enFazla)
            throw new ValidationException($"{etiket} en fazla {enFazla} karakter olabilir.", alan);
    }

    public static void Olustur(KiraOlusturIstegi i)
    {
        Tutar(i.GunlukUcret, "gunlukUcret", "Günlük ücret");
        Tutar(i.Provizyon, "provizyon", "Provizyon");
        Tutar(i.Depozito, "depozito", "Depozito");
        Tutar(i.KomisyonTutar, "komisyonTutar", "Komisyon tutarı");
        Tutar(i.DropUcreti, "dropUcreti", "Drop ücreti");
        Tutar(i.OpsiyonNet, "opsiyonNet", "Opsiyon net");
        Tutar(i.DamgaVergisi, "damgaVergisi", "Damga vergisi");
        Oran(i.KomisyonOran, "komisyonOran", "Komisyon oranı");
        Oran(i.SonraOdeOran, "sonraOdeOran", "Sonra öde oranı");
        Metin(i.KiralamaTuru, 64, "kiralamaTuru", "Kiralama türü");
        Metin(i.FaturalamaTipi, 64, "faturalamaTipi", "Faturalama tipi");
        Metin(i.FiyatTuru, 64, "fiyatTuru", "Fiyat türü");
        Metin(i.Doviz, 8, "doviz", "Döviz");
        foreach (var e in i.EkHizmetler ?? [])
            if (e.Miktar is { } m && m > 100_000m)
                throw new ValidationException("Ek hizmet miktarı gerçekçi değil (100.000 üstü).", "ekHizmetler");
    }

    public static void Guncelle(KiraGuncelleIstegi i)
    {
        Tutar(i.FazlaKmUcret, "fazlaKmUcret", "Fazla km ücreti");
        Tutar(i.YakitBirimUcret, "yakitBirimUcret", "Yakıt birim ücreti");
        Tutar(i.Provizyon, "provizyon", "Provizyon");
        Tutar(i.Depozito, "depozito", "Depozito");
        Tutar(i.KomisyonTutar, "komisyonTutar", "Komisyon tutarı");
        Tutar(i.DropUcreti, "dropUcreti", "Drop ücreti");
        Tutar(i.OpsiyonNet, "opsiyonNet", "Opsiyon net");
        Tutar(i.DamgaVergisi, "damgaVergisi", "Damga vergisi");
        Oran(i.KomisyonOran, "komisyonOran", "Komisyon oranı");
        Oran(i.SonraOdeOran, "sonraOdeOran", "Sonra öde oranı");
    }

    /// <summary>Hızlı müşteri: Customer kolon uzunlukları (CustomerConfigs).</summary>
    public static void Musteri(MusteriHizliIstegi i)
    {
        Metin(i.Ad, 128, "ad", "Ad");
        Metin(i.Soyad, 128, "soyad", "Soyad");
        Metin(i.Unvan, 256, "unvan", "Ünvan");
        Metin(i.TcKimlik, 11, "tcKimlik", "TC Kimlik No");
        Metin(i.CepTel, 32, "cepTel", "Cep telefonu");
        Metin(i.Email, 256, "email", "E-posta");
        Metin(i.Il, 64, "il", "İl");
        Metin(i.Ilce, 64, "ilce", "İlçe");
        Metin(i.EhliyetNo, 32, "ehliyetNo", "Ehliyet no");
        Metin(i.EhliyetSinifi, 16, "ehliyetSinifi", "Ehliyet sınıfı");
        Metin(i.EhliyetYeri, 64, "ehliyetYeri", "Ehliyet yeri");
    }
}
