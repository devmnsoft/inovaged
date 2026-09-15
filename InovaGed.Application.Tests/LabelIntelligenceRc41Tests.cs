using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Labels.Intelligence;
using InovaGed.Infrastructure.Labels;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class LabelIntelligenceRc41Tests
{
    [Fact] public void recommendation_prioritizes_subject_type_and_explains_score()
    {
        var service=new LabelTemplateRecommendationService();
        var result=service.Recommend(new(Guid.NewGuid(),"Box","A4",[
            new("DOCUMENT","Document","A4",true,true,true),
            new("BOX","Box","A4",true,true,true,true,true)]));
        Assert.Equal("BOX",result[0].TemplateKey); Assert.InRange(result[0].Score,0,100); Assert.NotEmpty(result[0].Reasons); Assert.True(result[0].IsCompatible);
        Assert.False(result.Single(x=>x.TemplateKey=="DOCUMENT").IsCompatible);
    }

    [Fact] public void recommendation_requires_tenant_scope() => Assert.Throws<ArgumentException>(() =>
        new LabelTemplateRecommendationService().Recommend(new(Guid.Empty,"Box","A4",[])));
 

    [Fact] public void print_profile_recommendation_orders_by_named_score()
    {
        var service = new LabelPrintProfileRecommendationService();
        var compatible = Guid.NewGuid();
        var exact = Guid.NewGuid();
        var profiles = new[]
        {
            new LabelPrintProfileCandidate(compatible, "A4 genérico", null, "A4", 100, 70, 100, false, false),
            new LabelPrintProfileCandidate(exact, "Zebra A4", "Zebra", "A4", 100, 70, 100, false, false)
        };

        Assert.Equal(exact, service.Recommend(Guid.NewGuid(), "A4", 100, 70, "Zebra", profiles)!.ProfileId);
    }

    

    [Fact] public void print_profile_recommendation_default_is_fallback()
    {
        var fallback = Guid.NewGuid();
        var profiles = new[] { new LabelPrintProfileCandidate(fallback, "Padrão", null, "LETTER", 50, 30, 100, true, false) };
        Assert.Equal(fallback, new LabelPrintProfileRecommendationService().Recommend(Guid.NewGuid(), "A5", 40, 20, null, profiles)!.ProfileId);
    }

    [Theory] [InlineData("EQUALS","Urgente","urgente",true)] [InlineData("CONTAINS","Administrativo central","central",true)] [InlineData("IS_EMPTY","","",true)]
    public void conditions_are_whitelisted(string op,string actual,string expected,bool result) => Assert.Equal(result,LabelConditionPolicy.Evaluate(op,actual,expected));
    [Fact] public void condition_rejects_unknown_operator()=>Assert.Throws<ArgumentException>(()=>LabelConditionPolicy.Evaluate("SCRIPT",null,null));
    [Fact] public void conditional_style_rejects_unsafe_property()=>Assert.DoesNotContain("css",LabelConditionPolicy.ConditionalStyleProperties);

    [Fact] public void auto_align_is_undoable_and_does_not_modify_locked_elements()
    {
        var source=new LabelCanvasDocumentDto{Canvas=new(){WidthMm=100,HeightMm=70,SafeMarginMm=3},Elements=[
            new(){Id="a",XMm=20,YMm=2,WidthMm=20,HeightMm=8},new(){Id="locked",XMm=50,YMm=60,WidthMm=20,HeightMm=8,Locked=true}]};
        var output=LabelAutoLayoutAssistant.Preview(source,LabelAutoLayoutAction.FitSafeMargin);
        Assert.Equal(3m,output.Elements[0].YMm); Assert.Equal(60m,output.Elements[1].YMm); Assert.Equal(2m,source.Elements[0].YMm);
    }
    [Fact] public void auto_fit_never_goes_below_min_font()=>Assert.Equal(6m,LabelAutoLayoutAssistant.SafeFontSize(8,.1m));
}
