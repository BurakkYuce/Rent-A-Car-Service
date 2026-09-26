using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — branch master (<c>/subeler</c>, Blazor <c>BranchList</c>): ManageUsers like the page and the Blazor
/// group (branch administration is tenant configuration, so no branch scope — admins see every branch).
/// Generic CRUD + branch-specific free services (<c>/subeler/{id}/hizmetler</c>) + merge (<c>/subeler/birlestir</c>,
/// preview first; irreversible, requires <c>onay: true</c>). A branch still referenced (composite FK) cannot be
/// deleted — the repository returns a clear message; deactivate or merge instead.
/// </summary>
public static class BranchApi
{
    private const string Tag = "Şubeler";

    public static void MapBranchApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapDefinition(new DefinitionRoute<Branch, BranchDto, BranchRequest>
        {
            Path = "/subeler", Tag = Tag, Permission = Permission.ManageUsers,
            List = (sp, ct) => Svc(sp).ListAsync(ct),
            Get = (sp, id, ct) => Svc(sp).GetAsync(id, ct),
            GetVersion = (sp, id, ct) => Svc(sp).GetVersionAsync(id, ct),
            GetVersions = (sp, ct) => Svc(sp).GetVersionsAsync(ct),
            Create = async (sp, b, ct) => await Svc(sp).CreateAsync(await InputOfAsync(sp, b, ct), ct),
            Update = async (sp, id, b, v, ct) => await Svc(sp).UpdateAsync(id, await InputOfAsync(sp, b, ct), v, ct),
            Delete = (sp, id, ct) => Svc(sp).DeleteAsync(id, ct),
            IdOf = e => e.Id,
            ToDto = BranchDto.From,
            Sort = BranchDto.Sort,
            SearchText = r => [r.Kod, r.Ad, r.Il, r.Ilce, r.Yetkili],
            ValidateLimits = Limits,
            FieldRules = [("Şube kodu", "kod"), ("'", "kod"), ("Şube adı", "ad")],
            NotFoundMessage = "Şube bulunamadı.",
        });

        g.MapGet("/{id:guid}/hizmetler", async Task<Results<Ok<IReadOnlyList<BranchServiceDto>>, ProblemHttpResult>> (Guid id, BranchService s, CancellationToken ct)
            => await s.GetAsync(id, ct) is null
                ? NotFound()
                : TypedResults.Ok<IReadOnlyList<BranchServiceDto>>((await s.ListServicesAsync(id, ct))
                    .Select(h => new BranchServiceDto(h.Id, h.HizmetAdi, h.Aciklama)).ToList()));

        g.MapPost("/{id:guid}/hizmetler", async Task<Results<Created<BranchServiceDto>, ProblemHttpResult>> (Guid id, BranchServiceRequest b, BranchService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return NotFound();
            Sinirlar.Metin(b.HizmetAdi, 128, "hizmetAdi", "Hizmet adı");
            Sinirlar.Metin(b.Aciklama, 512, "aciklama", "Açıklama");
            var hid = await s.AddServiceAsync(new SubeUcretsizHizmetInput { SubeId = id, HizmetAdi = b.HizmetAdi ?? "", Aciklama = b.Aciklama }, ct);
            var row = (await s.ListServicesAsync(id, ct)).First(h => h.Id == hid);
            return TypedResults.Created($"{UiApiExtensions.V1}/subeler/{id}/hizmetler/{hid}", new BranchServiceDto(row.Id, row.HizmetAdi, row.Aciklama));
        }).AlanlariEsle([("Hizmet adı", "hizmetAdi")]);

        // The service id alone identifies the row; it must belong to the branch in the path (no cross-branch delete).
        g.MapDelete("/{id:guid}/hizmetler/{hizmetId:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, Guid hizmetId, BranchService s, CancellationToken ct)
            => (await s.ListServicesAsync(id, ct)).Any(h => h.Id == hizmetId) && await s.RemoveServiceAsync(hizmetId, ct)
                ? TypedResults.NoContent()
                : F5Ortak.Bulunamadi("Hizmet bulunamadı."));

        g.MapGet("/birlestir/onizleme", async Task<Results<Ok<BranchMergePreviewDto>, ProblemHttpResult>> (Guid kaynakId, Guid hedefId, BranchService s, CancellationToken ct)
            => await s.PreviewMergeAsync(kaynakId, hedefId, ct) is { } p
                ? TypedResults.Ok(new BranchMergePreviewDto(p.KaynakAd, p.HedefAd,
                    p.Etkilenen.Select(e => new BranchMergeCountDto(e.Tablo, e.Adet)).ToList(), p.Toplam))
                : NotFound());

