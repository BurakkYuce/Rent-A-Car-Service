using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// F7.1 — cari kartı girdisinin UÇ katmanı kuralları: kolon sınırları (varchar + numeric — 22001/22003 500'e düşmesin),
/// enum adı, tarihlerin UTC'ye çevrilmesi ve KVKK "gizli alan korunur" birleşimi. İş kuralları (Ad/Ünvan zorunlu, TC
/// sağlama, benzersizlik, e-posta, negatif vade/risk) <see cref="CustomerService"/>'te.
/// </summary>
internal static class CustomerInputMapper
{
    /// <summary>Servis mesajı → alan (AlanlariEsle).</summary>
    public static readonly (string, string)[] FieldRules =
    [
        ("Bireysel cari için Ad", "ad"), ("TC Kimlik No geçersiz", "tcKimlik"), ("Kurumsal/Servis cari için Ünvan", "unvan"),
        ("Vergi No", "vergiNo"), ("E-posta adresi geçersiz", "email"), ("Doğum tarihi", "dogumTarihi"),
        ("Vade günü", "vadeGun"), ("Risk limiti", "riskLimiti"), ("Tevkifat oranı", "tevkifatOrani"),
    ];

    private const int MaxContacts = 30;

    /// <summary>Metin kolonları: (değer seçici, uzunluk, alan, etiket) — CustomerConfigs uzunlukları.</summary>
    private static readonly (Func<CustomerRequest, string?> Get, int Max, string Field, string Label)[] Texts =
    [
        (r => r.Ad, 128, "ad", "Ad"), (r => r.Soyad, 128, "soyad", "Soyad"), (r => r.Unvan, 256, "unvan", "Ünvan"),
        (r => r.VergiDairesi, 128, "vergiDairesi", "Vergi dairesi"), (r => r.VergiNo, 16, "vergiNo", "Vergi No"),
        (r => r.TcKimlik, 20, "tcKimlik", "TC Kimlik No"), (r => r.CepTel, 32, "cepTel", "Cep telefonu"),
        (r => r.Gsm2, 32, "gsm2", "GSM 2"), (r => r.Email, 256, "email", "E-posta"), (r => r.Il, 64, "il", "İl"),
        (r => r.Ilce, 64, "ilce", "İlçe"), (r => r.Adres, 512, "adres", "Adres"), (r => r.Kaynak, 64, "kaynak", "Kaynak"),
        (r => r.MusteriTemsilcisi, 128, "musteriTemsilcisi", "Müşteri temsilcisi"),
        (r => r.UyariNedeni, 256, "uyariNedeni", "Uyarı nedeni"), (r => r.EhliyetNo, 32, "ehliyetNo", "Ehliyet no"),
        (r => r.EhliyetSinifi, 16, "ehliyetSinifi", "Ehliyet sınıfı"), (r => r.EhliyetYeri, 64, "ehliyetYeri", "Ehliyet yeri"),
        (r => r.Tarife, 64, "tarife", "Tarife"), (r => r.RiskMesaji, 256, "riskMesaji", "Risk mesajı"),
        (r => r.HgsYansitmaTuru, 32, "hgsYansitmaTuru", "HGS yansıtma türü"),
        (r => r.OzelCariTip, 64, "ozelCariTip", "Özel cari tipi"), (r => r.MusteriTipi, 64, "musteriTipi", "Müşteri tipi"),
        (r => r.EhliyetUlke, 64, "ehliyetUlke", "Ehliyet ülkesi"), (r => r.Dil, 16, "dil", "Dil"), (r => r.Doviz, 8, "doviz", "Döviz"),
        (r => r.TevkifatDurum, 64, "tevkifatDurum", "Tevkifat durumu"), (r => r.Sinif, 32, "sinif", "Sınıf"),
        (r => r.BabaAdi, 128, "babaAdi", "Baba adı"), (r => r.AnaAdi, 128, "anaAdi", "Ana adı"),
        (r => r.PasaportNo, 32, "pasaportNo", "Pasaport no"), (r => r.FaturaDonemi, 32, "faturaDonemi", "Fatura dönemi"),
        (r => r.EkAdres, 512, "ekAdres", "Ek adres"), (r => r.BankaIban, 34, "bankaIban", "IBAN"),
        (r => r.BankaAdi, 128, "bankaAdi", "Banka adı"), (r => r.FaturaAdresi, 512, "faturaAdresi", "Fatura adresi"),
        (r => r.FaturaUnvan, 256, "faturaUnvan", "Fatura ünvanı"), (r => r.Ulke, 64, "ulke", "Ülke"), (r => r.Tel2, 32, "tel2", "Telefon 2"),
        (r => r.OzelKod, 64, "ozelKod", "Özel kod"), (r => r.EntegrasyonKodu, 64, "entegrasyonKodu", "Entegrasyon kodu"),
        (r => r.Aciklama, 1024, "aciklama", "Açıklama"), (r => r.RiskIzin, 64, "riskIzin", "Risk izni"),
        (r => r.DogumYeri, 128, "dogumYeri", "Doğum yeri"), (r => r.PasaportYeri, 128, "pasaportYeri", "Pasaport yeri"),
        (r => r.KurumsalNo, 64, "kurumsalNo", "Kurumsal no"), (r => r.UyariSerbest, 512, "uyariSerbest", "Serbest uyarı"),
        (r => r.TevkifatKodu, 32, "tevkifatKodu", "Tevkifat kodu"),
        (r => r.FaturaKiralayanIsim, 256, "faturaKiralayanIsim", "Faturada kiralayan ismi"),
        (r => r.IsAdresi, 512, "isAdresi", "İş adresi"), (r => r.IsTelefonu, 32, "isTelefonu", "İş telefonu"),
        (r => r.KayitliIl, 64, "kayitliIl", "Kayıtlı il"), (r => r.KayitliIlce, 64, "kayitliIlce", "Kayıtlı ilçe"),
        (r => r.MahalleKoy, 128, "mahalleKoy", "Mahalle/köy"), (r => r.SeriNo, 32, "seriNo", "Seri no"),
        (r => r.CiltNo, 32, "ciltNo", "Cilt no"), (r => r.AileSira, 32, "aileSira", "Aile sıra"), (r => r.SiraNo, 32, "siraNo", "Sıra no"),
        (r => r.Sifre, 128, "sifre", "Şifre"),
    ];

