using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Users;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — <c>/kullanicilar</c> (Blazor <c>UserList</c> + <c>UserEndpoints</c>), <c>/yetki/matris</c> ve
/// <c>/profil/sifre</c> (<c>ProfileEndpoints</c>).
/// <list type="bullet">
/// <item>Kullanıcı yönetimi <see cref="Permission.ManageUsers"/>. Users platform tablosudur (RLS'siz); kiracı
/// filtresi <c>UserRepository</c>'de açıkça uygulanır → başka firmanın kullanıcı kimliği 404.</item>
/// <item>Parola hash'i, takvim belirteci hiçbir yanıtta yok. Parola en az 6 karakter (servis), en çok 128 (uç).</item>
/// <item>Kemerler: kendini pasifleştiremez, son aktif Admin pasifleştirilemez (servis), kendi istisnanı
/// değiştiremezsin, Admin'den ManageUsers alınamaz (servis).</item>
/// <item>Kendi parolası: kimlik ASLA gövdeden alınmaz (<c>ICurrentUser</c>); giriş hız sınırı politikası uygulanır
/// (eski parola kaba kuvvet denemesine karşı).</item>
/// </list>
/// </summary>
public static partial class SystemAdminApi
{
    private const int PasswordMaxLength = 128;

    private static void MapUsers(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/kullanicilar").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);

        g.MapGet("", async Task<Ok<IReadOnlyList<UserDto>>> (UserService users, UserPermissionService exceptions, CancellationToken ct)
            => TypedResults.Ok(await ListUsersAsync(users, exceptions, ct)));

        g.MapGet("/{id:guid}", async Task<Results<Ok<UserDto>, ProblemHttpResult>> (Guid id, UserService users, UserPermissionService exceptions, CancellationToken ct)
            => (await ListUsersAsync(users, exceptions, ct)).FirstOrDefault(u => u.Id == id) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Kullanıcı bulunamadı."));

        g.MapPost("", async Task<Results<Created<UserDto>, ProblemHttpResult>> (UserCreateRequest i, UserService users,
            UserPermissionService exceptions, BranchService branches, CancellationToken ct) =>
        {
            RentalLimits.Text(i.KullaniciAdi, 128, "kullaniciAdi", "Kullanıcı adı");
            RentalLimits.Text(i.GorunenAd, 256, "gorunenAd", "Görünen ad");
            RequirePasswordBounds(i.Sifre, "sifre");
            var role = F5Shared.EnumAdi<UserRole>(i.Rol, "rol") ?? throw new ValidationException("Rol zorunludur.", "rol");
            var branch = await ResolveBranchAsync(i.AtanmisSube, branches, ct);
            var id = await users.CreateAsync(new UserInput
            {
                UserName = i.KullaniciAdi ?? "", DisplayName = i.GorunenAd ?? "", Rol = role, Password = i.Sifre ?? "",
                AtanmisSube = branch,
            }, ct);
            return (await ListUsersAsync(users, exceptions, ct)).FirstOrDefault(u => u.Id == id) is { } d
                ? TypedResults.Created($"{UiApiExtensions.V1}/kullanicilar/{id}", d)
                : SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
        }).MapFields(UserRules);

        g.MapPost("/{id:guid}/aktif", async Task<Results<Ok<UserDto>, ProblemHttpResult>> (Guid id, UserActiveRequest i, UserService users,
            UserPermissionService exceptions, CancellationToken ct) =>
        {
            if (!await users.SetActiveAsync(id, i.Aktif, ct)) return SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
            return (await ListUsersAsync(users, exceptions, ct)).FirstOrDefault(u => u.Id == id) is { } d
                ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
        }).MapFields([("Kendi hesabınızı", "aktif"), ("Son aktif Admin", "aktif")]);

        g.MapPost("/{id:guid}/sifre", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, PasswordResetRequest i, UserService users, CancellationToken ct) =>
        {
            RequirePasswordBounds(i.Sifre, "sifre");
            return await users.ResetPasswordAsync(id, i.Sifre ?? "", ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
        }).MapFields([("Parola", "sifre")]);

        // ---- kullanıcı-bazlı izin istisnaları (etkinleşme: hedef kullanıcının bir sonraki girişi)
        g.MapPut("/{id:guid}/istisnalar/{izin}", async Task<Results<Ok<UserDto>, ProblemHttpResult>> (Guid id, string izin, PermissionExceptionRequest i,
            UserService users, UserPermissionService exceptions, CancellationToken ct) =>
        {
            var p = F5Shared.EnumAdi<Permission>(izin, "izin") ?? throw new ValidationException("İzin zorunludur.", "izin");
            if ((await ListUsersAsync(users, exceptions, ct)).All(u => u.Id != id)) return SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
            await exceptions.SetAsync(id, p.ToString(), i.Ver, ct);
            return (await ListUsersAsync(users, exceptions, ct)).FirstOrDefault(u => u.Id == id) is { } d
                ? TypedResults.Ok(d) : SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
        }).MapFields(ExceptionRules);

        g.MapDelete("/{id:guid}/istisnalar/{izin}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, string izin,
            UserService users, UserPermissionService exceptions, CancellationToken ct) =>
        {
            var p = F5Shared.EnumAdi<Permission>(izin, "izin") ?? throw new ValidationException("İzin zorunludur.", "izin");
            if ((await ListUsersAsync(users, exceptions, ct)).All(u => u.Id != id)) return SystemApiCommon.NotFound("Kullanıcı bulunamadı.");
            return await exceptions.RemoveAsync(id, p.ToString(), ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound("İstisna bulunamadı.");
        }).MapFields(ExceptionRules);

        // ---- rol → izin matrisi (salt okunur; UI rozetleri için)
        v1.MapGet("/yetki/matris", () => TypedResults.Ok(PermissionMatrix()))
            .WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);

        // ---- kendi parolası (herhangi bir oturum)
        v1.MapPost("/profil/sifre", async Task<NoContent> (OwnPasswordRequest i, UserService users, CancellationToken ct) =>
        {
            RequirePasswordBounds(i.YeniSifre, "yeniSifre");
            RentalLimits.Text(i.EskiSifre, PasswordMaxLength, "eskiSifre", "Mevcut parola");
            if (i.YeniSifreTekrar is not null && !string.Equals(i.YeniSifre, i.YeniSifreTekrar, StringComparison.Ordinal))
                throw new ValidationException("Yeni parolalar birbiriyle eşleşmiyor.", "yeniSifreTekrar");
            await users.ChangeOwnPasswordAsync(i.EskiSifre ?? "", i.YeniSifre ?? "", ct);
            return TypedResults.NoContent();
        }).WithTags(SystemApiCommon.Tag)
          .PermissionExempt("Kendi parolası: her oturum açmış kullanıcı; kimlik ICurrentUser'dan, eski parola doğrulanır.")
          .RequireRateLimiting("login")
          .MapFields([("Mevcut parola", "eskiSifre"), ("Parola", "yeniSifre"), ("Oturum", "eskiSifre")]);
    }

