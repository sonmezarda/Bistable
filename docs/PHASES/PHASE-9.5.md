# Faz 9.5 — Workbench Architecture Spike: Theia + .NET Engine Host

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md)
**Karar sahibi:** Ürün sahibi, 2026-07-17
**Durum:** Aktif — stratejik yön sahibi tarafından kabul edildi; göç kapıları uygulanıyor
**Öncelik:** P0 karar kapısı

## Neden bu faz var?

Faz 9'un watcher, AST diff/cache, diagnostics, stale-schematic ve worker
hot-swap backend kapıları tamamlandı. Buna karşılık sahibi, Avalonia tabanlı
Source yüzeyini manuel kabulde reddetti:

- araç çubuğu dar alanda çakışıyor;
- seçilen dosyanın metni görünmüyor;
- Project paneli kapatılamıyor;
- belge/panel yaşam döngüsü VS Code benzeri serbest bir workbench oluşturmuyor.

Kod da bunun yalnız görsel cila olmadığını doğruluyor: mevcut dockable'lar
`CanClose/CanDrag/CanFloat/CanPin` bayraklarıyla kilitli ve Source UI tek büyük
programatik C# görünümünde kuruluyor. Yeni UI-ağır özellikleri aynı kabuğa
eklemek taşıma maliyetini büyüteceği için Faz 10'dan önce ölçümlü bir platform
kararı gerekir.

## Karar

- **Sahip kararı, 2026-07-17:** Bistable'ın ana ürün yönü Theia tabanlı markalı
  IDE olacaktır. Yeni UI-ağır özellikler Avalonia kabuğuna eklenmeyecek; önce
  Theia dilimi uygulanacaktır.
- Code-OSS fork'u yapılmayacak: upstream ürünleştirme/merge yükü alınmayacak.
- Ana POC, AMD Vitis Unified IDE ile aynı temel yaklaşım olan **Eclipse Theia
  tabanlı markalı masaüstü ürün** olacaktır.
- Çalışan HDL/simülasyon/sentez kodu TypeScript'e yazılmayacak. Yeni bir
  `.NET Bistable.Engine.Host` süreci, kararlı ve sürümlü JSON-RPC/stdio sınırı
  üzerinden Theia frontend'e hizmet verecek.
- VS Code extension uyumluluğu dil özellikleri ve genel komutlar için
  korunacak; şematik/waveform gibi ürün widget'ları Theia extension olacaktır.
- POC kararı çıkana kadar Avalonia uygulaması korunur; silme veya büyük taşıma
  yapılmaz.

Bu bir "hemen Avalonia'yı sil" kararı değildir. Electron paketleme, canlı
elaborasyon, RTL şematik, waveform/simülasyon ve RV32 performans kapıları
kapanana kadar Avalonia karşılaştırma ve geri dönüş yüzeyi olarak korunur.

## Kabul kapısı

1. Theia Electron uygulaması tek komutla build/start olur; Explorer, Monaco,
   Problems, Terminal ve Settings hazır workbench parçaları çalışır.
2. Dosya sekmeleri kapanabilir ve taşınabilir; Project/Problems/Terminal
   panelleri gizlenebilir, yeniden açılabilir ve alanlar arasında taşınabilir.
3. `Bistable.Engine.Host` JSON-line protokolünde en az `hello`, `loadProject`
   ve `shutdown` isteklerini sunar; protokol sürümü handshake ile doğrulanır.
4. Theia, `samples/riscv_single_cycle` projesini engine host üzerinden yükler;
   top modül, port/modül sayısı, Verilator sürümü ve diagnostics görüntülenir.
5. İlk Bistable widget'ı kapatılabilir/taşınabilir bir workbench view olarak
   açılır ve engine bağlantı durumunu gösterir.
6. Kaydet→yeniden elaborasyon POC'si ≤2 sn kapısını korur; hata Problems'e
   dosya/satır/kolon olarak düşer ve düzeltince temizlenir.
7. Soğuk başlangıç, RSS, `riscv_single_cycle` yükleme ve temel şematik pan/zoom
   ölçümleri kaydedilir. Sonuçta yazılı **go / no-go** kararı çıkar.

## Görevler

