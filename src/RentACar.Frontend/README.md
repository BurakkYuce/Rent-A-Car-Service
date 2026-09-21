# RentACar.Frontend

Yeni arayüz (Angular 21.2, zoneless, `/app/` altında). Kurallar, kapılar ve kararlar için
[AGENTS.md](AGENTS.md).

```bash
npm ci
npm run watch    # Web uygulamasıyla birlikte: http://localhost:5220/app/ (bkz. AGENTS.md)
npm start        # yalnız arayüz: http://localhost:4200/app/ (/api/ui yok)
npm run tipler   # docs/api/ui-v1.json → src/app/core/api/uretilen/ui-v1.ts
npm run build    # dist/rentacar-frontend/browser
```
