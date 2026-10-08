# InkGuide — Derin Durum Raporu

> Tarih: 8 Ekim 2026 · Hazırlayan: OpenCode (Muse Spark) · Kaynak: bu oturumda yapılan gerçek build/test/launch kanıtları + `docs/` kayıtları
> Bu dosya bilgilendirme amaçlıdır; `docs/MASTER_PROMPT.md` (spesifikasyon) ve `docs/BUILD_STATE.md` (canlı durum) geçerli kaynaklardır.

---

## 1. Özet

**InkGuide** (`inkguide.desktop`, v0.1.0), Google Drive'ı "GitHub tarzı ama masaüstü" bir çalışma alanına çeviren Windows (x64, Win10+) masaüstü uygulamasıdır. WPF kabuk + WebView2 ön yüz. **Dosyalar Drive'da yaşar; yerel disk (SQLite + önbellek) silinebilir kopyadır.** Her dosya otomatik sınıflanır (tür, rol, düz-dil özeti), web projeleri etiketlenir, her şey aranabilir ve düzenlenebilir.

**Bugünkü durum:** derleniyor (0 uyarı), tüm testler yeşil (dotnet 293 + UI 67), `verify.cmd` tam geçti, `publish.cmd` dağıtım üretti, yayınlanmış exe duman testinden geçti. 7 commit, push yok (yerel repo).

| Ölçü | Değer |
|---|---|
| C# dosyası / satırı | 188 dosya / ~20.100 satır |
| TS/TSX dosyası | 41 dosya (`ui/src`) |
| Test | 293 dotnet (274 unit + 12 integration + 7 arch) + 67 vitest |
| Dağıtım | `dist/InkGuide-0.1.0-win-x64/` + 78 MB zip + SHA256SUMS |
| Toolchain | .NET SDK 9.0.316, Node v24.18.0, Vite 8.3.3, React 19.1.1, WebView2 Runtime 154 |
| Git | 7 commit, `master`, temiz ağaç, remote'a push yok |

---

## 2. Mimari (katmanlı, tek yönlü bağımlılık)

```
InkGuide.sln
src/
  InkGuide.Domain/                    # entity, value object, Result/Error — BAĞIMLILIK YOK
  InkGuide.Application/               # portlar (IStorageProvider, IOutbox, ISecretStore…) + use-case — yalnız Domain
  InkGuide.Analysis/                  # kind/rol/insight/detector/tag — Domain + Application
  InkGuide.Infrastructure.Persistence/ # EF Core SQLite (WAL), FTS5, outbox, sync
  InkGuide.Infrastructure.Storage/     # GoogleDrive/ + LocalFolder/ sağlayıcıları
  InkGuide.Infrastructure.Platform/    # DPAPI secrets, LRU önbellek, harici-düzenleme, Anthropic istemcisi
  InkGuide.Bridge/                     # sürümlü host↔UI kontratı (DTO), dispatcher, handler — WPF/WebView2 YOK
  InkGuide.App/                        # WPF kabuk (composition root, iş mantığı YOK): WebView2, tekil örnek, CLI
  InkGuide.Tools/                      # konsol: contract-gen, live-check, seed-fixtures
tests/
  InkGuide.UnitTests/                  # 274 test (domain/analysis/app)
  InkGuide.IntegrationTests/           # 12 test (LocalFolder kontratı, bridge dispatcher, outbox, redaksiyon)
  InkGuide.ArchitectureTests/         # 7 test (NetArchTest katman kuralları, Bölüm 6.2)
ui/                                    # Vite + React + TS: core (sözleşmesiz mantık) + views (telsiz iskelet) + theme + i18n
config/                                # VERİ olarak kurallar: project-markers.json, file-roles.json, kind-map.json, ignore-patterns.json
build/ | installer/ | docs/ | assets/
```

Katman kuralları **testle zorlanır** (`LayerRulesTests`): Domain hiçbir şeye; Application yalnız Domain'e; Analysis Domain+Application'a; Infra yalnız Domain+Application'a; Bridge Application+Domain'e bakar. Google SDK türleri `Infrastructure.Storage/GoogleDrive` dışına sızamaz. `Directory.Packages.props` merkezi paket yönetimi; lisanslar yalnız MIT/Apache-2.0/BSD/ISC/MS-PL (`THIRD_PARTY_NOTICES.md`).

