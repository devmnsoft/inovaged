namespace InovaGed.Web.Services;

public sealed record AtlasIllustrationDefinition(
    string Name,
    string Path,
    int Width,
    int Height,
    string Usage,
    bool DecorativeByDefault);

public interface IAtlasIllustrationRegistry
{
    bool TryGet(string name, out AtlasIllustrationDefinition definition);
}
