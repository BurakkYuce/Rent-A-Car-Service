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
public static class DatePolicy
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    public static void RentalStart(DateTimeOffset start)
    {
        if (start > Now.AddYears(1))
            throw new ValidationException("Kira başlangıcı en fazla 1 yıl ileri tarihli olabilir.");
    }

    /// <summary>
    /// F4.1 adversarial L4: kira bitişi için anti-typo üst sınır — başlangıçtan en fazla <see cref="MaxRentalYears"/>
    /// yıl. Gerekçe: uzun dönem (operasyonel) kiralama sözleşmeleri 36–48 ay sürer; 5 yıl bunları kapsar ama
    /// "9999" gibi yazım hatasını keser (aksi hâlde gün × ücret numeric(19,4)'ü taşırıp 500 üretiyor ve araç
    /// yüzyıllarca bloke oluyordu). Oluşturma ve uzatma AYNI kuralı kullanır.
    /// </summary>
    public const int MaxRentalYears = 5;

    public static void RentalEnd(DateTimeOffset start, DateTimeOffset bit)
    {
        if (bit > start.AddYears(MaxRentalYears))
            throw new ValidationException($"Kira süresi en fazla {MaxRentalYears} yıl olabilir (bitiş tarihini kontrol edin).");
    }

    /// <summary>F4.1 adversarial L4: gerçek dönüş en fazla 1 yıl ileri (kira başlangıcıyla aynı anti-typo tamponu).
    /// Geç dönüş gününü ve bedelini sınırlar (int/numeric taşması yok).</summary>
    public static void ActualReturn(DateTimeOffset returnInfo)
    {
        if (returnInfo > Now.AddYears(1))
            throw new ValidationException("Gerçek dönüş tarihi en fazla 1 yıl ileri olabilir.");
    }

    public static void ReservationStart(DateTimeOffset start)
    {
        if (start < Now.AddDays(-1))
            throw new ValidationException("Rezervasyon geçmiş tarihe alınamaz.");
        if (start > Now.AddYears(1))
            throw new ValidationException("Rezervasyon en fazla 1 yıl ileri alınabilir.");
    }

    /// <summary>F5.1 adversarial M2 — belge/sözleşme tarihleri için makul alt sınır (yazım hatası "0001" kesilir).</summary>
    public static readonly DateTimeOffset EarliestDocumentDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// F5.1 adversarial M2 — filo (uzun dönem) kiralama başlangıcı: kira gibi GEÇMİŞE AÇIK (retroaktif giriş) ama
    /// <see cref="EarliestDocumentDate"/>'nden önce olamaz; gelecek ≤ +1 yıl (kira başlangıcıyla aynı anti-typo tamponu).
    /// Süre ≤ 120 ay ile son taksit vadesi en geç ~11 yıl ileridedir — "9999" başlangıç taksit planında
    /// <c>AddMonths</c>'ı taşırıp tüm kiracının filo listesini 500'e düşürüyordu.
    /// </summary>
    public static void FleetStart(DateTimeOffset start, string alan = "basTar")
    {
        if (start < EarliestDocumentDate)
            throw new ValidationException("Filo kiralama başlangıcı 2000 yılından önce olamaz.", alan);
        if (start > Now.AddYears(1))
            throw new ValidationException("Filo kiralama başlangıcı en fazla 1 yıl ileri tarihli olabilir.", alan);
    }

    /// <summary>F5.1 adversarial M2 — sözleşme/imza gibi bilgi tarihleri: [2000, bugün + 1 yıl].</summary>
    public static void DocumentDate(DateTimeOffset? date, string alan, string label)
    {
        if (date is not { } t) return;
        if (t < EarliestDocumentDate)
            throw new ValidationException($"{label} 2000 yılından önce olamaz.", alan);
        if (t > Now.AddYears(1))
            throw new ValidationException($"{label} en fazla 1 yıl ileri tarihli olabilir.", alan);
    }

    public static void BirthDate(DateTimeOffset? birth)
    {
        if (birth is { } d && d > Now)
            throw new ValidationException("Doğum tarihi gelecekte olamaz.");
    }

    public static void MoneyDate(DateTimeOffset? date, string alan)
    {
        if (date is { } t && t > Now.AddDays(1))
            throw new ValidationException($"{alan} tarihi gelecekte olamaz.");
    }
}
