-- Storage-only recovery for the original recorded attempt. Never an execution lease.
create table hsr.recorded_publications (
    organization_id uuid not null,
    job_id uuid not null,
    generation bigint not null,
    scan_id uuid not null,
    producer_id uuid not null check (producer_id<>'00000000-0000-0000-0000-000000000000'),
    traversal_route integer not null check (traversal_route between 0 and 4),
    bundle_sha256 text not null check (length(bundle_sha256)=64 and bundle_sha256~'^[a-f0-9]{64}$'),
    token_hash bytea not null check (octet_length(token_hash)=32),
    state text not null check (state in ('publishing','failed','completed')),
    claimed_at timestamptz not null,
    expires_at timestamptz not null,
    completed_at timestamptz,
    primary key (organization_id,job_id,generation),
    foreign key (organization_id,job_id,generation) references hsr.job_attempts(organization_id,job_id,generation),
    check ((state='completed')=(completed_at is not null))
);
create table hsr.recorded_publication_events (
    id bigint generated always as identity primary key,
    organization_id uuid not null,
    job_id uuid not null,
    generation bigint not null,
    kind text not null check (kind in ('Claimed','Failed','Completed')),
    observed_at timestamptz not null default clock_timestamp(),
    error_code text check (error_code is null or error_code~'^[A-Z][A-Z0-9_]{0,127}$'),
    foreign key (organization_id,job_id,generation) references hsr.recorded_publications(organization_id,job_id,generation)
);
create index recorded_publication_events_job on hsr.recorded_publication_events(organization_id,job_id,generation,id);
alter table hsr.recorded_publications enable row level security;
alter table hsr.recorded_publication_events enable row level security;
revoke all on hsr.recorded_publications,hsr.recorded_publication_events from public,anon,authenticated,hsr_worker;
grant select on hsr.recorded_publication_events to authenticated;
create policy member_publication_events on hsr.recorded_publication_events for select to authenticated
    using (hsr.is_member(organization_id));

