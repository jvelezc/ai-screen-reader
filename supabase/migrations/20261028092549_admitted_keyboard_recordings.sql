-- Fixed authored keyboard routes extend the admitted recording boundary.
alter table hsr.scan_jobs add column traversal_route integer not null default 0;
alter table hsr.scan_jobs add constraint traversal_route_mode check
    (traversal_route between 0 and 4 and (traversal_route=0 or record_observation));
create function hsr._create_scan_collection(
    p_organization_id uuid,
    p_request_id uuid,
    p_fixture text default 'synthetic-shop',
    p_reader_profile text default 'hsr-desktop-reader-v13',
    p_recording boolean default false,
    p_route integer default 0
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
    if p_route is null or p_route<0 or p_route>4 or (p_route>0 and not p_recording)
        or p_recording is null or p_request_id is null or p_fixture is distinct from 'synthetic-shop'
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
                and j.scan_id=selected_scan.id and j.record_observation=p_recording and j.traversal_route=p_route) then
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
    ) || case when p_recording then jsonb_build_object('recordObservation',true) else '{}'::jsonb end
      || case when p_route>0 then jsonb_build_object('traversalRoute',p_route) else '{}'::jsonb end) into strict selected_message;
    insert into hsr.scan_jobs(organization_id, scan_id, id, stage, queue_message_id, record_observation, traversal_route)
        values (p_organization_id, selected_scan.id, selected_job, 'observe-fixture', selected_message, p_recording, p_route);
    return query select selected_scan.id, selected_job, selected_message;
end $$;



revoke all on function hsr._create_scan_collection(uuid,uuid,text,text,boolean,integer) from public,anon,authenticated,hsr_worker;
create or replace function hsr._create_scan_mode(p_organization_id uuid,p_request_id uuid,
    p_fixture text default 'synthetic-shop',p_reader_profile text default 'hsr-desktop-reader-v13',p_recording boolean default false)
returns table(scan_id uuid,job_id uuid,queue_message_id bigint)
language sql security definer set search_path='' as $$
    select * from hsr._create_scan_collection(p_organization_id,p_request_id,p_fixture,p_reader_profile,p_recording,0)
$$;
create function hsr.create_recorded_traversal(p_organization_id uuid,p_request_id uuid,p_route integer)
returns table(scan_id uuid,job_id uuid,queue_message_id bigint)
language plpgsql security definer set search_path='' as $$
begin
    if p_route is null or p_route<1 or p_route>4 then
        raise exception using errcode='22023',message='HSR_INVALID_TRAVERSAL_ROUTE';
    end if;
    return query select * from hsr._create_scan_collection(p_organization_id,p_request_id,'synthetic-shop','hsr-desktop-reader-v13',true,p_route);
end $$;
revoke all on function hsr.create_recorded_traversal(uuid,uuid,integer) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.create_recorded_traversal(uuid,uuid,integer) to authenticated;
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
        || case when v_job.record_observation then jsonb_build_object('recordObservation',true) else '{}'::jsonb end
        || case when v_job.traversal_route>0 then jsonb_build_object('traversalRoute',v_job.traversal_route) else '{}'::jsonb end) then
        insert into hsr.queue_failures(message_id,reason) values(v_message.msg_id,'QUEUE_BINDING_UNCONFIRMED');
        perform hsr._block_job(v_job,'QUEUE_BINDING_UNCONFIRMED');
        status:='Rejected';error_code:='QUEUE_BINDING_UNCONFIRMED';return next;return;
    end if;
    -- Passive-only and legacy workers never obtain a traversal capability.
    if v_job.traversal_route>0 and current_setting('hsr.traversal_claim',true) is distinct from 'on' then
        perform pgmq.set_vt('hsr_scan_jobs',v_message.msg_id,2);
        if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
        status:='Deferred';return next;return;
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



create function hsr.claim_scan_job_with_traversal(p_worker uuid,p_ttl integer default 30)
returns table(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,
    lease_token uuid,lease_until timestamptz,error_code text,record_observation boolean,traversal_route integer)
language plpgsql security definer set search_path='' as $$
declare
    previous_recording text:=current_setting('hsr.recording_claim',true);
    previous_traversal text:=current_setting('hsr.traversal_claim',true);
begin
    perform set_config('hsr.recording_claim','on',true);
    perform set_config('hsr.traversal_claim','on',true);
    return query select c.*,coalesce(j.record_observation,false),coalesce(j.traversal_route,0) from hsr.claim_scan_job(p_worker,p_ttl) c
        left join hsr.scan_jobs j on j.organization_id=c.organization_id and j.id=c.job_id;
    perform set_config('hsr.recording_claim',coalesce(previous_recording,''),true);
    perform set_config('hsr.traversal_claim',coalesce(previous_traversal,''),true);
end $$;
revoke all on function hsr.claim_scan_job_with_traversal(uuid,integer) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_scan_job_with_traversal(uuid,integer) to hsr_worker;
