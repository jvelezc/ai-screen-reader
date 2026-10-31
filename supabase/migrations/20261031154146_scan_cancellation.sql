-- Cancellation revokes authority. It does not establish native absence or no input.
alter table hsr.scan_jobs
    add column cancellation_requested_at timestamptz,
    add column cancellation_requested_by uuid,
    add constraint cancellation_identity_paired check (
        (cancellation_requested_at is null)=(cancellation_requested_by is null)
        and (cancellation_requested_by is null or cancellation_requested_by<>'00000000-0000-0000-0000-000000000000')),
    add constraint cancelled_job_cannot_resume check (cancellation_requested_at is null or state='blocked');

create function hsr.request_scan_cancellation(p_organization_id uuid,p_scan_id uuid)
returns table(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,producer_id uuid,
    status text,input_assessment text,requested_at timestamptz)
language plpgsql security definer set search_path='' as $$
declare v_actor uuid:=auth.uid();v_job hsr.scan_jobs%rowtype;v_now timestamptz;
begin
    if p_organization_id is null or p_scan_id is null
        or '00000000-0000-0000-0000-000000000000'::uuid in (p_organization_id,p_scan_id) then
        raise exception using errcode='22023',message='HSR_INVALID_CANCELLATION_REQUEST';
    end if;
    -- Hold the original user's membership through this transaction, as admission does.
    perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=v_actor for key share;
    if not found then raise exception using errcode='42501',message='HSR_CANCELLATION_DENIED';end if;
    select j.* into v_job from hsr.scan_jobs j join hsr.scans s
        on s.organization_id=j.organization_id and s.id=j.scan_id
        where j.organization_id=p_organization_id and j.scan_id=p_scan_id and s.created_by=v_actor
        for update of j;
    if not found then raise exception using errcode='42501',message='HSR_CANCELLATION_DENIED';end if;
    organization_id:=v_job.organization_id;scan_id:=v_job.scan_id;job_id:=v_job.id;generation:=v_job.generation;
    select a.worker_id into producer_id from hsr.job_attempts a
        where a.organization_id=v_job.organization_id and a.job_id=v_job.id and a.generation=v_job.generation;
    input_assessment:='NotAssessed';
    if v_job.state='completed' then
        status:='AlreadyCompleted';requested_at:=null;return next;return;
    end if;
    if v_job.cancellation_requested_at is null then
        v_now:=clock_timestamp();
        if v_job.state<>'blocked' then
            perform hsr._block_job(v_job,'WORKER_CANCELLED_BY_CALLER');
        else
            -- Already-blocked messages were archived earlier. Never acknowledge twice.
            update hsr.scan_jobs j set last_error_code='WORKER_CANCELLED_BY_CALLER'
                where j.organization_id=v_job.organization_id and j.id=v_job.id;
            delete from hsr.job_leases l where l.organization_id=v_job.organization_id and l.job_id=v_job.id;
            if v_job.generation>0 then
                insert into hsr.attempt_events(organization_id,job_id,generation,kind,error_code)
                    values(v_job.organization_id,v_job.id,v_job.generation,'Blocked','WORKER_CANCELLED_BY_CALLER');
            end if;
        end if;
        update hsr.scan_jobs j set cancellation_requested_at=v_now,cancellation_requested_by=v_actor
            where j.organization_id=v_job.organization_id and j.id=v_job.id;
        requested_at:=v_now;
    else
        requested_at:=v_job.cancellation_requested_at;
    end if;
    -- A lease/intent is never converted to zero input. Only no admitted attempt is known.
    if v_job.generation=0 and not exists(select 1 from hsr.job_attempts a
        where a.organization_id=v_job.organization_id and a.job_id=v_job.id) then
        input_assessment:='NoAttemptStarted';
    end if;
    status:='Requested';return next;
