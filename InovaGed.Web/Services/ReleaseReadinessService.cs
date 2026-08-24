using InovaGed.Web.Models.ReleaseReadiness;

namespace InovaGed.Web.Services;

public interface IReleaseReadinessService
{
    IReadOnlyList<ModuleReadinessItem> GetModules(bool migrationsPending);
}

public sealed class ReleaseReadinessService(IModuleAvailabilityService availability) : IReleaseReadinessService
{
    public IReadOnlyList<ModuleReadinessItem> GetModules(bool migrationsPending)
    {
        ModuleReadinessItem Ready(string code, string name, string description, string route, string permission, string db = "Schema principal atualizado", string di = "Serviços registrados")
            => new(code, name, description, ModuleReadinessStatus.Ready, route, permission, db, di, null, "Manter smoke tests e monitoramento ativos.");
        ModuleReadinessItem Configured(string code, string name, string description, string route, string permission)
        {
            var flag = availability.Get(code);
            return flag.Enabled
                ? Ready(code, name, description, route, permission)
                : new(code, name, description, ModuleReadinessStatus.InImplementation, route, permission, "Não aplicável até habilitação", "Configuração Modules", null, flag.Reason ?? "Módulo em fechamento técnico.");
        }

        var modules = new List<ModuleReadinessItem>
        {
            Ready("Administration", "Administração", "Usuários, perfis e parâmetros operacionais.", "/Administration", "admin.full"),
            migrationsPending
                ? new("Database", "Banco de Dados / Migrations", "Plano seguro de evolução do schema.", ModuleReadinessStatus.NeedsMigration, "/DatabaseReadiness", "database.readiness.view", "Migrations obrigatórias pendentes", "IDatabaseMigrationRunner", null, "Revisar e aplicar em /DatabaseReadiness.")
                : Ready("Database", "Banco de Dados / Migrations", "Plano seguro de evolução do schema.", "/DatabaseReadiness", "database.readiness.view"),
            Ready("Incidents", "Central de Incidentes", "Diagnóstico e tratamento rastreável de falhas.", "/SystemIncidents", "system.incidents.view"),
            Ready("SchemaHealth", "SchemaHealth", "Compatibilidade do banco e orientações de reparo.", "/SchemaHealth", "database.readiness.view"),
            Ready("Labels", "Etiquetas", "Templates, impressão e histórico de etiquetas.", "/Labels", "labels.view"),
            Ready("LocDesk", "LocDesk", "Impressão assistida por estação.", "/Labels/LocDesk", "labels.print"),
            Ready("PrintQueue", "Fila de Impressão", "Acompanhamento dos trabalhos de impressão.", "/Labels/PrintQueue", "labels.print"),
            Ready("QrTracking", "Rastreio QR", "Rastreabilidade física por QR Code.", "/Labels/Tracking", "labels.view"),
            Ready("PhysicalArchive", "Acervo Físico", "Localizações, caixas e movimentações.", "/Physical/Boxes", "physical.archive.view"),
            Ready("Retention", "Retenção", "Destinação e temporalidade documental.", "/RetentionDestination", "retention.view"),
            Ready("Instruments", "Instrumentos PCD/TTD/POP", "Versionamento dos instrumentos arquivísticos.", "/Instruments/Versions/PCD", "instruments.view"),
            Ready("Loans", "Empréstimos", "Custódia, empréstimos e devoluções.", "/Loans", "loans.view"),
            Ready("Ocr", "OCR", "Extração e agendamento de reconhecimento.", "/Ocr", "ocr.view"),
            Ready("Search", "Busca", "Pesquisa documental operacional.", "/GedSearch", "physical.archive.view"),
            Ready("SmartSearch", "SmartSearch", "Busca contextual e refinada.", "/SmartSearch", "smartsearch.view"),
            Ready("Protocols", "Protocolos", "Atendimento e fluxo de solicitações.", "/Protocolo", "admin.full"),
            Ready("DocumentQuality", "Qualidade Documental", "Indicadores e validações documentais.", "/DocumentQuality", "admin.full"),
            Ready("HospitalBilling", "Faturamento Hospitalar", "Fluxos documentais hospitalares.", "/HospitalBilling", "admin.full"),
            Configured("ArchiveProductivity", "Produtividade/UST", "Medição operacional do arquivo.", "/ArchiveProductivity", "admin.full"),
            Configured("ContractFiscalization", "Fiscalização Contratual", "Gestão e fiscalização de contratos.", "/ContractFiscalization", "admin.full"),
            Configured("FiscalPortal", "Portal do Fiscal", "Interação controlada com fiscais externos.", "/FiscalPortal", "admin.full")
        };
        return modules;
    }
}
