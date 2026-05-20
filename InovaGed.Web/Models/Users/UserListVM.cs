using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Users
{
    public class UserListVM
    {
        public string? Q { get; set; }
        public bool? Active { get; set; }
        public bool? Locked { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        public int Total { get; set; }
        public int TotalPages => (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize));
        public List<Row> Items { get; set; } = new();

        public class Row
        {
            public Guid Id { get; set; }
            public Guid? ServidorId { get; set; }
            public string Name { get; set; } = "";
            public string Username
            {
                get => Name;
                set => Name = value;
            }
            public string Email { get; set; } = "";
            public string? Cpf { get; set; }
            public string? Matricula { get; set; }
            public string? Cargo { get; set; }
            public string? Funcao { get; set; }
            public string? Setor { get; set; }
            public string? Lotacao { get; set; }
            public string? Unidade { get; set; }
            public bool IsActive { get; set; }
            public bool IsLocked { get; set; }
            public bool Locked
            {
                get => IsLocked;
                set => IsLocked = value;
            }
            public bool MustChangePassword { get; set; }
            public bool MfaEnabled { get; set; }
            public bool CertificateRequired { get; set; }
            public bool CanSignWithIcp { get; set; }
            public string SecurityLevel { get; set; } = "PUBLIC";
            public DateTimeOffset? LastLoginAt { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public List<string> Roles { get; set; } = new();
            public string Role => Roles.FirstOrDefault() ?? string.Empty;
        }
    }
}
