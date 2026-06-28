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
}
