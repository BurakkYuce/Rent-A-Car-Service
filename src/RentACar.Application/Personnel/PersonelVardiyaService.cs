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
/// (bkz. <see cref="PersonelVardiya"/>).</para>
///
/// <para><b>Çakışma reddi:</b> aynı personel aynı anda iki yerde olamaz. Bölünmüş mesai
/// (08-12 + 13-18) serbesttir; kesişen aralık reddedilir. Kontrol MUTLAK aralık üzerinden yapılır
/// ki gece vardiyası (22:00–06:00) ertesi günün sabah vardiyasıyla çakışması yakalansın —
/// yalnız gün-içi saat karşılaştırsaydık bu çakışma görünmezdi.</para>
/// </summary>
public sealed class PersonelVardiyaService(
    IPersonelVardiyaRepository repository, IPersonelRepository personeller,
    IBranchRepository subeler, ICurrentUser currentUser)
{
    private readonly IPersonelVardiyaRepository _repository = repository;
    private readonly IPersonelRepository _personeller = personeller;
    private readonly IBranchRepository _subeler = subeler;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Matris genişlik tavanı — 366 günlük bir istek personel×gün ızgarasını
    /// tarayıcıda kullanılmaz hâle getirir (ve boşuna sorgu üretir).</summary>
    public const int MaxGun = 92;

    /// <summary>Filtre boşsa gösterilen varsayılan pencere (bugünden itibaren 1 hafta).</summary>
    public const int VarsayilanGun = 7;

    // ---- Okuma ----

    public async Task<VardiyaMatris> MatrisAsync(VardiyaFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        filtre ??= new VardiyaFilter();
        var (bas, bit) = Pencere(filtre);

        var satirlar = await _repository.SearchAsync(
            bas, bit, filtre, BranchScope.EffectiveFilter(_currentUser), ct);

        // Sütunlar aralığın TAMAMI — veriden türetilseydi vardiyasız gün kaybolurdu.
        var gunler = new List<DateOnly>();
        for (var g = bas; g <= bit; g = g.AddDays(1)) gunler.Add(g);

        var matris = satirlar
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

        return new VardiyaMatris(gunler, matris, satirlar.Count, satirlar.Sum(x => x.Vardiya.SureDk));
    }

    /// <summary>Düz liste (matrisle AYNI kapsam/filtre) — düzenleme tablosu ve export için.</summary>
    public async Task<IReadOnlyList<VardiyaSatir>> ListAsync(VardiyaFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        filtre ??= new VardiyaFilter();
        var (bas, bit) = Pencere(filtre);
        return await _repository.SearchAsync(bas, bit, filtre, BranchScope.EffectiveFilter(_currentUser), ct);
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
    public static (DateOnly Bas, DateOnly Bit) Pencere(VardiyaFilter f)
    {
        var bugun = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var bas = f.Bas ?? f.Bit?.AddDays(-(VarsayilanGun - 1)) ?? bugun;
        var bit = f.Bit ?? bas.AddDays(VarsayilanGun - 1);
        if (bit < bas) (bas, bit) = (bit, bas);          // ters aralık → düzelt (boş sayfa yerine)
        if (bit.DayNumber - bas.DayNumber + 1 > MaxGun) bit = bas.AddDays(MaxGun - 1);
        return (bas, bit);
    }

    // ---- Yazma ----

    public async Task<Guid> CreateAsync(VardiyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var row = new PersonelVardiya();
        await UygulaAsync(row, input, null, ct);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VardiyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var mevcut = await _repository.FindAsync(id, ct);
        if (mevcut is null) return false;
        BranchScope.RequireInScope(_currentUser, mevcut.SubeId, mevcut.Sube);

        // Doğrulama MEVCUT satırın klonu üzerinde yapılır; kendisi çakışma kontrolünden hariç.
        var kopya = new PersonelVardiya { Id = mevcut.Id };
        await UygulaAsync(kopya, input, id, ct);

        return await _repository.UpdateAsync(id, r =>
        {
            r.PersonelId = kopya.PersonelId;
            r.Tarih = kopya.Tarih;
            r.BaslangicSaat = kopya.BaslangicSaat;
            r.BitisSaat = kopya.BitisSaat;
            r.Sube = kopya.Sube;
            r.SubeId = kopya.SubeId;
            r.Aciklama = kopya.Aciklama;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var mevcut = await _repository.FindAsync(id, ct);
        if (mevcut is null) return false;
        BranchScope.RequireInScope(_currentUser, mevcut.SubeId, mevcut.Sube);
        return await _repository.DeleteAsync(id, ct);
    }

    /// <summary>Doğrulama + alan yazımı. Yeni alan eklenirse BURAYA da eklenmeli (Normalize dersi).</summary>
    private async Task UygulaAsync(PersonelVardiya row, VardiyaInput input, Guid? excludeId, CancellationToken ct)
    {
        if (input.PersonelId == Guid.Empty) throw new ValidationException("Personel seçilmeli.");
        var personel = await _personeller.FindAsync(input.PersonelId, ct)
            ?? throw new ValidationException("Personel bulunamadı.");
        if (input.Tarih == default) throw new ValidationException("Tarih zorunlu.");
        if (input.BaslangicSaat == input.BitisSaat)
            throw new ValidationException("Vardiya süresi sıfır olamaz (başlangıç ve bitiş saati aynı).");

        var sube = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim();
        if (sube is not null)
        {
            var tanimli = await _subeler.ListAsync(ct);
            if (!tanimli.Any(b => TurkishText.EqualsIgnoreTurkishCase(b.Ad, sube)))
                throw new ValidationException($"Şube bulunamadı: {sube}");
        }
        // Operatör başka şubeye vardiya yazamaz — guard GİRİŞ noktasında (tarih politikası dersi).
        BranchScope.RequireInScope(_currentUser, kayitSubeId: null, sube);

        await CakismaKontrolAsync(input, excludeId, personel, ct);

        row.PersonelId = input.PersonelId;
        row.Tarih = input.Tarih;
        row.BaslangicSaat = input.BaslangicSaat;
        row.BitisSaat = input.BitisSaat;
        row.Sube = sube;
        row.Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim();
    }

    private async Task CakismaKontrolAsync(
        VardiyaInput input, Guid? excludeId, Personel personel, CancellationToken ct)
    {
        var (yBas, yBit) = VardiyaZaman.Aralik(input.Tarih, input.BaslangicSaat, input.BitisSaat);
        var komsular = await _repository.ListForOverlapAsync(input.PersonelId, input.Tarih, excludeId, ct);
        foreach (var v in komsular)
        {
            var (bas, bit) = VardiyaZaman.Aralik(v.Tarih, v.BaslangicSaat, v.BitisSaat);
            if (yBas < bit && bas < yBit)      // yarı-açık aralık: 08-12 ile 12-18 ÇAKIŞMAZ
                throw new ValidationException(
                    $"{personel.Ad} {personel.Soyad} için çakışan vardiya var: " +
                    $"{v.Tarih:dd.MM.yyyy} {VardiyaBicim.Aralik(v)}.");
        }
    }
}
