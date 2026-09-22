using RentACar.Application.Bookings;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Kira;

/// <summary>
/// Kira sözleşmesinin TAM alan kümesi (F4.1 — kira formunun gösterdiği her alan). Entity'nin birebir
/// izdüşümü: <c>TenantId</c> ve gezinme koleksiyonu hariç; enum'lar ad olarak (<c>"Kirada"</c>). Hesap YOK —
/// para alanları servislerin yazdığı değerlerdir (Tutar/GenelToplam/Bakiye kaynağı RentalService/ReturnMath).
/// Üretim: entity alan listesinden mekanik (alan eklenince buraya da eklenir; derleme uyarısı yok — gözden geçirme).
/// </summary>
public sealed class KiraSozlesmesiDto
{
    public Guid Id { get; init; }
    public string SozlesmeNo { get; init; } = string.Empty;
    public string Durum { get; init; } = string.Empty;
    public Guid? ReservationId { get; init; }
    public Guid MusteriId { get; init; }
    public Guid VehicleId { get; init; }
    public DateTimeOffset BasTar { get; init; }
    public DateTimeOffset BitTar { get; init; }
    public string? CikisOfisi { get; init; }
    public Guid? CikisSubeId { get; init; }
    public string? DonusOfisi { get; init; }
    public int KmLimit { get; init; }
    public decimal FazlaKmUcret { get; init; }
    public decimal YakitBirimUcret { get; init; }
    public int? CikisKm { get; init; }
    public int? CikisYakit { get; init; }
    public int? DonusKm { get; init; }
    public int? DonusYakit { get; init; }
    public DateTimeOffset? GercekDonusTar { get; init; }
    public int FazlaKm { get; init; }
    public decimal FazlaKmBedeli { get; init; }
    public int EksikYakit { get; init; }
    public decimal YakitBedeli { get; init; }
    public int UzatmaGun { get; init; }
    public decimal UzatmaBedeli { get; init; }
    public int? KmHediye { get; init; }
    public string? BitisSebebi { get; init; }
    public Guid? TeslimAlanPersonelId { get; init; }
    public Guid? TeslimEdenPersonelId { get; init; }
    public string? OdemeSekli { get; init; }
    public Guid? IkinciSurucuId { get; init; }
    public string? IkinciSurucuSerbestAd { get; init; }
    public string? IkinciSurucuSerbestSoyad { get; init; }
    public string? IkinciSurucuSerbestTel { get; init; }
    public string? IkinciSurucuSerbestEhliyetSinifi { get; init; }
    public int? HediyeGun { get; init; }
    public int? FaturalananGun { get; init; }
    public DateTimeOffset? VadeTar { get; init; }
    public decimal? IskontoTutar { get; init; }
    public decimal? HaftaSonuFark { get; init; }
    public int Gun { get; init; }
    public decimal GunlukUcret { get; init; }
    public decimal Tutar { get; init; }
    public decimal GenelToplam { get; init; }
    public decimal Tahsilat { get; init; }
    public decimal Bakiye { get; init; }
    public decimal? Provizyon { get; init; }
    public decimal? Depozito { get; init; }
    public decimal? KomisyonOran { get; init; }
    public decimal? KomisyonTutar { get; init; }
    public decimal? DropUcreti { get; init; }
    public decimal? SonraOdeOran { get; init; }
    public string? Aciklama { get; init; }
    public string? Kaynak { get; init; }
    public string? KampanyaKodu { get; init; }
    public string? UyariAciklama { get; init; }
    public string? OzelFaturaAciklama { get; init; }
    public bool? FaturaListesindeGizle { get; init; }
    public string? UcusNo { get; init; }
    public string? ProvizyonNo { get; init; }
    public DateTimeOffset? ProvizyonTarih { get; init; }
    public string ProvizyonDurum { get; init; } = string.Empty;
    public DateTimeOffset? ProvizyonKapamaTarih { get; init; }
    public decimal? ProvizyonKapamaTutar { get; init; }
    public string? OnayKodu { get; init; }
    public string? FirmaKodu { get; init; }
    public string? ProjeAdi { get; init; }
    public string? OzelKod { get; init; }
    public string? TalepTuru { get; init; }
    public string? GeldigiBirim { get; init; }
    public string? KefilBilgisi { get; init; }
    public string? AssistFirma { get; init; }
    public string? OzelSoforBilgisi { get; init; }
    public string? EkKosullar { get; init; }
    public Guid? BelgeSablonId { get; init; }
    public decimal? OpsiyonNet { get; init; }
    public int? OpsiyonGun { get; init; }
    public bool RiskOnay { get; init; }
    public int? ManuelFindexPuan { get; init; }
    public bool? KabisCikis { get; init; }
    public bool? KabisDonus { get; init; }
    public bool? OtomatikUzat { get; init; }
    public bool? AksYedekAnahtarCikis { get; init; }
    public bool? AksYedekAnahtarDonus { get; init; }
    public bool? AksStepneCikis { get; init; }
    public bool? AksStepneDonus { get; init; }
    public bool? AksZincirCikis { get; init; }
    public bool? AksZincirDonus { get; init; }
    public bool? AksIlkYardimCikis { get; init; }
    public bool? AksIlkYardimDonus { get; init; }
    public string? AksLastikCikis { get; init; }
    public string? AksLastikDonus { get; init; }
    public string? KiralamaTuru { get; init; }
    public string? FaturalamaTipi { get; init; }
    public string? FiyatTuru { get; init; }
    public string? Doviz { get; init; }
    public decimal KurSnapshot { get; init; }
    public bool DonemselFaturalama { get; init; }
    public decimal? KdvOranSnapshot { get; init; }
    public decimal? OzelKdvOran { get; init; }
    public decimal? DamgaVergisi { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>
    /// F4.3 adversarial F2 — kayıt sürümü (opak; Postgres <c>xmin</c>). <c>PUT /kiralar/{id}</c> bunu ZORUNLU geri
    /// gönderir; satır kilidi altında güncel sürümle eşleşmezse 409 <c>cakisma</c> (başka oturumun değişikliği bayat
    /// tam değiştirmeyle geri alınmaz). Teslim/dönüş/uzat/iptal/provizyon/ek hizmet/tahsilat da sürümü değiştirir
    /// → SPA işlem sonrası detayı yeniden okur. Alanlardan ÖNCE okunur (yarışta güvenli taraf: yanlış 409, asla
    /// sessiz geri alma).
    /// </summary>
    public string? Surum { get; init; }

    public static KiraSozlesmesiDto From(RentalContract c, string? surum = null) => new()
    {
        Surum = surum,
        Id = c.Id,
        SozlesmeNo = c.SozlesmeNo,
        Durum = c.Durum.ToString(),
        ReservationId = c.ReservationId,
        MusteriId = c.MusteriId,
        VehicleId = c.VehicleId,
        BasTar = c.BasTar,
        BitTar = c.BitTar,
        CikisOfisi = c.CikisOfisi,
        CikisSubeId = c.CikisSubeId,
        DonusOfisi = c.DonusOfisi,
        KmLimit = c.KmLimit,
        FazlaKmUcret = c.FazlaKmUcret,
        YakitBirimUcret = c.YakitBirimUcret,
        CikisKm = c.CikisKm,
        CikisYakit = c.CikisYakit,
        DonusKm = c.DonusKm,
        DonusYakit = c.DonusYakit,
        GercekDonusTar = c.GercekDonusTar,
        FazlaKm = c.FazlaKm,
        FazlaKmBedeli = c.FazlaKmBedeli,
        EksikYakit = c.EksikYakit,
        YakitBedeli = c.YakitBedeli,
        UzatmaGun = c.UzatmaGun,
        UzatmaBedeli = c.UzatmaBedeli,
        KmHediye = c.KmHediye,
        BitisSebebi = c.BitisSebebi,
        TeslimAlanPersonelId = c.TeslimAlanPersonelId,
        TeslimEdenPersonelId = c.TeslimEdenPersonelId,
        OdemeSekli = c.OdemeSekli,
        IkinciSurucuId = c.IkinciSurucuId,
        IkinciSurucuSerbestAd = c.IkinciSurucuSerbestAd,
        IkinciSurucuSerbestSoyad = c.IkinciSurucuSerbestSoyad,
        IkinciSurucuSerbestTel = c.IkinciSurucuSerbestTel,
        IkinciSurucuSerbestEhliyetSinifi = c.IkinciSurucuSerbestEhliyetSinifi,
        HediyeGun = c.HediyeGun,
        FaturalananGun = c.FaturalananGun,
        VadeTar = c.VadeTar,
        IskontoTutar = c.IskontoTutar,
        HaftaSonuFark = c.HaftaSonuFark,
        Gun = c.Gun,
        GunlukUcret = c.GunlukUcret,
        Tutar = c.Tutar,
        GenelToplam = c.GenelToplam,
        Tahsilat = c.Tahsilat,
        Bakiye = c.Bakiye,
        Provizyon = c.Provizyon,
        Depozito = c.Depozito,
        KomisyonOran = c.KomisyonOran,
        KomisyonTutar = c.KomisyonTutar,
        DropUcreti = c.DropUcreti,
        SonraOdeOran = c.SonraOdeOran,
        Aciklama = c.Aciklama,
        Kaynak = c.Kaynak,
        KampanyaKodu = c.KampanyaKodu,
        UyariAciklama = c.UyariAciklama,
        OzelFaturaAciklama = c.OzelFaturaAciklama,
        FaturaListesindeGizle = c.FaturaListesindeGizle,
        UcusNo = c.UcusNo,
        ProvizyonNo = c.ProvizyonNo,
        ProvizyonTarih = c.ProvizyonTarih,
        ProvizyonDurum = c.ProvizyonDurum.ToString(),
        ProvizyonKapamaTarih = c.ProvizyonKapamaTarih,
        ProvizyonKapamaTutar = c.ProvizyonKapamaTutar,
        OnayKodu = c.OnayKodu,
        FirmaKodu = c.FirmaKodu,
        ProjeAdi = c.ProjeAdi,
        OzelKod = c.OzelKod,
        TalepTuru = c.TalepTuru,
        GeldigiBirim = c.GeldigiBirim,
        KefilBilgisi = c.KefilBilgisi,
        AssistFirma = c.AssistFirma,
        OzelSoforBilgisi = c.OzelSoforBilgisi,
        EkKosullar = c.EkKosullar,
        BelgeSablonId = c.BelgeSablonId,
        OpsiyonNet = c.OpsiyonNet,
        OpsiyonGun = c.OpsiyonGun,
        RiskOnay = c.RiskOnay,
        ManuelFindexPuan = c.ManuelFindexPuan,
        KabisCikis = c.KabisCikis,
        KabisDonus = c.KabisDonus,
        OtomatikUzat = c.OtomatikUzat,
        AksYedekAnahtarCikis = c.AksYedekAnahtarCikis,
        AksYedekAnahtarDonus = c.AksYedekAnahtarDonus,
        AksStepneCikis = c.AksStepneCikis,
        AksStepneDonus = c.AksStepneDonus,
        AksZincirCikis = c.AksZincirCikis,
        AksZincirDonus = c.AksZincirDonus,
        AksIlkYardimCikis = c.AksIlkYardimCikis,
        AksIlkYardimDonus = c.AksIlkYardimDonus,
        AksLastikCikis = c.AksLastikCikis,
        AksLastikDonus = c.AksLastikDonus,
        KiralamaTuru = c.KiralamaTuru,
        FaturalamaTipi = c.FaturalamaTipi,
        FiyatTuru = c.FiyatTuru,
        Doviz = c.Doviz,
        KurSnapshot = c.KurSnapshot,
        DonemselFaturalama = c.DonemselFaturalama,
        KdvOranSnapshot = c.KdvOranSnapshot,
        OzelKdvOran = c.OzelKdvOran,
        DamgaVergisi = c.DamgaVergisi,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc,
    };
}

/// <summary>
/// Açık kira güncelleme gövdesi (<c>PUT /kiralar/{id}</c>). Alan kümesi <see cref="RentalUpdateInput"/> ile
/// BİREBİR (whitelist TİP düzeyinde: para/tarih/durum alanları YOK — tarih = uzat, fiyat farkı = fark faturası).
/// <para><b>Her alan <c>required</c>:</b> güncelleme TAM DEĞİŞTİRMEDİR (servis gelen değeri yazar). Gövdede
/// unutulan bir alan sessizce null/0'a düşmesin diye JSON'da EKSİK alan 400'dür (null göndermek ise açık
/// "temizle" kararıdır). Örnek risk: <c>kmLimit</c> eksik → 0 = sınırsız km → aşım bedeli hiç doğmaz;
/// <c>dropUcreti</c> eksik → drop ücret satırı silinir. Blazor formu tüm alanları her gönderimde taşır; SPA da
/// detaydan ön-doldurup TAMAMINI gönderir.</para>
/// </summary>
public sealed class KiraGuncelleIstegi
{
    /// <summary>F4.3 adversarial F2: okunan kayıt sürümü (detaydaki <c>kira.surum</c>). ZORUNLU; eşleşmezse 409
    /// <c>cakisma</c>, hiçbir şey yazılmaz.</summary>
    public required string? Surum { get; init; }
    public required string? CikisOfisi { get; init; }
    public required string? DonusOfisi { get; init; }
    public required Guid? IkinciSurucuId { get; init; }
    public required Guid? TeslimEdenPersonelId { get; init; }
    public required string? OdemeSekli { get; init; }
    public required string? IkinciSurucuSerbestAd { get; init; }
    public required string? IkinciSurucuSerbestSoyad { get; init; }
    public required string? IkinciSurucuSerbestTel { get; init; }
    public required string? IkinciSurucuSerbestEhliyetSinifi { get; init; }
    public required string? Aciklama { get; init; }
    public required string? Kaynak { get; init; }
    public required string? KiralamaTuru { get; init; }
    public required bool DonemselFaturalama { get; init; }
    public required string? FaturalamaTipi { get; init; }
    public required int KmLimit { get; init; }
    public required decimal FazlaKmUcret { get; init; }
    public required decimal YakitBirimUcret { get; init; }
    public required decimal? Provizyon { get; init; }
    public required decimal? Depozito { get; init; }
    public required decimal? KomisyonOran { get; init; }
    public required decimal? KomisyonTutar { get; init; }
    public required decimal? DropUcreti { get; init; }
    public required decimal? SonraOdeOran { get; init; }
    public required string? UyariAciklama { get; init; }
    public required string? OzelFaturaAciklama { get; init; }
    public required bool? FaturaListesindeGizle { get; init; }
    public required string? UcusNo { get; init; }
    public required string? ProvizyonNo { get; init; }
    public required DateTimeOffset? ProvizyonTarih { get; init; }
    public required string? OnayKodu { get; init; }
    public required string? FirmaKodu { get; init; }
    public required string? ProjeAdi { get; init; }
    public required string? OzelKod { get; init; }
    public required decimal? OzelKdvOran { get; init; }
    public required decimal? DamgaVergisi { get; init; }
    public required string? TalepTuru { get; init; }
    public required string? GeldigiBirim { get; init; }
    public required string? KefilBilgisi { get; init; }
    public required string? AssistFirma { get; init; }
    public required string? OzelSoforBilgisi { get; init; }
    public required string? EkKosullar { get; init; }
    public required Guid? BelgeSablonId { get; init; }
    public required int? ManuelFindexPuan { get; init; }
    public required decimal? OpsiyonNet { get; init; }
    public required int? OpsiyonGun { get; init; }
    public required bool? KabisCikis { get; init; }
    public required bool? KabisDonus { get; init; }
    public required bool? OtomatikUzat { get; init; }
    public required bool? AksYedekAnahtarCikis { get; init; }
    public required bool? AksYedekAnahtarDonus { get; init; }
    public required bool? AksStepneCikis { get; init; }
    public required bool? AksStepneDonus { get; init; }
    public required bool? AksZincirCikis { get; init; }
    public required bool? AksZincirDonus { get; init; }
    public required bool? AksIlkYardimCikis { get; init; }
    public required bool? AksIlkYardimDonus { get; init; }
    public required string? AksLastikCikis { get; init; }
    public required string? AksLastikDonus { get; init; }

