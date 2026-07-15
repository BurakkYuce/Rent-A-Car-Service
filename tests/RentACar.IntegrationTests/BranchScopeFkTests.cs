using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 5-C2 — BranchScope FK-farkındalı geçiş kuralı (saf birim; DB gerekmez).
/// KURAL: Unrestricted geç; (iki FK dolu ∧ eşit) geç — YENİDEN-ADLANDIRMA KURTARMASI (bugün kilitlenirdi);
/// metin Ordinal-eşit geç (mevcut davranış — FK'sız kayıt/claim'siz eski oturum); aksi RED.
/// Bilinçli genişletme: FK-uyuşmaz ∧ metin-eşit GEÇER (metin doğruluk-kaynağı; C5'in koşullu işi daraltır).
/// </summary>
public sealed class BranchScopeFkTests
{
    private sealed class Kimlik : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string? UserName => "t";
        public UserRole? Role { get; init; } = UserRole.Operator;
        public string? AssignedBranch { get; init; }
        public Guid? AssignedBranchId { get; init; }
    }

    private static readonly Guid B1 = Guid.NewGuid();
    private static readonly Guid B2 = Guid.NewGuid();

    [Fact]
    public void Yeniden_adlandirilan_subede_fk_kurtarir()
    {
        // Claim eski metinle ("Merkez", FK=B1); kayıt yeni metinle ("MERKEZ OFİS", FK=B1).
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = B1 };
        BranchScope.RequireInScope(op, B1, "MERKEZ OFİS"); // metin uyuşmaz ama FK eşit → GEÇ (önce kilitlenirdi)
    }

    [Fact]
    public void Fksiz_kayit_metin_yoluyla_aynen()
    {
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = B1 };
        BranchScope.RequireInScope(op, null, "Merkez");                       // metin eşit → geç
        Assert.Throws<ValidationException>(
            () => BranchScope.RequireInScope(op, null, "Ankara"));            // metin farklı → red
    }

    [Fact]
    public void Claimsiz_eski_oturum_yalniz_metinle_calisir()
    {
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = null }; // deploy öncesi oturum
        BranchScope.RequireInScope(op, B1, "Merkez");                          // metin eşit → geç
        Assert.Throws<ValidationException>(
            () => BranchScope.RequireInScope(op, B1, "Ankara"));               // FK tek taraflı → kurtarmaz
    }

    [Fact]
    public void Fk_uyusmaz_metin_esit_bilincli_genisletmeyle_gecer()
    {
        // Yalnız yeniden-adlandırma/elle-SQL ile oluşabilir (interceptor FK'yı aynı metinden türetir).
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = B1 };
        BranchScope.RequireInScope(op, B2, "Merkez"); // metin doğruluk-kaynağı → geç (C5 daraltacak)
    }

    [Fact]
    public void Ikisi_de_uyusmazsa_red_unrestricted_serbest()
    {
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = B1 };
        Assert.Throws<ValidationException>(() => BranchScope.RequireInScope(op, B2, "Ankara"));

        var admin = new Kimlik { Role = UserRole.Admin, AssignedBranch = "Merkez", AssignedBranchId = B1 };
        BranchScope.RequireInScope(admin, B2, "Ankara");                       // Admin → Unrestricted

        var subesiz = new Kimlik { AssignedBranch = null, AssignedBranchId = null };
        BranchScope.RequireInScope(subesiz, B2, "Ankara");                     // şubesiz operatör → serbest
    }

    [Fact]
    public void Eski_yuzeyler_birebir_ayni_davranir()
    {
        // Effective() delegasyonu (liste filtreleri C3'e dek bunu kullanır) + 1-arg RequireInScope overload'ı.
        var op = new Kimlik { AssignedBranch = " Merkez ", AssignedBranchId = B1 };
        Assert.Equal("Merkez", BranchScope.Effective(op));
        BranchScope.RequireInScope(op, "Merkez");
        Assert.Throws<ValidationException>(() => BranchScope.RequireInScope(op, "Ankara"));
        Assert.Null(BranchScope.Effective(new Kimlik { Role = UserRole.Muhasebe, AssignedBranch = "Merkez" }));
    }
}
