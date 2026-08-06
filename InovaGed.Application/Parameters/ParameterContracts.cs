using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Parameters;

public sealed class ParameterCategoryRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsSystem { get; set; }
    public bool AllowHierarchy { get; set; }
    public bool IsActive { get; set; }
    public int TotalItems { get; set; }
}

public sealed class ParameterItemRow
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? ParentId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Abbreviation { get; set; }
    public string? ExternalCode { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string? ParentName { get; set; }
    public string? MetadataJson { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class ParameterSelectOption
{
    public string CategoryCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Abbreviation { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ParameterIndexVM
{
    public IReadOnlyList<ParameterCategoryRow> Categories { get; set; } = Array.Empty<ParameterCategoryRow>();
    public IReadOnlyList<ParameterItemRow> Items { get; set; } = Array.Empty<ParameterItemRow>();
    public string? CategoryCode { get; set; }
    public string? Search { get; set; }
    public string Status { get; set; } = "all";
    public int Total => Items.Count;
    public int Active => Items.Count(x => x.IsActive);
    public int Inactive => Items.Count(x => !x.IsActive);
    public int RecentlyChanged => Items.Count(x => (x.UpdatedAt ?? x.CreatedAt) >= DateTimeOffset.UtcNow.AddDays(-30));
    public Guid? SelectedCategoryId => Categories.FirstOrDefault(x => x.Code == CategoryCode)?.Id;
    public string SelectedCategoryName => Categories.FirstOrDefault(x => x.Code == CategoryCode)?.Name ?? "Todos os parâmetros";
}

public sealed class ParameterItemEditVM
{
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "Selecione uma categoria.")]
    public Guid CategoryId { get; set; }
    public Guid? ParentId { get; set; }
    public string? Code { get; set; }
    [Required(ErrorMessage = "Informe o nome do parâmetro.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 200 caracteres.")]
    public string Name { get; set; } = string.Empty;
    [StringLength(500, ErrorMessage = "A descrição deve ter no máximo 500 caracteres.")]
    public string? Description { get; set; }
    [StringLength(30)]
    public string? Abbreviation { get; set; }
    [StringLength(120)]
    public string? ExternalCode { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string? MetadataJson { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public interface IParameterRepository
{
    Task<IReadOnlyList<ParameterCategoryRow>> ListCategoriesAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<ParameterItemRow>> ListItemsAsync(Guid tenantId, string? categoryCode, string? search, CancellationToken ct);
    Task<IReadOnlyList<ParameterSelectOption>> ListOptionsAsync(Guid tenantId, IEnumerable<string> categoryCodes, CancellationToken ct);
    Task<ParameterItemEditVM?> GetItemAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<ParameterItemRow>> ListParentOptionsAsync(Guid tenantId, Guid categoryId, Guid? ignoreId, CancellationToken ct);
    Task<Guid> UpsertItemAsync(Guid tenantId, Guid userId, ParameterItemEditVM vm, CancellationToken ct);
    Task SetActiveAsync(Guid tenantId, Guid userId, Guid id, bool active, CancellationToken ct);
    Task DeleteAsync(Guid tenantId, Guid userId, Guid id, string? reason, CancellationToken ct);
}
