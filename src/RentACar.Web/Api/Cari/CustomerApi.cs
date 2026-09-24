using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Details;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// <c>/api/ui/v1/cariler/*</c> — F7.1 cari uçları (para YOK). İş mantığı <see cref="CustomerService"/>'te; burada uç
/// kuralları: izin, KVKK görünümü, sınırlar, sunucu tarafı sayfalama/sıralama, iyimser eşzamanlılık.
/// <list type="bullet">
/// <item><b>KVKK:</b> TC hiçbir yanıtta dönmez (liste, kart, detay, hata). Ehliyet/pasaport maskeli. <c>Anonim*</c>
/// bayrakları <see cref="MusteriGorunumu"/> gruplarını gizler. Kısmi TC araması YOK (yalnız 11 hane tam eşleşme,
/// blind-index).</item>
/// <item><b>Okuma</b>: OperationsWrite VEYA FinanceWrite VEYA ViewReports (Blazor sayfaları yalnız <c>[Authorize]</c>).
/// Cari firma geneli master kayıttır (şube kolonu yok); detaydaki KİRALAR şube kapsamına süzülür.</item>
/// <item><b>Yazma</b> OperationsWrite; <b>silme</b> OperationsDelete; kirada kullanılan cari silinemez (400).</item>
/// </list>
/// </summary>
public static partial class CustomerApi
{
    private const string Root = UiApiExtensions.V1 + "/cariler";
    private const int MaxPage = 1_000_000;

    public static RouteGroupBuilder MapCustomerApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/cariler").WithTags("Cari");

