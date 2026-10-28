-- Explicit passive recording of the already validated, sanitized bundled fixture.
-- Old admission, retry identity, worker execution and two-file provenance remain compatible.
alter table hsr.scan_jobs add column record_observation boolean not null default false;
alter table hsr.scan_jobs drop constraint producer_archive_complete;
alter table hsr.scan_jobs add constraint producer_archive_complete check (
    num_nonnulls(archive_index_sha256,archive_manifest_sha256,archive_bytes,archive_worker_id)=0
    or (num_nonnulls(archive_index_sha256,archive_manifest_sha256,archive_bytes,archive_worker_id)=4
        and state='completed' and archive_index_sha256 ~ '^[a-f0-9]{64}$' and length(archive_index_sha256)=64
        and archive_manifest_sha256 ~ '^[a-f0-9]{64}$' and length(archive_manifest_sha256)=64
        and archive_bytes between 1 and case when record_observation then 26218496 else 1052672 end
        and archive_worker_id <> '00000000-0000-0000-0000-000000000000'::uuid));


create function hsr._create_scan_mode(
    p_organization_id uuid,
    p_request_id uuid,
    p_fixture text default 'synthetic-shop',
    p_reader_profile text default 'hsr-desktop-reader-v13',
    p_recording boolean default false
) returns table (scan_id uuid, job_id uuid, queue_message_id bigint)
language plpgsql security definer set search_path = '' as $$
declare
    actor uuid := auth.uid();
    selected_scan hsr.scans%rowtype;
    selected_job uuid := gen_random_uuid();
    selected_message bigint;
begin
    -- Lock admission's membership until this transaction ends. Revocation cannot
    -- race a checked admission; later calls always check current membership.
    perform 1 from hsr.memberships m
        where m.organization_id = p_organization_id and m.user_id = actor
        for key share;
    if not found then
        raise exception using errcode = '42501', message = 'HSR_MEMBERSHIP_REQUIRED';
    end if;
    if p_recording is null or p_request_id is null or p_fixture is distinct from 'synthetic-shop'
        or p_reader_profile is distinct from 'hsr-desktop-reader-v13' then
        raise exception using errcode = '22023', message = 'HSR_INVALID_SCAN_REQUEST';
    end if;

    insert into hsr.scans(organization_id, request_id, created_by, fixture, reader_profile)
        values (p_organization_id, p_request_id, actor, p_fixture, p_reader_profile)
        on conflict (organization_id, request_id) do nothing
        returning * into selected_scan;
    if not found then
        select s.* into strict selected_scan from hsr.scans s
            where s.organization_id = p_organization_id and s.request_id = p_request_id;
        if selected_scan.created_by <> actor or selected_scan.fixture <> p_fixture
            or selected_scan.reader_profile <> p_reader_profile
            or not exists(select 1 from hsr.scan_jobs j where j.organization_id=p_organization_id
                and j.scan_id=selected_scan.id and j.record_observation=p_recording) then
            raise exception using errcode = '22023', message = 'HSR_REQUEST_CONFLICT';
        end if;
        return query select selected_scan.id, j.id, j.queue_message_id
            from hsr.scan_jobs j
            where j.organization_id = p_organization_id and j.scan_id = selected_scan.id;
        return;
    end if;

    select pgmq.send('hsr_scan_jobs', jsonb_build_object(
        'schema', 'hsr-stage-job-v1', 'organizationId', p_organization_id,
        'scanId', selected_scan.id, 'jobId', selected_job, 'stage', 'observe-fixture'
    ) || case when p_recording then jsonb_build_object('recordObservation',true) else '{}'::jsonb end) into strict selected_message;
    insert into hsr.scan_jobs(organization_id, scan_id, id, stage, queue_message_id, record_observation)
        values (p_organization_id, selected_scan.id, selected_job, 'observe-fixture', selected_message, p_recording);
    return query select selected_scan.id, selected_job, selected_message;
end $$;


revoke all on function hsr._create_scan_mode(uuid,uuid,text,text,boolean) from public,anon,authenticated,hsr_worker;
create or replace function hsr.create_scan(p_organization_id uuid,p_request_id uuid,
    p_fixture text default 'synthetic-shop',p_reader_profile text default 'hsr-desktop-reader-v13')
returns table(scan_id uuid,job_id uuid,queue_message_id bigint)
language sql security definer set search_path='' as $$
    select * from hsr._create_scan_mode(p_organization_id,p_request_id,p_fixture,p_reader_profile,false)
$$;
create function hsr.create_recorded_observation(p_organization_id uuid,p_request_id uuid)
returns table(scan_id uuid,job_id uuid,queue_message_id bigint)
language sql security definer set search_path='' as $$
    select * from hsr._create_scan_mode(p_organization_id,p_request_id,'synthetic-shop','hsr-desktop-reader-v13',true)
