using System.Security.Claims;
using InovaGed.Application.Workspace.Commands;
using InovaGed.Web.Models.Workspace.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Workspace")]
public sealed class WorkspaceController(IWorkspaceCommandCatalog commandCatalog) : Controller
{
    private static readonly IReadOnlyDictionary<string, string> GroupLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["actions"] = "Ações",
            ["navigation"] = "Navegação",
            ["favorites"] = "Favoritos",
            ["recents"] = "Recentes",
            ["documents"] = "Documentos",
            ["folders"] = "Pastas",
            ["protocols"] = "Protocolos",
            ["loans"] = "Empréstimos",
            ["saved-searches"] = "Buscas salvas"
        };

    [HttpGet("Commands")]
    [ProducesResponseType<WorkspaceCommandResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceCommandResponse>> Commands(
        [FromQuery] string? module,
        [FromQuery] string? controller,
        [FromQuery] string? action,
        [FromQuery] string? folderId,
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenantId)
            || !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
            return Forbid();

        var roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var context = new WorkspaceCommandContext(tenantId, userId, controller, action, module, folderId, roles);
        var available = await commandCatalog.GetAvailableAsync(context, cancellationToken);
        var normalizedQuery = query?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            available = available.Where(item => Matches(item, normalizedQuery)).ToArray();
        }

        var groups = available
            .GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkspaceCommandGroupResponse(
                group.Key,
                GroupLabels.GetValueOrDefault(group.Key, group.Key),
                group.Select(item => new WorkspaceCommandItemResponse(
                    item.Code,
                    item.Label,
                    item.Description,
                    item.Icon,
                    item.ActionType.ToString(),
                    item.TargetUrl,
                    item.ClientEvent,
                    item.Shortcut,
                    item.Keywords)).ToArray()))
            .ToArray();

        return Ok(new WorkspaceCommandResponse(groups));
    }

    private static bool Matches(WorkspaceCommand command, string query)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return command.Label.Contains(query, comparison)
            || command.Description.Contains(query, comparison)
            || command.Keywords.Any(keyword => keyword.Contains(query, comparison));
    }
}
