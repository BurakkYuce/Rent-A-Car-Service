# DEVİR — Angular geçişine kaldığın yerden devam

> Bu dosya, **hiç bağlamı olmayan** bir ajanın geçişin kalanını baştan sona yürütebilmesi için yazıldı.
> Tek giriş noktası budur. Her merge'den sonra **§1 Durum** güncellenir. Kısayol: `/roadmap-devam`
> becerisi (`.claude/skills/roadmap-devam/SKILL.md`) bu dosyayı okuyup sıradaki işi yapar.

## 0. Okuma sırası

1. `CLAUDE.md` (repo kökü, otomatik yüklenir): yığın, çok-kiracılık, para/defter, yetki, KVKK kuralları.
2. **Bu dosya**, özellikle §1, §2 ve §5.
3. `docs/roadmap/README.md`: plan, kilitli kararlar, tuzak tablosu, imzalar (ProblemDetails `kod`'ları, idempotency).
4. Sıradaki fazın `docs/roadmap/F*.md` dosyası: "Kalıp (F5–F12 ortak)", sayfa/uç envanteri, Exit.
5. `src/RentACar.Frontend/AGENTS.md`: frontend kuralları (lint/tip kapıları, çekirdek bileşenler, form seti, kira formu).
6. Para dokunan işte: `docs/api/idempotency-envanteri.md` ("SPA sözleşmesi" bölümü dahil).

**Faz sırası KİLİTLİ.** Öne ya da arkaya alma, sayfayı başka faza taşıma, kapsam daraltma yalnız kullanıcının
açık kararıyla ve `docs/roadmap/DEGISIKLIKLER.md` kaydıyla olur. Çekirdekte eksik çıkarsa o fazın içine
"çekirdek eki" PR'ı olarak girer.

## 1. Durum (her merge'den sonra güncelle)

Güncelleme: 2026-09-22. Main'de 60 PR'lık planın ~22,5'i var (~%37). Ekran olarak kira listesi, Panel ve kira
formu SPA'da; kullanıcılar henüz Blazor kullanıyor (pilot kapalı).

### ✅ Bitti
- G0, F0 (#235 #237), F1 (#238–#240 #243–#245), F2.1 (#241), F2.2 kodu (#249), F3 (#248 #250–#255, kapanış #256).
- F4.1 kira + panel uçları (#258), F4.4a finans uçları `/api/ui/v1/finans` (#257), F4.2 kira listesi (#260),
  F4.5 Panel (#259), F4.3 kira formu I (#261), F4.3b kira formu parite ekleri (#262).
- Yan düzeltmeler: #265 (müşteri bildirimleri + WhatsApp özeti 2026-08-17'den beri çalışmıyordu — UTC),
  #266 (iş koşu günlüğü hata satırı + üretici yalıtımı + üretici başına `racar_job_fail` metriği).

### ⏳ Açık PR'lar — İLK BUNLAR
1. **#263 F4.4 kira formu II (sabit finans paneli, PARA)** — dal `feat/f4-4-kira-formu-2`, head `f11db7f`,
   CI yeşil. 4 adversarial turu geçti; **4. turda bir MEDIUM (M-C) açık → merge YOK.**
   - **M-C:** Kullanıcı 500 yazar, yanıt kaybolur (ilk istek aslında yazıldı). Tutarı 600'e düzeltip aynı donmuş
     anahtarla tekrar gönderir. Sunucu 409 `mukerrer` + `mevcut{…, ayniIcerik:false}` döner. UI "BAŞKA bir tahsilat
     yazıldı; 600 YAZILMADI" deyip formu korur. Kullanıcı tekrar basınca 500+600=1100 yazılır (niyet 600).
   - **Düzeltme (yalnız SPA, 3 yer):** `features/kira-formu/finans-paneli/kira-finans-durumu.ts` (`tahsilatYap`),
     `features/kiralar/kira-listesi/tahsil-paneli.ts`, `features/panel/panel-tahsilat-formu.ts`. Gönderimden ÖNCE
     "bu donmuş anahtarla sonuçlanmamış bir deneme var mı" bilgisini yakala (`tf.kopya.sonuclanmamis`).
     `mevcut` dolu ve istek bir tekrar ise mesaj "Önceki denemeniz kaydedilmiş (No …, 500 TRY); 600 YAZILMADI"
     olsun ve tutar TEMİZLENSİN.
   - **Aynı turda ucuz Low'lar:**
     - L-1: `ayniIcerik` karşılaştırmasına kur + açıklama + kanal eklensin (`src/RentACar.Web/Api/Finans/FinansApi.cs`).
     - L-2: "YAZILMADI" sonrası dokunulmamış ön-dolu tutar yeni bakiyeyle yenilensin; kullanıcının yazdığı tutar korunsun.
   - **Sonra:** bağımsız adversarial 5. tur (§4; probe örneği `adv-f44-gercek3.spec.ts` H1b senaryosu), CI, merge.
2. **#264 F4.6 ilk kesiş (mekanik)** — dal `feat/f4-6-ilk-kesis`, head `194ca53`, güvenlik incelemesi TEMİZ.
   - İçerik: pilot anahtarı, tek giriş `/login` → `/app/giris`, 5 şablonluk GET yönlendirme haritası, kayıttan menü, test devri.
   - **Sıra ZORUNLU: #263'ten SONRA.** Aksi halde pilot kiracıda kiradan fatura/dönem/dış hizmet erişilemez.
   - #263 merge olunca: `git merge origin/main` → pilot kiracıda kira formunun finans paneli e2e ile doğrulanır
     (hiçbir bağlantı Blazor kira sayfasına düşmemeli) → CI → merge.

### ⬜ Sırada (başlamadı)
1. **F4 kapanış belgesi:**
   - `F4.md` durum + kapanış tablosu, `README.md` faz tablosu.
   - `DEGISIKLIKLER.md`: F4.3 → F4.3 + F4.3b bölündü (bilgi); F4.4 → F4.4a + F4.4 (bilgi); F5 kararı (§3).
2. **Çekirdek eki — rota bazlı tembel çeviri:** `tr.json` ilk pakete gömülü; ilk paket ~400 kB (uyarı 380,
   hata 450 — `angular.json`). F5'ten ÖNCE yapılmalı, yoksa ilk yeni fazda hata eşiği aşılır.
3. **Low temizliği PR'ı** (§6 listesi) — para dokunanlar varsa adversarial.
4. **F4.6b — Blazor kira + Panel sayfalarının ve hedefsiz POST uçlarının silinmesi:** YALNIZ pilotta 10 iş günü
   P1 olmadıktan SONRA. Ön koşul: kullanıcı F2.2 sunucu adımlarını yapmış ve pilotu açmış olmalı.
5. **F5 → F12** modül fazları (her biri `F*.md` "Kalıp"a göre), sonra **F13** söküm.

### 🧑 Kullanıcıda bekleyenler (cevap gelmeden ilgili işe dokunma)
- **F2.2 sunucu adımları:** `docs/ops/f2-2-sunucu-adimlari.md`. Bitmeden `/app` üretimde yok, pilot açılamaz.
- referans sistem parolası değişimi + GitGuardian olayı 37502190'ın kapatılması.
- **Karar (1):** F5, F4'ün "pilotta 10 iş günü P1 yok" Exit'ini beklemeden başlasın mı?
  - Önerilen: evet, F4 kodu bitince başlasın; pilot arka planda sürsün.
  - Karar gelince `DEGISIKLIKLER.md`'ye yaz.
  - Cevap yoksa F4 kapanışı + çekirdek eki + Low temizliği yapılabilir; **F5'e başlanmaz.**
- **Karar (2):** menü görünürlüğü rolden izne geçti (#264). Operatör 79 → 74 öğe, Muhasebe 48 → 53.
  Kullanıcı onayı bekleniyor; itiraz gelirse `MenuKaydi`'nde izin eşlemesi düzeltilir.
- **Karar (3): yakıt ölçeği.** Servis ve harici API 0–100, formlar ve referans sistem 0–12 kullanıyor. Önerilen: tek
  ölçek 0–12. Harici `RentalsApi` için iki seçenek var: (a) >12 → 400, (b) sınırda yüzde↔12 çevirisi. Karar
  gelmeden yakıt koduna dokunma.
- Pilotu platform konsolundan açma (Platform → kiracı detay → "Yeni Arayüz") + canlı duman testi (README "Doğrulama").
- Üretimde #265 etkisini PR'daki iki SQL ile doğrulama.

## 2. Bir PR'ı baştan sona yürütme (tek ajan)

1. **Hazırlık:**
   - `gh auth switch --user BurakkYuce` (makinede iki gh hesabı var; aktif hesap kendiliğinden değişebiliyor).
   - `git fetch -q origin`.
   - `gh pr list --state open` ile §1'i gerçekle karşılaştır, uyuşmazlık varsa önce §1'i düzelt.
2. **Dal:** main'den `feat/f<faz>-<no>-<kisa-ad>` (bağımlı PR'da bağımlılığın dalından). Paralel çalışıyorsan ayrı
   `git worktree add ../demo-<kisa> -b <dal> origin/main`.
3. **Oku:** fazın `F*.md` envanteri (sayfa → servis → uç ihtiyacı), Blazor karşılığı (`src/RentACar.Web/Components/Pages/…`),
   mevcut `/api/ui` desenleri (`src/RentACar.Web/Api/Kira/KiraApi.cs`, `Api/Finans/FinansApi.cs`, `Api/Secim/`).
4. **Backend (eklemeli):** uç + `IzinMetadata`/`IzinMuaf` (yapısal test zorlar) + alt kayıtlarda üst kaydın şube
   kapsamı + alan hataları (`errors[alan]`) + uç sınırları.
   - Sonra OpenAPI anlık görüntüsü: `RACAR_OPENAPI_GUNCELLE=1` ile `UiApiOpenApiTests`, ardından
     `cd src/RentACar.Frontend && npm run tipler`.
5. **Frontend:** çekirdeği kullan (`TemelStore`, liste sorgusu, tablo motoru, form seti, `rc-para-girdisi`, tanım CRUD).
   Revlo'dan kopyalama yok. Rota `sayfalar.ts`; çeviri `tr.json`'da kendi bloğun (`npm run i18n:tipler`).
6. **Testler:**
   - Beklenen değerler elle kurulmuş senaryodan gelir (bağımsız oracle), servis kodundan türetilmez.
   - Backend: WebFactory + gerçek PostgreSQL, `racar_app` ile izolasyon; SABİT PAROLA/KİMLİK YOK (GitGuardian).
   - Frontend: Vitest + Playwright e2e; üç zorunlu senaryo: "doğrulama hatasında form korunur", "oturum düşünce form
     kaybolmaz", "`cakisma` formu silmez". axe 0 ciddi/kritik; 320/390/768/1440 taşma 0.
7. **Kapılar (yerel):**
   - Frontend: `cd src/RentACar.Frontend && npm run lint && npm run typecheck && npm test && npm run build && npm run e2e`.
     **Vitest'i Node 22 ile koş.** Node 26'da jsdom `localStorage` bozuk.
   - Backend: `RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=burak;Database=postgres" dotnet test RentACar.slnx -c Debug > <scratch>/full.log 2>&1; tail -30 <scratch>/full.log`
     → "Failed: 0" açıkça. **Uzun komut çıktısını daima dosyaya yazıp `tail` ile oku**; aksi halde ajan izleyicisi
     600 sn sessizlikte ajanı düşürür.
8. **Commit/PR:**
   - Commit mesajı Türkçe ve ayrıntılı (ne + neden + test özeti); kurallar `CLAUDE.md` §3.
   - `git push -u origin <dal>`, sonra `gh pr create --base main`. PR açıklamasına riskli noktaları ve
     adversarial hedeflerini yaz.
9. **CI:**
   - `scripts/pr-izle.sh <pr> 6 <head-sha-önek>` yalnız izler, merge etmez. Çıktıyı OKU.
   - `spa-surum` PR'da SKIPPED normaldir; diğer 6 kontrol (build-test, frontend, e2e, mobil-tasma,
     deploy-betikleri, GitGuardian) SUCCESS olmalı.
10. **Para PR'ı ise** merge'den önce §4 bağımsız adversarial. Critical/High/Medium = 0 olana kadar düzelt → yeniden doğrulat.
11. **Merge:** ayrı komutta `gh pr merge <no> --merge --match-head-commit <sha>`. Kullanıcı yeşil ve testli PR'ın
    sormadan merge edilmesini istiyor.
    - Merge sonrası `git pull --ff-only`, worktree ve dalı sil (`git worktree remove`, `git branch -d`).
    - Bu dosyanın §1'ini güncelle; faz bitince `F*.md` + README durum ve `DEGISIKLIKLER.md` kaydı.
12. **Bağımlı PR'lar:** her merge'den sonra açık diğer PR'lar `git merge origin/main` ile güncellenir (force-push YOK).
    - Üretilen dosyalar (`docs/api/ui-v1.json`, `core/api/uretilen/ui-v1.ts`, `core/i18n/ceviri-anahtarlari.ts`)
      elle birleştirilmez, koddan yeniden üretilir.
    - `tr.json`'da git iki merge-base bulursa anahtar anahtar birleştir.

## 3. Paralel mod (isteğe bağlı, hız için)

Kullanıcının tercihi: "workflow değil, arkada subagent aç, paralel; amaç hız". Desen:
- Her PR için ayrı worktree + arka plan ajanı. Ajan kendi PR'ını açar, **merge etmez**.
- Koordinatör CI'ı `scripts/pr-izle.sh` ile izler ve ayrı adımda merge eder.
- Bağımlı PR, bağımlılığın dalı üzerinde erken başlar; bağımlılık merge olunca `git merge origin/main` yapar.
- Ağ kesintisinde düşen ajan yeniden açılmaz, aynı ajana mesajla kaldığı yerden devam ettirilir.
- Ajan push edip PR açmadan düşebilir: önce `gh pr list --head <dal>`, yoksa PR'ı koordinatör açar.
- Aynı dosyalara dokunan paralel ajanlara çakışmayı küçük tutmalarını söyle (kendi klasörü, kendi `tr.json` bloğu).
  Ortak çekirdek dosyasını (ör. `para-girdisi.ts`, `oturum-interceptor.ts`) yalnız BİR PR değiştirsin.
- **Döngüyü (`/loop`) kullanıcı istemeden kurma.** Kullanıcı mola verdiğinde uçuştaki işler biter, yenisi başlamaz.

## 4. Para PR'ında bağımsız adversarial inceleme

Kodu yazan ajan kendi PR'ını inceleyemez. **Ayrı bir ajan** (Agent aracı; yoksa kullanıcıdan ikinci bir oturum
iste) PR head'inde ayrı bir detached worktree'de çalışır. Commit/push yok; probe testlerini izlenmeyen dosya olarak
yazar.

**İstem şablonu (doldur):**
> Sen BAĞIMSIZ adversarial inceleyicisin. PR #<no> (<özet>) — ÇÜRÜT. Açıklamaya güvenme, ampirik kanıtla.
> Worktree `../demo-<kisa>-adv` (detached, head <sha>). Gerçek backend + yerel `racar` DB, firma `yucerent`:
> - pilot bayrağını test için aç, bitince KAPAT;
> - test kullanıcılarını rastgele parolayla üret, bitince SİL;
> - Web sürecini durdur.
>
> Saldırılar:
> 1. Çift sayım / mükerrer: aynı anahtar aynı ya da farklı içerik, eşzamanlı N gönderim, iki kullanıcı, iki sekme,
>    **kaybolan yanıt sonrası tekrar**.
> 2. Defter dengesi Σ borç = Σ alacak + işaret + cari bakiye (elle kurulmuş oracle).
> 3. Çok döviz ve kur (TRY'de kur≠1).
> 4. Yetki / şube kapsamı / RLS (başka şube 403, başka kiracı 404, izinsiz rol).
> 5. Giriş doğrulama ve 500 üreten girdiler (taşma, uzun metin, negatif, 0).
> 6. Hata sözleşmesi (`dogrulama`/`yetki_yok`/`cakisma`/`mukerrer`, CSRF).
> 7. SPA:
>    - tr sayı biçimi ("1.500,50"), otomatik/Tab/fare odağında ekleme;
>    - satır değişince eski tutar (`@if` örnek korunması);
>    - bayat anahtar/sürüm, dokunmadan kaydet → aç → kaydet kayması;
>    - localStorage'da PII.
>
> Rapor: her bulgu ŞİDDET (Critical/High/Medium/Low) + kanıt (probe + çıktı/SQL) + dosya:satır + öneri.
> Son satır "Critical/High/Medium: N" ve doğrulanan SHA.

Düzeltmeden sonra AYNI inceleyici yeniden doğrular. Düzeltmenin yeni açık getirmediğine özellikle bakar; bu
geçişte iki kez oldu. Para dokunmayan ama giriş/yetki/PII dokunan PR'larda aynı yöntemle güvenlik odaklı inceleme.

## 5. Kalıcı dersler (bu geçişte bedeli ödendi — tekrar etme)

**Backend**
- **Idempotency sırası:** önce "bu anahtarla kayıt var mı" (varsa 409 `mukerrer` + `mevcut{id, belgeNo, tutar,
  doviz, ayniIcerik}`), SONRA "anahtar bayat mı". Ters sıra, yanıtı kaybolan isteğin doğru tekrarını ikinci
  tahsilata yönlendirdi (gerçek DB'de 2×500).
- **Tahsilat anahtarı:** deterministik `tahsilatAnahtar` (kira + bakiye + işlem sayısı) sunucuda yeniden hesaplanır;
  başka kiranın ya da bayat anahtar 409 alır. Başlık anahtarı sunucuda UUIDv5(tenant|user|başlık) olur.
  `cakisma` ≠ `mukerrer`.
- **Kur:** TRY işlemde açık kur ≠ 1 reddedilir (`KurCozucu`). Yoksa baz tutar şişer ve defter dengeli göründüğü için
  yakalanmaz.
- **Kira satırını değiştiren her yol** kilit alır (`KiraKilitleri`, `FOR UPDATE`, sıra advisory → satır) ve durumu kilit
  altında yeniden okur. `Invoices`'ta `Rentals`'a FK YOK; "KEY SHARE alır" varsayımı yanlıştı.
- **Tam değiştirme PUT'u iyimser eşzamanlılık ister:** zorunlu `surum`, kilit altında karşılaştırma, uyuşmazlık
  409 `cakisma`. Kalıcı SPA sekmesiyle bayat form başka oturumun para değişikliğini sessizce eziyordu.
- **DB'ye giden her `DateTimeOffset` UTC olmalı.** Gün sınırı İstanbul'da (`TenantGun`) hesaplanır, parametre UTC gider.
  +03:00 parametre müşteri bildirimlerini 5 hafta sessizce durdurdu. İş testleri üretimdeki `now` + saat dilimiyle koşmalı.
- **Ofis adı eşleşmesi:** `OfisAdiAnahtari` tek anahtar. Postgres `lower('İ')` ≠ .NET `"İ".ToLowerInvariant()`.
- **Uç sınırları:** `numeric(19,4)` ve `varchar` uzunlukları uçta. `UiHata` 22001/22003 → 400 ağı sadece son savunmadır.
- **Yetki ve kapsam:** her `/api/ui` ucunda `IzinMetadata`/`IzinMuaf` (yapısal test). "OperationsWrite VEYA
  FinanceWrite" okuma için `RequireAnyPermission`. Alt kayıt uçları üst kaydın şube kapsamından geçer. Kapsam
  kontrolü durum kontrolünden ÖNCE yapılır (başka şubenin kaydının durumu sızmasın).
- **Varlık kontrolü:** var olmayan ya da başka kiracının cari/araç kimliğiyle para veya kira yazılamaz (RLS kapsamlı
  `FindAsync`). Kirada kullanılan cari silinemez.
- **KVKK:**
  - `Api/Kira/MusteriGorunumu.cs` tek kural; `Anonim*` bayraklarının her biri yalnız kendi grubunu siler.
  - TC hiçbir `/api/ui` yanıtında dönmez.
  - Maske: ≥8 karakter → son 4; 5–7 → son 2; ≤4 → tümü `*`.
- **Testlerde:** sabit parola/kimlik YOK (GitGuardian geçmişi tarar); `WebFixture` rastgele parola. xUnit
  `ThrowsAsync<T>` birebir tip eşler. Linux CI'da 100ns tick → DB'ye yazılıp okunan tarihlerde whole-second taban.

**Frontend**
- **`@if (x(); as y)`** dolu → dolu geçişte bileşen örneğini korur. Satır başına form anahtarlı yeniden oluşturulmalı
  (`@for (…; track id)`). Yoksa eski satırın tutarı yeni satıra gönderildi (90 TL bakiyeye 500 TL).
- **`rc-para-girdisi`:**
  - Kullanıcının yazdığı >2 ondalık hata verir (`paraFazlaHane`); programatik değer yuvarlanır.
  - Dokunulmamış ön-dolu değerde odak (otomatik, Tab, fare) tüm metni seçer; kullanıcı yazdıktan sonra tıklanan
    yer korunur. Aksi halde "1250,5090" ve "5002600" gönderildi.
- **Para formu yaşam döngüsü:**
  - İşlem başına `Idempotency-Key`, 2xx sonrası yenilenir.
  - `tahsilatAnahtar`'lı istek ASLA anahtarsız tekrar edilmez.
  - Hata sonrası tekrar donmuş kopyayla AYNI anahtar ve gövdeyle yapılır.
  - 409'da otomatik yeniden gönderim yok.
  - `mevcut.ayniIcerik` ve "kendi tekrarım mı" ayrımına göre mesaj verilir; kullanıcıyı ikinci tahsilata
    yönlendiren metin yasak.
- **Kalıcı sekme:**
  - İşlem sonrası detay ve `surum` tazelenir; yenileme bitene kadar Kaydet pasiftir.
  - Dokunulmamış alanlara sunucu değeri birleştirilir.
  - Çakışma yoksa 409'da tek sessiz yeniden gönderim yapılır.
- **`sessiz` istek:** hiçbir hatayı yutmamalı; 403/429/5xx/ağ hataları form içinde gösterilir.
- **Çekirdek 409 bildirimi:** `istekBaglami({ mukerrerBasligi })`.
- **Harici bağlantılar:** yalnız `shared/dis-baglantilar.ts`'te; lint istisnası dar. Mutlak URL başka yerde yasak.
- **Paket bütçesi:** uyarı 380, hata 450 kB. Yeni ekranlar tembel rota parçası olmalı.
- **Geliştirme:** e2e varsayılan portu 4321 başka ajanda doluysa geçici Playwright config ile başka port kullan
  ve config'i commit'leme. `tablo.spec.ts:116` yalnız macOS'ta kırmızı, CI'da yeşil.

**Süreç ve araç**
- `gh pr checks --watch` erken çıkabilir; boş çıktı "bitti" değildir. Yalnız `scripts/pr-izle.sh` kullan; merge ayrı komutta.
- Merge'den sonra main CI'ını da kontrol et (`gh run list --branch main --limit 3`).
- Canlı ya da gerçek DB denemesinden sonra: pilot bayrağı `false`, Web süreci durdurulmuş, test kullanıcıları
  silinmiş olmalı. Değişmez mali kayıtlar silinmez, iptal/ters kayıtla kapatılır.
- `scripts/roadmap-envanteri.py --kontrol` main'de F4–F13 bloklarında fark bildirir. Taban 2026-09-21'de donduruldu;
  CI kontrol etmez, "temiz" diye yazma.
- Yerel çalıştırma: `CLAUDE.md` §7. Seed parolası repoda yok (`dotnet user-secrets` `Seed:Parola` ya da açılış
  logundaki tek seferlik WARNING satırı). Betikler `RACAR_GIRIS_SIFRE` kullanır.

## 6. Low temizliği kuyruğu (tek PR ya da fazların içine)

**Para ve backend** (para dokunanlar → §4)
- R04 `FaturaDonemFiles`: mevcut RowKey'li kaydın tutar ve dövizi de karşılaştırılsın (Blazor ham anahtarla dönem
  tahsilatını önden alabiliyor).
- N4 `DonemFaturaUretici`: kilitten sonra kira durumu yeniden denetlensin (iş iptal kiraya dönem faturası kesebiliyor).
- Blazor `BatchCollect`/`BatchPay`: cari varlık kontrolü yok (F8'de).
- Ek hizmet ekleme anahtarsız (çift gönderim iki kalem; SPA kilidine bağlı).
- Kira oluşturma atomik değil: ücret satırları ve dönem planı ayrı adımda.
- `RentalsApi` (harici): yabancı ya da olmayan müşteri/araç kontrolü yok; kalıcı çözüm bileşik FK.
  45 test sentetik kimlik kullanıyor.
- Ofis adları normalize anahtarda tekil değil (kiracı başına tekillik kısıtı).
- Yakıt ölçeği: kullanıcı kararı bekliyor (§1).
- Üretimde bağlama hatası `kod`suz 400 dönüyor: `ThrowOnBadRequest` yalnız Development'ta açık.
- 22001/22003 güvenlik ağı Warning seviyesinde loglanmalı.

**KVKK ve yetki**
- `AnonimAd`: kira listesi, Panel ve `secim/musteri*` müşteri adı bayrağı okumuyor.
- Kullanıcı bazlı izin yasağı Blazor sayfalarını kapatmıyor (yalnız `[Authorize]`, 41 rota). SPA'da veri `/api/ui`
  izinleriyle korunur; Blazor F13'te kalkar.

**SPA**
- L5: kira detay yenilemesi 5xx dönerse finans paneli kayboluyor.
- Muhasebe rolü dönem planını okuyamıyor (Blazor paritesi; dokunma).

## 7. Faz haritası (kalan)

| Faz | Kapsam | PR | Para |
|---|---|---|---|
| F4 | #263, #264, kapanış; pilot sonrası F4.6b | — | ✔ |
| F5 | Rezervasyon, takvim, müsaitlik, rez şartları, teklifler, filo kiralama (6 sayfa) | 4 | — |
| F6 | Araçlar (14) | 5 | ✔ kredi + müşteri taksit |
| F7 | Cariler & CRM (8) | 3 | — |
| F8 | Finans + cari ekstre + fatura yazdır (19) | 6 | ✔ her PR |
| F9 | Servis & sigorta + vade + fiyat & tarife (15) | 5 | ✔ ödeme ve yansıtma |
| F10 | Raporlar (26; ortak rapor şablonu) | 4 | — |
| F11 | Tanımlar (genel CRUD) + sistem + web sitesi (~47) | 6 | — |
| F12 | Platform konsolu (4 + `/api/ui/v1/platform/*`) | 2 | — |
| F13 | Blazor söküm + belgeler | 2 | — |

Her modül fazı: (1) backend uçları, fazın kendi seçim/typeahead uçları dahil → (2) ekranlar → (3) parite + e2e →
(4) kesiş PR'ı (yönlendirme şablonları + menü sahibi `spa` + guard testleri devri; Blazor sayfalarının silinmesi
pilot doğrulamasından sonra). Ayrıntı her `F*.md` "Kalıp" bölümünde.
