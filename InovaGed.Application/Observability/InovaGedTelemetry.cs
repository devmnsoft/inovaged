using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InovaGed.Application.Observability;

public static class InovaGedTelemetry
{
    public const string ServiceNamespace = "InovaGed";
    public static readonly ActivitySource Web = new("InovaGed.Web");
    public static readonly ActivitySource Database = new("InovaGed.Database");
    public static readonly ActivitySource Workers = new("InovaGed.Workers");
    public static readonly ActivitySource Ocr = new("InovaGed.Ocr");
    public static readonly ActivitySource Preview = new("InovaGed.Preview");
    public static readonly ActivitySource Deployment = new("InovaGed.Deployment");
    public static readonly ActivitySource Continuity = new("InovaGed.Continuity");
    public static readonly Meter Meter = new("InovaGed", "04.1.26");
}
