using InovaGed.Application.Workspace.Commands;
using Microsoft.AspNetCore.Routing;

namespace InovaGed.Web.Services;

public sealed class WorkspaceCommandCatalog(
    LinkGenerator links,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration) : IWorkspaceCommandCatalog
{
    public Task<IReadOnlyList<WorkspaceCommand>> GetAvailableAsync(
        WorkspaceCommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TenantId == Guid.Empty || context.UserId == Guid.Empty)
            return Task.FromResult<IReadOnlyList<WorkspaceCommand>>([]);

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return Task.FromResult<IReadOnlyList<WorkspaceCommand>>([]);

        var commands = new List<WorkspaceCommand>();
        AddNavigation(commands, "documents.open", "Abrir documentos", "Gestão documental", "documents", "navigation", "Ged", "Index", "G D", ["ged", "arquivos"], 10);
        AddNavigation(commands, "workqueue.open", "Abrir Minha Fila", "Itens que precisam de atenção", "inbox", "navigation", "Operations", "Index", "G F", ["tarefas", "pendências"], 20);

        if (IsModuleEnabled("Protocol") && HasAnyRole(context, "Administrador", "Admin", "Arquivista", "Protocolo"))
            AddNavigation(commands, "protocol.create", "Criar protocolo", "Registrar um novo atendimento", "send", "actions", "Protocolo", "Novo", null, ["atendimento", "processo"], 30);

        if (IsModuleEnabled("Loans"))
            AddNavigation(commands, "loans.open", "Abrir empréstimos", "Acompanhar solicitações e prazos", "calendar", "navigation", "Loans", "Index", null, ["retirada", "devolução"], 40);

        commands.Add(new WorkspaceCommand(
            "notifications.open", "Abrir notificações", "Atualizações do workspace", "bell", "actions",
            WorkspaceCommandActionType.OpenDrawer, null, "workspace:notifications", null, ["avisos", "alertas"], 50));

        if (!string.Equals(context.Controller, "Administration", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add(new WorkspaceCommand(
                "assistant.open", "Perguntar ao Assistente", "Usar o contexto atual", "assistant", "actions",
                WorkspaceCommandActionType.OpenDrawer, null, "workspace:assistant", null, ["ajuda", "inteligência"], 60));
        }

        if (string.Equals(context.Controller, "Ged", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(context.FolderId))
        {
            commands.Add(new WorkspaceCommand(
                "documents.upload", "Enviar arquivos", "Adicionar à pasta atual", "upload", "actions",
                WorkspaceCommandActionType.OpenDialog, null, "workspace:upload", "Ctrl+Shift+U", ["adicionar", "documentos"], 5));
        }

        return Task.FromResult<IReadOnlyList<WorkspaceCommand>>(commands.OrderBy(item => item.Order).ToArray());

        void AddNavigation(
            ICollection<WorkspaceCommand> target,
            string code,
            string label,
            string description,
            string icon,
            string group,
            string controller,
            string action,
            string? shortcut,
            IReadOnlyList<string> keywords,
            int order)
        {
            var targetUrl = links.GetPathByAction(httpContext, action, controller);
            if (targetUrl is not null)
                target.Add(new(code, label, description, icon, group, WorkspaceCommandActionType.Navigate, targetUrl, null, shortcut, keywords, order));
        }
    }

    private bool IsModuleEnabled(string module)
        => configuration.GetValue($"Modules:{module}:Enabled", true);

    private static bool HasAnyRole(WorkspaceCommandContext context, params string[] roles)
        => context.Roles.Any(current => roles.Contains(current, StringComparer.OrdinalIgnoreCase));
}
