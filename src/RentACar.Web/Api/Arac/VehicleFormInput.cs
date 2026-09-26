using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Arac;

/// <summary>
/// F6.1a — araç kartı girdisinin UÇ katmanı kuralları: kolon sınırları (varchar + numeric(19,4) — 22001/22003 500'e
/// düşmesin), enum adları, tarihlerin UTC'ye çevrilmesi, hedef şubenin kapsamı ve not alanındaki cari kimliğinin
/// varlığı. İş kuralları (plaka zorunlu/benzersiz, model yılı, negatif bedel, SIPP 4 harf) <see cref="VehicleService"/>'te.
/// </summary>
internal static class VehicleFormInput
{
    /// <summary>Servis mesajı → alan (AlanlariEsle). Plaka çakışması alt tip olduğu için uçta ayrıca çevrilir.</summary>
    public static readonly (string, string)[] Rules =
    [
        ("Plaka zorunludur", "plaka"),
        ("KM negatif olamaz", "km"),
        ("Model yılı", "modelYili"),
        ("SIPP kodu", "sipp"),
        ("Alım bedeli negatif", "alimBedeli"),
        ("İkinci el değeri negatif", "ikinciElDeger"),
        ("Vitrin adedi", "vitrinAdet"),
        // #278 L4 — negatif vergi/maliyet/kur ve makul tarih aralığı (VehicleService.Validate mesajları).
        ("Alış vergisiz tutarı", "alisVergisiz"), ("Alış ÖTV", "alisOtv"), ("Alış KDV", "alisKdv"),
        ("Aylık maliyet", "aylikMaliyet"), ("Filo yönetim maliyeti", "filoYonetimMaliyeti"), ("Kira fiyatı", "kiraFiyat"),
        ("TSB kasko değeri", "tsbKaskoDegeri"), ("Alış EUR fiyatı", "alisEuroFiyat"), ("Satış EUR fiyatı", "satisEuroFiyat"),
        ("Alım bedeli kuru", "alimBedeliKur"), ("Araç 2. fiyat kuru", "arac2FiyatKur"), ("Şimdiki kur", "simdiKur"),
        ("Döviz aylık maliyet", "aylikMaliyetDoviz"),
        ("Tescil tarihi", "tescilTarihi"), ("Alım tarihi", "alimTarihi"), ("Filo giriş tarihi", "filoGirisTarih"),
        ("Filo çıkış tarihi", "filoCikisTarih"), ("Son teslim tarihi", "sonTeslimTarihi"), ("Kira bitiş tarihi", "kiraBitTar"),
        ("Kira beklenen tarihi", "kiraBekTar"), ("Son bakım tarihi", "sonBakimTarih"), ("Kapatma tarihi", "kapatmaTarih"),
        ("Çıkması planlanan tarih", "cikmasiPlananTarih"),
    ];

