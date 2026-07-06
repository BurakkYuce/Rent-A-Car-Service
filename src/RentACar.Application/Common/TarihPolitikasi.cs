namespace RentACar.Application.Common;

/// <summary>
/// Tarih giriş politikası — TEK KAYNAK (server-side asıl koruma; formdaki min/max yalnız UX yardımı, bypass
/// edilebilir). Kararlar kullanıcı onayıyla (2026-07-06):
/// - Kira başlangıç: GEÇMİŞE AÇIK (retroaktif giriş serbest); gelecek yalnız anti-typo tampon (+1 yıl) —
///   "bugün hazırla, yarın çıkar" akışını kilitlememek için 'bugün' DEĞİL. Bitiş &gt; başlangıç zaten
///   BookingMath.Validate'te.
/// - Rezervasyon başlangıç: GEÇMİŞE KAPALI (dün için rez saçma; 1 günlük TZ toleransı ile yanlış-red önlenir);
///   gelecek ≤ +1 yıl (ne kadar ileri rezervasyon).
/// - Doğum: gelecekte olamaz.
/// - Para tarihi (tahsilat/gider — form alanı YOK, now'a düşer; bu yalnız savunma): gelecekte olamaz
///   (1 günlük TZ toleransı). Geçmiş dönem-kilidiyle ayrıca korunur.
/// Sınırlar DateTimeOffset.UtcNow'a görelidir; girişler timestamptz-UTC normalize (FormParse.Date).
/// </summary>
public static class TarihPolitikasi
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    public static void KiraBaslangic(DateTimeOffset bas)
    {
        if (bas > Now.AddYears(1))
            throw new ValidationException("Kira başlangıcı en fazla 1 yıl ileri tarihli olabilir.");
    }

    public static void RezervasyonBaslangic(DateTimeOffset bas)
    {
        if (bas < Now.AddDays(-1))
            throw new ValidationException("Rezervasyon geçmiş tarihe alınamaz.");
        if (bas > Now.AddYears(1))
            throw new ValidationException("Rezervasyon en fazla 1 yıl ileri alınabilir.");
    }

    public static void DogumTarihi(DateTimeOffset? dogum)
    {
        if (dogum is { } d && d > Now)
            throw new ValidationException("Doğum tarihi gelecekte olamaz.");
    }

    public static void ParaTarihi(DateTimeOffset? tarih, string alan)
    {
        if (tarih is { } t && t > Now.AddDays(1))
            throw new ValidationException($"{alan} tarihi gelecekte olamaz.");
    }
}
