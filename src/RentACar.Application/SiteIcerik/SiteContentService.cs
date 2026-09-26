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
/// <para><b>OKUMA yolları guard'sız</b> (<see cref="PageAsync"/>, <see cref="PublishedPagesAsync"/>,
/// <see cref="PublishedFaqAsync"/>): halka açık siteyi anonim ziyaretçi çağırıyor, orada rol yok
/// (<c>FleetShowcaseService</c> ile aynı desen — guard'sız salt-okur derleme).</para>
/// </summary>
public sealed class SiteContentService(
    ISiteContentRepository repository,
    ICurrentUser currentUser,
    ScreenPermissionService screens)
{
    /// <summary>Yönetim ekranındaki metin alanlarının üst sınırları (UI ile sunucu aynı sayıyı görsün).</summary>
    public const int MaxTitle = 200;
    public const int MaxBody = 20_000;
    public const int MaxMeta = 300;
    public const int MaxQuestion = 300;
    public const int MaxAnswer = 4_000;

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
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
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

    public async Task<IReadOnlyList<SayfaOzet>> ListAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.ListAsync(ct);
    }

    public async Task<SayfaIcerik?> FetchAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.FetchAsync(id, ct);
    }

    public Task<Guid> SaveAsync(SayfaIcerikInput input, CancellationToken ct = default)
        => SaveAsync(input, expectedVersion: null, ct);

    /// <summary>
    /// F11.1b — <paramref name="expectedVersion"/> doluysa güncelleme satır kilidi altında sürümle karşılaştırılır
    /// (uyuşmazlık <see cref="ConcurrentModificationException"/>). Yeni kayıtta yok sayılır.
    /// </summary>
    public async Task<Guid> SaveAsync(SayfaIcerikInput input, string? expectedVersion, CancellationToken ct = default)
    {
        await GuardAsync(ct);

        var title = (input.Baslik ?? "").Trim();
        if (title.Length == 0) throw new ValidationException("Sayfa başlığı zorunludur.");
        if (title.Length > MaxTitle) throw new ValidationException($"Başlık en çok {MaxTitle} karakter olabilir.");

        var body = (input.Govde ?? "").Trim();
        if (body.Length == 0) throw new ValidationException("Sayfa içeriği boş olamaz.");
        if (body.Length > MaxBody) throw new ValidationException($"İçerik en çok {MaxBody} karakter olabilir.");

        var meta = string.IsNullOrWhiteSpace(input.MetaAciklama) ? null : input.MetaAciklama.Trim();
        if (meta is { Length: > MaxMeta }) throw new ValidationException($"Arama motoru özeti en çok {MaxMeta} karakter olabilir.");

        // Slug: verilmişse ondan, verilmemişse BAŞLIKTAN türetilir (ikisi de aynı süzgeçten geçer).
        var slug = TurkishText.Slugify(string.IsNullOrWhiteSpace(input.Slug) ? title : input.Slug);
        if (slug.Length == 0)
            throw new ValidationException("Başlıktan bir adres üretilemedi — lütfen adresi elle yazın.");
        if (ReservedSlugs.Contains(slug))
            throw new ValidationException($"\"{slug}\" adresi sistem tarafından kullanılıyor; başka bir adres seçin.");
        if (await repository.SlugExistsAsync(slug, input.Id, ct))
            throw new ValidationException($"\"{slug}\" adresi başka bir sayfada kullanılıyor.");

        if (input.Id is Guid id)
        {
            void Apply(SayfaIcerik s)
            {
                s.Baslik = title;
                s.Govde = body;
                s.Slug = slug;
                s.MetaAciklama = meta;
                s.Sira = input.Sira;
                s.Yayinda = input.Yayinda;
                s.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            var ok = expectedVersion is null
                ? await repository.UpdateAsync(id, Apply, ct)
                : await repository.UpdateAsync(id, expectedVersion, Apply, ct);
            if (!ok) throw new ValidationException("Sayfa bulunamadı.");
            InvalidateCache();
            return id;
        }

        var newItem = new SayfaIcerik
        {
            Baslik = title, Govde = body, Slug = slug, MetaAciklama = meta,
            Sira = input.Sira, Yayinda = input.Yayinda,
        };
        await repository.AddAsync(newItem, ct);
        InvalidateCache();
        return newItem.Id;
    }

    /// <summary>Yayınla / gizle. Gizlenen sayfa sitede 404 döner.</summary>
    public async Task PublishStatusAsync(Guid id, bool published, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var ok = await repository.UpdateAsync(id, s =>
        {
            s.Yayinda = published;
            s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        if (!ok) throw new ValidationException("Sayfa bulunamadı.");
        InvalidateCache();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!await repository.DeleteAsync(id, ct)) throw new ValidationException("Sayfa bulunamadı.");
        InvalidateCache();
    }

    // ---- SSS yönetimi ----

    public async Task<IReadOnlyList<SssSatiri>> ListFaqAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.ListFaqAsync(publishedOnly: false, ct);
    }

    public Task<Guid> SaveFaqAsync(SssInput input, CancellationToken ct = default)
        => SaveFaqAsync(input, expectedVersion: null, ct);

    /// <summary>F11.1b — sürümlü SSS kaydı (bkz. <see cref="SaveAsync(SayfaIcerikInput, string?, CancellationToken)"/>).</summary>
    public async Task<Guid> SaveFaqAsync(SssInput input, string? expectedVersion, CancellationToken ct = default)
    {
        await GuardAsync(ct);

        var question = (input.Soru ?? "").Trim();
        var answer = (input.Cevap ?? "").Trim();
        if (question.Length == 0) throw new ValidationException("Soru zorunludur.");
        if (question.Length > MaxQuestion) throw new ValidationException($"Soru en çok {MaxQuestion} karakter olabilir.");
        if (answer.Length == 0) throw new ValidationException("Cevap zorunludur.");
        if (answer.Length > MaxAnswer) throw new ValidationException($"Cevap en çok {MaxAnswer} karakter olabilir.");

        if (input.Id is Guid id)
        {
            void Apply(SssKaydi k)
            {
                k.Soru = question; k.Cevap = answer; k.Sira = input.Sira; k.Yayinda = input.Yayinda;
                k.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            var ok = expectedVersion is null
                ? await repository.UpdateFaqAsync(id, Apply, ct)
                : await repository.UpdateFaqAsync(id, expectedVersion, Apply, ct);
            if (!ok) throw new ValidationException("Soru bulunamadı.");
            InvalidateCache();
            return id;
        }

        var newItem = new SssKaydi { Soru = question, Cevap = answer, Sira = input.Sira, Yayinda = input.Yayinda };
        await repository.AddFaqAsync(newItem, ct);
        InvalidateCache();
        return newItem.Id;
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

    public async Task DeleteFaqAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!await repository.DeleteFaqAsync(id, ct)) throw new ValidationException("Soru bulunamadı.");
        InvalidateCache();
    }

    // ---- Halka açık okuma (GUARD YOK — anonim ziyaretçi) ----
    //
    // PR-19 — İSTEK-İÇİ ÖNBELLEK. Ölçümde görüldü ki halka açık sayfalarda bu iki sorgu İKİ KEZ
    // koşuyor: bir kez kabuk (MainLayout alt bilgisi), bir kez sayfanın kendi bölümü. Servis
    // `AddScoped` olduğu için örnek istek başına tek → aynı istekte ikinci çağrı DB'ye gitmez.
    // Veri istek ortasında değişmez, dolayısıyla tutarlılık riski yok.
    private IReadOnlyList<SayfaOzet>? _publishedPages;
    private IReadOnlyList<SssSatiri>? _publishedFaq;

    /// <summary>
    /// Yazma sonrası önbelleği DÜŞÜR. Halka açık okuma yolunda aynı scope'ta yazma olmuyor, ama
    /// yönetim akışı (ve testler) aynı örnekte "kaydet → oku" yapabiliyor; geçersiz kılmazsak
    /// ekran kendi yaptığı değişikliği görmezdi. Önbellek bir HIZ optimizasyonu, davranış değil.
    /// </summary>
    private void InvalidateCache()
    {
        _publishedPages = null;
        _publishedFaq = null;
    }

    /// <summary>Yayındaki sayfa; yoksa <c>null</c> → sayfa kendi 404'ünü yazar.</summary>
    public Task<SayfaGoster?> PageAsync(string slug, CancellationToken ct = default)
        => repository.FindAsync(slug, ct);

    /// <summary>Footer ve sitemap için yayındaki sayfa listesi.</summary>
    public async Task<IReadOnlyList<SayfaOzet>> PublishedPagesAsync(CancellationToken ct = default)
        => _publishedPages ??= await repository.PublishedAsync(ct);

    public async Task<IReadOnlyList<SssSatiri>> PublishedFaqAsync(CancellationToken ct = default)
        => _publishedFaq ??= await repository.ListFaqAsync(publishedOnly: true, ct);
}
