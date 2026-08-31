using InovaGed.Environment.Doctor.Quality;
namespace InovaGed.Environment.Doctor.Checks;
public sealed class MigrationFileQualityCheck : IQualityCheck
{
 public string Name => "Migrations";
 private static readonly string[] Files = ["database/apply_all_required_migrations.sql","database/migrations/2026_08_label_template_designer.sql","database/migrations/2026_08_classification_plan_compat_hotfix.sql","database/migrations/2026_08_retention_destination_di_schema_hotfix.sql","database/migrations/2026_08_label_print_queue.sql","database/migrations/2026_08_label_print_modes_and_templates.sql","database/migrations/2026_08_31_label_print_fidelity_2.sql"];
 private static readonly string[] Blocks = ["label_template","classification_plan","classification_plan_version","retention_destination","label_print_job","locdesk_label_draft"];
 public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext c, CancellationToken ct) { var r=new List<QualityFinding>(); foreach(var f in Files) r.Add(File.Exists(Path.Combine(c.Root,f)) ? new(Name,QualityStatus.Pass,$"Arquivo obrigatório presente: {f}",Resource:f) : new(Name,QualityStatus.Fail,$"Migration obrigatória ausente: {f}","O schema não pode ser reproduzido.","Restaurar/criar o script obrigatório.",f,f)); var apply=Path.Combine(c.Root,Files[0]); if(File.Exists(apply)){var sql=await File.ReadAllTextAsync(apply,ct); foreach(var b in Blocks) r.Add(sql.Contains(b,StringComparison.OrdinalIgnoreCase)?new(Name,QualityStatus.Pass,$"Bloco de migration presente: {b}"):new(Name,QualityStatus.Fail,$"Bloco obrigatório ausente no aplicador: {b}","Deploy incompleto.",$"Adicionar o bloco idempotente de {b}.",Files[0],Files[0]));} return r; }
}
