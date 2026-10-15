"""Seed only the named disposable Docker/PostgreSQL test database with synthetic data."""
import base64
import hashlib
import os
from pathlib import Path
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[2]
DATABASE = os.environ.get("INOVAGED_SECTOR_FIXTURE_DATABASE", "sector_clean")
if not DATABASE.startswith("sector_") or not DATABASE.replace("_", "").isalnum():
    raise SystemExit("Only an explicitly named sector_* disposable database is supported.")
PASSWORD = "Sector-Test-2026!"  # Synthetic account; never use in an installation.
salt = os.urandom(16)
password_hash = base64.b64encode(b"\x01" + struct.pack(">III", 2, 100000, 16) + salt + hashlib.pbkdf2_hmac("sha512", PASSWORD.encode(), salt, 100000, 32)).decode()

# A minimal valid, synthetic, single-page PDF with a native text stream.
stream = b"BT /F1 18 Tf 60 750 Td (Synthetic document - not clinical data) Tj ET"
objects = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
    b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"\nendstream"]
pdf = bytearray(b"%PDF-1.4\n")
offsets = [0]
for number, obj in enumerate(objects, 1):
    offsets.append(len(pdf))
    pdf.extend(f"{number} 0 obj\n".encode() + obj + b"\nendobj\n")
xref = len(pdf)
pdf.extend(f"xref\n0 {len(offsets)}\n0000000000 65535 f \n".encode())
for offset in offsets[1:]:
    pdf.extend(f"{offset:010d} 00000 n \n".encode())
pdf.extend(f"trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
storage = ROOT / "artifacts/sector-validation/storage"
storage.mkdir(parents=True, exist_ok=True)
(storage / "synthetic.pdf").write_bytes(pdf)
sha = hashlib.sha256(pdf).hexdigest()

sql = rf"""
\set ON_ERROR_STOP on
begin;
do $$begin
 if exists(select 1 from ged.tenant where code='default' and id<>'50000000-0000-0000-0000-000000000001') then
  raise exception 'Default tenant is not the fixture; use another disposable database';
 end if;
end$$;
insert into ged.tenant(id,name,code) values('50000000-0000-0000-0000-000000000001','Synthetic partner fixture','default') on conflict(id) do nothing;
insert into ged.app_user(id,tenant_id,name,email,user_name,password_hash)
values('50000000-0000-0000-0000-000000000002','50000000-0000-0000-0000-000000000001','Pessoa sintética','sector@example.test','sector@example.test','{password_hash}')
on conflict(id) do update set password_hash=excluded.password_hash,is_active=true,is_locked=false,deleted_at_utc=null;
insert into ged.role(id,tenant_id,code,name) values('50000000-0000-0000-0000-000000000003','50000000-0000-0000-0000-000000000001','HOSPITAL','Hospital') on conflict(id) do nothing;
insert into ged.app_role(id,tenant_id,name,normalized_name) values('50000000-0000-0000-0000-000000000003','50000000-0000-0000-0000-000000000001','Hospital','HOSPITAL') on conflict(id) do nothing;
insert into ged.user_role(user_id,role_id) values('50000000-0000-0000-0000-000000000002','50000000-0000-0000-0000-000000000003') on conflict do nothing;
insert into ged.permission(code,name) values('Documents.View','View synthetic documents') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values('50000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000003','Documents.View') on conflict do nothing;
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values('50000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000002','FATURAMENTO','ADMINISTRAR') on conflict do nothing;
insert into ged.sector_document_type(tenant_id,catalog_code,catalog_order,name,applicability,multiple_files,physical_original,view_sectors)
values('50000000-0000-0000-0000-000000000001','DS23',23,'Prescrição Médica','fixture',true,false,'{{FATURAMENTO}}') on conflict do nothing;
insert into ged.sector_patient(id,tenant_id,source_system,external_code,display_name,confirmed_at)
values('50000000-0000-0000-0000-000000000004','50000000-0000-0000-0000-000000000001','SYNTHETIC','000120','Paciente sintético — integração',now()) on conflict(id) do nothing;
insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values('50000000-0000-0000-0000-000000000005','50000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000004','000088',now()) on conflict(id) do nothing;
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values('50000000-0000-0000-0000-000000000006','50000000-0000-0000-0000-000000000001','SECTOR-FIXTURE','Prescrição sintética',false,'50000000-0000-0000-0000-000000000002') on conflict(id) do nothing;
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,content_text,created_by)
values('50000000-0000-0000-0000-000000000007','50000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000006',1,'synthetic.pdf','.pdf',{len(pdf)},'synthetic.pdf','{sha}','application/pdf','Prescrição de teste. Medicamento sintético 10 mg por via oral. Somente para homologação.','50000000-0000-0000-0000-000000000002') on conflict(id) do nothing;
update ged.document set current_version_id='50000000-0000-0000-0000-000000000007' where id='50000000-0000-0000-0000-000000000006';
insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values('50000000-0000-0000-0000-000000000008','50000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000004','50000000-0000-0000-0000-000000000005','50000000-0000-0000-0000-000000000006','50000000-0000-0000-0000-000000000007','DS23','SYNTHETIC','synthetic.pdf','{sha}','50000000-0000-0000-0000-000000000002','GRAVADO','NAO_APLICAVEL','NAO_APLICAVEL','AGUARDANDO') on conflict(id) do nothing;
insert into ged.sector_integration(id,tenant_id,client_id,secret_hash,source_system,origins,catalog_codes,max_minutes,allow_encounter_list,display_name)
values('50000000-0000-0000-0000-000000000009','50000000-0000-0000-0000-000000000001','partner-gate','{hashlib.sha256(b"synthetic-partner-secret-2026").hexdigest()}','SYNTHETIC','{{http://localhost:5190,http://127.0.0.1:5190}}','{{DS23}}',15,true,'Parceiro sintético de homologação')
on conflict(id) do update set active=true,origins=excluded.origins,catalog_codes=excluded.catalog_codes,display_name=excluded.display_name;
commit;
"""
subprocess.run(["docker", "exec", "-i", "inovaged-sector-validation", "psql", "-X", "-q", "-U", "postgres", "-d", DATABASE], input=sql.encode(), check=True)
print("Synthetic partner fixture ready; account sector@example.test; storage under artifacts/sector-validation/storage.")