$$;
revoke all on function hsr.create_recorded_observation(uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.create_recorded_observation(uuid,uuid) to authenticated;


create or replace function hsr.claim_scan_job(p_worker uuid,p_ttl integer default 30)
returns table(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,
    lease_token uuid,lease_until timestamptz,error_code text)
language plpgsql security definer set search_path='' as $$
declare
    v_message pgmq.message_record;
    v_job hsr.scan_jobs%rowtype;
    v_lease hsr.job_leases%rowtype;
    v_now timestamptz;
    v_deadline timestamptz;
    v_token uuid;
    v_expiry timestamptz;
begin
    if p_worker is null or p_ttl is null or p_ttl<5 or p_ttl>60 then
        raise exception using errcode='22023',message='INVALID_WORKER_LEASE_REQUEST';
    end if;
    select m.* into v_message from pgmq.read('hsr_scan_jobs',p_ttl,1) m;
    if not found then status:='Empty';return next;return;end if;
    -- Queue read locks queue first. Never wait on the opposite job-first order
    -- used by completion/renewal: defer a busy job instead of forming a cycle.
    select j.* into v_job from hsr.scan_jobs j where j.queue_message_id=v_message.msg_id
        for update skip locked;
    if not found then
        if exists(select 1 from hsr.scan_jobs j where j.queue_message_id=v_message.msg_id) then
            status:='Deferred';return next;return;
        end if;
        perform hsr._archive_message(v_message.msg_id);
        insert into hsr.queue_failures(message_id,reason) values(v_message.msg_id,'UNBOUND_QUEUE_MESSAGE');
        status:='Rejected';error_code:='UNBOUND_QUEUE_MESSAGE';return next;return;
    end if;
    organization_id:=v_job.organization_id;scan_id:=v_job.scan_id;job_id:=v_job.id;
    generation:=v_job.generation;
    if v_message.message is distinct from (jsonb_build_object('schema','hsr-stage-job-v1',
        'organizationId',v_job.organization_id,'scanId',v_job.scan_id,'jobId',v_job.id,'stage','observe-fixture')
        || case when v_job.record_observation then jsonb_build_object('recordObservation',true) else '{}'::jsonb end) then
        insert into hsr.queue_failures(message_id,reason) values(v_message.msg_id,'QUEUE_BINDING_UNCONFIRMED');
        perform hsr._block_job(v_job,'QUEUE_BINDING_UNCONFIRMED');
        status:='Rejected';error_code:='QUEUE_BINDING_UNCONFIRMED';return next;return;
    end if;
    -- Legacy workers never obtain a capability for a recording request.
    if v_job.record_observation and current_setting('hsr.recording_claim',true) is distinct from 'on' then
        perform pgmq.set_vt('hsr_scan_jobs',v_message.msg_id,2);
        if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
        status:='Deferred';return next;return;
    end if;
    if v_job.state in ('completed','blocked') then
        perform hsr._archive_message(v_message.msg_id);
        status:='Deduplicated';return next;return;
    end if;
    v_now:=clock_timestamp();
    v_deadline:=coalesce(v_job.first_claimed_at,v_now)+interval '10 minutes';
    select l.* into v_lease from hsr.job_leases l
        where l.organization_id=v_job.organization_id and l.job_id=v_job.id;
    if v_lease.expires_at>v_now and v_now<v_deadline then
        perform pgmq.set_vt('hsr_scan_jobs',v_message.msg_id,
            greatest(1,ceil(extract(epoch from v_lease.expires_at-v_now))::integer));
        if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
        status:='Deferred';return next;return;
    end if;
    if v_job.state='leased' then
        insert into hsr.attempt_events(organization_id,job_id,generation,kind,error_code)
            values(v_job.organization_id,v_job.id,v_job.generation,'Expired','WORKER_LEASE_EXPIRED');
    end if;
    if v_now>=v_deadline or v_job.generation>=3 then
        error_code:=case when v_now>=v_deadline then 'WORKER_BUDGET_EXHAUSTED' else 'WORKER_ATTEMPTS_EXHAUSTED' end;
        perform hsr._block_job(v_job,error_code);
        status:='Blocked';return next;return;
    end if;
    v_token:=gen_random_uuid();v_expiry:=least(v_now+make_interval(secs=>p_ttl),v_deadline);
    update hsr.scan_jobs j set state='leased',generation=v_job.generation+1,
        first_claimed_at=coalesce(j.first_claimed_at,v_now) where j.organization_id=v_job.organization_id and j.id=v_job.id;
    update hsr.scans s set state='leased' where s.organization_id=v_job.organization_id and s.id=v_job.scan_id;
    delete from hsr.job_leases l where l.organization_id=v_job.organization_id and l.job_id=v_job.id;
    insert into hsr.job_leases values(v_job.organization_id,v_job.id,v_job.generation+1,p_worker,v_token,v_expiry);
    insert into hsr.job_attempts values(v_job.organization_id,v_job.id,v_job.generation+1,p_worker,
        sha256(convert_to(v_token::text,'UTF8')),v_now,v_expiry);
    generation:=v_job.generation+1;lease_token:=v_token;lease_until:=v_expiry;status:='Claimed';return next;
end $$;


create function hsr.claim_scan_job_with_recording(p_worker uuid,p_ttl integer default 30)
returns table(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,
    lease_token uuid,lease_until timestamptz,error_code text,record_observation boolean)
language plpgsql security definer set search_path='' as $$
declare previous_setting text:=current_setting('hsr.recording_claim',true);
begin
    perform set_config('hsr.recording_claim','on',true);
    return query select c.*,coalesce(j.record_observation,false) from hsr.claim_scan_job(p_worker,p_ttl) c
        left join hsr.scan_jobs j on j.organization_id=c.organization_id and j.id=c.job_id;
    perform set_config('hsr.recording_claim',coalesce(previous_setting,''),true);
end $$;
revoke all on function hsr.claim_scan_job_with_recording(uuid,integer) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_scan_job_with_recording(uuid,integer) to hsr_worker;


create function hsr._complete_scan_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid,p_digest text,p_reference text)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype; v_reference text;
begin
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    if not found then return false;end if;
    v_reference:=replace(p_org::text,'-','')||'/'||replace(v_job.scan_id::text,'-','')||'/'||
        replace(p_job::text,'-','')||'/'||p_generation::text||'/observation.json';
    if p_digest is null or length(p_digest)<>64 or p_digest!~'^[a-f0-9]{64}$'
        or p_generation is null or p_generation<1 or p_generation>3 or p_reference is null
        or p_reference is distinct from v_reference then
        raise exception using errcode='22023',message='INVALID_OBSERVATION_RECEIPT';
    end if;
    if v_job.state='completed' then
        return v_job.generation=p_generation and v_job.result_digest=p_digest and v_job.result_reference=p_reference
            and exists(select 1 from hsr.job_attempts a where a.organization_id=p_org and a.job_id=p_job
                and a.generation=p_generation and a.worker_id=p_worker
                and a.token_hash=sha256(convert_to(p_token::text,'UTF8')));
    end if;
    select j.* into v_job from hsr._active_job(p_org,p_job,p_worker,p_generation,p_token) j;
    if not found then return false;end if;
    update hsr.scan_jobs j set state='completed',result_digest=p_digest,result_reference=p_reference
        where j.organization_id=p_org and j.id=p_job and exists(select 1 from hsr.job_leases l
            where l.organization_id=p_org and l.job_id=p_job and clock_timestamp()<l.expires_at
                and clock_timestamp()<v_job.first_claimed_at+interval '10 minutes');
    if not found then return false;end if;
    if not coalesce(pgmq.delete('hsr_scan_jobs',v_job.queue_message_id),false) then
        raise exception 'QUEUE_ACK_UNCONFIRMED';
    end if;
    update hsr.scans s set state='observed' where s.organization_id=p_org and s.id=v_job.scan_id;
    delete from hsr.job_leases l where l.organization_id=p_org and l.job_id=p_job;
    insert into hsr.attempt_events(organization_id,job_id,generation,kind) values(p_org,p_job,p_generation,'Completed');
    return true;
