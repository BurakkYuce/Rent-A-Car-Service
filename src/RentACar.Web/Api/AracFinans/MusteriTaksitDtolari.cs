using RentACar.Domain.Entities;

namespace RentACar.Web.Api.AracFinans;

/// <summary><c>POST /musteri-taksitleri</c> ve <c>PUT /musteri-taksitleri/{id}</c> (PUT'ta <c>surum</c> zorunlu). Takip kaydı —
/// DEFTERE YAZMAZ. <c>kur</c>: TRY'de boş ya da 1; dövizde boş → otomatik kur.</summary>
public sealed record MusteriTaksitIstegi
{
    public Guid CariId { get; init; }
    public Guid? VehicleId { get; init; }
    public Guid? VehicleSaleId { get; init; }
    public DateTimeOffset? Vade { get; init; }
    public decimal TaksitTutari { get; init; }
    public string? Doviz { get; init; }
    public decimal? Kur { get; init; }
    /// <summary>"Bekliyor" | "Odendi" (boş → Bekliyor).</summary>
    public string? Durum { get; init; }
    public DateTimeOffset? OdemeTarihi { get; init; }
    public string? Aciklama { get; init; }
    public string? Surum { get; init; }
}

/// <summary><c>POST /musteri-taksitleri/plan</c> — toplam N eşit aylık taksite bölünür (son taksit kalanı emer).</summary>
public sealed record TaksitPlanIstegi
{
    public Guid CariId { get; init; }
    public Guid? VehicleId { get; init; }
    public Guid? VehicleSaleId { get; init; }
    public decimal ToplamTutar { get; init; }
    public int TaksitSayisi { get; init; }
    public DateTimeOffset? IlkVade { get; init; }
    public string? Doviz { get; init; }
    public decimal? Kur { get; init; }
    public string? Aciklama { get; init; }
}

public sealed record TaksitPlanYaniti(int Adet, IReadOnlyList<Guid> Ids);

public sealed record TaksitOdendiIstegi(DateTimeOffset? OdemeTarihi = null);

public sealed record MusteriTaksitOlusturYaniti(Guid Id);

public sealed record MusteriTaksitSatiri(
    Guid Id, int Sira, Guid CariId, string CariAd, Guid? VehicleId, string? Plaka, Guid? VehicleSaleId,
    DateTimeOffset Vade, decimal TaksitTutari, string Doviz, decimal Kur, decimal TutarBaz, string Durum, bool Gecikti,
    DateTimeOffset? OdemeTarihi, string? Aciklama, string? Surum)
{
    public static MusteriTaksitSatiri From(MusteriTaksit t, string customerName, string? plate, string? version = null) => new(
        t.Id, t.Sira, t.CariId, customerName, t.VehicleId, plate, t.VehicleSaleId, t.Vade, t.TaksitTutari, t.Currency, t.Kur,
        t.TutarBaz, t.Durum.ToString(), t.Gecikti, t.OdemeTarihi, t.Aciklama, version);
}
