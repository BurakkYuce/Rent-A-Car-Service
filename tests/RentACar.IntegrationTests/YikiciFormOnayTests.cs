using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// Yıkıcı POST formu onaysız kalamaz: hedef yolu silme/iptal/ters-kayıt/kaldırma fiili taşıyan
/// (ya da aşağıda adıyla listelenen geri alınamaz işlemlerden birine giden) her
/// <c>&lt;form method="post"&gt;</c> ve her <c>formaction</c>'lı gönder butonu, formda YA DA
/// gönderen butonda <c>data-confirm</c> taşımalı. Mesajı <c>rc-ui.js</c> gösterir (CSP
/// <c>script-src 'self'</c>: inline <c>onsubmit</c> yasak, onay öznitelikle bağlanır).
///
/// <para><b>Neden var:</b> bu tarayıcıyla ölçüldüğünde main'de (05e7ac5) 87 yıkıcı POST hedefinin
/// 24'ü onaysızdı (82'si fiil taşıyan yol, 5'i adıyla listelenen geri alınamaz işlem) — B2B dış
/// hizmet alımının ters kayıtla iptali (kira sözleşmesi finans paneli), araç kredisi/BAF/ceza
/// iptali, doküman ve araç fotoğrafı silme, dönem kapanışı, depozito irat… Tek yanlış tık, geri
/// dönüşü olmayan bir ters kayıt ya da kalıcı silme üretiyordu; kullanıcıya hiçbir şey
/// sorulmuyordu.</para>
///
/// <para><b>Neden etiket etiket tarama:</b> dosyanın tamamına tek regex (<c>&lt;form.*?&gt;</c>)
/// ÇALIŞMAZ. Öznitelik değerindeki <c>&gt;</c> (<c>@(a &gt; b ? … )</c>, mesaj metni) etiketi erken
/// bitirir; örtüşmeyen eşleşmeler komşu formu yutar ve onaysız formu "onaylı" komşusunun
/// içinde saklar. Bu repoda aynı tuzak uç-izin taramasında 17 ihlalin 3'e düşmesine yol açmıştı.
/// Bu yüzden her <c>&lt;form</c> tırnaklara ve Razor ifadelerine (<c>@(…)</c>, <c>@x.Y(…)</c>)
/// saygılı küçük bir ayrıştırıcıyla KENDİ sonuna kadar okunur; gövde bir sonraki
/// <c>&lt;/form&gt;</c>'a kadardır.</para>
///
/// <para><b>Fiilsiz ama onaylı işlemler:</b> yolu fiil taşımayan işlemlerin onayı (toplu fatura,
/// iade faturası, bakiye düzeltme, ücretli bildirim testleri…) <c>OnayZorunluYollar</c> listesiyle
/// ADIYLA kilitlenir; listedeki her yolun repoda gerçekten bir POST hedefi olduğu ayrıca denetlenir
/// (yol yeniden adlandırılınca çit sessizce boşa çıkmasın).</para>
///
/// <para><b>Kapsam dışı (bilerek):</b> GET formları (süzgeç/arama, tekrar gönderimi zararsız) ve
/// <c>type="button"</c> ile fetch'e giden butonlar (submit olayı üretmez; onay gerekiyorsa kendi
/// betiği sorar).</para>
/// </summary>
public sealed class YikiciFormOnayTests
{
    /// <summary>
    /// Ölçüm (2026-09-21, bu PR'da): Web bileşenlerinde 87 yıkıcı POST hedefi var (86 form + 1
    /// formaction'lı buton — Depozito ekranındaki İrat). Tarayıcı bozulur da HİÇBİR şey bulamazsa
    /// ana test sessizce yeşil kalırdı — bu alt sınır onu yakalar. Form gerçekten kaldırıldığı
    /// için sayı düşerse sabiti ölçerek güncelleyin; ASLA tarayıcının kendi sayımından türetmeyin.
    /// </summary>
    private const int MinDestructiveTarget = 87;

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx")))
            d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<(string Dosya, RazorFormScanner.Hedef Hedef)> WebTargets(string root)
        => Directory
            .EnumerateFiles(Path.Combine(root, "src/RentACar.Web/Components"), "*.razor", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => RazorFormScanner.Targets(File.ReadAllText(f))
                .Select(h => (Path.GetRelativePath(root, f), h)));