| ID | Görev | Durum |
|---|---|---|
| P9.5-1 | Theia 1.73.x workspace; sürümler sabitlenmiş lockfile ve tekrar üretilebilir komutlar | **Browser tamam; Electron yerel paketleri bekliyor** |
| P9.5-2 | `Bistable.Engine` servis sınırı: UI bağımsız elaboration/project DTO'ları | **Tamamlandı** |
| P9.5-3 | `Bistable.Engine.Host`: sürümlü JSON-line RPC, süreç yaşam döngüsü, diagnostics | **Tamamlandı** |
| P9.5-4 | Theia backend extension: engine host süreç sahipliği ve frontend proxy | **Tamamlandı** |
| P9.5-5 | Bistable workbench widget + Explorer/Monaco/Problems entegrasyonu | **Tamamlandı; sahibi yön kabulü verdi** |
| P9.5-6 | Live reload ve şematik veri/render köprüsü | **Dockable top RTL + ELK sembolleri tamam; canlı simülasyon döngüsü (sür/izle) tamam; hiyerarşi bir sonraki dilim** |
| P9.5-7 | Otomatik testler, performans ölçümü ve ADR go/no-go sonucu | Aktif |
| P9.5-8 | Canlı döngü ilk kapısı: Engine session servisi + EngineHost RPC v2 + şematik sür/izle | **Otomatik testler yeşil; sahibin görsel/etkileşim kabulü bekleniyor** |
| P9.5-9 | Vivado-tarzı şematik okunabilirlik sözleşmesi: semantik pinler, ölçülü kolonlar, elision/LOD | **Uygulandı; sahibi görsel kabulü bekleniyor** |
| P9.5-10 | Hiyerarşik aç/kapa: instance içine girme, modül document kimliği, breadcrumb ve cone navigation | **Uygulandı: instance document + breadcrumb + poke güvenliği + seçici inline expand/collapse; sahibin görsel kabulü açık** |
| P9.5-11 | Logisim/Digital-tarzı manuel sürme: Poke modu, 1-bit toggle, çok-bit non-modal popover | **Uygulandı; sahibin manuel kabulü bekleniyor** |

## İlk dilim sonucu — 2026-07-17

- `Bistable.Engine` elaboration ve Verilator diagnostic parser sahipliğini UI'dan
  aldı; Avalonia `DesignLoadService` bu servise delegasyon yapan uyumluluk
  adaptörü oldu.
- Engine host protocol v1 üzerinde `hello`, `loadProject`, `shutdown` sunuyor.
  `riscv_single_cycle` smoke testinde top/modül/port/Verilator özeti alındı;
  bozuk HDL testi Problems'e taşınabilir dosya/satır/sütun verisini doğruluyor.
- Theia browser workbench, Explorer/Monaco/Problems/Terminal/Settings ve ilk
  kapatılabilir Bistable widget'ıyla build/start oluyor.
- Bistable widget'ları React'i doğrudan paketlemez; Theia'nın paylaşılan React
  runtime'ını kullanır. `npm test`, ikinci bir React kopyasının yeniden yalnız
  başlığı görünen boş widget üretmesini engelleyen kimlik sözleşmesini doğrular.
- Ürün çalışma zamanı audit'i: 1 low, 23 moderate, 0 high, 0 critical. Geliştirme
  zincirindeki kalan high/critical kayıtlar ve Open VSX marketplace zinciri
  kalıcı ürün kararı öncesinde kapatılacak.
- Electron native build'i bu makinede `libxkbfile-dev` ve `libsecret-1-dev`
  kurulmadan kapanmıyor. Browser hedefi karar çalışmasını bloklamıyor; masaüstü
  kabul kapısı henüz kapanmış sayılmaz.

## Geçiş uygulaması — ilk canlı dilim, 2026-07-17

- Workspace kökündeki `.bistable.json` açılışta otomatik bulunup elaborasyon
  başlatılıyor; manuel düğme artık yalnız zorla yeniden yükleme içindir.
- `.sv/.svh/.v/.vh` ve proje dosyası kayıtları 400 ms debounce edilir. Aktif
  elaborasyon sırasında gelen kayıtlar üst üste bindirilmez; tamamlanınca tam
  bir kez en yeni durum çalıştırılır.
- Hatalı elaborasyon Verilator diagnostics'ini Theia Problems'e koyar; başarılı
  takip turu önceki Bistable marker'larını temizler. Her iki yaşam döngüsü
  otomatik sözleşme testiyle kilitlidir.
- `loadProject` cevabı top-modül için layout-agnostic şematik düğüm/kenar DTO'su
  taşır. Şematik yan panele gömülmez; kaynak dosyalar gibi merkez document
  dock'unda açılır, kapanır ve başka tab gruplarına taşınabilir.
- ELK layered/orthogonal layout Theia backend sürecinde çalışır; renderer ana
  thread'inde layout yoktur. SVG renderer port, mux, AND/OR/XOR, inverter,
  buffer, arithmetic/comparator, DFF/latch, memory, splitter/joiner ve module
  instance için RTL sembolleri ve gerçek pin konumları çizer.