end $$;


revoke all on function hsr._complete_scan_job(uuid,uuid,uuid,bigint,uuid,text,text) from public,anon,authenticated,hsr_worker;
create or replace function hsr.complete_scan_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,
    p_token uuid,p_digest text,p_reference text) returns boolean language sql security definer set search_path='' as $$
    select case when exists(select 1 from hsr.scan_jobs where organization_id=p_org and id=p_job and record_observation)
        then false else hsr._complete_scan_job(p_org,p_job,p_worker,p_generation,p_token,p_digest,p_reference) end
$$;


create or replace function hsr.complete_scan_job_with_archive(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,
    p_token uuid,p_digest text,p_reference text,p_index text,p_manifest text,p_bytes bigint)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;
begin
    if p_index is null or length(p_index)<>64 or p_index !~ '^[a-f0-9]{64}$'
        or p_manifest is null or length(p_manifest)<>64 or p_manifest !~ '^[a-f0-9]{64}$'
        or p_bytes is null or p_bytes not between 1 and 26218496 then
        raise exception using errcode='22023',message='INVALID_PRODUCER_ARCHIVE';
    end if;
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    if not found then return false;end if;
    if p_bytes > (case when v_job.record_observation then 26218496 else 1052672 end) then
        raise exception using errcode='22023',message='INVALID_PRODUCER_ARCHIVE';
    end if;
    if v_job.state='completed' then
        -- Exact idempotent retry only. An older completed job cannot acquire a
        -- retrospective producer receipt, even with its original capability.
        return v_job.archive_index_sha256 is not distinct from p_index
            and v_job.archive_manifest_sha256 is not distinct from p_manifest
            and v_job.archive_bytes is not distinct from p_bytes
            and v_job.archive_worker_id is not distinct from p_worker
            and hsr._complete_scan_job(p_org,p_job,p_worker,p_generation,p_token,p_digest,p_reference);
    end if;
    if not hsr._complete_scan_job(p_org,p_job,p_worker,p_generation,p_token,p_digest,p_reference) then return false;end if;
    update hsr.scan_jobs set archive_index_sha256=p_index,archive_manifest_sha256=p_manifest,
        archive_bytes=p_bytes,archive_worker_id=p_worker where organization_id=p_org and id=p_job;
    return true;
end $$;
