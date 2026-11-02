-- A caller-scoped observation of worker permission, never a lease capability.
create function hsr.read_scan_job_permission(p_org uuid,p_scan uuid,p_job uuid,p_generation bigint)
returns table(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,status text,
    checked_at timestamptz,expires_at timestamptz)
language plpgsql security definer set search_path='' as $$
declare v_now timestamptz;
begin
    if p_org is null or p_scan is null or p_job is null or p_generation is null
        or p_org='00000000-0000-0000-0000-000000000000'::uuid
        or p_scan='00000000-0000-0000-0000-000000000000'::uuid
        or p_job='00000000-0000-0000-0000-000000000000'::uuid
        or p_generation<1 or p_generation>3 then
        raise exception using errcode='22023',message='INVALID_PERMISSION_SNAPSHOT_SCOPE';
    end if;
    -- Hold this original caller's membership through the scoped read. Revocation
    -- cannot race a checked snapshot; the next call checks current membership.
    perform 1 from hsr.memberships m
        where m.organization_id=p_org and m.user_id=(select auth.uid()) for key share;
    if not found then
        raise exception using errcode='42501',message='PERMISSION_SNAPSHOT_MEMBERSHIP_REQUIRED';
    end if;
    v_now:=clock_timestamp();
    return query
        select j.organization_id,j.scan_id,j.id,j.generation,
            case when l.job_id is null then 'Missing'::text
                 when l.expires_at>v_now then 'Active'::text else 'Expired'::text end,
            v_now,l.expires_at
        from hsr.scan_jobs j
        left join hsr.job_leases l on l.organization_id=j.organization_id and l.job_id=j.id
            and l.generation=j.generation and exists(
                select 1 from hsr.job_attempts a where a.organization_id=l.organization_id
                    and a.job_id=l.job_id and a.generation=l.generation and a.worker_id=l.worker_id)
        where j.organization_id=p_org and j.scan_id=p_scan and j.id=p_job
            and j.generation=p_generation and j.state='leased' and j.stage='observe-fixture';
end $$;
revoke all on function hsr.read_scan_job_permission(uuid,uuid,uuid,bigint) from public,anon,authenticated;
grant execute on function hsr.read_scan_job_permission(uuid,uuid,uuid,bigint) to authenticated;
comment on function hsr.read_scan_job_permission(uuid,uuid,uuid,bigint) is
    'Current-member database-time permission snapshot for one actual leased job/generation. No token, input, replay or completion authority.';
