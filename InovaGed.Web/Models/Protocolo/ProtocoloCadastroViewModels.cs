using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace InovaGed.Web.Models.Protocolo;
public sealed class ProtocoloCadastrosIndexVM { public string Tipo { get; set; } = "setores"; public string Titulo { get; set; } = "Cadastros"; public string? Q { get; set; } public List<ProtocoloCadastroItemVM> Itens { get; set; } = new(); }
public class ProtocoloCadastroItemVM { public Guid Id { get; set; } public string? Codigo { get; set; } [Required] public string Nome { get; set; } = ""; public string? Sigla { get; set; } public string? Descricao { get; set; } public int Ordem { get; set; } public int? PrazoDias { get; set; } public string? Cor { get; set; } public bool Ativo { get; set; } = true; public DateTime CreatedAt { get; set; } }
public sealed class ProtocoloCadastroFormVM : ProtocoloCadastroItemVM { public string Tipo { get; set; } = "setores"; public string Titulo { get; set; } = ""; }
public sealed class ProtocoloUsuarioSetorIndexVM { public List<ProtocoloUsuarioSetorRowVM> Itens { get; set; } = new(); }
public sealed class ProtocoloUsuarioSetorRowVM { public Guid Id { get; set; } public Guid UsuarioId { get; set; } public string? UsuarioNome { get; set; } public Guid SetorId { get; set; } public string SetorNome { get; set; } = ""; public string? SetorSigla { get; set; } public bool Ativo { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class ProtocoloUsuarioSetorFormVM { public Guid? Id { get; set; } [Required] public Guid UsuarioId { get; set; } public string? UsuarioNome { get; set; } [Required] public Guid SetorId { get; set; } public bool Ativo { get; set; } = true; public List<SelectListItem> Setores { get; set; } = new(); }