end $$;
revoke all on function hsr.request_scan_cancellation(uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.request_scan_cancellation(uuid,uuid) to authenticated;

-- The execution lease is removed by _block_job. Publication has a different
-- capability: deny its claim and every active check without deleting originals.

create or replace function hsr.claim_recorded_publication(p_org uuid,p_scan uuid,p_job uuid,p_generation bigint,
    p_producer uuid,p_bundle text,p_token uuid,p_ttl integer default 30)
returns table(status text,lease_until timestamptz,record_observation boolean,traversal_route integer)
language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;v_prior hsr.recorded_publications%rowtype;v_now timestamptz;
begin
    if p_org is null or p_scan is null or p_job is null or p_producer is null or p_token is null
        or '00000000-0000-0000-0000-000000000000'::uuid in (p_org,p_scan,p_job,p_producer,p_token)
        or p_generation is null or p_generation not between 1 and 3
        or p_bundle is null or length(p_bundle)<>64 or p_bundle!~'^[a-f0-9]{64}$'
        or p_ttl is null or p_ttl not between 5 and 60 then
        raise exception using errcode='22023',message='INVALID_RECORDED_PUBLICATION_REQUEST';
    end if;
    -- Consistent job-first locking; no browser, queue claim or execution token is issued.
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    status:='Rejected';
    if not found or v_job.cancellation_requested_at is not null or v_job.scan_id<>p_scan or v_job.generation<>p_generation or not v_job.record_observation
        or not exists(select 1 from hsr.job_attempts a where a.organization_id=p_org and a.job_id=p_job
            and a.generation=p_generation and a.worker_id=p_producer)
        or not exists(select 1 from hsr.scans s join hsr.memberships m on m.organization_id=s.organization_id and m.user_id=s.created_by
            where s.organization_id=p_org and s.id=p_scan) then return next;return;end if;
    select r.* into v_prior from hsr.recorded_publications r
        where r.organization_id=p_org and r.job_id=p_job and r.generation=p_generation for update;
    if found and (v_prior.scan_id<>p_scan or v_prior.producer_id<>p_producer or v_prior.bundle_sha256<>p_bundle
        or v_prior.traversal_route<>v_job.traversal_route or v_prior.state='completed' and v_job.state<>'completed') then
        return next;return;
    end if;
    record_observation:=v_job.record_observation;traversal_route:=v_job.traversal_route;
    if v_job.state='completed' then
        -- No retrospective producer receipt or storage writes for an existing completed job.
        if v_job.archive_worker_id=p_producer and v_job.archive_index_sha256 is not null then status:='AlreadyCompleted';end if;
        return next;return;
    end if;
    if v_job.state not in ('leased','blocked') then return next;return;end if;
    v_now:=clock_timestamp();
    if exists(select 1 from hsr.job_leases l where l.organization_id=p_org and l.job_id=p_job and v_now<l.expires_at) then
        status:='Busy';return next;return;
    end if;
    if v_prior.state='publishing' and v_now<v_prior.expires_at then
        if v_prior.token_hash=sha256(convert_to(p_token::text,'UTF8')) then
            status:='Claimed';lease_until:=v_prior.expires_at;
        else status:='Busy';end if;
        return next;return;
    end if;
    if v_job.state='leased' then
        insert into hsr.attempt_events(organization_id,job_id,generation,kind) values(p_org,p_job,p_generation,'Expired');
        perform hsr._block_job(v_job,'WORKER_PUBLICATION_RECOVERY_REQUESTED');
    end if;
    insert into hsr.recorded_publications(organization_id,job_id,generation,scan_id,producer_id,traversal_route,bundle_sha256,
        token_hash,state,claimed_at,expires_at) values(p_org,p_job,p_generation,p_scan,p_producer,v_job.traversal_route,p_bundle,
        sha256(convert_to(p_token::text,'UTF8')),'publishing',v_now,v_now+make_interval(secs=>p_ttl))
    on conflict(organization_id,job_id,generation) do update set token_hash=excluded.token_hash,
        state='publishing',claimed_at=excluded.claimed_at,expires_at=excluded.expires_at,completed_at=null;
    insert into hsr.recorded_publication_events(organization_id,job_id,generation,kind) values(p_org,p_job,p_generation,'Claimed');
    status:='Claimed';lease_until:=v_now+make_interval(secs=>p_ttl);return next;
end $$;


create or replace function hsr._active_recorded_publication(p_org uuid,p_job uuid,p_generation bigint,p_producer uuid,p_bundle text,p_token uuid)
returns setof hsr.recorded_publications language plpgsql set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;v_record hsr.recorded_publications%rowtype;v_now timestamptz;
begin
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    if not found or v_job.cancellation_requested_at is not null or v_job.state<>'blocked' or v_job.generation<>p_generation or not v_job.record_observation then return;end if;
    select r.* into v_record from hsr.recorded_publications r
        where r.organization_id=p_org and r.job_id=p_job and r.generation=p_generation for update;
    v_now:=clock_timestamp();
    if found and v_record.state='publishing' and v_record.scan_id=v_job.scan_id and v_record.producer_id=p_producer
        and v_record.traversal_route=v_job.traversal_route
        and v_record.bundle_sha256=p_bundle and v_record.token_hash=sha256(convert_to(p_token::text,'UTF8'))
        and v_now<v_record.expires_at and v_now<v_record.claimed_at+interval '10 minutes'
        and not exists(select 1 from hsr.job_leases l where l.organization_id=p_org and l.job_id=p_job)
        and exists(select 1 from hsr.job_attempts a where a.organization_id=p_org and a.job_id=p_job
            and a.generation=p_generation and a.worker_id=p_producer)
        and exists(select 1 from hsr.scans s join hsr.memberships m on m.organization_id=s.organization_id and m.user_id=s.created_by
            where s.organization_id=p_org and s.id=v_record.scan_id) then return next v_record;end if;
end $$;
