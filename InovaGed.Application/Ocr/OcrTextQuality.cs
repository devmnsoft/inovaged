namespace InovaGed.Application.Ocr;

/// <summary>Deterministic review signal for fragmented text; it is not a confidence score or accuracy guarantee.</summary>
public static class OcrTextQuality
{
    public static bool IsFragmented(string text)
    {
        foreach (var page in text.Split('\f'))
        {
            var lines = page.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Any(char.IsLetter)).ToArray();
            if (lines.Length >= 12 && lines.Count(line => line.Length == 1) / (double)lines.Length >= 0.6)
                return true;
        }
        return false;
    }
}
