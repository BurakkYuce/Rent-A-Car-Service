using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Web.Api.ServiceInsurance;

// F9.1 — catalog DTOs. JSON field names are Turkish (domain/form names); `surum` is filled only on single-record
// responses (the SPA opens a record before editing it) and is MANDATORY on PUT (409 cakisma on mismatch).

/// <summary>Tarife (rate card).</summary>
public sealed record RateCardDto(Guid Id, string Kod, string Ad, string Grup, int MinGun, int MaxGun, decimal GunlukUcret,
    string Doviz, DateTimeOffset? GecerliBas, DateTimeOffset? GecerliBit, bool ScdwDahil, bool MiniHasarDahil,
    bool HirsizlikDahil, bool ScdwZorunlu, bool Gosterme, Guid? TarifeGrubuId, bool Aktif, string? Surum)
{
    public static RateCardDto From(RateCard x, string? surum) => new(x.Id, x.Kod, x.Ad, x.Grup, x.MinGun, x.MaxGun,
        x.GunlukUcret, x.Doviz, x.GecerliBas, x.GecerliBit, x.ScdwDahil, x.MiniHasarDahil, x.HirsizlikDahil,
        x.ScdwZorunlu, x.Gosterme, x.TarifeGrubuId, x.Aktif, surum);
}

public sealed record RateCardRequest(string? Kod, string? Ad, string? Grup, int? MinGun, int? MaxGun, decimal? GunlukUcret,
    string? Doviz, DateTimeOffset? GecerliBas, DateTimeOffset? GecerliBit, bool ScdwDahil = false, bool MiniHasarDahil = false,
    bool HirsizlikDahil = false, bool ScdwZorunlu = false, bool Gosterme = false, Guid? TarifeGrubuId = null,
    bool Aktif = true, string? Surum = null) : ICatalogRequest;

/// <summary>Tarife grubu. Password is write-only: never returned; empty on PUT keeps the stored hash.</summary>
public sealed record RateGroupDto(Guid Id, string Kod, string Ad, decimal Oran, string? KullaniciAdi, bool SifreVar,
    bool Aktif, string? Surum)
{
    public static RateGroupDto From(TarifeGrubu x, string? surum)
        => new(x.Id, x.Kod, x.Ad, x.Oran, x.KullaniciAdi, !string.IsNullOrEmpty(x.SifreHash), x.Aktif, surum);
}

public sealed record RateGroupRequest(string? Kod, string? Ad, decimal? Oran, string? KullaniciAdi, string? Sifre,
    bool Aktif = true, string? Surum = null) : ICatalogRequest;

/// <summary>Sigorta / ek hizmet ürünü (fiyat motoru kataloğu).</summary>
public sealed record CoverageProductDto(Guid Id, string Kod, string Ad, string? AdEn, string? Aciklama, string Tur,
    decimal? GunlukUcret, decimal? KdvOrani, int? MaxGun, string? Doviz, bool Zorunlu, bool Aktif, string? Surum)
{
    public static CoverageProductDto From(CoverageProduct x, string? surum) => new(x.Id, x.Kod, x.Ad, x.AdEn, x.Aciklama,
        x.Tur.ToString(), x.GunlukUcret, x.KdvOrani, x.MaxGun, x.Doviz, x.Zorunlu, x.Aktif, surum);
}

public sealed record CoverageProductRequest(string? Kod, string? Ad, string? AdEn, string? Aciklama, string? Tur,
    decimal? GunlukUcret, decimal? KdvOrani, int? MaxGun, string? Doviz, bool Zorunlu = false, bool Aktif = true,
    string? Surum = null) : ICatalogRequest;

/// <summary>Ek hizmet tanımı. SYS-* rows are system fee definitions (code immutable, not deletable — service rule).</summary>
public sealed record ExtraServiceDto(Guid Id, string Kod, string Ad, decimal BirimUcret, decimal KdvOrani, string? Aciklama,
    int? MaxGun, bool Aktif, bool Sistem, string? Surum)
{
    public static ExtraServiceDto From(EkHizmetTanim x, string? surum) => new(x.Id, x.Kod, x.Ad, x.BirimUcret, x.KdvOrani,
        x.Aciklama, x.MaxGun, x.Aktif, x.Kod.StartsWith("SYS-", StringComparison.OrdinalIgnoreCase), surum);
}

public sealed record ExtraServiceRequest(string? Kod, string? Ad, decimal? BirimUcret, decimal? KdvOrani, string? Aciklama,
    int? MaxGun, bool Aktif = true, string? Surum = null) : ICatalogRequest;

/// <summary>Broker/kaynak satış yasağı.</summary>
public sealed record BrokerBanDto(Guid Id, string Kod, string Ad, string? Aciklama, string? Kaynak, string? AracGrupKod,
    string? Bolge, int? MinGun, bool TumSatisKapali, DateTimeOffset? GecerlilikBas, DateTimeOffset? GecerlilikBit,
    bool Aktif, string? Surum)
{
    public static BrokerBanDto From(BrokerYasak x, string? surum) => new(x.Id, x.Kod, x.Ad, x.Aciklama, x.Kaynak,
        x.AracGrupKod, x.Bolge, x.MinGun, x.TumSatisKapali, x.GecerlilikBas, x.GecerlilikBit, x.Aktif, surum);
}

public sealed record BrokerBanRequest(string? Kod, string? Ad, string? Aciklama, string? Kaynak, string? AracGrupKod,
    string? Bolge, int? MinGun, bool TumSatisKapali = false, DateTimeOffset? GecerlilikBas = null,
    DateTimeOffset? GecerlilikBit = null, bool Aktif = true, string? Surum = null) : ICatalogRequest;

/// <summary>Periyodik bakım (servis) tanımı.</summary>
public sealed record ServiceDefinitionDto(Guid Id, string Kod, string AracTipi, int BakimKm, string? Marka, string? Tip,
    string? Yakit, string? Vites, string? Aciklama, bool Aktif, string? Surum)
{
    public static ServiceDefinitionDto From(ServisTanim x, string? surum) => new(x.Id, x.Kod, x.AracTipi, x.BakimKm,
        x.Marka, x.Tip, x.Yakit, x.Vites, x.Aciklama, x.Aktif, surum);
}

public sealed record ServiceDefinitionRequest(string? Kod, string? AracTipi, int? BakimKm, string? Marka, string? Tip,
    string? Yakit, string? Vites, string? Aciklama, bool Aktif = true, string? Surum = null) : ICatalogRequest;

/// <summary>Filoda VAR ama tanımı OLMAYAN kombinasyon (öneri; hiçbir şey yazmaz).</summary>
public sealed record ServiceDefinitionSuggestionDto(string? Marka, string? Tip, string? Yakit, string? Vites,
    int AracSayisi, string Etiket, string OnerilenKod);