- Top-modül document yolu tamamdır. Instance içine girme, breadcrumb ve her
  modül için ayrı document kimliği bir sonraki hiyerarşi dilimidir.

## Canlı döngü ilk kapısı — 2026-07-17

Vizyonun kalbi (sür → gör → izle) artık Theia şematiği üzerinde çalışır. C#
simülasyon matematiği TypeScript'e kopyalanmadı; native Verilator worker'ı
`Bistable.Engine` tarafındaki yeni `SimulationSessionService` sahiplenir.

- **`Bistable.Engine`:** `SimulationSessionService` bir yüklü proje için worker'ı
  `SimulationWorkerBuilder` ile derler, `Hello` (protokol v3) el sıkışması yapar,
  probe kataloğunu okur ve canlı döngüyü sunar: `SetInput` (genişlik/format
  doğrulaması *IPC öncesi*), `Eval`/`Tick`/`Reset`, tek turlu `ReadSignals`.
  Her başlatma **session generation** artırır; proje reload'ında yeni worker
  hazırlanıp eskisi atomik olarak bırakılır, geç gelen frame/okuma düşürülür.
  Transport için UI'dan bağımsız `EngineSimulationWorker` (App
  `SimulationWorkerClient`'ının atomik gönder/boşalt disiplininin aynısı, sim
  matematiği yok) eklendi. Değer doğrulaması `SimulationValueValidator`'da
  izole edildi.
- **`Bistable.EngineHost`:** protokol **v1 → v2**. `simulation.start`,
  `simulation.setInput`, `simulation.eval`, `simulation.tick`, `simulation.reset`,
  `simulation.readSignals`, `simulation.stop` metotları; doğrulama hataları
  `invalid_value` koduyla yapılandırılmış döner. Eski/uyumsuz sürüm handshake'te
  reddedilir. stdout yalnız protokol; loglar stderr.
- **Theia frontend:** `BistableProjectState` artık simülasyon dilimini de
  sahiplenir (`onDidChangeSimulation`, değer haritası, seçili sinyal, driven
  kümesi). Şematik document seçim (pin bazlı, görünen etikete güvenmez),
  inspector (path/direction/width/değer), bin/hex/dec giriş + Apply
  (SetInput→Eval→tek batch ReadSignals→canlı overlay) ve Eval/Tick/Reset
  kontrolleri taşır. Değer overlay'leri mevcut ELK geometrisi üzerine saf SVG;
  değer değişiminde layout yeniden çalışmaz. Görünür probe yolu kümesi yalnız
  layout değişiminde hesaplanır (frame başına tam graf taraması yok).
- **Testler:** Engine session (sür→frame, tek batch, doğrulama IPC'siz reddeder,
  dispose'da sızıntı yok, reload generation), EngineHost RPC v2 (hello v2 +
  capabilities, tam lifecycle, invalid_value), Theia `check-simulation-state.mjs`
  (selected/driven/live → CSS + stale generation). Avalonia simülasyon testleri
  değişmedi.

## Şematik görsel sözleşmesi — Vivado birincil referans, 2026-07-17

Ürün sahibi RTL şematik gösteriminde birincil görsel/davranış referansı olarak
AMD Vivado'yu seçti. Bistable marka ve canlı-debug yeteneklerini korur; fakat
sembol/pin yoğunluğu, hiyerarşik keşif ve long-text davranışında Vivado'nun
profesyonel okunabilirlik ilkeleri izlenir.

- Exact net adı bağlantı, probe ve seçim kimliğidir; görünür pin etiketi değildir.
  Transport düğümleri artık paralel `inputLabels`/`outputLabels` metadata'sı
  taşır. MUX `S/I0…/Y`, gate `A/B…/Y`, register `D/CLK/ARST/Q`; instance ise
  gerçek HDL port adlarını gösterir. `__schematic_*` adları tooltip/Inspector'da
  denetlenebilir kalır ancak sembol yüzeyini doldurmaz.
- Node-side `schematic-visual-contract.ts`, sabit monospace metrikleriyle label
  kolonunu ölçer ve ELK'ye içerik-duyarlı fakat üst sınırlandırılmış node boyutu
  verir. Sol/sağ pin kolonları arasında sembol tipine göre korumalı merkez
  boşluğu vardır; SVG clip-path son güvenlik katmanıdır.
- Instance header'ı iki satırdır: güçlü instance adı + ikincil module type.
  Uzun metin prefix ve suffix'i koruyan orta elision ile kısalır; tam değer
  tooltip'tedir.