    /// <summary>Metin kolonları: (değer seçici, uzunluk, alan, etiket). Config'te sınırı olmayan metinler 256'da kesilir.</summary>
    private static readonly (Func<AracIstegi, string?> Al, int En, string Alan, string Etiket)[] Texts =
    [
        (i => i.Plaka, 16, "plaka", "Plaka"), (i => i.Marka, 64, "marka", "Marka"), (i => i.Tip, 64, "tip", "Tip"),
        (i => i.Grup, 64, "grup", "Grup"), (i => i.Segment, 64, "segment", "Segment"), (i => i.Sipp, 8, "sipp", "SIPP"),
        (i => i.Renk, 32, "renk", "Renk"), (i => i.SasiNo, 32, "sasiNo", "Şasi no"), (i => i.MotorNo, 32, "motorNo", "Motor no"),
        (i => i.Sube, 64, "sube", "Şube"), (i => i.RuhsatNo, 32, "ruhsatNo", "Ruhsat no"),
        (i => i.AracSahibi, 128, "aracSahibi", "Araç sahibi"),
        (i => i.OzelKod1, 64, "ozelKod1", "Özel kod 1"), (i => i.OzelKod2, 64, "ozelKod2", "Özel kod 2"),
        (i => i.OzelKod3, 64, "ozelKod3", "Özel kod 3"), (i => i.OzelKod4, 64, "ozelKod4", "Özel kod 4"),
        (i => i.OzelKod5, 64, "ozelKod5", "Özel kod 5"),
        (i => i.BelgeNo, 256, "belgeNo", "Belge no"), (i => i.RuhsatSahibi, 128, "ruhsatSahibi", "Ruhsat sahibi"),
        (i => i.SozNo, 64, "sozNo", "Sözleşme no"), (i => i.AraciAlan, 128, "araciAlan", "Aracı alan"),
        (i => i.Kiralayan, 128, "kiralayan", "Kiralayan"), (i => i.AssistanFirma, 128, "assistanFirma", "Asistans firma"),
        (i => i.TsbKodu, 32, "tsbKodu", "TSB kodu"), (i => i.OdemeSekli, 64, "odemeSekli", "Ödeme şekli"),
        (i => i.PasifSebep, 256, "pasifSebep", "Pasif sebebi"), (i => i.SonDurum, 256, "sonDurum", "Son durum"),
        (i => i.HgsFirma, 128, "hgsFirma", "HGS firma"),
        (i => i.HgsNo, 256, "hgsNo", "HGS no"), (i => i.OgsNo, 256, "ogsNo", "OGS no"),
        (i => i.KasaTipi, 256, "kasaTipi", "Kasa tipi"), (i => i.DetayTipi, 256, "detayTipi", "Detay tipi"),
        (i => i.AlimFaturaNo, 256, "alimFaturaNo", "Alım fatura no"),
        (i => i.AlimYapilanFirma, 256, "alimYapilanFirma", "Alım yapılan firma"),
        (i => i.LastikDurumu, 256, "lastikDurumu", "Lastik durumu"),
        (i => i.TsrbMarkaKodu, 32, "tsrbMarkaKodu", "TSRB marka kodu"), (i => i.TsrbTipKodu, 32, "tsrbTipKodu", "TSRB tip kodu"),
        (i => i.AltGrupAdi, 64, "altGrupAdi", "Alt grup adı"), (i => i.EntegrasyonKodu, 64, "entegrasyonKodu", "Entegrasyon kodu"),
        (i => i.TeypKodu, 64, "teypKodu", "Teyp kodu"), (i => i.TakipMarka, 64, "takipMarka", "Takip marka"),
        (i => i.TakipNo, 64, "takipNo", "GPS takip no"), (i => i.SahipGrup, 64, "sahipGrup", "Sahip grup"),
        (i => i.AracSahibiNo, 64, "aracSahibiNo", "Araç sahibi no"), (i => i.AracSahibi2, 128, "aracSahibi2", "2. araç sahibi"),
        (i => i.KrediFirma, 128, "krediFirma", "Kredi firma"), (i => i.Aciklama, 1024, "aciklama", "Açıklama"),
        (i => i.Konum, 128, "konum", "Konum"),
    ];

    /// <summary>numeric(19,4) kolonları — uç sınırı <see cref="RentalLimits.MaxAmount"/>.</summary>
    private static readonly (Func<AracIstegi, decimal?> Al, string Alan, string Etiket)[] Amounts =
    [
        (i => i.AlimBedeli, "alimBedeli", "Alım bedeli"), (i => i.AlisVergisiz, "alisVergisiz", "Alış vergisiz"),
        (i => i.AlisOtv, "alisOtv", "Alış ÖTV"), (i => i.AlisKdv, "alisKdv", "Alış KDV"),
        (i => i.AylikMaliyet, "aylikMaliyet", "Aylık maliyet"),
        (i => i.FiloYonetimMaliyeti, "filoYonetimMaliyeti", "Filo yönetim maliyeti"),
        (i => i.IkinciElDeger, "ikinciElDeger", "İkinci el değeri"), (i => i.KiraFiyat, "kiraFiyat", "Kira fiyatı"),
        (i => i.TsbKaskoDegeri, "tsbKaskoDegeri", "TSB kasko değeri"), (i => i.AlisEuroFiyat, "alisEuroFiyat", "Alış EUR fiyatı"),
        (i => i.SatisEuroFiyat, "satisEuroFiyat", "Satış EUR fiyatı"), (i => i.AlimBedeliKur, "alimBedeliKur", "Alım bedeli kuru"),
        (i => i.Arac2FiyatKur, "arac2FiyatKur", "Araç 2. fiyat kuru"), (i => i.SimdiKur, "simdiKur", "Şimdiki kur"),
        (i => i.AylikMaliyetDoviz, "aylikMaliyetDoviz", "Aylık maliyet döviz"),
    ];

