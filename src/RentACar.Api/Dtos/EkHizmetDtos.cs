using RentACar.Application.EkHizmetler;
using RentACar.Domain.Entities;

namespace RentACar.Api.Dtos;

public sealed record EkHizmetTanimResponse(
    Guid Id, string Kod, string Ad, decimal BirimUcret, decimal KdvOrani, bool Aktif,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc)
{
    public static EkHizmetTanimResponse From(EkHizmetTanim t) => new(
        t.Id, t.Kod, t.Ad, t.BirimUcret, t.KdvOrani, t.Aktif, t.CreatedAtUtc, t.UpdatedAtUtc);
}

public sealed class EkHizmetTanimRequest
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public decimal BirimUcret { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m;
    public bool Aktif { get; set; } = true;

    public EkHizmetTanimInput ToInput() => new()
    {
        Kod = Kod, Ad = Ad, BirimUcret = BirimUcret, KdvOrani = KdvOrani, Aktif = Aktif
    };
}
