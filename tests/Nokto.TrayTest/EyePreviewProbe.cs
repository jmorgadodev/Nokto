using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nokto.UI.Controls;
using Avalonia.Threading;
using Nokto.Core.Models;

internal static class EyePreviewProbe
{
    public static void Run()
    {
        string output = Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas");
        Directory.CreateDirectory(output);
        var springStart = IslandSpring.Sample(110, 332, 0, 0);
        var springEnd = IslandSpring.Sample(110, 332, 0, .75);
        var springMoving = IslandSpring.Sample(110, 332, 0, .12);
        var springReverse = IslandSpring.Sample(springMoving.Position, 110, springMoving.Velocity, 0);
        if (Math.Abs(springStart.Position - 110) > .0001 || Math.Abs(springEnd.Position - 332) > .1 ||
            Math.Abs(springReverse.Velocity - springMoving.Velocity) > .0001)
            throw new InvalidOperationException("El resorte pierde continuidad al invertir o no converge.");
        foreach (string theme in new[] { "CinemaSauron", "CinemaToothless", "CinemaT800" })
            Render(output, 145, 68, theme, $"theme-{theme}.png" );
        string[] styles = ["CrimsonRing", "SharinganOne", "SharinganTwo", "UchihaItachi", "UchihaObito", "UchihaShisui", "UchihaSasuke", "UchihaSasukeEternal", "UchihaMadara", "UchihaMadaraEternal", "UchihaIzuna", "UchihaSarada", "UchihaMadaraRinnegan", "Feline", "SeriesByakugan", "SeriesGeass", "SeriesSixEyes", "SeriesDemonMoon"];
        foreach (string style in styles)
        {
            Render(output, 145, 68, style, $"eye-{style}.png");
            Render(output, 74, 34, style, $"eye-{style}-compact.png");
        }
        foreach (string style in new[] { "CrimsonRing", "UchihaMadaraRinnegan", "Feline", "Eyes" })
        {
            Render(output, 145, 68, style, $"eye-{style}-single.png", 1);
            Render(output, 42, 34, style, $"eye-{style}-single-compact.png", 1);
        }
        var renders = styles.Select(style => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(Path.Combine(output, $"eye-{style}.png"))))).ToArray();
        if (renders.Distinct().Count() != styles.Length)
            throw new InvalidOperationException("Dos variantes producen la misma imagen.");
        Console.WriteLine($"[PASS] Las {styles.Length} variantes producen imágenes distintas.");
        VerifyActivation();
        VerifyBinocularGaze();
        VerifyPersonalities(1);
        VerifyPersonalities(2);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<IslandSettings>("{}");
        var saved = System.Text.Json.JsonSerializer.Deserialize<IslandSettings>(
            System.Text.Json.JsonSerializer.Serialize(new IslandSettings { EyeCount = 1 }));
        if (legacy?.EyeCount != 2 || saved?.EyeCount != 1)
            throw new InvalidOperationException("Cantidad de ojos no conserva compatibilidad o persistencia.");
        Console.WriteLine("[PASS] Cantidad de ojos: configuración anterior y persistencia.");
    }

    private static void VerifyBinocularGaze()
    {
        var left = BinocularGaze.Toward(0, 60, -20, 0, 1);
        var right = BinocularGaze.Toward(0, 60, 20, 0, 1);
        if (left.X <= 0 || right.X >= 0 || Math.Abs(left.X + right.X) > .0001)
            throw new InvalidOperationException("Los ojos no convergen hacia el puntero central.");
        var farLeft = BinocularGaze.Toward(500, 100, -20, 0, 1);
        var farRight = BinocularGaze.Toward(500, 100, 20, 0, 1);
        if (farLeft.X <= 0 || farRight.X <= 0 || farLeft.X <= farRight.X || Math.Abs(farLeft.X) > 1)
            throw new InvalidOperationException("Seguimiento lateral independiente fuera de rango.");
        var eye = new NoktoEyesControl { StyleId = "Feline", Width = 145, Height = 68 };
        var panel = new Border { Background = Brush.Parse("#10151E"), Child = eye, Width = 145, Height = 68 };
        panel.Measure(new Size(145, 68));
        panel.Arrange(new Rect(0, 0, 145, 68));
        byte[] Snapshot()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(145, 68), new Vector(96, 96));
            bitmap.Render(panel);
            using var bytes = new MemoryStream();
            bitmap.Save(bytes);
            return bytes.ToArray();
        }
        eye.SetBinocularPose(.8, 0, -.8, 0, 0, false);
        byte[] converged = Snapshot();
        eye.SetBinocularPose(.8, 0, .8, 0, 0, false);
        if (converged.SequenceEqual(Snapshot()))
            throw new InvalidOperationException("El segundo ojo no responde de forma independiente al renderizar.");
        Console.WriteLine("[PASS] Cada ojo converge de forma independiente hacia el puntero.");
    }

    private static void VerifyPersonalities(int count)
    {
        var eyes = EyeStyleCatalog.All.Where(s => s.Id != "None").Select(s =>
        {
            var eye = new NoktoEyesControl { StyleId = s.Id, EyeCount = count, Width = 145, Height = 68 };
            var panel = new Border { Background = Brush.Parse("#10151E"), Child = eye, Width = 145, Height = 68 };
            panel.Measure(new Size(145, 68));
            panel.Arrange(new Rect(0, 0, 145, 68));
            return (s.Id, Eye: eye, Panel: panel);
        }).ToArray();
        byte[] Snapshot(Border panel)
        {
            using var image = new RenderTargetBitmap(new PixelSize(145, 68), new Vector(96, 96));
            image.Render(panel);
            using var stream = new MemoryStream();
            image.Save(stream);
            return stream.ToArray();
        }
        var before = eyes.Select(x => Snapshot(x.Panel)).ToArray();
        foreach (var entry in eyes)
        {
            typeof(NoktoEyesControl).GetField("_activationProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(entry.Eye, .4);
            typeof(NoktoEyesControl).GetField("_activationAngle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(entry.Eye, 1.2);
            typeof(NoktoEyesControl).GetField("_activationGlow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(entry.Eye, .7);
            entry.Eye.InvalidateVisual();
        }
        string[]? unchanged = null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        DispatcherTimer.RunOnce(() =>
        {
            unchanged = eyes.Where((entry, index) => before[index].SequenceEqual(Snapshot(entry.Panel))).Select(entry => entry.Id).ToArray();
            stop.Cancel();
        }, TimeSpan.FromMilliseconds(500));
        try { Dispatcher.UIThread.MainLoop(stop.Token); }
        catch (OperationCanceledException) { }
        foreach (var entry in eyes) entry.Eye.StopActivation();
        if (unchanged is null || unchanged.Length > 0)
            throw new InvalidOperationException("Estilos sin reacción visible: " + string.Join(", ", unchanged ?? []));
        Console.WriteLine($"[PASS] {eyes.Length} estilos muestran una reacción visual con {count} ojo(s).");
    }

    private static void VerifyActivation()
    {
        var eye = new NoktoEyesControl { StyleId = "UchihaItachi", Width = 145, Height = 68 };
        var panel = new Border { Background = Brush.Parse("#10151E"), Child = eye, Width = 145, Height = 68 };
        panel.Measure(new Size(145, 68));
        panel.Arrange(new Rect(0, 0, 145, 68));
        byte[] Snapshot()
        {
            using var image = new RenderTargetBitmap(new PixelSize(145, 68), new Vector(96, 96));
            image.Render(panel);
            using var stream = new MemoryStream();
            image.Save(stream);
            return stream.ToArray();
        }
        byte[] before = Snapshot();
        var frames = new List<byte[]>();
        byte[]? after = null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        eye.PlayActivation();
        foreach (int milliseconds in new[] { 700, 1200, 1800, 2450 })
        {
            int frameTime = milliseconds;
            DispatcherTimer.RunOnce(() =>
            {
                byte[] frame = Snapshot();
                frames.Add(frame);
                File.WriteAllBytes(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", $"awakening-{frameTime}.png"), frame);
            }, TimeSpan.FromMilliseconds(frameTime));
        }
        DispatcherTimer.RunOnce(() => { after = Snapshot(); stop.Cancel(); }, TimeSpan.FromMilliseconds(3500));
        try { Dispatcher.UIThread.MainLoop(stop.Token); }
        catch (OperationCanceledException) { }
        eye.StopActivation();
        if (frames.Count != 4 || frames.Select(Convert.ToHexString).Distinct().Count() != 4)
            throw new InvalidOperationException("La activación no produjo cuatro fases diferentes.");
        if (after is null || !before.SequenceEqual(after))
            throw new InvalidOperationException("La activación no volvió al estado de reposo.");
        Console.WriteLine("[PASS] Activación Mangekyō: cuatro fases distintas y regreso al reposo.");
    }

    private static void Render(string output, int width, int height, string style, string name, int eyeCount = 2)
    {
        var eye = new NoktoEyesControl { StyleId = style, EyeCount = eyeCount, Width = width, Height = height };
        var panel = new Border { Background = Brush.Parse("#10151E"), Child = eye, Width = width, Height = height };
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(panel);
        bitmap.Save(Path.Combine(output, name));
        Console.WriteLine($"[PASS] Sharingan {width}×{height}: {name}");
    }
}
