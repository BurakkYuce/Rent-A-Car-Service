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

Güncelleme: 2026-09-25 sabah. **F4–F11'in KODU ve KESİŞİ main'de**; F12'nin ekranları main'de. Tenant sayfalarından
Blazor'da yalnız `/yetkisiz` ve `/hata` kaldı (hepsinin SPA karşılığı var). Kalan: F12 kesiş (canlı parite
kullanıcıda), F13 söküm (pilot sonrası), birkaç Low. Faz sırası kilidi F6–F12 için GEVŞETİLDİ (`DEGISIKLIKLER.md`).
**Pilot kapalı** — kullanıcılar hâlâ Blazor; kesiş yönlendirmeleri yalnız pilot kiracıda çalışır.

**DEVAM EDERKEN İLK İŞ:** `rtk gh pr list --state open` ile açık PR'lara bak (aşağıdaki "Açık PR" satırı bayatlamış
olabilir). Açık PR yoksa "Sırada" listesinin ilk maddesi.

### ✅ Bitti
- G0, F0 (#235 #237), F1 (#238–#240 #243–#245), F2.1 (#241), F2.2 kodu (#249), F3 (#248 #250–#255, kapanış #256).
- F4.1 kira + panel uçları (#258), F4.4a finans uçları `/api/ui/v1/finans` (#257), F4.2 kira listesi (#260),
  F4.5 Panel (#259), F4.3 kira formu I (#261), F4.3b kira formu parite ekleri (#262).
- **F4.4 kira formu II — sabit finans paneli (#263, PARA).** 6 adversarial turu; son tur Critical/High/Medium: 0.
  HIGH-1 (kaybolan yanıt sonrası ikinci tahsilat) → sunucuda "önce mevcut kayıt, sonra bayatlık" sırası +
  `mevcut{…, ayniIcerik}`; M-A (iki sekme), M-B (ön-dolu tutara fare tıklaması), M-C (tutarı değiştirilmiş tekrar)
  ve MEDIUM-1 (belirsiz tahsilat denemesi artık ANAHTARA bağlı) kapatıldı.
- **F4.6 ilk kesiş — mekanik (#264).** Platform konsolunda pilot anahtarı, tek giriş `/login` → `/app/giris`,
  5 şablonluk GET yönlendirme haritası (sorgu korunur; PDF/hesap/export yönlenmez), menü kayıttan (rol → izin),
  test devri, `mobil-tasma` SPA girişi. Güvenlik incelemesi temiz; pilotta finans paneli e2e ile doğrulandı.
  **Pilot anahtarı kapalı** — açmak kullanıcıya ait.
- **Çekirdek eki: rota bazlı tembel çeviri (#268).** `tr.json` artık ilk pakete gömülü değil; ilk paket
  395 → 372,5 kB (uyarı 380, hata 450). Yeni ekranlar çevirilerini kendi rota parçalarında yükler.
- **F4 kapanışı (#270):** kod tamam; Exit'in pilot maddesi ve F4.6b açık.
- **F5.1 rezervasyon modülü uçları (#271).** 2 adversarial tur: High (teklif kabul yarışı — iki eşzamanlı kabul
  iki rezervasyon üretebiliyordu) + 2 Medium (filo tarih/kapsam, müsaitlik saati) düzeltildi.
- **F5.2b planlama ekranları (#273):** takvim, müsaitlik, rez şartları, filo kiralama (+ #271 Low-1 filo künye
  tarihi kapandı).
- **F5.2a rezervasyon ve teklif ekranları (#272):** liste, form, detay + durum eylemleri.
- **F5.3 parite + gerçek backend e2e (#274)** (`docs/roadmap/F5-parite.md`) ve **F5.4 kesiş (#276)** — güvenlik
  incelemesi temiz. **F5 KODU BİTTİ**; Blazor rezervasyon sayfalarının silinmesi pilot sonrası (F4.6b deseni).
- **Low temizliği B (#277, PARA)** ve **Low temizliği A (#280, KVKK)** — adversarial/KVKK incelemeleri temiz.
- **F6.1a araç çekirdek uçları (#278)**, **F6.1b araç para uçları (#279, PARA; sipariş durum makinesi Medium
  düzeltildi)**, **F6.2a araç ekranları (#285)** (+ #278 Low'ları; tarih/tutar kuralı yalnız DEĞİŞEN alana).
- **F12.1 platform konsolu uçları (#281)** — güvenlik incelemesi temiz.
- **CI hafifletme (#289):** yol filtresi (`changes` işi), aynı PR'da eski koşu iptali, frontend+e2e tek iş.
  Kontrol adları artık: changes, build-test, mobil-tasma, frontend, deploy-betikleri (+ spa-surum main'de).
  Atlanan iş SKIPPED görünür (normal). Actions kotası (3.000 dk/ay) 2026-09-23'te %90 dolmuştu; 1 Ekim'de sıfırlanır.
- Yan düzeltmeler: #265 (müşteri bildirimleri + WhatsApp özeti 2026-08-17'den beri çalışmıyordu — UTC),
  #266 (iş koşu günlüğü hata satırı + üretici yalıtımı + üretici başına `racar_job_fail` metriği).
- **2026-09-24 gece serisi (hepsi bağımsız inceleme + CI yeşil ile):**
  - Backend: F7.1 #283 (KVKK), F8.1a #284 ve F8.1b #286 (PARA), F9.1 #292 (PARA), F10.1 #287, F11.1a #282,
    F11.1b #288 (4 güvenlik turu), F12.1 #281.
  - F6: F6.2b araç finans ekranları #291, F6.3 parite + e2e #294, F6.4 kesiş #298 → **F6 KODU BİTTİ.**
  - F7: F7.2 cari/CRM ekranları #295 (3 KVKK turu; tip değişiminde vergi no/TC ifşası ve assistans relink
    sızıntısı kapandı), F7.3 parite + kesiş #307 → **F7 KODU BİTTİ.**
  - F8: F8.2a kasa/banka #299, F8.2b fatura/ceza/gider/gelen e-fatura/satış #300 (üç para turu).
  - F9: F9.2 servis/sigorta/vade/fiyat-tarife ekranları #301 (iki para turu).
  - F10: F10.2 26 rapor tek ortak ekran #296, F10.3a vardiya yazma uçları #302, F10.3b parite + kesiş #303
    → **F10 KODU BİTTİ.**
  - F11: F11.2a tanımlar + `rc-tanim-crud` surum/409 çekirdek eki #297, F11.2b sistem/web #304 (kendi parolasını
    eski parolasız sıfırlama kapandı), F11.2c kalan 11 tanım #306, F11.2d personel + içe aktar #308 (içe aktarım
    bellek sınırları: CSV + xlsx akış sayımı, TC boşlukla silinmez).
  - F12: F12.2 platform ekranları #293.
  - Yan: #289 CI hafifletme, #290 belge, #305 e2e "tüm sayfalar" taramaları sayfa başına bölündü (CI 30 sn sınırı).
- **2026-09-25 serisi (hepsi bağımsız inceleme + CI yeşil ile):**
  - Kesişler: F9 #310, F8 #311, F11 #312 (47/47 F11 ekranı SPA'da) → **F8, F9, F11 KODU BİTTİ.**
  - Backend Low'lar: #313 (sır temizleme bayrağı, Admin ekran kilidi, vardiya mesajı, ShiftApi sürüm-satır-sürüm, CSV
    formül kaçışı), #314 (depozito anahtarı tüm türlerde tekil + anahtar kilidi, xlsx tüm girdileri kodlamadan
    bağımsız tarar, `DueItemDto` → `MessageDueItemDto` + şema adı benzersizlik testi), #319 (denetim sır maskesi tek
    kural `AuditSecretMask`, vardiya kapsam dışı personele 403, pasif override Admin kuralı, sabit kur TOCTOU, UCS-4,
    CSV baştaki boşluk), #321 (denetimde sürücü belgesi + nüfus cüzdanı alanları maskeli).
  - Eksik uçlar: #315 (finans: secim/kira FinanceWrite, gider kategori seçimi, fatura döviz özeti, satılabilir araç,
    ceza belgeNo, toplu faturalama seçimi), #317 (servis sayaçları + KDV/toplam, tüm zeyiller, CRM şube kapsamı SERVİS
    katmanında — Blazor CRM formları başka şubenin kaydını değiştirip silebiliyordu, CANLI açıktı; oluşturmada şube
    hedefi zorunlu; AnonimBelge'de doğum tarihi maskeli).
  - **SPA para çekirdeği #316:** dört ayrı uygulama tek `core/form/money-submission.ts` + `money-attempts.ts` +
    `money-notice.ts`'te; denemeler oturum KİMLİĞİNE (kiracı|kullanıcı) bağlı, sekmeler arası `rc-oturum-baglami`
    kanalı, tekrar öncesi `GET oturum/ben` doğrulaması. #320: bağlam anahtarı tek kaynak (`core/oturum/oturum-baglami.ts`)
    + parite testi, 401'de yeniden giriş + aynı kimlikle aynı anahtar, şube değişimi denemeyi düşürmez.
  - #318 kira paneli: "H1" test konumlayıcı yarışıydı (ürün hatası değil); tahsilat sürerken/tazeleme beklerken iki
    Tahsil Et pasif, 5xx'te "Yeniden yükle", tazelemede son iyi detay korunur (donmuş deneme kaybolmaz), 429 geçici.
  - Belge: #309 DEVIR güncellemesi.

### ⏳ Açık PR
- Yok (2026-09-25 sabah). Gerçek durum için `rtk gh pr list --state open`.

### ⬜ Sırada (başlamadı)
1. **F12 kesiş:** canlı parite kontrolü kullanıcıda; sonra kesiş PR'ı.
2. **Low kalıntıları** (§6 "2026-09-25 Low'ları").
3. **F4.6b / F5–F11 Blazor sayfa silme ve F13 söküm:** YALNIZ pilotta 10 iş günü P1 olmadıktan SONRA. Blazor'da
   kapatılmamış bilinen okuma sızıntıları F13'e kadar canlı: CRM liste sayfaları tüm şubeleri gösteriyor,
   `CustomerEdit.razor` anonimleştirme maskesi uygulamıyor.
4. **İngilizce adlandırma toplu dönüşümü:** kullanıcı "şimdi düzeltme, sonra yaparsın" dedi (2026-09-23).
   Başka iş koşarken yapılamaz (her dosyaya dokunur); zamanlamayı kullanıcıya sor.

### 🧑 Kullanıcıda bekleyenler (cevap gelmeden ilgili işe dokunma)
- **F2.2 sunucu adımları:** `docs/ops/f2-2-sunucu-adimlari.md`. Bitmeden `/app` üretimde yok, pilot açılamaz.
- referans sistem parolası değişimi + GitGuardian olayı 37502190'ın kapatılması.
- **GitGuardian 37582605 YANLIŞ ALARM** — `IlkKesisTests.cs` yönlendirme yol listesinde `"kullanicilar"` ile
  `"profil/sifre-degistir"` yan yana; kimlik bilgisi yok. Main'i birleştiren her PR'da kırmızı görünür; panelde
  "false positive" işaretlenmeli. İşaretlenene kadar yalnız bu olay kırmızıysa merge engeli değildir.
- **Karar (6): kirasız assistans talebi.** #317'den beri şube kapsamlı operatör kira seçmeden assistans talebi açamıyor
  (assistansta ofis alanı yok; şubesiz kayıt tüm şubelere açılıyordu). Böyle mi kalsın?
- ~~Karar (1)~~ ve ~~Karar (2)~~ **KAPANDI** — kullanıcı 2026-09-22'de oturumda doğrudan verdi
  (`DEGISIKLIKLER.md`): F5, F4'ün "pilotta 10 iş günü P1 yok" Exit'ini beklemeden başlar; #264 menü izin eşlemesi
  (Operatör 79 → 74, Muhasebe 48 → 53) onaylandı.
- **Karar (4) YENİ: rezervasyon güncellemesinde doluluk çarpanı (surge).** `CLAUDE.md` "rezervasyon-update'te
  surge atlanır" diyor; ama `ReservationService.UpdateAsync:181-182` `dolulukUygula:false` geçmiyor ve kod
  yorumu bunu bilinçli diyor. Hangisi doğru? Karar gelmeden bu davranışa dokunma.
- **Karar (5) YENİ: cari ekstre ucunun kapısı.** Cari detay ucu bakiyeyi yalnız FinanceWrite ∨ ViewReports ile
  döndürüyor; `/api/ui/v1/finans/cariler/{id}/ekstre` ise OperationsWrite ile de açık (Blazor paritesi) ve operatöre
  başka şubenin bakiyesini + sözleşme no'larını gösteriyor. SPA'da ekstre sekmesi, rotası ve bağlantıları artık
  FinanceWrite ∨ ViewReports ile kapılı (#295, #299, #307). Sunucu ucu da daraltılsın mı? Karar gelmeden uca dokunma.
- **Karar (3): yakıt ölçeği (hâlâ açık).** Servis ve harici API 0–100, formlar ve referans sistem 0–12 kullanıyor. Önerilen: tek
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
7. **Kapılar (yerel) — HIZ KURALI (kullanıcı kararı 2026-09-23):** yerelde TAM paket koşulmaz, yalnız dokunulan
   şeyin testleri.
   - Backend: filtreli sınıf —
     `RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=burak;Database=postgres" rtk test dotnet test tests/RentACar.IntegrationTests -c Debug --filter "FullyQualifiedName~<Sinif>"`
     → "Failed: 0" açıkça.
   - Frontend: değişen Vitest dosyaları (`rtk npx -p node@22 npm test -- <dosya>`; **Node 22** — Node 26'da jsdom
     `localStorage` bozuk) + `rtk npm run lint` + `rtk npm run typecheck` + değişen/yeni e2e spec dosyaları.
   - **Tam backend, tam e2e ve build yalnız CI'da koşar.**
   - Worktree'lerde `npm ci` YAPILMAZ; ana repodaki `src/RentACar.Frontend/node_modules` symlink'lenir
     (`rtk run 'ln -s /Users/burak/Desktop/demo-apps/demo/src/RentACar.Frontend/node_modules <worktree>/src/RentACar.Frontend/node_modules'`).
     Hedefte `node_modules/.bin/ng` yoksa kurulum sürüyordur, 1-2 dk bekle.
   - Uzun komutu `run_in_background` ile koş, log dosyasını `rtk read <log> --tail-lines N` ile oku; aksi halde ajan
     izleyicisi 600 sn sessizlikte ajanı düşürür.
8. **Commit/PR:**
   - Commit mesajı Türkçe ve ayrıntılı (ne + neden + test özeti); kurallar `CLAUDE.md` §3.
   - `git push -u origin <dal>`, sonra `gh pr create --base main`. PR açıklamasına riskli noktaları ve
     adversarial hedeflerini yaz.
9. **CI:**
   - **Ajan CI izlemez:** push → PR → "PR no + head SHA" raporu ve biter. CI'ı koordinatör izler.
   - Koordinatör: `scripts/pr-izle.sh <pr> 6 <head-sha-önek>` yalnız izler, merge etmez. Çıktıyı OKU.
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

### Araç kuralı (kullanıcı zorunlu kıldı — istisna yok)
Alt ajan talimatlarına **aynen** yazılır.
- Tüm Bash komutları `rtk` ile: `rtk git …`, `rtk gh …`, `rtk npm run …`, `rtk npx …`, `rtk test dotnet test …`;
  karşılığı yoksa `rtk run '<cmd>'`; çıktı bozuk ya da boşsa `rtk proxy <cmd>`.
- Grep ve Glob araçları YASAK. Çıplak `grep`/`find`/`cat`/`head`/`tail`/`sed`/`awk` ve heredoc YASAK.
- Arama `rtk grep -rn "<desen>" <dizin>`; dosya bulma `rtk find <dizin> -name "<desen>"`; okuma `rtk read <dosya>`.
- Dosyanın ortası: Read aracı `offset`/`limit` ile. `sed -n` rtk'yı atlar, kullanma.
- Log sonu `rtk read <dosya> --tail-lines N`; `-m` ile `--tail-lines` birlikte verilmez.
- Testler `rtk test …`; kesilmiş çıktının tamamı `rtk recall <id>`.
- Dosya düzenleme yalnız Edit/Write aracıyla. Commit mesajı Write ile scratchpad'e yazılır, sonra
  `rtk git commit -F <dosya>`.

## 3. Paralel mod (isteğe bağlı, hız için)

Kullanıcının tercihi: "workflow değil, arkada subagent aç, paralel; amaç hız". Desen:
- Her PR için ayrı worktree + arka plan ajanı. Ajan kendi PR'ını açar, **merge etmez**, CI izlemez.
- Aynı anda **en fazla 4 ajan** (inceleme ajanları dahil). 2026-09-23'te 8 ajan 16 GB'lık Mac'i aşırı ısıttı;
  paylaşılan derleyici sunucusu (VBCSCompiler) 12 GB'a çıktı. Kullanıcı daha fazlasını isterse bu riski önceden söyle.
- **Bellek kuralı:** her ajan tüm `dotnet build/test/run` komutlarının önüne
  `UseSharedCompilation=false DOTNET_CLI_USE_MSBUILD_SERVER=0` koyar. Şişme görülürse koordinatör
  `dotnet build-server shutdown` çalıştırır; güvenli, Roslyn süreç içi derlemeye düşer.
- **Alt ajan (fork) açma yasak.** Ajan işini kendisi yapar.
- **Adlandırma kuralı (CLAUDE.md §2):** yeni sınıf/metot/değişken/dosya adları İNGİLİZCE; yalnız domain alanları,
  form alanları, JSON alanları ve kullanıcı metinleri Türkçe. Çevredeki Türkçe adlı kod örnek alınmaz; mevcut adlar
  yeniden adlandırılmaz (toplu dönüşüm ayrı iş, §1 Sırada).
- **Push öncesi yapısal testler zorunlu** (birkaç saniye; CI kırmızılarının çoğu bunlardan çıktı):
  `TestTarihBombasi|GuvenliDonus|UcIzinKapsama|ModelGuard|UiApiTests|UiApiYapisal|UiApiOpenApi` +
  `npm run format:check` + (OpenAPI değiştiyse) `npm run tipler:kontrol`.
- Her ajanın talimatına **ortak kural dosyası** (scratchpad'de; araç kuralı + hız kuralı + bellek kuralı +
  adlandırma kuralı + proje kuralları özeti, "AYNEN uygula") ve ajana özgü bir **önek** (scratchpad dosya adları
  için, ör. `doc-`) yazılır. Scratchpad oturuma özeldir; yeni oturumda dosyayı bu maddelerden yeniden yaz.
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
- **2026-09-24 gecesi her SPA para PR'ı (#299, #300, #301) aynı üç hatayı AYRI AYRI yaptı** — yeni para formunda
  baştan uygula:
  - `mevcut`'suz 409 `mukerrer` (yarış kaybı, başka uç) "bayat anahtar / yazılmadı" SANILMAZ: "daha önce kaydedildi;
    ikinci kez yazılmadı, hareketleri kontrol edin". Anahtar yenilenmez.
  - İstek UÇUŞTAYKEN form kilitli (yalnız donmuşken değil); donmuş gövde forma geri yazılır. Uçuşta formu yok eden
    her düğme (Kapat, başka satır, filtre) pasif; deneme gönderimden ÖNCE sayfa düzeyinde "uçuşta" kaydedilir.
  - Döviz değişince kur, dövizi uymayan hesap ve TRY bakiye önerisi temizlenir. "Boş tutar = kalanın tamamı"
    formunda `mukerrer` sonrası kilit yalnız BAŞARILI ödemeyle kalkar.
- **Çakışan PR'da CI hiç koşmaz** (`pull_request` olayı merge commit'i ister). "CI bekleniyor" demeden önce
  `gh pr view N --json mergeable`. Paralel PR'lar `sayfalar.ts`, `BLOK_HARITASI` ve üretilen i18n'de sürekli çakışır:
  elle yazılanda iki tarafı koru (`git merge-file --union`), üretilenleri yeniden üret.
- **Tek testte "tüm sayfalar × iki tema axe" CI'da 30 sn'yi aşar** → sayfa başına ayrı test (#305).
- **İnceleyiciler yerel dev DB'ye yazmaz** (WebFixture ya da sahte API). Bir tur dev DB'ye gerçek tahsilat yazdı;
  ters kayıtla kapatıldı ama değişmez defterde kaldı.
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
- **graphify grafı YERELDİR, repoda değildir.** `graphify-out/` ve `.graphifyignore`, `.git/info/exclude` ile hariç
  tutulmuştur (graf ~30 MB ve her commit'te baştan yazılır; repoya girseydi her PR'da devasa ikili değişiklik olurdu).
  Bu makinede yerel `post-commit`/`post-checkout` hook'ları arka planda tazeler; CI ve PR'ın graftan haberi yoktur.
  Başka bir klonda ya da makinede graf YOKTUR: ya kurarsın ya da doğrudan `rg`/`grep` ile ararsın.
  Graf **kod-only**: migration'lar, `tests/`, `*.md`/`*.yml` ve görseller dışarıda — "grafta yok" ≠ "repoda yok".
- `scripts/roadmap-envanteri.py --kontrol` main'de F4–F13 bloklarında fark bildirir. Taban 2026-09-21'de donduruldu;
  CI kontrol etmez, "temiz" diye yazma.
- Yerel çalıştırma: `CLAUDE.md` §7. Seed parolası repoda yok (`dotnet user-secrets` `Seed:Parola` ya da açılış
  logundaki tek seferlik WARNING satırı). Betikler `RACAR_GIRIS_SIFRE` kullanır.

## 6. Low temizliği kuyruğu (tek PR ya da fazların içine)

Durum 2026-09-25: **Low A (#280) ve Low B (#277) main'de** — "[sürüyor — Low A/B]" etiketli maddeler KAPANDI.
Etiketsiz madde açıktır.

**Para ve backend** (para dokunanlar → §4)
- [sürüyor — Low B] R04 `FaturaDonemFiles`: mevcut RowKey'li kaydın tutar ve dövizi de karşılaştırılsın (Blazor
  ham anahtarla dönem tahsilatını önden alabiliyor).
- [sürüyor — Low B] N4 `DonemFaturaUretici`: kilitten sonra kira durumu yeniden denetlensin (iş iptal kiraya dönem
  faturası kesebiliyor).
- [sürüyor — Low B] `FinansApi`: +03:00 tarih parametresi 500 üretiyor (DB'ye giden tarih UTC olmalı, §5).
- Blazor `BatchCollect`/`BatchPay`: cari varlık kontrolü yok (F8'de).
- Ek hizmet ekleme anahtarsız (çift gönderim iki kalem; SPA kilidine bağlı).
- **Kira oluşturma atomik değil:** ücret satırları ve dönem planı ayrı adımda (açık, ayrı iş — Low PR'larına girmez).
- `RentalsApi` (harici): yabancı ya da olmayan müşteri/araç kontrolü yok; kalıcı çözüm bileşik FK.
  45 test sentetik kimlik kullanıyor.
- Ofis adları normalize anahtarda tekil değil (kiracı başına tekillik kısıtı).
- Yakıt ölçeği: kullanıcı kararı bekliyor (§1).
- ~~Üretimde bağlama hatası `kod`suz 400~~ KAPALI (doğrulandı 2026-09-25): `UiApiExtensions.GenelProblem` 400'ü `dogrulama` koduyla döner.
- ~~22001/22003 güvenlik ağı Warning~~ KAPALI (doğrulandı 2026-09-25): `UiApiExtensions.LogSeviyesi`.

**KVKK ve yetki**
- [sürüyor — Low A] `AnonimAd`: kira listesi, Panel ve `secim/musteri*` müşteri adı bayrağı okumuyor.
- Kullanıcı bazlı izin yasağı Blazor sayfalarını kapatmıyor (yalnız `[Authorize]`, 41 rota). SPA'da veri `/api/ui`
  izinleriyle korunur; Blazor F13'te kalkar.
- ~~`DUGME_IZINLERI`'nde `rezervasyonIptal` yok~~ GEREKSİZ (doğrulandı 2026-09-25): iptal düğmesi sunucunun hesapladığı
  `yetkiler.iptal`'e bağlı (`RezervasyonApi` → OperationsDelete); statik haritadan daha sıkı.

**SPA**
- [sürüyor — Low A] L5: kira detay yenilemesi 5xx dönerse finans paneli kayboluyor.
- [sürüyor — Low A] `TahsilatDenemeKaydi` sekmeler arası davranışı.
- [sürüyor — Low A] Filo aracı silinince kaydın görünmez olması.
- [sürüyor — Low A] Teklif kabulü 409'unda `mevcut` bilgisinin ele alınması.
- Muhasebe rolü dönem planını okuyamıyor (Blazor paritesi; dokunma).

**2026-09-25 Low'ları (açık)**
- SPA: donmuş denemenin tekrarı kesin redle (403/400) dönünce "önceki denemenin sonucu bilinmiyor" notu siliniyor
  (#320 L1; satış formundaki `rejected` kancasının genel hali). Tekrar öncesi doğrulama ile POST arasında
  milisaniyelik TOCTOU (#320 L2; kalıcı çözüm sunucuda beklenen kullanıcı başlığı). Sekmeler arası aynı kullanıcının
  şube değişimi `ben`'i yenilemiyor (görünüm, para riski yok).
- Backend: firmanın kendi IBAN/VKN'si denetimde tamamen `***` — IBAN değişikliği dolandırıcılık izi için kısmi maske
  (son 4 hane) ya da KARARLAR kaydı (#319 L2). ~~Personel seçim listesi şubeye göre süzülmüyor~~ KAPALI
  (doğrulandı 2026-09-25): `/secim/personel` `SecimService.PersonelAsync`'te `BranchScope.InScope` ile süzülür. Kira paneli: Nakit sonuçlanınca kirli Kart formu yeni anahtarla
  İKİNCİ tahsilat olarak yazılıyor (#318 L2 tasarımı; isteğe bağlı "diğer formun tahsilatı yazıldı" notu).
- Kullanıcı kararı bekleyen: şube kapsamlı operatör kirasız assistans talebi açamıyor (#317 L1 yan etkisi).

**2026-09-24 gece Low'ları — 2026-09-25'te KAPANDI** (#313, #314, #315, #316, #317, #319, #321)
- Backend: sır temizleme bayrağı (#304 L2); ekran override'ında Admin'e dokunma yalnız Admin'e (#304 L3); vardiya
  çakışma mesajı başka şubenin saatini sızdırıyor (#302 L1); `ShiftApi.DtoAsync` satır + sürüm ayrı sorgu (#302 L2);
  depozito Idempotency anahtarı işlem türleri arası (#299 L2); xlsx sayımı `xl/worksheets/*.xml` yoluna bağlı
  (#308 L1); CSV dışa aktarımda formül kaçışı (#308 L2); `DueItemDto` OpenAPI şema adı çakışması (#301).
- SPA: `followCustomerQuery` kirli formda cariyi uyarısız değiştiriyor + bakiye düzeltme onay metninde cari adı yok
  (#299 L-new-1); gider listesinde uçuşta filtre formu yok edebiliyor (#300 L1); satış bağlam notu yalnız "zaten
  satılmış" reddinde (#300 L2); toplu faturalamada görünmeyen kiralar seçimde kalıyor (#300 L5); ceza ödemesi
  `mevcut.belgeNo` yalnız sıra (#300 L2 eski); CRM kaydı boş hedefle tüm şubelere açılabiliyor (#295 L3, backend
  kemeri gerekir); CRM analizinde AnonimBelge'de DogumTarihi (#295 bilgi); tarife aktarım kanal silmeye beklenen
  adet (#301, isteğe bağlı eklemeli).
- Eksik uçlar (ekranlar geçici çözümle çalışıyor): FinanceWrite'lı `secim/kira` ve gider kategori seçimi, fatura
  döviz özeti, satılabilir araç seçimi (#300); servis listesi durum sayaçları + KDV/genel toplam, tüm zeyiller
  listesi (#301).

**Bilgi / kapandı**
- Blazor müsaitlik ekranı saati UTC sayıyor — bilgi; Blazor F13'te kalkar, düzeltilmez.
- ~~#271 Low-1 filo künye tarihi~~ KAPANDI (#273).

## 7. Faz haritası (kalan)

| Faz | Kapsam | PR | Para |
|---|---|---|---|
| F4 | kod ✔ (#263, #264, #270); pilot sonrası F4.6b | — | ✔ |
| F5 | Rezervasyon, takvim, müsaitlik, rez şartları, teklifler, filo kiralama (6 sayfa) — sürüyor | 5 (2a/2b bölündü) | — |
| F6 | Araçlar (14) — F6.1a/F6.1b sürüyor | 6 (1a/1b bölündü) | ✔ kredi + müşteri taksit |
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
