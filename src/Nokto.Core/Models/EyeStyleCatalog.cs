namespace Nokto.Core.Models;

public sealed record EyeStyleDefinition(string Id, string Pack, string Es, string En,
    string Shape, string Pupil, string Color, string Iris);

/// <summary>Diseños vectoriales originales, sin archivos externos ni recursos de franquicias.</summary>
public static class EyeStyleCatalog
{
    public static readonly IReadOnlyList<EyeStyleDefinition> All = new EyeStyleDefinition[]
    {
        new("GreenDot","Minimal","Punto verde","Green dot","dot","round","#00E676","#00E676"),
        new("BlackDot","Minimal","Punto negro","Black dot","dot","round","#12151C","#12151C"),
        new("GlowDot","Minimal","Punto luminoso","Glow dot","dot","round","#72EFFF","#72EFFF"),
        new("DualDot","Minimal","Dos puntos","Dual dots","dual","round","#E7F5FF","#95B7FF"),
        new("SensorLine","Minimal","Línea sensor","Sensor line","line","round","#6DF9B6","#6DF9B6"),
        new("FocusDot","Minimal","Sensor de enfoque","Focus sensor","focus","round","#9CDFFF","#9CDFFF"),

        new("Eyes","Nokto","Clásico","Classic","oval","round","#F2FAFF","#182532"),
        new("ClassicSoft","Nokto","Clásico suave","Soft classic","oval","round","#DDEBFC","#3E5A73"),
        new("ClassicNeon","Nokto","Neón","Neon","ring","round","#5DF5F0","#12B8EA"),
        new("ClassicMono","Nokto","Monocromo","Monochrome","oval","round","#FFFFFF","#060A0F"),
        new("ClassicGhost","Nokto","Fantasma","Ghost","oval","round","#C8CAFC","#635DC4"),
        new("ClassicSleepy","Nokto","Dormilón","Sleepy","sleepy","round","#DBECFF","#516E8A"),

        new("Orbital","Cyber","Orbital","Orbital","ring","ring","#7DD9FF","#4870FF"),
        new("Sentinel","Cyber","Centinela","Sentinel","hex","slit","#59EACD","#2CE0E7"),
        new("Scanner","Cyber","Escáner","Scanner","scanner","slit","#75FC91","#30BF70"),
        new("Pulse","Cyber","Pulso","Pulse","ring","round","#FF6BBC","#E22C80"),
        new("Echo","Cyber","Eco","Echo","ring","ring","#9AA2FF","#5E5FFF"),
        new("Vector","Cyber","Vector","Vector","hex","round","#FED877","#F6A934"),

        new("CrimsonRing","Anime","Anillo carmesí","Crimson ring","ring","ring","#FF7777","#B42638"),
        new("Vortex","Anime","Vórtice","Vortex","spiral","ring","#BDB1FF","#8064FE"),
        new("LunarVein","Anime","Vena lunar","Lunar vein","ring","slit","#B9ECFF","#5685E2"),
        new("SpiralFocus","Anime","Espiral","Spiral focus","spiral","round","#E7BBFF","#9858EB"),
        new("Ember","Anime","Brasa","Ember","ring","slit","#FFB067","#E64E28"),
        new("PhantomGaze","Anime","Mirada fantasma","Phantom gaze","oval","ring","#DED4FF","#8366D4"),

        new("Feline","Creature","Felino","Feline","cat","slit","#FFD38C","#52341A"),
        new("Reptile","Creature","Reptil","Reptile","cat","slit","#B4F179","#415830"),
        new("Owl","Creature","Búho","Owl","round","round","#FFDA9C","#684629"),
        new("Fox","Creature","Zorro","Fox","cat","slit","#FFA96C","#9C4321"),
        new("Wolf","Creature","Lobo","Wolf","oval","slit","#C4DEFA","#54778B"),
        new("Dragon","Creature","Dragón","Dragon","ring","slit","#9CF4BB","#299461"),

        new("Kawaii","Cute","Kawaii","Kawaii","round","round","#FFC4E5","#964C90"),
        new("Surprised","Cute","Sorprendido","Surprised","round","round","#FFF3C1","#5868AF"),
        new("Shy","Cute","Tímido","Shy","sleepy","round","#F8BCD8","#A95D88"),
        new("Happy","Cute","Feliz","Happy","happy","round","#F8EFA6","#C27C38"),
        new("Pixel","Cute","Píxel","Pixel","pixel","square","#B5FAE9","#17686E"),
        new("TinySleepy","Cute","Mini dormilón","Tiny sleepy","sleepy","slit","#FFD6F0","#9A5179"),

        new("Phantom","Dark","Fantasma oscuro","Dark phantom","oval","ring","#A8ABBC","#6B5B9B"),
        new("Spectral","Dark","Espectral","Spectral","ring","slit","#93FFC4","#4C977C"),
        new("Shadow","Dark","Sombra","Shadow","sleepy","slit","#8995B1","#30384F"),
        new("Glitch","Dark","Glitch","Glitch","pixel","square","#F276A3","#762D5F"),
        new("Void","Dark","Vacío","Void","round","ring","#F27C79","#A72E40"),
        new("Hollow","Dark","Hueco","Hollow","ring","ring","#B5D1EC","#455A81"),
    };

    public static EyeStyleDefinition Find(string? id)
        => All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
           ?? All[6];

    public static int IndexOf(string? id)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        return 6; // Classic
    }

    public static bool IsMinimal(string? id) => Find(id).Pack == "Minimal";
}
