using InovaGed.Web.Controllers;

namespace InovaGed.Application.Tests;

public sealed class LabelsControllerClassificationSchemaTests
{
    [Fact]
    public void Builders_UseTitleWhenPresent()
    {
        var schema = new LabelsController.ClassificationPlanSchemaInfo(true, true, true, true, true);

        Assert.Contains("cp.title", LabelsController.BuildClassificationTitleExpression(schema));
        Assert.Equal("nullif(max(cp.code), '')", LabelsController.BuildClassificationCodeExpression(schema));
        Assert.Equal("coalesce(max(cp.final_destination), '')", LabelsController.BuildFinalDestinationExpression(schema));
        Assert.Equal("coalesce(dc.classification_id, d.classification_id)", LabelsController.BuildDocumentClassificationJoinExpression(schema));
    }

    [Fact]
    public void TitleBuilder_FallsBackToDescriptionWithoutReferencingMissingTitle()
    {
        var expression = LabelsController.BuildClassificationTitleExpression(new(true, false, true, false, true));

        Assert.DoesNotContain("cp.title", expression);
        Assert.Contains("cp.description", expression);
    }

    [Fact]
    public void TitleBuilder_FallsBackToCodeWithoutReferencingMissingColumns()
    {
        var expression = LabelsController.BuildClassificationTitleExpression(new(true, false, false, false, true));

        Assert.Equal("coalesce(nullif(max(cp.code), ''))", expression);
    }

    [Fact]
    public void Builders_ReturnSqlNullsWhenClassificationColumnsAreAbsent()
    {
        var schema = new LabelsController.ClassificationPlanSchemaInfo(false, false, false, false, true);

        Assert.Equal("null", LabelsController.BuildClassificationCodeExpression(schema));
        Assert.Equal("null", LabelsController.BuildClassificationTitleExpression(schema));
        Assert.Equal("''", LabelsController.BuildFinalDestinationExpression(schema));
    }

    [Fact]
    public void JoinBuilder_DoesNotReferenceMissingDocumentClassificationId()
    {
        var expression = LabelsController.BuildDocumentClassificationJoinExpression(new(true, true, true, true, false));

        Assert.Equal("dc.classification_id", expression);
        Assert.DoesNotContain("d.classification_id", expression);
    }
}