    public static void Limit(CustomerRequest r)
    {
        foreach (var (get, max, field, label) in Texts) Sinirlar.Metin(get(r), max, field, label);
        Sinirlar.Tutar(r.RiskLimiti, "riskLimiti", "Risk limiti");
        Sinirlar.Tutar(r.BayiKomisyon, "bayiKomisyon", "Bayi komisyonu");
        Sinirlar.Tutar(r.WebIndirim, "webIndirim", "Web indirimi");
        if (r.VadeGun > 3650) throw new ValidationException("Vade günü en fazla 3.650 olabilir.", "vadeGun");
        var contacts = r.Kisiler ?? [];
        if (contacts.Count > MaxContacts)
            throw new ValidationException($"En fazla {MaxContacts} yetkili kişi girilebilir.", "kisiler");
        foreach (var k in contacts)
        {
            Sinirlar.Metin(k.AdSoyad, 128, "kisiler", "Yetkili ad soyad");
            Sinirlar.Metin(k.Telefon, 32, "kisiler", "Yetkili telefon");
            Sinirlar.Metin(k.Mail, 256, "kisiler", "Yetkili e-posta");
            Sinirlar.Metin(k.Gorev, 64, "kisiler", "Yetkili görevi");
        }
    }

    /// <summary>
    /// #295 H1: vergi no yanıtta DÜZ gösterilmez — bireysel caride (şahıs vergi no = TC olabilir) ya da tipten bağımsız
    /// 11 rakam taşıyan değerde (eski kayıtta VergiNo alanına yazılmış TC). Böyle bir değer yalnız maskeli döner.
    /// </summary>
    public static bool TaxNumberHidden(CariType type, string? taxNumber)
        => type == CariType.Bireysel || LooksLikeTc(taxNumber);

