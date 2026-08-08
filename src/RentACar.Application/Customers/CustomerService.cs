using RentACar.Application.Common;
using RentACar.Application.Authorization;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Domain.Validation;

namespace RentACar.Application.Customers;

/// <summary>
/// Cari iş mantığı: türe göre doğrulama + benzersizlik + CRUD. Tenant izolasyonu ve
/// audit alt katmanda otomatik. Bireysel: Ad zorunlu, TC (varsa) checksum + tenant'ta
/// benzersiz. Kurumsal/Servis: Ünvan zorunlu, Vergi No (varsa) format + benzersiz.
/// KVKK/F2: TC/ehliyet/pasaport at-rest ŞİFRELİ (ISecretProtector), TC benzersizliği ve
/// tam-eşleşme araması HMAC blind-index (IPiiHasher) üzerinden — düz metin DB'ye yazılmaz.
/// </summary>
public sealed class CustomerService(
    ICustomerRepository repository, ISecretProtector secrets, IPiiHasher pii, ITenantContext tenant,
    ICurrentUser currentUser, IPasswordHasher hasher)
{
    private readonly ICustomerRepository _repository = repository;
    private readonly ISecretProtector _secrets = secrets;
    private readonly IPiiHasher _pii = pii;
    private readonly ITenantContext _tenant = tenant;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPasswordHasher _hasher = hasher;   // FAZ-40: portal şifresi tek yönlü özet

    /// <summary>Blind-index tuzu için tenant (PII yazan/arayan akışlar daima kimlikli).</summary>
    private Guid TenantId => _tenant.TenantId
        ?? throw new ValidationException("Tenant bağlamı yok — PII işlemi yapılamaz.");

    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Dropdown listesi (Id + görünen ad). PII ÇÖZMEZ — cari seçtirmek için
    /// <see cref="ListAsync"/> yerine BUNU kullanın (bkz. ICustomerRepository.ListSecimAsync).</summary>
    public Task<IReadOnlyList<CariSecim>> ListSecimAsync(CancellationToken ct = default)
        => _repository.ListSecimAsync(ct);

    /// <summary>Liste ekranı: arama + sayfalama.</summary>
    public Task<PagedResult<Customer>> SearchAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 200) filter.PageSize = 20;
        PrepareTcSearch(filter);
        return _repository.SearchAsync(filter, ct);
    }

    /// <summary>CRM liste ekranı: arama/filtre + sayfalama + kira agregaları (adet/ciro/son kira).</summary>
    public Task<PagedResult<CustomerRow>> SearchRowsAsync(CustomerFilter filter, CancellationToken ct = default)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 200) filter.PageSize = 20;
        PrepareTcSearch(filter);
        return _repository.SearchRowsAsync(filter, ct);
    }

    /// <summary>Sorgu 11 haneli TC ise blind-index özetini filtreye koyar (şifreli TC'de
    /// ILike çalışmaz; tam-eşleşme hash üzerinden). Kısmi TC araması bilinçli olarak yok.</summary>
    private void PrepareTcSearch(CustomerFilter filter)
    {
        var digits = OnlyDigitsOrNull(filter.Query);
        filter.TcHash = digits?.Length == 11 ? _pii.Hash(TenantId, digits) : null;
    }

    public Task<Customer?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CustomerInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4: servis-katmanı savunma
        var n = Normalize(input);
        Validate(n);
        await EnsureUniqueAsync(n, excludeId: null, ct);

        var customer = new Customer();
        Apply(customer, n);
        await _repository.CreateAsync(customer, ct);
        return customer.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, CustomerInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        var n = Normalize(input);
        Validate(n);
        await EnsureUniqueAsync(n, excludeId: id, ct);

        return await _repository.UpdateAsync(id, c =>
        {
            Apply(c, n);
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        return _repository.DeleteAsync(id, ct);
    }

    // ---- iç yardımcılar ----

    private static void Validate(CustomerInput n)
    {
        if (n.Tip == CariType.Bireysel)
        {
            if (string.IsNullOrWhiteSpace(n.Ad))
                throw new ValidationException("Bireysel cari için Ad zorunludur.");
            if (!string.IsNullOrEmpty(n.TcKimlik) && !TurkishIdentity.IsValidTcKimlik(n.TcKimlik))
                throw new ValidationException("TC Kimlik No geçersiz.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(n.Unvan))
                throw new ValidationException("Kurumsal/Servis cari için Ünvan zorunludur.");
            if (!string.IsNullOrEmpty(n.VergiNo) && !TurkishIdentity.IsValidVergiNoFormat(n.VergiNo))
                throw new ValidationException("Vergi No 10 haneli olmalıdır.");
        }

        if (!string.IsNullOrEmpty(n.Email) && !IsValidEmail(n.Email))
            throw new ValidationException("E-posta adresi geçersiz.");
        TarihPolitikasi.DogumTarihi(n.DogumTarihi); // gelecekte doğmuş olamaz (yaş kuralı AYRI: fiyat motoru)
        if (n.VadeGun < 0)
            throw new ValidationException("Vade günü negatif olamaz.");
        if (n.RiskLimiti < 0)
            throw new ValidationException("Risk limiti negatif olamaz.");
        if (n.TevkifatOrani is < 0m or > 100m)
            throw new ValidationException("Tevkifat oranı 0 ile 100 arasında olmalıdır (%).");
    }

    private async Task EnsureUniqueAsync(CustomerInput n, Guid? excludeId, CancellationToken ct)
    {
        // TC benzersizliği blind-index üzerinden (düz metin DB'de yok) — DB'deki kısmi unique
        // index (TenantId, TcKimlikHash) yarışta ikinci savunmadır.
        if (!string.IsNullOrEmpty(n.TcKimlik)
            && await _repository.TcKimlikHashExistsAsync(_pii.Hash(TenantId, n.TcKimlik)!, excludeId, ct))
            throw new DuplicateCariException("TC Kimlik No", n.TcKimlik!);
        if (!string.IsNullOrEmpty(n.VergiNo) && await _repository.VergiNoExistsAsync(n.VergiNo!, excludeId, ct))
            throw new DuplicateCariException("Vergi No", n.VergiNo!);
    }

    private static CustomerInput Normalize(CustomerInput input) => new()
    {
        Tip = input.Tip,
        Ad = Trim(input.Ad),
        Soyad = Trim(input.Soyad),
        TcKimlik = OnlyDigitsOrNull(input.TcKimlik),
        Unvan = Trim(input.Unvan),
        VergiDairesi = Trim(input.VergiDairesi),
        VergiNo = OnlyDigitsOrNull(input.VergiNo),
        CepTel = Trim(input.CepTel),
        Gsm2 = Trim(input.Gsm2),
        Email = Trim(input.Email),
        Il = Trim(input.Il),
        Ilce = Trim(input.Ilce),
        Adres = Trim(input.Adres),
        Kaynak = Trim(input.Kaynak),
        MusteriTemsilcisi = Trim(input.MusteriTemsilcisi),
        IysIzinli = input.IysIzinli,
        Uyari = input.Uyari,
        UyariNedeni = Trim(input.UyariNedeni),
        EhliyetNo = Trim(input.EhliyetNo),
        EhliyetSinifi = Trim(input.EhliyetSinifi),
        EhliyetTarihi = input.EhliyetTarihi,
        EhliyetYeri = Trim(input.EhliyetYeri),
        Tarife = Trim(input.Tarife),
        VadeGun = input.VadeGun,
        RiskLimiti = input.RiskLimiti,
        RiskMesaji = Trim(input.RiskMesaji),
        RiskTarihi = input.RiskTarihi,
        HgsYansitmaTuru = Trim(input.HgsYansitmaTuru),
        KaraListe = input.KaraListe,
        Pasif = input.Pasif,
        OzelCariTip = Trim(input.OzelCariTip),
        MusteriTipi = Trim(input.MusteriTipi),
        EhliyetUlke = Trim(input.EhliyetUlke),
        Dil = Trim(input.Dil),
        Doviz = Trim(input.Doviz),
        TevkifatDurum = Trim(input.TevkifatDurum),
        Sinif = Trim(input.Sinif),
        MailIzin = input.MailIzin,
        SmsIzin = input.SmsIzin,
        TelefonIzin = input.TelefonIzin,
        DogumTarihi = input.DogumTarihi,
        BabaAdi = Trim(input.BabaAdi),
        AnaAdi = Trim(input.AnaAdi),
        PasaportNo = Trim(input.PasaportNo),
        FaturaDonemi = Trim(input.FaturaDonemi),
        TevkifatOrani = input.TevkifatOrani,
        // PR-E: yetkili kişiler — AdSoyad'ı boş olan satır atlanır (form değişken satır gönderir).
        Kisiler = (input.Kisiler ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k.AdSoyad))
            .Select(k => new CustomerContactInput
            {
                AdSoyad = Trim(k.AdSoyad), Telefon = Trim(k.Telefon), Mail = Trim(k.Mail), Gorev = Trim(k.Gorev)
            }).ToList(),
        // roadmap K4
        KvkkOnay = input.KvkkOnay,
        KvkkOnayTarih = input.KvkkOnayTarih,
        EkAdres = Trim(input.EkAdres),
        BankaIban = string.IsNullOrWhiteSpace(input.BankaIban) ? null : input.BankaIban.Trim().ToUpperInvariant(),
        BankaAdi = Trim(input.BankaAdi),
        FaturaAdresi = Trim(input.FaturaAdresi),
        FaturaUnvan = Trim(input.FaturaUnvan),
        // FAZ-40: Normalize KOPYA KURUCUDUR — yeni alan buraya DA yazılmalı (BelgeSablon dersi).
        Ulke = Trim(input.Ulke),
        Tel2 = Trim(input.Tel2),
        OzelKod = Trim(input.OzelKod),
        EntegrasyonKodu = Trim(input.EntegrasyonKodu),
        Aciklama = Trim(input.Aciklama),
        RiskIzin = Trim(input.RiskIzin),
        DogumYeri = Trim(input.DogumYeri),
        PasaportYeri = Trim(input.PasaportYeri),
        KurumsalNo = Trim(input.KurumsalNo),
        UyariSerbest = Trim(input.UyariSerbest),
        TevkifatKodu = Trim(input.TevkifatKodu),
        FaturaKiralayanIsim = Trim(input.FaturaKiralayanIsim),
        IsAdresi = Trim(input.IsAdresi),
        IsTelefonu = Trim(input.IsTelefonu),
        KayitliIl = Trim(input.KayitliIl),
        KayitliIlce = Trim(input.KayitliIlce),
        MahalleKoy = Trim(input.MahalleKoy),
        SeriNo = Trim(input.SeriNo),
        CiltNo = Trim(input.CiltNo),
        AileSira = Trim(input.AileSira),
        SiraNo = Trim(input.SiraNo),
        TcDogrulama = input.TcDogrulama,
        FaturaAdresFarkli = input.FaturaAdresFarkli,
        FaturaTekSatir = input.FaturaTekSatir,
        DogumGunuTakip = input.DogumGunuTakip,
        AnonimAd = input.AnonimAd,
        AnonimTc = input.AnonimTc,
        AnonimTelefon = input.AnonimTelefon,
        AnonimMail = input.AnonimMail,
        AnonimAdres = input.AnonimAdres,
        AnonimBelge = input.AnonimBelge,
        BakiyeGor = input.BakiyeGor,
        AracVerilmez = input.AracVerilmez,
        YasEhliyetSerbest = input.YasEhliyetSerbest,
        MerkezKurumsal = input.MerkezKurumsal,
        Broker = input.Broker,
        FindexZorunlu = input.FindexZorunlu,
        BayiKomisyon = input.BayiKomisyon,
        PasaportTarihi = input.PasaportTarihi,
        WebIndirim = input.WebIndirim,
        KaraZamani = input.KaraZamani,
        IslemSubeId = input.IslemSubeId,
        FirmaId = input.FirmaId,
        Sifre = Trim(input.Sifre)
    };

    private void Apply(Customer c, CustomerInput n)
    {
        c.Tip = n.Tip;
        c.Ad = n.Ad;
        c.Soyad = n.Soyad;
        // KVKK/F2: düz metin PII DB'ye yazılmaz — cipher + tenant-tuzlu blind-index; eski kolonlar null.
        c.TcKimlik = null;
        c.TcKimlikEnc = _secrets.Protect(n.TcKimlik);
        c.TcKimlikHash = _pii.Hash(TenantId, n.TcKimlik);
        c.Unvan = n.Unvan;
        c.VergiDairesi = n.VergiDairesi;
        c.VergiNo = n.VergiNo;
        c.CepTel = n.CepTel;
        c.Gsm2 = n.Gsm2;
        c.Email = n.Email;
        c.Il = n.Il;
        c.Ilce = n.Ilce;
        c.Adres = n.Adres;
        c.Kaynak = n.Kaynak;
        c.MusteriTemsilcisi = n.MusteriTemsilcisi;
        c.IysIzinli = n.IysIzinli;
        c.Uyari = n.Uyari;
        c.UyariNedeni = n.UyariNedeni;
        c.EhliyetNo = null;
        c.EhliyetNoEnc = _secrets.Protect(n.EhliyetNo);
        c.EhliyetSinifi = n.EhliyetSinifi;
        c.EhliyetTarihi = n.EhliyetTarihi;
        c.EhliyetYeri = n.EhliyetYeri;
        c.Tarife = n.Tarife;
        c.VadeGun = n.VadeGun;
        c.RiskLimiti = n.RiskLimiti;
        c.RiskMesaji = n.RiskMesaji;
        c.RiskTarihi = n.RiskTarihi;
        c.HgsYansitmaTuru = n.HgsYansitmaTuru;
        c.KaraListe = n.KaraListe;
        c.Pasif = n.Pasif;
        c.OzelCariTip = n.OzelCariTip;
        c.MusteriTipi = n.MusteriTipi;
        c.EhliyetUlke = n.EhliyetUlke;
        c.Dil = n.Dil;
        c.Doviz = n.Doviz;
        c.TevkifatDurum = n.TevkifatDurum;
        c.Sinif = n.Sinif;
        c.MailIzin = n.MailIzin;
        c.SmsIzin = n.SmsIzin;
        c.TelefonIzin = n.TelefonIzin;
        c.DogumTarihi = n.DogumTarihi;
        c.BabaAdi = n.BabaAdi;
        c.AnaAdi = n.AnaAdi;
        c.PasaportNo = null;
        c.PasaportNoEnc = _secrets.Protect(n.PasaportNo);
        c.FaturaDonemi = n.FaturaDonemi;
        c.TevkifatOrani = n.TevkifatOrani;
        // PR-E: yetkili kişiler child — clear+add (update'te EF farkı: kaldırılanlar cascade silinir, yeniler eklenir;
        // flat Yetkili1-3 kolonları DEPRECATED, artık YAZILMAZ). Repo UpdateAsync Include(Kisiler) ile yükler.
        c.Kisiler.Clear();
        for (var i = 0; i < n.Kisiler.Count; i++)
        {
            var k = n.Kisiler[i];
            c.Kisiler.Add(new CustomerContact { Sira = i, AdSoyad = k.AdSoyad!, Telefon = k.Telefon, Mail = k.Mail, Gorev = k.Gorev });
        }
        // roadmap K4
        c.KvkkOnay = n.KvkkOnay;
        c.KvkkOnayTarih = n.KvkkOnayTarih;
        c.EkAdres = n.EkAdres;
        c.BankaIban = n.BankaIban;
        c.BankaAdi = n.BankaAdi;
        c.FaturaAdresi = n.FaturaAdresi;
        c.FaturaUnvan = n.FaturaUnvan;
        c.Ulke = n.Ulke;
        c.Tel2 = n.Tel2;
        c.OzelKod = n.OzelKod;
        c.EntegrasyonKodu = n.EntegrasyonKodu;
        c.Aciklama = n.Aciklama;
        c.RiskIzin = n.RiskIzin;
        c.DogumYeri = n.DogumYeri;
        c.PasaportYeri = n.PasaportYeri;
        c.KurumsalNo = n.KurumsalNo;
        c.UyariSerbest = n.UyariSerbest;
        c.TevkifatKodu = n.TevkifatKodu;
        c.FaturaKiralayanIsim = n.FaturaKiralayanIsim;
        c.IsAdresi = n.IsAdresi;
        c.IsTelefonu = n.IsTelefonu;
        c.KayitliIl = n.KayitliIl;
        c.KayitliIlce = n.KayitliIlce;
        c.MahalleKoy = n.MahalleKoy;
        c.SeriNo = n.SeriNo;
        c.CiltNo = n.CiltNo;
        c.AileSira = n.AileSira;
        c.SiraNo = n.SiraNo;
        c.TcDogrulama = n.TcDogrulama;
        c.FaturaAdresFarkli = n.FaturaAdresFarkli;
        c.FaturaTekSatir = n.FaturaTekSatir;
        c.DogumGunuTakip = n.DogumGunuTakip;
        c.AnonimAd = n.AnonimAd;
        c.AnonimTc = n.AnonimTc;
        c.AnonimTelefon = n.AnonimTelefon;
        c.AnonimMail = n.AnonimMail;
        c.AnonimAdres = n.AnonimAdres;
        c.AnonimBelge = n.AnonimBelge;
        c.BakiyeGor = n.BakiyeGor;
        c.AracVerilmez = n.AracVerilmez;
        c.YasEhliyetSerbest = n.YasEhliyetSerbest;
        c.MerkezKurumsal = n.MerkezKurumsal;
        c.Broker = n.Broker;
        c.FindexZorunlu = n.FindexZorunlu;
        c.BayiKomisyon = n.BayiKomisyon;
        c.PasaportTarihi = n.PasaportTarihi;
        c.WebIndirim = n.WebIndirim;
        c.KaraZamani = n.KaraZamani;
        c.IslemSubeId = n.IslemSubeId;
        c.FirmaId = n.FirmaId;
        // ŞİFRE: boş = "değiştirme". Her kaydetmede sıfırlansaydı portal erişimi sessizce
        // kaybolurdu (TarifeGrubu dersi). Düz metin kolona ASLA yazılmaz.
        if (!string.IsNullOrWhiteSpace(n.Sifre)) c.SifreHash = _hasher.Hash(n.Sifre!);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? OnlyDigitsOrNull(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('.', at) > at;
    }
}
