using InovaGed.Environment.Doctor.Checks;

namespace InovaGed.Application.Tests;

public sealed class LabelRc411HotfixContractTests
{
    [Fact]
    public void print_wizard_validation_preserves_signature()
    {
        var source = Read("InovaGed.Web", "Controller", "LabelsController.cs");
        Assert.Contains("reprintReason: input.ReprintReason", source);
        Assert.Contains("ct: ct", source);
    }

    [Fact]
    public void subject_required_rejects_null_subject_id()
    {
        var source = Read("InovaGed.Web", "Controller", "LabelsController.cs");
        Assert.Contains("RequiresSubjectId(input.SubjectType)", source);
        Assert.Contains("!input.SubjectId.HasValue || input.SubjectId.Value == Guid.Empty", source);
        Assert.Contains("Selecione o registro que será etiquetado.", source);
    }

    [Fact]
    public void manual_label_allows_null_subject_id()
    {
        var source = Read("InovaGed.Web", "Controller", "LabelsController.cs");
        Assert.Contains("!string.Equals(subjectType, LabelSubjectType.Manual", source);
        Assert.DoesNotContain("input.SubjectId ?? Guid.Empty", source);
    }

    [Fact]
    public void rc41_doctors_compile()
    {
        var source = Read("InovaGed.Environment.Doctor", "Checks", "Rc41QualityChecks.cs");
        Assert.Contains("new Dictionary<string, bool>", source);
        Assert.DoesNotContain("error,new()", source);
    }

    [Theory]
    [InlineData("preflight")]
    [InlineData("recommendation")]
    [InlineData("designer")]
    [InlineData("publish")]
    public void rc41_doctors_execute(string doctor)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = doctor switch
        {
            "preflight" => Rc41QualityChecks.SmartPreflight(Root(), output, error),
            "recommendation" => Rc41QualityChecks.Recommendation(Root(), output, error),
            "designer" => Rc41QualityChecks.DesignerAssist(Root(), output, error),
            _ => Rc41QualityChecks.PublishQuality(Root(), output, error)
        };
        Assert.Equal(0, result);
        Assert.Contains("[OK]", output.ToString());
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(path).ToArray()));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
