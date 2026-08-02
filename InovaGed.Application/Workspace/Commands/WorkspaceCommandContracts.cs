namespace InovaGed.Application.Workspace.Commands;

public interface IWorkspaceCommandCatalog
{
    Task<IReadOnlyList<WorkspaceCommand>> GetAvailableAsync(
        WorkspaceCommandContext context,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceCommand(
    string Code,
    string Label,
    string Description,
    string Icon,
    string Group,
    WorkspaceCommandActionType ActionType,
    string? TargetUrl,
    string? ClientEvent,
    string? Shortcut,
    IReadOnlyList<string> Keywords,
    int Order);

public enum WorkspaceCommandActionType
{
    Navigate,
    OpenDrawer,
    OpenDialog,
    DispatchClientEvent
}

public sealed record WorkspaceCommandContext(
    Guid TenantId,
    Guid UserId,
    string? Controller,
    string? Action,
    string? Module,
    string? FolderId,
    IReadOnlyList<string> Roles);
