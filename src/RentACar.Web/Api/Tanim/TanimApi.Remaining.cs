using RentACar.Application.Authorization;
using RentACar.Application.FuelKinds;
using RentACar.Application.HesapKodlari;
using RentACar.Application.PaymentTypes;
using RentACar.Application.ReservationSources;
using RentACar.Application.TransmissionTypes;
using RentACar.Application.VehicleColors;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.2c — the F11 definitions that F11.1a/F11.1b left without a <c>/api/ui/v1</c> endpoint, on the same generic
/// contract (<see cref="DefinitionEndpoints"/>): payment types, fuel kinds, transmission types, colours
/// (<c>MasterTanimService</c> family), reservation sources (FAZ-24/49 fields; "Aşağıya Yansıt" stays in
/// <c>SystemDefinitionsApi</c>) and ledger account codes. Permissions equal the Blazor endpoint policies
/// (OperationsWrite); routes equal the Blazor page routes.
/// </summary>
public static partial class TanimApi
{
    private static void MapRemainingDefinitions(RouteGroupBuilder v1)
    {
        v1.MapDefinition(MasterDefinitions.For<PaymentType, PaymentTypeService>("/odeme-tipleri", Tag, "ödeme tipi",
            (s, b, ct) => s.CreateAsync(new PaymentTypeInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new PaymentTypeInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
        v1.MapDefinition(MasterDefinitions.For<FuelKind, FuelKindService>("/yakit-turleri", Tag, "yakıt türü",
            (s, b, ct) => s.CreateAsync(new FuelKindInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new FuelKindInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
        v1.MapDefinition(MasterDefinitions.For<TransmissionType, TransmissionTypeService>("/vites-turleri", Tag, "vites türü",
            (s, b, ct) => s.CreateAsync(new TransmissionTypeInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new TransmissionTypeInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
        v1.MapDefinition(MasterDefinitions.For<VehicleColor, VehicleColorService>("/renkler", Tag, "renk",
            (s, b, ct) => s.CreateAsync(new VehicleColorInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new VehicleColorInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));

        v1.MapDefinition(new DefinitionRoute<ReservationSource, ReservationSourceDto, ReservationSourceRequest>
        {
            Path = "/rezervasyon-kaynaklari", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<ReservationSourceService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<ReservationSourceService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<ReservationSourceService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<ReservationSourceService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<ReservationSourceService>(sp).CreateAsync(ReservationSourceInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<ReservationSourceService>(sp).UpdateAsync(id, ReservationSourceInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<ReservationSourceService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = ReservationSourceDto.From,
            Sort = ReservationSourceDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Tedarikci],
            ValidateLimits = ReservationSourceLimits,
            // Service messages (ReservationSourceService.Ek) carry the field label as prefix.
            FieldRules =
            [
                ("Rezervasyon kaynağı kodu", "kod"), ("'", "kod"), ("Rezervasyon kaynağı adı", "ad"),
                ("Tedarikçi", "tedarikci"), ("Kira oranı", "kiraOrani"), ("Hizmet oranı", "hizmetOrani"),
                ("Drop oranı", "dropOrani"), ("En fazla gün", "maxGun"), ("Komisyon oranı", "komisyonOrani"),
                ("Ön ödeme oranı", "onOdemeOrani"), ("İndirim oranı", "indirimOrani"), ("Puan oranı", "puanOrani"),
                ("Bebek koltuğu", "bebekKoltugu"), ("Navigasyon", "navigasyon"), ("Ek sürücü", "ekSurucu"),
                ("Wifi", "wifi"), ("Sigorta kaynak no", "sigortaKaynakNo"), ("Drop kaynak no", "dropKaynakNo"),
                ("Provizyon seçeneği", "provizyonSecenek"), ("Muafiyet seçeneği", "muafiyatSecenek"),
                ("Mail adresi", "mailAdres"), ("Kaynak grubu", "kaynakGrubu"),
            ],
            NotFoundMessage = "Rezervasyon kaynağı bulunamadı.",
        });

        v1.MapDefinition(new DefinitionRoute<HesapKodu, AccountCodeDto, AccountCodeRequest>
        {
            Path = "/hesap-kodlari", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<AccountCodeService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<AccountCodeService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<AccountCodeService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<AccountCodeService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<AccountCodeService>(sp).CreateAsync(AccountCodeInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<AccountCodeService>(sp).UpdateAsync(id, AccountCodeInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<AccountCodeService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new AccountCodeDto(e.Id, e.Kod, e.Ad, e.Aciklama, e.Aktif, v),
            Sort = AccountCodeDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Aciklama],
            ValidateLimits = r =>
            {
                Sinirlar.Metin(r.Kod, 32, "kod", "Kod");
                Sinirlar.Metin(r.Ad, 200, "ad", "Ad");
                Sinirlar.Metin(r.Aciklama, 512, "aciklama", "Açıklama");
            },
            // Service: "Hesap kodu zorunludur.", "Hesap kodu en çok …", "'X' kodlu hesap zaten var.", "Hesap adı …".
            FieldRules = [("Hesap kodu", "kod"), ("'", "kod"), ("Hesap adı", "ad")],
            NotFoundMessage = "Hesap kodu bulunamadı.",
        });
    }

    /// <summary>Column limits (varchar lengths, numeric(5,2) rates, numeric(19,4) amounts) at the edge → 400
    /// <c>errors[alan]</c>. Business ranges (rate 0–100, max days 1–3650) stay in the service.</summary>
    private static void ReservationSourceLimits(ReservationSourceRequest r)
    {
        Sinirlar.Metin(r.Kod, 32, "kod", "Kod");
        Sinirlar.Metin(r.Ad, 128, "ad", "Ad");
        Sinirlar.Metin(r.Tedarikci, 128, "tedarikci", "Tedarikçi");
        Sinirlar.Metin(r.SigortaKaynakNo, 64, "sigortaKaynakNo", "Sigorta kaynak no");
        Sinirlar.Metin(r.DropKaynakNo, 64, "dropKaynakNo", "Drop kaynak no");
        Sinirlar.Metin(r.ProvizyonSecenek, 64, "provizyonSecenek", "Provizyon seçeneği");
        Sinirlar.Metin(r.MuafiyatSecenek, 64, "muafiyatSecenek", "Muafiyet seçeneği");
        Sinirlar.Metin(r.MailAdres, 256, "mailAdres", "Mail adresi");
        Sinirlar.Tutar(r.BebekKoltugu, "bebekKoltugu", "Bebek koltuğu");
        Sinirlar.Tutar(r.Navigasyon, "navigasyon", "Navigasyon");
        Sinirlar.Tutar(r.EkSurucu, "ekSurucu", "Ek sürücü");
        Sinirlar.Tutar(r.Wifi, "wifi", "Wifi");
    }

    private static ReservationSourceInput ReservationSourceInputOf(ReservationSourceRequest b) => new()
    {
        Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif, Tedarikci = b.Tedarikci,
        KiraOrani = b.KiraOrani, HizmetOrani = b.HizmetOrani, DropOrani = b.DropOrani,
        KaynakGrubu = F5Ortak.EnumAdi<ReservationSourceGroup>(b.KaynakGrubu, "kaynakGrubu"),
        Uzatamaz = b.Uzatamaz, RezTarihleriDegisemez = b.RezTarihleriDegisemez, ProvizyonYok = b.ProvizyonYok,
        KmSinirsiz = b.KmSinirsiz, AyniYonDrop = b.AyniYonDrop, MaxGun = b.MaxGun,
        MaliyetYansitma = b.MaliyetYansitma, MatrisErken = b.MatrisErken, MatrisGecikme = b.MatrisGecikme,
        MatrisIptal = b.MatrisIptal, MatrisNoShow = b.MatrisNoShow, MatrisUzatma = b.MatrisUzatma,
        SigortaKaynakNo = b.SigortaKaynakNo, DropKaynakNo = b.DropKaynakNo, ProvizyonSecenek = b.ProvizyonSecenek,
        MuafiyatSecenek = b.MuafiyatSecenek, ScdwDahil = b.ScdwDahil, CdwDahil = b.CdwDahil, LcfDahil = b.LcfDahil,
        PaiDahil = b.PaiDahil, BebekKoltugu = b.BebekKoltugu, Navigasyon = b.Navigasyon, EkSurucu = b.EkSurucu,
        Wifi = b.Wifi, KomisyonOrani = b.KomisyonOrani, OnOdemeOrani = b.OnOdemeOrani, IndirimOrani = b.IndirimOrani,
        PuanOrani = b.PuanOrani, MailAdres = b.MailAdres, OtomatikMailGitme = b.OtomatikMailGitme,
        RiskAnalizYapma = b.RiskAnalizYapma, SubeGor = b.SubeGor, AcenteFiyatDegistir = b.AcenteFiyatDegistir,
        Gizle = b.Gizle, SadeceMusteriOdeme = b.SadeceMusteriOdeme,
    };

    private static HesapKoduInput AccountCodeInputOf(AccountCodeRequest b)
        => new() { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aciklama = F(b.Aciklama), Aktif = b.Aktif };
}
