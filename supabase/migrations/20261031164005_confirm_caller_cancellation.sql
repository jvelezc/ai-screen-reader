-- Read-only reconciliation, not execution authority or native-cleanup confirmation.
create function hsr.confirm_scan_cancellation(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid)
returns boolean language sql stable security definer set search_path='' as $$
    select exists(
        select 1 from hsr.scan_jobs j
        join hsr.scans s on s.organization_id=j.organization_id and s.id=j.scan_id
        join hsr.job_attempts a on a.organization_id=j.organization_id and a.job_id=j.id and a.generation=j.generation
        where j.organization_id=p_org and j.id=p_job and j.generation=p_generation
            and j.state='blocked' and j.cancellation_requested_at is not null
            and j.cancellation_requested_by=s.created_by and j.last_error_code='WORKER_CANCELLED_BY_CALLER'
            and a.worker_id=p_worker and a.token_hash=sha256(convert_to(p_token::text,'UTF8'))
            and not exists(select 1 from hsr.job_leases l where l.organization_id=j.organization_id and l.job_id=j.id)
    );
$$;
revoke all on function hsr.confirm_scan_cancellation(uuid,uuid,uuid,bigint,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.confirm_scan_cancellation(uuid,uuid,uuid,bigint,uuid) to hsr_worker;
