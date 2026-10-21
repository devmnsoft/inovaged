using System.Globalization;
using InovaGed.Application.Billing;
using InovaGed.Application.HospitalBilling;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace InovaGed.Web.ModelBinding;

/// <summary>HTML number fields submit decimal points independently of the server culture.</summary>
public sealed class BillingDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.ContainerType;
        var financial = type == typeof(BillingReviewInput) || type == typeof(BillingInstallmentInput)
            || type == typeof(BillingSettlementInput) || type == typeof(HospitalBillingReviewRequest)
            || type == typeof(BillingFilter) || type == typeof(HospitalBillingFilter);
        return financial && (context.Metadata.ModelType == typeof(decimal) || context.Metadata.ModelType == typeof(decimal?))
            ? new BillingDecimalModelBinder() : null;
    }
}

public sealed class BillingDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        var text = value.FirstValue?.Trim();
        if (string.IsNullOrEmpty(text) && context.ModelType == typeof(decimal?))
            context.Result = ModelBindingResult.Success(null);
        else if (decimal.TryParse(text?.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount)) context.Result = ModelBindingResult.Success(amount);
        else context.ModelState.TryAddModelError(context.ModelName, "Informe um valor sem separador de milhares.");
        return Task.CompletedTask;
    }
}
