using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Tanim;

/// <summary>Branch master row (all Blazor form fields). Rates are fractions (0.10 = %10).</summary>
public sealed record BranchDto(
    Guid Id, string Kod, string Ad, string? Adres, string? Telefon, string? Eposta, string? Il, string? Ilce,
    string? Yetkili, string? CalismaSaatleri, decimal? KomisyonOran, string? EvrakNoOnek,
    string? WebIsim, string? FirmaUnvani, int? WebRezOncesiSaat, decimal? Enlem, decimal? Boylam,
    decimal? HizmetKomisyonOran, string? RezervasyonRengi, bool AlisSubesiDegilMi, int? WebSira,
    string? WebOtoparkId, string? BayiCariKod, string? BayiOfisId, string? KomisyonHesabi, string? OnlineRezId,
    string? SozlesmeNoFormati, Guid? NakitHesapId, Guid? BankaHesapId, string? EntegrasyonKodu,
    string? ResimDosyasi, string? HaftalikCalismaSaatleri, bool Aktif, string? Surum) : IDefinitionRow
{
    public static BranchDto From(Branch b, string? surum) => new(
        b.Id, b.Kod, b.Ad, b.Adres, b.Telefon, b.Eposta, b.Il, b.Ilce, b.Yetkili, b.CalismaSaatleri, b.KomisyonOran,
        b.EvrakNoOnek, b.WebIsim, b.FirmaUnvani, b.WebRezOncesiSaat, b.Enlem, b.Boylam, b.HizmetKomisyonOran,
        b.RezervasyonRengi, b.AlisSubesiDegilMi, b.WebSira, b.WebOtoparkId, b.BayiCariKod, b.BayiOfisId,
        b.KomisyonHesabi, b.OnlineRezId, b.SozlesmeNoFormati, b.NakitHesapId, b.BankaHesapId, b.EntegrasyonKodu,
        b.ResimDosyasi, b.HaftalikCalismaSaatleri, b.Aktif, surum);

    internal static readonly SortFieldMap<BranchDto> Sort = SortFieldMap<BranchDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("il", x => x.Il)
        .Alan("webSira", x => x.WebSira).Alan("aktif", x => x.Aktif);
}

/// <summary>POST/PUT body — full replacement: every field is written (omitted = cleared). <c>surum</c> REQUIRED on PUT.</summary>
public sealed record BranchRequest(
    string? Kod, string? Ad, string? Adres = null, string? Telefon = null, string? Eposta = null, string? Il = null,
    string? Ilce = null, string? Yetkili = null, string? CalismaSaatleri = null, decimal? KomisyonOran = null,
    string? EvrakNoOnek = null, string? WebIsim = null, string? FirmaUnvani = null, int? WebRezOncesiSaat = null,
    decimal? Enlem = null, decimal? Boylam = null, decimal? HizmetKomisyonOran = null, string? RezervasyonRengi = null,
    bool AlisSubesiDegilMi = false, int? WebSira = null, string? WebOtoparkId = null, string? BayiCariKod = null,
    string? BayiOfisId = null, string? KomisyonHesabi = null, string? OnlineRezId = null,
    string? SozlesmeNoFormati = null, Guid? NakitHesapId = null, Guid? BankaHesapId = null,
    string? EntegrasyonKodu = null, string? ResimDosyasi = null, string? HaftalikCalismaSaatleri = null,
    bool Aktif = true, string? Surum = null) : IDefinitionRequest
{
    public BranchInput ToInput() => new()
    {
        Kod = Kod ?? "", Ad = Ad ?? "", Adres = Adres, Telefon = Telefon, Eposta = Eposta, Il = Il, Ilce = Ilce,
        Yetkili = Yetkili, CalismaSaatleri = CalismaSaatleri, KomisyonOran = KomisyonOran, EvrakNoOnek = EvrakNoOnek,
        WebIsim = WebIsim, FirmaUnvani = FirmaUnvani, WebRezOncesiSaat = WebRezOncesiSaat, Enlem = Enlem,
        Boylam = Boylam, HizmetKomisyonOran = HizmetKomisyonOran, RezervasyonRengi = RezervasyonRengi,
        AlisSubesiDegilMi = AlisSubesiDegilMi, WebSira = WebSira, WebOtoparkId = WebOtoparkId,
        BayiCariKod = BayiCariKod, BayiOfisId = BayiOfisId, KomisyonHesabi = KomisyonHesabi,
        OnlineRezId = OnlineRezId, SozlesmeNoFormati = SozlesmeNoFormati, NakitHesapId = NakitHesapId,
        BankaHesapId = BankaHesapId, EntegrasyonKodu = EntegrasyonKodu, ResimDosyasi = ResimDosyasi,
        HaftalikCalismaSaatleri = HaftalikCalismaSaatleri, Aktif = Aktif,
    };
}

/// <summary>Branch-specific free service (e.g. free airport delivery at this branch).</summary>
public sealed record BranchServiceDto(Guid Id, string HizmetAdi, string? Aciklama);
public sealed record BranchServiceRequest(string? HizmetAdi, string? Aciklama);

public sealed record BranchMergePreviewDto(string KaynakAd, string HedefAd, IReadOnlyList<BranchMergeCountDto> Etkilenen, int Toplam);
public sealed record BranchMergeCountDto(string Tablo, int Adet);
/// <summary>Merge: every reference moves from source to target, the source is DEACTIVATED (not deleted).
/// <c>onay</c> must be true — the bulk update cannot be undone.</summary>
public sealed record BranchMergeRequest(Guid? KaynakId, Guid? HedefId, bool? Onay);
public sealed record BranchMergeResultDto(int TasinanKayit);
