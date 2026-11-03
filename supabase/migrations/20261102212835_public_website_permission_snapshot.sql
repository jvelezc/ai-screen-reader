-- One statement joins job state and its clock-bounded lease snapshot. Consumers
-- never receive a private token or separately sampled, contradictory states.
create function hsr.read_public_scan_status(p_organization_id uuid,p_scan_id uuid)
returns jsonb language plpgsql security definer set search_path='' as $$
declare observed_at timestamptz;result jsonb;
begin
    if p_organization_id is null or p_scan_id is null
        or p_organization_id='00000000-0000-0000-0000-000000000000'::uuid
        or p_scan_id='00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using errcode='22023',message='HSR_PUBLIC_SCAN_IDENTITY_INVALID';
    end if;
    perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=auth.uid() for key share;
    if not found then raise exception using errcode='42501',message='HSR_PUBLIC_SCAN_ACCESS_DENIED';end if;
    observed_at:=clock_timestamp();
    select jsonb_build_object('schema','hsr-public-scan-status-v1','organization_id',s.organization_id,'id',s.id,
        'request_id',s.request_id,'created_by',s.created_by,'target_url',s.target_url,'allowed_host',s.allowed_host,
        'reader_profile',s.reader_profile,'state',j.state,'created_at',s.created_at,'job_id',j.id,'generation',j.generation,
        'producer_id',j.result_worker_id,'result_digest',j.result_digest,'last_error_code',j.last_error_code,
        'worker_permission',case when j.state='leased' then jsonb_build_object('organization_id',s.organization_id,'scan_id',s.id,
            'job_id',j.id,'generation',j.generation,'status',case when l.job_id is null then 'Missing'
                when l.expires_at<=observed_at then 'Expired' else 'Active' end,
            'checked_at',observed_at,'expires_at',l.expires_at) else null end)
        into result from hsr.public_scans s join hsr.public_scan_jobs j on j.organization_id=s.organization_id and j.scan_id=s.id
        left join hsr.public_scan_leases l on l.organization_id=j.organization_id and l.job_id=j.id and l.generation=j.generation
        where s.organization_id=p_organization_id and s.id=p_scan_id;
    return result;
end $$;
revoke all on function hsr.read_public_scan_status(uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.read_public_scan_status(uuid,uuid) to authenticated;
