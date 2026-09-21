using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.1 hata sözleşmesi. Beklenen değerler ELLE yazılmış tablodan gelir (bağımsız oracle) — üretim
/// kodundaki sabitler/sözlük KULLANILMAZ; kod tablosu değişirse test bilinçli güncellenmeli.
/// </summary>
public sealed class UiHataTests
{
    public static TheoryData<Exception, int, string> EslemeTablosu() => new()
    {
        { new YetkiYokException("Bu işlem için yetkiniz yok (FinanceWrite)."), 403, "yetki_yok" },
        { new MukerrerIslemException("Bu işlem zaten kaydedilmiş."), 409, "mukerrer" },
        { new AvailabilityConflictException(), 409, "cakisma" },
        { new DuplicateCariException("TC", "11111111111"), 409, "cakisma" },
        { new DuplicatePlakaException("34ABC01"), 409, "cakisma" },
        { new ValidationException("Plaka zorunludur.", "plaka"), 400, "dogrulama" },
        { new ValidationException("Genel iş kuralı."), 400, "dogrulama" },
    };

    [Theory]
    [MemberData(nameof(EslemeTablosu))]
    public void Esle_istisna_turunu_dogru_durum_ve_koda_cevirir(Exception ex, int durum, string kod)
    {
        var e = UiHata.Esle(ex);
        Assert.NotNull(e);
        Assert.Equal(durum, e.Value.Status);
        Assert.Equal(kod, e.Value.Kod);
    }

    [Fact]
    public void Esle_taninmayan_istisnada_null_doner()
    {
        Assert.Null(UiHata.Esle(new InvalidOperationException("iç ayrıntı")));
        Assert.Null(UiHata.Esle(new DbUpdateException("x")));
    }

    [Fact]
    public void Kod_tablosu_sozlesmedeki_kodlari_ve_durumlarini_tasir()
    {
        var beklenen = new Dictionary<string, int>
        {
            ["dogrulama"] = 400, ["yetki_yok"] = 403, ["pilot_degil"] = 403, ["cakisma"] = 409,
            ["mukerrer"] = 409, ["oturum_yok"] = 401, ["kiraci_kapali"] = 401, ["cok_istek"] = 429,
            ["xsrf_gecersiz"] = 400, // F1.2 eki: CSRF reddi (SPA token yenileyip tekrarlar)
        };
        Assert.Equal(beklenen.Count, UiHata.Tablo.Count);
        foreach (var (kod, durum) in beklenen)
        {
            Assert.Equal(durum, UiHata.Tablo[kod].Status);
            Assert.False(string.IsNullOrWhiteSpace(UiHata.Tablo[kod].Baslik));
        }
    }

    [Fact]
    public void Problem_alanli_dogrulamada_errors_uretir()
    {
        var p = UiHata.Problem(new ValidationException("Plaka zorunludur.", "plaka")).ProblemDetails;

        Assert.Equal(400, p.Status);
        Assert.Equal("Plaka zorunludur.", p.Detail);
        Assert.Equal("dogrulama", p.Extensions["kod"]);
        Assert.False(string.IsNullOrWhiteSpace(p.Title));
        Assert.False(string.IsNullOrWhiteSpace(p.Type));
        var errors = Assert.IsType<Dictionary<string, string[]>>(p.Extensions["errors"]);
        Assert.Equal(["Plaka zorunludur."], errors["plaka"]);
    }

