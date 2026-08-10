using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.TenantSettings;

/// <summary>
/// Tenant ayarları iş mantığı (roadmap D1): firma + entegrasyon kimlikleri. Hassas alanlar
/// <see cref="ISecretProtector"/> ile at-rest ŞİFRELENİR (yaz) / çözülür (oku). Ayarlar hassas
/// (entegrasyon sırları) → yalnız <see cref="Permission.ManageUsers"/> (admin) okur/yazar.
/// Sır alanı yazmada BOŞ ise mevcut korunur (her kayıtta sır yeniden girilmesin).
/// </summary>
public sealed class TenantSettingsService(
    ITenantSettingsRepository repository, ICurrentUser currentUser, ISecretProtector secrets, ScreenPermissionService screens,
    ITenantDomainRepository domains, ITenantContext tenant)
{
    public async Task<TenantSettingsModel> GetAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        var s = await repository.GetAsync(ct);
        if (s is null) return new TenantSettingsModel();
        return new TenantSettingsModel
        {
            FirmaUnvan = s.FirmaUnvan,
            FirmaVergiDairesi = s.FirmaVergiDairesi,
            FirmaVergiNo = s.FirmaVergiNo,
            FirmaAdres = s.FirmaAdres,
            FirmaTel = s.FirmaTel,
            FirmaEmail = s.FirmaEmail,
            FirmaMobilTel = s.FirmaMobilTel,
            FirmaMarka = s.FirmaMarka,
            EFaturaKullanici = s.EFaturaKullanici,
            EFaturaSifre = secrets.Unprotect(s.EFaturaSifreEnc),
            SmsBaslik = s.SmsBaslik,
            SmsApiKey = secrets.Unprotect(s.SmsApiKeyEnc),
            PosMerchantId = s.PosMerchantId,
            PosApiKey = secrets.Unprotect(s.PosApiKeyEnc),
            // roadmap M1
            LogoUrl = s.LogoUrl,
            LogoBytes = s.LogoBytes, // PR-C

            VarsayilanDoviz = s.VarsayilanDoviz,
            VarsayilanKdvOrani = s.VarsayilanKdvOrani,
            VarsayilanGrupId = s.VarsayilanGrupId, // PR-10
            // FAZ-82 fiyat/muhasebe varsayılanları + iş kuralı anahtarı
            VarsayilanFiyatTuru = s.VarsayilanFiyatTuru,
            VarsayilanYakitSeviyesi = s.VarsayilanYakitSeviyesi,
            DropMesafeYokIseSifir = s.DropMesafeYokIseSifir,
            SaatFarkiToleransDk = s.SaatFarkiToleransDk,
            IadeIslemSaatSiniri = s.IadeIslemSaatSiniri,
            KurElleGirisKilitli = s.KurElleGirisKilitli,
            // FAZ-81 renk kodları
            RenkGecikenler = s.RenkGecikenler,
            RenkBugunDonecekler = s.RenkBugunDonecekler,
            RenkBugunCikacaklar = s.RenkBugunCikacaklar,
            RenkOpsiyonlu = s.RenkOpsiyonlu,
            RenkLimitBakiye = s.RenkLimitBakiye,
            RenkAlacakli = s.RenkAlacakli,
            RenkRezAtananPlaka = s.RenkRezAtananPlaka,
            RenkKiralanmayan = s.RenkKiralanmayan,
            DonemselFaturalamaJob = s.DonemselFaturalamaJob,
            DonemselOtomatikTahsilat = s.DonemselOtomatikTahsilat,
            MinKiraGun = s.MinKiraGun,
            MaxKiraGun = s.MaxKiraGun,
            RezOnayZorunlu = s.RezOnayZorunlu,
            SmtpHost = s.SmtpHost,
            SmtpPort = s.SmtpPort,
            SmtpKullanici = s.SmtpKullanici,
            SmtpSifre = secrets.Unprotect(s.SmtpSifreEnc),
            SmtpSsl = s.SmtpSsl,
            WhatsAppNumarasi = s.WhatsAppNumarasi,
            WhatsAppGunlukOzet = s.WhatsAppGunlukOzet,
            // PR-2: public-site
            PublicSiteEnabled = s.PublicSiteEnabled,
            PublicSiteHost = await domains.GetActiveHostAsync(TenantId, ct),
            // PR-5: tüm domain kayıtları (durum rozeti için)
            CustomDomains = (await domains.ListAsync(TenantId, ct))
                .Select(d => new TenantDomainRow(d.Host, d.Kind.ToString(), DurumMetni(d.Status)))
                .ToList()
        };
    }

    /// <summary>PR-2: "Sitemi Aç" — subdomain host'unu (idempotent) oluşturur + siteyi aktifleştirir.
    /// Çağıranın kendi çerez-scoped ITenantContext'i altında çalışır (owner-bypass YOK).</summary>
    public async Task OpenPublicSiteAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        await domains.EnsureSubdomainAsync(TenantId, ct);
        await repository.UpsertAsync(s => s.PublicSiteEnabled = true, ct);
    }

    /// <summary>PR-5: özel domain ekler — `PendingVerification` ile başlar, Caddy `on_demand_tls`'in
    /// ilk gerçek isteği başarıyla çözdüğü an kendi kendini `Active`'e doğrular (bkz. PublicTenantResolver).</summary>
    public async Task AddCustomDomainAsync(string host, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        if (string.IsNullOrWhiteSpace(host))
            throw new ValidationException("Alan adı zorunludur.");
        await domains.AddCustomAsync(TenantId, host, ct);
    }

    private static string DurumMetni(RentACar.Domain.Entities.TenantDomainStatus status) => status switch
    {
        RentACar.Domain.Entities.TenantDomainStatus.Active => "Aktif",
        RentACar.Domain.Entities.TenantDomainStatus.PendingVerification => "Doğrulama Bekliyor",
        RentACar.Domain.Entities.TenantDomainStatus.Failed => "Başarısız",
        _ => status.ToString()
    };

    private Guid TenantId => tenant.TenantId
        ?? throw new ValidationException("Tenant bağlamı yok — işlem yapılamaz.");

    /// <summary>
    /// FAZ-81 — renk kodu doğrulama. Boş/whitespace → null (varsayılana düş). Dolu ise KESİN
    /// <c>#rrggbb</c>: bu değer doğrudan bir CSS özel değişkenine yazılıyor, doğrulanmadan
    /// geçirilirse hem stil bozulur hem de CSS'e serbest metin enjekte edilmiş olur.
    /// </summary>
    private static string? Renk(string? deger, string alan)
    {
        if (string.IsNullOrWhiteSpace(deger)) return null;
        var v = deger.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$"))
            throw new ValidationException($"{alan} geçerli bir renk kodu olmalı (#rrggbb).");
        return v.ToLowerInvariant();
    }

    /// <summary>
    /// FAZ-82 — fiyat türü doğrulama. Boş/whitespace → null ("seçilmemiş"; bugünkü davranış).
    /// Dolu ise <see cref="Pricing.FiyatTuruSecenek.Hepsi"/> içinde OLMAK ZORUNDA ve KANONİK yazımıyla
    /// saklanır: motor bu metni karşılaştırıyor, tanımadığı bir değer sessizce başka bir fiyat/KDV
    /// davranışına düşerdi.
    /// </summary>
    private static string? FiyatTuruDogrula(string? deger)
    {
        if (string.IsNullOrWhiteSpace(deger)) return null;
        return Pricing.FiyatTuruSecenek.Normalize(deger)
            ?? throw new ValidationException(
                "Varsayılan fiyat türü geçersiz. Geçerli değerler: "
                + string.Join(", ", Pricing.FiyatTuruSecenek.Hepsi) + ".");
    }

    public async Task SaveAsync(TenantSettingsModel m, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        await repository.UpsertAsync(s =>
        {
            s.FirmaUnvan = Trim(m.FirmaUnvan);
            s.FirmaVergiDairesi = Trim(m.FirmaVergiDairesi);
            s.FirmaVergiNo = Trim(m.FirmaVergiNo);
            s.FirmaAdres = Trim(m.FirmaAdres);
            s.FirmaTel = Trim(m.FirmaTel);
            s.FirmaEmail = Trim(m.FirmaEmail);
            s.FirmaMobilTel = Trim(m.FirmaMobilTel);
            s.FirmaMarka = Trim(m.FirmaMarka);
            s.EFaturaKullanici = Trim(m.EFaturaKullanici);
            s.SmsBaslik = Trim(m.SmsBaslik);
            s.PosMerchantId = Trim(m.PosMerchantId);
            // Sır: dolu ise şifrele+güncelle; boş ise mevcut cipher KORUNUR.
            s.EFaturaSifreEnc = Secret(m.EFaturaSifre, s.EFaturaSifreEnc);
            s.SmsApiKeyEnc = Secret(m.SmsApiKey, s.SmsApiKeyEnc);
            s.PosApiKeyEnc = Secret(m.PosApiKey, s.PosApiKeyEnc);
            // roadmap M1 — görünüm/operasyon (düz) + SMTP (host/port/user düz, şifre Enc)
            s.LogoUrl = Trim(m.LogoUrl);
            s.VarsayilanDoviz = string.IsNullOrWhiteSpace(m.VarsayilanDoviz) ? null : m.VarsayilanDoviz.Trim().ToUpperInvariant();
            if (m.VarsayilanKdvOrani is < 0m or > 1m)
                throw new ValidationException("Varsayılan KDV oranı kesir olmalı (0.20 = %20); 0-1 arası."); // A6
            s.VarsayilanKdvOrani = m.VarsayilanKdvOrani;
            // PR-10: FK'si ON DELETE SET NULL; ayrıca çözücü grubu AKTİF olarak arar → pasifleşen
            // ya da başka tenant'a ait bir Id sessizce Ekonomi zincirine düşer, araç yanlış gruba girmez.
            s.VarsayilanGrupId = m.VarsayilanGrupId;
            // ---- FAZ-82 fiyat/muhasebe varsayılanları ----
            // Fiyat türü: serbest metin DEĞİL — motor bu değeri string karşılaştırmasıyla okuyor
            // (Otomatik → manuel ücret yok sayılır; Günlük/Toplam → fatura NET modu). Yazım hatalı bir
            // varsayılan formda ön-seçili görünüp motorda BAŞKA davranış üretirdi.
            s.VarsayilanFiyatTuru = FiyatTuruDogrula(m.VarsayilanFiyatTuru);
            // Yakıt skalası kirada 0-12 (RentalContract.CikisYakit ile aynı skala). Aralık dışı bir
            // varsayılan teslim formunu HTML doğrulamasıyla çakıştırır (min/max 0-12) → form gönderilemez.
            if (m.VarsayilanYakitSeviyesi is < 0 or > 12)
                throw new ValidationException("Varsayılan yakıt seviyesi 0 ile 12 arasında olmalıdır.");
            s.VarsayilanYakitSeviyesi = m.VarsayilanYakitSeviyesi;
            // BEKLEMEDE alanlar: hiçbir hesaba bağlı değil. Yine de negatif değer saklanmaz — ileride
            // motora bağlanırsa "eksi tolerans" gibi anlamsız bir veri hazır beklemesin.
            s.DropMesafeYokIseSifir = m.DropMesafeYokIseSifir;
            if (m.SaatFarkiToleransDk is < 0)
                throw new ValidationException("Saat farkı toleransı negatif olamaz (dakika).");
            s.SaatFarkiToleransDk = m.SaatFarkiToleransDk;
            if (m.IadeIslemSaatSiniri is < 0)
                throw new ValidationException("İade işlem saat sınırı negatif olamaz.");
            s.IadeIslemSaatSiniri = m.IadeIslemSaatSiniri;
            // İş kuralı anahtarı — UYGULANIYOR (KurCozucu giriş noktası). false = bugünkü davranış.
            s.KurElleGirisKilitli = m.KurElleGirisKilitli;
            // FAZ-81 renk kodları — boş serbest (null = koddaki varsayılan renk), dolu ise
            // KESİN "#rrggbb" olmalı: doğrulanmamış bir metin doğrudan CSS'e basılıyor.
            s.RenkGecikenler = Renk(m.RenkGecikenler, "RenkGecikenler");
            s.RenkBugunDonecekler = Renk(m.RenkBugunDonecekler, "RenkBugunDonecekler");
            s.RenkBugunCikacaklar = Renk(m.RenkBugunCikacaklar, "RenkBugunCikacaklar");
            s.RenkOpsiyonlu = Renk(m.RenkOpsiyonlu, "RenkOpsiyonlu");
            s.RenkLimitBakiye = Renk(m.RenkLimitBakiye, "RenkLimitBakiye");
            s.RenkAlacakli = Renk(m.RenkAlacakli, "RenkAlacakli");
            s.RenkRezAtananPlaka = Renk(m.RenkRezAtananPlaka, "RenkRezAtananPlaka");
            s.RenkKiralanmayan = Renk(m.RenkKiralanmayan, "RenkKiralanmayan");
            s.DonemselFaturalamaJob = m.DonemselFaturalamaJob;
            s.DonemselOtomatikTahsilat = m.DonemselOtomatikTahsilat;
            s.MinKiraGun = m.MinKiraGun;
            s.MaxKiraGun = m.MaxKiraGun;
            s.RezOnayZorunlu = m.RezOnayZorunlu;
            s.SmtpHost = Trim(m.SmtpHost);
            s.SmtpPort = m.SmtpPort;
            s.SmtpKullanici = Trim(m.SmtpKullanici);
            s.SmtpSifreEnc = Secret(m.SmtpSifre, s.SmtpSifreEnc);
            s.SmtpSsl = m.SmtpSsl;
            s.WhatsAppNumarasi = Trim(m.WhatsAppNumarasi);
            s.WhatsAppGunlukOzet = m.WhatsAppGunlukOzet ?? false;
        }, ct);
    }

    /// <summary>
    /// PR-C: PDF firma logosunu ayarla/kaldır (Ayarlar upload). null/boş → logoyu sil.
    ///
    /// <para>PR-A: doğrulama ARTIK BURADA (<see cref="LogoKurallari.Reddet"/>) — tür/boyut/ölçü.
    /// Önceden yalnız web ucunda (`TenantSettingsEndpoints`) yapılıyordu; uç kontrolü kalıyor
    /// (iki katman) ama servis public API olduğu için tek başına yeterli değildi: yeni bir çağıran
    /// (REST API, içe aktarım, platform yolu) doğrulamayı sessizce atlardı ve bozuk bayt PDF üretimini
    /// patlatarak sözleşmenin HİÇ basılamamasına yol açardı.</para>
    /// </summary>
    public async Task SetLogoAsync(byte[]? bytes, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        if (bytes is { Length: > 0 } dolu && LogoKurallari.Reddet(dolu) is { } hata)
            throw new ValidationException(hata);
        await repository.UpsertAsync(s => s.LogoBytes = bytes is { Length: > 0 } ? bytes : null, ct);
    }

    public Task<IReadOnlyList<RentACar.Domain.Entities.WhatsAppGonderim>> ListWhatsAppGonderimAsync(int n = 7, CancellationToken ct = default)
        => repository.ListWhatsAppGonderimAsync(n, ct);

    private string? Secret(string? yeni, string? mevcutCipher)
        => string.IsNullOrWhiteSpace(yeni) ? mevcutCipher : secrets.Protect(yeni.Trim());

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
