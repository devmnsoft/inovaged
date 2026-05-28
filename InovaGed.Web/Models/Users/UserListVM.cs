namespace InovaGed.Web.Models.Users;

public sealed class UserListVM
{
    public string? Q { get; set; }
    public bool? Active { get; set; }
    public bool? Locked { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    public int Total { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)Total / PageSize);

    public List<Row> Items { get; set; } = new();

    public sealed class Row
    {
        public Guid Id { get; set; }
        public Guid? ServidorId { get; set; }
        public Guid? UserId { get; set; }
        public string EditIdSource { get; set; } = "";
        public Guid? EffectiveEditId =>
            ServidorId.HasValue && ServidorId.Value != Guid.Empty
                ? ServidorId
                : UserId.HasValue && UserId.Value != Guid.Empty
                    ? UserId
                    : null;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Cpf { get; set; }
        public string? Matricula { get; set; }
        public string? Cargo { get; set; }
        public string? Funcao { get; set; }
        public string? Setor { get; set; }
        public string? Lotacao { get; set; }
        public List<string> Roles { get; set; } = new();
        public string SecurityLevel { get; set; } = "PUBLIC";
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public bool MustChangePassword { get; set; }
        public bool MfaEnabled { get; set; }
        public bool CertificateRequired { get; set; }
        public bool CanSignWithIcp { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
    }
}
