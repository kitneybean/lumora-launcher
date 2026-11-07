namespace Lumora.Launcher.App.Services;

public static class Text
{
    /// <summary>Russian plural: Plural(5, "мод", "мода", "модов") gives "5 модов".</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        var word = mod100 is >= 11 and <= 14 ? many : mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
        return $"{n} {word}";
    }

    public static string Memory(int mb) => mb >= 1024 ? $"{mb / 1024.0:0.#} ГБ" : $"{mb} МБ";
}
