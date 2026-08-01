namespace InovaGed.Web.Services;

public sealed record AtlasIconDefinition(
    string Name,
    string SymbolId,
    string Category,
    string Usage,
    string Variant,
    IReadOnlyList<string> Aliases);

public interface IAtlasIconRegistry
{
    bool TryGet(string name, out AtlasIconDefinition definition);
    IReadOnlyCollection<AtlasIconDefinition> GetAll();
}
