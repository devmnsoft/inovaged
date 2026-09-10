namespace InovaGed.Application.Labels.Canvas;

public sealed record LabelCanvasSheetLayoutRequest(string PaperSize,string Orientation,decimal? PaperWidthMm,decimal? PaperHeightMm,
    decimal LabelWidthMm,decimal LabelHeightMm,decimal MarginTopMm,decimal MarginRightMm,decimal MarginBottomMm,decimal MarginLeftMm,
    decimal GapXMm,decimal GapYMm,decimal ScalePercent,int LabelCount);

public sealed record LabelCanvasSheetLayout(int Columns,int Rows,int LabelsPerPage,decimal EffectiveLabelWidth,
    decimal EffectiveLabelHeight,int PageCount,decimal PaperWidthMm,decimal PaperHeightMm,string? ErrorCode)
{
    public bool IsValid => ErrorCode is null;
}

public static class LabelCanvasSheetLayoutCalculator
{
    public const string DoesNotFitError = "LABEL_DOES_NOT_FIT_PAPER";

    public static LabelCanvasSheetLayout Calculate(LabelCanvasSheetLayoutRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (width,height)=ResolvePaper(input);
        if(input.Orientation.Equals("landscape",StringComparison.OrdinalIgnoreCase))(width,height)=(height,width);
        var scale=Math.Clamp(input.ScalePercent,1,500)/100m;
        var effectiveWidth=input.LabelWidthMm*scale;
        var effectiveHeight=input.LabelHeightMm*scale;
        var availableWidth=width-Math.Max(0,input.MarginLeftMm)-Math.Max(0,input.MarginRightMm);
        var availableHeight=height-Math.Max(0,input.MarginTopMm)-Math.Max(0,input.MarginBottomMm);
        var columns=Fit(availableWidth,effectiveWidth,Math.Max(0,input.GapXMm));
        var rows=Fit(availableHeight,effectiveHeight,Math.Max(0,input.GapYMm));
        var perPage=columns*rows;
        return new(columns,rows,perPage,effectiveWidth,effectiveHeight,perPage==0?0:(int)Math.Ceiling(Math.Max(0,input.LabelCount)/(decimal)perPage),width,height,perPage==0?DoesNotFitError:null);
    }

    private static int Fit(decimal available,decimal item,decimal gap) => item<=0||available<item?0:1+(int)Math.Floor((available-item)/(item+gap));
    private static (decimal Width,decimal Height) ResolvePaper(LabelCanvasSheetLayoutRequest input) => input.PaperSize.Trim().ToUpperInvariant() switch
    {
        "A4" => (210m,297m), "A5" => (148m,210m), "LETTER" => (215.9m,279.4m),
        "CUSTOM" when input.PaperWidthMm>0&&input.PaperHeightMm>0 => (input.PaperWidthMm.Value,input.PaperHeightMm.Value),
        "CUSTOM" => throw new ArgumentException("CUSTOM exige dimensões físicas suficientes."),
        _ => throw new ArgumentException("Tamanho de papel não suportado.")
    };
}
