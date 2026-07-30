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
///   <item><see cref="AdimBirAsync"/> — araç seç → TASLAK ilan(lar) + araç üyelikleri</item>
///   <item><see cref="AdimIkiAsync"/> — fiyat</item>
///   <item><see cref="AdimUcAsync"/> — teknik özellikler → YAYINDA</item>
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
public sealed class WebIlanService(
    IWebIlanRepository repository,
    IVehicleGroupRepository gruplar,
    IVehiclePhotoRepository fotograflar,
    VehiclePhotoService fotoServisi,
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
    public async Task<IReadOnlyList<AracKumesi>> HavuzAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var kapsam = BranchScope.EffectiveFilter(currentUser);
        var araclar = (await repository.ListIlansizAraclarAsync(ct))
            .Where(v => kapsam.Unrestricted || BranchScope.InScope(kapsam, v.SubeId, v.Sube))
            .ToList();

        return [.. araclar
            .GroupBy(AracImza.Hesapla)
            .Select(g =>
            {
                var liste = g.OrderBy(v => v.Plaka, StringComparer.CurrentCulture).ToList();
                return new AracKumesi(g.Key, AracImza.Baslik(liste), AracImza.YilAralik(liste), liste);
            })
            .OrderByDescending(k => k.Araclar.Count)
            .ThenBy(k => k.Baslik, StringComparer.CurrentCulture)];
    }

    /// <summary>İlan listesi + her birinin yayına girmesi için eksikleri.
    /// Foto durumu TEK TOPLU sorguyla alınır (ilan başına sorgu N+1 üretirdi).</summary>
    public async Task<IReadOnlyList<WebIlanSatiri>> ListAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var detaylar = await repository.ListAsync(ct);
        var aktifGruplar = await gruplar.ListActiveAsync(ct);
        var fotoluAracIdler = await fotograflar.ListVehicleIdsWithPhotoAsync(
            [.. detaylar.SelectMany(d => d.Araclar).Select(v => v.Id).Distinct()], ct);
        var satirlar = new List<WebIlanSatiri>();

        foreach (var d in detaylar)
        {
            var eksikler = new List<string>();
            if (d.Araclar.Count == 0) eksikler.Add("Araç yok");
            else if (!d.Araclar.Any(v => fotoluAracIdler.Contains(v.Id))) eksikler.Add("Foto yok");
            if (d.Ilan.GunlukFiyat <= 0m) eksikler.Add("Fiyat girilmedi");
            if (d.Ilan.Durum == WebIlanDurum.Taslak) eksikler.Add("Taslak — sihirbaz tamamlanmadı");
            if (d.Ilan.Durum == WebIlanDurum.Pasif) eksikler.Add("Pasif (gizlendi)");

            var bayat = d.Araclar.Count > 0
                && OzellikSnapshot.Bayatladi(d.Ozellikler, d.Araclar, GrupBul(aktifGruplar, d.Araclar[0]));

            satirlar.Add(new WebIlanSatiri(d.Ilan.Id, d.Ilan.Baslik, d.Ilan.Durum, d.Araclar.Count,
                d.Araclar.Sum(v => v.VitrinAdet ?? 1), d.Ilan.GunlukFiyat, d.Ilan.KdvDahil, eksikler, bayat));
        }
        return satirlar;
    }

    public async Task<WebIlanDetay?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return await repository.FindAsync(id, ct);
    }

    /// <summary>"Ayrı" modda aynı imzadan yaratılmış ve HÂLÂ TASLAK olan diğer ilan sayısı — sihirbaz
    /// ekranında "girdiğiniz değer N ilana daha uygulanacak" bilgisini basmak için.</summary>
    public async Task<int> KardesTaslakSayisiAsync(Guid ilanId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(ilanId, ct);
        if (d?.Ilan.EslesmeAnahtari is not { } anahtar) return 0;
        return (await repository.FindByAnahtarAsync(anahtar, ct))
            .Count(i => i.Id != ilanId && i.Durum == WebIlanDurum.Taslak);
    }

    /// <summary>Adım-3 ekranının başlangıç satırları: kayıtlı özellik varsa o, yoksa araçlardan
    /// üretilen snapshot.</summary>
    public async Task<IReadOnlyList<OzellikSatiri>> OnerilenOzelliklerAsync(Guid ilanId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(ilanId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        if (d.Ozellikler.Count > 0)
            return [.. d.Ozellikler.Select(o => new OzellikSatiri(o.Etiket, o.Deger, o.Gorunur))];
        if (d.Araclar.Count == 0) return [];
        return OzellikSnapshot.Uret(d.Araclar, GrupBul(await gruplar.ListActiveAsync(ct), d.Araclar[0]));
    }

    // ---- Adım 1 ----

    /// <summary>
    /// Seçilen araçlardan TASLAK ilan(lar) üretir ve araçları bağlar.
    ///
    /// <paramref name="beraber"/> = true → "aynı araç" kümesi başına TEK ilan (12 Egea = 1 kart).
    /// false → her araç kendi ilanı (12 Egea = 12 kart); fiyat/özellik adımları bir kez doldurulup
    /// kardeşlere kopyalanır.
    ///
    /// <para><b>İKİZ İLAN KORUMASI:</b> "beraber" modda aynı imzalı bir ilan ZATEN varsa yeni ilan
    /// yaratılmaz, araçlar MEVCUDA katılır. Aksi halde çok-şubeli tenant'ta İstanbul operatörü
    /// "Egea 1.500 ₺", Ankara operatörü "Egea 1.700 ₺" yaratır ve sitede iki özdeş kart çıkardı.</para>
    ///
    /// <returns>Sihirbazın devam edeceği ilan Id'si (birden çok yaratıldıysa ilki).</returns>
    /// </summary>
    public async Task<Guid> AdimBirAsync(IReadOnlyList<Guid> aracIdler, bool beraber, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (aracIdler.Count == 0) throw new ValidationException("En az bir araç seçin.");

        // WHITELIST: seçilen id gerçekten kapsamda VE ilansız mı (form'dan gelen id'ye güvenilmez).
        var havuz = (await HavuzAsync(ct)).SelectMany(k => k.Araclar).ToDictionary(v => v.Id);
        var secilen = aracIdler.Distinct().Where(havuz.ContainsKey).Select(id => havuz[id]).ToList();
        if (secilen.Count == 0)
            throw new ValidationException("Seçilen araçlar bulunamadı (zaten yayınlanmış ya da kapsamınız dışında olabilir).");

        return await OlusturAsync(secilen, beraber, ct);
    }

    /// <summary>
    /// "Beraber" modun giriş noktası: form ARAÇ ID'si değil <b>imza</b> gönderir ("bir satırı seçince
    /// o modeldeki araçların hepsi seçilsin" davranışı statik SSR'da JS'siz böyle olur — gizli
    /// checkbox'ların durumu görünen kutuya bağlanamaz). İmza havuzdan araçlara AÇILIR, yani
    /// seçim daima o anda gerçekten uygun olan araç kümesidir.
    /// </summary>
    public async Task<Guid> AdimBirImzaAsync(IReadOnlyList<string> imzalar, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (imzalar.Count == 0) throw new ValidationException("En az bir araç seçin.");

        var istenen = imzalar.ToHashSet(StringComparer.Ordinal);
        var secilen = (await HavuzAsync(ct))
            .Where(k => istenen.Contains(k.Imza))
            .SelectMany(k => k.Araclar).ToList();
        if (secilen.Count == 0)
            throw new ValidationException("Seçilen araçlar bulunamadı (zaten yayınlanmış ya da kapsamınız dışında olabilir).");

        return await OlusturAsync(secilen, beraber: true, ct);
    }

    private async Task<Guid> OlusturAsync(IReadOnlyList<Vehicle> secilen, bool beraber, CancellationToken ct)
    {
        var gruplar = new List<(WebIlan Ilan, bool Yeni, IReadOnlyList<Guid> AracIdler)>();
        // Slug çakışmasını BELLEKTE takip et: "ayrı" mod aynı başlıkla N ilan üretir, hepsi aynı
        // SaveChanges'te yazılır → DB'ye sormak yetmez, yeni verilenler de kümede olmalı.
        var kullanilan = (await repository.ListSluglarAsync(ct)).ToHashSet(StringComparer.Ordinal);

        if (beraber)
        {
            foreach (var kume in secilen.GroupBy(AracImza.Hesapla))
            {
                var liste = kume.OrderBy(v => v.Plaka, StringComparer.CurrentCulture).ToList();
                var idler = liste.Select(v => v.Id).ToList();
                // Aynı imzalı ilan zaten varsa KATIL (ikiz kart engeli) — yeni ilan yaratma.
                var mevcut = (await repository.FindByAnahtarAsync(kume.Key, ct)).FirstOrDefault();
                gruplar.Add(mevcut is not null
                    ? (mevcut, false, idler)
                    : (YeniIlan(liste, kume.Key, kullanilan), true, idler));
            }
        }
        else
        {
            // "Ayrı": her araç kendi ilanı. İmza yine YAZILIR — kardeşlere fiyat/özellik kopyalamak
            // ve sonradan alınan aracın hangi kümeye ait olduğunu bilmek için gerekli.
            foreach (var v in secilen)
                gruplar.Add((YeniIlan([v], AracImza.Hesapla(v), kullanilan), true, new[] { v.Id }));
        }

        await repository.CreateWithUyelikAsync(gruplar, ct);
        cache.Invalidate(VehicleService.CacheKey); // araçların WebIlanId'si değişti

        // Sihirbaz TASLAK bir ilanla devam etmeli: katılım yapılan ilan zaten yayında olabilir.
        var devam = gruplar.FirstOrDefault(g => g.Yeni);
        return devam.Ilan?.Id ?? gruplar[0].Ilan.Id;
    }

    // ---- Adım 2 ----

    /// <summary>
    /// Fiyat. <paramref name="haftalikToplam"/>/<paramref name="aylikToplam"/> TOPLAM tutardır
    /// (7/30 günün parası) — <c>RateMatrix.GunHaftalik</c> gibi GÜNLÜK ücret DEĞİL. Karıştırmak
    /// fiyatı ~7 kat yanlış basar, o yüzden adlar bilinçli olarak farklı.
    ///
    /// <paramref name="kardeslereKopyala"/>: "ayrı" modda aynı imzalı TASLAK kardeşlere aynı fiyat
    /// uygulanır (kullanıcı kararı: "tek kez doldur, hepsine kopyala").
    /// </summary>
    public async Task<int> AdimIkiAsync(Guid ilanId, decimal gunlukFiyat, decimal? haftalikToplam,
        decimal? aylikToplam, bool kdvDahil, bool kardeslereKopyala = true, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (gunlukFiyat <= 0m) throw new ValidationException("Günlük fiyat sıfırdan büyük olmalıdır.");
        if (haftalikToplam is <= 0m) throw new ValidationException("Haftalık toplam sıfırdan büyük olmalıdır (boş bırakılabilir).");
        if (aylikToplam is <= 0m) throw new ValidationException("Aylık toplam sıfırdan büyük olmalıdır (boş bırakılabilir).");

        var ok = await repository.UpdateAsync(ilanId, i =>
        {
            i.GunlukFiyat = gunlukFiyat;
            i.HaftalikToplam = haftalikToplam;
            i.AylikToplam = aylikToplam;
            i.KdvDahil = kdvDahil;
        }, ct);
        if (!ok) throw new ValidationException("İlan bulunamadı.");

        return kardeslereKopyala ? await repository.KardeslereFiyatKopyalaAsync(ilanId, ct) : 0;
    }

    // ---- Adım 3 ----

    /// <summary>Teknik özellikleri yazar ve ilanı YAYINA alır. Satır sınırları burada zorlanır
    /// (sınırsız satır bir tenant'ın kendi sayfasını şişirir).</summary>
    /// <summary>
    /// Adım-3: özellikleri kaydeder ve — <b>fotoğraf varsa</b> — taslağı yayına alır.
    /// </summary>
    /// <returns><c>true</c>: ilan yayında. <c>false</c>: özellikler kaydedildi ama fotoğraf olmadığı
    /// için taslakta kaldı (uç bunu kullanıcıya söyler).</returns>
    public async Task<bool> AdimUcAsync(Guid ilanId, IReadOnlyList<OzellikSatiri> satirlar, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var temiz = satirlar
            .Where(s => !string.IsNullOrWhiteSpace(s.Etiket) && !string.IsNullOrWhiteSpace(s.Deger))
            .Select(s => new OzellikSatiri(s.Etiket.Trim(), s.Deger.Trim(), s.Gorunur))
            .ToList();

        if (temiz.Count > OzellikSnapshot.MaxSatir)
            throw new ValidationException($"En fazla {OzellikSnapshot.MaxSatir} özellik satırı eklenebilir.");
        if (temiz.Any(s => s.Etiket.Length > OzellikSnapshot.MaxEtiket))
            throw new ValidationException($"Özellik adı en çok {OzellikSnapshot.MaxEtiket} karakter olabilir.");
        if (temiz.Any(s => s.Deger.Length > OzellikSnapshot.MaxDeger))
            throw new ValidationException($"Özellik değeri en çok {OzellikSnapshot.MaxDeger} karakter olabilir.");

        var d = await repository.FindAsync(ilanId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        await repository.ReplaceOzelliklerAsync(ilanId,
            [.. temiz.Select((s, i) => new WebIlanOzellik { Etiket = s.Etiket, Deger = s.Deger, Sira = i, Gorunur = s.Gorunur })],
            ct);

        // FOTO KAPISI. Halka açık vitrinin yayın şartlarından biri "üye araçlardan en az birinin
        // fotoğrafı var" (FleetShowcaseService). Fotoğrafsız bir ilanı Yayinda'ya almak, personele
        // "yayınlandı" deyip sitede HİÇBİR ŞEY göstermemek demekti — canlıda tam olarak bu yaşandı.
        // Bu yüzden: özellikler HER ZAMAN kaydedilir (emek kaybolmaz), ama yayına almak fotoğrafa bağlı.
        var fotolu = await fotograflar.ListVehicleIdsWithPhotoAsync(
            [.. d.Araclar.Select(v => v.Id)], ct);
        var yayinlandi = false;
        if (d.Araclar.Any(v => fotolu.Contains(v.Id)))
        {
            // Zaten Yayinda/Pasif ise durumu DEĞİŞTİRME (personel bilinçli gizlemiş olabilir).
            if (d.Ilan.Durum == WebIlanDurum.Taslak)
                await repository.UpdateAsync(ilanId, i => i.Durum = WebIlanDurum.Yayinda, ct);
            yayinlandi = true;
        }

        // "Ayrı" modda kardeş taslaklara aynı özellikleri kopyala (fiyatla aynı kural). Foto kapısı
        // kardeş BAŞINA uygulanır: ayrı modda her ilanın kendi aracı ve kendi fotoğrafı var.
        var kardesler = (await repository.FindByAnahtarAsync(d.Ilan.EslesmeAnahtari ?? "", ct))
            .Where(k => k.Id != ilanId && k.Durum == WebIlanDurum.Taslak).ToList();
        foreach (var kardes in kardesler)
        {
            await repository.ReplaceOzelliklerAsync(kardes.Id,
                [.. temiz.Select((s, i) => new WebIlanOzellik { Etiket = s.Etiket, Deger = s.Deger, Sira = i, Gorunur = s.Gorunur })],
                ct);
            var kd = await repository.FindAsync(kardes.Id, ct);
            if (kd is null) continue;
            var kf = await fotograflar.ListVehicleIdsWithPhotoAsync([.. kd.Araclar.Select(v => v.Id)], ct);
            if (kd.Araclar.Any(v => kf.Contains(v.Id)))
                await repository.UpdateAsync(kardes.Id, i => i.Durum = WebIlanDurum.Yayinda, ct);
        }

        return yayinlandi;
    }

    // ---- Fotoğraflar (sihirbaz adım-3) ----

    /// <summary>
    /// İlanın fotoğrafları = ÜYE ARAÇLARIN fotoğrafları. İlana ayrı bir foto tablosu AÇILMADI:
    /// vitrin kapağı zaten "fotoğrafı olan ilk gösterilebilir araç" üzerinden hesaplanıyor
    /// (<c>FleetShowcaseService</c>), ikinci bir kaynak eklemek iki gerçek yaratırdı.
    /// </summary>
    public async Task<IReadOnlyList<IlanFotoSatiri>> FotolarAsync(Guid ilanId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(ilanId, ct);
        if (d is null) return [];

        var sonuc = new List<IlanFotoSatiri>();
        foreach (var v in d.Araclar)
            foreach (var m in await fotograflar.ListMetaAsync(v.Id, ct))
                sonuc.Add(new IlanFotoSatiri(v.Id, m.Id, v.Plaka, m.Sira));
        return sonuc;
    }

    /// <summary>
    /// İlana fotoğraf ekler. Hedef araç DETERMİNİSTİK seçilir: fotoğrafı olan ilk üye, yoksa ilk üye.
    /// Böylece "beraber" modda 9 aynı Egea için bayt 9 kez kopyalanmaz — vitrin tek kart gösteriyor,
    /// tek kapak yeter. Doğrulama/thumbnail üretimi <see cref="VehiclePhotoService"/>'te (tek kaynak).
    /// </summary>
    public async Task FotoEkleAsync(Guid ilanId, byte[] bytes, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(ilanId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        if (d.Araclar.Count == 0)
            throw new ValidationException("İlana bağlı araç yok — fotoğraf eklenemez.");

        var fotolu = await fotograflar.ListVehicleIdsWithPhotoAsync([.. d.Araclar.Select(v => v.Id)], ct);
        var hedef = d.Araclar.FirstOrDefault(v => fotolu.Contains(v.Id)) ?? d.Araclar[0];
        await fotoServisi.AddAsync(hedef.Id, bytes, ct);
    }

    /// <summary>
    /// Fotoğrafı siler. <paramref name="vehicleId"/> bu ilanın ÜYESİ olmak zorunda — aksi halde ilan
    /// id'si üzerinden başka bir aracın fotoğrafı silinebilirdi (yetki var, hedef yanlış).
    /// </summary>
    public async Task FotoSilAsync(Guid ilanId, Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        var d = await repository.FindAsync(ilanId, ct) ?? throw new ValidationException("İlan bulunamadı.");
        if (d.Araclar.All(v => v.Id != vehicleId))
            throw new ValidationException("Bu fotoğraf bu ilana ait değil.");
        await fotoServisi.DeleteAsync(vehicleId, photoId, ct);
    }

    // ---- Yönetim ----

    public async Task SetDurumAsync(Guid ilanId, WebIlanDurum durum, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (durum == WebIlanDurum.Taslak) throw new ValidationException("İlan taslağa geri alınamaz.");
        if (!await repository.UpdateAsync(ilanId, i => i.Durum = durum, ct))
            throw new ValidationException("İlan bulunamadı.");
    }

    public async Task SilAsync(Guid ilanId, CancellationToken ct = default)
    {
        await GuardAsync(ct);
        if (!await repository.DeleteAsync(ilanId, ct)) throw new ValidationException("İlan bulunamadı.");
        cache.Invalidate(VehicleService.CacheKey); // araçlar yeniden "ilansız" oldu
    }

    /// <summary>Tanılama: web'de hiç yayınlanmamış araç sayısı (operatör kapsamıyla).</summary>
    public async Task<int> IlansizAracSayisiAsync(CancellationToken ct = default)
    {
        await GuardAsync(ct);
        return (await HavuzAsync(ct)).Sum(k => k.Araclar.Count);
    }

    // ---- Yardımcılar ----

    private static WebIlan YeniIlan(IReadOnlyList<Vehicle> araclar, string imza, HashSet<string> kullanilanSluglar)
    {
        var baslik = AracImza.Baslik(araclar);
        return new WebIlan
        {
            Baslik = baslik,
            Slug = BenzersizSlug(baslik, kullanilanSluglar),
            EslesmeAnahtari = imza,
            Durum = WebIlanDurum.Taslak,
        };
    }

    /// <summary>
    /// Başlıktan Türkçe-doğru slug; çakışırsa <c>-2</c>, <c>-3</c>… ekler. Blog'dan FARKLI olarak
    /// hata FIRLATMAZ: "ayrı göster" modu aynı başlıkla N ilan üretmek İÇİN vardır (12 Egea = 12
    /// kart), çakışma burada beklenen durumdur. <paramref name="kullanilan"/> hem DB'dekileri hem
    /// bu çağrıda üretilenleri taşır (hepsi tek SaveChanges'te yazılıyor).
    /// </summary>
    private static string BenzersizSlug(string baslik, HashSet<string> kullanilan)
    {
        var taban = TurkishText.Slugify(baslik);
        if (taban.Length == 0) taban = "arac"; // başlık tamamen simgeyse adres yine üretilebilsin
        var aday = taban;
        var n = 1;
        while (!kullanilan.Add(aday)) aday = $"{taban}-{++n}";
        return aday;
    }

    /// <summary>Aracın serbest-metin <c>Grup</c> değerine Türkçe-duyarsız eşleşen aktif grup
    /// (koltuk/kapı/bagaj oradan gelir — <see cref="Vehicle"/>'da bu alanlar yok).</summary>
    private static VehicleGroup? GrupBul(IReadOnlyList<VehicleGroup> aktifGruplar, Vehicle arac)
        => aktifGruplar.FirstOrDefault(g => TurkishText.EqualsIgnoreTurkishCase(g.Ad, arac.Grup));
}
