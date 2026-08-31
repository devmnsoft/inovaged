using InovaGed.Application.WorkspaceSearch;

namespace InovaGed.Web.Models.Search;

public sealed record GlobalSearchPageVm(string Query, WorkspaceSearchResponse Response);
