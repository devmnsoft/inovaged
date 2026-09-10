using System.Text.Json;
using InovaGed.Application.PhysicalArchive;

namespace InovaGed.Infrastructure.PhysicalArchive;

public class LabelPayloadBuilder : ILabelPayloadBuilder
{
    public string Build(object snapshot) => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
