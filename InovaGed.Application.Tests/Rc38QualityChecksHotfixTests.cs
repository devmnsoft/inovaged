using InovaGed.Environment.Doctor.Checks;

namespace InovaGed.Application.Tests;

public sealed class Rc38QualityChecksHotfixTests
{
    [Fact]
    public void rc38_quality_checks_use_concrete_dictionary()
    {
        var source=File.ReadAllText(Path.Combine(Root(),"InovaGed.Environment.Doctor","Checks","Rc38QualityChecks.cs"));
        Assert.Contains("new Dictionary<string, bool>",source);
        Assert.DoesNotContain("new(){",source);
    }

    [Theory]
    [InlineData("reuse")]
    [InlineData("print")]
    [InlineData("designer")]
    public void rc38_doctor_executes(string doctor)
    {
        using var output=new StringWriter();using var error=new StringWriter();
        var result=doctor switch
        {
            "reuse"=>Rc38QualityChecks.ReusePortability(Root(),output,error),
            "print"=>Rc38QualityChecks.PrintOperations(Root(),output,error),
            _=>Rc38QualityChecks.DesignerUx(Root(),output,error)
        };
        Assert.Equal(0,result);
        Assert.Contains("[OK]",output.ToString());
    }

    private static string Root()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"InovaGed.sln")))return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