    public static bool TaxNumberHidden(Customer c) => TaxNumberHidden(c.Tip, c.VergiNo);

    /// <summary>
    /// #295b L-A: karar YALNIZ rakamlara bakar — biçimli yazılmış eski TC ("123 456 789 01", "123-45678901", sonda
    /// boşluk) da 11 rakam taşır ve gizli sayılır. SQL karşılığı <c>CustomerRepository.ElevenDigitsPattern</c>.
    /// </summary>
    public static bool LooksLikeTc(string? value) => value is not null && value.Count(char.IsAsciiDigit) == 11;

    /// <summary>
    /// #295 H1: Bireysel → Kurumsal/Servis geçişinde saklı vergi no gizliydi (kart göstermedi). İstek yeni değer ya da
    /// "" (temizle) getirmezse "koru" kararı gizli değeri yeni tipte düz gösterime taşırdı → 400 <c>errors[vergiNo]</c>.
    /// </summary>
    public static void RequireTaxNumberOnTypeChange(Customer stored, CustomerRequest r)
    {
        var newType = F5Ortak.EnumAdi<CariType>(r.Tip, "tip") ?? CariType.Bireysel;
        if (stored.Tip == CariType.Bireysel && newType != CariType.Bireysel && !string.IsNullOrEmpty(stored.VergiNo)
            && r.VergiNo is null)
            throw new ValidationException("Tür değişikliğinde vergi no yeniden girilmeli.", "vergiNo");
    }

