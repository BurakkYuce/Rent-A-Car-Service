using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.CustomCodes;
using RentACar.Application.ExpenseCategories;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Tanim;

public static partial class DefinitionApi
{
    private static void MapCodeDefinitions(RouteGroupBuilder v1)
    {
        v1.MapDefinition(new DefinitionRoute<CustomCode, CustomCodeDto, CustomCodeRequest>
        {
            Path = "/ozel-kodlar", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<CustomCodeService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<CustomCodeService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<CustomCodeService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<CustomCodeService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<CustomCodeService>(sp).CreateAsync(CustomCodeInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<CustomCodeService>(sp).UpdateAsync(id, CustomCodeInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<CustomCodeService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new CustomCodeDto(e.Id, e.Kod, e.Ad, e.Aciklama, e.Turu, e.Aktif, v),
            Sort = CustomCodeDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Aciklama, r.Turu],
            ValidateLimits = r =>
            {
                RentalLimits.Text(r.Kod, 32, "kod", "Kod");
                RentalLimits.Text(r.Ad, 128, "ad", "Ad");
                RentalLimits.Text(r.Aciklama, 512, "aciklama", "Açıklama");
                RentalLimits.Text(r.Turu, FreeTextMax, "turu", "Tür");
            },
            // Service messages: "Özel kod zorunludur.", "Özel kod en çok…", "Özel kod adı zorunludur." — the
            // longer prefix must come first.
            FieldRules = [("Özel kod adı", "ad"), ("Özel kod", "kod"), ("'", "kod")],
            InUse = CustomCodeInUseAsync,
            NotFoundMessage = "Özel kod bulunamadı.",
        });

        v1.MapDefinition(new DefinitionRoute<ExpenseCategory, ExpenseCategoryDto, ExpenseCategoryRequest>
        {
            Path = "/gider-turleri", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<ExpenseCategoryService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<ExpenseCategoryService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<ExpenseCategoryService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<ExpenseCategoryService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<ExpenseCategoryService>(sp).CreateAsync(ExpenseCategoryInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<ExpenseCategoryService>(sp).UpdateAsync(id, ExpenseCategoryInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<ExpenseCategoryService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new ExpenseCategoryDto(e.Id, e.Kod, e.Ad, e.Tur, e.Aktif, v),
            Sort = ExpenseCategoryDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Tur],
            ValidateLimits = r =>
            {
                RentalLimits.Text(r.Kod, 32, "kod", "Kod");
                RentalLimits.Text(r.Ad, 128, "ad", "Ad");
                RentalLimits.Text(r.Tur, FreeTextMax, "tur", "Tür");
            },
            FieldRules = [("Gider türü kodu", "kod"), ("'", "kod"), ("Gider türü adı", "ad")],
            // Incoming e-invoices reference the category BY ID (no FK constraint) — a delete would orphan them.
            InUse = async (sp, e, ct) => await CountAsync(sp, db => db.GelenEFaturalar.CountAsync(x => x.ExpenseCategoryId == e.Id, ct)) is var n and > 0
                ? DefinitionMessages.InUse("gider türü", "gelen e-fatura", n) : null,
            NotFoundMessage = "Gider türü bulunamadı.",
        });
    }

    private static CustomCodeInput CustomCodeInputOf(CustomCodeRequest b)
        => new() { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aciklama = F(b.Aciklama), Turu = F(b.Turu), Aktif = b.Aktif };

    private static ExpenseCategoryInput ExpenseCategoryInputOf(ExpenseCategoryRequest b)
        => new() { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Tur = F(b.Tur), Aktif = b.Aktif };

    /// <summary>A custom code is stored as text (the SPA pick list offers name and code) on customers, rentals and
    /// cash/bank accounts.</summary>
    private static async Task<string?> CustomCodeInUseAsync(IServiceProvider sp, CustomCode e, CancellationToken ct)
    {
        var n = await CountAsync(sp, async db =>
            await db.Customers.CountAsync(x => x.OzelKod == e.Ad || x.OzelKod == e.Kod, ct)
            + await db.Rentals.CountAsync(x => x.OzelKod == e.Ad || x.OzelKod == e.Kod, ct)
            + await db.FinancialAccounts.CountAsync(x => x.OzelKod == e.Ad || x.OzelKod == e.Kod, ct));
        return n > 0 ? DefinitionMessages.InUse("özel kod", "cari/kira/hesap", n) : null;
    }
}

public sealed record AccessoryDto(Guid Id, string Kod, string Ad, string? Aciklama, bool Aktif, string? Surum) : IDefinitionRow
{
    internal static readonly SortFieldMap<AccessoryDto> Sort = SortFieldMap<AccessoryDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("aktif", x => x.Aktif);
}
public sealed record AccessoryRequest(string? Kod, string? Ad, string? Aciklama, bool Aktif = true, string? Surum = null) : IDefinitionRequest;

public sealed record CurrencyDto(Guid Id, string Kod, string Ad, string? Sembol, string? Ulke, bool Aktif, string? Surum) : IDefinitionRow
{
    internal static readonly SortFieldMap<CurrencyDto> Sort = SortFieldMap<CurrencyDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("ulke", x => x.Ulke).Alan("aktif", x => x.Aktif);
}
public sealed record CurrencyRequest(string? Kod, string? Ad, string? Sembol, string? Ulke, bool Aktif = true, string? Surum = null) : IDefinitionRequest;

public sealed record CustomCodeDto(Guid Id, string Kod, string Ad, string? Aciklama, string? Turu, bool Aktif, string? Surum) : IDefinitionRow
{
    internal static readonly SortFieldMap<CustomCodeDto> Sort = SortFieldMap<CustomCodeDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("turu", x => x.Turu).Alan("aktif", x => x.Aktif);
}
public sealed record CustomCodeRequest(string? Kod, string? Ad, string? Aciklama, string? Turu, bool Aktif = true, string? Surum = null) : IDefinitionRequest;

public sealed record ExpenseCategoryDto(Guid Id, string Kod, string Ad, string? Tur, bool Aktif, string? Surum) : IDefinitionRow
{
    internal static readonly SortFieldMap<ExpenseCategoryDto> Sort = SortFieldMap<ExpenseCategoryDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("tur", x => x.Tur).Alan("aktif", x => x.Aktif);
}
public sealed record ExpenseCategoryRequest(string? Kod, string? Ad, string? Tur, bool Aktif = true, string? Surum = null) : IDefinitionRequest;
