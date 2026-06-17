namespace InovaGed.Web.Security;

public static class AppPolicies
{
    public const string Dashboard = "Dashboard";
    public const string Documentos = "Documentos";
    public const string Emprestimos = "Emprestimos";
    public const string Relatorios = "Relatorios";
    public const string Auditoria = "Auditoria";
    public const string Administracao = "Administracao";
    public const string HospitalDocumentsOrLoansAccess = "HospitalDocumentsOrLoansAccess";
    public const string Operations = "Operations";
    public const string AdminOnly = "AdminOnly";

    public const string FullAdminOnly = "FullAdminOnly";
    public const string GedAccess = "GedAccess";
    public const string Ocr = "OCR";
    public const string HospitalDocumentsAccess = "HospitalDocumentsAccess";
    public const string LoansView = "LoansView";
    public const string LoansManage = "LoansManage";
    public const string LoansRequest = "LoansRequest";
    public const string ProtocolRequest = "ProtocolRequest";
    public const string ProtocolView = "ProtocolView";
    public const string ProtocolManage = "ProtocolManage";
    public const string ProtocolAdmin = "ProtocolAdmin";
    public const string SystemAdmin = "SystemAdmin";
    public const string SystemHealth = "SystemHealth";
    public const string ParametersAdmin = "ParametersAdmin";
    public const string UsersAdmin = "UsersAdmin";
    public const string UsersGlobalManage = "UsersGlobalManage";
    public const string UsersSectorManage = "UsersSectorManage";
    public const string SystemLogs = "SystemLogs";
    public const string LogsAccess = SystemLogs;
    public const string SchemaRepair = "SchemaRepair";
    public const string OperationsAccess = "OperationsAccess";
}
