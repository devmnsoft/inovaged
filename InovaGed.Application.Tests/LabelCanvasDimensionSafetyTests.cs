using System.Globalization;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Application.Tests;

public sealed class LabelCanvasDimensionSafetyTests
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void Designer_dimensions_render_invariant_under_culture(string culture)
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
            Assert.Equal("100.5",100.5m.ToString(CultureInfo.InvariantCulture));
            Assert.Equal("70.5",70.5m.ToString(CultureInfo.InvariantCulture));
        }
        finally{CultureInfo.CurrentCulture=previous;}
    }

    [Theory]
    [InlineData(50,30)]
    [InlineData(70,40)]
    [InlineData(100,50)]
    [InlineData(100,70)]
    [InlineData(174,110)]
    public void Supported_starter_dimensions_are_within_shared_policy(decimal width,decimal height)
    {
        Assert.InRange(width,LabelCanvasDimensionPolicy.MinWidthMm,LabelCanvasDimensionPolicy.MaxWidthMm);
        Assert.InRange(height,LabelCanvasDimensionPolicy.MinHeightMm,LabelCanvasDimensionPolicy.MaxHeightMm);
    }

    [Fact]
    public void Javascript_contract_prevents_empty_dimension_coercion_and_concurrent_save()
    {
        var js=File.ReadAllText(Path.Combine(FindRoot(),"InovaGed.Web","wwwroot","js","labels-designer.js"));
        Assert.DoesNotContain("Number($('[data-meta=\"widthMm\"]').value)",js);
        Assert.Contains("valueAsNumber",js);Assert.Contains("if(activeSave)return activeSave",js);
        Assert.Contains("saveState==='validationError'",js);Assert.Contains("DIMENSION_MISMATCH",File.ReadAllText(Path.Combine(FindRoot(),"InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs")));
    }

    private static string FindRoot(){var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null&&!File.Exists(Path.Combine(current.FullName,"InovaGed.sln")))current=current.Parent;return current?.FullName??throw new DirectoryNotFoundException();}
}
