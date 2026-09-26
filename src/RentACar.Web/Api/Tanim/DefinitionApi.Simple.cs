using Microsoft.EntityFrameworkCore;
using RentACar.Application.Accessories;
using RentACar.Application.Authorization;
using RentACar.Application.Banks;
using RentACar.Application.Common;
using RentACar.Application.Currencies;
using RentACar.Application.CustomCodes;
using RentACar.Application.ExpenseCategories;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — definitions with their own service (not the <see cref="MasterDefinitionService{T}"/> base): Aksesuar, Banka,
/// Döviz, Özel kod, Gider türü. Same generic contract; extra columns and limits per entity.
/// Free-text columns without a DB limit (<c>text</c>) get a 512-char edge cap so a body cannot grow unbounded.
/// </summary>
public static partial class DefinitionApi
{
    private const int FreeTextMax = 512;

    private static T S<T>(IServiceProvider sp) where T : notnull => sp.GetRequiredService<T>();

    private static void MapSimpleDefinitions(RouteGroupBuilder v1)
    {
        v1.MapDefinition(new DefinitionRoute<Accessory, AccessoryDto, AccessoryRequest>
        {
            Path = "/aksesuarlar", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<AccessoryService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<AccessoryService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<AccessoryService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<AccessoryService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<AccessoryService>(sp).CreateAsync(AccessoryInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<AccessoryService>(sp).UpdateAsync(id, AccessoryInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<AccessoryService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new AccessoryDto(e.Id, e.Kod, e.Ad, e.Aciklama, e.Aktif, v),
            Sort = AccessoryDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Aciklama],
            ValidateLimits = r =>
            {
                RentalLimits.Text(r.Kod, 32, "kod", "Kod");
                RentalLimits.Text(r.Ad, 128, "ad", "Ad");
                RentalLimits.Text(r.Aciklama, 512, "aciklama", "Açıklama");
            },
            FieldRules = [("Aksesuar kodu", "kod"), ("'", "kod"), ("Aksesuar adı", "ad")],
            NotFoundMessage = "Aksesuar bulunamadı.",
        });

        v1.MapDefinition(new DefinitionRoute<Bank, DefinitionDto, DefinitionRequest>
        {
            Path = "/bankalar", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<BankService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<BankService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<BankService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<BankService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<BankService>(sp).CreateAsync(new BankInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            Update = (sp, id, b, v, ct) => S<BankService>(sp).UpdateAsync(id, new BankInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct),
            Delete = (sp, id, ct) => S<BankService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new DefinitionDto(e.Id, e.Kod, e.Ad, e.Aktif, v),
            Sort = MasterDefinitions.Sort,
            SearchText = r => [r.Kod, r.Ad],
            ValidateLimits = MasterDefinitions.Limits,
            FieldRules = [("Banka kodu", "kod"), ("'", "kod"), ("Banka adı", "ad")],
            // Bank is stored BY NAME on cash/bank accounts (FinancialAccount.Banka).
            InUse = async (sp, e, ct) => await CountAsync(sp, db => db.FinancialAccounts.CountAsync(x => x.Banka == e.Ad, ct)) is var n and > 0
                ? DefinitionMessages.InUse("banka", "kasa/banka hesabı", n) : null,
            NotFoundMessage = "Banka bulunamadı.",
        });

        v1.MapDefinition(new DefinitionRoute<Currency, CurrencyDto, CurrencyRequest>
        {
            Path = "/dovizler", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<CurrencyService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<CurrencyService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<CurrencyService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<CurrencyService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<CurrencyService>(sp).CreateAsync(CurrencyInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<CurrencyService>(sp).UpdateAsync(id, CurrencyInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<CurrencyService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = (e, v) => new CurrencyDto(e.Id, e.Kod, e.Ad, e.Sembol, e.Ulke, e.Aktif, v),
            Sort = CurrencyDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Ulke],
            ValidateLimits = r =>
            {
                RentalLimits.Text(r.Ad, 128, "ad", "Ad");
                RentalLimits.Text(r.Sembol, 8, "sembol", "Sembol");
                RentalLimits.Text(r.Ulke, 64, "ulke", "Ülke");
            },
            FieldRules = [("Döviz kodu", "kod"), ("'", "kod"), ("Döviz adı", "ad")],
            InUse = CurrencyInUseAsync,
            NotFoundMessage = "Döviz bulunamadı.",
        });

        MapCodeDefinitions(v1);
        MapAccountDefinitions(v1);
        MapPricingDefinitions(v1);
    }

    private static AccessoryInput AccessoryInputOf(AccessoryRequest b)
        => new() { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aciklama = F(b.Aciklama), Aktif = b.Aktif };

    private static CurrencyInput CurrencyInputOf(CurrencyRequest b)
        => new() { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Sembol = F(b.Sembol), Ulke = F(b.Ulke), Aktif = b.Aktif };

    /// <summary>Blank → null, otherwise trimmed (Blazor <c>FormParse.Str</c>).</summary>
    private static string? F(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<int> CountAsync(IServiceProvider sp, Func<AppDbContext, Task<int>> count)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await count(db);
    }

    /// <summary>A currency code on posted money (ledger, invoices, expenses) must stay resolvable: deleting its
    /// definition would leave history pointing at an unknown currency. Deactivate instead.</summary>
    private static async Task<string?> CurrencyInUseAsync(IServiceProvider sp, Currency e, CancellationToken ct)
    {
        var n = await CountAsync(sp, async db =>
            await db.AccountLedgerEntries.CountAsync(x => x.Amount.Currency == e.Kod, ct)
            + await db.Invoices.CountAsync(x => x.Currency == e.Kod, ct)
            + await db.Expenses.CountAsync(x => x.Currency == e.Kod, ct));
        return n > 0 ? DefinitionMessages.InUse("döviz", "defter/fatura/gider", n) : null;
    }
}