- Overview zoom'da pin/module-type/live-value ayrıntıları gizlenir; topoloji ve
  ana semboller kalır. Bu LOD yalnız çizimi değiştirir, bağlantı/selection
  semantiğini veya layout geometrisini değiştirmez.
- Bir sonraki zorunlu dilim P9.5-10'dur: Vivado benzeri seçici hiyerarşi
  expand/collapse, instance document navigation ve breadcrumb. Küçük görsel
  cilalar bu navigasyon kapısının önüne geçmez.

## Manuel simülasyon etkileşimi — Logisim + Digital referansı, 2026-07-17

Manuel sürme davranışı için araştırılmış birincil referanslar Logisim Evolution
ve Digital'dır. Bağlayıcı ayrıntılar
[docs/SIMULATION_INTERACTION_UX.md](../SIMULATION_INTERACTION_UX.md) içindedir.

- Port/pin seçiminden sonra constant literal kutusunun tamamı da exact output
  netini seçer; Inspector bunu `constant · read only` gösterir.
- Gezinme güvenliği için Select mutasyon yapmaz. P9.5-10 sonrasındaki sürme
  dilimi ayrı Poke/Drive modu taşır: 1-bit input tek tıkla toggle olur; çok-bit
  input Digital benzeri anchored, non-modal Apply/OK/Escape popover'ı açar.
- Clock/reset/button rolleri isimden tahmin edilmez. Tick mevcut toolbar
  komutunda kalır; özel etkileşim ancak açık input-role metadata'sıyla eklenir.
- Her sürme aynı `SetInput → Eval → tek batch ReadSignals` yolunu kullanır;
  şematik layout'u yeniden çalıştırmaz.
- **Sahip öncelik kararı, 2026-07-18:** P9.5-11'in manuel sürme dilimi
  P9.5-10'dan önce uygulanmıştır. Toolbar'daki ayrı Poke modu yalnız worker
  hazırken açılır. Scalar input tek tıkla toggle edilir; multi-bit input
  tıklanan noktada BIN/HEX/UDEC/SDEC, bit düğmeleri, Apply/OK/Escape taşıyan
  non-modal popover açar. Select salt-okunur kalır. Bu dar öncelik değişikliği
  sonrasında P9.5-10 yine sıradaki bağlayıcı iştir.

## Hiyerarşik gezinme ilk dilimi — P9.5-10, 2026-07-18

Vivado-tarzı hiyerarşi keşfinin ilk bağlayıcı dilimi uygulandı: instance
document navigation + breadcrumb. Seçici inline expand/collapse sonraki dilime
bırakıldı.

- **Engine/EngineHost:** `EngineInstancePathResolver` bir hiyerarşik instance
  path'ini (`top.u_core.u_alu`) AST üzerinde segment segment çözer; kimlik
  module type değil instance path'tir ve aynı tipin iki instance'ı bağımsız
  çözülür. EngineHost protokol v2'ye additive `schematic.module` capability ve
  `loadModuleSchematic(projectPath, instancePath)` metodu eklendi
  (`docs/ENGINE_HOST_PROTOCOL.md`); çözümsüz path yapılandırılmış
  `invalid_path` döner. Host son elaborasyonu cache'ler: instance açmak
  Verilator'ı yeniden çalıştırmaz.
- **Document kimliği:** Şematik widget factory artık `instancePath` options'ı
  taşır. Root document eski `bistable.schematic.document` kimliğini korur;
  child'lar `bistable.schematic.document:<instance path>` olur. Theia
  WidgetManager options'a göre anahtarladığı için aynı path ikinci kez
  açıldığında mevcut sekme aktive edilir — duplicate tab oluşmaz. Her child
  ayrı kapatılabilir/taşınabilir main-dock document'ıdır.
