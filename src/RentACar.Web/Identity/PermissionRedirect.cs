namespace RentACar.Web.Identity;

/// <summary>
/// Cookie şemasının 401/403 yönlendirme hedefini ve girişten sonra kullanıcının nereye döneceğini belirler.
///
/// <para><b>Neden ayrı sınıf:</b> bu karar <c>Program.cs</c> içinde lambda olarak duruyordu ve
/// test edilemiyordu. Saf fonksiyona çıkarıldı → davranış birim testiyle kilitlenir.</para>
///
/// <para><b>Neden 403 artık <c>/login</c>'e GİTMEZ (canlı hata):</b> yetkisiz bir POST 403 üretiyor,
/// <c>AccessDeniedPath = "/login"</c> kullanıcıyı login'e atıyor, ama kullanıcı ZATEN giriş yapmış
/// olduğu için <c>Login.razor</c> onu uygulamaya — yani <c>/</c>'a — yönlendiriyordu. Sonuç: hiçbir
/// açıklama olmadan ana ekrana düşmek. Kullanıcının "durduk yere ana ekrana atıyor" şikayetinin
/// üç kök nedeninden biri buydu. Artık 403 → <c>/yetkisiz</c>: kullanıcı ne olduğunu görüyor.</para>
///
/// <para><b>Neden dönüş adresi (ReturnUrl) (canlı hata):</b> oturumu düşmüş kullanıcı bildirimden /
/// WhatsApp'tan gelen <c>/kiralar/…</c> bağlantısını açınca düz <c>/login</c>'e gidiyor, girişten
/// sonra da sabit <c>/vehicles</c>'a düşüyordu — derin bağlantı kayboluyordu. Artık GET isteğinin
/// asıl hedefi <c>ReturnUrl</c> olarak taşınıyor, varsayılan iniş de Panel (<c>/</c>). Dönüş adresi
/// kullanıcının tarayıcısından gelen GÜVENİLMEZ bir değer olduğu için <see cref="SafeReturn"/>
/// açık-yönlendirme (open redirect) çitidir; her kuralın gerekçesi orada.</para>
/// </summary>
public static class PermissionRedirect
{
    /// <summary>
    /// Dönüş adresinin sorgu/form alanı adı. ASP.NET'in kendi varsayılanı
    /// (<c>CookieAuthenticationDefaults.ReturnUrlParameter</c>) ile aynı tutuldu: tarayıcı geçmişinde,
    /// loglarda ve destek konuşmalarında tanıdık görünür; Türkçe ad bir şey kazandırmaz.
    /// </summary>
    public const string ReturnParameter = "ReturnUrl";

    /// <summary>Tenant girişinden sonraki varsayılan iniş: Panel.</summary>
    public const string Default = "/";

    private const string LoginPage = "/login";

    /// <summary>Platform alanı ayrı bir kabuk kullanır; oradaki 401/403 kendi login'ine gider.</summary>
    private static bool Platform(PathString path) => path.StartsWithSegments("/platform");

    /// <summary>401 (kimlik yok) hedefi.</summary>
    public static string LoginTarget(PathString path) => Platform(path) ? "/platform/login" : LoginPage;

    /// <summary>403 (kimlik var, yetki yok) hedefi.</summary>
    public static string UnauthorizedTarget(PathString path) => Platform(path) ? "/platform/login" : "/yetkisiz";

