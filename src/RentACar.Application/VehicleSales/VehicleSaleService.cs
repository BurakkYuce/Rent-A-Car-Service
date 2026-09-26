using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.VehicleSales;

/// <summary>
/// Araç satışı: net + KDV → brüt; alıcı cari BORÇLANIR ve araç filodan çıkar (Satildi).
///   Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv) — DENGELİ.
/// Maliyet/sabit-kıymet defteri bu sürümde tutulmaz (gelir tam net olarak yazılır), tıpkı
/// kira gelirinde olduğu gibi; gelecekte amortisman/defter-değeri eklenebilir.
/// </summary>
public sealed class VehicleSaleService(
    IVehicleSaleRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.ExchangeRateResolver exchangeRateResolver)
{
    private readonly IVehicleSaleRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.ExchangeRateResolver _exchangeRateResolver = exchangeRateResolver;

    public Task<IReadOnlyList<VehicleSale>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>
    /// FAZ-18 — filtreli liste (canlı arac_satis_ara.aspx). Salt-okur; satış ekranını görebilen
    /// rollerin hepsi arayabilsin diye izin OR'lanır (Muhasebe'de FinanceWrite/ViewReports,
    /// Operatör'de yalnız OperationsWrite vardır — tek izin istemek Operatör'de 500 üretirdi).
    /// </summary>
    public Task<IReadOnlyList<VehicleSale>> SearchAsync(VehicleSaleFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser,
            Permission.FinanceWrite, Permission.ViewReports, Permission.OperationsWrite);
        return _repository.SearchAsync(filter ?? new VehicleSaleFilter(), ct);
    }

    public Task<VehicleSale?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleSaleInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.AliciCariId == Guid.Empty) throw new ValidationException("Alıcı (cari) seçilmelidir.");
        if (input.SatisNet <= 0) throw new ValidationException("Satış tutarı pozitif olmalıdır.");
        if (input.KdvOrani < 0) throw new ValidationException("KDV oranı negatif olamaz.");
        // FAZ-18 bilgi alanı doğrulamaları (kolon sınırına çarpıp 500 üretmesin; anlamsız değer girmesin).
        if (input.IlanKm is < 0) throw new ValidationException("İlan KM negatif olamaz.");
        if (!string.IsNullOrWhiteSpace(input.ListeDoviz) && input.ListeDoviz.Trim().Length != 3)
            throw new ValidationException("Liste fiyatı para birimi 3 harfli olmalıdır (TRY/USD/EUR).");

        // Kur çözümü (1.1): açık kur (>0 guard çözücüde) aynen; boş → TRY=1 / döviz KurService (yoksa net red).
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(input.Doviz, input.Kur, input.Tarih, ct);
        // #286 adversarial M2: net KURUŞA sabitlenir ve kontrol YUVARLAMADAN SONRA yapılır. Önce ham net (333,333)
        // Gelir bacağına, kuruşa yuvarlanmış net + KDV Cari bacağına yazılıyor → küme dengesiz, iç hata mesajı dışarı
        // sızıyordu; 0,004 net ise 0,00 tutarlı satış belgesi üretirdi.
        var net = VatMath.RoundGross(input.SatisNet);
        if (net <= 0) throw new ValidationException("Satış tutarı kuruşa yuvarlandığında pozitif olmalıdır.");
        var (vat, gross) = VatMath.FromNet(net, input.KdvOrani);
        var sale = new VehicleSale
        {
            VehicleId = input.VehicleId,
            AliciCariId = input.AliciCariId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            NoterNo = input.NoterNo,
            SatisNet = net,
            KdvOrani = input.KdvOrani,
            KdvTutar = vat,
            GenelToplam = gross,
            // #279 N1 sınıfı (gider düzeltmesiyle aynı): "TL" ham yazılınca kur TRY=1 çözülürken defter "TL"
            // dövizinde kalıyordu (cari bakiyesi döviz bazında ayrışır). Saklanabilir ISO koda indirgenir.
            Currency = RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(input.Doviz),
            Kur = resolvedRate,
            Aciklama = input.Aciklama,
            HedefFiyat = input.HedefFiyat,
            // FAZ-28 ihale/noter bilgileri
            IhaleTarihi = input.IhaleTarihi,
            IhaleFirmasi = string.IsNullOrWhiteSpace(input.IhaleFirmasi) ? null : input.IhaleFirmasi.Trim(),
            NoterSatisTarihi = input.NoterSatisTarihi,
            SatisKm = input.SatisKm,
            SatisKanali = string.IsNullOrWhiteSpace(input.SatisKanali) ? null : input.SatisKanali.Trim(),
            Devir = string.IsNullOrWhiteSpace(input.Devir) ? null : input.Devir.Trim(),
            // ---- FAZ-18 bilgi alanları ----
            // DİKKAT: hiçbiri BuildEntries'e girmez; defter kümesi yalnız SatisNet/KdvTutar/GenelToplam
            // üzerinden kurulur (KARARLAR.md genel politikası + "P&L yalnız defterden").
            KirayaVerme = input.KirayaVerme,
            IlanKm = input.IlanKm,
            ListeDoviz = string.IsNullOrWhiteSpace(input.ListeDoviz)
                ? null : input.ListeDoviz.Trim().ToUpperInvariant(),
            SatisNoktasi = string.IsNullOrWhiteSpace(input.SatisNoktasi) ? null : input.SatisNoktasi.Trim(),
            UygulananKampanya = string.IsNullOrWhiteSpace(input.UygulananKampanya) ? null : input.UygulananKampanya.Trim(),
            IhaleSayisi = string.IsNullOrWhiteSpace(input.IhaleSayisi) ? null : input.IhaleSayisi.Trim(),
            SatisiVerildi = input.SatisiVerildi,
            YevmiyeNumarasi = string.IsNullOrWhiteSpace(input.YevmiyeNumarasi) ? null : input.YevmiyeNumarasi.Trim(),
            Aciklama2 = string.IsNullOrWhiteSpace(input.Aciklama2) ? null : input.Aciklama2.Trim(),
            Durum = SaleStatus.Tamamlandi
        };
        await _lock.EnsureOpenAsync(sale.Tarih, ct); // dönem kilidi: kapalı tarihe araç satışı postlanamaz

        await _repository.PostAsync(sale, BuildEntries(sale), ct);
        return sale.Id;
    }

    /// <summary>Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv). DENGELİ.</summary>
    private static List<AccountLedgerEntry> BuildEntries(VehicleSale s)
    {
        AccountLedgerEntry Entry(LedgerAccountType type, Guid? reff, LedgerDirection dir, decimal amount) => new()
        {
            EntryDateUtc = s.Tarih, AccountType = type, AccountRef = reff, Direction = dir,
            Amount = new Money(amount, s.Currency, s.Kur),
            SourceType = "AracSatis", SourceId = s.Id, Description = $"Araç satış {s.No}"
        };

        return
        [
            Entry(LedgerAccountType.Cari, s.AliciCariId, LedgerDirection.Debit, s.GenelToplam),
            Entry(LedgerAccountType.Gelir, null, LedgerDirection.Credit, s.SatisNet),
            Entry(LedgerAccountType.Kdv, null, LedgerDirection.Credit, s.KdvTutar)
        ];
    }
}
