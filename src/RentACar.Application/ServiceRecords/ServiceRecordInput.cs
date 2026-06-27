using RentACar.Domain.Enums;

namespace RentACar.Application.ServiceRecords;

public sealed class ServiceLineInput
{
    public string Aciklama { get; set; } = string.Empty;
    public decimal Tutar { get; set; }
}

public sealed class ServiceRecordInput
{
    public Guid VehicleId { get; set; }
    public ServisTipi Tip { get; set; } = ServisTipi.Periyodik;
    public string? AtolyeAdi { get; set; }
    public DateTimeOffset? GirisTarihi { get; set; }
    public int GirisKm { get; set; }
    public HasarSorumlu HasarSorumlu { get; set; } = HasarSorumlu.Yok;
    public decimal? KusurOrani { get; set; }
    public string? Aciklama { get; set; }
    public List<ServiceLineInput> Lines { get; set; } = [];
}
