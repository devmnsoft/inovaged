using Xunit;

namespace InovaGed.UiTests;

public sealed class ParametersAndHospitalDocumentsContractTests
{
    private static string RepoFile(string relative) => RecoverySource.Read(relative);

    [Fact]
    public void Parameters_management_has_secure_crud_and_premium_assets()
    {
        var view = RepoFile("InovaGed.Web/Views/Parameters/Index.cshtml");
        var controller = RepoFile("InovaGed.Web/Controller/ParametersController.cs");
        var script = RepoFile("InovaGed.Web/wwwroot/js/parameters.js");

        Assert.Contains("Parâmetros do Sistema", view);
        Assert.Contains("parameters-metrics", view);
        Assert.Contains("AntiForgeryToken", view);
        Assert.Contains("js-parameter-delete", view);
        Assert.Contains("Details/{id:guid}", controller);
        Assert.Contains("Duplicate/{id:guid}", controller);
        Assert.DoesNotContain("confirm(", view + script);
        Assert.DoesNotContain("alert(", view + script);
        Assert.DoesNotContain("prompt(", view + script);
        Assert.DoesNotContain("bi-", view + script);
    }

    [Fact]
    public void Hospital_more_actions_and_local_assistant_are_functional()
    {
        var view = RepoFile("InovaGed.Web/Views/HospitalDocuments/Index.cshtml");
        var script = RepoFile("InovaGed.Web/wwwroot/js/hospital-document-assistant.js");

        Assert.Contains("data-bs-toggle=\"dropdown\"", view);
        Assert.Contains("btnActionsRefresh", view + script);
        Assert.Contains("btnActionsClear", view + script);
        Assert.Contains("documentAssistantCanvas", view);
        Assert.Contains("disabled title=\"Em breve\"", view);
        Assert.Contains("advancedOcrStatus", script);
        Assert.Contains("dateFrom", script);
        Assert.DoesNotContain("alert(", script);
        Assert.DoesNotContain("confirm(", script);
        Assert.DoesNotContain("prompt(", script);
        Assert.DoesNotContain("bi-", script);
    }
}
