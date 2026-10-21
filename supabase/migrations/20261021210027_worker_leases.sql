-- Observation-only stage ownership. Lease expiry is not browser-stop evidence.
create role hsr_worker nologin;
grant usage on schema hsr to hsr_worker;
alter table hsr.scans drop constraint scans_state_check;
alter table hsr.scans add constraint scans_state_check
    check (state in ('queued','leased','observed','blocked'));
alter table hsr.scan_jobs
    add column state text not null default 'queued' check (state in ('queued','leased','completed','blocked')),
    add column generation bigint not null default 0 check (generation between 0 and 3),
    add column first_claimed_at timestamptz,
    add column result_digest text,
    add column result_reference text,
    add column last_error_code text,
    add constraint completed_result_required check (
        (state='completed' and result_digest is not null and result_reference is not null)
        or (state<>'completed' and result_digest is null and result_reference is null));

-- Bearer capabilities never enter the client-readable job row or attempt log.
create table hsr.job_leases (
    organization_id uuid not null,
    job_id uuid not null,
    generation bigint not null check (generation between 1 and 3),
    worker_id uuid not null,
    token uuid not null,
    expires_at timestamptz not null,
    primary key (organization_id,job_id),
    foreign key (organization_id,job_id) references hsr.scan_jobs(organization_id,id)
);
create table hsr.job_attempts (
    organization_id uuid not null,
    job_id uuid not null,
    generation bigint not null check (generation between 1 and 3),
    worker_id uuid not null,
    token_hash bytea not null check (octet_length(token_hash)=32),
    claimed_at timestamptz not null,
    initial_expiry timestamptz not null check (initial_expiry>claimed_at),
    primary key (organization_id,job_id,generation),
    foreign key (organization_id,job_id) references hsr.scan_jobs(organization_id,id)
);
create table hsr.attempt_events (
    id bigint generated always as identity primary key,
    organization_id uuid not null,
    job_id uuid not null,
    generation bigint not null,
    kind text not null check (kind in ('Renewed','Expired','Completed','Blocked')),
    observed_at timestamptz not null default clock_timestamp(),
    lease_until timestamptz,
    error_code text,
    foreign key (organization_id,job_id,generation)
        references hsr.job_attempts(organization_id,job_id,generation)
);
create index attempt_events_job on hsr.attempt_events(organization_id,job_id,id);
create table hsr.queue_failures (
    message_id bigint primary key,
    reason text not null check (reason in ('UNBOUND_QUEUE_MESSAGE','QUEUE_BINDING_UNCONFIRMED')),
    observed_at timestamptz not null default clock_timestamp()
);
alter table hsr.job_leases enable row level security;
alter table hsr.job_attempts enable row level security;
alter table hsr.attempt_events enable row level security;
alter table hsr.queue_failures enable row level security;
revoke all on hsr.job_leases,hsr.job_attempts,hsr.attempt_events,hsr.queue_failures
    from public,anon,authenticated,hsr_worker;
grant select on hsr.job_attempts,hsr.attempt_events to authenticated;
create policy member_attempts on hsr.job_attempts for select to authenticated
    using (hsr.is_member(organization_id));
create policy member_attempt_events on hsr.attempt_events for select to authenticated
    using (hsr.is_member(organization_id));

create function hsr._archive_message(p_message_id bigint) returns void
language plpgsql set search_path='' as $$
begin
    if not coalesce(pgmq.archive('hsr_scan_jobs',p_message_id),false) then
        raise exception 'QUEUE_ACK_UNCONFIRMED';
    end if;
end $$;

create function hsr._block_job(p_job hsr.scan_jobs,p_code text) returns void
language plpgsql set search_path='' as $$
begin
    perform hsr._archive_message(p_job.queue_message_id);
    update hsr.scan_jobs set state='blocked',last_error_code=p_code
        where organization_id=p_job.organization_id and id=p_job.id;
    update hsr.scans set state='blocked'
        where organization_id=p_job.organization_id and id=p_job.scan_id;
    delete from hsr.job_leases where organization_id=p_job.organization_id and job_id=p_job.id;
    if p_job.generation>0 then
        insert into hsr.attempt_events(organization_id,job_id,generation,kind,error_code)
            values(p_job.organization_id,p_job.id,p_job.generation,'Blocked',p_code);
    end if;
