using System.Text.Json;
using System.Text.Json.Nodes;
using InovaGed.Application.Sectors;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorClinicalEvidenceTests
{
    internal const string Evidence="Medicamento sintético 10 mg via oral a cada 8 horas. Quantidade: 2. Data: 15/10/2026.";
    internal static JsonObject Item(string evidence=Evidence,string context="PRESCRITO")=>new()
    {
        ["category"]="medicamento",["name"]="Medicamento sintético",["dose"]="10 mg",["unit"]="mg",["route"]="oral",
        ["frequency"]="a cada 8 horas",["quantity"]="2",["date"]="15/10/2026",["context"]=context,["evidence"]=evidence
    };
    internal static JsonDocument Output(JsonObject? item=null)=>JsonDocument.Parse(new JsonObject { ["items"]=new JsonArray(item??Item()) }.ToJsonString());

    [Fact]
    public void Structured_items_are_literal_and_page_is_derived_from_the_unique_source_excerpt()
    {
        using var json=Output();
        Assert.True(SectorClinicalEvidence.TryRead(json.RootElement,"--- página 3 ---\n"+Evidence,"DS23",out var items));
        var item=Assert.Single(items);
        Assert.Equal(3,item.Page); Assert.Equal("10 mg",item.Dose);Assert.Equal("oral",item.Route);
        Assert.Contains("Quantidade: 2",item.DisplayValue);
        Assert.True(SectorClinicalEvidence.TryRead(json.RootElement,Evidence+"\n--- página 4 ---\n"+Evidence,"DS23",out items));
        Assert.Null(Assert.Single(items).Page);
    }

    [Theory]
    [InlineData("name","Medicamento inventado")]
    [InlineData("dose","20 mg")]
    [InlineData("route","intravenosa")]
    [InlineData("frequency","a cada 4 horas")]
    [InlineData("quantity","10")]
    [InlineData("date","16/10/2026")]
    [InlineData("evidence","Trecho que não está na fonte")]
    [InlineData("context","CONFIRMADO")]
    [InlineData("context","DESCRITO_COMO_REALIZADO")]
    [InlineData("context","NEGADO")]
    [InlineData("context","CANCELADO")]
    [InlineData("context","PLANEJADO")]
    [InlineData("page","8")]
    [InlineData("confidence","0.99")]
    public void Unsupported_or_invented_values_are_rejected(string field,string value)
    {
        var item=Item(); item[field]=value;
        using var json=Output(item);
        Assert.False(SectorClinicalEvidence.TryRead(json.RootElement,Evidence,"DS23",out _));
    }

    [Fact]
    public void Dose_is_not_reinterpreted_as_quantity_and_duplicate_json_members_are_rejected()
    {
        var item=Item();item["quantity"]="10";
        using var json=Output(item);
        Assert.False(SectorClinicalEvidence.TryRead(json.RootElement,Evidence,"DS23",out _));
        using var valid=Output();
        using var duplicated=JsonDocument.Parse(valid.RootElement.GetRawText().Replace("\"dose\":","\"dose\":\"invented\",\"dose\":"));
        Assert.False(SectorClinicalEvidence.TryRead(duplicated.RootElement,Evidence,"DS23",out _));
    }

    [Fact]
    public void Negative_performance_and_future_are_not_accepted_as_completed_execution()
    {
        foreach(var text in new[]{"Procedimento não realizado","Procedimento será realizado","Procedimento planejado, ainda não foi realizado"})
        {
            var item=Item(text,"DESCRITO_COMO_REALIZADO");item["category"]="procedimento";item["name"]="Procedimento";
            foreach(var key in new[]{"dose","unit","route","frequency","quantity","date"})item[key]="";
            using var json=Output(item);
            Assert.False(SectorClinicalEvidence.TryRead(json.RootElement,text,"DS08",out _));
        }
    }

    [Fact]
    public void Empty_items_are_valid_and_no_source_page_or_quantity_is_invented()
    {
        using var json=JsonDocument.Parse("{\"items\":[]}");
        Assert.True(SectorClinicalEvidence.TryRead(json.RootElement,Evidence,"DS23",out var items));
        Assert.Empty(items);
        var hash=SectorClinicalEvidence.Fingerprint(Guid.Empty,Guid.Empty,Guid.Empty,Guid.Empty,"DS23","OCR",Evidence);
        Assert.NotEqual(hash,SectorClinicalEvidence.Fingerprint(Guid.Empty,Guid.Empty,Guid.Empty,Guid.Empty,"DS23","TEXTO_NATIVO",Evidence));
    }
}
