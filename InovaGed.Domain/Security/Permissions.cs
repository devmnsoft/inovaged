namespace InovaGed.Domain.Security
{

    public static class Permissions
    {
        // Pastas
        public const string FolderRead = "FOLDER_READ";
        public const string FolderWrite = "FOLDER_WRITE";
        public const string FolderAdmin = "FOLDER_ADMIN";

        // Documentos
        public const string DocRead = "DOC_READ";
        public const string DocWrite = "DOC_WRITE";
        public const string DocDelete = "DOC_DELETE";
        public const string DocDownload = "DOC_DOWNLOAD";
        public const string DocWorkflow = "DOC_WORKFLOW";

        // Admin
        public const string AdminUsers = "ADMIN_USERS";
        public const string AdminRoles = "ADMIN_ROLES";
        public const string AdminTypes = "ADMIN_DOCTYPES";
        public const string AdminAudit = "ADMIN_AUDIT";
    }
}
