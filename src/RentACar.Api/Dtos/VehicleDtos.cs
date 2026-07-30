using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Api.Dtos;

/// <summary>Araç yanıt modeli (entity sızdırmaz; enum'lar JSON'da string).</summary>
public sealed record VehicleResponse(
    Guid Id, string Plaka, string? Marka, string? Grup, string? Sube,
    VehicleStatus Durum, int Km, FuelType? Yakit,   // PR-21: null = "girilmedi"
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc)
{
    public static VehicleResponse From(Vehicle v) => new(
        v.Id, v.Plaka, v.Marka, v.Grup, v.Sube, v.Durum, v.Km, v.Yakit, v.CreatedAtUtc, v.UpdatedAtUtc);
}

/// <summary>Araç oluştur/güncelle istek modeli (enum'lar string olarak gelir).</summary>
public sealed class VehicleRequest
{
    public string Plaka { get; set; } = string.Empty;
    public string? Marka { get; set; }
    public string? Grup { get; set; }
    public string? Sube { get; set; }
    public VehicleStatus Durum { get; set; } = VehicleStatus.Musait;
    public int Km { get; set; }
    /// <summary>PR-21: artık NULLABLE ve VARSAYILANI YOK. Eskiden gönderilmezse sessizce
    /// "Benzin" kaydediliyordu; API istemcisi yakıt bilgisini bilmiyorsa hiç yazmamalı.</summary>
    public FuelType? Yakit { get; set; }

    public VehicleInput ToInput() => new()
    {
        Plaka = Plaka, Marka = Marka, Grup = Grup, Sube = Sube, Durum = Durum, Km = Km, Yakit = Yakit
    };
}
