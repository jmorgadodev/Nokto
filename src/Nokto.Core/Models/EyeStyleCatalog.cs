namespace Nokto.Core.Models;

public sealed record EyeStyleDefinition(string Id, string Pack, string Es, string En,
    string Shape, string Pupil, string Color, string Iris);

/// <summary>Catálogo curado de diseños vectoriales y texturas locales.</summary>
public static class EyeStyleCatalog
{
    public static readonly IReadOnlyList<EyeStyleDefinition> All = new EyeStyleDefinition[]
    {

        new("Eyes","Nokto","Clásico","Classic","oval","round","#F2FAFF","#182532"),

        new("Orbital","Cyber","Orbital","Orbital","ring","ring","#7DD9FF","#4870FF"),
        new("Sentinel","Cyber","Centinela","Sentinel","hex","slit","#59EACD","#2CE0E7"),

        new("CrimsonRing","Anime","Sharingan · tres tomoe","Sharingan · three tomoe","ring","ring","#FF7777","#B42638"),

        new("Feline","Creature","Felino","Feline","cat","slit","#FFD38C","#52341A"),
        new("Fox","Creature","Zorro","Fox","cat","slit","#FFA96C","#9C4321"),
        new("Dragon","Creature","Dragón","Dragon","ring","slit","#9CF4BB","#299461"),

        new("Kawaii","Cute","Kawaii","Kawaii","round","round","#FFC4E5","#964C90"),
        new("Happy","Cute","Feliz","Happy","happy","round","#F8EFA6","#C27C38"),

        new("Spectral","Dark","Espectral","Spectral","ring","slit","#93FFC4","#4C977C"),
        new("None","Minimal","Sin indicador","No indicator","none","round","#080D15","#080D15"),
        new("SharinganOne","Anime","Sharingan · un tomoe","Sharingan · one tomoe","ring","ring","#FF7777","#B42638"),
        new("SharinganTwo","Anime","Sharingan · dos tomoe","Sharingan · two tomoe","ring","ring","#FF7777","#B42638"),
        new("UchihaItachi","Anime","Mangekyō · Itachi","Mangekyō · Itachi","ring","ring","#FF7777","#B42638"),
        new("UchihaObito","Anime","Mangekyō · Obito / Kakashi","Mangekyō · Obito / Kakashi","ring","ring","#FF7777","#B42638"),
        new("UchihaShisui","Anime","Mangekyō · Shisui","Mangekyō · Shisui","ring","ring","#FF7777","#B42638"),
        new("UchihaSasuke","Anime","Mangekyō · Sasuke","Mangekyō · Sasuke","ring","ring","#FF7777","#B42638"),
        new("UchihaSasukeEternal","Anime","Mangekyō eterno · Sasuke","Eternal Mangekyō · Sasuke","ring","ring","#FF7777","#B42638"),
        new("UchihaMadara","Anime","Mangekyō · Madara","Mangekyō · Madara","ring","ring","#FF7777","#B42638"),
        new("UchihaMadaraEternal","Anime","Mangekyō eterno · Madara","Eternal Mangekyō · Madara","ring","ring","#FF7777","#B42638"),
        new("UchihaIzuna","Anime","Mangekyō · Izuna","Mangekyō · Izuna","ring","ring","#FF7777","#B42638"),
        new("UchihaSarada","Anime","Mangekyō · Sarada","Mangekyō · Sarada","ring","ring","#FF7777","#B42638"),
        new("UchihaMadaraRinnegan","Anime","Rinnegan · Madara","Rinnegan · Madara","ring","ring","#D6C8FA","#8C70B7"),
        new("SeriesByakugan","Anime","Byakugan · Naruto","Byakugan · Naruto","ring","ring","#E3DBF4","#AAA1C9"),
        new("SeriesGeass","Anime","Geass · Lelouch","Geass · Lelouch","ring","ring","#E99BB8","#6B396F"),
        new("SeriesSixEyes","Anime","Seis Ojos · Gojo","Six Eyes · Gojo","ring","ring","#DAF9FF","#2B99D0"),
        new("SeriesDemonMoon","Anime","Luna demoníaca · Kimetsu","Demon Moon · Kimetsu","ring","slit","#FFD47C","#C25B38"),
        new("CinemaSauron","Cinema","Sauron · El Señor de los Anillos","Sauron · The Lord of the Rings","ring","slit","#F6B444","#C65419"),
        new("CinemaToothless","Cinema","Desdentao · Cómo entrenar a tu dragón","Toothless · How to Train Your Dragon","cat","slit","#A6E95C","#79AA32"),
        new("CinemaT800","Cinema","T-800 · Terminator","T-800 · Terminator","round","round","#FF543C","#AA201F"),
        new("StaticDot","Minimal","Punto estático","Static dot","dot","none","#74E5A3","#74E5A3"),
    };

    public static EyeStyleDefinition Find(string? id)
        => All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
           ?? All.First(x => x.Id == "Eyes");

    public static int IndexOf(string? id)
    {

        for (int i = 0; i < All.Count; i++)
            if (All[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        return All.ToList().FindIndex(x => x.Id == "Eyes");
    }

    public static bool IsMinimal(string? id) => Find(id).Pack == "Minimal";
    public static bool IsSharingan(string? id) => id == "CrimsonRing" ||
        (id?.StartsWith("Sharingan", StringComparison.Ordinal) == true) ||
        (id?.StartsWith("Uchiha", StringComparison.Ordinal) == true);
    public static bool IsSeriesEye(string? id) => id?.StartsWith("Series", StringComparison.Ordinal) == true;
    public static bool HasIndicator(string? id) => !string.Equals(Find(id).Id, "None", StringComparison.OrdinalIgnoreCase);
}
