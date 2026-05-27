using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Search;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Search;

public sealed class GedSearchService : IGedSearchService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<GedSearchService> _logger;

    public GedSearchService(IDbConnectionFactory db, ILogger<GedSearchService> logger)
    { _db = db; _logger = logger; }

    public async Task<GedSearchResultDto> SearchAsync(GedSearchFilter filter, CancellationToken ct)
    {
        try
        {
            filter.Page = Math.Max(1, filter.Page);
            filter.PageSize = Math.Clamp(filter.PageSize <= 0 ? 20 : filter.PageSize, 1, 100);
            var offset = (filter.Page - 1) * filter.PageSize;
            var termLike = string.IsNullOrWhiteSpace(filter.Term) ? null : $"%{filter.Term.Trim()}%";
            const string baseWhere = @"where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and upper(coalesce(d.status::text,''))<>'DELETED'
and (@FolderId is null or d.folder_id=@FolderId)
and (@DocumentTypeId is null or d.document_type_id=@DocumentTypeId)
and (@DocumentStatus is null or upper(coalesce(d.status::text,''))=upper(@DocumentStatus))
and (@From is null or d.created_at>=@From)
and (@To is null or d.created_at<=@To)
and (@UploadedBy is null or d.created_by=@UploadedBy)
and (@OnlyWithOcr = false or coalesce(ds.ocr_text,'')<>'')
and (@OnlyOcrError = false or upper(coalesce(ds.ocr_status::text,''))='ERROR')
and (@Visibility is null or coalesce(d.visibility,'')=@Visibility)
and (@OnlyUnclassified = false or dc.classification_id is null)
and (@OnlyWithSuggestion = false or exists (select 1 from ged.document_classification_suggestion sug where sug.tenant_id=d.tenant_id and sug.document_id=d.id and sug.reg_status='A' and upper(sug.status)='PENDING'))
and (@TermLike is null or lower(coalesce(d.title,'')) ilike lower(@TermLike) or lower(coalesce(d.original_file_name,'')) ilike lower(@TermLike) or lower(coalesce(ds.ocr_text,'')) ilike lower(@TermLike))";

            var orderBy = "d.created_at desc";
            if (string.Equals(filter.Sort, "name", StringComparison.OrdinalIgnoreCase)) orderBy = "d.title asc";

            var countSql = "select count(1) from ged.document d left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A' " + baseWhere;
            var dataSql = $@"select d.id as DocumentId, ds.version_id as VersionId, d.title as Title, d.original_file_name as OriginalFileName, d.file_extension as FileExtension,
 f.name as FolderName, f.path as FolderPath, d.folder_id as FolderId, dt.name as DocumentType, cp.code as ClassificationCode, cp.name as ClassificationName,
 coalesce(ds.ocr_status::text,'PENDING') as OcrStatus, d.status::text as DocumentStatus, d.created_at as CreatedAt, u.name as CreatedByName,
 (coalesce(ds.ocr_text,'')<>'') as HasOcr, exists (select 1 from ged.document_classification_suggestion sug where sug.tenant_id=d.tenant_id and sug.document_id=d.id and sug.reg_status='A' and upper(sug.status)='PENDING') as HasSuggestion,
 coalesce(d.is_confidential,false) as IsConfidential, d.visibility as Visibility, left(coalesce(ds.ocr_text,''),200) as OcrSnippet,
 case when @TermLike is null then 0 else 1 end::numeric as Score,
 true as CanView, true as CanDownload, true as CanClassify, true as CanMove
 from ged.document d
 left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id
 left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
 left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.document_type_id
 left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
 left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id=dc.classification_id
 left join ged.app_user u on u.tenant_id=d.tenant_id and u.id=d.created_by
 {baseWhere}
 order by {orderBy} limit @PageSize offset @Offset";

            await using var conn = await _db.OpenAsync(ct);
            var p = new { filter.TenantId, filter.FolderId, filter.DocumentTypeId, filter.DocumentStatus, filter.From, filter.To, filter.UploadedBy, filter.OnlyWithOcr, filter.OnlyOcrError, filter.Visibility, filter.OnlyUnclassified, filter.OnlyWithSuggestion, TermLike = termLike, filter.PageSize, Offset = offset };
            var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, p, cancellationToken: ct));
            var items = (await conn.QueryAsync<GedSearchResultItemDto>(new CommandDefinition(dataSql, p, cancellationToken: ct))).AsList();
            return new GedSearchResultDto { Items = items, Total = total, Page = filter.Page, PageSize = filter.PageSize, TotalPages = (int)Math.Ceiling(total / (double)filter.PageSize) };
        }
        catch (Exception ex)
        { _logger.LogError(ex, "Erro no GedSearchService. Tenant={TenantId} User={UserId}", filter.TenantId, filter.UserId); throw; }
    }

    public Task<IReadOnlyList<GedSearchSuggestionDto>> SuggestAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<GedSearchSuggestionDto> items = Array.Empty<GedSearchSuggestionDto>();
            return Task.FromResult(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar sugestões de busca GED. Tenant={TenantId} User={UserId} Term={Term}", tenantId, userId, term);
            IReadOnlyList<GedSearchSuggestionDto> items = Array.Empty<GedSearchSuggestionDto>();
            return Task.FromResult(items);
        }
    }
}
