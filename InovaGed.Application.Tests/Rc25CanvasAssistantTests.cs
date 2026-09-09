using InovaGed.Application.SmartGed.Assistant;
using InovaGed.Infrastructure.Labels;
using InovaGed.Infrastructure.PhysicalArchive;
using InovaGed.Infrastructure.SmartGed.Assistant;

namespace InovaGed.Application.Tests;

public sealed class Rc25CanvasAssistantTests
{
 [Theory]
 [InlineData("Box","BOX")][InlineData("LocDeskBox","BOX")][InlineData("Document","DOCUMENT")][InlineData("Folder","DOCUMENT")][InlineData("MedicalRecord","DOCUMENT")][InlineData("Process","DOCUMENT")][InlineData("LocDeskFolder","DOCUMENT")][InlineData("Batch","BATCH")]
 public void Designer_subject_type_is_normalized_only_at_operational_boundary(string semantic,string expected)=>Assert.Equal(expected,LabelTemplateCatalogService.NormalizeDesignerSubjectType(semantic));

 [Theory]
 [InlineData("Box","boxCode")][InlineData("Document","documentCode")][InlineData("Folder","folderName")][InlineData("MedicalRecord","recordNumber")][InlineData("Process","processNumber")]
 public void Canvas_value_resolver_covers_each_semantic_type(string semantic,string required)
 {
  var id=Guid.Parse("11111111-2222-3333-4444-555555555555");var entity=new Dictionary<string,object?>{{"code","DOC-001"},{"title","Documento"},{"process_number","PROC-001"},{"record_number","PR-001"},{"patient_name","Pessoa"},{"label_code","CX-001"},{"box_no","42"}};var folder=new Dictionary<string,object?>{{"name","Pasta administrativa"}};
  var values=LabelCanvasValueResolver.ResolveValues(semantic,id,entity,folder,new Dictionary<string,object?>{{"code","ADM.120"},{"title","Documentação administrativa"}},new Dictionary<string,object?>{{"location_code","AC.E01"}});
  Assert.True(values.ContainsKey(required));Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(values[required])));
 }

 [Theory]
 [InlineData("hoje","2026-09-08","2026-09-09")][InlineData("ontem","2026-09-07","2026-09-08")][InlineData("esta semana","2026-09-07","2026-09-14")][InlineData("em 2026","2026-01-01","2027-01-01")]
 public void Relative_dates_produce_half_open_ranges(string question,string from,string to)
 {
  var range=LocalSmartAssistantIntentResolver.ResolveDateRange(question,new DateOnly(2026,9,8));Assert.NotNull(range);Assert.Equal(DateOnly.Parse(from),range.Value.From);Assert.Equal(DateOnly.Parse(to),range.Value.To);
 }

 [Theory][InlineData("Quantas etiquetas foram reimpressas hoje?")][InlineData("Quantos documentos estão sem classificação?")]
 public void Quantitative_questions_request_count(string question)=>Assert.Equal("COUNT",new LocalSmartAssistantIntentResolver().Resolve(question,[]).Aggregation);

 [Fact]
 public void Follow_up_reuses_template_and_box_context()
 {
  var resolver=new LocalSmartAssistantIntentResolver();var history=new[]{new SmartAssistantConversationMessage("USER","Qual perfil usa CLIENTE_CAIXA_CANVAS_V1?"),new SmartAssistantConversationMessage("ASSISTANT","A caixa CX-0042 está em AC.E01.")};
  var template=resolver.Resolve("Qual logo esse template usa?",history);Assert.Equal("CLIENTE_CAIXA_CANVAS_V1",template.Filters["template"]);
  var box=resolver.Resolve("Quais documentos estão nessa caixa?",history);Assert.Equal("FIND_DOCUMENT",box.Intent);Assert.Equal("CX-0042",box.Filters["boxCode"]);
 }

 [Theory][InlineData("/Labels/History",true)][InlineData("/Labels/Designer/Edit/CLIENTE_V1",true)][InlineData("//evil.example",false)][InlineData("https://evil.example",false)][InlineData("/ok\r\nLocation: evil",false)][InlineData("\\server",false)]
 public void Action_urls_accept_only_safe_local_paths(string url,bool valid)=>Assert.Equal(valid,SmartAssistantActionUrlPolicy.Normalize(url) is not null);

 [Fact]
 public void Generic_samples_do_not_expose_legacy_customer_brands()
 {
  var sample=new LabelCanvasFieldCatalogService().GetSampleData("Documento GED");var text=string.Join(' ',sample.Values);Assert.DoesNotContain("HOL.132.3",text,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Ophir Loyola",text,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("LocDesk",text,StringComparison.OrdinalIgnoreCase);Assert.Contains("ADM.120",text);
 }
}
