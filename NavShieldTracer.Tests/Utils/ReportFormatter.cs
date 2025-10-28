namespace NavShieldTracer.Tests.Utils;

public static class ReportFormatter
{
    public static void WriteSection(string title, params (string Label, string Value)[] lines)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");

        if (lines.Length == 0)
        {
            return;
        }

        var padding = lines.Max(l => l.Label.Length);

        foreach (var (label, value) in lines)
        {
            Console.WriteLine($"{label.PadRight(padding)} : {value}");
        }
    }

    public static void WriteList(string title, IEnumerable<string> items)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");
        foreach (var item in items)
        {
            Console.WriteLine($"- {item}");
        }
    }
}
