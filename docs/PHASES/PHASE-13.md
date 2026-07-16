# Faz 13 — Gate-Level Kapanış: 6.5 Ölçümleri + Expand Cone

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §7 H5](../VISION_GAP_ANALYSIS.md); `docs/HANDOFFS/PHASE-6.5-GATE-PIN-LABELS-NEXT.md` §5 kapanış kalemleri.
**Öncelik:** P2
**Önkoşul:** yok (bağımsız; P0'ları bloklamasın diye arkaya alındı)
**Hedef:** En olgun eksen olan gate-level görüntüleyicinin resmî kapanışı +
Vivado-tarzı odaklı inceleme (`Expand Cone`).

**Faz kapısı (kabul):**
- Güncel GUI/Yosys akışıyla yeniden üretilmiş RV32 sentez JSON'u üzerinde
  etiketler kapalı/gruplu/detaylı kare-süre ölçümleri kayıtlı (PHASE-6.5.md'ye).
- Sahibin manuel kabulü: RV32 pin okunabilirliği, hover tooltip, bus trunk,
  yerinde modül genişletme.
- `Expand Cone`: seçili pin/hücreden fan-in ve fan-out konisi — yalnız koni
  düğümleri görünür, kalanlar soluk/gizli; derinlik sınırı ayarlanabilir.
- PHASE-6.5.md durum satırı "Closed" + tarih.

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P13-1 | RV32 sentez JSON'unu güncel akışla yeniden üret; eski/bayat artefaktı değiştir | 0,5 g |
| P13-2 | Kare-süre ölçümleri (hidden/grouped/detailed) — otomatikleştirilebilir kısmı UiTests bütçe testine, sayılar PHASE-6.5.md'ye | 1 g |
| P13-3 | `GateConeAnalyzer`: `GatePinInteractionIndex` üzerinden BFS fan-in/fan-out (derinlik sınırlı); per-bit net kimliği korunur (guardrail) | 2 g |
| P13-4 | UI: pin/hücre bağlam menüsü "Expand Cone (in/out)", derinlik seçimi, koni-dışını soluklaştır/gizle; temizle | 1,5 g |
| P13-5 | Testler: koni doğruluğu (bilinen grafikte beklenen küme), performans (RV32 ölçeği), UI smoke | 1 g |
| P13-6 | Manuel kabul oturumu + PHASE-6.5.md kapanış kaydı | 0,5 g |

**Toplam tahmin:** ~6,5 gün

## Kod dokunuş noktaları

- `App/Services/Routing/Elk/` — yeni `GateConeAnalyzer` (index zaten var: `GatePinInteractionIndex`)
- `App/Views/GateSchematicCanvas.cs`, `GateLevelSchematicView.cs` — koni modu render/etkileşim
- `docs/PHASES/PHASE-6.5.md` — ölçümler + kapanış

## Riskler / notlar

- Koni hesabı O(kenar) BFS — RV32'de sorun beklenmez; yine de bütçe testi.
- Guardrail hatırlatması: bit-düzeyi net kimlikleri ve seçim semantiği bozulamaz;
  koni yalnız görünürlük filtresidir, graf yeniden kurulmaz.