    /// <summary>
    /// 401 yönlendirmesinin TAM adresi: <see cref="LoginTarget"/> + (uygunsa) <c>?ReturnUrl=</c>.
    /// <c>OnRedirectToLogin</c> bunu çağırır.
    /// </summary>
    /// <param name="previousPage">
    /// İsteğin geldiği sayfanın YEREL yolu (aynı kökenli Referer'dan <see cref="SameOriginPath"/> ile
    /// çıkarılmış). Yalnız asıl hedef bir İNDİRME adresiyse kullanılır: o zaman kullanıcı girişten
    /// sonra indirmeye değil, indirmeyi başlattığı sayfaya döner.
    /// </param>
    public static string LoginRedirect(string method, PathString path, QueryString query, string? previousPage = null)
    {
        var target = LoginTarget(path);

        // Platform alanı kendi login'ine gider ve dönüş TAŞIMAZ: platform login'i ReturnUrl okumaz,
        // ayrıca tenant ile platform kabukları arasında dönüş adresi gezdirmek alan karıştırır.
        if (Platform(path)) return target;

        // YALNIZ GET: dönüş adresi girişten sonra tarayıcının GET ile açacağı bir SAYFA olmalı.
        // Oturumu düşmüş kullanıcının form gönderimi (POST /kiralar/kaydet vb.) challenge ürettiğinde
        // o uç adresi dönüş olarak alınsaydı giriş sonrası tarayıcı POST ucuna GET atar → 405/404;
        // form verisi zaten kaybolmuştur, yeniden gönderilemez. Bu durumda varsayılan Panel dürüst hedef.
        if (!HttpMethods.IsGet(method)) return target;

        // Kendi ürettiğimiz dönüş adresi de aynı çitten geçer: /auth/… gibi reddedilecek bir hedefi
        // URL'ye hiç koymayız. Hedef zaten Panel ise ?ReturnUrl=%2F gürültüsü de eklenmez.
        var original = path.ToUriComponent() + query.ToUriComponent();
        var returnInfo = SafeReturn(original);

        // İndirme adresi dönüş olamaz (GuvenliDonus reddeder, bkz. IndirmeAdresiMi); kullanıcı
        // indirmeyi başlattığı sayfaya döner — o sayfa bilinmiyorsa Panel'e. Önceki sayfa da aynı
        // çitten geçer (yabancı/indirme/login adresi yine Panel'e iner).
        if (returnInfo == Default && IsDownloadUrl(original))
            returnInfo = SafeReturn(previousPage);

        return returnInfo == Default
            ? target
            : target + QueryString.Create(ReturnParameter, returnInfo).ToUriComponent();
    }

    /// <summary>
    /// Hatalı girişte login sayfasına geri dönüş adresi: <c>/login?hata=1</c> + (uygunsa) dönüş.
    /// Dönüş korunmazsa kullanıcı şifresini bir kez yanlış yazdığında derin bağlantıyı kaybederdi.
    /// Yalnız <see cref="SafeReturn"/>'ten geçen değer geri yansıtılır.
    /// </summary>
    public static string InvalidLoginTarget(string? returnInfo) => ErrorTarget("1", returnInfo);

    /// <summary>
    /// Giriş hız sınırına takılan isteğin hedefi: <c>/login?hata=limit</c> + (uygunsa) dönüş. Hatalı
    /// girişle AYNI gerekçe: şifreyi birkaç kez yanlış yazıp sınıra takılan kullanıcı (aynı NAT
    /// arkasındaki ofiste daha sık) bir dakika sonraki doğru girişte derin bağlantısını kaybetmesin.
    /// </summary>
    public static string LimitTarget(string? returnInfo) => ErrorTarget("limit", returnInfo);

    private static string ErrorTarget(string error, string? returnInfo)
    {
        var safe = SafeReturn(returnInfo);
        var query = QueryString.Create("hata", error);
        if (safe != Default) query = query.Add(ReturnParameter, safe);
        return LoginPage + query.ToUriComponent();
    }

    /// <summary>
    /// Referer başlığı BİZİM sunucumuzu (aynı host[:port]) gösteriyorsa yolu + sorgusu (kodlanmış
    /// hâliyle); değilse <c>null</c>. Şema bilinçli karşılaştırılmaz: TLS'i vekilde sonlandıran
    /// kurulumda istek "http", Referer "https" görünebilir. Sonuç çağıranda yine
    /// <see cref="SafeReturn"/>'ten geçer.
    /// </summary>
    public static string? SameOriginPath(string? referer, HostString host)
    {
        if (string.IsNullOrEmpty(referer) || !host.HasValue) return null;
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return null;
        var same = string.Equals(u.Authority, host.Value, StringComparison.OrdinalIgnoreCase)
                   // Varsayılan port Referer'da yazılmaz ama Host başlığında yazılabilir (ör. ":443").
                   || ((host.Port is null or 80 or 443) && string.Equals(u.Host, host.Host, StringComparison.OrdinalIgnoreCase));
        return same ? u.PathAndQuery : null;
    }

