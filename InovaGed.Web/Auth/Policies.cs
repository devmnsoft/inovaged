using InovaGed.Application.Auth;

namespace InovaGed.Web.Auth;

public static class Policies
{
    public const string CanManageRetention = "perm:Retention.Manage";     // criar casos, decidir, executar caso
    public const string CanSignRetention = "perm:Retention.Sign";         // assinar termo
    public const string CanExecuteFinal = "perm:Retention.ExecuteFinal";  // DISPOSED
    public const string CanViewRetention = "perm:Retention.View";         // auditoria/relatórios
}