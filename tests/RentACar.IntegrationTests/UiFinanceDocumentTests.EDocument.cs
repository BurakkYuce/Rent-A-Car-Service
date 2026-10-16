using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Finance;

namespace RentACar.IntegrationTests;

/// <summary>
/// e-Belge durumu ve eylemleri GERÇEK Web boru hattında — bu sürümün üretim gerçeği: kayıtlı entegratör adaptörü
/// YOK. Beklenen: fatura kesilir, e-belge "Taslak — bağlı değil" kalır, gönderime alma dürüstçe reddedilir, ETTN
/// "gönderildi" gibi gösterilmez. Beklenen cümleler sabit metinlerdir (servis kodundan türetilmez).
/// </summary>
public sealed partial class UiFinanceDocumentTests
{
    private const string NotConnected = "e-Fatura gönderilmedi: entegratör bağlı değil (entegratör seçilmedi).";

    [Fact]
    public async Task Edocument_status_is_honest_draft_in_detail_and_list_and_money_is_unchanged()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 333.33m), NewKey()));

        var detail = await Ok(await GetAsync(s, $"/faturalar/{id}"));
        var ebelge = detail.GetProperty("eBelge");
        Assert.Equal("Taslak", ebelge.GetProperty("durum").GetString());
        Assert.Equal(NotConnected, ebelge.GetProperty("mesaj").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, ebelge.GetProperty("ettn").ValueKind); // gönderilmedi → ETTN gösterilmez
        Assert.False(ebelge.GetProperty("entegratorBagli").GetBoolean());
        Assert.False(ebelge.GetProperty("gonderilebilir").GetBoolean());
        Assert.True(ebelge.GetProperty("iptalEdilebilir").GetBoolean());
        Assert.False(detail.GetProperty("eFaturaGonderildi").GetBoolean());
        Assert.Equal(400.00m, detail.GetProperty("genelToplam").GetDecimal()); // 333,33 + 66,67 (elle)

        var row = (await Ok(await GetAsync(s, "/faturalar"))).GetProperty("kayitlar").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("Taslak", row.GetProperty("eBelgeDurum").GetString());
        Assert.Equal(NotConnected, row.GetProperty("eBelgeMesaj").GetString());

        // Gönderime alma: bağlı değil → 400, durum değişmez; defter aynı.
        var problem = await Problem(await PostAsync(s, $"/faturalar/{id}/e-belge/gonder", null), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(NotConnected, problem.GetProperty("detail").GetString());
        Assert.Equal("Taslak", (await Ok(await GetAsync(s, $"/faturalar/{id}"))).GetProperty("eBelge").GetProperty("durum").GetString());
        Assert.Equal(400.00m, await CustomerBalanceAsync(e, e.Customer));
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Edocument_actions_require_permissions_and_cancel_needs_a_reason()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 100m), NewKey()));

        var plain = await LoginAsync(e, Who.OperatorPlain); // FinanceWrite yok
        await Problem(await PostAsync(plain, $"/faturalar/{id}/e-belge/gonder", null), HttpStatusCode.Forbidden, "yetki_yok");
        var noReverse = await LoginAsync(e, Who.AccountantNoReverse);
        await Problem(await PostAsync(noReverse, $"/faturalar/{id}/e-belge/iptal", new { gerekce = "x" }), HttpStatusCode.Forbidden, "yetki_yok");

        await Problem(await PostAsync(s, $"/faturalar/{id}/e-belge/iptal", new { gerekce = " " }), HttpStatusCode.BadRequest, "dogrulama", "gerekce");
        await Problem(await PostAsync(s, $"/faturalar/{id}/e-belge/iptal", new { gerekce = new string('a', 257) }), HttpStatusCode.BadRequest, "dogrulama", "gerekce");

        var cancelled = await Ok(await PostAsync(s, $"/faturalar/{id}/e-belge/iptal", new { gerekce = "Kağıt fatura kesildi" }));
        Assert.Equal("Iptal", cancelled.GetProperty("durum").GetString());
        Assert.Equal("e-Belge gönderilmeden iptal edildi: Kağıt fatura kesildi", cancelled.GetProperty("mesaj").GetString());
        Assert.False(cancelled.GetProperty("iptalEdilebilir").GetBoolean());
        Assert.Equal(120.00m, await CustomerBalanceAsync(e, e.Customer)); // e-belge iptali faturayı/defteri GERİ ALMAZ
    }

    /// <summary>Adversarial M1 (Web yolu): iade, kaynağın gönderilmemiş e-belgesini aynı işlemde iptal eder; detay
    /// "iptal" gösterir, gönderime alma ve iptal düğmeleri kapanır, gönderime alma ucu da reddeder.</summary>
    [Fact]
    public async Task Refund_cancels_the_unsent_source_edocument_and_closes_its_actions()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 100m), NewKey()));
        await Ok(await PostAsync(s, $"/faturalar/{id}/iade", null));

        var ebelge = (await Ok(await GetAsync(s, $"/faturalar/{id}"))).GetProperty("eBelge");
        Assert.Equal("Iptal", ebelge.GetProperty("durum").GetString());
        Assert.Equal("Fatura iade edildi; e-belge gönderilmeden iptal edildi.", ebelge.GetProperty("mesaj").GetString());
        Assert.False(ebelge.GetProperty("gonderilebilir").GetBoolean());
        Assert.False(ebelge.GetProperty("iptalEdilebilir").GetBoolean());
        await Problem(await PostAsync(s, $"/faturalar/{id}/e-belge/gonder", null), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(0m, await CustomerBalanceAsync(e, e.Customer)); // 120 fatura − 120 iade
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Edocument_actions_check_branch_scope_before_state_and_other_tenant_gets_404()
    {
        var e = await SetupAsync();
        // SubeA kirasının faturası (servis yolu).
        var id = await ReadAsync(e.TenantId, sp => sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(e.Rental));

        var otherBranch = await LoginAsync(e, Who.OperatorB); // SubeB + FinanceWrite istisnası
        await Problem(await PostAsync(otherBranch, $"/faturalar/{id}/e-belge/gonder", null), HttpStatusCode.Forbidden, "yetki_yok");
        var ownBranch = await LoginAsync(e, Who.OperatorA);
        // Kapsam içi: kapsam geçildi, durum kuralı konuşur (bağlı değil → 400).
        await Problem(await PostAsync(ownBranch, $"/faturalar/{id}/e-belge/gonder", null), HttpStatusCode.BadRequest, "dogrulama");

        var other = await SetupAsync();
        var stranger = await LoginAsync(other, Who.Accountant);
        await Problem(await PostAsync(stranger, $"/faturalar/{id}/e-belge/gonder", null), HttpStatusCode.NotFound, null);
        await Problem(await PostAsync(stranger, $"/faturalar/{id}/e-belge/iptal", new { gerekce = "x" }), HttpStatusCode.NotFound, null);
    }
}