    public RentalUpdateInput ToInput() => new()
    {
        BeklenenSurum = Surum,
        CikisOfisi = CikisOfisi,
        DonusOfisi = DonusOfisi,
        IkinciSurucuId = IkinciSurucuId,
        TeslimEdenPersonelId = TeslimEdenPersonelId,
        OdemeSekli = OdemeSekli,
        IkinciSurucuSerbestAd = IkinciSurucuSerbestAd,
        IkinciSurucuSerbestSoyad = IkinciSurucuSerbestSoyad,
        IkinciSurucuSerbestTel = IkinciSurucuSerbestTel,
        IkinciSurucuSerbestEhliyetSinifi = IkinciSurucuSerbestEhliyetSinifi,
        Aciklama = Aciklama,
        Kaynak = Kaynak,
        KiralamaTuru = KiralamaTuru,
        DonemselFaturalama = DonemselFaturalama,
        FaturalamaTipi = FaturalamaTipi,
        KmLimit = KmLimit,
        FazlaKmUcret = FazlaKmUcret,
        YakitBirimUcret = YakitBirimUcret,
        Provizyon = Provizyon,
        Depozito = Depozito,
        KomisyonOran = KomisyonOran,
        KomisyonTutar = KomisyonTutar,
        DropUcreti = DropUcreti,
        SonraOdeOran = SonraOdeOran,
        UyariAciklama = UyariAciklama,
        OzelFaturaAciklama = OzelFaturaAciklama,
        FaturaListesindeGizle = FaturaListesindeGizle,
        UcusNo = UcusNo,
        ProvizyonNo = ProvizyonNo,
        ProvizyonTarih = ProvizyonTarih,
        OnayKodu = OnayKodu,
        FirmaKodu = FirmaKodu,
        ProjeAdi = ProjeAdi,
        OzelKod = OzelKod,
        OzelKdvOran = OzelKdvOran,
        DamgaVergisi = DamgaVergisi,
        TalepTuru = TalepTuru,
        GeldigiBirim = GeldigiBirim,
        KefilBilgisi = KefilBilgisi,
        AssistFirma = AssistFirma,
        OzelSoforBilgisi = OzelSoforBilgisi,
        EkKosullar = EkKosullar,
        BelgeSablonId = BelgeSablonId,
        ManuelFindexPuan = ManuelFindexPuan,
        OpsiyonNet = OpsiyonNet,
        OpsiyonGun = OpsiyonGun,
        KabisCikis = KabisCikis,
        KabisDonus = KabisDonus,
        OtomatikUzat = OtomatikUzat,
        AksYedekAnahtarCikis = AksYedekAnahtarCikis,
        AksYedekAnahtarDonus = AksYedekAnahtarDonus,
        AksStepneCikis = AksStepneCikis,
        AksStepneDonus = AksStepneDonus,
        AksZincirCikis = AksZincirCikis,
        AksZincirDonus = AksZincirDonus,
        AksIlkYardimCikis = AksIlkYardimCikis,
        AksIlkYardimDonus = AksIlkYardimDonus,
        AksLastikCikis = AksLastikCikis,
        AksLastikDonus = AksLastikDonus,
    };
}
