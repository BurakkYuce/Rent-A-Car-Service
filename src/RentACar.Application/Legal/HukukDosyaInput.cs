using RentACar.Domain.Enums;

namespace RentACar.Application.Legal;

public sealed class HukukDosyaInput
{
    public string? DosyaNo { get; set; }
    public Guid? CariId { get; set; }
    public HukukTuru Tur { get; set; } = HukukTuru.Dava;
    public string? Avukat { get; set; }
    public decimal Tutar { get; set; }
    public HukukDurum Durum { get; set; } = HukukDurum.Acik;
    public DateTimeOffset? Tarih { get; set; }
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;

    // ---- FAZ-41 derinlik ----
    /// <summary>Dosyaya konu fatura no (serbest metin; fatura tablosuna FK değil).</summary>
    public string? FaturaNoTemp { get; set; }
    public string? AvukatTel { get; set; }
    public string? AvukatMail { get; set; }
    public string? Avukat2Ad { get; set; }
    public string? Avukat2Tel { get; set; }
    public string? Avukat2Mail { get; set; }

    /// <summary>
    /// Tahsil edildiği BİLDİRİLEN tutar — BİLGİ ALANI, deftere/cari bakiyeye yazmaz
    /// (bkz. <see cref="Domain.Entities.HukukDosya.Tahsilat"/>).
    /// </summary>
    public decimal? Tahsilat { get; set; }
}
