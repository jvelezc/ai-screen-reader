-- Expand only passive original capacity; retain every caller/lease/producer/native gate.
begin;
alter table hsr.public_scan_jobs drop constraint public_scan_jobs_result_text_check;
alter table hsr.public_scan_jobs add constraint public_scan_jobs_result_text_check
    check(result_text is null or octet_length(result_text) between 1 and 4194304);

create or replace function hsr.finish_public_scan(p_organization_id uuid,p_job_id uuid,p_generation bigint,p_worker_id uuid,p_token uuid,p_result_text text,p_digest text)
returns boolean language plpgsql security definer set search_path='' as $$
declare selected hsr.public_scan_jobs%rowtype; source hsr.public_scans%rowtype; result jsonb; capture jsonb;
begin
    select j.* into selected from hsr.public_scan_jobs j join hsr.public_scan_leases l on l.organization_id=j.organization_id and l.job_id=j.id
        where j.organization_id=p_organization_id and j.id=p_job_id and j.state='leased' and j.generation=p_generation
        and l.generation=p_generation and l.worker_id=p_worker_id and l.token=p_token and l.expires_at>clock_timestamp() for update of j,l;
    if not found then return false; end if;
    select s.* into strict source from hsr.public_scans s where s.organization_id=selected.organization_id and s.id=selected.scan_id;
    perform 1 from hsr.memberships m where m.organization_id=source.organization_id and m.user_id=source.created_by for key share;
    if not found then return false; end if;
    if p_result_text is null or octet_length(p_result_text) not between 1 and 4194304
        or p_digest is distinct from encode(sha256(convert_to(p_result_text,'UTF8')),'hex') then return false; end if;
    begin result:=p_result_text::jsonb; exception when invalid_text_representation then return false; end;
    if result->>'Schema' is distinct from 'hsr-public-website-observation-v1'
        or result->'Job' is distinct from jsonb_build_object('OrganizationId',selected.organization_id,'ScanId',selected.scan_id,
            'JobId',selected.id,'Generation',selected.generation,'Source',jsonb_build_object('Profile',source.reader_profile,'TargetUrl',source.target_url,'AllowedHost',source.allowed_host))
        or result->>'WorkerId' is distinct from p_worker_id::text or result->'NativeShutdownConfirmed' is distinct from 'true'::jsonb
        or result->'ResourcesAbsenceConfirmed' is distinct from 'true'::jsonb or result->'DispatchedInputs' is distinct from '0'::jsonb
        or result->'GoalVerified' is distinct from 'false'::jsonb or jsonb_typeof(result->'Captures') is distinct from 'array'
        or result->>'ReceivedAt' is null then return false; end if;
    if jsonb_array_length(result->'Captures')<>2 then return false; end if;
    for capture in select value from jsonb_array_elements(result->'Captures') loop
        if capture->>'DocumentId' is null or length(btrim(capture->>'DocumentId'))=0
            or capture->>'DocumentId' is distinct from result->'Captures'->0->>'DocumentId'
            or capture->'CaptureEpoch' is distinct from '1'::jsonb or jsonb_typeof(capture->'Nodes') is distinct from 'array'
            or not hsr.valid_public_source(capture->>'DocumentUrl',source.allowed_host) then return false; end if;
        if jsonb_array_length(capture->'Nodes') not between 1 and 2048 then return false; end if;
    end loop;
    perform 1 from pgmq.q_hsr_public_scan_jobs q where q.msg_id=selected.queue_message_id for update;
    if not found then return false; end if;
    if not exists(select 1 from hsr.public_scan_leases l where l.organization_id=p_organization_id and l.job_id=p_job_id
        and l.generation=p_generation and l.worker_id=p_worker_id and l.token=p_token and l.expires_at>clock_timestamp()) then return false; end if;
    if not pgmq.delete('hsr_public_scan_jobs',selected.queue_message_id) then return false; end if;
    update hsr.public_scan_jobs set state='completed',result_text=p_result_text,result_digest=p_digest,result_worker_id=p_worker_id,last_error_code=null
        where organization_id=selected.organization_id and id=selected.id;
    update hsr.public_scans set state='observed' where organization_id=source.organization_id and id=source.id;
    update hsr.public_scan_attempts set state='completed',finished_at=clock_timestamp() where organization_id=selected.organization_id and job_id=selected.id and generation=p_generation;
    delete from hsr.public_scan_leases where organization_id=selected.organization_id and job_id=selected.id;
    return true;
end $$;
revoke all on function hsr.finish_public_scan(uuid,uuid,bigint,uuid,uuid,text,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.finish_public_scan(uuid,uuid,bigint,uuid,uuid,text,text) to hsr_worker;


commit;

