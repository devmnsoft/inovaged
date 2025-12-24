namespace InovaGed.Domain.Acl;

[Flags]
public enum AclPermission
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4,
    Download = 8,
    Admin = 16
}
