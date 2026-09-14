using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Application.Tests;

public sealed class LabelDesignerControllerSafetyRc35Tests
{
    [Fact]
    public void label_designer_actions_have_single_antiforgery_attribute()
    {
        var duplicated = typeof(LabelDesignerController)
            .GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).Length > 1)
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void compare_conflict_has_two_route_aliases()
    {
        var routes = CompareConflictMethod()
            .GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();

        Assert.Equal(2, routes.Length);
        Assert.Contains("/Labels/Designer/{templateKey}/CompareConflict", routes);
        Assert.Contains("/Labels/Designer/{templateKey}/CompareLocal", routes);
    }

    [Fact]
    public void compare_conflict_has_single_validate_antiforgery()
    {
        var attributes = CompareConflictMethod()
            .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true);

        Assert.Single(attributes);
    }

    private static System.Reflection.MethodInfo CompareConflictMethod() =>
        typeof(LabelDesignerController).GetMethod(nameof(LabelDesignerController.CompareConflict))
        ?? throw new InvalidOperationException("Action CompareConflict não encontrada.");
}
