using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleGroups;

/// <summary>Bilinen hiçbir aktif `VehicleGroup.Ad`'a (Türkçe-duyarlı normalize dahil) eşleşmeyen,
/// filodaki DISTINCT `Vehicle.Grup` serbest-metin değeri (PR-4.5 tanılama — bkz. FleetShowcaseService
/// doc-yorumu: case-fold bunu çözmez, bu ayrıksı bir veri-kalitesi sinyalidir).
/// PR-10: <paramref name="Bos"/> true olan satır, grubu hiç GİRİLMEMİŞ araçları toplar (PR-10 öncesi
/// açılmış kayıtlar varsayılan kuralından etkilenmez) — atama çağrısına string yerine bu BAYRAKLA
/// gider, aksi halde gerçekten "(boş)" yazan bir grup değeriyle karışırdı.</summary>
public sealed record UnmatchedGrupValue(string Grup, int AracSayisi, bool Bos = false);

/// <summary>
/// Araç grubu master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. Açılır liste okuması
/// (<see cref="ListActiveAsync"/>) yetkisizdir (araç kayıt formu çağırır). Tenant izolasyonu/audit
/// alt katmanda otomatik.
/// </summary>
public sealed class VehicleGroupService(
    IVehicleGroupRepository repository, ICurrentUser currentUser, VehicleService vehicles, ITenantCache cache)
{
    private readonly IVehicleGroupRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<VehicleGroup>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Form açılır listesi kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<VehicleGroup>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    /// <summary>
    /// FAZ-20 — grup ADI başına araç sayısı (liste ekranındaki "Araç Sayısı" kolonu).
    ///
    /// <para>Eşleştirme, <see cref="ListUnmatchedGroupValuesAsync"/> ile AYNI kuralı kullanır
    /// (<c>TurkishText.EqualsIgnoreTurkishCase</c>): araçtaki grup bir METİN alanıdır, FK değil —
    /// "Ekonomik" ile "EKONOMİK" aynı gruptur. İki yerde iki farklı eşleştirme kuralı olsaydı
    /// "eşleşmeyen" listesi ile sayaç birbirini tutmazdı.</para>
    ///
    /// <para>Dönen sözlüğün anahtarı grup ADIdır; listede olmayan ad hiç görünmez (sayaç 0 olarak
    /// okunur).</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> VehicleCountsAsync(CancellationToken ct = default)
    {
        var groups = await ListAsync(ct);
        var fleet = await vehicles.ListAsync(ct);
        return groups.ToDictionary(
            g => g.Id,
            g => fleet.Count(v => !string.IsNullOrWhiteSpace(v.Grup)
                                 && TurkishText.EqualsIgnoreTurkishCase(g.Ad, v.Grup!)));
    }

    /// <summary>PR-4.5 tanılama — engelleyici değil, yalnız görünürlük. Yetki gerektirmez (okuma).</summary>
    public async Task<IReadOnlyList<UnmatchedGrupValue>> ListUnmatchedGroupValuesAsync(CancellationToken ct = default)
    {
        var activeNames = (await ListActiveAsync(ct)).Select(g => g.Ad).ToList();
        var fleet = await vehicles.ListAsync(ct);

        var result = fleet
            .Select(v => v.Grup)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .Where(g => !activeNames.Any(name => TurkishText.EqualsIgnoreTurkishCase(name, g)))
            .GroupBy(g => g, StringComparer.Ordinal)
            .Select(grp => new UnmatchedGrupValue(grp.Key, grp.Count()))
            .OrderByDescending(x => x.AracSayisi)
            .ToList();

        // PR-10: grubu hiç girilmemiş araçlar da atanabilir bir kaynaktır (en sonda — bunlar bir
        // "yanlış değer" değil, eksik değerdir).
        var emptyCount = fleet.Count(v => string.IsNullOrWhiteSpace(v.Grup));
        if (emptyCount > 0) result.Add(new UnmatchedGrupValue("(boş)", emptyCount, Bos: true));

        return result;
    }

    public Task<VehicleGroup?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleGroupInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç grubu zaten var.");
        if (await _repository.NameExistsAsync(n.Ad, excludeId: null, ct))
            throw new ValidationException($"'{n.Ad}' adlı araç grubu zaten var.");

        var group = new VehicleGroup();
        Apply(group, n);
        await _repository.CreateAsync(group, ct);
        return group.Id;
    }

    public Task<bool> UpdateAsync(Guid id, VehicleGroupInput input, CancellationToken ct = default)
        => UpdateAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1b — satır sürümü (opak); yoksa <c>null</c>.</summary>
    public Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default) => _repository.RowVersionAsync(id, ct);

    /// <summary>F11.1b — <paramref name="expectedVersion"/> doluysa kilit altında sürüm karşılaştırmalı tam değiştirme
    /// (rename cascade aynı işlemde).</summary>
    public async Task<bool> UpdateAsync(Guid id, VehicleGroupInput input, string? expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç grubu zaten var.");
        // Ad taşıyıcı kolondur (araç eşlemesi/cascade/vitrin hep Ad üstünden) → mevcut bir adın
        // ÜSTÜNE rename edilirse iki grubun filosu tek isim havuzunda birleşirdi.
        if (await _repository.NameExistsAsync(n.Ad, excludeId: id, ct))
            throw new ValidationException($"'{n.Ad}' adlı araç grubu zaten var.");

        void ApplyAll(VehicleGroup group)
        {
            Apply(group, n);
            group.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        var result = expectedVersion is null
            ? await _repository.UpdateAsync(id, ApplyAll, ct)
            : await _repository.UpdateAsync(id, expectedVersion, ApplyAll, ct);

        // Cascade araçların Grup değerini değiştirdiyse araç listesi cache'i bayat kaldı.
        // (Grup listesi cache'lenmiyor — invalidate edilecek ayrı bir anahtarı yok.)
        if (result.TasinanArac > 0) cache.Invalidate(VehicleService.CacheKey);
        return result.Bulundu;
    }

    /// <summary>
    /// PR-10 eşleme aracı: filodaki serbest-metin bir <c>Grup</c> değerini tanımlı bir gruba taşır
    /// (Araç Grupları ekranındaki tanılama panelinin "Ata" butonu). Rename cascade ile AYNI repo
    /// çekirdeğini kullanır — tek davranış, tek Türkçe-karşılaştırma kuralı.
    /// </summary>
    /// <param name="sourceValue">Taşınacak serbest-metin değer (<paramref name="emptyOnes"/> true ise yok sayılır).</param>
    /// <param name="emptyOnes">true → kaynak, <c>Grup</c>'u BOŞ olan araçlardır (panelde "(boş)" satırı).</param>
    /// <returns>Taşınan araç sayısı.</returns>
    public async Task<int> AssignGroupValueAsync(string? sourceValue, bool emptyOnes, Guid targetGroupId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (!emptyOnes && string.IsNullOrWhiteSpace(sourceValue))
            throw new ValidationException("Kaynak grup değeri zorunludur.");

        var target = await _repository.FindAsync(targetGroupId, ct)
            ?? throw new ValidationException("Hedef araç grubu bulunamadı.");
        // Pasif gruba atamak araçları sessizce görünmez yapardı (vitrin yalnız aktif grupları okur).
        if (!target.Aktif)
            throw new ValidationException($"'{target.Ad}' grubu pasif — araçlar atanamaz. Önce grubu aktifleştirin.");

        var n = await _repository.MoveGroupValueAsync(sourceValue?.Trim(), emptyOnes, target.Ad, ct);
        if (n > 0) cache.Invalidate(VehicleService.CacheKey);
        return n;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(VehicleGroupInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Araç grubu kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Araç grubu kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Araç grubu adı zorunludur.");

        if (!string.IsNullOrEmpty(n.Sipp) && n.Sipp.Length != 4)
            throw new ValidationException("SIPP kodu 4 harf olmalıdır (ör. CDMD).");
        RequireNonNegativeInt(n.KoltukSayisi, "Koltuk sayısı");
        RequireNonNegativeInt(n.KapiSayisi, "Kapı sayısı");
        RequireNonNegativeInt(n.BagajSayisi, "Bagaj sayısı");
        RequireNonNegativeInt(n.KucukBagaj, "Küçük bagaj");
        RequireNonNegativeInt(n.BuyukBagaj, "Büyük bagaj");
        RequireNonNegativeInt(n.GunlukKmLimiti, "Günlük KM limiti");
        RequireNonNegativeInt(n.AylikMaxKm, "Aylık max KM");
        RequireNonNegativeInt(n.WebSira, "Web sıra");
        RequireNonNegativeInt(n.UpgradeSira, "Upgrade sıra");
        RequireNonNegativeDec(n.Provizyon, "Provizyon");
        RequireNonNegativeDec(n.Provizyon2, "Provizyon 2");
        RequireNonNegativeDec(n.MuafiyetTutari, "Muafiyet tutarı");
        RequireNonNegativeDec(n.Muafiyet2, "Muafiyet 2");
        RequireNonNegativeDec(n.AsimKmUcreti, "Aşım KM ücreti");
        RequireNonNegativeDec(n.YakitFiyati, "Yakıt fiyatı");
        RequireNonNegativeDec(n.GencSurucuUcretGunluk, "Genç sürücü ücreti (net/gün)");
        RequireNonNegativeDec(n.EkSurucuUcretGunluk, "Ek sürücü ücreti (net/gün)");
        if (n.SurucuMinYas is < 16 or > 99)
            throw new ValidationException("Sürücü min. yaş 16 ile 99 arasında olmalıdır.");
        if (n.GencSurucuYas is < 16 or > 99)
            throw new ValidationException("Genç sürücü yaşı 16 ile 99 arasında olmalıdır.");
        if (n.EhliyetMinYil is < 0 or > 80)
            throw new ValidationException("Ehliyet min. yıl 0 ile 80 arasında olmalıdır.");
        if (n.GencEhliyetMinYil is < 0 or > 80)
            throw new ValidationException("Genç ehliyet min. yıl 0 ile 80 arasında olmalıdır.");
        if (n.SonraOdeOran is < 0m or > 100m)
            throw new ValidationException("Sonra öde oranı 0 ile 100 arasında olmalıdır (%).");
    }

    private static void RequireNonNegativeInt(int? v, string label)
    {
        if (v is < 0) throw new ValidationException($"{label} negatif olamaz.");
    }

    private static void RequireNonNegativeDec(decimal? v, string label)
    {
        if (v is < 0m) throw new ValidationException($"{label} negatif olamaz.");
    }

    private static VehicleGroupInput Normalize(VehicleGroupInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Sipp = string.IsNullOrWhiteSpace(input.Sipp) ? null : input.Sipp.Trim().ToUpperInvariant(),
        Segment = TrimOrNull(input.Segment),
        KasaTuru = TrimOrNull(input.KasaTuru),
        Marka = TrimOrNull(input.Marka),
        Tipi = TrimOrNull(input.Tipi),
        KoltukSayisi = input.KoltukSayisi,
        KapiSayisi = input.KapiSayisi,
        BagajSayisi = input.BagajSayisi,
        KucukBagaj = input.KucukBagaj,
        BuyukBagaj = input.BuyukBagaj,
        SurucuMinYas = input.SurucuMinYas,
        GencSurucuYas = input.GencSurucuYas,
        GencSurucuUcretGunluk = input.GencSurucuUcretGunluk,
        EkSurucuUcretGunluk = input.EkSurucuUcretGunluk,
        EhliyetMinYil = input.EhliyetMinYil,
        GencEhliyetMinYil = input.GencEhliyetMinYil,
        Provizyon = input.Provizyon,
        Provizyon2 = input.Provizyon2,
        MuafiyetTutari = input.MuafiyetTutari,
        Muafiyet2 = input.Muafiyet2,
        GunlukKmLimiti = input.GunlukKmLimiti,
        AylikMaxKm = input.AylikMaxKm,
        AsimKmUcreti = input.AsimKmUcreti,
        YakitFiyati = input.YakitFiyati,
        SonraOdeOran = input.SonraOdeOran,
        KrediKartiSart = input.KrediKartiSart,
        WebSira = input.WebSira,
        UpgradeSira = input.UpgradeSira,
        // FAZ-20 — Normalize YENİ nesne kurar: buraya eklenmeyen alan sessizce kaybolur.
        ProvizyonDoviz = NormalizeCurrency(input.ProvizyonDoviz),
        Provizyon2Doviz = NormalizeCurrency(input.Provizyon2Doviz),
        YakitTuru = input.YakitTuru,
        Vites = input.Vites,
        EntegrasyonKod1 = TrimOrNull(input.EntegrasyonKod1),
        WebId = TrimOrNull(input.WebId),
        ServisId = TrimOrNull(input.ServisId),
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Döviz kodu: trim + büyük harf (TRY/EUR/USD ile aynı sözlük).</summary>
    private static string? NormalizeCurrency(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

    private static void Apply(VehicleGroup group, VehicleGroupInput n)
    {
        group.Kod = n.Kod;
        group.Ad = n.Ad;
        group.Aciklama = n.Aciklama;
        group.Sipp = n.Sipp;
        group.Segment = n.Segment;
        group.KasaTuru = n.KasaTuru;
        group.Marka = n.Marka;
        group.Tipi = n.Tipi;
        group.KoltukSayisi = n.KoltukSayisi;
        group.KapiSayisi = n.KapiSayisi;
        group.BagajSayisi = n.BagajSayisi;
        group.KucukBagaj = n.KucukBagaj;
        group.BuyukBagaj = n.BuyukBagaj;
        group.SurucuMinYas = n.SurucuMinYas;
        group.GencSurucuYas = n.GencSurucuYas;
        group.GencSurucuUcretGunluk = n.GencSurucuUcretGunluk;
        group.EkSurucuUcretGunluk = n.EkSurucuUcretGunluk;
        group.EhliyetMinYil = n.EhliyetMinYil;
        group.GencEhliyetMinYil = n.GencEhliyetMinYil;
        group.Provizyon = n.Provizyon;
        group.Provizyon2 = n.Provizyon2;
        group.MuafiyetTutari = n.MuafiyetTutari;
        group.Muafiyet2 = n.Muafiyet2;
        group.GunlukKmLimiti = n.GunlukKmLimiti;
        group.AylikMaxKm = n.AylikMaxKm;
        group.AsimKmUcreti = n.AsimKmUcreti;
        group.YakitFiyati = n.YakitFiyati;
        group.SonraOdeOran = n.SonraOdeOran;
        group.KrediKartiSart = n.KrediKartiSart;
        group.WebSira = n.WebSira;
        group.UpgradeSira = n.UpgradeSira;
        group.ProvizyonDoviz = n.ProvizyonDoviz;
        group.Provizyon2Doviz = n.Provizyon2Doviz;
        group.YakitTuru = n.YakitTuru;
        group.Vites = n.Vites;
        group.EntegrasyonKod1 = n.EntegrasyonKod1;
        group.WebId = n.WebId;
        group.ServisId = n.ServisId;
        group.Aktif = n.Aktif;
    }
}
