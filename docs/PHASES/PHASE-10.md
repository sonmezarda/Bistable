# Faz 10 — Şematik Export: SVG / PNG

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §7 H4](../VISION_GAP_ANALYSIS.md); eski `SCHEMATIC_REWRITE_PLAN.md` Faz 7'nin mirasçısı.
**Öncelik:** P1 (küçük iş, görünür değer)
**Önkoşul:** Faz 7 (yumuşak — dürüst olmayan şematiği export etmek değersiz)
**Hedef:** RTL ve gate görünümleri `File → Export` ile SVG (vektör) ve PNG
(bitmap) olarak dışa aktarılır.

**Faz kapısı (kabul):**
- SVG, tarayıcı ve Inkscape'te doğru açılır (düğümler, kenarlar, etiketler,
  junction'lar, tema renkleri).
- PNG mevcut görünümün birebir rasteri.
- Gate görünümünde de her iki export çalışır.
- SVG yapı testi (golden) + PNG smoke testi.

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P10-1 | `Services/Export/SchematicSvgExporter.cs`: düzenlenmiş `ElkGraph` (geometri + tema) → SVG; düğüm gövdeleri, portlar, kenar poliline'ları, junction, bridge, etiketler | 1,5 g |
| P10-2 | PNG: mevcut Skia render yolundan `RenderTargetBitmap` kaydı (RTL `SchematicPreviewControl`, gate `GateSchematicCanvas`) | 0,5 g |
| P10-3 | Menü + dosya diyaloğu (`File → Export → SVG…/PNG…`); aktif görünüme göre yönlendirme | 0,5 g |
| P10-4 | Testler: SVG golden (yapısal — koordinat toleranslı), PNG boyut/format smoke | 0,5 g |

**Toplam tahmin:** ~3 gün

## Kod dokunuş noktaları

- **Yeni:** `src/Bistable.App/Services/Export/SchematicSvgExporter.cs`
- `MainWindow` menü + komut bağlama (mantık serviste — Faz 12 disiplinine uygun)
- `SchematicTheme` renklerinin SVG'ye taşınması (hex zaten mevcut)

## Riskler / notlar

- Yazı tipi ölçümü SVG'de birebir değildir; `text-anchor` + ölçülen genişlik
  kutuları ile yaklaşıklık yeterli (golden toleransı buna göre).
- İleride PDF istenirse SVG→PDF harici araca bırakılır (kapsam dışı).
