using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR #295 H1/M1 çiti: tür çevirmesi gizli (bireysel) vergi no'yu açığa çıkarmaz; 11 haneli tamamı rakam vergi no
/// (eski kayıtta VergiNo'ya yazılmış TC) tipten bağımsız hiçbir yanıtta düz dönmez ve kısmi aranamaz; operatörün cari
/// detayında bakiye ve başka şubenin kirası yok.
/// </summary>
public sealed partial class UiCustomerApiTests
{
    [Fact]
    public async Task R295_P3_type_flip_without_tax_number_is_rejected_and_never_unmasks()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        const string tax = "9123456780";
        var (card, _) = await Json(await Send(admin, HttpMethod.Post, Customers,
            new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Vedat", ["vergiNo"] = tax }), HttpStatusCode.Created);
        var id = card.GetProperty("id").GetGuid();
        var flip = new Dictionary<string, object?>
        { ["tip"] = "Kurumsal", ["unvan"] = "X Ltd", ["surum"] = card.GetProperty("surum").GetString() };

        var raw = await Problem(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", flip), HttpStatusCode.BadRequest,
            "dogrulama", "vergiNo");
        Assert.DoesNotContain(tax, raw);
        Assert.Equal(CariType.Bireysel, await ReadAsync(e.TenantId, db => db.Customers.Where(c => c.Id == id).Select(c => c.Tip).SingleAsync()));
        var (search, rawSearch) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}?q={tax[..8]}"));
        Assert.Empty(Records(search));
        Assert.DoesNotContain(tax, rawSearch);

        // Yeni vergi no ile çevirme geçer; yeni (kurumsal, 10 hane) değer düz görünür.
        flip["vergiNo"] = "1234567890";
        var (ok, _) = await Json(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", flip));
        Assert.Equal("Kurumsal", ok.GetProperty("tip").GetString());
        Assert.Equal("1234567890", ok.GetProperty("vergiNo").GetString());
    }

    [Fact]
    public async Task R295_P3b_legacy_tc_in_tax_number_never_returned_plain_for_any_type()
    {
        var e = await SetupAsync();
        var opA = await LoginAsync(e, Who.OperatorA);
        var tc = RandomTc();
        var legacy = new Customer { Tip = CariType.Bireysel, Ad = "Eski", VergiNo = tc };
        var corporate = new Customer { Tip = CariType.Kurumsal, Unvan = "Eski Kurum " + Marker(), VergiNo = RandomTc() };
        await WriteAsync(e.TenantId, db => db.Customers.AddRange(legacy, corporate));

        var (card, raw0) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}/{legacy.Id}"));
        Assert.DoesNotContain(tc, raw0);
        var raw = await Problem(await Send(opA, HttpMethod.Put, $"{Customers}/{legacy.Id}", new Dictionary<string, object?>
        { ["tip"] = "Kurumsal", ["unvan"] = "Eski Ltd", ["surum"] = card.GetProperty("surum").GetString() }),
            HttpStatusCode.BadRequest, "dogrulama", "vergiNo");
        Assert.DoesNotContain(tc, raw);

        // Kurumsal kayıtta 11 haneli rakam vergi no: kart maskeli, liste null, kısmi arama eşleşmez.
        var corpTax = corporate.VergiNo!;
        var (corpCard, corpRaw) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}/{corporate.Id}"));
        Assert.DoesNotContain(corpTax, corpRaw);
        Assert.Equal(JsonValueKind.Null, corpCard.GetProperty("vergiNo").ValueKind);
        Assert.EndsWith(corpTax[^4..], corpCard.GetProperty("vergiNoMaske").GetString());
        var (list, listRaw) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}?tip=Kurumsal"));
        Assert.Contains(Records(list), r => r.GetProperty("id").GetGuid() == corporate.Id);
        Assert.DoesNotContain(corpTax, listRaw);
        var (search, _) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}?q={corpTax[..8]}"));
        Assert.Empty(Records(search));

        // Kartın round-trip PUT'u (vergiNo null) saklı değeri korur.
        await Json(await Send(opA, HttpMethod.Put, $"{Customers}/{corporate.Id}", new Dictionary<string, object?>
        { ["tip"] = "Kurumsal", ["unvan"] = corporate.Unvan, ["surum"] = corpCard.GetProperty("surum").GetString() }));
        Assert.Equal(corpTax, await ReadAsync(e.TenantId, db => db.Customers.Where(c => c.Id == corporate.Id).Select(c => c.VergiNo).SingleAsync()));
    }

    [Fact]
    public async Task R295_P4_operator_detail_has_no_balance_and_no_foreign_branch_rental()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        var cari = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Bakiyeli" });
        var rB = await RentalAsync(e, cari, "SubeB");
        var marker = "SUBEB-" + rB.ContractNo;
        await WriteAsync(e.TenantId, db =>
        {
            var src = Guid.NewGuid();
            db.AccountLedgerEntries.Add(new AccountLedgerEntry { AccountType = LedgerAccountType.Cari, AccountRef = cari,
                Direction = LedgerDirection.Debit, Amount = new Money(750m, "TRY", 1m), Description = marker, SourceType = "Fatura", SourceId = src });
            db.AccountLedgerEntries.Add(new AccountLedgerEntry { AccountType = LedgerAccountType.Gelir,
                Direction = LedgerDirection.Credit, Amount = new Money(750m, "TRY", 1m), Description = marker, SourceType = "Fatura", SourceId = src });
        });

        var (detail, rawDetail) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}/{cari}/detay"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("bakiye").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("hareketler").ValueKind);
        Assert.Empty(detail.GetProperty("kiralar").EnumerateArray());
        Assert.DoesNotContain(marker, rawDetail);

        // Admin (finans görür) aynı detayda 750 borçlu bakiyeyi ve SubeB kirasını görür.
        var (full, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{cari}/detay"));
        Assert.Equal(750m, full.GetProperty("bakiye").GetDecimal());
        Assert.Single(full.GetProperty("kiralar").EnumerateArray());

        // Karar (5), 2026-09-25: ekstre ucu da FinanceWrite ∨ ViewReports ister — operatör 403 alır, bakiye sızmaz.
        var statement = await Send(opA, HttpMethod.Get, $"{V1}/finans/cariler/{cari}/ekstre");
        Assert.Equal(HttpStatusCode.Forbidden, statement.StatusCode);
        Assert.DoesNotContain(marker, await statement.Content.ReadAsStringAsync());
    }
}
