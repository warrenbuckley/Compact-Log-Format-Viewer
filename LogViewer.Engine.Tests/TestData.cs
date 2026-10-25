namespace LogViewer.Engine.Tests;

internal static class TestData
{
    /// <summary>A mixed log: message-template properties, properties the template does not mention, nested structures, arrays, exceptions and escapes.</summary>
    public static void WriteMixedLog(string path, int rows)
    {
        var rng = new Random(7);
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var lines = new List<string>();
        for (var i = 0; i < rows; i++)
        {
            var t = $"\"@t\":\"{t0.AddMilliseconds(i * 13):O}\"";
            lines.Add((i % 6) switch
            {
                0 => $"{{{t},\"@mt\":\"Hello {{Name}} from {{City}}\",\"Name\":\"Person{i % 17}\",\"City\":\"Town{i % 5}\",\"Duration\":{rng.Next(0, 2500)},\"SourceContext\":\"Umbraco.Core.Thing{i % 3}\"}}",
                1 => $"{{{t},\"@mt\":\"Took {{Duration}}ms\",\"@l\":\"Debug\",\"Duration\":{rng.Next(0, 2500)},\"SourceContext\":\"Umbraco.Web.Other\",\"Tags\":[\"a\",\"b{i % 4}\"]}}",
                2 => $"{{{t},\"@mt\":\"Nested {{Info}}\",\"Info\":{{\"$type\":\"Info\",\"Id\":{i},\"Label\":\"label{i % 9}\"}},\"Duration\":{rng.Next(0, 2500)}}}",
                3 => $"{{{t},\"@mt\":\"No properties used here\",\"@l\":\"Warning\",\"@@escaped\":{i % 3},\"Other\":{i}}}",
                4 => $"{{{t},\"@mt\":\"Failure for {{Name}}\",\"@l\":\"Error\",\"@x\":\"System.Exception: nope {i}\",\"Name\":\"Person{i % 17}\"}}",
                _ => $"{{{t},\"@mt\":\"Brace {{{{literal}}}} {{Name}}\",\"Name\":\"Bob {i}\",\"Duration\":{i % 3000}}}"
            });
        }

        File.WriteAllLines(path, lines);
    }
}
