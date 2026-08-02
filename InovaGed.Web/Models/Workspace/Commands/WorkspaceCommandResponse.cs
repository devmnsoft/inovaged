namespace InovaGed.Web.Models.Workspace.Commands;

public sealed record WorkspaceCommandResponse(IReadOnlyList<WorkspaceCommandGroupResponse> Groups);

public sealed record WorkspaceCommandGroupResponse(
    string Code,
    string Label,
    IReadOnlyList<WorkspaceCommandItemResponse> Items);

public sealed record WorkspaceCommandItemResponse(
    string Code,
    string Label,
    string Description,
    string Icon,
    string ActionType,
    string? TargetUrl,
    string? ClientEvent,
    string? Shortcut,
    IReadOnlyList<string> Keywords);
