---
name: roadmap-devam
description: Angular geçiş roadmap'ine kaldığı yerden devam et. docs/roadmap/DEVIR.md'yi okur, gerçek durumu (açık PR'lar, main) kontrol eder, sıradaki işi seçer ve PR'ı baştan sona yürütür (kod, test, CI, para ise bağımsız adversarial, merge), sonra devir kontrol listesini günceller. Kullanıcı "devam", "roadmap'e devam", "kaldığın yerden", "sıradaki PR" dediğinde ya da /roadmap-devam yazdığında kullan.
---

# Roadmap'e kaldığın yerden devam

Argümanlar (isteğe bağlı): `paralel` (worktree + arka plan ajanlarıyla birden çok PR), bir sayı (en fazla kaç PR
yürütülecek; varsayılan 1), ya da doğrudan bir iş adı (ör. `#263`, `F5.1`, `çekirdek eki`).

## Adımlar

1. **Oku:** `docs/roadmap/DEVIR.md` baştan sona (özellikle §1 Durum, §2 İş akışı, §4 Adversarial, §5 Kalıcı dersler).
   Sonra seçtiğin işin `docs/roadmap/F*.md` dosyası ve frontend işiyse `src/RentACar.Frontend/AGENTS.md`.
2. **Gerçekle karşılaştır:**
   ```bash
   gh auth switch --user BurakkYuce
   git fetch -q origin
   gh pr list --state open --json number,title,headRefName,headRefOid
   git log --oneline -15 origin/main
   ```
   §1 ile gerçek farklıysa (PR merge olmuş, yenisi açılmış) önce §1'i gerçeğe göre düzelt.
3. **Sıradaki işi seç**, şu öncelikle:
   1. §1 "Açık PR'lar": bekleyen düzeltme ya da doğrulama varsa önce o. Sıra kısıtlarına uy (ör. #264, #263'ten sonra).
   2. §1 "Sırada" listesinin ilk işaretlenmemiş maddesi.
   3. Madde §1 "Kullanıcıda bekleyenler" içindeki bir karara bağlıysa (ör. F5'e başlamak, yakıt ölçeği) o işe
      BAŞLAMA. Kullanıcıya kısa ve seçenekli bir soru sor, beklerken karara bağlı olmayan bir sonraki maddeye geç.
4. **Yürüt:** `DEVIR.md` §2'deki 12 adımı uygula. Para dokunuyorsa §4 bağımsız adversarial inceleme zorunlu:
   ayrı bir ajan, ayrı bir worktree. Critical/High/Medium sıfırlanana kadar düzeltip yeniden doğrulat. §5'teki
   dersleri yeni kodda baştan uygula.
   - Uzun komutların (dotnet test, npm ci/build/e2e) çıktısını dosyaya yaz ve `tail` ile oku.
   - CI'ı yalnız `scripts/pr-izle.sh <pr> 6 <sha-önek>` ile izle; merge ayrı komutta:
     `gh pr merge <no> --merge --match-head-commit <sha>`.
5. **Kapat:** merge sonrası:
   - worktree ve dalı temizle;
   - `DEVIR.md` §1'i güncelle (bitti/açık/sırada);
   - faz bittiyse `F*.md` + `README.md` durumunu ve `DEGISIKLIKLER.md` kaydını yaz.
   Belge güncellemesi küçük bir docs PR'ı olabilir ya da işin kendi PR'ına eklenebilir.
6. **Dur ve raporla:** istenen sayıda PR bitince, bir kullanıcı kararına takılınca ya da sunucu erişimi gerekince
   (F2.2, pilot açma) dur. Kullanıcıya Türkçe kısa rapor ver: ne bitti, ne açık, ne sırada, senden ne bekleniyor.

## Kurallar

- **Döngü kurma** (`/loop`, zamanlanmış uyanış) — kullanıcı açıkça istemedikçe.
- **Faz sırası kilitli:** sıra değişikliği yalnız kullanıcı kararı + `DEGISIKLIKLER.md` kaydıyla.
- **Force-push yok.** İstisna: sırrın geçmişe girmesi. O durumda tek commit'lik temiz dal açılır.
- **Merge edilmemiş dal silinmez.**
- **Sunucuda Node/npm yok; `yayinla.sh`'yi çalıştırma.** Sunucu adımları kullanıcıya aittir.
- **Revlo reposu salt-okur.**
- **Kullanıcı C# okumaz:** doğruluk testlerden, bağımsız oracle'dan ve adversarial incelemeden gelir. Rapora
  kanıt koy: test sayısı, "Failed: 0", CI sonucu, adversarial sonuç satırı.