    [Fact]
    public void Yikici_post_formu_onay_ister()
    {
        var root = RepoRoot();
        var violations = WebTargets(root)
            .Where(x => x.Hedef.Post && RazorFormScanner.IsConfirmRequired(x.Hedef.Aksiyon) && !x.Hedef.Onayli)
            .Select(x => $"{x.Dosya}:{x.Hedef.Satir}  ({x.Hedef.Tur})  →  {x.Hedef.Aksiyon}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Yıkıcı POST hedefi onay sormuyor. <form>'a (ya da gönderen butona) data-confirm ekleyin; " +
            "mesaj NESNEYİ ve GERÇEK sonucu söylesin (kalıcı silme mi, pasife alma mı, ters kayıt mı), " +
            "\"Emin misiniz?\" yetmez. Önce ucun ve çağırdığı servisin ne yaptığını okuyun.\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void Tarayici_yikici_hedefleri_gercekten_buluyor()
    {
        // Alt-sınır: ayrıştırıcı bozulup formları göremez hale gelirse (ör. etiket sonu yanlış
        // bulunur, fiil listesi boşalır) yukarıdaki test "0 ihlal" diye yeşil kalırdı.
        var root = RepoRoot();
        var destructive = WebTargets(root)
            .Count(x => x.Hedef.Post && RazorFormScanner.IsDestructive(x.Hedef.Aksiyon));

        Assert.True(destructive >= MinDestructiveTarget,
            $"Tarayıcı yalnız {destructive} yıkıcı POST hedefi buldu, ölçülen en az {MinDestructiveTarget}. " +
            "Tarayıcı mı bozuldu, yoksa formlar gerçekten mi kaldırıldı? İkincisiyse sabiti ölçerek düşürün.");
    }

    [Fact]
    public void Adiyla_listelenen_her_yol_repoda_POST_hedefi_olarak_var()
    {
        // Listeler ADIYLA çalışır: uç yeniden adlandırılır ya da form taşınırsa listedeki yol hiçbir
        // hedefe denk gelmez ve çit o işlem için SESSİZCE boşa çıkar (onay silinse de yeşil kalır).
        // Bu test o kaymayı yakalar: listedeki her yol bugün en az bir POST hedefiyle eşleşmeli.
        var root = RepoRoot();
        var found = WebTargets(root)
            .Where(x => x.Hedef.Post)
            .SelectMany(x => RazorFormScanner.PathCandidates(x.Hedef.Aksiyon).Select(RazorFormScanner.NormalPath))
            .ToHashSet(StringComparer.Ordinal);

        var missing = RazorFormScanner.PathsListedByName.Where(y => !found.Contains(y)).ToList();

        Assert.True(missing.Count == 0,
            "Adıyla listelenen yol repoda hiçbir POST hedefiyle eşleşmiyor (uç yeniden adlandırıldı ya da " +
            "form kaldırıldı mı?). Listeyi yeni yola göre güncelleyin; silmeden önce işlemin gerçekten " +
            "kalktığını doğrulayın.\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Onay_zorunlu_yollar_listesi_bugun_onayli_hedeflerle_dolu()
    {
        // Canlı-kanıt kilidi: PR'ın düzelttiği yedi buton (toplu fatura, bakiye düzeltme, otomatik
        // tahsilat, gider ödemesi, ücretli WhatsApp/e-posta/SMS testleri) ve fatura iadesi gibi
        // FİİLSİZ yollar yalnız bu listeyle çitin içine girer. Liste boşalırsa ya da içinden biri
        // düşerse onları geri getiren hiçbir test kalmaz — elle yazılmış beklenen küme.
        var expected = new[]
        {
            "/finans/fatura-iade", "/finans/fatura-toplu", "/finans/bakiye-duzeltme",
            "/finans/otomatik-tahsilat/calistir", "/giderler/odeme",
            "/ayarlar/whatsapp-test", "/ayarlar/smtp-test", "/ayarlar/sms-test",
            "/subeler/birlestir", "/takvim/yenile", "/kiralar/{}/paylas-yeni",
        };
        foreach (var y in expected)
            Assert.True(RazorFormScanner.IsConfirmRequired(y.Replace("{}", "@Id", StringComparison.Ordinal)),
                $"{y} onay zorunlu sayılmıyor — listeden düşmüş.");

        // "iade" FİİL DEĞİL: depozito iadesi olağan bir postlama, bilerek kapsam dışı.
        Assert.False(RazorFormScanner.IsConfirmRequired("/depozito/iade"));
    }

    [Fact]
    public void Her_post_formunun_hedefi_statik_okunabilir()
    {
        // Çitin kör noktası: `action="@Url"` gibi yalnız değişkenden gelen hedef, fiil taşısa bile
        // taranamaz ve yıkıcı form çitten sessizce kaçar. Bugün böyle bir form yok; gelirse burada
        // durur ve hedef ya literal yazılır ya da bu test bilinçli genişletilir.
        var root = RepoRoot();
        var unreadable = WebTargets(root)
            .Where(x => x.Hedef.Post && !RazorFormScanner.PathCandidates(x.Hedef.Aksiyon).Any())
            .Select(x => $"{x.Dosya}:{x.Hedef.Satir}  ({x.Hedef.Tur})  →  {x.Hedef.Aksiyon ?? "(action yok)"}")
            .ToList();

        Assert.True(unreadable.Count == 0,
            "POST hedefi kaynak koddan okunamıyor; yıkıcı-form çiti bu formu denetleyemez.\n  " +
            string.Join("\n  ", unreadable));
    }

    [Fact]
    public void Onay_mesaji_jenerik_olamaz()
    {
        // "Emin misiniz?" NEYİN silindiğini ve NE olacağını söylemez (kalıcı silme mi, ters kayıt
        // mı, durum değişikliği mi); kullanıcı okumadan geçmeye alışır. Mesaj nesneyi ve sonucu
        // adlandırmalı.
        var root = RepoRoot();
        var found = Directory
            .EnumerateFiles(Path.Combine(root, "src/RentACar.Web/Components"), "*.razor", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => RazorFormScanner.ConfirmMessages(File.ReadAllText(f))
                .Where(RazorFormScanner.IsGeneric)
                .Select(m => $"{Path.GetRelativePath(root, f)}  →  \"{m}\""))
            .ToList();

        Assert.True(found.Count == 0,
            "Jenerik onay mesajı: nesneyi ve gerçek sonucu adlandırın (ör. \"X kalıcı olarak silinsin mi? " +
            "Geri alınamaz.\").\n  " + string.Join("\n  ", found));
    }

    // ---------------------------------------------------------------------------------------
    // Tarayıcının kendi doğruluğu — beklenen değerler ELLE yazılmış örneklerden.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Jenerik_mesaj_tanimi_nesneli_mesaji_yakalamaz()
    {
        Assert.True(RazorFormScanner.IsGeneric("Emin misiniz?"));
        Assert.True(RazorFormScanner.IsGeneric("  emin misiniz  "));
        Assert.True(RazorFormScanner.IsGeneric("Onaylıyor musunuz?"));
        // Tek fiilden ibaret mesaj da jeneriktir: NEYİN silindiğini/iptal edildiğini söylemez.
        Assert.True(RazorFormScanner.IsGeneric("Silinsin mi?"));
        Assert.True(RazorFormScanner.IsGeneric("İptal edilsin mi?"));
        Assert.True(RazorFormScanner.IsGeneric("Reddedilsin mi?"));
        Assert.True(RazorFormScanner.IsGeneric("Kaldırılsın mı?"));
        Assert.True(RazorFormScanner.IsGeneric("Devam?"));
        Assert.False(RazorFormScanner.IsGeneric("Marka silinsin mi?"));
        Assert.False(RazorFormScanner.IsGeneric("Kira iptal edilsin mi?"));
        Assert.False(RazorFormScanner.IsGeneric("Kayıt silinsin mi? Emin misiniz?"));
        Assert.False(RazorFormScanner.IsGeneric("@b.No numaralı araç tahsisi iptal edilsin mi?"));
        Assert.Equal(
            new[] { "Kalıcı silinsin mi?", "Ters kayıt alınsın mı?" },
            RazorFormScanner.ConfirmMessages(
                "<form method=\"post\" action=\"/a/sil\" data-confirm=\"Kalıcı silinsin mi?\"></form>" +
                "<button formaction=\"/b/ters\" data-confirm=\"Ters kayıt alınsın mı?\">T</button>" +
                "<div data-confirm=\"div sayılmaz\"></div>").ToArray());
    }

    [Fact]
    public void Komsu_onayli_form_onaysiz_formu_yutmaz()
    {
        // Tek regex'in klasik hatası: ilk formun mesajındaki ">" ve aynı satırdaki ikinci form.
        const string razor =
            "<form method=\"post\" action=\"/a/delete\" data-confirm=\"Tutar > 0 olan kayıt silinsin mi?\"><button>Sil</button></form>" +
            "<form method=\"post\" action=\"/b/iptal\"><button type=\"submit\">İptal</button></form>";

        var h = RazorFormScanner.Targets(razor).ToList();

        Assert.Equal(2, h.Count);
        Assert.Equal("/a/delete", h[0].Aksiyon);
        Assert.True(h[0].Onayli);
        Assert.Equal("/b/iptal", h[1].Aksiyon);
        Assert.False(h[1].Onayli);
    }

    [Fact]
    public void Razor_ifadesindeki_tirnak_ve_buyuktur_etiketi_bitirmez()
    {
        const string razor =
            "<form method=\"post\" action=\"@(_duzenle is null ? \"/x/create\" : \"/x/update\")\"\n" +
            "      class=\"@(a > b ? \"x\" : \"y\")\"\n" +
            "      data-confirm=\"@($\"{k.Name} silinsin mi? \\\"{k.Kod}\\\" kalıcı gider.\")\">\n" +
            "  <input type=\"hidden\" name=\"id\" value=\"@k.Id\" />\n" +
            "</form>\n" +
            "<form method=\"post\" action=\"/cezalar/@c.Id/iptal\"><button>İptal</button></form>";

        var h = RazorFormScanner.Targets(razor).ToList();

        Assert.Equal(2, h.Count);
        Assert.True(h[0].Onayli);
        Assert.Equal(new[] { "/x/create", "/x/update" }, RazorFormScanner.PathCandidates(h[0].Aksiyon).ToArray());
        Assert.False(RazorFormScanner.IsDestructive(h[0].Aksiyon));
        Assert.Equal(6, h[1].Satir);
        Assert.True(RazorFormScanner.IsDestructive(h[1].Aksiyon));
        Assert.False(h[1].Onayli);
    }

    [Fact]
    public void Onay_formda_ya_da_gonderen_butonda_olabilir()
    {
        const string razor =
            // 1) Onay tek gönder butonunda → onaylı.
            "<form method=\"post\" action=\"/a/sil\"><button type=\"submit\" data-confirm=\"A silinsin mi?\">Sil</button></form>\n" +
            // 2) İki gönder butonu, yalnız biri onaylı → onaysız (diğeri sormadan siler).
            "<form method=\"post\" action=\"/b/sil\"><button data-confirm=\"B?\">Sil</button><button>Hızlı sil</button></form>\n" +
            // 3) type=button gönderen sayılmaz; tek gönderen onaylı → onaylı.
            "<form method=\"post\" action=\"/c/sil\"><button type=\"button\">Önizle</button><input type=\"submit\" data-confirm=\"C?\" /></form>\n" +
            // 4) formaction'lı yıkıcı buton: formda onay yok, butonda yok → onaysız buton hedefi.
            "<form method=\"post\" action=\"/d/kaydet\"><button>Kaydet</button><button formaction=\"/d/ters\">Ters</button></form>\n" +
            // 5) Ana formun DIŞINDAN form=\"…\" ile bağlanan buton o formun göndericisidir.
            "<form id=\"f5\" method=\"post\" action=\"/e/iptal\"></form><button form=\"f5\" data-confirm=\"E?\">İptal</button>\n" +
            // 6) Boş mesaj onay sayılmaz.
            "<form method=\"post\" action=\"/f/delete\" data-confirm=\"\"><button>Sil</button></form>\n" +
            // 7) Razor yorumundaki form taranmaz.
            "@* <form method=\"post\" action=\"/g/sil\"><button>Sil</button></form> *@\n" +
            // 8) action'sız, tüm göndericileri formaction'lı form (Depozito deseni): form hedefi
            //    ÜRETİLMEZ (kendi hedefine hiç gitmez), butonlar tek tek denetlenir.
            "<form method=\"post\"><button formaction=\"/h/kaydet\">K</button><button formaction=\"/h/sil\" data-confirm=\"H silinsin mi?\">S</button></form>\n";

        var h = RazorFormScanner.Targets(razor).ToList();
        var summary = h.Select(x => (x.Tur, x.Aksiyon, x.Onayli)).ToArray();

        Assert.Equal(
            new (string, string?, bool)[]
            {
                ("form", "/a/sil", true),
                ("form", "/b/sil", false),
                ("form", "/c/sil", true),
                ("form", "/d/kaydet", false),
                ("buton", "/d/ters", false),
                ("form", "/e/iptal", true),
                ("form", "/f/delete", false),
                ("buton", "/h/kaydet", false),
                ("buton", "/h/sil", true),
            },
            summary);
    }

    [Fact]
    public void Yikici_fiil_yalniz_son_yol_parcasinda_aranir()
    {
        // Kaynak adı fiil İÇEREBİLİR ("iptal-sebepleri" ana verisi): fiil son parçada aranmalı.
        Assert.False(RazorFormScanner.IsDestructive("/iptal-sebepleri/create"));
        Assert.False(RazorFormScanner.IsDestructive("/iptal-sebepleri/update"));
        Assert.True(RazorFormScanner.IsDestructive("/iptal-sebepleri/delete"));
        // Tire ile birleşik son parça.
        Assert.True(RazorFormScanner.IsDestructive("/finans/dis-hizmet-iptal"));
        Assert.True(RazorFormScanner.IsDestructive("/blog-yonetim/@p.Id/kapak-sil"));
        Assert.True(RazorFormScanner.IsDestructive("/finans/tahsilat/ters"));
        Assert.True(RazorFormScanner.IsDestructive("/kiralar/cancel?x=1"));
        // Fiilin parçası olan ama fiil olmayan sözcük ("silah", "tersane") yakalanmaz.
        Assert.False(RazorFormScanner.IsDestructive("/a/silah-kaydet"));
        Assert.False(RazorFormScanner.IsDestructive("/a/tersane"));
        // Adıyla listelenen geri alınamaz işlem.
        Assert.True(RazorFormScanner.IsDestructive("/donem-kapanis/kilitle"));
        Assert.False(RazorFormScanner.IsDestructive(null));
    }

    [Fact]
    public void Fiil_sorgu_degerinde_ya_da_ifade_icindeki_parca_literalinde_de_aranir()
    {
        // Kör nokta (b): fiil yolun son parçasında değil, sorgu değerinde ya da ifadenin
        // birleştirdiği çıplak bir parça literalinde duruyorsa çitten kaçıyordu.
        Assert.True(RazorFormScanner.IsDestructive("/x/islem?tur=sil"));
        Assert.True(RazorFormScanner.IsDestructive("/x/islem?a=1&tur=kapak-sil"));
        Assert.True(RazorFormScanner.IsDestructive("@($\"/x/{(a ? \"sil\" : \"kaydet\")}\")"));
        Assert.True(RazorFormScanner.IsDestructive("@(\"/x/\" + (a ? \"iptal\" : \"onayla\"))"));
        // Sorgudaki dönüş ADRESİ fiil taşısa da işlem o değildir (kaynak adı "iptal-sebepleri").
        Assert.False(RazorFormScanner.IsDestructive("/x/kaydet?donus=/iptal-sebepleri"));
        Assert.False(RazorFormScanner.IsDestructive("/x/kaydet?id=@x.Id"));
        Assert.False(RazorFormScanner.IsDestructive("@(_duzenle is null ? \"/iptal-sebepleri/create\" : \"/iptal-sebepleri/update\")"));
    }

    [Fact]
    public void Formu_ayni_dosyada_bulunamayan_formaction_butonu_POST_sayilir()
    {
        // Kör nokta (a): tarama DOSYA başınadır. Ebeveyn bileşenin POST formunun içine render edilen
        // alt bileşendeki <button formaction="/x/sil"> (formmethod'suz) eskiden Post=false sayılıp
        // HİÇ denetlenmiyordu. Formu görünmeyen gönderici için güvenli varsayım POST'tur.
        var tek = RazorFormScanner.Targets("<button formaction=\"/x/sil\">Sil</button>").ToList();
        Assert.Equal(("buton", "/x/sil", true, false), (tek[0].Tur, tek[0].Aksiyon, tek[0].Post, tek[0].Onayli));
        Assert.Single(tek);

        // form="…" başka dosyadaki forma bağlanıyorsa da aynı varsayım.
        var outer = RazorFormScanner.Targets("<button form=\"ana\" formaction=\"/x/iptal\">İptal</button>").Single();
        Assert.True(outer.Post);

        // Açık formmethod varsayımı ezer; aynı dosyadaki GET formunun içindeki buton GET kalır.
        Assert.False(RazorFormScanner.Targets("<button formaction=\"/x/sil\" formmethod=\"get\">S</button>").Single().Post);
        Assert.False(RazorFormScanner.Targets("<form action=\"/ara\"><button formaction=\"/x/sil\">S</button></form>")
            .Single(h => h.Tur == "buton").Post);
    }

    /// <summary>
    /// Razor şablonundaki form/buton etiketlerini tırnaklara ve Razor ifadelerine saygılı okuyan
    /// küçük ayrıştırıcı. Tam bir Razor ayrıştırıcısı değildir; yalnız bu çitin ihtiyacı olan
    /// üç etiketi (<c>form</c>, <c>button</c>, <c>input</c>) ve öznitelik değerlerini çıkarır.
    /// </summary>
    public static class RazorFormScanner
    {
        /// <summary>Taranan tek bir POST adayı.</summary>
        /// <param name="Tur">"form" (formun kendi action'ı) ya da "buton" (formaction'lı gönderen).</param>
        /// <param name="Satir">Etiketin başladığı satır (1 tabanlı).</param>
        /// <param name="Aksiyon">Ham action/formaction değeri (Razor ifadesi olduğu gibi).</param>
        /// <param name="Post">Etkin yöntem POST mu (buton için formmethod formunkini ezer).</param>
        /// <param name="Onayli">Formda ya da hedefe gönderen TÜM butonlarda boş olmayan data-confirm var mı.</param>
        public sealed record Hedef(string Tur, int Satir, string? Aksiyon, bool Post, bool Onayli);

        private sealed record Etiket(string Ad, int Bas, int Son, Dictionary<string, string> Oz)
        {
            public string? Value(string name) => Oz.TryGetValue(name, out var v) ? v : null;
            public bool Onay => !string.IsNullOrWhiteSpace(Value("data-confirm"));

            public bool GonderirMi => Ad switch
            {
                // <button>'ın varsayılan tipi SUBMIT'tir; yalnız button/reset göndermez.
                "button" => Value("type")?.Trim().ToLowerInvariant() is not ("button" or "reset"),
                "input" => Value("type")?.Trim().ToLowerInvariant() is "submit" or "image",
                _ => false,
            };
        }

        /// <summary>
        /// Hedef yolu yıkıcı fiil taşıyan son parçaya sahipse ya da adıyla listelenen geri
        /// alınamaz işlemlerdense. Fiil SON yol parçasında, tire/alt çizgiyle ayrılmış TAM sözcük
        /// olarak aranır: "/iptal-sebepleri/create" (kaynak adı) yıkıcı değil,
        /// "/finans/dis-hizmet-iptal" yıkıcı.
        /// </summary>
        public static bool IsDestructive(string? rawAction)
        {
            foreach (var candidate in PathCandidates(rawAction))
            {
                var path = NormalPath(candidate);
                if (IrreversiblePaths.Contains(path)) return true;
                if (CarriesVerb(path[(path.LastIndexOf('/') + 1)..])) return true;
                // Sorgu DEĞERLERİ ("/x/islem?tur=sil"): işlemi seçen parametre fiil taşıyabilir. Değer
                // yalnız tire/alt çizgiyle bölünür — "/" ile bölünseydi "?donus=/iptal-sebepleri" gibi
                // bir DÖNÜŞ ADRESİ (işlem değil) yanlış alarm verirdi.
                var question = candidate.IndexOf('?');
                if (question >= 0)
                {
                    var query = candidate[(question + 1)..].Split('#')[0];
                    foreach (var part in query.Split('&'))
                    {
                        var equal = part.IndexOf('=');
                        if (equal >= 0 && CarriesVerb(part[(equal + 1)..])) return true;
                    }
                }
            }
            // İfadenin birleştirdiği ÇIPLAK parça literalleri (@($"/x/{(a ? "sil" : "kaydet")}")):
            // eğik çizgisiz, boşluksuz bir literal yol PARÇASIDIR ve son parça olabilir. Bilinçli
            // temkinli: böyle bir literal fiil taşıyorsa hedef yıkıcı sayılır (yanlış alarmın bedeli
            // bir onay mesajı; kaçırmanın bedeli sorulmadan silinen kayıt).
            return PartLiterals(rawAction).Any(CarriesVerb);
        }

        private static bool CarriesVerb(string part)
            => part.Split('-', '_').Any(s => DestructiveVerbs.Contains(s.ToLowerInvariant()));

        /// <summary>Razor ifadesindeki eğik çizgisiz, boşluksuz string literalleri ("sil", "kapak-sil").</summary>
        private static IEnumerable<string> PartLiterals(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || !raw.TrimStart().StartsWith("@(", StringComparison.Ordinal))
                return [];
            return Regex.Matches(raw, "\"([A-Za-z0-9_-]+)\"").Select(m => m.Groups[1].Value).ToList();
        }

        /// <summary>
        /// Karşılaştırma için yol: sorgu/parça atılır, sondaki "/" kırpılır, Razor ifadesi taşıyan
        /// her parça ("@Id", "@k.Id") "{}" olur → "/kiralar/@Id/paylas-yeni" = "/kiralar/{}/paylas-yeni".
        /// </summary>
        public static string NormalPath(string candidate)
        {
            var path = candidate.Split('?', '#')[0].Trim().TrimEnd('/');
            return string.Join('/', path.Split('/').Select(p => p.Contains('@') ? "{}" : p));
        }

        /// <summary>Görev tanımındaki fiiller: silme, iptal, ters kayıt, kaldırma.</summary>
        private static readonly HashSet<string> DestructiveVerbs =
            ["sil", "delete", "iptal", "cancel", "ters", "reverse", "kaldir", "remove"];

        /// <summary>Hedef onay İSTEMELİ mi: yıkıcı fiil / geri alınamaz işlem ya da adıyla onay zorunlu.</summary>
        public static bool IsConfirmRequired(string? rawAction)
            => IsDestructive(rawAction)
               || PathCandidates(rawAction).Any(a => ConfirmRequiredPaths.Contains(NormalPath(a)));

        /// <summary>İki listenin birleşimi — her biri repoda en az bir POST hedefiyle eşleşmeli.</summary>
        public static IEnumerable<string> PathsListedByName
            => IrreversiblePaths.Concat(ConfirmRequiredPaths).OrderBy(y => y, StringComparer.Ordinal);

        /// <summary>
        /// Yolunda fiil OLMAYAN ama onayı BİLİNÇLİ konmuş işlemler. <see cref="IrreversiblePaths"/>'dan
        /// ayrı tutuldu: bunlar yıkıcı sayımına (<see cref="MinDestructiveTarget"/>) girmez, ama onayları
        /// silinirse çit kırmızıya döner. Her biri ADIYLA ve gerekçesiyle.
        ///
        /// <para><b>Neden var (adversarial bulgu):</b> PR'ın gerekçesi, mesajı gönder butonuna koymuş
        /// yedi işlemin (toplu fatura, bakiye düzeltme, otomatik tahsilat, gider ödemesi, ücretli
        /// WhatsApp/e-posta/SMS testleri) HİÇ sormamasıydı. Yol fiil taşımadığı için bu yedisinin
        /// onayı silinse de hiçbir test kırılmıyordu; iade faturası (ters kayıt) da öyle.</para>
        ///
        /// <para>"iade" kelimesi fiil listesine BİLEREK eklenmedi: <c>/depozito/iade</c> olağan bir
        /// postlama (bkz. <see cref="IrreversiblePaths"/>'ın "listede olmayanlar" notu).</para>
        /// </summary>
        private static readonly HashSet<string> ConfirmRequiredPaths = new(StringComparer.Ordinal)
        {
            // İade faturası: kaynak faturanın gelir/KDV/cari etkisini TERS kayıtla geri alır; iade
            // faturası da silinemez. Yolunda "ters" geçmediği için fiil kuralı yakalamıyor.
            "/finans/fatura-iade",
            // Toplu fatura: seçili her kira için tek tıkla fatura keser; fatura silinemez (düzeltme
            // iade faturasıyla).
            "/finans/fatura-toplu",
            // Cari bakiye düzeltme: elle borç/alacak kaydı deftere yazılır; silinemez (ters kayıtla düzelir).
            "/finans/bakiye-duzeltme",
            // Otomatik tahsilat: seçili dönemler için toplu fatura (+ işaretliyse tahsilat) yazar.
            "/finans/otomatik-tahsilat/calistir",
            // Gider ödemesi: tutar boşsa kalanın TAMAMI ödenmiş sayılır — tek tıkla kapanır.
            "/giderler/odeme",
            // Ücretli/dışa giden testler: gerçek WhatsApp, e-posta ve (ücretli) SMS gönderir; geri çağrılamaz.
            "/ayarlar/whatsapp-test",
            "/ayarlar/smtp-test",
            "/ayarlar/sms-test",
            // Şube birleştirme: kaynak şubenin tüm kayıtları hedefe taşınır; geri alınamaz.
            "/subeler/birlestir",
            // Takvim aboneliği yenileme: ESKİ abonelik bağlantısı ölür, abone olan her takvim kopar.
            "/takvim/yenile",
            // Sözleşme paylaşımında "Yeni Sürüm": müşteriye gönderilmiş ESKİ link ölür (paylas-iptal ile
            // aynı sonuç; o yol "iptal" fiiliyle zaten kapsamda).
            "/kiralar/{}/paylas-yeni",
        };

        /// <summary>
        /// Yolunda fiil OLMAYAN ama geri alınamaz işlemler — her biri ADIYLA ve gerekçesiyle.
        /// Buraya eklemeden önce ucun ve servisin ne yaptığını okuyun.
        ///
        /// <para><b>Bilerek listede OLMAYANLAR:</b> olağan defter postlamaları (tahsilat, ödeme,
        /// fatura kesimi, ceza/servis yansıtma, depozito al/iade/mahsup). Defter tasarım gereği
        /// değişmezdir, yani HER postlama "geri alınamaz"; hepsine onay koymak kullanıcıyı onayı
        /// okumadan geçmeye alıştırır ve gerçekten tehlikeli olanın uyarısını değersizleştirir.
        /// Durum geçişleri de yok (hasar/e-fatura onay-red, provizyon kapama): para ve veri
        /// kaybettirmezler; "Reddet"e onay koymak simetrik "Onayla"yı da gerektirirdi. Firma
        /// kapatma (<c>/platform/tenants/close</c>) zaten data-confirm'den SERT bir onayla korunur
        /// (firma kodu sunucuda eşleşmeli).</para>
        /// </summary>
        private static readonly HashSet<string> IrreversiblePaths = new(StringComparer.Ordinal)
        {
            // Dönem kapanışı: Gelir/Gider'i Dönem Sonucu'na taşıyan fiş değişmez deftere yazılır ve
            // tarih + öncesi postlamaya kilitlenir (DonemKapanisRepository.KapatAsync).
            "/donem-kapanis/kilitle",
            // Kilidi kaldırma: tüm kapalı dönemleri tek tıkla yeniden kayda açar ve fişi GERİ
            // ALMAZ — kullanıcı "kapanışı geri aldım" sanıp kapalı döneme kayıt girebilir.
            "/donem-kapanis/ac",
            // Depozito irat: müşterinin emanetini GELİR'e çevirir (Borç Depozito / Alacak Gelir);
            // ters-kayıt ucu yok, kesilen tutar bir daha iade edilemez.
            "/depozito/irat",
            // Tek cari toplu kapatma: tahsilatla birlikte kalemleri KapatmaTahsis'le kalıcı
            // "kapalı" işaretler; tahsilatın ters kaydı bu tahsisi serbest BIRAKMAZ.
            "/finans/tek-cari-kapat",
        };

        /// <summary>
        /// Ham değerden taranabilir yol(lar). Razor ifadesiyse (<c>@(a ? "/x/create" : "/x/update")</c>)
        /// içindeki string literalleri; değilse değerin kendisi. Yalnız değişkenden gelen hedef
        /// (<c>@Url</c>) için BOŞ döner — bunu <see cref="Her_post_formunun_hedefi_statik_okunabilir"/> yakalar.
        /// </summary>
        public static IReadOnlyList<string> PathCandidates(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return [];
            var h = raw.Trim();
            if (h.StartsWith("@(", StringComparison.Ordinal))
                return Regex.Matches(h, "\"((?:[^\"\\\\]|\\\\.)*)\"")
                    .Select(m => m.Groups[1].Value)
                    .Where(s => s.StartsWith('/'))
                    .ToList();
            return h.StartsWith('/') ? [h] : [];
        }

        /// <summary>Metindeki POST adayları: her form (kendi action'ıyla) + formaction'lı her gönderen buton.</summary>
        public static IEnumerable<Hedef> Targets(string text)
        {
            var labels = Tags(text);
            var forms = labels.Where(e => e.Ad == "form")
                .Select(f =>
                {
                    var closing = text.IndexOf("</form", f.Son, StringComparison.OrdinalIgnoreCase);
                    return (Form: f, GovdeSon: closing < 0 ? text.Length : closing);
                })
                .ToList();

            // Butonun formu: form="…" varsa o id'li form (dışarıdan bağlama), yoksa içinde durduğu form.
            (Etiket Form, int GovdeSon)? FindForm(Etiket b)
            {
                var id = b.Value("form");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    foreach (var x in forms)
                        if (string.Equals(x.Form.Value("id"), id, StringComparison.Ordinal))
                            return x;
                    return null;
                }
                foreach (var x in forms)
                    if (b.Bas >= x.Form.Son && b.Bas < x.GovdeSon)
                        return x;
                return null;
            }

            var submitters = labels.Where(e => e.GonderirMi)
                .Select(b => (Buton: b, Form: FindForm(b)))
                .ToList();

            var result = new List<(int Bas, Hedef H)>();
            foreach (var (form, _) in forms)
            {
                var post = IsPostMethod(form.Value("method"));
                // Formun KENDİ action'ına gönderenler: formaction'ı olmayan butonlar.
                var all = submitters
                    .Where(g => g.Form is { } f && ReferenceEquals(f.Form, form))
                    .Select(g => g.Buton)
                    .ToList();
                var own = all.Where(b => b.Value("formaction") is null).ToList();
                // action'sız ve TÜM göndericileri formaction'lı form (Depozito: Al/İade/Mahsup/İrat)
                // kendi hedefine hiç gönderilmez; hedefler aşağıda buton buton denetlenir.
                if (form.Value("action") is null && all.Count > 0 && own.Count == 0) continue;
                var approved = form.Onay || (own.Count > 0 && own.All(b => b.Onay));
                result.Add((form.Bas, new Hedef("form", Row(text, form.Bas), form.Value("action"), post, approved)));
            }

            foreach (var (button, formMatch) in submitters)
            {
                var target = button.Value("formaction");
                if (target is null) continue;
                var formmethod = button.Value("formmethod");
                // Formu bu dosyada bulunamayan gönderici (alt bileşen ebeveynin formunun İÇİNDE render
                // edilir; mega-form paneller böyle) yöntemi bilinemez → POST varsayılır. Aksi halde
                // Post=false sayılıp hiç denetlenmezdi (tarama dosya başına).
                var post = formmethod is not null ? IsPostMethod(formmethod)
                    : formMatch is null || IsPostMethod(formMatch.Value.Form.Value("method"));
                var approved = button.Onay || (formMatch?.Form.Onay ?? false);
                result.Add((button.Bas, new Hedef("buton", Row(text, button.Bas), target, post, approved)));
            }

            return result.OrderBy(x => x.Bas).Select(x => x.H).ToList();
        }

        /// <summary>form/button/input etiketlerindeki tüm data-confirm değerleri (ham, Razor ifadesi olduğu gibi).</summary>
        public static IEnumerable<string> ConfirmMessages(string text)
            => Tags(text).Select(e => e.Value("data-confirm")).OfType<string>().ToList();

        /// <summary>Mesaj TAMAMEN jenerik bir kalıptan mı ibaret (nesne ve sonuç adlandırılmamış).</summary>
        public static bool IsGeneric(string message)
            // 'İ' elle 'i'ye indirilir: değişmez kültürde küçültme ona birleşik nokta ekleyebilir
            // ("İptal" → "i̇ptal") ve kalıp eşleşmezdi.
            => GenericPatterns.Contains(message.Trim().TrimEnd('?', '!', '.', ' ').Replace('İ', 'i').ToLowerInvariant());

        // Tek fiilden ibaret mesajlar da jenerik: "Silinsin mi?" NEYİN silindiğini ve sonucun kalıcı
        // silme mi, pasife alma mı olduğunu söylemez.
        private static readonly HashSet<string> GenericPatterns =
            ["emin misiniz", "emin misin", "onaylıyor musunuz", "onaylıyor musun", "devam edilsin mi", "are you sure",
             "devam", "devam mı", "silinsin mi", "silinecek", "iptal edilsin mi", "reddedilsin mi",
             "kaldırılsın mı", "onaylansın mı"];

        private static bool IsPostMethod(string? method)
            => string.Equals(method?.Trim(), "post", StringComparison.OrdinalIgnoreCase);

        private static int Row(string text, int location)
        {
            var row = 1;
            for (var i = 0; i < location; i++)
                if (text[i] == '\n') row++;
            return row;
        }

        // ---- ayrıştırıcı ------------------------------------------------------------------

        private static readonly string[] RelatedTags = ["form", "button", "input"];

        private static List<Etiket> Tags(string t)
        {
            var result = new List<Etiket>();
            var i = 0;
            while (i < t.Length)
            {
                if (StartsWith(t, i, "@*")) { i = Skip(t, i + 2, "*@"); continue; }
                if (StartsWith(t, i, "<!--")) { i = Skip(t, i + 4, "-->"); continue; }
                if (t[i] == '<')
                {
                    var name = RelatedTags.FirstOrDefault(a =>
                        string.Compare(t, i + 1, a, 0, a.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && i + 1 + a.Length < t.Length
                        && (char.IsWhiteSpace(t[i + 1 + a.Length]) || t[i + 1 + a.Length] is '>' or '/'));
                    if (name is not null)
                    {
                        var (last, oz) = ReadTag(t, i + 1 + name.Length);
                        result.Add(new Etiket(name, i, last, oz));
                        i = last; // etiketin İÇİNDEN yeniden aramaya başlanmaz (komşu yutma yok)
                        continue;
                    }
                }
                i++;
            }
            return result;
        }

        private static (int Son, Dictionary<string, string> Oz) ReadTag(string t, int i)
        {
            var start = i;
            var oz = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (i < t.Length)
            {
                var c = t[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '>') return (i + 1, oz);
                if (c == '/' && Next(t, i) == '>') return (i + 2, oz);
                if (c == '@' && Next(t, i) == '*') { i = Skip(t, i + 2, "*@"); continue; }
                if (c == '@' && Next(t, i) == '(') { i = ExpressionEnd(t, i + 1); continue; }

                var nameStart = i;
                while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != '=' && t[i] != '>'
                       && !(t[i] == '/' && Next(t, i) == '>'))
                    i++;
                var name = t[nameStart..i];

                var j = SkipWhitespace(t, i);
                var value = "";
                if (j < t.Length && t[j] == '=')
                    (value, i) = ReadValue(t, SkipWhitespace(t, j + 1));
                else if (i == nameStart)
                    i++; // ilerleme garantisi (beklenmeyen tek karakter)

                if (name.Length > 0) oz.TryAdd(name, value);
            }
            throw new InvalidOperationException($"Kapanmayan etiket (konum {start}) — tarayıcı dosyanın sonuna taştı.");
        }

        private static (string Deger, int Son) ReadValue(string t, int j)
        {
            if (j >= t.Length) return ("", j);
            var q = t[j];
            if (q is '"' or '\'')
            {
                var k = j + 1;
                while (k < t.Length)
                {
                    if (t[k] == q) return (t[(j + 1)..k], k + 1);
                    k = t[k] == '@' ? RazorEnd(t, k) : k + 1;
                }
                throw new InvalidOperationException($"Kapanmayan öznitelik değeri (konum {j}).");
            }
            var m = j;
            while (m < t.Length && !char.IsWhiteSpace(t[m]) && t[m] != '>')
                m = t[m] == '@' ? RazorEnd(t, m) : m + 1;
            return (t[j..m], m);
        }

        /// <summary>t[k]=='@' — Razor yapısının bittiği konum (ifade içindeki tırnak/parantez değeri bitirmez).</summary>
        private static int RazorEnd(string t, int k)
        {
            var d = Next(t, k);
            if (d == '@') return k + 2;                                   // @@ kaçışı
            if (d == '*') return Skip(t, k + 2, "*@");                    // @* yorum *@
            if (d == '(') return ExpressionEnd(t, k + 1);                     // @( açık ifade )
            if (k > 0 && char.IsLetterOrDigit(t[k - 1])) return k + 1;    // e-posta: a@b.com düz metin
            if (d is { } h && (char.IsLetter(h) || h == '_'))              // @x.Y(…)[…] örtük ifade
            {
                var m = IdentifierEnd(t, k + 1);
                while (m < t.Length)
                {
                    if (t[m] is '(' or '[') { m = ExpressionEnd(t, m); continue; }
                    if (t[m] == '.' && Next(t, m) is { } n1 && (char.IsLetter(n1) || n1 == '_'))
                    { m = IdentifierEnd(t, m + 1); continue; }
                    if (t[m] == '?' && Next(t, m) == '.' && m + 2 < t.Length && (char.IsLetter(t[m + 2]) || t[m + 2] == '_'))
                    { m = IdentifierEnd(t, m + 2); continue; }
                    break;
                }
                return m;
            }
            return k + 1;
        }

        /// <summary>t[k] '(' '[' ya da '{' — eşleşen kapanıştan sonraki konum. C# dizeleri/karakterleri atlanır.</summary>
        private static int ExpressionEnd(string t, int k)
        {
            var start = k;
            var depth = 0;
            while (k < t.Length)
            {
                var c = t[k];
                switch (c)
                {
                    case '(' or '[' or '{':
                        depth++; k++; break;
                    case ')' or ']' or '}':
                        depth--; k++;
                        if (depth == 0) return k;
                        break;
                    case '"':
                        k = StringEnd(t, k, fullLetters: false, withIntermediateValue: false); break;
                    case '\'':
                        k = CharacterEnd(t, k); break;
                    case '$' when StartsWith(t, k, "$@\""):
                        k = StringEnd(t, k + 2, fullLetters: true, withIntermediateValue: true); break;
                    case '@' when StartsWith(t, k, "@$\""):
                        k = StringEnd(t, k + 2, fullLetters: true, withIntermediateValue: true); break;
                    case '$' when Next(t, k) == '"':
                        k = StringEnd(t, k + 1, fullLetters: false, withIntermediateValue: true); break;
                    case '@' when Next(t, k) == '"':
                        k = StringEnd(t, k + 1, fullLetters: true, withIntermediateValue: false); break;
                    default:
                        k++; break;
                }
            }
            throw new InvalidOperationException($"Kapanmayan Razor/C# ifadesi (konum {start}).");
        }

        /// <summary>t[k]=='"' — dize sonundan sonraki konum. Aradeğerli dizede {…} delikleri C# ifadesidir.</summary>
        private static int StringEnd(string t, int k, bool fullLetters, bool withIntermediateValue)
        {
            var start = k;
            k++;
            while (k < t.Length)
            {
                var c = t[k];
                if (!fullLetters && c == '\\') { k += 2; continue; }
                if (c == '"')
                {
                    if (fullLetters && Next(t, k) == '"') { k += 2; continue; }
                    return k + 1;
                }
                if (withIntermediateValue && c == '{')
                {
                    if (Next(t, k) == '{') { k += 2; continue; }
                    k = ExpressionEnd(t, k);
                    continue;
                }
                if (withIntermediateValue && c == '}' && Next(t, k) == '}') { k += 2; continue; }
                k++;
            }
            throw new InvalidOperationException($"Kapanmayan C# dizesi (konum {start}).");
        }

        private static int CharacterEnd(string t, int k)
        {
            var m = k + 1;
            m += m < t.Length && t[m] == '\\' ? 2 : 1;
            while (m < t.Length && t[m] != '\'') m++; // ç gibi uzun kaçışlar
            return m + 1;
        }

        private static int IdentifierEnd(string t, int k)
        {
            while (k < t.Length && (char.IsLetterOrDigit(t[k]) || t[k] == '_')) k++;
            return k;
        }

        private static int Skip(string t, int k, string end)
        {
            var i = t.IndexOf(end, k, StringComparison.Ordinal);
            return i < 0 ? t.Length : i + end.Length;
        }

        private static int SkipWhitespace(string t, int k)
        {
            while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
            return k;
        }

        private static bool StartsWith(string t, int k, string s)
            => string.CompareOrdinal(t, k, s, 0, s.Length) == 0;

        private static char? Next(string t, int k) => k + 1 < t.Length ? t[k + 1] : null;
    }
}