        g.MapPost("/birlestir", async Task<Ok<BranchMergeResultDto>> (BranchMergeRequest b, BranchService s, CancellationToken ct) =>
        {
            if (b.Onay is not true) throw new ValidationException("Birleştirme geri alınamaz; onaylayın.", "onay");
            return TypedResults.Ok(new BranchMergeResultDto(await s.MergeAsync(b.KaynakId ?? Guid.Empty, b.HedefId ?? Guid.Empty, ct)));
        }).AlanlariEsle([("Kaynak ve hedef şube farklı", "hedefId"), ("Kaynak ve hedef şube seçilmelidir", "kaynakId"),
            ("Kaynak şube", "kaynakId"), ("Hedef şube", "hedefId")]);
    }

    private static BranchService Svc(IServiceProvider sp) => sp.GetRequiredService<BranchService>();

    private static ProblemHttpResult NotFound() => F5Ortak.Bulunamadi("Şube bulunamadı.");

    /// <summary>Linked cash/bank accounts must exist in THIS tenant (RLS-scoped lookup) and be of the right kind.</summary>
    private static async Task<BranchInput> InputOfAsync(IServiceProvider sp, BranchRequest b, CancellationToken ct)
    {
        var accounts = sp.GetRequiredService<FinancialAccountService>();
        await AccountAsync(accounts, b.NakitHesapId, Domain.Enums.LedgerAccountType.Kasa, "nakitHesapId", "Nakit hesabı", ct);
        await AccountAsync(accounts, b.BankaHesapId, Domain.Enums.LedgerAccountType.Banka, "bankaHesapId", "Banka hesabı", ct);
        return b.ToInput();
    }

    private static async Task AccountAsync(FinancialAccountService s, Guid? id, Domain.Enums.LedgerAccountType kind,
        string field, string label, CancellationToken ct)
    {
        if (id is not { } v) return;
        var a = await s.GetAsync(v, ct) ?? throw new ValidationException($"{label} bulunamadı.", field);
        if (AccountResolver.ResolveType(a.Tur) != kind)
            throw new ValidationException($"{label} {kind} türünde olmalıdır.", field);
    }

    private static void Limits(BranchRequest r)
    {
        Sinirlar.Metin(r.Kod, 32, "kod", "Kod");
        Sinirlar.Metin(r.Ad, 128, "ad", "Ad");
        Sinirlar.Metin(r.Adres, 512, "adres", "Adres");
        Sinirlar.Metin(r.Telefon, 32, "telefon", "Telefon");
        Sinirlar.Metin(r.Eposta, 128, "eposta", "E-posta");
        Sinirlar.Metin(r.Il, 64, "il", "İl");
        Sinirlar.Metin(r.Ilce, 64, "ilce", "İlçe");
        Sinirlar.Metin(r.Yetkili, 128, "yetkili", "Yetkili");
        Sinirlar.Metin(r.CalismaSaatleri, 64, "calismaSaatleri", "Çalışma saatleri");
        Sinirlar.Metin(r.EvrakNoOnek, 16, "evrakNoOnek", "Evrak no öneki");
        Sinirlar.Metin(r.WebIsim, 128, "webIsim", "Web ismi");
        Sinirlar.Metin(r.FirmaUnvani, 256, "firmaUnvani", "Firma unvanı");
        Sinirlar.Metin(r.RezervasyonRengi, 7, "rezervasyonRengi", "Rezervasyon rengi");
        Sinirlar.Metin(r.WebOtoparkId, 64, "webOtoparkId", "Web otopark kimliği");
        Sinirlar.Metin(r.BayiCariKod, 64, "bayiCariKod", "Bayi cari kodu");
        Sinirlar.Metin(r.BayiOfisId, 64, "bayiOfisId", "Bayi ofis kimliği");
        Sinirlar.Metin(r.KomisyonHesabi, 32, "komisyonHesabi", "Komisyon hesabı");
        Sinirlar.Metin(r.OnlineRezId, 64, "onlineRezId", "Online rezervasyon kimliği");
        Sinirlar.Metin(r.SozlesmeNoFormati, 64, "sozlesmeNoFormati", "Sözleşme no formatı");
        Sinirlar.Metin(r.EntegrasyonKodu, 64, "entegrasyonKodu", "Entegrasyon kodu");
        Sinirlar.Metin(r.ResimDosyasi, 512, "resimDosyasi", "Resim dosyası");
        Sinirlar.Metin(r.HaftalikCalismaSaatleri, 1024, "haftalikCalismaSaatleri", "Haftalık çalışma saatleri");
        // Rates are FRACTIONS (0.10 = %10) like the Blazor form (min 0, max 1).
        if (r.KomisyonOran is < 0m or > 1m) throw new ValidationException("Komisyon oranı 0 ile 1 arasında olmalıdır.", "komisyonOran");
        if (r.HizmetKomisyonOran is < 0m or > 1m) throw new ValidationException("Hizmet komisyon oranı 0 ile 1 arasında olmalıdır.", "hizmetKomisyonOran");
        if (r.Enlem is < -90m or > 90m) throw new ValidationException("Enlem -90 ile 90 arasında olmalıdır.", "enlem");
        if (r.Boylam is < -180m or > 180m) throw new ValidationException("Boylam -180 ile 180 arasında olmalıdır.", "boylam");
        if (r.WebRezOncesiSaat is < 0 or > 8760) throw new ValidationException("Web rezervasyon öncesi saat 0 ile 8760 arasında olmalıdır.", "webRezOncesiSaat");
        if (r.WebSira is < 0 or > 100_000) throw new ValidationException("Web sırası 0 ile 100000 arasında olmalıdır.", "webSira");
    }
}
