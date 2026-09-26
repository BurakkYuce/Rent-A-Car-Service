using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracSiparisleri;

/// <summary>
/// Araç sipariş/tedarik iş mantığı (roadmap L3): sipariş oluştur/güncelle/listele + durum geçişi
/// (onayla/teslim al/iptal). DEFTER POSTLAMAZ (teslim/satınalma faturalama ayrı) → salt sipariş
/// takibi; yazma OperationsWrite.
///
/// <para><b>PARA ÇİTİ (FAZ-17):</b> eklenen üç fiyat katmanı (Piyasa/Ops/Filo) <b>SALT BİLGİ</b>dir.
/// Resmi birim tutar <c>BirimFiyat</c> olarak KALDI; sipariş toplamı hâlâ Adet × BirimFiyat'tır ve
/// bu servis hiçbir <c>AccountLedgerEntry</c> yazmaz, hiçbir cari bakiyesine dokunmaz (KARARLAR.md
/// genel politikası + FAZ-17 kararı). Kırılgan regresyon testi
/// <c>AracSiparisTests.Fiyat_katmanlari_ve_cari_bagi_deftere_yazmaz</c> bunu kalıcı kilitler.</para>
/// </summary>
public sealed class VehicleOrderService(IVehicleOrderRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleOrderRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-17 — filtreli liste (Cari/Ad-Soyad/Araç/Dosya/Durum/Tarih aralığı). Salt-okur;
    /// ekranın açık olduğu rollerin hepsi görebilsin diye izin OR'lanır (Operatör'de yalnız
    /// OperationsWrite, Muhasebe'de yalnız FinanceWrite/ViewReports var).</summary>
    public Task<IReadOnlyList<AracSiparis>> SearchAsync(AracSiparisFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser,
            Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        return _repository.SearchAsync(filter ?? new AracSiparisFilter(), ct);
    }

    public Task<AracSiparis?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AracSiparisInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);

        var row = new AracSiparis
        {
            Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? ia : Guid.NewGuid(), // F6.1b idempotent oluşturma
            SiparisTarihi = input.SiparisTarihi ?? DateTimeOffset.UtcNow,
            Durum = OrderStatus.Bekliyor
        };
        Apply(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>
    /// FAZ-17 — sipariş alanlarını güncelle. <b>Durum BURADAN değişmez</b> (Onayla/Teslim Al/İptal
    /// ayrı akış); <c>No</c> da değişmez (boşluksuz sıra). İptal edilmiş sipariş düzenlenemez —
    /// kapanmış bir dosyanın alanlarını sessizce değiştirmek geçmişi yeniden yazmak olurdu.
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, AracSiparisInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);

        var existing = await _repository.FindAsync(id, ct);
        if (existing is null) return false;
        if (existing.Durum == OrderStatus.Iptal)
            throw new ValidationException("İptal edilmiş sipariş düzenlenemez.");

        return await _repository.UpdateAsync(id, row =>
        {
            // SiparisTarihi boş gelirse MEVCUT değer korunur (create'teki "şimdi" varsayılanı
            // güncellemede uygulanamaz: form tarihi boş bırakıldığında kayıt tarihi bugüne kayardı).
            row.SiparisTarihi = input.SiparisTarihi ?? row.SiparisTarihi;
            Apply(row, input);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    // F6.1b adversarial M1: Blazor da aynı kilitli geçiş tablosundan geçer (bilinçli sıkılaştırma — önce kilitsiz
    // okuma ile yalnız İptal→X kapalıydı; TeslimAlındı→Onaylandı/İptal geri dönüşü açıktı).
    public Task<bool> ApproveAsync(Guid id, CancellationToken ct = default) => ChangeStatusAsync(id, OrderStatus.Onaylandi, ct);
    public Task<bool> ReceiveAsync(Guid id, CancellationToken ct = default) => ChangeStatusAsync(id, OrderStatus.TeslimAlindi, ct);
    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default) => ChangeStatusAsync(id, OrderStatus.Iptal, ct);

    /// <summary>
    /// F6.1b adversarial M1 — izinli durum geçişleri (TEK kaynak; uç yetki bayrakları da buradan türer):
    /// Onaylandı yalnız Bekliyor'dan; TeslimAlındı yalnız Bekliyor|Onaylandı'dan; İptal TeslimAlındı ve İptal
    /// DIŞINDAN. İptal ve TeslimAlındı terminal (teslim alınmış sipariş geri alınamaz / iptal edilemez).
    /// Aynı duruma "geçiş" bu tabloda yoktur — çağıran onu no-op sayar.
    /// </summary>
    public static bool IsTransitionAllowed(OrderStatus from, OrderStatus to) => to switch
    {
        OrderStatus.Onaylandi => from == OrderStatus.Bekliyor,
        OrderStatus.TeslimAlindi => from is OrderStatus.Bekliyor or OrderStatus.Onaylandi,
        OrderStatus.Iptal => from is not (OrderStatus.TeslimAlindi or OrderStatus.Iptal),
        _ => false,
    };

    /// <summary>F6.1b — kayıt sürümü (xmin; tam değiştirme PUT'unun iyimser eşzamanlılığı).</summary>
    public Task<string?> VersionAsync(Guid id, CancellationToken ct = default) => _repository.VersionAsync(id, ct);

    /// <summary>
    /// F6.1b — <see cref="UpdateAsync"/>'in sürümlü ve KİLİTLİ karşılığı (/api/ui PUT). İptal çiti ve sürüm
    /// karşılaştırması satır kilidinin ALTINDA: eşzamanlı iptal ile düzenleme yarışı iptal kaydı değiştiremez; bayat
    /// sürüm → 409 <c>cakisma</c>. Durum ve No değişmez.
    /// </summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, AracSiparisInput input, string version, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        return await _repository.UpdateLockedAsync(id, version, row =>
        {
            if (row.Durum == OrderStatus.Iptal)
                throw new ValidationException("İptal edilmiş sipariş düzenlenemez.");
            row.SiparisTarihi = input.SiparisTarihi ?? row.SiparisTarihi;
            Apply(row, input);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// F6.1b — durum geçişi KİLİT ALTINDA, <see cref="IsTransitionAllowed"/> tablosuyla. Aynı duruma ikinci geçiş
    /// yapısal no-op (çift tık zararsız); tabloda olmayan geçiş 409 <c>cakisma</c> (bayat sekme — güncel kayıt
    /// yeniden yüklenmeli).
    /// </summary>
    public async Task<bool> ChangeStatusAsync(Guid id, OrderStatus status, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.UpdateLockedAsync(id, null, row =>
        {
            if (row.Durum == status) return;
            if (!IsTransitionAllowed(row.Durum, status))
                throw new ConcurrentModificationException(row.Durum == OrderStatus.Iptal
                    ? "İptal edilmiş sipariş onaylanamaz ya da teslim alınamaz."
                    : $"Sipariş '{row.Durum}' durumunda; '{status}' durumuna geçilemez. Güncel kaydı kontrol edin.");
            row.Durum = status;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Ortak doğrulama — create ve update AYNI kuralları uygular (biri gevşek kalırsa
    /// güncelleme yoluyla geçersiz kayıt üretilirdi).</summary>
    private static void Validate(AracSiparisInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Tedarikci)) throw new ValidationException("Tedarikçi zorunludur.");
        if (input.Adet is < 1 or > 10000) throw new ValidationException("Adet 1 ile 10000 arasında olmalıdır.");
        if (input.BirimFiyat < 0m) throw new ValidationException("Birim fiyat negatif olamaz.");
        if (input.Kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        // Fiyat katmanları bilgi alanı olsa da NEGATİF değer işaret hatasıdır → red (bilgi alanı
        // "her şey serbest" demek değil).
        if (input.PiyasaFiyat is < 0m) throw new ValidationException("Piyasa fiyatı negatif olamaz.");
        if (input.OpsFiyat is < 0m) throw new ValidationException("Ops fiyatı negatif olamaz.");
        if (input.FiloFiyat is < 0m) throw new ValidationException("Filo fiyatı negatif olamaz.");

        // Uzunluk çitleri: DB kolon sınırının aşılması Postgres 22001 → 500 verirdi; kullanıcı
        // anlaşılır mesaj görmeli.
        Length(input.DosyaNo, 64, "Dosya no");
        Length(input.SatisTemsilci, 128, "Satış temsilcisi");
        Length(input.OzelTemsilci, 128, "Özel temsilci");
        Length(input.Versiyon, 100, "Versiyon");
        Length(input.Opsiyon, 512, "Opsiyon");
        Length(input.Renk, 64, "Renk");
        Length(input.IcRenk, 64, "İç renk");
        Length(input.KaynakTip, 32, "Kaynak tipi");
        Length(input.SatisTipi, 32, "Satış tipi");
        Length(input.TsbKayitNo, 64, "TSB kayıt no");
        Length(input.Marka, 100, "Marka");
        Length(input.Tip, 100, "Tip");
        Length(input.Grup, 100, "Grup");
        Length(input.Aciklama, 512, "Açıklama");
        Length(input.Tedarikci, 200, "Tedarikçi");
    }

    private static void Length(string? value, int maximum, string name)
    {
        if (value is not null && value.Trim().Length > maximum)
            throw new ValidationException($"{name} en çok {maximum} karakter olabilir.");
    }

    /// <summary>Girişten entity'ye tek eşleme noktası — create/update sapmasın (bir alan yalnız
    /// birinde eşlenirse o form kaydettiğinde alan sessizce sıfırlanırdı).</summary>
    private static void Apply(AracSiparis row, AracSiparisInput input)
    {
        row.Tedarikci = input.Tedarikci!.Trim();
        row.TedarikciCariId = input.TedarikciCariId;
        row.BeklenenTeslim = input.BeklenenTeslim;
        row.ImzaTarih = input.ImzaTarih;
        row.DosyaNo = Trim(input.DosyaNo);
        row.SatisTemsilci = Trim(input.SatisTemsilci);
        row.OzelTemsilci = Trim(input.OzelTemsilci);
        row.Marka = Trim(input.Marka);
        row.Tip = Trim(input.Tip);
        row.Grup = Trim(input.Grup);
        row.Versiyon = Trim(input.Versiyon);
        row.Opsiyon = Trim(input.Opsiyon);
        row.Renk = Trim(input.Renk);
        row.IcRenk = Trim(input.IcRenk);
        row.KaynakTip = Trim(input.KaynakTip);
        row.SatisTipi = Trim(input.SatisTipi);
        row.TsbKayitNo = Trim(input.TsbKayitNo);
        row.KrediId = input.KrediId;
        row.Adet = input.Adet;
        row.BirimFiyat = input.BirimFiyat;   // RESMİ tutar — katmanlar bunun yerine GEÇMEZ
        row.PiyasaFiyat = input.PiyasaFiyat;
        row.OpsFiyat = input.OpsFiyat;
        row.FiloFiyat = input.FiloFiyat;
        row.Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant();
        row.Kur = input.Kur;
        row.Aciklama = Trim(input.Aciklama);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
