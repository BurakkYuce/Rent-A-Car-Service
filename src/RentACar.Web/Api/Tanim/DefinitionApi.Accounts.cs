using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — cash/bank account definitions (<c>/hesaplar</c>, Blazor <c>FinancialAccountList</c>): OperationsWrite like
/// the page. No money is written here; the service keeps its guards (type must resolve to Kasa/Banka, an account with
/// ledger history cannot be deleted — deactivate it). Tenant-wide list like the Blazor page (no branch scope).
/// </summary>
public static partial class DefinitionApi
{
    private static void MapAccountDefinitions(RouteGroupBuilder v1)
        => v1.MapDefinition(new DefinitionRoute<FinancialAccount, FinancialAccountDto, FinancialAccountRequest>
        {
            Path = "/hesaplar", Tag = Tag, Permission = Permission.OperationsWrite,
            List = (sp, ct) => S<FinancialAccountService>(sp).ListAsync(ct),
            Get = (sp, id, ct) => S<FinancialAccountService>(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => S<FinancialAccountService>(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => S<FinancialAccountService>(sp).GetVersionsAsync(ct),
            Create = (sp, b, ct) => S<FinancialAccountService>(sp).CreateAsync(FinancialAccountInputOf(b), ct),
            Update = (sp, id, b, v, ct) => S<FinancialAccountService>(sp).UpdateAsync(id, FinancialAccountInputOf(b), v, ct),
            Delete = (sp, id, ct) => S<FinancialAccountService>(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = FinancialAccountDto.From,
            Sort = FinancialAccountDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Banka, r.Iban, r.Sube],
            ValidateLimits = r =>
            {
                RentalLimits.Text(r.Kod, 32, "kod", "Kod");
                RentalLimits.Text(r.Ad, 128, "ad", "Ad");
                RentalLimits.Text(r.Tur, 32, "tur", "Tür");
                RentalLimits.Text(r.Iban, 34, "iban", "IBAN");
                RentalLimits.Text(r.HesapNo, 64, "hesapNo", "Hesap no");
                RentalLimits.Text(r.Banka, 128, "banka", "Banka");
                RentalLimits.Text(r.Sube, 128, "sube", "Şube");
                RentalLimits.Text(r.OzelKod, 32, "ozelKod", "Özel kod");
                RentalLimits.Text(r.UyariMailListesi, 512, "uyariMailListesi", "Uyarı mail listesi");
            },
            FieldRules =
            [
                ("Hesap kodu", "kod"), ("'", "kod"), ("Hesap adı", "ad"), ("Hesap türü", "tur"), ("Döviz kodu", "doviz"),
            ],
            NotFoundMessage = "Hesap bulunamadı.",
        });

    private static FinancialAccountInput FinancialAccountInputOf(FinancialAccountRequest b) => new()
    {
        Kod = b.Kod ?? "", Ad = b.Ad ?? "", Tur = b.Tur, Doviz = b.Doviz, Iban = b.Iban, HesapNo = b.HesapNo,
        Banka = b.Banka, Sube = b.Sube, HediyeCek = b.HediyeCek, OzelKod = b.OzelKod,
        UyariMailListesi = b.UyariMailListesi, Aktif = b.Aktif,
    };
}

/// <summary>Cash/bank account. <c>Tur</c>: "Kasa" | "Banka" (free text resolving to one of them).</summary>
public sealed record FinancialAccountDto(Guid Id, string Kod, string Ad, string? Tur, string? Doviz, string? Iban,
    string? HesapNo, string? Banka, string? Sube, bool HediyeCek, string? OzelKod, string? UyariMailListesi, bool Aktif,
    string? Surum) : IDefinitionRow
{
    public static FinancialAccountDto From(FinancialAccount a, string? version) => new(a.Id, a.Kod, a.Ad, a.Tur, a.Doviz,
        a.Iban, a.HesapNo, a.Banka, a.Sube, a.HediyeCek, a.OzelKod, a.UyariMailListesi, a.Aktif, version);

    internal static readonly SortFieldMap<FinancialAccountDto> Sort = SortFieldMap<FinancialAccountDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("tur", x => x.Tur)
        .Alan("doviz", x => x.Doviz).Alan("banka", x => x.Banka).Alan("sube", x => x.Sube).Alan("aktif", x => x.Aktif);
}

/// <summary>POST/PUT body; <c>surum</c> REQUIRED on PUT. Döviz is normalized to 3 upper-case letters by the service.</summary>
public sealed record FinancialAccountRequest(string? Kod, string? Ad, string? Tur, string? Doviz, string? Iban,
    string? HesapNo, string? Banka, string? Sube, bool HediyeCek = false, string? OzelKod = null,
    string? UyariMailListesi = null, bool Aktif = true, string? Surum = null) : IDefinitionRequest;
