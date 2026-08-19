namespace InovaGed.Application.Labels;
public interface ILabelTemplateRenderer { Task<LabelRenderDefinition?> BuildAsync(Guid tenantId,Guid templateId,IReadOnlyDictionary<string,string?> values,CancellationToken ct); }
