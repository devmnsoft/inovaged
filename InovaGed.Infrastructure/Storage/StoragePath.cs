using System.IO;

namespace InovaGed.Infrastructure.Storage;

public static class StoragePath
{
    public static string CombineSafe(string root, params string[] parts)
    {
        var p = root;
        foreach (var part in parts)
            p = Path.Combine(p, part);

        return p;
    }
}