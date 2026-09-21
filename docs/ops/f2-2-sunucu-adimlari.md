# F2.2 — Sunucu adımları (yeni arayüz artifact'ı ile yayın)

> Bu belge, F2.2 `main`'e girdikten sonra **sunucu sahibinin elle yapacağı** adımlardır. Sıra önemlidir.
> Genel kurulum ve güncelleme akışı: [deploy-checklist.md](deploy-checklist.md) (§1, §3, §10).
>
> Aşağıda `<repo>` sunucudaki repo checkout'u (ör. `/opt/racar/src`), `<host>` ERP alan adıdır
> (ör. `erp.senindomainin.com`).

**Ne değişti:** Yeni arayüz (SPA) sunucuda DERLENMEZ, sunucuda Node olmaz. CI, `main`'e her push'ta
(tüm kapılar yeşilse) SPA'yı derleyip `spa-<sha>` adlı GitHub release'ine yükler. `deploy/yayinla.sh`
checkout edilen commit'in artifact'ını **salt-okur** bir token ile indirir, sha256 doğrular ve
`/opt/racar/releases/<zaman>/app/`'e açar. Token yoksa, artifact yoksa ya da checksum tutmazsa yayın
**reddedilir** ve çalışan sürüme dokunulmaz.

---

## 0. Ön kontrol (GitHub'da, tarayıcıdan)

1. Repo → **Actions** → `CI` → `main`'deki son koşu: **tüm işler yeşil**, `spa-surum` işi dahil.
2. Repo → **Releases**: `spa-<40 haneli sha>` adlı ön-sürüm var; içinde üç dosya:
   `spa-<sha>.tar.gz`, `spa-<sha>.tar.gz.sha256`, `chunks.txt`.

Release yoksa sunucuda yayın yapılamaz (bilinçli: "artifact var" = "o commit'in CI'ı yeşil").

## 1. Salt-okur GitHub token'ı oluştur (fine-grained)

1. GitHub → sağ üst profil → **Settings** → **Developer settings** → **Personal access tokens** →
   **Fine-grained tokens** → **Generate new token**.
2. Doldur:
   - **Token name:** `racar-sunucu-spa-okuma`
   - **Expiration:** 90 gün (takvime yenileme hatırlatıcısı koy; süresi dolunca yayın `HTTP 401` ile durur).
   - **Resource owner:** repo sahibi hesap.
   - **Repository access:** **Only select repositories** → yalnız `Rent-A-Car-Service`.
   - **Permissions → Repository permissions → Contents: Read-only.** (`Metadata: Read-only` kendiliğinden
     gelir.) **Başka hiçbir izin verme** — yazma yok, Actions yok, hesap izni yok.
3. **Generate token** → değer bir kez gösterilir. Doğrudan 2. adımdaki dosyaya yapıştır; sohbet, not,
   e-posta, commit'e **yazma**.

## 2. Token'ı `/etc/racar/racar.env`'e koy

```bash
# Önce yedek (dosya Pii__HmacKey'i de taşır — kaybı veri kaybıdır). cp -a izinleri korur.
sudo cp -a /etc/racar/racar.env /etc/racar/racar.env.yedek-$(date +%F)

# Düzenle: sudoedit dosyanın sahibini/iznini değiştirmez, token kabuk geçmişine düşmez.
sudoedit /etc/racar/racar.env
```

Dosyaya şu satırı ekle. `RACAR_GH_TOKEN=DEGISTIR_...` satırı zaten varsa onu değiştir; aynı anahtar iki kez
olmasın:

```
RACAR_GH_TOKEN=<az önce kopyaladığın token>
```

İzinleri ve varlığı **değeri yazdırmadan** doğrula:

```bash
sudo chown root:racar /etc/racar/racar.env && sudo chmod 640 /etc/racar/racar.env
ls -l /etc/racar/racar.env                         # -rw-r----- 1 root racar ...
sudo grep -c '^RACAR_GH_TOKEN=' /etc/racar/racar.env  # 1
sudo grep -c 'DEGISTIR' /etc/racar/racar.env          # 0 (yoksa yayinla.sh reddeder)
```

