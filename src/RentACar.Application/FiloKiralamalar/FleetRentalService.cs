using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FiloKiralamalar;

/// <summary>
/// Filo (uzun-dönem) kiralama iş mantığı (roadmap L1): sözleşme oluştur/listele + taksit planı hesapla.
/// DEFTER POSTLAMAZ (gelir aylık faturalama ile tanınır) → salt sözleşme/hesap; yazma OperationsWrite.
/// </summary>
public sealed class FleetRentalService(
    IFleetRentalRepository repository, ICurrentUser currentUser, ICustomerRepository customers, IVehicleRepository vehicles)
{
    private readonly IFleetRentalRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Sözleşmeler; <paramref name="filter"/> null → tüm kayıtlar (FAZ-21 öncesi davranış).</summary>
    public Task<IReadOnlyList<FiloKiralama>> ListAsync(
        FiloKiralamaFilter? filter = null, CancellationToken ct = default)
        => _repository.ListAsync(filter, ct);

    public Task<FiloKiralama?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    // ------------------------------------------------------------ F5.1 adversarial M3 — şube kapsamı
    // Sözleşmenin kendi şube alanı yok; kapsam ARACIN şubesinden (Vehicle.SubeId/Sube) türetilir — BranchScope TEK
    // kuralı (FK iki tarafta doluysa FK, aksi metin). /api/ui yüzeyi bu yolları kullanır; Blazor yolları değişmedi.

    /// <summary>Liste, çağıranın şube kapsamıyla süzülür (operatör yalnız kendi şubesinin araçlarının sözleşmeleri).</summary>
    public Task<IReadOnlyList<FiloKiralama>> ListScopedAsync(FiloKiralamaFilter filter, CancellationToken ct = default)
    {
        filter.Kapsam = BranchScope.EffectiveFilter(_currentUser);
        return _repository.ListAsync(filter, ct);
    }

    /// <summary>Tekil kayıt: bulunamazsa null; başka şubenin aracına aitse 403 <c>yetki_yok</c> — durum/içerikten ÖNCE.</summary>
    public async Task<FiloKiralama?> GetComprehensiveAsync(Guid id, CancellationToken ct = default)
    {
        var k = await _repository.FindAsync(id, ct);
        if (k is not null) await VehicleScopeAsync(k.VehicleId, ct);
        return k;
    }

    /// <summary>Araç şube kapsamı guard'ı. Araç bulunamazsa (silinmiş) kapsamlı kullanıcı için red (şubesi bilinemez).</summary>
    public async Task VehicleScopeAsync(Guid vehicleId, CancellationToken ct = default)
    {
        var branch = await _repository.VehicleBranchAsync(vehicleId, ct);
        BranchScope.RequireInScope(_currentUser, branch?.SubeId, branch?.Sube);
    }

    /// <summary>Oluşturma, araç kapsamı GİRİŞ NOKTASINDA denetlenerek (başka şubenin aracına sözleşme açılamaz).</summary>
    public async Task<Guid> CreateComprehensiveAsync(FiloKiralamaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.VehicleId != Guid.Empty) await VehicleScopeAsync(input.VehicleId, ct);
        return await CreateAsync(input, ct);
    }

    public async Task<Guid> CreateAsync(FiloKiralamaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.MusteriId == Guid.Empty) throw new ValidationException("Müşteri seçilmelidir.");
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.SureAy is < 1 or > 120) throw new ValidationException("Süre (ay) 1 ile 120 arasında olmalıdır.");
        if (input.AylikUcret <= 0m) throw new ValidationException("Aylık ücret pozitif olmalıdır.");
        if (input.KdvOrani is < 0m or > 1m) throw new ValidationException("KDV oranı 0-1 arası kesir olmalıdır.");
        if (input.Kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        if (input.DamgaVergisi is < 0m) throw new ValidationException("Damga vergisi negatif olamaz.");
        // #332 review L2: Blazor /filo-kiralama formu da müşteri/araç varlık kontrolünden geçer (/api/ui ucu zaten geçiyordu).
        await BookingPartyCheck.RequireAsync(customers, vehicles, input.MusteriId, input.VehicleId, ct);
        // F5.1 adversarial M2: tarih sınırları (taksit planı AddMonths taşması → tüm liste 500).
        var startDate = input.BasTar ?? DateTimeOffset.UtcNow;
        DatePolicy.FleetStart(startDate);
        DatePolicy.DocumentDate(input.SozlesmeTarihi, "sozlesmeTarihi", "Sözleşme tarihi");
        DatePolicy.DocumentDate(input.ImzaTarih, "imzaTarih", "İmza tarihi");

        var row = new FiloKiralama
        {
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = startDate,
            SureAy = input.SureAy,
            AylikUcret = input.AylikUcret,
            KdvOrani = input.KdvOrani,
            Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
            Kur = input.Kur,
            ToplamKmLimiti = input.ToplamKmLimiti,
            DamgaVergisi = input.DamgaVergisi,
            Durum = FleetRentalStatus.Aktif,
            Aciklama = Text(input.Aciklama),
            // FAZ-21 künye alanları — taksit planına GİRMEZ.
            SatisTemsilcisi = Text(input.SatisTemsilcisi),
            FaturaTuru = Text(input.FaturaTuru),
            SozlesmeTarihi = input.SozlesmeTarihi,
            ImzaTarih = input.ImzaTarih,
            MakbuzNo = Text(input.MakbuzNo),
            DosyaNo = Text(input.DosyaNo),
            SozlesmeNo = Text(input.SozlesmeNo),
            VadeGun = input.VadeGun,
            FiyatTuru = Text(input.FiyatTuru),
            Kaynak = Text(input.Kaynak),
            CikisKm = input.CikisKm,
            ToplamKm = input.ToplamKm
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>
    /// Sözleşme KÜNYESİNİ günceller. Para/süre alanları <see cref="FiloKiralamaMetaInput"/> tipinde
    /// BULUNMAZ → taksit planı bu yoldan sessizce değiştirilemez (RentalUpdateInput deseni).
    /// İptal edilmiş sözleşme düzenlenemez: kapanmış bir belgenin künyesini değiştirmek geçmişi bozar.
    /// </summary>
    public Task<bool> UpdateMetaAsync(Guid id, FiloKiralamaMetaInput input, CancellationToken ct = default)
        => UpdateMetaAsync(id, input, expectedVersion: null, ct);

    /// <summary>F5.1 — <paramref name="expectedVersion"/> doluysa satır kilidi altında sürüm karşılaştırmalı künye
    /// güncellemesi; kilit ALTINDA "iptal edilmiş sözleşme düzenlenemez" yeniden denetlenir. null → Blazor yolu.</summary>
    public async Task<bool> UpdateMetaAsync(Guid id, FiloKiralamaMetaInput input, string? expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.VadeGun is < 0) throw new ValidationException("Vade günü negatif olamaz.");
        if (input.CikisKm is < 0 || input.ToplamKm is < 0) throw new ValidationException("Kilometre negatif olamaz.");
        if (input.ToplamKmLimiti is < 0) throw new ValidationException("KM limiti negatif olamaz.");
        if (input.CikisKm is { } c && input.ToplamKm is { } t && t < c)
            throw new ValidationException("Toplam KM, çıkış KM'sinden küçük olamaz.");
        var existing = await _repository.FindAsync(id, ct)
            ?? throw new ValidationException("Sözleşme bulunamadı.");
        if (existing.Durum == FleetRentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş sözleşme düzenlenemez.");

        // F5.2b (#271 adversarial Low-1): belge tarihi sınırı YALNIZ tarih DEĞİŞİYORSA uygulanır. Sınır dışı
        // (2000 öncesi / +1 yıl sonrası) tarihli ESKİ sözleşme yalnız açıklaması değişse bile düzenlenemiyordu
        // (rezervasyon tarih politikası H5 dersi: kural girişte, dokunulmayan geçmiş değeri kilitlemez).
        if (IsDateChanged(existing.SozlesmeTarihi, input.SozlesmeTarihi))
            DatePolicy.DocumentDate(input.SozlesmeTarihi, "sozlesmeTarihi", "Sözleşme tarihi");
        if (IsDateChanged(existing.ImzaTarih, input.ImzaTarih))
            DatePolicy.DocumentDate(input.ImzaTarih, "imzaTarih", "İmza tarihi");

        void Apply(FiloKiralama row)
        {
            if (expectedVersion is not null && row.Durum == FleetRentalStatus.Iptal) // kilit altında (yarış penceresi)
                throw new ValidationException("İptal edilmiş sözleşme düzenlenemez.");
            row.SatisTemsilcisi = Text(input.SatisTemsilcisi);
            row.FaturaTuru = Text(input.FaturaTuru);
            row.SozlesmeTarihi = input.SozlesmeTarihi;
            row.ImzaTarih = input.ImzaTarih;
            row.MakbuzNo = Text(input.MakbuzNo);
            row.DosyaNo = Text(input.DosyaNo);
            row.SozlesmeNo = Text(input.SozlesmeNo);
            row.VadeGun = input.VadeGun;
            row.FiyatTuru = Text(input.FiyatTuru);
            row.Kaynak = Text(input.Kaynak);
            row.CikisKm = input.CikisKm;
            row.ToplamKm = input.ToplamKm;
            row.ToplamKmLimiti = input.ToplamKmLimiti;
            row.Aciklama = Text(input.Aciklama);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        return expectedVersion is null
            ? await _repository.UpdateAsync(id, Apply, ct)
            : await _repository.UpdateAsync(id, expectedVersion, Apply, ct);
    }

    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Belge tarihi değişiyor mu? Aynı an ya da aynı takvim günü (UTC ya da Türkiye +03) = değişmedi: Blazor künye
    /// formu tarihi GÜN olarak geri gönderir (saat düşer, sunucu yerel gece yarısına çözülür), bu yüzden gün eşitliği
    /// "dokunulmadı" sayılır. Sınır kuralı gün bazlıdır; aynı gün içinde saat oynaması kararı değiştirmez.
    /// </summary>
    internal static bool IsDateChanged(DateTimeOffset? existing, DateTimeOffset? newItem)
        => (mevcut: existing, yeni: newItem) switch
        {
            (null, null) => false,
            ({ } m, { } y) => m != y
                && m.UtcDateTime.Date != y.UtcDateTime.Date
                && m.ToOffset(TurkeyOffset).Date != y.ToOffset(TurkeyOffset).Date,
            _ => true,
        };

    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // inceltme
        return _repository.SetStatusAsync(id, FleetRentalStatus.Iptal, ct);
    }

    public async Task<bool> CompleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // İptal TERMİNALDİR: ekranın iptal onayı "iptal geri alınamaz" diyor; eski listeden "Tamamla"ya
        // basan kullanıcı İptal → Tamamlandı geçişi yapabiliyordu (adversarial bulgu). UpdateMetaAsync'in
        // "İptal edilmiş sözleşme düzenlenemez" kuralıyla aynı.
        if ((await _repository.FindAsync(id, ct))?.Durum == FleetRentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş sözleşme tamamlanamaz.");
        return await _repository.SetStatusAsync(id, FleetRentalStatus.Tamamlandi, ct);
    }

    /// <summary>Taksit planı + mali özet (salt-hesap). Her ay: net = aylık ücret, kdv = net × oran;
    /// vade = BasTar + n ay. ToplamNet/Kdv = taksit toplamları (yuvarlama tutarlı).</summary>
    public static FiloKiraOzet InstallmentPlan(FiloKiralama k)
    {
        var installments = new List<FiloKiraTaksit>(k.SureAy);
        for (var i = 0; i < k.SureAy; i++)
        {
            var net = Round(k.AylikUcret);
            var vat = Round(k.AylikUcret * k.KdvOrani);
            installments.Add(new FiloKiraTaksit(i + 1, Due(k.BasTar, i), net, vat, net + vat));
        }
        var totalNet = installments.Sum(t => t.Net);
        var totalVat = installments.Sum(t => t.Kdv);
        var stamp = k.DamgaVergisi ?? 0m;
        return new FiloKiraOzet(totalNet, totalVat, stamp, totalNet + totalVat + stamp, installments);
    }

    /// <summary>F5.1 adversarial M2 — savunmacı vade: sınırdan önce yazılmış bozuk bir satır (ör. BasTar 9999) takvim
    /// taşmasında tüm listeyi 500'e düşürmesin; taşan vade takvimin son anına kırpılır (tutarlar etkilenmez).</summary>
    private static DateTimeOffset Due(DateTimeOffset start, int month)
        => start <= DateTimeOffset.MaxValue.AddMonths(-month) ? start.AddMonths(month) : DateTimeOffset.MaxValue;

    private static decimal Round(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);
}
