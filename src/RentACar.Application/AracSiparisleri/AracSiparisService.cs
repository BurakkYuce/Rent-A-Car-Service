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
public sealed class AracSiparisService(IAracSiparisRepository repository, ICurrentUser currentUser)
{
    private readonly IAracSiparisRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-17 — filtreli liste (Cari/Ad-Soyad/Araç/Dosya/Durum/Tarih aralığı). Salt-okur;
    /// ekranın açık olduğu rollerin hepsi görebilsin diye izin OR'lanır (Operatör'de yalnız
    /// OperationsWrite, Muhasebe'de yalnız FinanceWrite/ViewReports var).</summary>
    public Task<IReadOnlyList<AracSiparis>> SearchAsync(AracSiparisFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser,
            Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        return _repository.SearchAsync(filtre ?? new AracSiparisFilter(), ct);
    }

    public Task<AracSiparis?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AracSiparisInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);

        var row = new AracSiparis
        {
            SiparisTarihi = input.SiparisTarihi ?? DateTimeOffset.UtcNow,
            Durum = SiparisDurum.Bekliyor
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

        var mevcut = await _repository.FindAsync(id, ct);
        if (mevcut is null) return false;
        if (mevcut.Durum == SiparisDurum.Iptal)
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

    public Task<bool> OnaylaAsync(Guid id, CancellationToken ct = default) => SetDurum(id, SiparisDurum.Onaylandi, ct);
    public Task<bool> TeslimAlAsync(Guid id, CancellationToken ct = default) => SetDurum(id, SiparisDurum.TeslimAlindi, ct);
    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default) => SetDurum(id, SiparisDurum.Iptal, ct);

    private async Task<bool> SetDurum(Guid id, SiparisDurum durum, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // İptal TERMİNALDİR: ekranın iptal onayı "bir daha onaylanamaz, teslim alınamaz" diyor; iki
        // sekmede eski listeden "Onayla"/"Teslim Al"a basan kullanıcı İptal → Onaylandı/TeslimAlındı
        // geçişi yapabiliyordu (adversarial bulgu). Düzenleme guard'ıyla (UpdateAsync) aynı kural.
        if (durum != SiparisDurum.Iptal
            && (await _repository.FindAsync(id, ct))?.Durum == SiparisDurum.Iptal)
            throw new ValidationException("İptal edilmiş sipariş onaylanamaz ya da teslim alınamaz.");
        return await _repository.SetDurumAsync(id, durum, ct);
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
        Uzunluk(input.DosyaNo, 64, "Dosya no");
        Uzunluk(input.SatisTemsilci, 128, "Satış temsilcisi");
        Uzunluk(input.OzelTemsilci, 128, "Özel temsilci");
        Uzunluk(input.Versiyon, 100, "Versiyon");
        Uzunluk(input.Opsiyon, 512, "Opsiyon");
        Uzunluk(input.Renk, 64, "Renk");
        Uzunluk(input.IcRenk, 64, "İç renk");
        Uzunluk(input.KaynakTip, 32, "Kaynak tipi");
        Uzunluk(input.SatisTipi, 32, "Satış tipi");
        Uzunluk(input.TsbKayitNo, 64, "TSB kayıt no");
        Uzunluk(input.Marka, 100, "Marka");
        Uzunluk(input.Tip, 100, "Tip");
        Uzunluk(input.Grup, 100, "Grup");
        Uzunluk(input.Aciklama, 512, "Açıklama");
        Uzunluk(input.Tedarikci, 200, "Tedarikçi");
    }

    private static void Uzunluk(string? deger, int enFazla, string ad)
    {
        if (deger is not null && deger.Trim().Length > enFazla)
            throw new ValidationException($"{ad} en çok {enFazla} karakter olabilir.");
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