Token'ın repoya erişebildiğini dene (token komut satırına/`ps`'e düşmez, çıktıya yazılmaz):

```bash
sudo bash -c 'T=$(grep "^RACAR_GH_TOKEN=" /etc/racar/racar.env | cut -d= -f2-);
  curl -s -o /dev/null -w "%{http_code}\n" -K - \
    "https://api.github.com/repos/BurakkYuce/Rent-A-Car-Service/releases?per_page=1" \
    <<<"header = \"Authorization: Bearer $T\""'
# 200 = tamam · 401 = token yanlış/süresi dolmuş · 404 = token bu repoya yetkili değil
```

> Not: servisler de aynı dosyayı `EnvironmentFile` olarak okur, yani token web/publicsite süreçlerinin
> ortamında da bulunur (uygulama onu KULLANMAZ). Token yalnız bu repoyu okuyabildiği için kabul edilen
> risktir. Sızdığından şüphelenirsen GitHub'dan hemen **Revoke** et, yenisini oluşturup 2. adımı tekrarla.

## 3. Sunucu önkoşulları (tek seferlik)

```bash
git -C <repo> fetch origin && git -C <repo> checkout --detach origin/main
sudo <repo>/deploy/kurulum.sh     # idempotent: .NET SDK 10 + curl + jq; racar.env'i EZMEZ
dotnet --list-sdks | grep '^10\.' # en az bir satır
command -v node || echo "Node yok (doğru)"
```

`kurulum.sh` sonunda "RACAR_GH_TOKEN yok" uyarısı çıkıyorsa 2. adım eksik.

## 4. İlk yayın

```bash
# Yedek — migration'dan ÖNCE, her seferinde
sudo -u postgres pg_dump racar | gzip > /var/backups/racar-$(date +%F-%H%M).sql.gz

git -C <repo> rev-parse HEAD      # bu SHA için GitHub'da spa-<sha> release'i OLMALI (0. adım)
sudo <repo>/deploy/yayinla.sh
```

Beklenen satırlar (sırayla): `SPA token'ı tanımlı (değer yazdırılmaz)` → `Commit: <sha>` →
`sha256 doğrulandı` → `SPA açıldı: N hash'li dosya` → (ilk seferde) `Önceki sürümde SPA chunks.txt yok`
(normal) → publish satırları → `Yeni arayüz /app/ hazır` → `YAYIN TAMAM`.

```bash
cat /opt/racar/current/app/SURUM          # = git rev-parse HEAD
ls /opt/racar/current/app/browser         # index.html, main-XXXXXXXX.js, styles-XXXXXXXX.css …
```

## 5. Doğrula

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://<host>/app/              # 200
curl -s -o /dev/null -w '%{http_code}\n' https://<host>/app/kiralar/5     # 200 (istemci rotası → kabuk)
curl -si https://<host>/api/ui/v1/oturum/ben | head -n 12
#   HTTP/2 401 · content-type: application/problem+json · gövdede "kod":"oturum_yok"
#   Location başlığı YOK (/login'e 302 OLMAMALI)
<repo>/deploy/dogrula.sh <host>                                           # "Hepsi temiz."
```

Tarayıcıda `https://<host>/app/` → "Yeni arayüz yapım aşamasında"; DevTools konsolunda CSP hatası yok.

## 6. Geri alma provası

İlk F2.2 yayınından önceki release'te `app/` yoktur; ona dönünce `/app/` 404 verir (beklenen). Anlamlı prova
için **ikinci bir yayın** yap (aynı SHA ile tekrar `sudo <repo>/deploy/yayinla.sh` yeterli), sonra:

