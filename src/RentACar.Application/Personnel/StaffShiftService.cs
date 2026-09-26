using RentACar.Application.Authorization;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

/// <summary>
/// Vardiya iş mantığı (FAZ-45).
///
/// <para><b>Yetki:</b> yazma <see cref="Permission.OperationsWrite"/>; OKUMA
/// <c>ViewReports VEYA OperationsWrite</c>. Operatör'de ViewReports YOK (bkz. RolePermissions) —
/// yalnız ViewReports isteseydik vardiyayı GİREN rol girdiğini GÖREMEZDİ; ekran kullanılamazdı.
/// Vardiya PII taşımadığından (personel adı zaten operasyon dropdown'larında görünür) personel
/// master'ın ManageUsers kapısı burada gerekmez.</para>
///
/// <para><b>Şube kapsamı VARDİYANIN şubesine göre</b> — personelin kadro şubesine göre değil
/// (bkz. <see cref="PersonelVardiya"/>). YAZMADA ek olarak personelin kadro şubesi de kapsam içinde olmalı
/// (2026-09-25: kapsam dışı personel, çakışma kontrolü üzerinden başka şubenin saatlerini sızdırıyordu).</para>
///
/// <para><b>Çakışma reddi:</b> aynı personel aynı anda iki yerde olamaz. Bölünmüş mesai
/// (08-12 + 13-18) serbesttir; kesişen aralık reddedilir. Kontrol MUTLAK aralık üzerinden yapılır
/// ki gece vardiyası (22:00–06:00) ertesi günün sabah vardiyasıyla çakışması yakalansın —
/// yalnız gün-içi saat karşılaştırsaydık bu çakışma görünmezdi.</para>
/// </summary>
public sealed class StaffShiftService(
    IPersonnelShiftRepository repository, IPersonnelRepository staffMembers,
    IBranchRepository branches, ICurrentUser currentUser, IRowVersionStore? rowVersions = null)
{
    private readonly IPersonnelShiftRepository _repository = repository;
    private readonly IPersonnelRepository _personnel = staffMembers;
    private readonly IBranchRepository _branches = branches;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Matris genişlik tavanı — 366 günlük bir istek personel×gün ızgarasını
    /// tarayıcıda kullanılmaz hâle getirir (ve boşuna sorgu üretir).</summary>
    public const int MaxDays = 92;

    /// <summary>Filtre boşsa gösterilen varsayılan pencere (bugünden itibaren 1 hafta).</summary>
    public const int DefaultDay = 7;

    // ---- Okuma ----

    public async Task<VardiyaMatris> MatrixAsync(VardiyaFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        filter ??= new VardiyaFilter();
        var (start, bit) = Window(filter);

        var rows = await _repository.SearchAsync(
            start, bit, filter, BranchScope.EffectiveFilter(_currentUser), ct);

        // Sütunlar aralığın TAMAMI — veriden türetilseydi vardiyasız gün kaybolurdu.
        var gunler = new List<DateOnly>();
        for (var g = start; g <= bit; g = g.AddDays(1)) gunler.Add(g);

        var matrix = rows
            .GroupBy(x => (x.Vardiya.PersonelId, x.PersonelAd))
            .OrderBy(g => g.Key.PersonelAd, StringComparer.CurrentCulture)
            .Select(g => new VardiyaMatrisSatir(
                g.Key.PersonelId,
                g.Key.PersonelAd,
                g.GroupBy(x => x.Vardiya.Tarih).ToDictionary(
                    k => k.Key,
                    k => (IReadOnlyList<PersonelVardiya>)k.Select(x => x.Vardiya)
                        .OrderBy(v => v.BaslangicSaat).ToList()),
                g.Sum(x => x.Vardiya.SureDk)))
            .ToList();

        return new VardiyaMatris(gunler, matrix, rows.Count, rows.Sum(x => x.Vardiya.SureDk));
    }

    /// <summary>Düz liste (matrisle AYNI kapsam/filtre) — düzenleme tablosu ve export için.</summary>
    public async Task<IReadOnlyList<VardiyaSatir>> ListAsync(VardiyaFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        filter ??= new VardiyaFilter();
        var (start, bit) = Window(filter);
        return await _repository.SearchAsync(start, bit, filter, BranchScope.EffectiveFilter(_currentUser), ct);
    }

    public async Task<PersonelVardiya?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        var row = await _repository.FindAsync(id, ct);
        if (row is null) return null;
        BranchScope.RequireInScope(_currentUser, row.SubeId, row.Sube);
        return row;
    }

    /// <summary>İstenen pencere; boşsa varsayılan, ters verilirse düzeltilir, geniş verilirse kırpılır.</summary>
    public static (DateOnly Bas, DateOnly Bit) Window(VardiyaFilter f)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var start = f.Bas ?? f.Bit?.AddDays(-(DefaultDay - 1)) ?? today;
        var bit = f.Bit ?? start.AddDays(DefaultDay - 1);
        if (bit < start) (start, bit) = (bit, start);          // ters aralık → düzelt (boş sayfa yerine)
        if (bit.DayNumber - start.DayNumber + 1 > MaxDays) bit = start.AddDays(MaxDays - 1);
        return (start, bit);
    }

    // ---- Yazma ----

    public async Task<Guid> CreateAsync(VardiyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var row = new PersonelVardiya();
        await ApplyAsync(row, input, null, ct);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VardiyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var existing = await _repository.FindAsync(id, ct);
        if (existing is null) return false;
        BranchScope.RequireInScope(_currentUser, existing.SubeId, existing.Sube);

        // Doğrulama MEVCUT satırın klonu üzerinde yapılır; kendisi çakışma kontrolünden hariç.
        var copy = new PersonelVardiya { Id = existing.Id };
        await ApplyAsync(copy, input, id, ct);

        return await _repository.UpdateAsync(id, r =>
        {
            r.PersonelId = copy.PersonelId;
            r.Tarih = copy.Tarih;
            r.BaslangicSaat = copy.BaslangicSaat;
            r.BitisSaat = copy.BitisSaat;
            r.Sube = copy.Sube;
            r.SubeId = copy.SubeId;
            r.Aciklama = copy.Aciklama;
        }, ct);
    }

    /// <summary>
    /// F10.3 — one shift with the staff name (form of <c>/api/ui</c>). Same permission and branch scope as
    /// <see cref="GetAsync"/>; the name comes from the staff master (no PII: name only, like the matrix).
    /// </summary>
    public async Task<VardiyaSatir?> GetWithStaffAsync(Guid id, CancellationToken ct = default)
    {
        var row = await GetAsync(id, ct);
        if (row is null) return null;
        var staff = await _personnel.FindAsync(row.PersonelId, ct);
        var name = staff is null ? "—" : $"{staff.Ad} {staff.Soyad}".Trim();
        return new VardiyaSatir(row, name, staff?.Sube);
    }

    /// <summary>
    /// #302 L2 — the shift and its version as ONE consistent pair (TOCTOU fix). The version is read BEFORE the row and
    /// compared again AFTER it; a write in between retries the read. If it keeps changing, the OLDER version is returned
    /// with the row: a later PUT then gets 409 <c>cakisma</c> (safe direction) instead of a fresh version paired with
    /// stale fields, which would let the stale form silently overwrite the concurrent change.
    /// </summary>
    public async Task<(VardiyaSatir Row, string Version)?> GetWithStaffAndVersionAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        var store = RowVersionStoreGuard.Require(rowVersions);
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var before = await store.GetVersionAsync<PersonelVardiya>(id, ct);
            if (before is null) return null;
            if (await GetWithStaffAsync(id, ct) is not { } row) return null;
            var after = await store.GetVersionAsync<PersonelVardiya>(id, ct);
            if (after == before || attempt == maxAttempts) return (row, before);
        }
    }

    /// <summary>F10.3 — opaque row version for the full-replacement PUT of <c>/api/ui</c>.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersionStoreGuard.Require(rowVersions).GetVersionAsync<PersonelVardiya>(id, ct);

    /// <summary>
    /// F10.3 — <see cref="UpdateAsync"/> with optimistic concurrency: same validation (staff, day, zero length,
    /// branch, overlap) and same field whitelist, but the write happens under a row lock with a version check
    /// (409 <c>cakisma</c>). The branch scope is checked on the stored row BEFORE validation (another branch's row
    /// must not leak its state) and again under the lock (the row may have moved to another branch meanwhile).
    /// </summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, VardiyaInput input, string expectedVersion,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var current = await _repository.FindAsync(id, ct);
        if (current is null) return false;
        BranchScope.RequireInScope(_currentUser, current.SubeId, current.Sube);

        var copy = new PersonelVardiya { Id = current.Id };
        await ApplyAsync(copy, input, id, ct);

        return await RowVersionStoreGuard.Require(rowVersions).UpdateAsync<PersonelVardiya>(id, expectedVersion, r =>
        {
            BranchScope.RequireInScope(_currentUser, r.SubeId, r.Sube);
            r.PersonelId = copy.PersonelId;
            r.Tarih = copy.Tarih;
            r.BaslangicSaat = copy.BaslangicSaat;
            r.BitisSaat = copy.BitisSaat;
            r.Sube = copy.Sube;
            r.SubeId = copy.SubeId;
            r.Aciklama = copy.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, "Vardiya zaten var.", ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var existing = await _repository.FindAsync(id, ct);
        if (existing is null) return false;
        BranchScope.RequireInScope(_currentUser, existing.SubeId, existing.Sube);
        return await _repository.DeleteAsync(id, ct);
    }

    /// <summary>Doğrulama + alan yazımı. Yeni alan eklenirse BURAYA da eklenmeli (Normalize dersi).</summary>
    private async Task ApplyAsync(PersonelVardiya row, VardiyaInput input, Guid? excludeId, CancellationToken ct)
    {
        if (input.PersonelId == Guid.Empty) throw new ValidationException("Personel seçilmeli.");
        var staff = await _personnel.FindAsync(input.PersonelId, ct)
            ?? throw new ValidationException("Personel bulunamadı.");
        // 2026-09-25 incelemesi: kapsamlı operatör kapsam DIŞI personele vardiya yazamaz (403). Aksi halde çakışma
        // kontrolü, başka şubedeki personelin vardiya saatleri için bir kehanet olur: mesaj saati gizlese de (#302 L1)
        // deneme-yanılma ile aralık daraltılabilir. Kontrol diğer doğrulamalardan ve çakışma aramasından ÖNCE.
        // Kural personelin KADRO şubesidir (BranchScope.InScope tek kural); şubesiz personel kapsamlı operatör için
        // kapsam dışıdır. Görünürlük (liste/matris) hâlâ VARDİYANIN şubesine göredir.
        BranchScope.RequireInScope(_currentUser, staff.SubeId, staff.Sube);
        if (input.Tarih == default) throw new ValidationException("Tarih zorunlu.");
        if (input.BaslangicSaat == input.BitisSaat)
            throw new ValidationException("Vardiya süresi sıfır olamaz (başlangıç ve bitiş saati aynı).");

        var branch = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim();
        if (branch is not null)
        {
            var defined = await _branches.ListAsync(ct);
            if (!defined.Any(b => TurkishText.EqualsIgnoreTurkishCase(b.Ad, branch)))
                throw new ValidationException($"Şube bulunamadı: {branch}");
        }
        // Operatör başka şubeye vardiya yazamaz — guard GİRİŞ noktasında (tarih politikası dersi).
        BranchScope.RequireInScope(_currentUser, recordBranchId: null, branch);

        await CheckConflictAsync(input, excludeId, staff, ct);

        row.PersonelId = input.PersonelId;
        row.Tarih = input.Tarih;
        row.BaslangicSaat = input.BaslangicSaat;
        row.BitisSaat = input.BitisSaat;
        row.Sube = branch;
        row.Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim();
    }

    private async Task CheckConflictAsync(
        VardiyaInput input, Guid? excludeId, Personel staff, CancellationToken ct)
    {
        var (yStart, yBit) = VardiyaZaman.Range(input.Tarih, input.BaslangicSaat, input.BitisSaat);
        var neighbors = await _repository.ListForOverlapAsync(input.PersonelId, input.Tarih, excludeId, ct);
        var scope = BranchScope.EffectiveFilter(_currentUser);
        foreach (var v in neighbors)
        {
            var (start, bit) = VardiyaZaman.Range(v.Tarih, v.BaslangicSaat, v.BitisSaat);
            if (!(yStart < bit && start < yBit)) continue;      // yarı-açık aralık: 08-12 ile 12-18 ÇAKIŞMAZ
            // #302 L1: çakışan satır kapsam dışı bir şubedeyse tarih/saati sızdırılmaz — yalnız çakışma bilgisi.
            if (!BranchScope.InScope(scope, v.SubeId, v.Sube))
                throw new ValidationException(
                    "Bu personelin bu saatlerde başka bir şubede vardiyası var.", "baslangicSaat");
            throw new ValidationException(
                    $"{staff.Ad} {staff.Soyad} için çakışan vardiya var: " +
                    $"{v.Tarih:dd.MM.yyyy} {ShiftFormat.Range(v)}.",
                    // F10.3: the message starts with the staff name (no fixed prefix to map in the endpoint),
                    // so the field is named here; the Blazor path reads only the message.
                    "baslangicSaat");
        }
    }
}
