-- Actions emitted by the existing audit services, missing from the legacy base enum.
alter type ged.audit_action_enum add value if not exists 'HTTP';
alter type ged.audit_action_enum add value if not exists 'UNLOCK_USER';
alter type ged.audit_action_enum add value if not exists 'VIEW';
alter type ged.audit_action_enum add value if not exists 'MOVE_DOCUMENT_FOLDER';
alter type ged.audit_action_enum add value if not exists 'MOVE_DOCUMENT_FOLDER_BULK';
alter type ged.audit_action_enum add value if not exists 'ACCESS_DENIED_MOVE_DOCUMENT';
alter type ged.audit_action_enum add value if not exists 'VIEW_GED_DASHBOARD';
