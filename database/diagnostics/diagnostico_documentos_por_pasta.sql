select
    folder_id,
    count(*) as total
from ged.document
where tenant_id = '00000000-0000-0000-0000-000000000001'
and coalesce(reg_status,'A') = 'A'
group by folder_id
order by total desc;

select *
from ged.folder
where tenant_id = '00000000-0000-0000-0000-000000000001'
and id in (
    '3551d298-b112-4b55-ad16-2499c40bb78e',
    'f0000000-0000-0000-0000-000000000010'
);
