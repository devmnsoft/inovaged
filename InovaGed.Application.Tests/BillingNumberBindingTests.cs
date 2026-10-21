using System.Globalization;
using InovaGed.Web.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace InovaGed.Application.Tests;

public sealed class BillingNumberBindingTests
{
    [Theory]
    [InlineData("pt-BR", "1234.56", 1234.56)]
    [InlineData("pt-BR", "1234,56", 1234.56)]
    [InlineData("en-US", "0.01", 0.01)]
    public async Task Html_numbers_do_not_change_with_the_server_culture(string culture,string value,double expected)
    {
        var context=Context(value,CultureInfo.GetCultureInfo(culture));
        await new BillingDecimalModelBinder().BindModelAsync(context);
        Assert.True(context.Result.IsModelSet);
        Assert.Equal((decimal)expected,context.Result.Model);
    }

    [Fact]
    public async Task Ambiguous_thousands_separator_is_rejected()
    {
        var context=Context("1.234,56",CultureInfo.GetCultureInfo("pt-BR"));
        await new BillingDecimalModelBinder().BindModelAsync(context);
        Assert.False(context.Result.IsModelSet);
        Assert.False(context.ModelState.IsValid);
    }

    private static ModelBindingContext Context(string value,CultureInfo culture)
    {
        var action=new ActionContext(new DefaultHttpContext(),new RouteData(),new ActionDescriptor());
        var provider=new QueryStringValueProvider(BindingSource.Query,new QueryCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>{{"Amount",value}}),culture);
        return DefaultModelBindingContext.CreateBindingContext(action,provider,new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal)),new BindingInfo(),"Amount");
    }
}
