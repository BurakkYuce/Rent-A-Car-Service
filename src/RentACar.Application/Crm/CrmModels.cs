using RentACar.Domain.Enums;

namespace RentACar.Application.Crm;

public sealed class AnketInput
{
    public Guid? CariId { get; set; }
    public int Puan { get; set; }
    public string? Yorum { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? Kaynak { get; set; }
}

public sealed class SikayetInput
{
    public Guid? CariId { get; set; }
    public string? Konu { get; set; }
    public string? Detay { get; set; }
    public SikayetDurum Durum { get; set; } = SikayetDurum.Acik;
    public DateTimeOffset? Tarih { get; set; }
    public string? Cozum { get; set; }
}
