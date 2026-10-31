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
Expect(styles.Count == 30, $"Se esperaban 30 opciones, hay {styles.Count}");
Expect(styles.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == styles.Count,
    "El catalogo contiene ID duplicados");
Expect(styles.Select(s => s.Pack).Distinct().Count() == 8, "Se esperaban ocho familias");
foreach (var style in styles)
{
    Expect(EyeStyleCatalog.Find(style.Id).Id == style.Id, "No se recupero " + style.Id);
    Expect(!string.IsNullOrWhiteSpace(style.Es) && !string.IsNullOrWhiteSpace(style.En),
        "Traduccion vacia en " + style.Id);
}
Expect(EyeStyleCatalog.Find("MagiSystem").Id == "Eyes" && EyeStyleCatalog.Find("IPhoneIsland").Id == "Eyes", "Los estilos retirados deben migrar a Clásico");
Expect(EyeStyleCatalog.Find("None").Id == "None" && !EyeStyleCatalog.HasIndicator("None"), "El modo vacío debe estar disponible");
Expect(!EyeStyleCatalog.IsMinimal("Feline"), "Felino no debe ser minimalista");
Expect(EyeStyleCatalog.Find("Eva01").Id == "Eyes", "Los EVA retirados deben migrar a Clásico");
Expect(EyeStyleCatalog.IsSharingan("UchihaItachi") && EyeStyleCatalog.IsSharingan("CrimsonRing"), "Variantes Sharingan no detectadas");
Console.WriteLine("[PASS] 29 estilos visuales + modo sin indicador, ocho packs, identificadores únicos, traducciones ES/EN y migración de estilos retirados.");
