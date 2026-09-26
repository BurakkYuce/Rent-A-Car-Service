using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 5-C2/C5 — BranchScope FK-farkındalı geçiş kuralı (saf birim; DB gerekmez).
/// KURAL (C5 son hali): Unrestricted geç; iki FK de doluysa FK eşitliği TEK BAŞINA karar verir
/// (yeniden-adlandırma kurtarması kalır; FK-uyuşmaz∧metin-eşit SIZINTISI [F2] kapalı);
/// FK'lardan biri boşsa metin Ordinal-eşit geç (FK'sız kayıt/claim'siz eski oturum — KALICI); aksi RED.
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
        Assert.Throws<NoPermissionException>(
            () => BranchScope.RequireInScope(op, null, "Ankara"));            // metin farklı → red
    }

    [Fact]
    public void Claimsiz_eski_oturum_yalniz_metinle_calisir()
    {
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = null }; // deploy öncesi oturum
        BranchScope.RequireInScope(op, B1, "Merkez");                          // metin eşit → geç
        Assert.Throws<NoPermissionException>(
            () => BranchScope.RequireInScope(op, B1, "Ankara"));               // FK tek taraflı → kurtarmaz
    }

    [Fact]
    public void Fk_uyusmaz_metin_esit_artik_red_f2_kapali()
    {
        // C5 daraltması: iki FK de doluyken metin OR'u düşer — yeniden-adlandırma çakışması
        // (B2'ye B1'in eski adı verilir) artık çapraz-şube sızdırmaz. C2'de bilinçli genişletmeyle
        // GEÇİYORDU (dokümante F2); ön koşul (FK-uyuşmaz∧metin-eşleşir sayacı 0) doğrulanıp kapatıldı.
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = B1 };
        Assert.Throws<NoPermissionException>(() => BranchScope.RequireInScope(op, B2, "Merkez"));
    }

    [Fact]
    public void Ikisi_de_uyusmazsa_red_unrestricted_serbest()
    {
        var op = new Kimlik { AssignedBranch = "Merkez", AssignedBranchId = B1 };
        Assert.Throws<NoPermissionException>(() => BranchScope.RequireInScope(op, B2, "Ankara"));

        var admin = new Kimlik { Role = UserRole.Admin, AssignedBranch = "Merkez", AssignedBranchId = B1 };
        BranchScope.RequireInScope(admin, B2, "Ankara");                       // Admin → Unrestricted

        var withoutBranch = new Kimlik { AssignedBranch = null, AssignedBranchId = null };
        BranchScope.RequireInScope(withoutBranch, B2, "Ankara");                     // şubesiz operatör → serbest
    }

    [Fact]
    public void Eski_yuzeyler_birebir_ayni_davranir()
    {
        // Effective() delegasyonu (liste filtreleri C3'e dek bunu kullanır) + 1-arg RequireInScope overload'ı.
        var op = new Kimlik { AssignedBranch = " Merkez ", AssignedBranchId = B1 };
        Assert.Equal("Merkez", BranchScope.Effective(op));
        BranchScope.RequireInScope(op, "Merkez");
        Assert.Throws<NoPermissionException>(() => BranchScope.RequireInScope(op, "Ankara"));
        Assert.Null(BranchScope.Effective(new Kimlik { Role = UserRole.Muhasebe, AssignedBranch = "Merkez" }));
    }
}
