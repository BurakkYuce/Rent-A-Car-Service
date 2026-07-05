namespace RentACar.Domain.Entities;

/// <summary>
/// Günlük TCMB döviz kuru kaydı. PAYLAŞIMLI/ulusal veri — tenant-owned DEĞİL (RLS yok, TenantId yok):
/// tüm tenant'lar aynı TCMB kurunu okur. <c>TcmbKurJob</c> günlük çeker (public feed, kimliksiz).
/// Değerler HAM TCMB değeri (Birim kadar döviz için); <c>KurService</c> Birim'e bölerek 1 birim TL
/// karşılığını verir. Firma kendi kurunu isterse <see cref="SabitKur"/> ile ezer.
/// </summary>
public class KurKaydi
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Kurun geçerli olduğu gün (UTC gün başı — saat 00:00Z).</summary>
    public DateTimeOffset Tarih { get; set; }

    /// <summary>ISO kod (USD/EUR/GBP…), büyük harf.</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>TCMB Unit — değerin kaç birim için olduğu (genelde 1; JPY=100). 1 birim = değer/Birim.</summary>
    public int Birim { get; set; } = 1;

    // TCMB kur türleri (bazı dövizlerde forex boş olabilir → nullable).
    public decimal? ForexAlis { get; set; }    // Döviz Alış (ForexBuying)
    public decimal? ForexSatis { get; set; }   // Döviz Satış (ForexSelling)
    public decimal? EfektifAlis { get; set; }  // Efektif Alış (BanknoteBuying)
    public decimal? EfektifSatis { get; set; } // Efektif Satış (BanknoteSelling)

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
