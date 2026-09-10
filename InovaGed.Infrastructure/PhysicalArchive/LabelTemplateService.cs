using InovaGed.Application.PhysicalArchive;

namespace InovaGed.Infrastructure.PhysicalArchive;

public class LabelTemplateService : ILabelTemplateService
{
    public LabelTemplate GetCurrent(string subjectType) => new LabelTemplate("DUMMY", "Dummy", "Dummy");
}