    [Fact]
    public void Problem_alansiz_istisnada_errors_yok()
    {
        var p = UiHata.Problem(new YetkiYokException("Bu kayıt şube kapsamınız dışında.")).ProblemDetails;

        Assert.Equal(403, p.Status);
        Assert.Equal("yetki_yok", p.Extensions["kod"]);
        Assert.Equal("Bu kayıt şube kapsamınız dışında.", p.Detail);
        Assert.False(p.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void Problem_taninmayan_istisnada_500_ve_mesaji_sizdirmaz()
    {
        var p = UiHata.Problem(new InvalidOperationException("connection string: gizli")).ProblemDetails;

        Assert.Equal(500, p.Status);
        Assert.DoesNotContain("gizli", p.Detail ?? string.Empty);
        Assert.False(p.Extensions.ContainsKey("kod"));
    }
}

/// <summary>
/// Unique-ihlali sınıflandırması. Adlar DB'deki GERÇEK index adlarıdır (2026-09-21 dev DB'den elle
/// kopyalandı); <c>_Sira</c>/<c>_No</c> iş benzersizliğidir ve mükerrer SAYILMAMALI.
/// </summary>
public sealed class IdempotencyKisitiTests
{
    [Theory]
    [InlineData("IX_CashTransactions_TenantId_IslemAnahtari", true)]
    [InlineData("IX_CashTransactions_TenantId_TersAlinanId", true)]
    [InlineData("IX_AccountLedgerEntries_BakiyeDuzeltme_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_CariVirman_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_CezaOdeme_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_Depozito_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_MtvOdeme_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_MuayeneOdeme_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_ServisYansitma_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_SigortaOdeme_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_Virman_Idem", true)]
    [InlineData("IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction", true)]
    [InlineData("IX_DisHizmetAlimlari_TenantId_IslemAnahtari", true)]
    [InlineData("IX_Expenses_TenantId_IslemAnahtari", true)]
    [InlineData("IX_GiderOdemeleri_TenantId_Anahtar", true)]
    [InlineData("IX_GiderOdemeleri_TenantId_IslemAnahtari", true)]
    [InlineData("IX_MtvOdemeleri_TenantId_IslemAnahtari", true)]
    [InlineData("IX_MuayeneOdemeleri_TenantId_IslemAnahtari", true)]
    [InlineData("IX_PenaltyOdemeleri_TenantId_Anahtar", true)]
    [InlineData("IX_PenaltyOdemeleri_TenantId_IslemAnahtari", true)]
    // İş benzersizliği → mükerrer DEĞİL (davranış eskisi gibi düz ValidationException).
    [InlineData("IX_CashTransactions_TenantId_No", false)]
    [InlineData("IX_MtvOdemeleri_TenantId_MtvId_Sira", false)]
    [InlineData("IX_MuayeneOdemeleri_TenantId_InspectionId_Sira", false)]
    [InlineData("IX_PenaltySatirlari_TenantId_PenaltyId_Sira", false)]
    [InlineData("IX_AccountLedgerEntries_TenantId_SourceType_SourceId", false)]
    [InlineData("IX_CashTransactions_TenantId_IslemAnahtari_Eski", false)] // sonek kuralı: "içerir" değil "biter"
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MukerrerKisitiMi_gercek_index_adlarini_dogru_siniflar(string? ad, bool beklenen)
        => Assert.Equal(beklenen, IdempotencyKisiti.MukerrerKisitiMi(ad));

    private static DbUpdateException UniqueIhlali(string? kisit) =>
        new("kaydetme hatası", new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR",
            PostgresErrorCodes.UniqueViolation, constraintName: kisit));

    [Fact]
    public void Red_idempotency_kisitinda_Mukerrer_doner_mesaj_korunur()
    {
        var ex = IdempotencyKisiti.Red(UniqueIhlali("IX_CashTransactions_TenantId_IslemAnahtari"), "Bu işlem zaten kaydedilmiş.");
        Assert.IsType<MukerrerIslemException>(ex);
        Assert.Equal("Bu işlem zaten kaydedilmiş.", ex.Message);
    }

    [Fact]
    public void Red_is_benzersizliginde_duz_ValidationException_doner()
    {
        var ex = IdempotencyKisiti.Red(UniqueIhlali("IX_MtvOdemeleri_TenantId_MtvId_Sira"), "Bu MTV ödemesi zaten kaydedilmiş.");
        Assert.IsType<ValidationException>(ex); // tam tip: alt tip DEĞİL
        Assert.Equal("Bu MTV ödemesi zaten kaydedilmiş.", ex.Message);
    }

    [Fact]
    public void Red_postgres_disi_ic_istisnada_duz_ValidationException_doner()
    {
        var ex = IdempotencyKisiti.Red(new DbUpdateException("x", new InvalidOperationException()), "m");
        Assert.IsType<ValidationException>(ex);
    }

    [Theory]
    [InlineData("IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction")]
    [InlineData("IX_CashTransactions_TenantId_TersAlinanId")]
    [InlineData("IX_CashTransactions_TenantId_IslemAnahtari")]
    public void Acik_adla_taninan_index_EF_modelinde_var(string indexAdi)
    {
        // Sonek kuralına uymayan iki açık ad yeniden adlandırılırsa sınıflandırma SESSİZCE düz 400'e düşer;
        // bu test onu kırar. Model kurulumu bağlantı açmaz.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options;
        using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);

        var adlar = db.Model.GetEntityTypes()
            .SelectMany(et => et.GetIndexes())
            .Select(i => i.GetDatabaseName())
            .ToHashSet();

        Assert.Contains(indexAdi, adlar);
    }
}

/// <summary>RentACar.Api: servis-katmanı yetki reddi (şube kapsamı) artık 403 <c>forbidden</c>.</summary>
[Collection("postgres")]
public sealed class ApiHataSozlesmesiTests(PostgresFixture fx)
{
    private sealed record ErrBody(string error, string message);
    private sealed record IdBody(Guid id);

    private async Task KullaniciEkleAsync(Guid tenantId, string userName, string sifre, UserRole rol, string? sube)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var user = new User
        {
            TenantId = tenantId, UserName = userName, DisplayName = userName,
            Rol = rol, AtanmisSube = sube, IsActive = true
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, sifre);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sube_kapsami_disi_kayit_403_forbidden_doner()
    {
        var code = $"f11{Guid.NewGuid():N}";
        var tenantId = await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        await KullaniciEkleAsync(tenantId, "op", "p", UserRole.Operator, "Kadikoy");
        using var api = new ApiFactory(fx.AppConnectionString);

        var admin = await api.LoginClientAsync(code, "umit", "p");
        var created = await admin.PostAsJsonAsync("/api/v1/vehicles",
            new { plaka = "34F11A1", sube = "Merkez", durum = "Musait", km = 0, yakit = "Benzin" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<IdBody>())!.id;

        var op = await api.LoginClientAsync(code, "op", "p");
        var resp = await op.GetAsync($"/api/v1/vehicles/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrBody>();
        Assert.Equal("forbidden", err!.error);
        Assert.Equal("Bu kayıt şube kapsamınız dışında.", err.message);
    }
}
