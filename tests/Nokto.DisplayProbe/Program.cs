using Nokto.Platform.Windows.Display;

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