    /// <summary>
    /// İstek → servis girdisi. <paramref name="stored"/> (güncellemede, çözülmüş kayıt) verilirse gizli alanlar
    /// korunur: TC/ehliyet/pasaport <c>null</c> → mevcut değer; <c>Anonim*</c> bayrağı KAYITLI grubun alanı <c>null</c>
    /// → mevcut değer (kart o alanı göstermediği için PUT'un onu sessizce silmesi engellenir).
    /// </summary>
    public static CustomerInput ToInput(CustomerRequest r, Customer? stored)
    {
        string? Keep(string? value, bool hidden, string? old) => value is null && hidden ? old : value;
        DateTimeOffset? KeepDate(DateTimeOffset? value, bool hidden, DateTimeOffset? old) => value is null && hidden ? old : value;
        var s = stored;
        bool name = s?.AnonimAd == true, phone = s?.AnonimTelefon == true, mail = s?.AnonimMail == true,
            address = s?.AnonimAdres == true, document = s?.AnonimBelge == true;
        return new CustomerInput
        {
            Tip = F5Ortak.EnumAdi<CariType>(r.Tip, "tip") ?? CariType.Bireysel,
            Ad = Keep(r.Ad, name, s?.Ad), Soyad = Keep(r.Soyad, name, s?.Soyad), Unvan = Keep(r.Unvan, name, s?.Unvan),
            // Gizli numaralar: null = değiştirme (kart göstermez); "" servis normalizasyonunda null'a (temizle) döner.
            TcKimlik = r.TcKimlik ?? s?.TcKimlik,
            EhliyetNo = r.EhliyetNo ?? s?.EhliyetNo,
            PasaportNo = r.PasaportNo ?? s?.PasaportNo,
            VergiDairesi = r.VergiDairesi,
            // #283 M3: an individual's tax number is returned masked only → null keeps the stored value, "" clears.
            VergiNo = Keep(r.VergiNo, s is not null && TaxNumberHidden(s), s?.VergiNo),
            CepTel = Keep(r.CepTel, phone, s?.CepTel), Gsm2 = Keep(r.Gsm2, phone, s?.Gsm2), Email = Keep(r.Email, mail, s?.Email),
            Il = Keep(r.Il, address, s?.Il), Ilce = Keep(r.Ilce, address, s?.Ilce), Adres = Keep(r.Adres, address, s?.Adres),
            Kaynak = r.Kaynak, MusteriTemsilcisi = r.MusteriTemsilcisi, IysIzinli = r.IysIzinli, Uyari = r.Uyari,
            UyariNedeni = r.UyariNedeni,
            EhliyetSinifi = Keep(r.EhliyetSinifi, document, s?.EhliyetSinifi),
            EhliyetTarihi = F5Ortak.Utc(KeepDate(r.EhliyetTarihi, document, s?.EhliyetTarihi)),
            EhliyetYeri = Keep(r.EhliyetYeri, document, s?.EhliyetYeri),
            EhliyetUlke = Keep(r.EhliyetUlke, document, s?.EhliyetUlke),
            PasaportYeri = Keep(r.PasaportYeri, document, s?.PasaportYeri),
            Tarife = r.Tarife, VadeGun = r.VadeGun, RiskLimiti = r.RiskLimiti, RiskMesaji = r.RiskMesaji,
            RiskTarihi = F5Ortak.Utc(r.RiskTarihi), HgsYansitmaTuru = r.HgsYansitmaTuru, KaraListe = r.KaraListe, Pasif = r.Pasif,
            OzelCariTip = r.OzelCariTip, MusteriTipi = r.MusteriTipi, Dil = r.Dil, Doviz = r.Doviz,
            TevkifatDurum = r.TevkifatDurum, Sinif = r.Sinif, MailIzin = r.MailIzin, SmsIzin = r.SmsIzin,
            TelefonIzin = r.TelefonIzin, DogumTarihi = F5Ortak.Utc(r.DogumTarihi),
            BabaAdi = Keep(r.BabaAdi, name, s?.BabaAdi), AnaAdi = Keep(r.AnaAdi, name, s?.AnaAdi),
            FaturaDonemi = r.FaturaDonemi, TevkifatOrani = r.TevkifatOrani,
            Kisiler = (r.Kisiler ?? []).Select(k => new CustomerContactInput
            { AdSoyad = k.AdSoyad, Telefon = k.Telefon, Mail = k.Mail, Gorev = k.Gorev }).ToList(),
            KvkkOnay = r.KvkkOnay, KvkkOnayTarih = F5Ortak.Utc(r.KvkkOnayTarih), EkAdres = Keep(r.EkAdres, address, s?.EkAdres),
            BankaIban = r.BankaIban, BankaAdi = r.BankaAdi, FaturaAdresi = Keep(r.FaturaAdresi, address, s?.FaturaAdresi),
            FaturaUnvan = Keep(r.FaturaUnvan, name, s?.FaturaUnvan), Ulke = r.Ulke, Tel2 = Keep(r.Tel2, phone, s?.Tel2),
            OzelKod = r.OzelKod, EntegrasyonKodu = r.EntegrasyonKodu, Aciklama = r.Aciklama, RiskIzin = r.RiskIzin,
            DogumYeri = Keep(r.DogumYeri, document, s?.DogumYeri), KurumsalNo = r.KurumsalNo, UyariSerbest = r.UyariSerbest,
            TevkifatKodu = r.TevkifatKodu, FaturaKiralayanIsim = Keep(r.FaturaKiralayanIsim, name, s?.FaturaKiralayanIsim),
            IsAdresi = Keep(r.IsAdresi, address, s?.IsAdresi), IsTelefonu = Keep(r.IsTelefonu, phone, s?.IsTelefonu),
            KayitliIl = Keep(r.KayitliIl, address, s?.KayitliIl), KayitliIlce = Keep(r.KayitliIlce, address, s?.KayitliIlce),
            MahalleKoy = Keep(r.MahalleKoy, address, s?.MahalleKoy),
            SeriNo = Keep(r.SeriNo, document, s?.SeriNo), CiltNo = Keep(r.CiltNo, document, s?.CiltNo),
            AileSira = Keep(r.AileSira, document, s?.AileSira), SiraNo = Keep(r.SiraNo, document, s?.SiraNo),
            TcDogrulama = r.TcDogrulama, FaturaAdresFarkli = r.FaturaAdresFarkli, FaturaTekSatir = r.FaturaTekSatir,
            DogumGunuTakip = r.DogumGunuTakip, AnonimAd = r.AnonimAd, AnonimTc = r.AnonimTc, AnonimTelefon = r.AnonimTelefon,
            AnonimMail = r.AnonimMail, AnonimAdres = r.AnonimAdres, AnonimBelge = r.AnonimBelge, BakiyeGor = r.BakiyeGor,
            AracVerilmez = r.AracVerilmez, YasEhliyetSerbest = r.YasEhliyetSerbest, MerkezKurumsal = r.MerkezKurumsal,
            Broker = r.Broker, FindexZorunlu = r.FindexZorunlu, BayiKomisyon = r.BayiKomisyon,
            PasaportTarihi = F5Ortak.Utc(KeepDate(r.PasaportTarihi, document, s?.PasaportTarihi)),
            WebIndirim = r.WebIndirim, KaraZamani = F5Ortak.Utc(r.KaraZamani), IslemSubeId = r.IslemSubeId, FirmaId = r.FirmaId,
            Sifre = r.Sifre,
        };
    }

