namespace Nokto.Core;

/// <summary>
/// Identidad de la distribucion local de desarrollo. La version publica sigue siendo Nokto.exe.
/// El ejecutable portable de pruebas se publica con el nombre Nokto.Dev.exe.
/// </summary>
public static class BuildIdentity
{
    public static bool IsDevelopment => string.Equals(
        Path.GetFileName(Environment.ProcessPath),
        "Nokto.Dev.exe",
        StringComparison.OrdinalIgnoreCase);
}