    private static readonly (string, string)[] UserRules =
    [
        ("Kullanıcı adı", "kullaniciAdi"), ("Bu kullanıcı adı", "kullaniciAdi"), ("Parola", "sifre"),
    ];

    private static readonly (string, string)[] ExceptionRules =
    [
        ("Geçersiz izin", "izin"), ("Kendi izin", "izin"), ("Admin rolündeki", "izin"), ("Kullanıcı bulunamadı", "izin"),
    ];

    private static void RequirePasswordBounds(string? password, string field)
    {
        if (password is { Length: > PasswordMaxLength })
            throw new ValidationException($"Parola en fazla {PasswordMaxLength} karakter olabilir.", field);
    }

    /// <summary>Atanmış şube (metin) — yalnız firmanın AKTİF şubelerinden biri (Blazor seçim listesi paritesi; serbest metin
    /// operatörü hiçbir kaydı göremeyen "yetim" kapsama düşürürdü).</summary>
    private static async Task<string?> ResolveBranchAsync(string? branch, BranchService branches, CancellationToken ct)
    {
        if (SystemApiCommon.Clean(branch) is not { } b) return null;
        var match = (await branches.ListActiveAsync(ct)).FirstOrDefault(x => string.Equals(x.Ad, b, StringComparison.Ordinal));
        return match?.Ad ?? throw new ValidationException("Atanmış şube firmanın aktif şubelerinden biri olmalıdır.", "atanmisSube");
    }

    private static async Task<IReadOnlyList<UserDto>> ListUsersAsync(UserService users, UserPermissionService exceptions, CancellationToken ct)
    {
        var list = await users.ListAsync(ct);
        var ex = (await exceptions.ListAsync(ct)).ToLookup(x => x.UserId);
        return list.Select(u =>
        {
            var own = ex[u.Id].ToList();
            var grant = own.Where(x => x.Ver).Select(x => x.Izin).ToList();
            var deny = own.Where(x => !x.Ver).Select(x => x.Izin).ToList();
            return new UserDto(u.Id, u.UserName, u.DisplayName, u.Rol.ToString(), u.IsActive, u.AtanmisSube,
                own.Select(x => new PermissionExceptionDto(x.Izin, x.Ver, x.Tanimlayan, x.TarihUtc.ToUniversalTime())).ToList(),
                Enum.GetValues<Permission>().Where(p => EffectivePermission.Has(u.Rol, p, grant, deny)).Select(p => p.ToString()).ToList());
        }).ToList();
    }

    private static IReadOnlyList<RolePermissionsDto> PermissionMatrix()
        => Enum.GetValues<UserRole>().Select(r => new RolePermissionsDto(r.ToString(),
            Enum.GetValues<Permission>().Where(p => RolePermissions.Has(r, p)).Select(p => p.ToString()).ToList())).ToList();
}

public sealed record UserDto(Guid Id, string KullaniciAdi, string GorunenAd, string Rol, bool Aktif, string? AtanmisSube,
    IReadOnlyList<PermissionExceptionDto> Istisnalar, IReadOnlyList<string> EtkinIzinler);

public sealed record PermissionExceptionDto(string Izin, bool Ver, string? Tanimlayan, DateTimeOffset TarihUtc);

public sealed record RolePermissionsDto(string Rol, IReadOnlyList<string> Izinler);

public sealed record UserCreateRequest(string? KullaniciAdi, string? GorunenAd, string? Rol, string? Sifre, string? AtanmisSube);

public sealed record UserActiveRequest(bool Aktif);

public sealed record PasswordResetRequest(string? Sifre);

public sealed record PermissionExceptionRequest(bool Ver);

public sealed record OwnPasswordRequest(string? EskiSifre, string? YeniSifre, string? YeniSifreTekrar);
