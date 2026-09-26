using System.Diagnostics.Metrics;

namespace RentACar.Application.Observability;

/// <summary>
/// İş metrikleri — TEK <c>Meter "RentACar"</c>. Application katmanında (BCL Meter, ASP.NET bağımlılığı yok)
/// → hem Infrastructure (LoginService/CashRepository) hem host'lar (Web/Api) emit edebilir. OTel host'ta
/// <c>.AddMeter("RentACar")</c> ile toplanır.
/// KARDİNALİTE KURALI: etiketler YALNIZ düşük-kardinalite (result=success/fail, job-adı). tenant/kullanıcı/
/// plaka gibi sınırsız değerler ASLA etiket olmaz (Prometheus kardinalite patlaması) — onlar log/trace'te.
/// </summary>
public static class RacarMetrics
{
    public static readonly Meter Meter = new("RentACar", "1.0.0");

    private static readonly Counter<long> LoginTotal =
        Meter.CreateCounter<long>("racar.login.total", description: "Giriş denemeleri (result=success|fail).");
    private static readonly Counter<long> CollectionTotal =
        Meter.CreateCounter<long>("racar.tahsilat.total", description: "Tahsilat işlemleri (result=ok|fail).");
    private static readonly Counter<long> LedgerIdempotentReject =
        Meter.CreateCounter<long>("racar.ledger.idempotent_reject.total", description: "Defter çift-gönderim (idempotent) reddi.");
    private static readonly Counter<long> RateLimitReject =
        Meter.CreateCounter<long>("racar.ratelimit.reject.total", description: "Rate-limit reddi (policy etiketi).");
    private static readonly Counter<long> JobFail =
        Meter.CreateCounter<long>("racar.job.fail.total", description: "Arka plan job hatası (job etiketi).");

    public static void LoginSuccess() => LoginTotal.Add(1, new KeyValuePair<string, object?>("result", "success"));
    public static void LoginFail() => LoginTotal.Add(1, new KeyValuePair<string, object?>("result", "fail"));
    public static void CollectionOk() => CollectionTotal.Add(1, new KeyValuePair<string, object?>("result", "ok"));
    public static void CollectionFail() => CollectionTotal.Add(1, new KeyValuePair<string, object?>("result", "fail"));
    public static void LedgerIdempotentRejected() => LedgerIdempotentReject.Add(1);
    public static void RateLimitRejected(string policy) => RateLimitReject.Add(1, new KeyValuePair<string, object?>("policy", policy));
    public static void JobFailed(string job) => JobFail.Add(1, new KeyValuePair<string, object?>("job", job));
}
