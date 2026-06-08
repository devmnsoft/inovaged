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

        // Documentos incompletos / fracionados
        public const string DocumentPartMarkIncomplete = "DOCUMENT_PART_MARK_INCOMPLETE";
        public const string DocumentPartAdd = "DOCUMENT_PART_ADD";
        public const string DocumentPartView = "DOCUMENT_PART_VIEW";
        public const string DocumentPartConsolidate = "DOCUMENT_PART_CONSOLIDATE";
        public const string DocumentPartCancel = "DOCUMENT_PART_CANCEL";

        // Admin
        public const string AdminUsers = "ADMIN_USERS";
        public const string AdminRoles = "ADMIN_ROLES";
        public const string AdminTypes = "ADMIN_DOCTYPES";
        public const string AdminAudit = "ADMIN_AUDIT";
    }
}
