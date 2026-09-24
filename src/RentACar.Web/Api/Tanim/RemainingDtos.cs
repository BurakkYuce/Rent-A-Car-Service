using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Tanim;

// F11.2c — DTOs of the remaining definition endpoints. JSON field names are Turkish (domain fields), type names English.

/// <summary>
/// Reservation source (FAZ-24 supplier rates + FAZ-49 rule matrix). Rates are PERCENT (12.5 = %12,5) and only
/// STORED — no price/commission/ledger calculation reads them. Of the flags, only <c>Uzatamaz</c>,
/// <c>RezTarihleriDegisemez</c>, <c>ProvizyonYok</c>, <c>KmSinirsiz</c>, <c>AyniYonDrop</c> and <c>MaxGun</c> are
/// enforced by the booking flow; the rest is information. <c>KaynakGrubu</c> is the enum NAME (null = unspecified).
/// </summary>
public sealed record ReservationSourceDto(
    Guid Id, string Kod, string Ad, string? Tedarikci, decimal? KiraOrani, decimal? HizmetOrani, decimal? DropOrani,
    string? KaynakGrubu, bool Uzatamaz, bool RezTarihleriDegisemez, bool ProvizyonYok, bool KmSinirsiz,
    bool AyniYonDrop, int? MaxGun, bool MaliyetYansitma, bool MatrisErken, bool MatrisGecikme, bool MatrisIptal,
    bool MatrisNoShow, bool MatrisUzatma, string? SigortaKaynakNo, string? DropKaynakNo, string? ProvizyonSecenek,
    string? MuafiyatSecenek, bool ScdwDahil, bool CdwDahil, bool LcfDahil, bool PaiDahil, decimal? BebekKoltugu,
    decimal? Navigasyon, decimal? EkSurucu, decimal? Wifi, decimal? KomisyonOrani, decimal? OnOdemeOrani,
    decimal? IndirimOrani, decimal? PuanOrani, string? MailAdres, bool OtomatikMailGitme, bool RiskAnalizYapma,
    bool SubeGor, bool AcenteFiyatDegistir, bool Gizle, bool SadeceMusteriOdeme, bool Aktif, string? Surum)
    : IDefinitionRow
{
    internal static readonly SiralamaHaritasi<ReservationSourceDto> Sort = SiralamaHaritasi<ReservationSourceDto>
        .Olustur(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("tedarikci", x => x.Tedarikci)
        .Alan("kaynakGrubu", x => x.KaynakGrubu).Alan("aktif", x => x.Aktif);

    public static ReservationSourceDto From(ReservationSource x, string? version) => new(
        x.Id, x.Kod, x.Ad, x.Tedarikci, x.KiraOrani, x.HizmetOrani, x.DropOrani, x.KaynakGrubu?.ToString(),
        x.Uzatamaz, x.RezTarihleriDegisemez, x.ProvizyonYok, x.KmSinirsiz, x.AyniYonDrop, x.MaxGun,
        x.MaliyetYansitma, x.MatrisErken, x.MatrisGecikme, x.MatrisIptal, x.MatrisNoShow, x.MatrisUzatma,
        x.SigortaKaynakNo, x.DropKaynakNo, x.ProvizyonSecenek, x.MuafiyatSecenek, x.ScdwDahil, x.CdwDahil,
        x.LcfDahil, x.PaiDahil, x.BebekKoltugu, x.Navigasyon, x.EkSurucu, x.Wifi, x.KomisyonOrani, x.OnOdemeOrani,
        x.IndirimOrani, x.PuanOrani, x.MailAdres, x.OtomatikMailGitme, x.RiskAnalizYapma, x.SubeGor,
        x.AcenteFiyatDegistir, x.Gizle, x.SadeceMusteriOdeme, x.Aktif, version);
}

/// <summary>Reservation source POST/PUT body (full replacement; <c>surum</c> REQUIRED on PUT).</summary>
public sealed record ReservationSourceRequest(
    string? Kod, string? Ad, string? Tedarikci = null, decimal? KiraOrani = null, decimal? HizmetOrani = null,
    decimal? DropOrani = null, string? KaynakGrubu = null, bool Uzatamaz = false, bool RezTarihleriDegisemez = false,
    bool ProvizyonYok = false, bool KmSinirsiz = false, bool AyniYonDrop = false, int? MaxGun = null,
    bool MaliyetYansitma = false, bool MatrisErken = false, bool MatrisGecikme = false, bool MatrisIptal = false,
    bool MatrisNoShow = false, bool MatrisUzatma = false, string? SigortaKaynakNo = null, string? DropKaynakNo = null,
    string? ProvizyonSecenek = null, string? MuafiyatSecenek = null, bool ScdwDahil = false, bool CdwDahil = false,
    bool LcfDahil = false, bool PaiDahil = false, decimal? BebekKoltugu = null, decimal? Navigasyon = null,
    decimal? EkSurucu = null, decimal? Wifi = null, decimal? KomisyonOrani = null, decimal? OnOdemeOrani = null,
    decimal? IndirimOrani = null, decimal? PuanOrani = null, string? MailAdres = null, bool OtomatikMailGitme = false,
    bool RiskAnalizYapma = false, bool SubeGor = false, bool AcenteFiyatDegistir = false, bool Gizle = false,
    bool SadeceMusteriOdeme = false, bool Aktif = true, string? Surum = null) : IDefinitionRequest;

/// <summary>Ledger account code (roadmap N1): Kod (32, upper-case), Ad (200), Açıklama (512).</summary>
public sealed record AccountCodeDto(Guid Id, string Kod, string Ad, string? Aciklama, bool Aktif, string? Surum) : IDefinitionRow
{
    internal static readonly SiralamaHaritasi<AccountCodeDto> Sort = SiralamaHaritasi<AccountCodeDto>
        .Olustur(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("aktif", x => x.Aktif);
}

public sealed record AccountCodeRequest(string? Kod, string? Ad, string? Aciklama, bool Aktif = true, string? Surum = null) : IDefinitionRequest;