    /// <summary>Tamsayı alanlarının gerçekçilik sınırı (int taşması bağlamada zaten 400; burada anlamsız büyüklük).</summary>
    private const int MaxKm = 10_000_000;

    public static void Limit(AracIstegi i)
    {
        foreach (var (get, en, alan, label) in Texts) RentalLimits.Text(get(i), en, alan, label);
        foreach (var (get, alan, label) in Amounts) RentalLimits.Amount(get(i), alan, label);
        foreach (var (value, alan, label) in new (int?, string, string)[]
                 {
                     (i.Km, "km", "KM"), (i.SonTeslimKm, "sonTeslimKm", "Son teslim KM"), (i.DisKmLimit, "disKmLimit", "Dış KM limiti"),
                     (i.KiraKmLimiti, "kiraKmLimiti", "Kira KM limiti"), (i.SonBakimKm, "sonBakimKm", "Son bakım KM"),
                     (i.AracSatisKm, "aracSatisKm", "Satış KM"), (i.KiraGun, "kiraGun", "Kira gün"),
                     (i.MotorGucu, "motorGucu", "Motor gücü"), (i.SilindirHacmi, "silindirHacmi", "Silindir hacmi"),
                 })
            if (value is { } d && (d < 0 || d > MaxKm))
                throw new ValidationException($"{label} 0 ile {MaxKm:N0} arasında olmalıdır.", alan);
    }

    /// <summary>
    /// Hedef şube GİRİŞ NOKTASINDA kapsamdan geçer (F5 <c>CikisOfisiKapsamiAsync</c> deseni): şubeye bağlı kullanıcı
    /// aracı başka şubeye açamaz/taşıyamaz ve şubesiz ("yetim", kendisinin de göremeyeceği) araç açamaz. Blazor formu bu
    /// kontrolü yapmıyor; yeni yüzey o açığı taşımaz.
    /// </summary>
    public static async Task TargetBranchScopeAsync(IBranchRepository branches, ICurrentUser user, string? branch, CancellationToken ct)
    {
        var s = F5Shared.Nz(branch);
        if (s is null)
        {
            if (!BranchScope.EffectiveFilter(user).Unrestricted)
                throw new ValidationException("Şube zorunludur (şubeye bağlı kullanıcı kendi şubesini seçmelidir).", "sube");
            return;
        }
        BranchScope.RequireInScope(user, (await branches.FindByNameAsync(s, ct))?.Id, s);
    }

