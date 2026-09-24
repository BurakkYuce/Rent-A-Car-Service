using RentACar.Application.ServiceRecords;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.ServiceInsurance;

// F9.1 — servis/bakım kaydı DTOs. `toplamIscilik` (Σ kalem NET) is the ONLY number that reaches the ledger (rücu
// yansıtma = toplamIscilik × kusurOrani); fatura/ödeme blocks are information (FAZ-16 decision, no ledger effect).

public sealed record ServiceRecordRow(Guid Id, string No, Guid VehicleId, string Plaka, string Tip, string Durum,
    DateTimeOffset GirisTarihi, DateTimeOffset? CikisTarihi, int GirisKm, int? CikisKm, string? AtolyeAdi,
    string HasarSorumlu, decimal? KusurOrani, decimal ToplamIscilik, bool Yansitildi, DateTimeOffset? PlanBasTarihi,
    DateTimeOffset? PlanBitTarihi)
{
    public static ServiceRecordRow From(ServiceRecord s, string plaka) => new(s.Id, s.No, s.VehicleId, plaka, s.Tip.ToString(),
        s.Durum.ToString(), s.GirisTarihi, s.CikisTarihi, s.GirisKm, s.CikisKm, s.AtolyeAdi, s.HasarSorumlu.ToString(),
        s.KusurOrani, s.ToplamIscilik, s.Yansitildi, s.PlanBasTarihi, s.PlanBitTarihi);
}

/// <summary>Kalem: <c>tutar</c> = KDV hariç net (kalıcı); brüt/KDV/genel toplam satır bazında yuvarlanmış gösterim.</summary>
public sealed record ServiceLineDto(Guid Id, string Aciklama, decimal Tutar, decimal? BirimFiyat, decimal? Miktar,
    decimal? Indirim, decimal? KdvOran, decimal Brut, decimal KdvTutar, decimal GenelToplam)
{
    public static ServiceLineDto From(ServiceLine l)
    {
        var h = ServisKalemHesap.Hesapla(l.Tutar, l.BirimFiyat, l.Miktar, l.KdvOran);
        return new(l.Id, l.Aciklama, l.Tutar, l.BirimFiyat, l.Miktar, l.Indirim, l.KdvOran, h.Brut, h.KdvTutar, h.GenelToplam);
    }
}

/// <summary>FAZ-16 bilgi blokları (kaza / fatura / ödeme / yakıt / plan). PUT <c>/bilgi</c> gövdesiyle aynı alanlar.</summary>
public sealed record ServiceInfoDto(string? AtolyeAdi, string? Aciklama, string? BeyanTuru, string? KarsiPlaka,
    string? KarsiTrafikSigortasi, DateTimeOffset? KazaTarihi, string? KazaSorumlusu, string? HasarDosyaNo, decimal? DegerKaybi,
    DateTimeOffset? FaturaTarihi, string? FaturaNo, decimal? FaturaTutar, decimal? FaturaKdv, decimal? FaturaGenelToplam,
    DateTimeOffset? OdemeTarihi, decimal? Odeme, string? OdemeDoviz, decimal? OdemeKur, string? OdemeTuru, string? KasaKodu,
    string? HesapNo, int? CikisYakit, int? DonusYakit, DateTimeOffset? PlanBasTarihi, DateTimeOffset? PlanBitTarihi)
{
    public static ServiceInfoDto From(ServiceRecord s) => new(s.AtolyeAdi, s.Aciklama, s.BeyanTuru, s.KarsiPlaka,
        s.KarsiTrafikSigortasi, s.KazaTarihi, s.KazaSorumlusu, s.HasarDosyaNo, s.DegerKaybi, s.FaturaTarihi, s.FaturaNo,
        s.FaturaTutar, s.FaturaKdv, s.FaturaGenelToplam, s.OdemeTarihi, s.Odeme, s.OdemeDoviz, s.OdemeKur,
        s.OdemeTuru?.ToString(), s.KasaKodu, s.HesapNo, s.CikisYakit, s.DonusYakit, s.PlanBasTarihi, s.PlanBitTarihi);
}

/// <summary>Rücu yansıtma izi (defter): tutar, cari (KVKK görünen ad), tarih.</summary>
public sealed record ServiceReflectionDto(decimal Tutar, Guid? CariId, string? CariAd, DateTimeOffset? Tarih);

/// <summary>Yansıtma önizlemesi: tamamlanmış + sorumlu Müşteri/Sigorta + kusur &gt; 0 iken yansıtılacak tutar.</summary>
public sealed record ServiceActions(bool ServiseAlabilir, bool Baslatabilir, bool Tamamlayabilir, bool IptalEdebilir,
    bool KalemEkleyebilir, bool Yansitabilir, decimal? YansitilacakTutar);

public sealed record ServiceRecordDetail(ServiceRecordRow Kayit, ServiceInfoDto Bilgi, IReadOnlyList<ServiceLineDto> Kalemler,
    decimal KdvToplam, decimal GenelToplam, ServiceReflectionDto? Yansitma, ServiceActions Yetkiler, string? Surum);

public sealed record ServiceLineRequest(string? Aciklama, decimal? Tutar, decimal? BirimFiyat, decimal? Miktar,
    decimal? Indirim, decimal? KdvOran);

public sealed record ServiceInfoRequest(string? AtolyeAdi, string? Aciklama, string? BeyanTuru, string? KarsiPlaka,
    string? KarsiTrafikSigortasi, DateTimeOffset? KazaTarihi, string? KazaSorumlusu, string? HasarDosyaNo, decimal? DegerKaybi,
    DateTimeOffset? FaturaTarihi, string? FaturaNo, decimal? FaturaTutar, decimal? FaturaKdv, DateTimeOffset? OdemeTarihi,
    decimal? Odeme, string? OdemeDoviz, decimal? OdemeKur, string? OdemeTuru, string? KasaKodu, string? HesapNo,
    int? CikisYakit, int? DonusYakit, DateTimeOffset? PlanBasTarihi, DateTimeOffset? PlanBitTarihi, string? Surum = null);

public sealed record ServiceRecordRequest(Guid? VehicleId, string? Tip, int? GirisKm, DateTimeOffset? GirisTarihi,
    string? HasarSorumlu, decimal? KusurOrani, bool Rezervasyon, ServiceInfoRequest? Bilgi, ServiceLineRequest? Kalem);

public sealed record ServiceIntakeRequest(int? GirisKm);

public sealed record ServiceCompleteRequest(int? CikisKm, int? SonrakiBakimKm);

public sealed record ServiceReflectRequest(Guid? CariId);

public sealed record ServiceOptions(IReadOnlyList<string> Tipler, IReadOnlyList<string> Durumlar,
    IReadOnlyList<string> HasarSorumlulari, IReadOnlyList<string> OdemeTurleri);