```bash
ls -1t /opt/racar/releases                          # en yeni üstte
SIMDIKI=$(basename "$(readlink -f /opt/racar/current)")
ONCEKI=$(ls -1t /opt/racar/releases | grep -vx "$SIMDIKI" | head -n 1)
echo "şimdiki=$SIMDIKI önceki=$ONCEKI"

# Geri
sudo ln -sfn "/opt/racar/releases/$ONCEKI" /opt/racar/current && sudo systemctl restart racar-web racar-publicsite
YOKLA="curl -s -o /dev/null -w %{http_code}\n --retry 20 --retry-delay 2 --retry-connrefused"
$YOKLA http://127.0.0.1:5220/health/ready    # 200 (açılış birkaç saniye sürer; curl bekler)
$YOKLA http://127.0.0.1:5220/app/            # 200 (önceki release'in SPA'sı)

# İleri (provadan dön)
sudo ln -sfn "/opt/racar/releases/$SIMDIKI" /opt/racar/current && sudo systemctl restart racar-web racar-publicsite
$YOKLA http://127.0.0.1:5220/health/ready    # 200
```

Geri alma ikilileri ve SPA'yı döndürür, **veritabanı şemasını döndürmez** (deploy-checklist §10.4).

## 7. "Yanlış SHA / checksum reddediliyor" testi

**a) Artifact'ı olmayan commit (gerçek sunucuda, güvenli):** geçici bir worktree'de push edilmemiş boş bir
commit oluştur; onun `spa-<sha>` release'i yoktur, `yayinla.sh` reddetmelidir. Çalışan sürüme dokunulmaz.
(Repo checkout'u root'a aitse `git` komutlarının başına `sudo` ekle.)

```bash
ONCE=$(readlink -f /opt/racar/current); SAYI=$(ls /opt/racar/releases | wc -l)
git -C <repo> worktree add --detach /tmp/racar-ret-testi HEAD
git -C /tmp/racar-ret-testi -c user.name=ret -c user.email=ret@yerel commit --allow-empty -q -m "ret testi"
sudo /tmp/racar-ret-testi/deploy/yayinla.sh; echo "çıkış=$?"
#   HATA: SPA artifact'ı YOK: 'spa-<sha>' release'i bulunamadı ... Yayın reddedildi.   çıkış=1
[ "$(readlink -f /opt/racar/current)" = "$ONCE" ] && echo "current DEĞİŞMEDİ"
[ "$(ls /opt/racar/releases | wc -l)" = "$SAYI" ] && echo "yeni release YOK"
git -C <repo> worktree remove --force /tmp/racar-ret-testi
```

**b) Checksum uyuşmazlığı, yanlış SHA'lı arşiv, eksik `.sha256`, geçersiz token:** GitHub'daki gerçek
asset'leri bozmak yerine sahte bir GitHub API'sine karşı sınanır. Sunucuda root gerekmez, `/opt/racar`'a ve
ağa dokunmaz (python3 Ubuntu'da hazır gelir):

```bash
bash <repo>/deploy/spa-testi.sh     # sonda: "15 geçti · 0 kaldı"
bash <repo>/deploy/guard-testi.sh   # token yok / biçimsiz → red; sonda: "8 geçti · 0 kaldı"
```

Aynı iki test CI'da (`deploy-betikleri` işi) her PR'da koşar.

## 8. Sonraki yayınlar

```bash
sudo -u postgres pg_dump racar | gzip > /var/backups/racar-$(date +%F-%H%M).sql.gz
git -C <repo> fetch origin && git -C <repo> checkout --detach origin/main   # CI'ı yeşil main commit'i
sudo <repo>/deploy/yayinla.sh
<repo>/deploy/dogrula.sh <host>
```

Yayın sırasında açık sekmeler kırılmaz: önceki release'in kendi `chunks.txt`'indeki dosyalar yeni release'e
kopyalanır (bir yayın geriye kadar; birikmez). Token süresi dolunca `yayinla.sh` `HTTP 401` der → 1. ve 2.
adımı tekrarla.
