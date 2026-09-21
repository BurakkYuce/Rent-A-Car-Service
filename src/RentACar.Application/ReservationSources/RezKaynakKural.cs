using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// FAZ-49 — rezervasyon kaynağı KURAL MATRİSİNİN saf (I/O'suz) tüketimi.
///
/// <para><b>Neden ayrı ve saf:</b> kural bir kere burada tanımlanır, 6 giriş noktası (rezervasyon
/// oluştur/güncelle, kira oluştur/güncelle, uzatma, provizyon) aynı gövdeyi çağırır. Kuralı her
/// serviste elle tekrar yazmak, birinde unutulduğunda kuralın "bazen" geçerli olmasına yol açardı.</para>
///
/// <para><b>Yalnız KURAL bayrakları burada.</b> Kaynağın oran/tutar alanları (komisyon, önödeme,
/// indirim, puan, ek hizmet varsayılanları) bu sınıfa hiç girmez — onlar bilgi alanıdır ve hiçbir
/// hesabı etkilemez (docs/KARARLAR.md FAZ-49 + genel politika).</para>
///
/// <para><b>Guard GİRİŞ NOKTASINDA:</b> tüm metotlar servisin başında, kabul/dönüştürme
/// yapılmadan önce çağrılır. Guard'ı akışın ortasına koymak, geçmişte tüm yaşlanmış-kayıt
/// düzenleme akışını kilitlemişti (TarihPolitikasi dersi).</para>
/// </summary>
public static class RezKaynakKural
{
    /// <summary>KURAL: <c>Uzatamaz</c> → uzatma (bitişi ileri alma) reddedilir.</summary>
    public static void UzatmaGuard(ReservationSource? kaynak)
    {
        if (kaynak is { Uzatamaz: true })
            throw new ValidationException(
                $"'{kaynak.Ad}' kaynağının kuralı gereği bu sözleşme uzatılamaz.");
    }

    /// <summary>
    /// KURAL: <c>RezTarihleriDegisemez</c> → rezervasyon tarihleri değiştirilemez.
    /// <b>İKİ kaynak da denetlenir</b> (kayıtlı ve yeni): aksi halde kullanıcı aynı istekte kaynağı
    /// serbest bir kaynağa çevirip tarihi de değiştirerek kuralın etrafından dolaşırdı.
    /// </summary>
    public static void TarihDegisiklikGuard(ReservationSource? mevcutKaynak, ReservationSource? yeniKaynak)
    {
        var engelleyen = mevcutKaynak is { RezTarihleriDegisemez: true } ? mevcutKaynak
                       : yeniKaynak is { RezTarihleriDegisemez: true } ? yeniKaynak : null;
        if (engelleyen is not null)
            throw new ValidationException(
                $"'{engelleyen.Ad}' kaynağının kuralı gereği rezervasyon tarihleri değiştirilemez.");
    }

    /// <summary>KURAL: <c>ProvizyonYok</c> → bu kaynakta provizyon (bloke) alınamaz.</summary>
    public static void ProvizyonGuard(ReservationSource? kaynak)
    {
        if (kaynak is { ProvizyonYok: true })
            throw new ValidationException(
                $"'{kaynak.Ad}' kaynağının kuralı gereği provizyon alınmaz.");
    }

    /// <summary>
    /// KURAL: <c>MaxGun</c> → gün sayısı üst sınırı (null/0 = sınır yok). Oluşturma ve uzatmada
    /// aynı gövde çağrılır; uzatmada TOPLAM gün (yeni bitişe göre) verilir.
    /// </summary>
    public static void MaxGunGuard(ReservationSource? kaynak, int gun)
    {
        if (kaynak?.MaxGun is > 0 && gun > kaynak.MaxGun)
            throw new ValidationException(
                $"'{kaynak.Ad}' kaynağında en fazla {kaynak.MaxGun} gün kiralanabilir (istenen: {gun} gün).");
    }

    /// <summary>
    /// KURAL: <c>AyniYonDrop</c> → tek yön (drop) kapalı; dönüş ofisi çıkış ofisiyle aynı olmalı.
    /// Dönüş ofisi BOŞ ise kural tetiklenmez (boş = "çıkışla aynı" varsayımı; mevcut form davranışı).
    /// </summary>
    public static void DropGuard(ReservationSource? kaynak, string? cikisOfisi, string? donusOfisi)
    {
        if (kaynak is not { AyniYonDrop: true }) return;
        if (string.IsNullOrWhiteSpace(cikisOfisi) || string.IsNullOrWhiteSpace(donusOfisi)) return;
        if (!string.Equals(cikisOfisi.Trim(), donusOfisi.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException(
                $"'{kaynak.Ad}' kaynağında farklı ofise bırakma (drop) yapılamaz; dönüş ofisi çıkış ofisiyle aynı olmalıdır.");
    }

    /// <summary>
    /// KURAL: <c>KmSinirsiz</c> → km limiti 0'a (sınırsız) SABİTLENİR.
    ///
    /// <para>Neden red değil de sabitleme: bayrağın anlamı "bu kaynakta km sınırsızdır" —
    /// sözleşmenin sonucu sınırsız olmalıdır. Form her kayıtta bir km limiti gönderdiğinden red,
    /// kaynağı kullanılamaz hale getirirdi. Yön PARA-GÜVENLİ: <c>ReturnMath</c> yalnız
    /// <c>KmLimit &gt; 0</c> iken fazla km bedeli hesaplar, dolayısıyla müşteriye kaynağın vaat
    /// etmediği bir aşım ücreti çıkmaz.</para>
    /// </summary>
    public static int KmLimitUygula(ReservationSource? kaynak, int istenenKmLimit)
        => kaynak is { KmSinirsiz: true } ? 0 : istenenKmLimit;
}