create function hsr.claim_recorded_publication(p_org uuid,p_scan uuid,p_job uuid,p_generation bigint,
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
    if not found or v_job.scan_id<>p_scan or v_job.generation<>p_generation or not v_job.record_observation
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

create function hsr._active_recorded_publication(p_org uuid,p_job uuid,p_generation bigint,p_producer uuid,p_bundle text,p_token uuid)
returns setof hsr.recorded_publications language plpgsql set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;v_record hsr.recorded_publications%rowtype;v_now timestamptz;
begin
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    if not found or v_job.state<>'blocked' or v_job.generation<>p_generation or not v_job.record_observation then return;end if;
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

create function hsr.renew_recorded_publication(p_org uuid,p_job uuid,p_generation bigint,p_producer uuid,p_bundle text,p_token uuid,p_ttl integer default 30)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_record hsr.recorded_publications%rowtype;
begin
    if p_ttl is null or p_ttl not between 5 and 60 then
        raise exception using errcode='22023',message='INVALID_RECORDED_PUBLICATION_REQUEST';end if;
    select r.* into v_record from hsr._active_recorded_publication(p_org,p_job,p_generation,p_producer,p_bundle,p_token) r;
    if not found then return false;end if;
    update hsr.recorded_publications set expires_at=least(clock_timestamp()+make_interval(secs=>p_ttl),claimed_at+interval '10 minutes')
        where organization_id=p_org and job_id=p_job and generation=p_generation and clock_timestamp()<expires_at
            and clock_timestamp()<claimed_at+interval '10 minutes';
    return found;
end $$;

create function hsr.fail_recorded_publication(p_org uuid,p_job uuid,p_generation bigint,p_producer uuid,p_bundle text,p_token uuid,p_code text)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_record hsr.recorded_publications%rowtype;
begin
    if p_code is null or p_code!~'^[A-Z][A-Z0-9_]{0,127}$' then
        raise exception using errcode='22023',message='INVALID_RECORDED_PUBLICATION_ERROR';end if;
    select r.* into v_record from hsr._active_recorded_publication(p_org,p_job,p_generation,p_producer,p_bundle,p_token) r;
    if not found then return false;end if;
    update hsr.recorded_publications set state='failed',expires_at=clock_timestamp()
        where organization_id=p_org and job_id=p_job and generation=p_generation;
    insert into hsr.recorded_publication_events(organization_id,job_id,generation,kind,error_code) values(p_org,p_job,p_generation,'Failed',p_code);
    return true;
end $$;

create function hsr.complete_recorded_publication(p_org uuid,p_job uuid,p_generation bigint,p_producer uuid,p_bundle text,p_token uuid,
    p_digest text,p_reference text,p_index text,p_manifest text,p_bytes bigint)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;v_record hsr.recorded_publications%rowtype;v_reference text;
begin
    if p_digest is null or length(p_digest)<>64 or p_digest!~'^[a-f0-9]{64}$'
        or p_index is null or length(p_index)<>64 or p_index!~'^[a-f0-9]{64}$'
        or p_manifest is null or length(p_manifest)<>64 or p_manifest!~'^[a-f0-9]{64}$'
        or p_bytes is null or p_bytes not between 1 and 26218496 then
        raise exception using errcode='22023',message='INVALID_RECORDED_PUBLICATION_RECEIPT';end if;
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    if not found then return false;end if;
    v_reference:=replace(p_org::text,'-','')||'/'||replace(v_job.scan_id::text,'-','')||'/'||replace(p_job::text,'-','')||'/'||p_generation::text||'/observation.json';
    if p_reference is null or p_reference is distinct from v_reference then
        raise exception using errcode='22023',message='INVALID_RECORDED_PUBLICATION_RECEIPT';end if;
    select r.* into v_record from hsr.recorded_publications r
        where r.organization_id=p_org and r.job_id=p_job and r.generation=p_generation for update;
    if v_job.state='completed' then
        return coalesce(found and v_record.state='completed' and v_record.producer_id=p_producer and v_record.bundle_sha256=p_bundle
            and v_record.token_hash=sha256(convert_to(p_token::text,'UTF8')) and v_job.generation=p_generation
            and v_job.result_digest=p_digest and v_job.result_reference=p_reference and v_job.archive_index_sha256=p_index
            and v_job.archive_manifest_sha256=p_manifest and v_job.archive_bytes=p_bytes and v_job.archive_worker_id=p_producer,false);
    end if;
    select r.* into v_record from hsr._active_recorded_publication(p_org,p_job,p_generation,p_producer,p_bundle,p_token) r;
    if not found then return false;end if;
    update hsr.scan_jobs set state='completed',result_digest=p_digest,result_reference=p_reference,last_error_code=null,
        archive_index_sha256=p_index,archive_manifest_sha256=p_manifest,archive_bytes=p_bytes,archive_worker_id=p_producer
        where organization_id=p_org and id=p_job and state='blocked' and generation=p_generation
            and clock_timestamp()<v_record.expires_at and clock_timestamp()<v_record.claimed_at+interval '10 minutes';
    if not found then return false;end if;
    update hsr.scans set state='observed' where organization_id=p_org and id=v_job.scan_id;
    update hsr.recorded_publications set state='completed',completed_at=clock_timestamp()
        where organization_id=p_org and job_id=p_job and generation=p_generation;
    insert into hsr.recorded_publication_events(organization_id,job_id,generation,kind) values(p_org,p_job,p_generation,'Completed');
    return true;
end $$;

revoke all on function hsr._active_recorded_publication(uuid,uuid,bigint,uuid,text,uuid) from public,anon,authenticated,hsr_worker;
revoke all on function hsr.claim_recorded_publication(uuid,uuid,uuid,bigint,uuid,text,uuid,integer),
    hsr.renew_recorded_publication(uuid,uuid,bigint,uuid,text,uuid,integer),
    hsr.fail_recorded_publication(uuid,uuid,bigint,uuid,text,uuid,text),
    hsr.complete_recorded_publication(uuid,uuid,bigint,uuid,text,uuid,text,text,text,text,bigint) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_recorded_publication(uuid,uuid,uuid,bigint,uuid,text,uuid,integer),
    hsr.renew_recorded_publication(uuid,uuid,bigint,uuid,text,uuid,integer),
    hsr.fail_recorded_publication(uuid,uuid,bigint,uuid,text,uuid,text),
    hsr.complete_recorded_publication(uuid,uuid,bigint,uuid,text,uuid,text,text,text,text,bigint) to hsr_worker;
