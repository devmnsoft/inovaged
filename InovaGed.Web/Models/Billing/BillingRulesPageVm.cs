using InovaGed.Application.Billing;

namespace InovaGed.Web.Models.Billing;

public sealed record BillingRulesPageVm(IReadOnlyList<BillingExtractionRuleDto> Rules, BillingExtractionRuleInput Form);
