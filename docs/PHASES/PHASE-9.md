# Faz 9 — Canlı Döngü: İzle → Artımlı Elaborasyon → Tazele

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §7 H1+H2](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P0 — **vizyonun kalbi**
**Önkoşul:** Faz 7 (dürüst şematik) + Faz 8 (batch değer kanalı) kapanmış olmalı.
**Hedef deneyim:** *Dosyayı kaydet → ≤1–2 sn içinde şematik güncel → değerler
canlı akmaya devam ediyor → hata varsa satır referanslı panelde.*

**Faz kapısı (kabul):**
- Örnek ölçekli tasarımda (riscv_single_cycle) kaydet→şematik-güncel süresi
  ≤2 sn (otomatik ölçüm testi + sahibin elle denemesi).
- Sözdizimi/elaborasyon hatasında: panelde `dosya:satır` tıklanabilir kayıt;
  şematik "bayat" (stale) rozetiyle solmuş; düzeltince kendiliğinden toparlıyor.
- Port arayüzü değişmeyen kayıtta mevcut worker ve canlı değerler kesintisiz;
  arayüz değişiminde arka planda yeni worker derlenip şeffaf takas.
- Otomatik yeniden derleme aç/kapa ayarı; debounce süresi yapılandırılabilir.

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P9-1 | `ProjectFileWatcherService`: `FileSystemWatcher` (sources + includeDirs), 300–500 ms debounce, olay birleştirme (coalescing) | 1 g |
| P9-2 | Otomatik yeniden elaborasyon: mevcut `DesignLoadService.LoadAsync` yolunun izleyiciye bağlanması; koşarken yeni kayıt gelirse iptal+yeniden (mevcut cancellation altyapısı) | 1 g |
| P9-3 | Modül-düzeyi diff: eski/yeni `DesignAst` modül içerik-hash karşılaştırması → kirli modül kümesi; `ElkSchematicEngine` LRU anahtarının içerik-hash içerdiğinin doğrulanması (değilse eklenmesi) → değişmeyen scope'lar önbellekten | 1,5 g |
| P9-4 | Worker sıcak-takas politikası: port-arayüz hash'i değişmemişse worker korunur; değiştiyse arka planda `SimulationWorkerBuilder` ile yeni worker, hazır olunca operasyon koordinatörü üzerinden takas; bu sırada UI "yeniden derleniyor" durumu | 2 g |
| P9-5 | Hata yüzeyi: verilator stderr ayrıştırma (`%Error: dosya:satır:kolon`) → tanılama paneli (tıklanınca editörde/dosyada aç); şematikte stale rozeti | 1 g |
| P9-6 | Ayarlar: `runtime.liveReload { enabled, debounceMs }` proje şemasına + Preferences'a | 0,5 g |
| P9-7 | Testler: watcher-debounce birimi; diff'in kirli-küme doğruluğu; uçtan uca "dosyaya yaz → yeni elaborasyon → graf değişti" entegrasyonu; hata-panel ayrıştırma testi | 1,5 g |
| P9-8 | *(stretch)* Salt-okunur kaynak görüntüleyici bölmesi (AvaloniaEdit) + hata satırı vurgusu — kapı koşulu DEĞİL, döngünün hissini tamamlar | 2 g |

**Toplam tahmin:** ~8,5 gün (+stretch 2 g)

## Kod dokunuş noktaları

- **Yeni:** `src/Bistable.App/Services/ProjectFileWatcherService.cs`,
  `Services/ElaborationDiagnosticsParser.cs`
- `src/Bistable.App/Services/DesignLoadService*` — tetiklenebilir/iptal-birleştirmeli hâle
- `src/Bistable.App/Services/Routing/Elk/ElkSchematicEngine.cs` — önbellek anahtarı denetimi
- `src/Bistable.Verilator/SimulationWorkerBuilder.cs` + operasyon koordinatörü — arka plan derleme/takas
- `MainWindowViewModel` — **yalnız bağlama**; yeni mantık servislerde (Faz 12 ayrıştırmasına borç bırakma)
- `src/Bistable.Core/Projects/ProjectConfiguration` — liveReload ayarı

## Riskler / notlar

- **UI thread disiplini:** tüm izleme/elaborasyon/derleme arka planda; guardrail
  zaten "layout/measure UI thread'e dönmez" diyor — aynı kural burada da geçerli.
- Watcher fırtınası (editörlerin çoklu yazması): debounce + tek kuyruk; test edilmeli.
- Büyük tasarımda elaborasyon süresi: `--xml-only` hızlıdır ama ölçülmeli;
  gerekirse "yalnız kaydedilen dosyanın modülleri" için Verilator'a dosya-seti
  daraltması sonraki iterasyon.
- Bu faz kapandığında ürün, vizyon cümlesinin kendisini yapar; tanıtım
  GIF'i/videosu bu fazın kapısında çekilmeli (H6 görünürlüğü).