    /// <summary>Not alanındaki cari bu kiracıda olmalı (RLS kapsamlı okuma; başka kiracının kimliği "yok"tur).</summary>
    public static async Task CustomerExistenceAsync(IDbContextFactory<AppDbContext> dbf, Guid? customerId, CancellationToken ct)
    {
        if (customerId is not { } id) return;
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Customers.AsNoTracking().AnyAsync(c => c.Id == id, ct))
            throw new ValidationException("Seçilen kiralayan cari bulunamadı.", "kiraMusteriId");
    }

    private static DateTimeOffset? U(DateTimeOffset? an) => F5Shared.Utc(an);

    /// <summary>İstek → servis girdisi (enum adları doğrulanır, tarihler UTC).</summary>
    public static VehicleInput Input(AracIstegi i) => new()
    {
        Plaka = i.Plaka ?? string.Empty,
        Marka = i.Marka, Tip = i.Tip, Grup = i.Grup, GrupBilincliBos = i.GrupBilincliBos, VitrinAdet = i.VitrinAdet,
        Segment = i.Segment, Sipp = i.Sipp, Renk = i.Renk, ModelYili = i.ModelYili,
        Vites = F5Shared.EnumAdi<Transmission>(i.Vites, "vites"),
        SasiNo = i.SasiNo, MotorNo = i.MotorNo, Sube = i.Sube,
        Durum = F5Shared.EnumAdi<VehicleStatus>(i.Durum, "durum") ?? VehicleStatus.Musait,
        FiloDurum = F5Shared.EnumAdi<FleetLifecycleStatus>(i.FiloDurum, "filoDurum"),
        Km = i.Km,
        Yakit = F5Shared.EnumAdi<FuelType>(i.Yakit, "yakit"),
        MotorGucu = i.MotorGucu, SilindirHacmi = i.SilindirHacmi, RuhsatNo = i.RuhsatNo, TescilTarihi = U(i.TescilTarihi),
        AracSahibi = i.AracSahibi, AlimBedeli = i.AlimBedeli, AlimTarihi = U(i.AlimTarihi), AlisVergisiz = i.AlisVergisiz,
        AlisOtv = i.AlisOtv, AlisKdv = i.AlisKdv, AylikMaliyet = i.AylikMaliyet, FiloYonetimMaliyeti = i.FiloYonetimMaliyeti,
        IkinciElDeger = i.IkinciElDeger, FiloGirisTarih = U(i.FiloGirisTarih), FiloCikisTarih = U(i.FiloCikisTarih),
        OzelKod1 = i.OzelKod1, OzelKod2 = i.OzelKod2, OzelKod3 = i.OzelKod3, OzelKod4 = i.OzelKod4, OzelKod5 = i.OzelKod5,
        BelgeNo = i.BelgeNo, RuhsatSahibi = i.RuhsatSahibi, SozNo = i.SozNo, AraciAlan = i.AraciAlan, Kiralayan = i.Kiralayan,
        AssistanFirma = i.AssistanFirma, TsbKodu = i.TsbKodu, OdemeSekli = i.OdemeSekli, PasifSebep = i.PasifSebep,
        SonDurum = i.SonDurum, HgsFirma = i.HgsFirma, SonTeslimKm = i.SonTeslimKm, KiraGun = i.KiraGun,
        DisKmLimit = i.DisKmLimit, KiraFiyat = i.KiraFiyat, TsbKaskoDegeri = i.TsbKaskoDegeri,
        AlisEuroFiyat = i.AlisEuroFiyat, SatisEuroFiyat = i.SatisEuroFiyat, SonTeslimTarihi = U(i.SonTeslimTarihi),
        KiraBitTar = U(i.KiraBitTar), KiraBekTar = U(i.KiraBekTar), KiraMusteriId = i.KiraMusteriId, AlisEuro = i.AlisEuro,
        HgsNo = i.HgsNo, OgsNo = i.OgsNo, KasaTipi = i.KasaTipi, DetayTipi = i.DetayTipi, AlimFaturaNo = i.AlimFaturaNo,
        AlimYapilanFirma = i.AlimYapilanFirma, KiraKmLimiti = i.KiraKmLimiti,
        WebRezKapat = i.WebRezKapat, OfisRezKapat = i.OfisRezKapat, ZIzni = i.ZIzni, Utts = i.Utts,
        KarLastigi = i.KarLastigi, YedekAnahtar = i.YedekAnahtar, Temizlik = i.Temizlik, Rehin = i.Rehin,
        SonBakimTarih = U(i.SonBakimTarih), SonBakimKm = i.SonBakimKm, LastikDurumu = i.LastikDurumu,
        TsrbMarkaKodu = i.TsrbMarkaKodu, TsrbTipKodu = i.TsrbTipKodu, AltGrupAdi = i.AltGrupAdi,
        EntegrasyonKodu = i.EntegrasyonKodu, TeypKodu = i.TeypKodu, TakipMarka = i.TakipMarka, TakipNo = i.TakipNo,
        SahipGrup = i.SahipGrup, AracSahibiNo = i.AracSahibiNo, AracSahibi2 = i.AracSahibi2, KrediFirma = i.KrediFirma,
        KapatmaTarih = U(i.KapatmaTarih), CikmasiPlananTarih = U(i.CikmasiPlananTarih), AracSatisKm = i.AracSatisKm,
        Aciklama = i.Aciklama, Konum = i.Konum, AlimBedeliKur = i.AlimBedeliKur, Arac2FiyatKur = i.Arac2FiyatKur,
        SimdiKur = i.SimdiKur, AylikMaliyetDoviz = i.AylikMaliyetDoviz,
    };

    /// <summary>Varlık → kart (PUT'ta geri gönderilince hiçbir alan kaymaz: aynı alan kümesi).</summary>
    public static AracKartDto Card(Domain.Entities.Vehicle v, string? version) => new()
    {
        Id = v.Id, Surum = version, SubeId = v.SubeId, CreatedAtUtc = v.CreatedAtUtc, UpdatedAtUtc = v.UpdatedAtUtc,
        Plaka = v.Plaka, Marka = v.Marka, Tip = v.Tip, Grup = v.Grup, GrupBilincliBos = v.Grup is null,
        VitrinAdet = v.VitrinAdet, Segment = v.Segment, Sipp = v.Sipp, Renk = v.Renk, ModelYili = v.ModelYili,
        Vites = v.Vites?.ToString(), SasiNo = v.SasiNo, MotorNo = v.MotorNo, Sube = v.Sube, Durum = v.Durum.ToString(),
        FiloDurum = v.FiloDurum?.ToString(), Km = v.Km, Yakit = v.Yakit?.ToString(),
        MotorGucu = v.MotorGucu, SilindirHacmi = v.SilindirHacmi, RuhsatNo = v.RuhsatNo, TescilTarihi = v.TescilTarihi,
        AracSahibi = v.AracSahibi, AlimBedeli = v.AlimBedeli, AlimTarihi = v.AlimTarihi, AlisVergisiz = v.AlisVergisiz,
        AlisOtv = v.AlisOtv, AlisKdv = v.AlisKdv, AylikMaliyet = v.AylikMaliyet, FiloYonetimMaliyeti = v.FiloYonetimMaliyeti,
        IkinciElDeger = v.IkinciElDeger, FiloGirisTarih = v.FiloGirisTarih, FiloCikisTarih = v.FiloCikisTarih,
        OzelKod1 = v.OzelKod1, OzelKod2 = v.OzelKod2, OzelKod3 = v.OzelKod3, OzelKod4 = v.OzelKod4, OzelKod5 = v.OzelKod5,
        BelgeNo = v.BelgeNo, RuhsatSahibi = v.RuhsatSahibi, SozNo = v.SozNo, AraciAlan = v.AraciAlan, Kiralayan = v.Kiralayan,
        AssistanFirma = v.AssistanFirma, TsbKodu = v.TsbKodu, OdemeSekli = v.OdemeSekli, PasifSebep = v.PasifSebep,
        SonDurum = v.SonDurum, HgsFirma = v.HgsFirma, SonTeslimKm = v.SonTeslimKm, KiraGun = v.KiraGun,
        DisKmLimit = v.DisKmLimit, KiraFiyat = v.KiraFiyat, TsbKaskoDegeri = v.TsbKaskoDegeri,
        AlisEuroFiyat = v.AlisEuroFiyat, SatisEuroFiyat = v.SatisEuroFiyat, SonTeslimTarihi = v.SonTeslimTarihi,
        KiraBitTar = v.KiraBitTar, KiraBekTar = v.KiraBekTar, KiraMusteriId = v.KiraMusteriId, AlisEuro = v.AlisEuro,
        HgsNo = v.HgsNo, OgsNo = v.OgsNo, KasaTipi = v.KasaTipi, DetayTipi = v.DetayTipi, AlimFaturaNo = v.AlimFaturaNo,
        AlimYapilanFirma = v.AlimYapilanFirma, KiraKmLimiti = v.KiraKmLimiti,
        WebRezKapat = v.WebRezKapat, OfisRezKapat = v.OfisRezKapat, ZIzni = v.ZIzni, Utts = v.Utts,
        KarLastigi = v.KarLastigi, YedekAnahtar = v.YedekAnahtar, Temizlik = v.Temizlik, Rehin = v.Rehin,
        SonBakimTarih = v.SonBakimTarih, SonBakimKm = v.SonBakimKm, LastikDurumu = v.LastikDurumu,
        TsrbMarkaKodu = v.TsrbMarkaKodu, TsrbTipKodu = v.TsrbTipKodu, AltGrupAdi = v.AltGrupAdi,
        EntegrasyonKodu = v.EntegrasyonKodu, TeypKodu = v.TeypKodu, TakipMarka = v.TakipMarka, TakipNo = v.TakipNo,
        SahipGrup = v.SahipGrup, AracSahibiNo = v.AracSahibiNo, AracSahibi2 = v.AracSahibi2, KrediFirma = v.KrediFirma,
        KapatmaTarih = v.KapatmaTarih, CikmasiPlananTarih = v.CikmasiPlananTarih, AracSatisKm = v.AracSatisKm,
        Aciklama = v.Aciklama, Konum = v.Konum, AlimBedeliKur = v.AlimBedeliKur, Arac2FiyatKur = v.Arac2FiyatKur,
        SimdiKur = v.SimdiKur, AylikMaliyetDoviz = v.AylikMaliyetDoviz,
    };
}