    /// <summary>
    /// Adres, dosyayı EK olarak (<c>Content-Disposition: attachment</c>) dönen bir GET ucunu mu
    /// gösteriyor. Böyle bir adres girişten sonraki dönüş hedefi OLAMAZ (<see cref="SafeReturn"/>
    /// Kural 9).
    ///
    /// <para><b>Neden (adversarial bulgu):</b> oturumu düşmüş kullanıcı liste ekranında "Excel"e
    /// tıklıyor → <c>/login?ReturnUrl=/listeler/export/cariler</c> → giriş → 302 ile export'a
    /// gidiliyor → dosya iniyor ama tarayıcı BELGEYİ DEĞİŞTİRMEDİĞİ için ekranda yine giriş formu
    /// duruyor (menü yok). Kullanıcı girişin başarısız olduğunu sanıyor; sayfayı yenileyince
    /// <c>Login.razor</c> girişli kullanıcıyı yine dönüşe, yani indirmeye yolluyor → giriş ekranına
    /// hapsoluyor ve her denemede dosya yeniden iniyor. Eskiden sabit <c>/vehicles</c>'a gidildiği
    /// için bu durum yoktu; ReturnUrl ile geldi.</para>
    ///
    /// <para><b>Kapsam</b> (Web'de <c>Results.File</c> kullanan her tenant ucu —
    /// <c>GuvenliDonusTests</c> kaynak taraması yeni bir dosya ucunu burada gözden geçirmeye zorlar):
    /// <c>/listeler/export…</c> (liste + personel), <c>/raporlar/export/…</c>, son parçası
    /// <c>indir</c> olan belge/doküman indirmeleri ve <c>indir</c> sorgu ANAHTARI taşıyan PDF'ler
    /// (<c>/kiralar/{id}/pdf?indir=1</c> — anahtarsız hâli tarayıcıda GÖRÜNTÜLENİR, dönüş olabilir).
    /// Karşılaştırma kodlaması çözülmüş, büyük/küçük harf duyarsız segmentler üzerindedir.</para>
    /// </summary>
    public static bool IsDownloadUrl(string? address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        var end = address.AsSpan().IndexOfAny('?', '#');
        var path = ResolveFull(end < 0 ? address : address[..end]);
        if (path is null) return false;

        var s = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        static bool AreEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        if (s.Length >= 2 && AreEqual(s[0], "listeler") && s[1].StartsWith("export", StringComparison.OrdinalIgnoreCase))
            return true;
        if (s.Length >= 2 && AreEqual(s[0], "raporlar") && AreEqual(s[1], "export"))
            return true;
        if (s.Length >= 1 && AreEqual(s[^1], "indir"))
            return true;

        // Sorgu ANAHTARI "indir" (değeri değil — "?ara=indir" bir arama): /kiralar/{id}/pdf?indir=1,
        // /firma-belgeleri/{id}/indir?indir.
        if (end >= 0 && address[end] == '?')
        {
            var query = address[(end + 1)..].Split('#')[0];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var key = ResolveFull(part.Split('=')[0].Replace('+', ' '));
                if (key is not null && AreEqual(key.Trim(), "indir")) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Girişten sonra gidilecek adres: <paramref name="returnInfo"/> güvenli bir YEREL yolsa aynen döner,
    /// değilse <see cref="Default"/> (Panel). ASP.NET <c>IsLocalUrl</c>'in izinden gider ama ondan
    /// SIKIDIR (kodlanmış ayırıcı, nokta segmenti, döngü/alan kuralları ek).
    ///
    /// <para>Neden reddetmek, düzeltmekten iyi: dönüş adresi kolaylıktır, güvenlik değil. Şüpheli her
    /// değer için Panel'e inmek kullanıcıya en fazla bir tık kaybettirir; "onarılmış" bir adres ise
    /// tarayıcının bizden farklı yorumladığı bir kenar durumda açık yönlendirme olur.</para>
    /// </summary>
    public static string SafeReturn(string? returnInfo)
    {
        // Kural 1 — boş: gidilecek bir yer yoksa varsayılan Panel.
        if (string.IsNullOrEmpty(returnInfo)) return Default;

        // Kural 2 — yalnız yazdırılabilir ASCII (0x21–0x7E):
        //  * Kontrol karakterleri: tarayıcılar URL'den sekme/satır sonunu SESSİZCE SİLER →
        //    "/\t/evil.com" tarayıcıda "//evil.com" (başka alan adı) olur. CR/LF ayrıca başlık
        //    enjeksiyonu denemesidir.
        //  * Boşluk: tarayıcı baştaki/sondaki boşluğu kırpar; kırpılmış hali bizim denetlediğimiz
        //    değer olmaz.
        //  * ASCII dışı: Kestrel Location başlığında ASCII dışı karakteri reddeder → giriş ucu 500
        //    verirdi. Kendi ürettiğimiz dönüş adresi daima yüzde-kodlu (saf ASCII) olduğu için meşru
        //    bir bağlantı bu kuraldan etkilenmez.
        foreach (var c in returnInfo)
            if (c < '!' || c > '~') return Default;

        // Kural 3 — tek "/" ile başlamalı: "https://evil.com" (mutlak URL), "javascript:…" (şema),
        // "kiralar" (göreli yol — bulunulan sayfaya göre çözülür, öngörülemez) reddedilir.
        if (returnInfo[0] != '/') return Default;

        // Kural 4 — ters bölü HİÇBİR yerde olamaz: tarayıcılar http(s) adreslerinde "\"'yi "/" sayar
        // → "/\evil.com" = "//evil.com" = şema-göreli başka alan adı. Meşru bağlantılarımızda yok.
        if (returnInfo.Contains('\\')) return Default;

        // Aşağıdaki kurallar yalnız YOL kısmına bakar. Sorgu dizesi (?…) sayfanın kendi verisidir:
        // "/kiralar?ara=%2F%2Fx" gibi kodlanmış eğik çizgi orada zararsızdır ve meşru olabilir.
        var end = returnInfo.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? returnInfo : returnInfo[..end];

        // Kural 5 — kodlanmış ayırıcılar: yol, kodlaması tamamen çözülene kadar açılır ve kurallar o
        // ÇÖZÜLMÜŞ hal üzerinde işler. "/%2F%2Fevil.com" bugün aynı-kökenli bir yoldur, ama arada
        // bir kez daha çözen herhangi bir katman (vekil, çerçeve, sonraki bir yönlendirme) onu
        // "//evil.com"a çevirir. İç içe kodlama ("%252F") da aynı hileyi bir kat daha saklar.
        // 4 turda durulmayan kodlama meşru bir bağlantıda olmaz → ret.
        var resolved = ResolveFull(path);
        if (resolved is null) return Default;

        // Kural 6 — çözülmüş yol "//" veya "/\" ile başlayamaz (şema-göreli başka alan adı) ve kontrol
        // karakteri / ters bölü içeremez ("%09", "%0d%0a", "%5C" — Kural 2 ve 4'ün kodlanmış hali).
        if (resolved.Length > 1 && (resolved[1] == '/' || resolved[1] == '\\')) return Default;
        foreach (var c in resolved)
            if (char.IsControl(c) || c == '\\') return Default;

        // Kural 7 — nokta segmenti ("." / ".."; kodlanmışı "%2e" Kural 5'te açıldı) yok: tarayıcı
        // "/x/../login" adresini "/login"e indirger ve aşağıdaki döngü/alan kuralını atlatır. Kestrel
        // gelen istekte nokta segmentlerini zaten temizlediği için kendi ürettiğimiz dönüşte olmazlar.
        var segments = resolved.Split('/');
        foreach (var s in segments)
            if (s is "." or "..") return Default;

        // Kural 8 — döngü ve alan geçişi (ilk segment, büyük/küçük harf duyarsız — yönlendirme de öyle):
        //  * /login  → giriş sonrası login'e dönmek anlamsız; Login.razor girişli kullanıcıyı yeniden
        //              yönlendirir, dönüş zinciri döngüye girebilir.
        //  * /auth/… → POST-only kimlik uçları; GET 405 verir, /auth/logout ise yeni girişi düşürür.
        //  * /platform… → ayrı kabuk ve ayrı yetki (süper-admin). Tenant girişinin dönüşü oraya
        //              taşınmaz; platform alanı kendi login'ini kullanır.
        // Segment karşılaştırması bilinçli: "/platformlar" platform alanı DEĞİLDİR (Platform() ile
        // aynı tanım; YetkiYonlendirmeTests Platform()'u, GuvenliDonusTests bu kuralı kilitliyor).
        var first = segments.Length > 1 ? segments[1] : "";
        if (first.Equals("login", StringComparison.OrdinalIgnoreCase)
            || first.Equals("auth", StringComparison.OrdinalIgnoreCase)
            || first.Equals("platform", StringComparison.OrdinalIgnoreCase))
            return Default;

        // Kural 9 — indirme adresi dönüş olamaz: dosya ek olarak iner, tarayıcı belgeyi değiştirmez ve
        // kullanıcı giriş ekranında kalır (yenileyince Login.razor onu yine indirmeye yollar → tuzak).
        // Gerekçe ve kapsam: IndirmeAdresiMi. 401 yönlendirmesi bu durumda önceki sayfayı taşır.
        if (IsDownloadUrl(returnInfo)) return Default;

        // Kodlanmış hal (orijinal metin) döner: tarayıcı onu bir kez çözecek, bizim denetlediğimiz
        // çözülmüş hal de odur.
        return returnInfo;
    }

    /// <summary>
    /// Yüzde-kodlamayı sabit noktaya kadar açar. <see cref="Uri.UnescapeDataString(string)"/> geçersiz
    /// dizileri ("%zz") olduğu gibi bırakır, istisna atmaz. Durulmazsa <c>null</c>.
    /// </summary>
    private static string? ResolveFull(string s)
    {
        for (var i = 0; i < 4; i++)
        {
            var next = Uri.UnescapeDataString(s);
            if (next == s) return s;
            s = next;
        }
        return null;
    }
}
