using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.WebSite;

/// <summary>Liste satırı: ilan + yayına girmesi için eksik olanlar (PR-10/11'in tanılama felsefesi).</summary>
public sealed record WebIlanSatiri(
    Guid Id, string Baslik, WebIlanDurum Durum, int AracSayisi, int Adet,
    decimal GunlukFiyat, bool KdvDahil, IReadOnlyList<string> Eksikler, bool OzellikBayat)
{
    public bool Yayinda => Durum == WebIlanDurum.Yayinda && Eksikler.Count == 0;
}

/// <summary>Sihirbazda gösterilen ilan fotoğrafı (kaynağı bir ÜYE ARAÇ — ilana ait ayrı tablo yok).</summary>
public sealed record IlanFotoSatiri(Guid VehicleId, Guid PhotoId, string Plaka, int Sira);

/// <summary>Adım-1 havuzundaki bir "aynı araç" kümesi (beraber modda tek satır olarak seçilir).</summary>
public sealed record AracKumesi(string Imza, string Baslik, string? YilAralik, IReadOnlyList<Vehicle> Araclar);

/// <summary>
/// PR-13 — halka açık site ilan sihirbazı. ÜÇ ADIM, her biri BAĞIMSIZ bir servis çağrısı:
/// <list type="number">
///   <item><see cref="StepOneAsync"/> — araç seç → TASLAK ilan(lar) + araç üyelikleri</item>
///   <item><see cref="StepTwoAsync"/> — fiyat</item>
///   <item><see cref="StepThreeAsync"/> — teknik özellikler → YAYINDA</item>
/// </list>
///
/// <para><b>Neden taslak-kayıt, hidden-input DEĞİL:</b> statik SSR'da adımlar arası durumu gizli
/// alanlarla taşımak 200 araçlık filoda adım-3 POST'unu ~8.400 form alanına çıkarır ve ASP.NET'in
/// varsayılan <c>ValueCountLimit=1024</c> sınırına çarpar → kullanıcı üç adımı doldurduktan SONRA
/// 400 alır ve girdiğinin tamamını kaybeder. Query string de ölü (200 GUID ≈ 7,4 KB, Kestrel istek
/// satırı 8 KB). Taslak-kayıt ayrıca her adımı entegrasyon testiyle doğrulanabilir kılar — repoda
/// HTTP form akışı için harness YOK.</para>
///
/// <para>Yetki: <see cref="Permission.OperationsWrite"/> + <c>"web-sitesi"</c> ekran override'ı.
/// Modül lisansı (satın alma) AYRI bir kademedir ve web ucunda doğrulanır.</para>
/// </summary>
public sealed class WebListingService(
    IWebListingRepository repository,
    IVehicleGroupRepository groups,
    IVehiclePhotoRepository photos,
    VehiclePhotoService photoService,
    ICurrentUser currentUser,
    ITenantCache cache,
    ScreenPermissionService screens)
{
    private async Task GuardAsync(CancellationToken ct)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        await screens.EnsureScreenAccessAsync("web-sitesi", Permission.OperationsWrite, ct);
    }

    // ---- Okuma ----

    /// <summary>Sihirbaz adım-1 havuzu: HENÜZ İLANA BAĞLANMAMIŞ araçlar, "aynı araç" kümelerine
    /// gruplanmış. Operatör yalnız kendi şubesindekileri görür (BranchScope).</summary>
    public async Task<IReadOnlyList<AracKumesi>> PoolAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var scope = BranchScope.EffectiveFilter(currentUser);
        var vehicles = (await repository.ListVehiclesWithoutListingAsync(ct))
            .Where(v => scope.Unrestricted || BranchScope.InScope(scope, v.SubeId, v.Sube))
            .ToList();

        return [.. vehicles
            .GroupBy(VehicleSignature.Calculate)
            .Select(g =>
            {
                var list = g.OrderBy(v => v.Plaka, StringComparer.CurrentCulture).ToList();
                return new AracKumesi(g.Key, VehicleSignature.Header(list), VehicleSignature.YearRange(list), list);
            })
            .OrderByDescending(k => k.Araclar.Count)
            .ThenBy(k => k.Baslik, StringComparer.CurrentCulture)];
    }

    /// <summary>İlan listesi + her birinin yayına girmesi için eksikleri.
    /// Foto durumu TEK TOPLU sorguyla alınır (ilan başına sorgu N+1 üretirdi).</summary>
    public async Task<IReadOnlyList<WebIlanSatiri>> ListAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var details = await repository.ListAsync(ct);
        var activeGroups = await groups.ListActiveAsync(ct);
        var vehicleIdsWithPhoto = await photos.ListVehicleIdsWithPhotoAsync(
            [.. details.SelectMany(d => d.Araclar).Select(v => v.Id).Distinct()], ct);
        var rows = new List<WebIlanSatiri>();

        foreach (var d in details)
        {
            var missingItems = new List<string>();
            if (d.Araclar.Count == 0) missingItems.Add("Araç yok");
            else if (!d.Araclar.Any(v => vehicleIdsWithPhoto.Contains(v.Id))) missingItems.Add("Foto yok");
            if (d.Ilan.GunlukFiyat <= 0m) missingItems.Add("Fiyat girilmedi");
            if (d.Ilan.Durum == WebIlanDurum.Taslak) missingItems.Add("Taslak — sihirbaz tamamlanmadı");
            if (d.Ilan.Durum == WebIlanDurum.Pasif) missingItems.Add("Pasif (gizlendi)");

            var stale = d.Araclar.Count > 0
                && FeatureSnapshot.IsStale(d.Ozellikler, d.Araclar, FindGroup(activeGroups, d.Araclar[0]));

            rows.Add(new WebIlanSatiri(d.Ilan.Id, d.Ilan.Baslik, d.Ilan.Durum, d.Araclar.Count,
                d.Araclar.Sum(v => v.VitrinAdet ?? 1), d.Ilan.GunlukFiyat, d.Ilan.KdvDahil, missingItems, stale));
        }
        return rows;
    }

    public async Task<WebIlanDetay?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.FindAsync(id, ct);
    }

    /// <summary>"Ayrı" modda aynı imzadan yaratılmış ve HÂLÂ TASLAK olan diğer ilan sayısı — sihirbaz
    /// ekranında "girdiğiniz değer N ilana daha uygulanacak" bilgisini basmak için.</summary>
    public async Task<int> SiblingDraftCountAsync(Guid listingId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(listingId, ct);
        if (d?.Ilan.EslesmeAnahtari is not { } key) return 0;
        return (await repository.FindByKeyAsync(key, ct))
            .Count(i => i.Id != listingId && i.Durum == WebIlanDurum.Taslak);
    }

    /// <summary>Adım-3 ekranının başlangıç satırları: kayıtlı özellik varsa o, yoksa araçlardan
    /// üretilen snapshot.</summary>
    public async Task<IReadOnlyList<OzellikSatiri>> SuggestedFeaturesAsync(Guid listingId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(listingId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        if (d.Ozellikler.Count > 0)
            return [.. d.Ozellikler.Select(o => new OzellikSatiri(o.Etiket, o.Deger, o.Gorunur))];
        if (d.Araclar.Count == 0) return [];
        return FeatureSnapshot.Generate(d.Araclar, FindGroup(await groups.ListActiveAsync(ct), d.Araclar[0]));
    }

    // ---- Adım 1 ----

    /// <summary>
    /// Seçilen araçlardan TASLAK ilan(lar) üretir ve araçları bağlar.
    ///
    /// <paramref name="together"/> = true → "aynı araç" kümesi başına TEK ilan (12 Egea = 1 kart).
    /// false → her araç kendi ilanı (12 Egea = 12 kart); fiyat/özellik adımları bir kez doldurulup
    /// kardeşlere kopyalanır.
    ///
    /// <para><b>İKİZ İLAN KORUMASI:</b> "beraber" modda aynı imzalı bir ilan ZATEN varsa yeni ilan
    /// yaratılmaz, araçlar MEVCUDA katılır. Aksi halde çok-şubeli tenant'ta İstanbul operatörü
    /// "Egea 1.500 ₺", Ankara operatörü "Egea 1.700 ₺" yaratır ve sitede iki özdeş kart çıkardı.</para>
    ///
    /// <returns>Sihirbazın devam edeceği ilan Id'si (birden çok yaratıldıysa ilki).</returns>
    /// </summary>
    public async Task<Guid> StepOneAsync(IReadOnlyList<Guid> vehicleIds, bool together, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (vehicleIds.Count == 0) throw new ValidationException("En az bir araç seçin.");

        // WHITELIST: seçilen id gerçekten kapsamda VE ilansız mı (form'dan gelen id'ye güvenilmez).
        var pool = (await PoolAsync(ct)).SelectMany(k => k.Araclar).ToDictionary(v => v.Id);
        var selected = vehicleIds.Distinct().Where(pool.ContainsKey).Select(id => pool[id]).ToList();
        if (selected.Count == 0)
            throw new ValidationException("Seçilen araçlar bulunamadı (zaten yayınlanmış ya da kapsamınız dışında olabilir).");

        return await CreateAsync(selected, together, ct);
    }

    /// <summary>
    /// "Beraber" modun giriş noktası: form ARAÇ ID'si değil <b>imza</b> gönderir ("bir satırı seçince
    /// o modeldeki araçların hepsi seçilsin" davranışı statik SSR'da JS'siz böyle olur — gizli
    /// checkbox'ların durumu görünen kutuya bağlanamaz). İmza havuzdan araçlara AÇILIR, yani
    /// seçim daima o anda gerçekten uygun olan araç kümesidir.
    /// </summary>
    public async Task<Guid> StepOneSignatureAsync(IReadOnlyList<string> signatures, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (signatures.Count == 0) throw new ValidationException("En az bir araç seçin.");

        var requested = signatures.ToHashSet(StringComparer.Ordinal);
        var selected = (await PoolAsync(ct))
            .Where(k => requested.Contains(k.Imza))
            .SelectMany(k => k.Araclar).ToList();
        if (selected.Count == 0)
            throw new ValidationException("Seçilen araçlar bulunamadı (zaten yayınlanmış ya da kapsamınız dışında olabilir).");

        return await CreateAsync(selected, together: true, ct);
    }

    private async Task<Guid> CreateAsync(IReadOnlyList<Vehicle> selected, bool together, CancellationToken ct)
    {
        var groups = new List<(WebIlan Ilan, bool Yeni, IReadOnlyList<Guid> AracIdler)>();
        // Slug çakışmasını BELLEKTE takip et: "ayrı" mod aynı başlıkla N ilan üretir, hepsi aynı
        // SaveChanges'te yazılır → DB'ye sormak yetmez, yeni verilenler de kümede olmalı.
        var used = (await repository.ListSlugsAsync(ct)).ToHashSet(StringComparer.Ordinal);

        if (together)
        {
            foreach (var set in selected.GroupBy(VehicleSignature.Calculate))
            {
                var list = set.OrderBy(v => v.Plaka, StringComparer.CurrentCulture).ToList();
                var ids = list.Select(v => v.Id).ToList();
                // Aynı imzalı ilan zaten varsa KATIL (ikiz kart engeli) — yeni ilan yaratma.
                var existing = (await repository.FindByKeyAsync(set.Key, ct)).FirstOrDefault();
                groups.Add(existing is not null
                    ? (existing, false, idler: ids)
                    : (NewListing(list, set.Key, used), true, idler: ids));
            }
        }
        else
        {
            // "Ayrı": her araç kendi ilanı. İmza yine YAZILIR — kardeşlere fiyat/özellik kopyalamak
            // ve sonradan alınan aracın hangi kümeye ait olduğunu bilmek için gerekli.
            foreach (var v in selected)
                groups.Add((NewListing([v], VehicleSignature.Calculate(v), used), true, new[] { v.Id }));
        }

        await repository.CreateWithMembershipAsync(groups, ct);
        cache.Invalidate(VehicleService.CacheKey); // araçların WebIlanId'si değişti

        // Sihirbaz TASLAK bir ilanla devam etmeli: katılım yapılan ilan zaten yayında olabilir.
        var proceed = groups.FirstOrDefault(g => g.Yeni);
        return proceed.Ilan?.Id ?? groups[0].Ilan.Id;
    }

    // ---- Adım 2 ----

    /// <summary>
    /// Fiyat. <paramref name="weeklyTotal"/>/<paramref name="monthlyTotal"/> TOPLAM tutardır
    /// (7/30 günün parası) — <c>RateMatrix.GunHaftalik</c> gibi GÜNLÜK ücret DEĞİL. Karıştırmak
    /// fiyatı ~7 kat yanlış basar, o yüzden adlar bilinçli olarak farklı.
    ///
    /// <paramref name="copyToSiblings"/>: "ayrı" modda aynı imzalı TASLAK kardeşlere aynı fiyat
    /// uygulanır (kullanıcı kararı: "tek kez doldur, hepsine kopyala").
    /// </summary>
    public async Task<int> StepTwoAsync(Guid listingId, decimal dailyPrice, decimal? weeklyTotal,
        decimal? monthlyTotal, bool vatIncluded, bool copyToSiblings = true, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (dailyPrice <= 0m) throw new ValidationException("Günlük fiyat sıfırdan büyük olmalıdır.");
        if (weeklyTotal is <= 0m) throw new ValidationException("Haftalık toplam sıfırdan büyük olmalıdır (boş bırakılabilir).");
        if (monthlyTotal is <= 0m) throw new ValidationException("Aylık toplam sıfırdan büyük olmalıdır (boş bırakılabilir).");

        var ok = await repository.UpdateAsync(listingId, i =>
        {
            i.GunlukFiyat = dailyPrice;
            i.HaftalikToplam = weeklyTotal;
            i.AylikToplam = monthlyTotal;
            i.KdvDahil = vatIncluded;
        }, ct);
        if (!ok) throw new ValidationException("İlan bulunamadı.");

        return copyToSiblings ? await repository.CopyPriceToSiblingsAsync(listingId, ct) : 0;
    }

    /// <summary>
    /// F11.1b — adım-2'nin iyimser eşzamanlı hâli: ilan satırı kilitlenir, <paramref name="expectedVersion"/> kilit
    /// altında karşılaştırılır (uyuşmazlık <see cref="ConcurrentModificationException"/>). Doğrulama ve kardeş
    /// kopyalama <see cref="StepTwoAsync(Guid, decimal, decimal?, decimal?, bool, bool, CancellationToken)"/> ile aynı.
    /// </summary>
    public async Task<int> StepTwoAsync(Guid listingId, decimal dailyPrice, decimal? weeklyTotal,
        decimal? monthlyTotal, bool vatIncluded, string expectedVersion, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (dailyPrice <= 0m) throw new ValidationException("Günlük fiyat sıfırdan büyük olmalıdır.");
        if (weeklyTotal is <= 0m) throw new ValidationException("Haftalık toplam sıfırdan büyük olmalıdır (boş bırakılabilir).");
        if (monthlyTotal is <= 0m) throw new ValidationException("Aylık toplam sıfırdan büyük olmalıdır (boş bırakılabilir).");

        var ok = await repository.UpdateAsync(listingId, expectedVersion, i =>
        {
            i.GunlukFiyat = dailyPrice;
            i.HaftalikToplam = weeklyTotal;
            i.AylikToplam = monthlyTotal;
            i.KdvDahil = vatIncluded;
        }, ct);
        if (!ok) throw new ValidationException("İlan bulunamadı.");
        return await repository.CopyPriceToSiblingsAsync(listingId, ct);
    }

    /// <summary>
    /// F11.1b — adım-3'ün iyimser eşzamanlı hâli. Özellik satırları ayrı tabloda olduğu için ilan satırının sürümü
    /// kendiliğinden değişmez: önce satırlar DOĞRULANIR, sonra ilan satırı kilit altında sürümle karşılaştırılıp
    /// "dokunularak" (UpdatedAtUtc) sürümü ilerletilir — aynı sürümle gelen ikinci yazım 409 alır. Ardından
    /// <see cref="StepThreeAsync(Guid, IReadOnlyList{OzellikSatiri}, CancellationToken)"/> ile aynı yol koşar.
    /// </summary>
    public async Task<bool> StepThreeAsync(Guid listingId, IReadOnlyList<OzellikSatiri> rows, string expectedVersion,
        CancellationToken ct = default)
    {
        await GuardAsync(ct);
        _ = CleanFeatures(rows);
        if (!await repository.UpdateAsync(listingId, expectedVersion, _ => { }, ct))
            throw new ValidationException("İlan bulunamadı.");
        return await StepThreeAsync(listingId, rows, ct);
    }

    /// <summary>F11.1b — ilan satırı sürümü (opak; PUT'ta geri gönderilir).</summary>
    public async Task<string?> VersionAsync(Guid listingId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.VersionAsync(listingId, ct);
    }

    /// <summary>Özellik satırlarını kırpar ve sınırları zorlar (boş satırlar atılır).</summary>
    private static List<OzellikSatiri> CleanFeatures(IReadOnlyList<OzellikSatiri> rows)
    {
        var clean = rows
            .Where(s => !string.IsNullOrWhiteSpace(s.Etiket) && !string.IsNullOrWhiteSpace(s.Deger))
            .Select(s => new OzellikSatiri(s.Etiket.Trim(), s.Deger.Trim(), s.Gorunur))
            .ToList();

        if (clean.Count > FeatureSnapshot.MaxRows)
            throw new ValidationException($"En fazla {FeatureSnapshot.MaxRows} özellik satırı eklenebilir.");
        if (clean.Any(s => s.Etiket.Length > FeatureSnapshot.MaxLabel))
            throw new ValidationException($"Özellik adı en çok {FeatureSnapshot.MaxLabel} karakter olabilir.");
        if (clean.Any(s => s.Deger.Length > FeatureSnapshot.MaxValue))
            throw new ValidationException($"Özellik değeri en çok {FeatureSnapshot.MaxValue} karakter olabilir.");
        return clean;
    }

    // ---- Adım 3 ----

    /// <summary>Teknik özellikleri yazar ve ilanı YAYINA alır. Satır sınırları burada zorlanır
    /// (sınırsız satır bir tenant'ın kendi sayfasını şişirir).</summary>
    /// <summary>
    /// Adım-3: özellikleri kaydeder ve — <b>fotoğraf varsa</b> — taslağı yayına alır.
    /// </summary>
    /// <returns><c>true</c>: ilan yayında. <c>false</c>: özellikler kaydedildi ama fotoğraf olmadığı
    /// için taslakta kaldı (uç bunu kullanıcıya söyler).</returns>
    public async Task<bool> StepThreeAsync(Guid listingId, IReadOnlyList<OzellikSatiri> rows, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var clean = CleanFeatures(rows);

        var d = await repository.FindAsync(listingId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        await repository.ReplaceFeaturesAsync(listingId,
            [.. clean.Select((s, i) => new WebIlanOzellik { Etiket = s.Etiket, Deger = s.Deger, Sira = i, Gorunur = s.Gorunur })],
            ct);

        // FOTO KAPISI. Halka açık vitrinin yayın şartlarından biri "üye araçlardan en az birinin
        // fotoğrafı var" (FleetShowcaseService). Fotoğrafsız bir ilanı Yayinda'ya almak, personele
        // "yayınlandı" deyip sitede HİÇBİR ŞEY göstermemek demekti — canlıda tam olarak bu yaşandı.
        // Bu yüzden: özellikler HER ZAMAN kaydedilir (emek kaybolmaz), ama yayına almak fotoğrafa bağlı.
        var withPhoto = await photos.ListVehicleIdsWithPhotoAsync(
            [.. d.Araclar.Select(v => v.Id)], ct);
        var published = false;
        if (d.Araclar.Any(v => withPhoto.Contains(v.Id)))
        {
            // Zaten Yayinda/Pasif ise durumu DEĞİŞTİRME (personel bilinçli gizlemiş olabilir).
            if (d.Ilan.Durum == WebIlanDurum.Taslak)
                await repository.UpdateAsync(listingId, i => i.Durum = WebIlanDurum.Yayinda, ct);
            published = true;
        }

        // "Ayrı" modda kardeş taslaklara aynı özellikleri kopyala (fiyatla aynı kural). Foto kapısı
        // kardeş BAŞINA uygulanır: ayrı modda her ilanın kendi aracı ve kendi fotoğrafı var.
        var siblings = (await repository.FindByKeyAsync(d.Ilan.EslesmeAnahtari ?? "", ct))
            .Where(k => k.Id != listingId && k.Durum == WebIlanDurum.Taslak).ToList();
        foreach (var sibling in siblings)
        {
            await repository.ReplaceFeaturesAsync(sibling.Id,
                [.. clean.Select((s, i) => new WebIlanOzellik { Etiket = s.Etiket, Deger = s.Deger, Sira = i, Gorunur = s.Gorunur })],
                ct);
            var kd = await repository.FindAsync(sibling.Id, ct);
            if (kd is null) continue;
            var kf = await photos.ListVehicleIdsWithPhotoAsync([.. kd.Araclar.Select(v => v.Id)], ct);
            if (kd.Araclar.Any(v => kf.Contains(v.Id)))
                await repository.UpdateAsync(sibling.Id, i => i.Durum = WebIlanDurum.Yayinda, ct);
        }

        return published;
    }

    // ---- Fotoğraflar (sihirbaz adım-3) ----

    /// <summary>
    /// İlanın fotoğrafları = ÜYE ARAÇLARIN fotoğrafları. İlana ayrı bir foto tablosu AÇILMADI:
    /// vitrin kapağı zaten "fotoğrafı olan ilk gösterilebilir araç" üzerinden hesaplanıyor
    /// (<c>FleetShowcaseService</c>), ikinci bir kaynak eklemek iki gerçek yaratırdı.
    /// </summary>
    public async Task<IReadOnlyList<IlanFotoSatiri>> PhotosAsync(Guid listingId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(listingId, ct);
        if (d is null) return [];

        var result = new List<IlanFotoSatiri>();
        foreach (var v in d.Araclar)
            foreach (var m in await photos.ListMetaAsync(v.Id, ct))
                result.Add(new IlanFotoSatiri(v.Id, m.Id, v.Plaka, m.Sira));
        return result;
    }

    /// <summary>
    /// İlana fotoğraf ekler. Hedef araç DETERMİNİSTİK seçilir: fotoğrafı olan ilk üye, yoksa ilk üye.
    /// Böylece "beraber" modda 9 aynı Egea için bayt 9 kez kopyalanmaz — vitrin tek kart gösteriyor,
    /// tek kapak yeter. Doğrulama/thumbnail üretimi <see cref="VehiclePhotoService"/>'te (tek kaynak).
    /// </summary>
    public async Task AddPhotoAsync(Guid listingId, byte[] bytes, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(listingId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        if (d.Araclar.Count == 0)
            throw new ValidationException("İlana bağlı araç yok — fotoğraf eklenemez.");

        var withPhoto = await photos.ListVehicleIdsWithPhotoAsync([.. d.Araclar.Select(v => v.Id)], ct);
        var target = d.Araclar.FirstOrDefault(v => withPhoto.Contains(v.Id)) ?? d.Araclar[0];
        await photoService.AddAsync(target.Id, bytes, ct);
    }

    /// <summary>
    /// Fotoğrafı siler. <paramref name="vehicleId"/> bu ilanın ÜYESİ olmak zorunda — aksi halde ilan
    /// id'si üzerinden başka bir aracın fotoğrafı silinebilirdi (yetki var, hedef yanlış).
    /// </summary>
    public async Task DeletePhotoAsync(Guid listingId, Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(listingId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        if (d.Araclar.All(v => v.Id != vehicleId))
            throw new ValidationException("Bu fotoğraf bu ilana ait değil.");
        await photoService.DeleteAsync(vehicleId, photoId, ct);
    }

    // ---- Yönetim ----

    public async Task SetStatusAsync(Guid listingId, WebIlanDurum status, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (status == WebIlanDurum.Taslak) throw new ValidationException("İlan taslağa geri alınamaz.");
        if (!await repository.UpdateAsync(listingId, i => i.Durum = status, ct))
            throw new ValidationException("İlan bulunamadı.");
    }

    public async Task DeleteAsync(Guid listingId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!await repository.DeleteAsync(listingId, ct)) throw new ValidationException("İlan bulunamadı.");
        cache.Invalidate(VehicleService.CacheKey); // araçlar yeniden "ilansız" oldu
    }

    /// <summary>Tanılama: web'de hiç yayınlanmamış araç sayısı (operatör kapsamıyla).</summary>
    public async Task<int> UnlistedVehicleCountAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return (await PoolAsync(ct)).Sum(k => k.Araclar.Count);
    }

    // ---- Yardımcılar ----

    private static WebIlan NewListing(IReadOnlyList<Vehicle> vehicles, string signature, HashSet<string> usedSlugs)
    {
        var title = VehicleSignature.Header(vehicles);
        return new WebIlan
        {
            Baslik = title,
            Slug = UniqueSlug(title, usedSlugs),
            EslesmeAnahtari = signature,
            Durum = WebIlanDurum.Taslak,
        };
    }

    /// <summary>
    /// Başlıktan Türkçe-doğru slug; çakışırsa <c>-2</c>, <c>-3</c>… ekler. Blog'dan FARKLI olarak
    /// hata FIRLATMAZ: "ayrı göster" modu aynı başlıkla N ilan üretmek İÇİN vardır (12 Egea = 12
    /// kart), çakışma burada beklenen durumdur. <paramref name="used"/> hem DB'dekileri hem
    /// bu çağrıda üretilenleri taşır (hepsi tek SaveChanges'te yazılıyor).
    /// </summary>
    private static string UniqueSlug(string title, HashSet<string> used)
    {
        var floor = TurkishText.Slugify(title);
        if (floor.Length == 0) floor = "arac"; // başlık tamamen simgeyse adres yine üretilebilsin
        var candidate = floor;
        var n = 1;
        while (!used.Add(candidate)) candidate = $"{floor}-{++n}";
        return candidate;
    }

    /// <summary>Aracın serbest-metin <c>Grup</c> değerine Türkçe-duyarsız eşleşen aktif grup
    /// (koltuk/kapı/bagaj oradan gelir — <see cref="Vehicle"/>'da bu alanlar yok).</summary>
    private static VehicleGroup? FindGroup(IReadOnlyList<VehicleGroup> activeGroups, Vehicle vehicle)
        => activeGroups.FirstOrDefault(g => TurkishText.EqualsIgnoreTurkishCase(g.Ad, vehicle.Grup));
}
