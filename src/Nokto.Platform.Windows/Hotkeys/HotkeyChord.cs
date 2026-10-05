namespace Nokto.Platform.Windows.Hotkeys;

public readonly record struct HotkeyChord(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            uint modifier = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 2,
                "SHIFT" => 4,
                "ALT" => 1,
                "WIN" or "WINDOWS" => 8,
                _ => 0
            };
            if (modifier == 0 || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        string key = parts[^1].ToUpperInvariant();
        uint vk = key switch { "PAUSE" or "PAUSA" or "BREAK" => 0x13, "SPACE" or "ESPACIO" => 0x20, _ => 0 };
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) vk = key[0];
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 24 && function != 12)
            vk = (uint)(0x70 + function - 1);
        if (vk == 0) return false;
        chord = new(modifiers, vk);
        return true;
    }
}