    /// <summary>
    /// Kayıt (çözülmüş) → kart. TC ASLA; ehliyet/pasaport (ve bireysel caride vergi no) maskeli; <c>Anonim*</c> grupları
    /// <c>null</c>. Gruplar birincil alanlarda <see cref="MusteriGorunumu"/> ile aynı, kartta İKİNCİL alanları da kapsar
    /// (#283 M2) — <see cref="ToInput"/> aynı alanları korur:
    /// ad → ad/soyad/ünvan, fatura ünvanı, faturada kiralayan ismi, baba/ana adı; telefon → cep, GSM 2, telefon 2,
    /// iş telefonu; e-posta; adres → adres/il/ilçe, ek adres, fatura adresi, iş adresi, kayıtlı il/ilçe, mahalle/köy;
    /// belge → numaralar + sınıf/tarih/yer/ülke, pasaport yeri/tarihi, doğum yeri, nüfus seri/cilt/aile sıra/sıra no.
    /// </summary>
    public static CustomerCardDto ToCard(Customer c, string? version)
    {
        bool name = c.AnonimAd, phone = c.AnonimTelefon, mail = c.AnonimMail, address = c.AnonimAdres, document = c.AnonimBelge;
        return new CustomerCardDto
        {
            Id = c.Id, Surum = version, CreatedAtUtc = c.CreatedAtUtc, UpdatedAtUtc = c.UpdatedAtUtc,
            TcKimlikVar = !string.IsNullOrEmpty(c.TcKimlik) || !string.IsNullOrEmpty(c.TcKimlikHash),
            EhliyetNoMaske = document ? null : MusteriGorunumu.Maske(c.EhliyetNo),
            PasaportNoMaske = document ? null : MusteriGorunumu.Maske(c.PasaportNo),
            SifreVar = !string.IsNullOrEmpty(c.SifreHash),
            Tip = c.Tip.ToString(),
            Ad = name ? null : c.Ad, Soyad = name ? null : c.Soyad, Unvan = name ? null : c.Unvan,
            VergiDairesi = c.VergiDairesi,
            // #283 M3: an individual's tax number may be the TC → only masked.
            VergiNo = TaxNumberHidden(c) ? null : c.VergiNo,
            VergiNoMaske = TaxNumberHidden(c) ? MusteriGorunumu.Maske(c.VergiNo) : null,
            CepTel = phone ? null : c.CepTel, Gsm2 = phone ? null : c.Gsm2, Email = mail ? null : c.Email,
            Il = address ? null : c.Il, Ilce = address ? null : c.Ilce, Adres = address ? null : c.Adres,
            Kaynak = c.Kaynak, MusteriTemsilcisi = c.MusteriTemsilcisi, IysIzinli = c.IysIzinli, Uyari = c.Uyari,
            UyariNedeni = c.UyariNedeni,
            EhliyetSinifi = document ? null : c.EhliyetSinifi, EhliyetTarihi = document ? null : c.EhliyetTarihi,
            EhliyetYeri = document ? null : c.EhliyetYeri, EhliyetUlke = document ? null : c.EhliyetUlke,
            PasaportYeri = document ? null : c.PasaportYeri, PasaportTarihi = document ? null : c.PasaportTarihi,
            Tarife = c.Tarife, VadeGun = c.VadeGun, RiskLimiti = c.RiskLimiti, RiskMesaji = c.RiskMesaji,
            RiskTarihi = c.RiskTarihi, HgsYansitmaTuru = c.HgsYansitmaTuru, KaraListe = c.KaraListe, Pasif = c.Pasif,
            OzelCariTip = c.OzelCariTip, MusteriTipi = c.MusteriTipi, Dil = c.Dil, Doviz = c.Doviz,
            TevkifatDurum = c.TevkifatDurum, Sinif = c.Sinif, MailIzin = c.MailIzin, SmsIzin = c.SmsIzin,
            TelefonIzin = c.TelefonIzin, DogumTarihi = c.DogumTarihi,
            BabaAdi = name ? null : c.BabaAdi, AnaAdi = name ? null : c.AnaAdi,
            FaturaDonemi = c.FaturaDonemi, TevkifatOrani = c.TevkifatOrani,
            Kisiler = c.Kisiler.OrderBy(k => k.Sira).Select(k => new CustomerContactDto(k.AdSoyad, k.Telefon, k.Mail, k.Gorev)).ToList(),
            KvkkOnay = c.KvkkOnay, KvkkOnayTarih = c.KvkkOnayTarih, EkAdres = address ? null : c.EkAdres,
            BankaIban = c.BankaIban, BankaAdi = c.BankaAdi, FaturaAdresi = address ? null : c.FaturaAdresi,
            FaturaUnvan = name ? null : c.FaturaUnvan, Ulke = c.Ulke, Tel2 = phone ? null : c.Tel2,
            OzelKod = c.OzelKod, EntegrasyonKodu = c.EntegrasyonKodu, Aciklama = c.Aciklama, RiskIzin = c.RiskIzin,
            DogumYeri = document ? null : c.DogumYeri, KurumsalNo = c.KurumsalNo, UyariSerbest = c.UyariSerbest,
            TevkifatKodu = c.TevkifatKodu, FaturaKiralayanIsim = name ? null : c.FaturaKiralayanIsim,
            IsAdresi = address ? null : c.IsAdresi, IsTelefonu = phone ? null : c.IsTelefonu,
            KayitliIl = address ? null : c.KayitliIl, KayitliIlce = address ? null : c.KayitliIlce,
            MahalleKoy = address ? null : c.MahalleKoy, SeriNo = document ? null : c.SeriNo,
            CiltNo = document ? null : c.CiltNo, AileSira = document ? null : c.AileSira, SiraNo = document ? null : c.SiraNo,
            TcDogrulama = c.TcDogrulama,
            FaturaAdresFarkli = c.FaturaAdresFarkli, FaturaTekSatir = c.FaturaTekSatir, DogumGunuTakip = c.DogumGunuTakip,
            AnonimAd = c.AnonimAd, AnonimTc = c.AnonimTc, AnonimTelefon = c.AnonimTelefon, AnonimMail = c.AnonimMail,
            AnonimAdres = c.AnonimAdres, AnonimBelge = c.AnonimBelge, BakiyeGor = c.BakiyeGor, AracVerilmez = c.AracVerilmez,
            YasEhliyetSerbest = c.YasEhliyetSerbest, MerkezKurumsal = c.MerkezKurumsal, Broker = c.Broker,
            FindexZorunlu = c.FindexZorunlu, BayiKomisyon = c.BayiKomisyon, WebIndirim = c.WebIndirim,
            KaraZamani = c.KaraZamani, IslemSubeId = c.IslemSubeId, FirmaId = c.FirmaId,
        };
    }
}
