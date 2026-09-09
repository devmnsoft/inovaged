using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;

namespace InovaGed.Application.Tests;

public sealed class LabelCanvasFidelityRc26Tests
{
    [Fact]
    public void A4_100x70_calculates_valid_grid()
    {
        var result=LabelCanvasSheetLayoutCalculator.Calculate(new("A4","portrait",null,null,100,70,10,0,0,10,4,4,100,6));
        Assert.True(result.IsValid);Assert.Equal(2,result.Columns);Assert.Equal(3,result.Rows);Assert.Equal(6,result.LabelsPerPage);
    }

    [Fact]
    public void Landscape_uses_landscape_dimensions()
    {
        var result=LabelCanvasSheetLayoutCalculator.Calculate(new("A5","landscape",null,null,100,70,0,0,0,0,0,0,100,1));
        Assert.Equal(210,result.PaperWidthMm);Assert.Equal(148,result.PaperHeightMm);
    }

    [Fact]
    public void Label_larger_than_paper_returns_error()
    {
        var result=LabelCanvasSheetLayoutCalculator.Calculate(new("A4","portrait",null,null,300,300,0,0,0,0,0,0,100,1));
        Assert.False(result.IsValid);Assert.Equal(LabelCanvasSheetLayoutCalculator.DoesNotFitError,result.ErrorCode);
    }

    [Fact]
    public void Version_hash_changes_when_branding_changes()
    {
        var a=LabelCanvasVersionSnapshotReader.ComputeVersionHash("{\"templateKey\":\"A\",\"defaultBrandingProfileId\":null,\"designJson\":{}}");
        var b=LabelCanvasVersionSnapshotReader.ComputeVersionHash("{\"templateKey\":\"A\",\"defaultBrandingProfileId\":\"295c40cc-fab1-4c9b-9633-ef99ed24d7ed\",\"designJson\":{}}");
        Assert.NotEqual(a,b);
    }

    [Fact]
    public void Oversized_inline_image_is_rejected()
    {
        var data=Convert.ToBase64String(new byte[LabelCanvasSafeImageSource.MaxInlineImageBytes+1]);
        Assert.False(LabelCanvasSafeImageSource.IsSafe("data:image/png;base64,"+data));
    }
}
