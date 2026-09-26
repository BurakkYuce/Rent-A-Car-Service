using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Notifications;
using RentACar.Application.Search;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — <c>/mesaj-sablonlari</c> (ManageUsers; PUT tam değiştirme, zorunlu <c>surum</c>), <c>/bildirimler</c> ve
/// <c>/ara</c> (Blazor'da <c>[Authorize]</c> — oturum yeter; kiracı izolasyonu RLS, arama şube kapsamı serviste).
/// </summary>
public static partial class SystemAdminApi
{
    private static void MapMessages(RouteGroupBuilder v1)
    {
        var t = v1.MapGroup("/mesaj-sablonlari").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);

        t.MapGet("", async Task<Ok<IReadOnlyList<MessageTemplateDto>>> (CustomerNotificationService s, CancellationToken ct)
            => TypedResults.Ok(await TemplatesAsync(s, ct)));

        t.MapPut("/{tur}/{kanal}", async Task<Ok<MessageTemplateDto>> (string tur, string kanal, MessageTemplateRequest i,
            CustomerNotificationService s, CancellationToken ct) =>
        {
            var type = F5Shared.EnumAdi<MessageType>(tur, "tur") ?? throw new ValidationException("Şablon türü zorunludur.", "tur");
            var channel = F5Shared.EnumAdi<MessageChannel>(kanal, "kanal") ?? throw new ValidationException("Kanal zorunludur.", "kanal");
            RentalLimits.Text(i.Konu, 256, "konu", "Konu");
            RentalLimits.Text(i.Govde, 8192, "govde", "Gövde");
            // Satır varsa sürüm zorunlu (bayat sekme başka oturumun şablonunu sessizce ezmesin); yoksa ilk kayıt.
            if ((await TemplatesAsync(s, ct)).Any(x => x.Tur == type.ToString() && x.Kanal == channel.ToString() && x.Surum is not null))
                SystemApiCommon.RequireVersion(i.Surum);
            await s.SaveTemplateAsync(new MesajSablonInput { Tur = type, Kanal = channel, Konu = i.Konu, Govde = i.Govde ?? "", Aktif = i.Aktif },
                SystemApiCommon.Clean(i.Surum), ct);
            return TypedResults.Ok((await TemplatesAsync(s, ct)).First(x => x.Tur == type.ToString() && x.Kanal == channel.ToString()));
        }).MapFields([("Mesaj gövdesi", "govde"), ("SMS gövdesi", "govde"), ("E-posta şablonunda konu", "konu")]);

        // ---- bildirim merkezi
        var n = v1.MapGroup("/bildirimler").WithTags(SystemApiCommon.Tag)
            .PermissionExempt("Bildirim merkezi: oturum yeter (Blazor [Authorize] paritesi); kiracı izolasyonu RLS.");

        n.MapGet("", async Task<Ok<NotificationCenterDto>> (bool? okundu, InAppNotificationService s, CancellationToken ct) =>
        {
            var d = await s.GetAsync(ct: ct);
            var list = await s.ListPersistedAsync(okundu, ct);
            return TypedResults.Ok(new NotificationCenterDto(
                d.VadeGecmis, d.VadeYakin, d.AcikSikayet, d.DonemKapanis?.ToUniversalTime(),
                d.Vadeler.Select(v => new MessageDueItemDto(v.VehicleId, v.Tur, v.Bitis.ToUniversalTime(), v.KalanGun,
                    v.Bucket == DueBucket.Gecmis)).ToList(),
                d.Sikayetler.Select(x => new OpenComplaintDto(x.Id, x.Konu, x.Tarih.ToUniversalTime())).ToList(),
                list.Select(b => new NotificationDto(b.Id, b.Tur, b.Mesaj, b.VehicleId, b.VadeTarihi.ToUniversalTime(), b.Okundu,
                    b.OlusturmaTarihi.ToUniversalTime())).ToList(),
                await s.UnreadCountAsync(ct)));
        });

        n.MapPost("/{id:guid}/oku", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, InAppNotificationService s, CancellationToken ct)
            => await s.MarkReadAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound("Bildirim bulunamadı."));

        n.MapPost("/hepsini-oku", async Task<Ok<CountResult>> (InAppNotificationService s, CancellationToken ct)
            => TypedResults.Ok(new CountResult(await s.MarkAllReadAsync(ct))));

        // ---- genel arama (şube kapsamı SearchService'te)
        v1.MapGet("/ara", async Task<Ok<IReadOnlyList<SearchHitDto>>> (string? q, SearchService s, CancellationToken ct) =>
        {
            RentalLimits.Text(q, 100, "q", "Arama metni");
            return TypedResults.Ok<IReadOnlyList<SearchHitDto>>((await s.SearchAsync(q, ct))
                .Select(h => new SearchHitDto(h.Tur, h.Baslik, h.Alt, h.Url)).ToList());
        }).WithTags(SystemApiCommon.Tag)
          .PermissionExempt("Genel arama: oturum yeter (Blazor [Authorize] paritesi); şube kapsamı SearchService'te, kiracı RLS.");
    }

    /// <summary>Tüm (tür × kanal) birleşimleri döner; kayıtlı olmayanlar <c>Kayitli=false</c>, <c>Surum=null</c>.</summary>
    private static async Task<IReadOnlyList<MessageTemplateDto>> TemplatesAsync(CustomerNotificationService s, CancellationToken ct)
    {
        var versions = await s.TemplateVersionsAsync(ct);
        var rows = (await s.ListTemplatesAsync(ct)).ToDictionary(x => (x.Tur, x.Kanal));
        var result = new List<MessageTemplateDto>();
        foreach (var type in Enum.GetValues<MessageType>())
            foreach (var channel in Enum.GetValues<MessageChannel>())
                result.Add(rows.TryGetValue((type, channel), out var r)
                    ? new MessageTemplateDto(type.ToString(), channel.ToString(), true, r.Konu, r.Govde, r.Aktif, versions.GetValueOrDefault(r.Id))
                    : new MessageTemplateDto(type.ToString(), channel.ToString(), false, null, "", false, null));
        return result;
    }
}

public sealed record MessageTemplateDto(string Tur, string Kanal, bool Kayitli, string? Konu, string Govde, bool Aktif, string? Surum);

public sealed record MessageTemplateRequest(string? Konu, string? Govde, bool Aktif = true, string? Surum = null);

public sealed record NotificationCenterDto(int VadeGecmis, int VadeYakin, int AcikSikayet, DateTimeOffset? DonemKapanis,
    IReadOnlyList<MessageDueItemDto> Vadeler, IReadOnlyList<OpenComplaintDto> Sikayetler, IReadOnlyList<NotificationDto> Bildirimler, int Okunmamis);

/// <summary>Bildirim merkezi vade satırı. Ad <c>Api/ServiceInsurance</c>'taki <c>DueItemDto</c>'dan AYRI olmalı: OpenAPI
/// şema adları kısa addan üretilir, çakışınca <c>GET /vade</c> bu şekille belgeleniyordu (yapısal test kilitler).</summary>
public sealed record MessageDueItemDto(Guid AracId, string Tur, DateTimeOffset BitisUtc, int KalanGun, bool Gecmis);

public sealed record OpenComplaintDto(Guid Id, string Konu, DateTimeOffset TarihUtc);

public sealed record NotificationDto(Guid Id, string Tur, string Mesaj, Guid AracId, DateTimeOffset VadeTarihiUtc, bool Okundu, DateTimeOffset OlusturmaUtc);

public sealed record SearchHitDto(string Tur, string Baslik, string? Alt, string Url);
