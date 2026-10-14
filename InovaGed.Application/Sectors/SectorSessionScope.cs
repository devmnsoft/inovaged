namespace InovaGed.Application.Sectors;

public static class SectorSessionScope
{
    public const int PageSize = 20;
    public const int MaxPageIndex = 50;

    public const string EmptyCatalogMeansAllTypes =
        "Lista vazia de tipos na integração significa todos os tipos ativos. Lista preenchida restringe a esses códigos e é relida a cada requisição.";

    public const string LinkFilter = """
  and l.tenant_id=@tenantId and l.patient_id=@patientId and l.reg_status='A'
  and ((not @expand and l.encounter_id=@encounterId) or (@expand and exists (
        select 1 from ged.sector_encounter scope_e
        where scope_e.id=l.encounter_id and scope_e.tenant_id=l.tenant_id and scope_e.patient_id=@patientId)))
  and (@catalog is null or l.catalog_code=@catalog)
  and (@documentId is null or l.document_id=@documentId)
  and (@versionId is null or l.version_id=@versionId)
  and (cardinality(@allowedCatalog)=0 or l.catalog_code = any(@allowedCatalog))
  and exists (
        select 1 from ged.document scope_d
        where scope_d.tenant_id=l.tenant_id and scope_d.id=l.document_id and coalesce(scope_d.reg_status,'A')='A')
""";

    public static int NormalizePage(int page) => Math.Clamp(page, 0, MaxPageIndex);

    public static object Args(SectorSessionAccess session, bool expand = false, Guid? versionId = null, int? take = null, int? skip = null)
        => new
        {
            tenantId = session.TenantId,
            patientId = session.PatientId,
            encounterId = session.EncounterId,
            expand,
            catalog = session.CatalogCode,
            documentId = session.DocumentId,
            versionId = versionId ?? (session.Mode == "documento" ? session.VersionId : null),
            allowedCatalog = session.AllowedCatalogCodes ?? [],
            take,
            skip
        };
}
