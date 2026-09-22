// @ts-check
import eslint from '@eslint/js';
import angular from 'angular-eslint';
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';

/**
 * Ham büyük/küçük harf dönüşümü yasak: Türkçe "I/ı" ve "İ/i" bozulur, arama kaçar.
 * MemberExpression seçilir ki `.map(s => s.toLowerCase())` kadar metot referansı da yakalansın.
 */
const TR_METIN = [
  {
    selector: 'MemberExpression[property.name=/^to(Lower|Upper)Case$/]',
    message:
      "Ham toLowerCase/toUpperCase yasak (Türkçe İ/ı bozulur). '@core/metin/tr-normalize' kullanın.",
  },
  {
    selector:
      'CallExpression[callee.property.name=/^toLocale(Lower|Upper)Case$/][arguments.length=0]',
    message:
      "Yerel ayarsız toLocaleLowerCase/UpperCase yasak (tarayıcı diline bağlı). '@core/metin/tr-normalize' kullanın.",
  },
];

/**
 * Mutlak URL yasak: XSRF header'ı yalnız göreli URL'lere eklenir; mutlak adres CSRF korumasını
 * sessizce düşürür ve CSP connect-src 'self' ile de çalışmaz.
 */
const MUTLAK_URL = [
  {
    selector: 'Literal[value=/^https?:/i]',
    message:
      'Mutlak http(s) URL yasak. Göreli URL kullanın (XSRF header yalnız göreli URL’de eklenir).',
  },
  {
    selector: 'TemplateElement[value.raw=/^https?:/i]',
    message:
      'Mutlak http(s) URL yasak. Göreli URL kullanın (XSRF header yalnız göreli URL’de eklenir).',
  },
];

/**
 * F4.3b istisnası: harici paylaşım bağlantıları (`src/app/shared/dis-baglantilar.ts`) YALNIZ iki mutlak köke
 * izinlidir — WhatsApp (`https://wa.me/`) ve Gmail taslak kökü. Bunlar API çağrısı değil yeni sekmede açılan
 * gezinme bağlantısıdır; dosya HTTP katmanını içe aktaramaz (aşağıdaki blok). Başka her mutlak adres yine hata.
 * (esquery regex'inde `/` yazılamaz → `\x2F`.)
 */
const IZINLI_DIS_KOK = String.raw`^https:\x2F\x2F(wa\.me\x2F|mail\.google\.com\x2Fmail\x2F\?view=cm&fs=1)$`;
const DIS_BAGLANTI_URL = [
  {
    selector: `Literal[value=/^https?:/i]:not([value=/${IZINLI_DIS_KOK}/])`,
    message:
      'Bu dosyada yalnız WhatsApp (https://wa.me/) ve Gmail taslak kökü izinli (AGENTS.md "Harici paylaşım").',
  },
  MUTLAK_URL[1],
];

/**
 * Veri katmanı (F3.4): özellik store'ları veriyi yalnız `TemelStore` ile yükler. Ham `subscribe`
 * iptal edilmeyen/yarışan istek, sızan abonelik ve hatanın boş listeye dönüşmesi demektir.
 */
const STORE_SUBSCRIBE = [
  {
    selector: "CallExpression[callee.property.name='subscribe']",
    message:
      "Özellik store'unda ham subscribe yasak. Veriyi '@core/veri/temel-store' TemelStore ile yükleyin (dört durum + switchMap iptali).",
  },
];

export default defineConfig(
  {
    ignores: [
      'dist/',
      'out-tsc/',
      '.angular/',
      'coverage/',
      'node_modules/',
      'test-results/',
      'playwright-report/',
      'blob-report/',
      // openapi-typescript çıktısı (npm run tipler): elle düzenlenmez, CI yeniden üretip farkı yakalar.
      'src/app/core/api/uretilen/',
    ],
  },
  {
    files: ['**/*.mjs'],
    extends: [eslint.configs.recommended],
    languageOptions: {
      globals: { console: 'readonly', process: 'readonly', URL: 'readonly' },
    },
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'rc', style: 'kebab-case' },
      ],
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'rc', style: 'camelCase' },
      ],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@typescript-eslint/no-explicit-any': 'error',
      'no-restricted-syntax': ['error', ...TR_METIN],
    },
  },
  {
    // Uygulama kodu: mutlak URL de yasak (e2e ve araç yapılandırması localhost'a gidebilir).
    files: ['src/**/*.ts'],
    rules: {
      'no-restricted-syntax': ['error', ...TR_METIN, ...MUTLAK_URL],
    },
  },
  {
    // Katman sınırı: core/ ve shared/ özelliklerden bağımsızdır; features/ onları kullanır, tersi yok.
    files: ['src/app/core/**/*.ts', 'src/app/shared/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@features', '@features/*', '**/features', '**/features/*'],
              message: 'core/ ve shared/ features/ içe aktaramaz (katman sınırı, bkz. AGENTS.md).',
            },
          ],
        },
      ],
    },
  },
  {
    // F4.3b harici paylaşım bağlantıları: dar mutlak-URL istisnası + HTTP katmanı içe aktarılamaz.
    files: ['src/app/shared/dis-baglantilar.ts', 'src/app/shared/dis-baglantilar.spec.ts'],
    rules: {
      'no-restricted-syntax': ['error', ...TR_METIN, ...DIS_BAGLANTI_URL],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/common/http',
              message: 'Harici paylaşım bağlantıları HTTP ile çağrılmaz (yalnız gezinme).',
            },
          ],
          patterns: [
            {
              group: ['@core/api', '@core/api/*', '**/core/api/*'],
              message: 'Harici paylaşım bağlantıları ApiIstemcisi ile çağrılmaz (yalnız gezinme).',
            },
            {
              group: ['@features', '@features/*', '**/features', '**/features/*'],
              message: 'core/ ve shared/ features/ içe aktaramaz (katman sınırı, bkz. AGENTS.md).',
            },
          ],
        },
      ],
    },
  },
  {
    // Özellik store'ları: ham subscribe yasak (TemelStore zorunlu).
    files: ['src/app/features/**/store/**/*.ts', 'src/app/features/**/*.store.ts'],
    ignores: ['**/*.spec.ts'],
    rules: {
      'no-restricted-syntax': ['error', ...TR_METIN, ...MUTLAK_URL, ...STORE_SUBSCRIBE],
    },
  },
  {
    // Özellikler HTTP'ye yalnız ApiIstemcisi ile çıkar (göreli URL, withCredentials, tipli ApiHatasi).
    files: ['src/app/features/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/common/http',
              importNames: ['HttpClient'],
              message:
                "HttpClient'ı doğrudan kullanmayın; '@core/api/api-istemcisi' ApiIstemcisi kullanın.",
            },
          ],
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      // lowercase/uppercase/titlecase pipe'ları içeride ham toLowerCase kullanır → Türkçe bozulur.
      'no-restricted-syntax': [
        'error',
        {
          selector: 'BindingPipe[name=/^(lowercase|uppercase|titlecase)$/]',
          message:
            "lowercase/uppercase/titlecase pipe yasak (Türkçe İ/ı bozulur). '@core/metin/tr-normalize' kullanın.",
        },
      ],
    },
  },
);
