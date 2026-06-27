using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Api.Dtos;

/// <summary>Cari yanıt modeli (DisplayName türetilmiş; enum string).</summary>
public sealed record CustomerResponse(
    Guid Id, CariType Tip, string DisplayName,
    string? Ad, string? Soyad, string? TcKimlik,
    string? Unvan, string? VergiDairesi, string? VergiNo,
    string? CepTel, string? Email, string? Il, string? Ilce, string? Adres,
    string? Tarife, int VadeGun, decimal RiskLimiti, bool KaraListe, bool Pasif,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc)
{
    public static CustomerResponse From(Customer c) => new(
        c.Id, c.Tip, c.DisplayName, c.Ad, c.Soyad, c.TcKimlik, c.Unvan, c.VergiDairesi, c.VergiNo,
        c.CepTel, c.Email, c.Il, c.Ilce, c.Adres, c.Tarife, c.VadeGun, c.RiskLimiti, c.KaraListe, c.Pasif,
        c.CreatedAtUtc, c.UpdatedAtUtc);
}

/// <summary>Cari oluştur/güncelle istek modeli.</summary>
public sealed class CustomerRequest
{
    public CariType Tip { get; set; } = CariType.Bireysel;
    public string? Ad { get; set; }
    public string? Soyad { get; set; }
    public string? TcKimlik { get; set; }
    public string? Unvan { get; set; }
    public string? VergiDairesi { get; set; }
    public string? VergiNo { get; set; }
    public string? CepTel { get; set; }
    public string? Email { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Adres { get; set; }
    public string? Tarife { get; set; }
    public int VadeGun { get; set; }
    public decimal RiskLimiti { get; set; }
    public bool KaraListe { get; set; }
    public bool Pasif { get; set; }

    public CustomerInput ToInput() => new()
    {
        Tip = Tip, Ad = Ad, Soyad = Soyad, TcKimlik = TcKimlik, Unvan = Unvan,
        VergiDairesi = VergiDairesi, VergiNo = VergiNo, CepTel = CepTel, Email = Email,
        Il = Il, Ilce = Ilce, Adres = Adres, Tarife = Tarife, VadeGun = VadeGun,
        RiskLimiti = RiskLimiti, KaraListe = KaraListe, Pasif = Pasif
    };
}
