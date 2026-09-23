using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.SiteIcerik;

/// <summary>Yönetim ekranından gelen sayfa girdisi. <see cref="Slug"/> boşsa başlıktan türetilir.</summary>
public sealed record SayfaIcerikInput(
    Guid? Id, string Baslik, string Govde, string? Slug, string? MetaAciklama, int Sira, bool Yayinda);

/// <summary>Yönetim ekranından gelen SSS girdisi.</summary>
public sealed record SssInput(Guid? Id, string Soru, string Cevap, int Sira, bool Yayinda);

/// <summary>
/// PR-16 — halka açık site içeriği (sayfalar + SSS).
///
/// <para><b>Yetki:</b> <see cref="Permission.OperationsWrite"/> + <c>"web-sitesi"</c> ekran kodu —
/// <c>WebIlanService</c> ile AYNI kapı. Yeni bir <c>Permission</c> enum değeri EKLENMEDİ (matris
/// testlerini dalgalandırır). Modül lisansı (satın alma) AYRI bir kademedir ve web ucunda
/// <c>RequireWebSitesiModulu()</c> ile doğrulanır.</para>
///
/// <para><b>OKUMA yolları guard'sız</b> (<see cref="SayfaAsync"/>, <see cref="YayindakiSayfalarAsync"/>,
/// <see cref="YayindakiSssAsync"/>): halka açık siteyi anonim ziyaretçi çağırıyor, orada rol yok
/// (<c>FleetShowcaseService</c> ile aynı desen — guard'sız salt-okur derleme).</para>
/// </summary>
public sealed class SiteIcerikService(
    ISiteIcerikRepository repository,
    ICurrentUser currentUser,
    ScreenPermissionService screens)
{
    /// <summary>Yönetim ekranındaki metin alanlarının üst sınırları (UI ile sunucu aynı sayıyı görsün).</summary>
    public const int MaxBaslik = 200;
    public const int MaxGovde = 20_000;
    public const int MaxMeta = 300;
    public const int MaxSoru = 300;
    public const int MaxCevap = 4_000;

    /// <summary>
    /// Kök seviyede YASAK slug'lar. Sayfalar <c>/hakkimizda</c> gibi kök adreslerde yaşadığı için,
    /// bunlardan biri kabul edilse ASP.NET'in literal-önce yönlendirmesi gerçek rotayı seçer ve
    /// firmanın içerik sayfası SESSİZCE hiç açılmaz — personel "kaydettim ama görünmüyor" der.
    ///
    /// <para><b>NOKTALI rotalar listede YOK</b> (<c>robots.txt</c>, <c>sitemap.xml</c>) ve olmalarına
    /// gerek de yok: <see cref="TurkishText.Slugify"/> yalnız harf/rakam bırakıp gerisini <c>-</c>
    /// yaptığı için bir slug ASLA nokta içeremez. Onları listeye yazmak ölü koddu — yazma anında hiç
    /// eşleşmezlerdi (test bu değişmezi kilitliyor).</para>
    ///
    /// <para>Liste ELLE tutuluyor: PublicSite'a yeni bir kök rota eklendiğinde buraya da eklenmeli —
    /// <c>SiteIcerikTests</c> bugün bilinen rotaların hepsinin listede olduğunu doğruluyor.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> RezerveSluglar = new HashSet<string>(StringComparer.Ordinal)
    {
        "araclar", "blog", "blog-kapak", "cok-istek", "dogrulama", "foto", "iletisim", "musaitlik",
        "not-found", "rezervasyon-talebi", "sss", "talep-alindi",
    };

    private async Task GuardAsync(CancellationToken ct)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        await screens.EnsureScreenAccessAsync("web-sitesi", Permission.OperationsWrite, ct);
    }

    // ---- Yönetim (girişli) ----

    public async Task<IReadOnlyList<SayfaOzet>> ListeleAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.ListeleAsync(ct);
    }

    public async Task<SayfaIcerik?> GetirAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.GetirAsync(id, ct);
    }

    public Task<Guid> KaydetAsync(SayfaIcerikInput input, CancellationToken ct = default)
        => KaydetAsync(input, expectedVersion: null, ct);

    /// <summary>
    /// F11.1b — <paramref name="expectedVersion"/> doluysa güncelleme satır kilidi altında sürümle karşılaştırılır
    /// (uyuşmazlık <see cref="EszamanliDegisiklikException"/>). Yeni kayıtta yok sayılır.
    /// </summary>
    public async Task<Guid> KaydetAsync(SayfaIcerikInput input, string? expectedVersion, CancellationToken ct = default)
    {
        await GuardAsync(ct);

        var baslik = (input.Baslik ?? "").Trim();
        if (baslik.Length == 0) throw new ValidationException("Sayfa başlığı zorunludur.");
        if (baslik.Length > MaxBaslik) throw new ValidationException($"Başlık en çok {MaxBaslik} karakter olabilir.");

        var govde = (input.Govde ?? "").Trim();
        if (govde.Length == 0) throw new ValidationException("Sayfa içeriği boş olamaz.");
        if (govde.Length > MaxGovde) throw new ValidationException($"İçerik en çok {MaxGovde} karakter olabilir.");

        var meta = string.IsNullOrWhiteSpace(input.MetaAciklama) ? null : input.MetaAciklama.Trim();
        if (meta is { Length: > MaxMeta }) throw new ValidationException($"Arama motoru özeti en çok {MaxMeta} karakter olabilir.");

        // Slug: verilmişse ondan, verilmemişse BAŞLIKTAN türetilir (ikisi de aynı süzgeçten geçer).
        var slug = TurkishText.Slugify(string.IsNullOrWhiteSpace(input.Slug) ? baslik : input.Slug);
        if (slug.Length == 0)
            throw new ValidationException("Başlıktan bir adres üretilemedi — lütfen adresi elle yazın.");
        if (RezerveSluglar.Contains(slug))
            throw new ValidationException($"\"{slug}\" adresi sistem tarafından kullanılıyor; başka bir adres seçin.");
        if (await repository.SlugVarMiAsync(slug, input.Id, ct))
            throw new ValidationException($"\"{slug}\" adresi başka bir sayfada kullanılıyor.");

        if (input.Id is Guid id)
        {
            void Apply(SayfaIcerik s)
            {
                s.Baslik = baslik;
                s.Govde = govde;
                s.Slug = slug;
                s.MetaAciklama = meta;
                s.Sira = input.Sira;
                s.Yayinda = input.Yayinda;
                s.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            var ok = expectedVersion is null
                ? await repository.GuncelleAsync(id, Apply, ct)
                : await repository.GuncelleAsync(id, expectedVersion, Apply, ct);
            if (!ok) throw new ValidationException("Sayfa bulunamadı.");
            OnbellegiDusur();
            return id;
        }

        var yeni = new SayfaIcerik
        {
            Baslik = baslik, Govde = govde, Slug = slug, MetaAciklama = meta,
            Sira = input.Sira, Yayinda = input.Yayinda,
        };
        await repository.EkleAsync(yeni, ct);
        OnbellegiDusur();
        return yeni.Id;
    }

    /// <summary>Yayınla / gizle. Gizlenen sayfa sitede 404 döner.</summary>
    public async Task YayinDurumuAsync(Guid id, bool yayinda, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var ok = await repository.GuncelleAsync(id, s =>
        {
            s.Yayinda = yayinda;
            s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        if (!ok) throw new ValidationException("Sayfa bulunamadı.");
        OnbellegiDusur();
    }

    public async Task SilAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!await repository.SilAsync(id, ct)) throw new ValidationException("Sayfa bulunamadı.");
        OnbellegiDusur();
    }

    // ---- SSS yönetimi ----

    public async Task<IReadOnlyList<SssSatiri>> SssListeAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.SssListeAsync(yalnizYayinda: false, ct);
    }

    public Task<Guid> SssKaydetAsync(SssInput input, CancellationToken ct = default)
        => SssKaydetAsync(input, expectedVersion: null, ct);

    /// <summary>F11.1b — sürümlü SSS kaydı (bkz. <see cref="KaydetAsync(SayfaIcerikInput, string?, CancellationToken)"/>).</summary>
    public async Task<Guid> SssKaydetAsync(SssInput input, string? expectedVersion, CancellationToken ct = default)
    {
        await GuardAsync(ct);

        var soru = (input.Soru ?? "").Trim();
        var cevap = (input.Cevap ?? "").Trim();
        if (soru.Length == 0) throw new ValidationException("Soru zorunludur.");
        if (soru.Length > MaxSoru) throw new ValidationException($"Soru en çok {MaxSoru} karakter olabilir.");
        if (cevap.Length == 0) throw new ValidationException("Cevap zorunludur.");
        if (cevap.Length > MaxCevap) throw new ValidationException($"Cevap en çok {MaxCevap} karakter olabilir.");

        if (input.Id is Guid id)
        {
            void Apply(SssKaydi k)
            {
                k.Soru = soru; k.Cevap = cevap; k.Sira = input.Sira; k.Yayinda = input.Yayinda;
                k.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            var ok = expectedVersion is null
                ? await repository.SssGuncelleAsync(id, Apply, ct)
                : await repository.SssGuncelleAsync(id, expectedVersion, Apply, ct);
            if (!ok) throw new ValidationException("Soru bulunamadı.");
            OnbellegiDusur();
            return id;
        }

        var yeni = new SssKaydi { Soru = soru, Cevap = cevap, Sira = input.Sira, Yayinda = input.Yayinda };
        await repository.SssEkleAsync(yeni, ct);
        OnbellegiDusur();
        return yeni.Id;
    }

    /// <summary>F11.1b — sayfa satır sürümü (opak).</summary>
    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.VersionAsync(id, ct);
    }

    /// <summary>F11.1b — SSS satır sürümü (opak).</summary>
    public async Task<string?> FaqVersionAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.FaqVersionAsync(id, ct);
    }

    public async Task SssSilAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!await repository.SssSilAsync(id, ct)) throw new ValidationException("Soru bulunamadı.");
        OnbellegiDusur();
    }

    // ---- Halka açık okuma (GUARD YOK — anonim ziyaretçi) ----
    //
    // PR-19 — İSTEK-İÇİ ÖNBELLEK. Ölçümde görüldü ki halka açık sayfalarda bu iki sorgu İKİ KEZ
    // koşuyor: bir kez kabuk (MainLayout alt bilgisi), bir kez sayfanın kendi bölümü. Servis
    // `AddScoped` olduğu için örnek istek başına tek → aynı istekte ikinci çağrı DB'ye gitmez.
    // Veri istek ortasında değişmez, dolayısıyla tutarlılık riski yok.
    private IReadOnlyList<SayfaOzet>? _yayindakiSayfalar;
    private IReadOnlyList<SssSatiri>? _yayindakiSss;

    /// <summary>
    /// Yazma sonrası önbelleği DÜŞÜR. Halka açık okuma yolunda aynı scope'ta yazma olmuyor, ama
    /// yönetim akışı (ve testler) aynı örnekte "kaydet → oku" yapabiliyor; geçersiz kılmazsak
    /// ekran kendi yaptığı değişikliği görmezdi. Önbellek bir HIZ optimizasyonu, davranış değil.
    /// </summary>
    private void OnbellegiDusur()
    {
        _yayindakiSayfalar = null;
        _yayindakiSss = null;
    }

    /// <summary>Yayındaki sayfa; yoksa <c>null</c> → sayfa kendi 404'ünü yazar.</summary>
    public Task<SayfaGoster?> SayfaAsync(string slug, CancellationToken ct = default)
        => repository.BulAsync(slug, ct);

    /// <summary>Footer ve sitemap için yayındaki sayfa listesi.</summary>
    public async Task<IReadOnlyList<SayfaOzet>> YayindakiSayfalarAsync(CancellationToken ct = default)
        => _yayindakiSayfalar ??= await repository.YayindakilerAsync(ct);

    public async Task<IReadOnlyList<SssSatiri>> YayindakiSssAsync(CancellationToken ct = default)
        => _yayindakiSss ??= await repository.SssListeAsync(yalnizYayinda: true, ct);
}