---

## 3. Veri Modeli & Senkron

- **Drive = gerçek kaynak.** SQLite + önbellek silinip Drive'dan yeniden kurulabilir. Her yerel mutasyon önce dayanıklı **Outbox**'a yazılır, sonra UI'a onay verilir; worker tekrar oynatır (opId ile idempotent).
- Drive'da dayanıklılık: insan özeti `description` alanında; makine bilgileri `appProperties` (`inkguide.k/s/dh/ch/op`); etiket/proje/repo `.inkguide/` klasöründe (`repo.json`, `tags.json`, `projects.json`); snapshot'lar `.inkguide/snapshots/`.
- Kullanıcı-edit tespiti: uzak `description` hash'i farklıysa `source=user` olur, bir daha otomatik ezilmez.
- Çakışma: `baseVersion` ile güvenli kayıt; sürüm kaymışsa üzerine yazılmaz — çakışma kopyası + `ConflictRecord` + olay. Kalıcı silme onay token'ı ister; silme = çöpe taşıma.
- Analiz hattı (dosya başına): uzantı+MIME+**magic-byte** → kind → içerik yoklama (≤256 KB, Drive'da range-read) → `file-roles.json` ile rol → şablonlu 1–3 cümle özet (yapay zekâsız da anlamlı) → güven skoru → hassasiyet (`Sensitive`: `.env/*.pem/*.key`, PEM başlıkları, API-anahtar kalıpları → indeks/AI dışı). Klasörlerde `project-markers.json` ile proje tespiti (derinlik ≤4, mono-root: npm/pnpm/nx/turbo/çok-projeli `.sln`); **web siteleri birincil vaka** (`type:web-site` + `tech:nextjs/react/vite/wordpress/php…`).

---

## 4. Bridge (host ↔ UI tek kapısı)

- Zarf v1 JSON (`{v,id,type:req|res|evt|cancel,method,params,result,error}`), 1 MB üst sınır, origin kontrolü, `app.hello` el sıkışması (aynı major, host minor ≥ UI minor), `cursor/limit` sayfalama, `idempotencyKey`, yıkıcı işlerde `confirmationToken`, hata olarak yalnız kod (Appendix C) taşınır, dosya baytı asla geçmez (önbellek URL'si verilir).
- Metot ad alanları: app, window, settings, accounts, workspace, repos, projects, nodes, transfers, import, insight, tags, search, sync, snapshots, activity, organize, duplicates, external, ai, storage, rules, diagnostics. Olaylar 100 ms pencerede birleştirilir.
- `ui/src/core/contract.ts` + `docs/BRIDGE_PROTOCOL.md`, C# kontratlardan **üretilir** (`Tools contract-gen`, `--check` kapısı).
- WebView2 sertleştirme: 3 sanal host (ui/cache/usercontent), gezinme izin-listesi, `http/https/mailto` dışı şema engeli, tüm izinler reddedilir, indirmeler uygulamaya yönlenir, host-object kapalı, DevTools varsayılan kapalı (Release).

---

## 5. UI (tasarım yok — bilerek)

- `core/`: üretilmiş kontrat, promise bridge istemcisi (AbortSignal iptal, el sıkışma, yeniden bağlanma), Zustand store'ları, i18n yükleyici, `Intl` biçimleme, kısayol kaydı — tamamı mock bridge ile testli.
- `views/`: 18 işlevsel telsiz ekran (onboarding, repo liste/oluştur, repo özet, tarayıcı, inspector, editör-CodeMirror 6, önizleyici-marked+DOMPurify, arama, etiketler, inbox/organizer, kopyalar, snapshot, aktivite/geri-al, sync/outbox/çakışma, transfer, çöp, ayarlar, hakkında/tanı). Her görünümde loading/empty/error/offline/reauth/not-configured halleri; semantik HTML + `data-testid`/`data-action`.
- Yasaklar (lint ile zorlanır): renk/font/boşluk estetiği, gölge, animasyon, ikon/emoji, UI kiti, CSS framework, CDN, inline `style`. Görsellik yalnız `tokens.css` değişkenlerinde (gri yer tutucu, "PLACEHOLDER — tasarımcı değiştirecek" başlıklı); `structure.css` yalnız yerleşim.
- Değiştirme mekanizmaları: `%LOCALAPPDATA%\InkGuide\ui-override\` (manifest uyumluysa derlemesiz takas), `--ui-dev-url` (Vite dev), `?bridge=fixture` (taramasız tasarım köprüsü, prod paketinden ayıklanır — taramada sızıntı yok).
- i18n: `tr-TR` (varsayılan) + `en-US`, eksik/fazla anahtar testleri.

---

## 6. Özellik Durumu (F-01…F-26)

`done` = kod + referans UI + test/kanıt var. Ayrıntı `docs/BUILD_STATE.md`.

| ID | Özellik | Durum |
|---|---|---|
| F-01 | Hesaplar & OAuth (sistem tarayıcı, loopback+PKCE, DPAPI, re-auth) | done (canlı Google testi yok) |
| F-02 | Depo soyutlama + Google Drive sağlayıcı | done kod / **kontrat testi eksik** |
| F-03 | LocalFolder sağlayıcı (yalnız dev/test) | done (12 test + launch) |
| F-04 | Gözatma (ağaç, liste, kırıntı, sırala/süz, sayfala, favori) | done |
| F-05 | Dosya işlemleri (oluştur/yeniden-adlandır/taşı/kopyala/çöp/kurtar/korumalı silme/toplu) | done |
| F-06 | Aktarımlar (devamlı yükleme/indirme, kuyruk, ilerleme, iptal/tekrar) | done (live-check 7/7) |
| F-07 | Uygulama-içi metin/kod düzenleme (çakışmasız kayıt) | done |
| F-08 | Önizleme (metin/md/resim/PDF/ses-video/CSV/JSON/arşiv/korumalı HTML) | done |
| F-09 | Harici düzenleme oturumları | done |
| F-10 | Otomatik insight | done (274 test) |
| F-11 | Proje tespiti + otomatik etiket | done |
| F-12 | Etiketler (CRUD, kalıtım, istenmeyen oto-etiket bastırma) | done |
| F-13 | Repolar (single/mono) + içe aktarma (kuru-çalışma planlı) | done |
| F-14 | Snapshot'lar (pin/kopya, diff, yıkımsız geri yükleme) | done |
| F-15 | Aktivite günlüğü + geri alma | done |
| F-16 | Inbox + Organizer önerileri | done |
| F-17 | Kopya bulucu (asla otomatik değil) | done |
| F-18 | Arama (FTS + filtre, çevrimdışı) | done |
| F-19 | Senkron motoru (ilk indeks, değişiklik yoklaması, outbox, çakışma) | done |
| F-20 | Opsiyonel AI tanımlayıcı (kapalı-varsayılan, onay+bütçe+koruma) | done kod / canlı anahtar yok |
| F-21 | Ayarlar, yapılandırma, sırlar | done |
| F-22 | Sertleştirilmiş WebView2 + bridge | done (el sıkışma logda) |
| F-23 | UI katmanı + değiştirme | done |
| F-24 | i18n tr-TR/en-US | done |
| F-25 | Tanı (log, paket dışa aktarım, live-check) | done |
| F-26 | Paketleme, kurulum betiği, yayın dokümanları | done (kurulum derlenmedi) |

---

## 7. Doğrulama Kanıtları (bu oturumda gerçekten koştu)

| Kapı | Sonuç |
|---|---|
| `dotnet build InkGuide.sln` (Debug + Release) | 0 uyarı / 0 hata |
| `dotnet test` | 274 unit + 12 integration + 7 architecture = **293/293** |
| UI `tsc --noEmit`, `eslint --max-warnings 0` | temiz |
| UI `vitest` | 67/67 (%97.8 deyim, %97.7 satır) |
| `vite build` | ~666 ms, 62 modül, `dist/` üretildi |
| `contract:check` | fresh (contract 1.0) |
| Prod paket sızıntı taraması | fixture/DEV-ONLY dizgisi yok |
| `dotnet list package --vulnerable` + `npm audit` | temiz (verify Release koşusunda) |
| `build\verify.cmd` | **VERIFY OK, exit 0** (8/8 aşama) |
| `Tools live-check --local-provider` | 7/7 adım PASS |
| `build\publish.cmd 0.1.0` | **PUBLISH OK**: klasör + 78 MB zip + SHA256SUMS, WebView2Loader mevcut |
| Launch (Debug + LocalFolder) | pencere responding, el sıkışma `contract 1.0`, WebView2 154, 45 sn+ sıfır `malformed` |
| Launch (yayınlanmış exe) | responding pencere, el sıkışma `contract 1.0` |

**DoğrulanMAyanlar:** canlı Google Drive akışı, gerçek Anthropic çağrısı, Drive fake-HTTP kontrat testleri, DB rebuild round-trip testi, installer derlemesi, kod imzalama, 100k sentetik performans koşusu.

---

## 8. Bu Oturumda Bulunan & Düzeltilen Gerçek Hatalar (kanıtlı)

1. **LocalFolder indeksi boş okunuyordu** — `index.json` diskte dolu (346 bayt) ama `ListChildren` 0 dönüyordu. Neden: `System.Text.Json` get-only `Dictionary.Items`'ı doldurmuyor. Çözüm: setter eklendi. (`DebugLocalFolderTests` ile kanıtlandı, sonra silindi.)
2. **SQLite DateTimeOffset çökmesi** — `Where(NextAttemptAtUtc <= …)` ve `ORDER BY DateTimeOffset` EF Core SQLite'ta `InvalidOperationException/NotSupportedException`. Çözüm: dar sunucu filtresi + bellek-içi sıralama (`OutboxStore`, `MiscStore`).
3. **Bridge eksik DTO'lar + analizör hataları** — `*ById` istek kayıtları eklendi; CA1716 gerekçeli suppress (D-004), CA1068 parametre sırası, CA1822 statik.
4. **contract-gen bozuk TS üretiyordu** — `PagedResult\`1`, `Result\`1`, `Array<T>` ve `BridgeEnvelope` çakışması. Çözüm: yalnız `Contracts` ad alanı, açık jenerikler atlanır, zarf çakışmaları atlanır.
5. **Vite 7.3.7 build kilitlenmesi** — `transforming...`'de sonsuz takılma (385 CPU-sn). Biseksiyon kanıtı: yalın proje 260 ms OK; react-only takılır; configsiz izole react projesi takılır; aynı proje Vite 8.3.3'te 597 ms. Neden: Vite 7.3'ün Rollup 4 hattı React 19.1 dist + Node 24'te kilitleniyor (Vite 8 Rolldown kullanır). Çözüm: `vite@8.3.3` + `plugin-react@5.2.0`. React plugini aklandı.
6. **Launch diyalogları** — `ui-missing` (dotnet run'da paket yoktu) + `TimeProvider` DI kaydı yok + çift yönlü kodlama uyumsuzluğu (UI stringify edip postMessage'lıyordu, host obje bekliyordu; ters yönde UI `JSON.parse` yapıyordu ama host zaten obje iletiyordu → 31 sn arayla 2 `malformed`). Çözüm: host string-sarmalını açar + obje kabul eder; UI obje-veya-string dinler; `TimeProvider.System` kaydı; dev'de `ui/dist` exe yanına kopyalanır; el sıkışma loglanır.
7. **Publish MSB3094** — `_CopyResolvedFilesToPublishPreserveNewest` 436→1 çöküyordu. Sırayla aklandı: dev-copy target (guard eklendi, yine çöktü), artık dosyalar (tam bin/obj temizliği, yine çöktü). Belirleyici test: aynı publish `C:\igpub`'a exit 0 (294 dosya). Neden: checkout yolundaki **apostrof** (`Demir's`). Çözüm: publish `%TEMP%` altında stage'ler, `dist/`'e robocopy ile toplanır.

---

## 9. Karar Kayıtları (özet — tam metin `docs/DECISIONS.md`)

| ID | Konu |
|---|---|
| D-001 | Hedef net9 (makinede LTS SDK yok; net10'a geçiş notu) |
| D-002 | APP_NAME=InkGuide, `inkguide.*` anahtarları, `.inkguide/` klasörü |
| D-003 | Tek-koşu yerine kontrol noktalı artımlı çalışma (`BUILD_STATE.md` bellek) |
| D-004 | CA1716 gerekçeli suppress (bridge `Namespace` terimi) |
| D-006…D-015 | WebView2 pini, TFM niteleyici, UseWPF sırası, reflection backend, konsol/ILogger ayrımı, contract-gen taban kataloğu, live-check dürüstlüğü, klasör-publish, unsafe blok, LoggerMessage |
| D-016 | LocalFolder setter + DateTimeOffset istemci-içi sıralama |
| D-017 | Vite 8 yükseltmesi (kanıtlı) |
| D-018 | Bridge çift-kodlama düzeltmesi |
| D-019 | Publish %TEMP% stage (apostrof MSB3094) |

Doğrulanmamış varsayımlar (resmi dokümana karşı kontrol edilecek): `about.get` alan izinleri, `appProperties` limitleri, Anthropic model id, WebView2 PDF/range davranışı, paket sürümleri, Google 7-gün token ifadesi.

---

## 10. Bilinen Eksikler & Riskler

- Google Drive fake-HTTP kontrat testleri yok (katman kuralı var, davranış testi yok).
- DB rebuild round-trip testi yok (`RebuildFromDriveService` var).
- Installer derlenmedi (makinede Inno Setup yok); imza yok.
- .NET 9 STS destek dışı — EF 9.0.x yama hattı kapalı görünüyor; net10 geçişi planlanmalı.
- Debug `dotnet list package` bir ara SQLitePCLRaw geçişli High göstermişti; Release verify denetimi temiz. Takip notu `docs/KNOWN_LIMITATIONS.md` L-04'te.
- 670 KB'lık UI paketi için chunk uyarısı (CodeMirror; referans UI için kabul).

---

## 11. Önemli Dosya Yolları

- Spesifikasyon: `InkGuide.md` → kopyası `docs/MASTER_PROMPT.md`
- Canlı durum: `docs/BUILD_STATE.md` · kararlar: `docs/DECISIONS.md` · mimari: `docs/ARCHITECTURE.md` · bridge: `docs/BRIDGE_PROTOCOL.md` (üretilir) · kurulum: `docs/GOOGLE_SETUP.md` · yayın: `docs/PUBLISHING.md` · taslak gizlilik: `docs/PRIVACY.md` · sınırlar: `docs/KNOWN_LIMITATIONS.md`
- Kurallar (veri): `config/project-markers.json`, `config/file-roles.json`, `config/kind-map.json`, `config/ignore-patterns.json`
- Kapılar: `build/verify.cmd`, `build/publish.cmd`, `build/live-check.cmd`, `build/sign.cmd` (+ `.ps1` aynaları)
- Dağıtım: `dist/InkGuide-0.1.0-win-x64/`, `dist/*.zip`, `dist/SHA256SUMS.txt`
- Testler: `tests/InkGuide.{UnitTests,IntegrationTests,ArchitectureTests}/`

## 12. Komutlar

```cmd
build\verify.cmd                                   &:: tam kapı
dotnet test InkGuide.sln --no-build --no-restore   &:: hızlı test
cd ui && npm run typecheck && npm run lint && npm test && npm run build
dotnet run --project src\InkGuide.Tools -- contract-gen --check
dotnet run --project src\InkGuide.Tools -- live-check --local-provider %TEMP%\ig-live
dotnet run --project src\InkGuide.App -- --local-provider %TEMP%\ig-live --data-dir %TEMP%\ig-data
build\publish.cmd 0.1.0
```

## 13. Sahip Yapacakları

1. `docs/GOOGLE_SETUP.md` (Cloud projesi → Desktop istemcisi → 7-gün uyarısı) sonra `build\live-check.cmd` (gerçek Drive duman testi).
2. Opsiyonel AI anahtarı + veri-paylaşım onayı (`docs/AI_SETUP.md`).
3. `assets/app.ico`, lisans seçimi, kod-imzalama sertifikası.
4. `docs/UI_REPLACEMENT_GUIDE.md` ile tasarımcıya devir.
5. Boşluklar isterse: Drive fake-HTTP testleri, rebuild testi, net10 geçişi.
