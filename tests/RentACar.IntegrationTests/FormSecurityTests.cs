using Microsoft.AspNetCore.Builder;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap E2 — antiforgery ortam toggle. BAĞIMSIZ ORACLE: EnforceAntiforgery=false (dev/test) →
/// AntiforgeryByEnv DisableAntiforgery konvansiyonu ekler (gevşek); =true (prod) → eklemez (token zorunlu).
/// Birim test (DB yok). Web HTTP form akışı için harness yok — prod-enforce inşaca + derleme + 154 form
/// token'ı ile garanti; bu test toggle kararını kilitler.
/// </summary>
public sealed class FormSecurityTests
{
    private sealed class FakeConventionBuilder : IEndpointConventionBuilder
    {
        public int Count { get; private set; }
        public void Add(Action<EndpointBuilder> convention) => Count++;
        public void Finally(Action<EndpointBuilder> finallyConvention) => Count++;
    }

    [Fact]
    public void Dev_mode_disables_antiforgery()
    {
        FormSecurity.EnforceAntiforgery = false;
        var b = new FakeConventionBuilder();
        b.AntiforgeryByEnv();
        Assert.True(b.Count >= 1); // DisableAntiforgery bir konvansiyon ekler
    }

    [Fact]
    public void Prod_mode_keeps_antiforgery()
    {
        try
        {
            FormSecurity.EnforceAntiforgery = true;
            var b = new FakeConventionBuilder();
            b.AntiforgeryByEnv();
            Assert.Equal(0, b.Count); // enforce → DisableAntiforgery çağrılmaz
        }
        finally
        {
            FormSecurity.EnforceAntiforgery = false; // diğer testleri etkileme
        }
    }
}
