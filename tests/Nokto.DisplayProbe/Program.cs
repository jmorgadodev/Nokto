using Nokto.Platform.Windows.Display;
using Nokto.Core.Models;

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Expect(DisplayRefreshRateDetector.Normalize(239) == 240, "239 debe normalizar a 240");
Expect(DisplayRefreshRateDetector.Normalize(59) == 60, "59 debe normalizar a 60");
Expect(DisplayRefreshRateDetector.Normalize(144) == 144, "144 debe permanecer en 144");
Expect(DisplayRefreshRateDetector.Normalize(0) == 60, "0 debe usar fallback 60");

int primaryHz = DisplayRefreshRateDetector.GetPrimaryRefreshRate();
int atOriginHz = DisplayRefreshRateDetector.GetRefreshRateAt(0, 0);
Expect(primaryHz >= 30 && primaryHz <= 500, "Frecuencia primaria fuera de rango");
Expect(atOriginHz >= 30 && atOriginHz <= 500, "Frecuencia por monitor fuera de rango");
Console.WriteLine($"[PASS] Monitor principal: {primaryHz} Hz");
Console.WriteLine($"[PASS] Monitor en (0,0): {atOriginHz} Hz");
Console.WriteLine("[PASS] Normalizacion nominal 239/240, 59/60 y fallback.");

var styles = EyeStyleCatalog.All;
Expect(styles.Count == 43, $"Se esperaban 42 estilos, hay {styles.Count}");
Expect(styles.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 43,
    "El catalogo contiene ID duplicados");
Expect(styles.Select(s => s.Pack).Distinct().Count() == 7, "Se esperaban siete familias");
foreach (var style in styles)
{
    Expect(EyeStyleCatalog.Find(style.Id).Id == style.Id, "No se recupero " + style.Id);
    Expect(!string.IsNullOrWhiteSpace(style.Es) && !string.IsNullOrWhiteSpace(style.En),
        "Traduccion vacia en " + style.Id);
}
Expect(EyeStyleCatalog.IsMinimal("GreenDot"), "El punto verde debe ser minimalista");
Expect(EyeStyleCatalog.Find("None").Id == "None" && !EyeStyleCatalog.HasIndicator("None"), "El modo vacío debe estar disponible");
Expect(!EyeStyleCatalog.IsMinimal("Feline"), "Felino no debe ser minimalista");
Expect(EyeStyleCatalog.IndexOf("Eyes") == 6, "Compatibilidad del estilo Eyes");
Console.WriteLine("[PASS] 42 estilos visuales + modo sin indicador, siete packs, identificadores unicos, traducciones ES/EN.");