- **Gezinme:** Instance gövdesine double-click o instance'ın document'ını açar
  (tek tık seçim olarak kalır). Her document toolbar'ında `top › u_core ›
  u_alu` breadcrumb'ı vardır; üst segmente tıklamak o document'ı açar/aktive
  eder (parent navigation).
- **Canlı değerler:** Child probe yolu document path öneki ile üretilir
  (`top.u_alu.result`). Her document görünür probe kümesini yalnız layout
  değişiminde hesaplayıp `BistableProjectState`'e kaydeder; SetInput/Eval/
  Tick/Reset tüm açık document'ların birleşimini **tek** batched `ReadSignals`
  ile yeniler. Değer değişimi hiçbir document'ta ELK'yi yeniden çalıştırmaz;
  layout backend sürecinde kalır.
- **Poke güvenliği (zorunlu regresyon):** Sürülebilir port çözümü tek noktaya
  (`topLevelDrivePort`) indirildi; hierarchical document'lar hiçbir zaman drive
  port çözemez — adı bir top-level input ile çakışan child boundary portu dahil.
  Child document'ta Poke modu kapalıdır; bare-name value/driven fallback'leri
  yalnız root'ta çalışır. `check-schematic-hierarchy.mjs` bu sözleşmeyi kilitler.
- **Testler:** `EngineInstancePathResolverTests` (9), EngineRpcServer
  capability + `loadModuleSchematic` entegrasyonu (`invalid_path` dahil),
  `check-schematic-hierarchy.mjs` (kimlik, breadcrumb, probe prefix, poke
  güvenliği, tek-batch birleşimi).

## Seçici inline expand/collapse — P9.5-10 ikinci dilim, 2026-07-19

Vivado-benzeri seçici hiyerarşi açma artık document içinde de çalışır; tüm
hiyerarşi asla tek seferde açılmaz.

- **Engine:** `EngineSchematicComposer`, seçilen relative instance path'lerini
  (`u_core`, `u_core.u_alu`) yerinde **Container** düğümü olarak compose eder.
  İç netler instance adıyla namespace'lenir (`u_alu.zero`) — exact per-bit
  kimlik korunur, alias yapılmaz. Child boundary portları parent net ↔
  namespaced iç net arasında pass-through Port sembolü olur (`typeLabel`
  yön ipucu). Bilinmeyen instance `invalid_path` döner.
- **Protokol:** `loadModuleSchematic` opsiyonel `expand[]` parametresi ve
  `schematic.expand` capability'si aldı; node DTO'suna `containerId` eklendi
  (additive, v2 korunur).
- **Layout:** ELK `INCLUDE_CHILDREN` ile container'lar iç içe düzenlenir;
  layout Theia backend'inde kalır. elkjs'in container-göreli kenar
  koordinatları flatten sırasında mutlak uzaya taşınır; child düğümler mutlak
  koordinatla tek düz listede döner (renderer'da özel iç içe çizim yok).
- **Widget:** Instance/Container başlığındaki ⊞/⊟ düğmesi expand/collapse
  yapar; double-click davranışı (ayrı document) korunur. Expansion durumu
  document başına relative path kümesidir; collapse, altındaki iç
  expansion'ları da budar ve parent seçim semantiği değişmez. Compose edilen
  graph'lar expansion-key ile memoize edilir (collapse geri dönüşü anlık),
  reload memo'yu temizler; yarım kalan expand layout generation guard'ıyla
  iptal edilir. Namespaced boundary netleri top-level input adlarıyla
  çakışamayacağı için Poke güvenliği yapısal olarak korunur.
- **Testler:** `EngineSchematicComposerTests` (5: düz eşdeğerlik, boundary
  pass-through, nested container zinciri, bilinmeyen instance, child-document
  expansion), RPC `expand` entegrasyonu, `check-schematic-hierarchy.mjs`
  (relative path/toggle/collapse-prune, pass-through iç net kimliği, nested
  container mutlak-koordinat layout doğrulaması).

Kalan iş: sahibin Poke + hiyerarşi görsel/etkileşim kabulü.

## Hedef mimari

```text
Bistable Theia Desktop (Electron)
├── Monaco / Explorer / Problems / Terminal / Settings
├── Bistable project & simulation widgets
├── Schematic documents (ELK geometry → SVG; layout Theia backend'de)
└── Waveform widget
                 │
                 │ versioned JSON-RPC over stdio
                 ▼
Bistable.Engine.Host (.NET)
├── Bistable.Engine (application services + transport DTOs)
├── Bistable.Core
├── Bistable.Verilator
├── Bistable.Yosys
└── Bistable.Protocol
```

## Guardrail'ler

- Var olan per-bit net kimlikleri ve seçim semantiği transport sırasında
  korunur; frontend için kozmetik bus collapse yapılmaz.
- Theia frontend ana thread'inde ELK layout veya tam RV32 graph taraması yoktur;
  layout backend sürecinde kalır.
- Engine host stdout'u yalnız protokol frame'leri içindir; loglar stderr'e gider.
- Host kapanınca sahip olduğu Verilator/Yosys/worker alt süreçlerini bırakmaz.
- POC, Avalonia kodunu silmek veya mevcut feature setini yeniden yazmak için
  otomatik yetki değildir; kalıcı göç go kararından sonra ayrı planlanır.
