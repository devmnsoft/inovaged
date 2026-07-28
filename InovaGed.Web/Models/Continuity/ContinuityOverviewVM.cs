using InovaGed.Application.Continuity;
using InovaGed.Web.Services;
namespace InovaGed.Web.Models.Continuity;
public sealed record ContinuityOverviewVM(UiModuleAvailability Availability, ContinuityDashboardDto? Dashboard);
