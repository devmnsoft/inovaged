using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Users
{

    public class UserListVM
    {
        public string? Q { get; set; }
        public bool? Active { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        public int Total { get; set; }
        public List<Row> Items { get; set; } = new();

        public class Row
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
            public bool IsActive { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public List<string> Roles { get; set; } = new();
        }
    }
}