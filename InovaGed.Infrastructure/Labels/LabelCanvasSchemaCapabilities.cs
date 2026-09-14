using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasSchemaCapabilities(IDbConnectionFactory dbFactory) : ILabelCanvasSchemaCapabilities
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LabelCanvasSchemaCapabilitySnapshot? _cached;
    private DateTimeOffset _expiresAt;

    public async Task<LabelCanvasSchemaCapabilitySnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt) return _cached;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt) return _cached;
            await using var db = await dbFactory.OpenAsync(cancellationToken);
            const string sql = """
select
  to_regclass('ged.label_template_design') is not null as HasLabelTemplateDesign,
  exists(select 1 from information_schema.columns where table_schema='ged' and table_name='label_template_design' and column_name='lock_version') as HasLockVersion,
  to_regclass('ged.label_canvas_component_preset') is not null as HasComponentPreset,
  to_regclass('ged.label_print_selection') is not null as HasPrintSelection
""";
            _cached = await db.QuerySingleAsync<LabelCanvasSchemaCapabilitySnapshot>(new CommandDefinition(sql, cancellationToken: cancellationToken));
            _expiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _cached;
        }
        finally { _gate.Release(); }
    }
}