end $$;

create function hsr._active_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid)
returns setof hsr.scan_jobs language plpgsql set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype; v_now timestamptz;
begin
    select j.* into v_job from hsr.scan_jobs j
        where j.organization_id=p_org and j.id=p_job for update;
    if not found then return; end if;
    -- Check the server clock after acquiring the job lock, never before a wait.
    v_now:=clock_timestamp();
    if v_job.state='leased' and v_job.generation=p_generation
        and v_now<v_job.first_claimed_at+interval '10 minutes'
        and exists(select 1 from hsr.job_leases l where l.organization_id=p_org and l.job_id=p_job
            and l.generation=p_generation and l.worker_id=p_worker and l.token=p_token and v_now<l.expires_at) then
        return next v_job;
    end if;
end $$;

create function hsr.claim_scan_job(p_worker uuid,p_ttl integer default 30)
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
    if v_message.message is distinct from jsonb_build_object('schema','hsr-stage-job-v1',
        'organizationId',v_job.organization_id,'scanId',v_job.scan_id,'jobId',v_job.id,'stage','observe-fixture') then
        insert into hsr.queue_failures(message_id,reason) values(v_message.msg_id,'QUEUE_BINDING_UNCONFIRMED');
        perform hsr._block_job(v_job,'QUEUE_BINDING_UNCONFIRMED');
        status:='Rejected';error_code:='QUEUE_BINDING_UNCONFIRMED';return next;return;
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

create function hsr.renew_scan_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid,p_ttl integer default 30)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype; v_expiry timestamptz;
begin
    if p_ttl is null or p_ttl<5 or p_ttl>60 then
        raise exception using errcode='22023',message='INVALID_WORKER_LEASE_REQUEST';
    end if;
    select j.* into v_job from hsr._active_job(p_org,p_job,p_worker,p_generation,p_token) j;
    if not found then return false;end if;
    update hsr.job_leases l set expires_at=least(clock_timestamp()+make_interval(secs=>p_ttl),v_job.first_claimed_at+interval '10 minutes')
        where l.organization_id=p_org and l.job_id=p_job and clock_timestamp()<l.expires_at
        and clock_timestamp()<v_job.first_claimed_at+interval '10 minutes' returning l.expires_at into v_expiry;
    if not found then return false;end if;
    perform pgmq.set_vt('hsr_scan_jobs',v_job.queue_message_id,
        greatest(1,ceil(extract(epoch from v_expiry-clock_timestamp()))::integer));
    if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
    insert into hsr.attempt_events(organization_id,job_id,generation,kind,lease_until)
        values(p_org,p_job,p_generation,'Renewed',v_expiry);
    return true;
end $$;

create function hsr.complete_scan_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid,p_digest text,p_reference text)
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

create function hsr.block_scan_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid,p_code text)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;
begin
    if p_code is null or p_code not in ('FIXTURE_OBSERVATION_UNCONFIRMED','OWNED_BROWSER_CLOSE_UNCONFIRMED','WORKER_STOP_REQUESTED') then
        raise exception using errcode='22023',message='INVALID_WORKER_FAILURE_CODE';
    end if;
    select j.* into v_job from hsr._active_job(p_org,p_job,p_worker,p_generation,p_token) j;
    if not found then return false;end if;
    perform hsr._block_job(v_job,p_code);
    return true;
end $$;

revoke all on function hsr._archive_message(bigint),hsr._block_job(hsr.scan_jobs,text),
    hsr._active_job(uuid,uuid,uuid,bigint,uuid),hsr.claim_scan_job(uuid,integer),
    hsr.renew_scan_job(uuid,uuid,uuid,bigint,uuid,integer),
    hsr.complete_scan_job(uuid,uuid,uuid,bigint,uuid,text,text),
    hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_scan_job(uuid,integer),hsr.renew_scan_job(uuid,uuid,uuid,bigint,uuid,integer),
    hsr.complete_scan_job(uuid,uuid,uuid,bigint,uuid,text,text),
    hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text) to hsr_worker;