        var read = g.MapGroup("").RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        read.MapGet("", List).AlanlariEsle(F5Ortak.SiralamaKurallari);
        read.MapGet("/{id:guid}", Card);
        read.MapGet("/{id:guid}/detay", Detail);
        read.MapGet("/secim/il", async (string? q, int? limit, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
            => Arac.AracApi.Oneri(q, limit, [], await AddressValuesAsync(dbf, c => c.Il, ct)));
        read.MapGet("/secim/ilce", async (string? q, int? limit, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
            => Arac.AracApi.Oneri(q, limit, [], await AddressValuesAsync(dbf, c => c.Ilce, ct)));

        var write = g.MapGroup("").RequirePermission(Permission.OperationsWrite);
        write.MapPost("", Create).AlanlariEsle(CustomerInputMapper.FieldRules);
        write.MapPut("/{id:guid}", Update).AlanlariEsle(CustomerInputMapper.FieldRules);
        g.MapDelete("/{id:guid}", Delete).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult NotFound() => F5Ortak.Bulunamadi("Cari bulunamadı.");

    // ================================================================== liste

    private static readonly SiralamaHaritasi<Customer> SortMap = SiralamaHaritasi<Customer>
        .Olustur(c => c.Id)
        .Alan("tip", c => c.Tip)
        // #283 KVKK M1: keys follow the DISPLAYED value — an anonymised name sorts at the label, a hidden
        // address/soyad sorts as empty; otherwise the row position would leak the real value.
        .Alan("ad", c => c.AnonimAd ? CariAnonimlik.AdEtiketi : c.Ad)
        .Alan("soyad", c => c.AnonimAd ? null : c.Soyad)
        .Alan("unvan", c => c.AnonimAd ? CariAnonimlik.AdEtiketi : c.Unvan)
        .Alan("il", c => c.AnonimAdres ? null : c.Il)
        .Alan("ilce", c => c.AnonimAdres ? null : c.Ilce)
        .Alan("kaynak", c => c.Kaynak)
        .Alan("sinif", c => c.Sinif)
        .Alan("vadeGun", c => c.VadeGun)
        .Alan("musteriTemsilcisi", c => c.MusteriTemsilcisi)
        .Alan("olusturma", c => c.CreatedAtUtc);

    /// <summary>Blazor <c>CustomerList</c> süzgeçleri. <c>q</c>: ad/soyad/ünvan/vergi no içinde; TC yalnız TAM 11 hane.</summary>
    public sealed class CustomerListFilter
    {
        [FromQuery(Name = "q")] public string? Q { get; set; }
        /// <summary><c>Bireysel</c> | <c>Kurumsal</c> | <c>Servis</c>.</summary>
        [FromQuery(Name = "tip")] public string? Tip { get; set; }
        [FromQuery(Name = "iysIzinli")] public bool? IysIzinli { get; set; }
        [FromQuery(Name = "uyari")] public bool? Uyari { get; set; }
        [FromQuery(Name = "karaListe")] public bool? KaraListe { get; set; }
        [FromQuery(Name = "pasif")] public bool? Pasif { get; set; }
        [FromQuery(Name = "aracVerilmez")] public bool? AracVerilmez { get; set; }
    }

    private static async Task<Ok<Sayfa<CustomerListRow>>> List(
        [AsParameters] CustomerListFilter f, CustomerService customers, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var request = new ListeIstegi(Math.Min(sayfa ?? 1, MaxPage), boyut ?? 50, sirala);
        var q = F5Ortak.Nz(f.Q);
        if (q is { Length: > 100 }) throw new ValidationException("Arama metni en fazla 100 karakter olabilir.", "q");
        var filter = new CustomerFilter
        {
            Query = q, Tip = F5Ortak.EnumAdi<CariType>(f.Tip, "tip"), IysIzinli = f.IysIzinli, Uyari = f.Uyari,
            KaraListe = f.KaraListe, Pasif = f.Pasif, AracVerilmez = f.AracVerilmez,
            Page = request.Sayfa, PageSize = request.Boyut,
        };
        if (request.Sirala is { } s)
        {
            SortMap.Uygula(Array.Empty<Customer>().AsQueryable(), s); // bilinmeyen alan → 400 errors[sirala], sorgudan ÖNCE
            filter.Siralama = x => SortMap.Uygula(x, s);
        }
        var result = await customers.SearchRowsAsync(filter, ct);
        var rows = result.Items.Select(Row).ToList();
        return TypedResults.Ok(new Sayfa<CustomerListRow>(rows, result.Total, request.Sayfa, request.Boyut));
    }

    /// <summary>Satır: TC YOK; görünen ad/telefon/e-posta/adres <see cref="MusteriGorunumu"/> kuralıyla.</summary>
    private static CustomerListRow Row(CustomerRow r)
    {
        var view = new Customer
        {
            Tip = r.Tip, CepTel = r.CepTel, Email = r.Email, AnonimAd = r.AnonimAd, AnonimTelefon = r.AnonimTelefon,
            AnonimMail = r.AnonimMail,
        };
        var name = r.AnonimAd ? MusteriGorunumu.AnonimAdEtiketi : r.DisplayName;
        return new CustomerListRow(
            r.Id, r.Tip.ToString(), name, r.AnonimAd, r.Tip == CariType.Bireysel ? null : r.VergiNo,
            MusteriGorunumu.Telefon(view), r.AnonimTelefon ? null : r.Gsm2, MusteriGorunumu.Eposta(view),
            r.AnonimAdres ? null : r.Il, r.AnonimAdres ? null : r.Ilce, r.Kaynak, r.MusteriTemsilcisi, r.EntegrasyonKodu,
            r.OzelKod, r.Sinif, r.Ulke, r.VadeGun, r.KiraAdet, r.Ciro, r.SonKira, r.KaraListe, r.Pasif, r.Uyari,
            r.UyariNedeni, r.IysIzinli, r.AracVerilmez);
    }

    /// <summary>Seç-veya-yaz il/ilçe önerisi: kayıtlarda geçen değerler (adresi anonimleştirilmiş cari HARİÇ).</summary>
    private static async Task<IReadOnlyList<string?>> AddressValuesAsync(
        IDbContextFactory<AppDbContext> dbf, System.Linq.Expressions.Expression<Func<Customer, string?>> field, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking().Where(c => !c.AnonimAdres).Select(field).Distinct().Take(2000).ToListAsync(ct);
    }

    // ================================================================== detay

    /// <summary>
    /// Cari 360°: özet (KVKK kuralıyla) + kiralar (ŞUBE KAPSAMINA süzülür — DetailService süzmez) + bakiye/son hareketler
    /// (yalnız FinanceWrite ya da ViewReports; operatöre firma geneli cari defteri açılmaz).
    /// </summary>
    private static async Task<Results<Ok<CustomerDetailView>, ProblemHttpResult>> Detail(
        Guid id, DetailService details, ICurrentUser user, CancellationToken ct)
    {
        var d = await details.GetCustomerAsync(id, ct);
        if (d is null) return NotFound();
        var filter = BranchScope.EffectiveFilter(user);
        var rentals = d.Rentals.Where(r => BranchScope.InScope(filter, r.CikisSubeId, r.CikisOfisi))
            .Select(r => new CustomerRentalSummary(r.Id, r.SozlesmeNo, r.BasTar, r.BitTar, r.Durum.ToString(), r.GenelToplam,
                r.Bakiye, r.Doviz))
            .ToList();
        var finance = EffectivePermission.Has(user, Permission.FinanceWrite) || EffectivePermission.Has(user, Permission.ViewReports);
        var c = d.Customer;
        return TypedResults.Ok(new CustomerDetailView(
            MusteriGorunumu.Ozet(c), c.KaraListe, c.Pasif, c.AracVerilmez, rentals,
            finance ? d.Bakiye : null,
            finance
                ? d.RecentLedger.Select(e => new CustomerLedgerLine(e.EntryDateUtc, e.SourceType, e.Description,
                    e.Direction == LedgerDirection.Debit ? e.Amount.AmountInBase : null,
                    e.Direction == LedgerDirection.Credit ? e.Amount.AmountInBase : null)).ToList()
                : null));
    }

    // ================================================================== silme

    /// <summary>Kalıcı silme (OperationsDelete). Kirada kullanılan cari (servis kuralı) ya da FK'li kayıt → 400.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, CustomerService customers, CancellationToken ct)
    {
        if (await customers.GetVersionAsync(id, ct) is null) return NotFound();
        try
        {
            return await customers.DeleteAsync(id, ct) ? TypedResults.NoContent() : NotFound();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ValidationException("Cari başka kayıtlarda kullanıldığı için silinemez; pasif işaretleyin.");
        }
    }
}
