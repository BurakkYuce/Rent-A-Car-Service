namespace RentACar.Application.Baflar;

/// <summary>BAF (personel araç tahsis) oluşturma girişi (roadmap L5).</summary>
public sealed class BafInput
{
    public Guid PersonelId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset? CikisTarihi { get; set; }
    public int CikisKm { get; set; }
    public int? CikisYakit { get; set; }
    public string? Sube { get; set; }
    public string? Aciklama { get; set; }

    // ---- FAZ-18: çıkış anında girilen bilgi alanları (defter postlamaz) ----
    public Domain.Enums.BafKullanimAmaci? KullanimAmaci { get; set; }
    /// <summary>Onaylayan personel (Personel.Id) — BİLGİ; yetki kontrolü bu alandan YAPILMAZ.</summary>
    public Guid? Onaylayan { get; set; }
    public bool KirayaVer { get; set; }
    public TimeOnly? CikisSaat { get; set; }
    /// <summary>F6.1b — <c>/api/ui</c> çift gönderim anahtarı; doluysa kaydın Id'si olur (ikinci oluşturma → 409).</summary>
    public Guid? IslemAnahtari { get; set; }
}
