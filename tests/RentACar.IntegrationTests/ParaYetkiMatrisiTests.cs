using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Integrations;
using RentACar.Application.Penalties;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Application.ServiceRecords;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Denetim O7 — PARA-YAZAN servis metodlarının Operator (FinanceWrite YOK) reddi: TAM matris.
/// Red SERVİS katmanında tetiklenmeli: PermissionGuard mesajı ("yetkiniz yok") assert edilir —
/// girdi doğrulaması ya da "bulunamadı" hatası guard yerine GEÇMEZ. Guard tüm metodlarda ilk
/// adım olduğundan kurulum verisi gerekmez (rastgele Id'ler yeterli); guard'dan önce başka bir
/// kontrol araya girerse bu testler mesaj assert'iyle KIRILIR (bilinçli).
/// </summary>
[Collection("postgres")]
public sealed class ParaYetkiMatrisiTests(PostgresFixture fx)
{
    /// <summary>Operator scope'unda işlemi çağırır; ValidationException + guard mesajı bekler.</summary>
    private async Task OperatorReddiAsync(Func<IServiceScope, Task> islem)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => islem(scope));
        // Mesaj kanıtı: red YETKİDEN geliyor (PermissionGuard), girdi/veri hatasından değil.
        Assert.Contains("yetkiniz yok", ex.Message);
    }

    private static T Svc<T>(IServiceScope s) where T : notnull
        => s.ServiceProvider.GetRequiredService<T>();

    // ---- Fatura (InvoiceService) ----
    [Fact]
    public Task Operator_kiradan_fatura_kesemez()
        => OperatorReddiAsync(s => Svc<InvoiceService>(s).CreateFromRentalAsync(Guid.NewGuid()));

    [Fact]
    public Task Operator_manuel_fatura_kesemez()
        => OperatorReddiAsync(s => Svc<InvoiceService>(s).CreateManualAsync(
            new ManualInvoiceInput { CariId = Guid.NewGuid(), NetTutar = 100m }));

    [Fact]
    public Task Operator_iade_faturasi_kesemez()
        => OperatorReddiAsync(s => Svc<InvoiceService>(s).CreateIadeAsync(Guid.NewGuid()));

    // ---- Gider (ExpenseService) ----
    [Fact]
    public Task Operator_gider_giremez()
        => OperatorReddiAsync(s => Svc<ExpenseService>(s).CreateAsync(new ExpenseInput { NetTutar = 100m }));

    [Fact]
    public Task Operator_toplu_gider_giremez()
        => OperatorReddiAsync(s => Svc<ExpenseService>(s).BatchCreateAsync([new ExpenseInput { NetTutar = 100m }]));

    // ---- Depozito (DepozitoService: Al / İade / Mahsup) ----
    [Fact]
    public Task Operator_depozito_alamaz()
        => OperatorReddiAsync(s => Svc<DepozitoService>(s).AlAsync(Guid.NewGuid(), 100m, LedgerAccountType.Kasa));

    [Fact]
    public Task Operator_depozito_iade_edemez()
        => OperatorReddiAsync(s => Svc<DepozitoService>(s).IadeAsync(Guid.NewGuid(), 100m, LedgerAccountType.Kasa));

    [Fact]
    public Task Operator_depozito_mahsup_edemez()
        => OperatorReddiAsync(s => Svc<DepozitoService>(s).MahsupAsync(Guid.NewGuid(), 100m));

    // ---- Ceza yansıtma (PenaltyService.YansitAsync) ----
    [Fact]
    public Task Operator_ceza_yansitamaz()
        => OperatorReddiAsync(s => Svc<PenaltyService>(s).YansitAsync(Guid.NewGuid()));

    // ---- Araç satışı (VehicleSaleService.CreateAsync) ----
    [Fact]
    public Task Operator_arac_satamaz()
        => OperatorReddiAsync(s => Svc<VehicleSaleService>(s).CreateAsync(
            new VehicleSaleInput { VehicleId = Guid.NewGuid(), AliciCariId = Guid.NewGuid(), SatisNet = 100m }));

    // ---- Regülasyon ÖDEMELERİ (RegulationService: MTV / muayene / sigorta → defter yazar) ----
    [Fact]
    public Task Operator_mtv_odeyemez()
        => OperatorReddiAsync(s => Svc<RegulationService>(s).MtvOdeAsync(Guid.NewGuid(), LedgerAccountType.Kasa));

    [Fact]
    public Task Operator_muayene_odeyemez()
        => OperatorReddiAsync(s => Svc<RegulationService>(s).MuayeneOdeAsync(Guid.NewGuid(), LedgerAccountType.Kasa));

    [Fact]
    public Task Operator_sigorta_odeyemez()
        => OperatorReddiAsync(s => Svc<RegulationService>(s).SigortaOdeAsync(Guid.NewGuid(), LedgerAccountType.Kasa));

    // ---- Servis rücu yansıtma (ServiceRecordService.YansitAsync) ----
    [Fact]
    public Task Operator_servis_maliyeti_yansitamaz()
        => OperatorReddiAsync(s => Svc<ServiceRecordService>(s).YansitAsync(Guid.NewGuid(), Guid.NewGuid()));

    // ---- Dönem kilidi (DonemKilidiService: Lock / Unlock) ----
    [Fact]
    public Task Operator_donem_kilitleyemez()
        => OperatorReddiAsync(s => Svc<DonemKilidiService>(s).LockAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public Task Operator_donem_kilidini_acamaz()
        => OperatorReddiAsync(s => Svc<DonemKilidiService>(s).UnlockAsync());

    // ---- Kasa/Banka (CashService: tahsilat / ödeme / virman / cari-virman / ters) ----
    [Fact]
    public Task Operator_tahsilat_yapamaz()
        => OperatorReddiAsync(s => Svc<CashService>(s).CollectAsync(
            new CashInput { CariId = Guid.NewGuid(), Tutar = 100m }));

    [Fact]
    public Task Operator_odeme_yapamaz()
        => OperatorReddiAsync(s => Svc<CashService>(s).PayAsync(
            new CashInput { CariId = Guid.NewGuid(), Tutar = 100m }));

    [Fact]
    public Task Operator_virman_yapamaz()
        => OperatorReddiAsync(s => Svc<CashService>(s).TransferAsync(
            LedgerAccountType.Kasa, LedgerAccountType.Banka, 100m));

    [Fact]
    public Task Operator_cari_virman_yapamaz()
        => OperatorReddiAsync(s => Svc<CashService>(s).TransferBetweenCariAsync(
            Guid.NewGuid(), Guid.NewGuid(), 100m));

    [Fact]
    public Task Operator_ters_kayit_atamaz()
        => OperatorReddiAsync(s => Svc<CashService>(s).ReverseAsync(Guid.NewGuid()));

    // ---- Regülasyon KAYITLARI operasyoneldir (tasarım sözleşmesi) ----
    // Sigorta/MTV/muayene KAYDI defter YAZMAZ (para ödemede yazılır) ve web ucu OperationsWrite
    // ister → Operator'a SERBESTTİR. Denetim O7 listesi "create" dese de gerçek para-yazan yol
    // ödemelerdir (yukarıda RED edildi). Bu test mevcut tasarım sözleşmesini SABİTLER: kayıt
    // serbest + defter boş; ödeme yetkili. (VehicleId'ye FK yok — yalnız index — rastgele Id yeter.)
    [Fact]
    public async Task Regulasyon_kaydi_operasyonel_Operator_serbest_defter_bos()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var reg = Svc<RegulationService>(scope);
        var arac = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var pol = await reg.AddInsuranceAsync(arac, InsuranceType.Kasko, t0, t0.AddYears(1), 12000m, "P-1", "Firma", null);
        var mtv = await reg.AddMtvAsync(arac, "2026/1", 3000m, t0.AddMonths(1));
        var mua = await reg.AddInspectionAsync(arac, t0, t0.AddYears(2), 1500m);
        Assert.NotEqual(Guid.Empty, pol);
        Assert.NotEqual(Guid.Empty, mtv);
        Assert.NotEqual(Guid.Empty, mua);

        // Kayıt para YAZMADI: bu tenant'ın defteri bomboş.
        var factory = Svc<IDbContextFactory<AppDbContext>>(scope);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.AccountLedgerEntries.AsNoTracking().CountAsync());
    }

    // ---- BULGU O7-HGS — HgsReflectionService.ReflectAsync yetki DENETİMSİZ ----
    // Servis ICurrentUser almaz ve PermissionGuard ÇAĞIRMAZ → Operator (FinanceWrite YOK) HGS
    // yansıtmasıyla cari BORÇLANDIRABİLİYOR (Borç Cari / Alacak Gelir). CLAUDE.md §4 servis-guard
    // sözleşmesinin ihlali. Bu test MEVCUT YANLIŞ davranışı ampirik BELGELER (DenetimParaProbe
    // "BULGU" deseni) — guard eklenince ValidationException beklenecek şekilde GÜNCELLENMELİDİR.
    // Hafifletici: servis şu an hiçbir web ucuna bağlı değil (yalnız DI'da kayıtlı).
    [Fact]
    public async Task BULGU_O7_Hgs_yansitma_Operator_ile_para_yazabiliyor_guard_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var sp = scope.ServiceProvider;
        var t = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var hgs = new HgsReflectionService(
            new SabitHgs([new TollCrossing(t, "Köprü", 100m)]),
            sp.GetRequiredService<ILedgerPoster>(),
            sp.GetRequiredService<IPeriodLockGuard>());
        var cari = Guid.NewGuid();

        // BEKLENEN (doğru davranış): ValidationException("yetkiniz yok"). GERÇEK: para postlanıyor.
        var sonuc = await hgs.ReflectAsync(cari, "34 OP 01", t, t.AddDays(1));

        Assert.Equal(103m, sonuc.YansitilanTutar); // 100 × 1.03 — bulgu kanıtı: defter yazıldı
        Assert.Equal(103m, await Svc<CashService>(scope).GetCariBalanceAsync(cari));
    }

    /// <summary>Sabit geçiş listesi döndüren HGS test double'ı.</summary>
    private sealed class SabitHgs(IReadOnlyList<TollCrossing> crossings) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(
            string plaka, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(crossings);
    }
}
