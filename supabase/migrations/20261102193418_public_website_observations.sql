-- Separate declared-URL passive collection. Existing fixture/recording provenance is unchanged.
-- Apply only after disposable PostgreSQL/PostgREST/native composition and CI pass.
do $$ begin
    if to_regclass('hsr.memberships') is null or not exists(select 1 from pg_roles where rolname='hsr_worker') then
        raise exception 'HSR_PUBLIC_FOUNDATION_REQUIRED';
    end if;
    if exists(select 1 from pgmq.list_queues() where queue_name='hsr_public_scan_jobs') then
        raise exception 'HSR_PUBLIC_QUEUE_ALREADY_EXISTS';
    end if;
end $$;
select pgmq.create('hsr_public_scan_jobs');
alter table pgmq.q_hsr_public_scan_jobs enable row level security;
alter table pgmq.a_hsr_public_scan_jobs enable row level security;
revoke all on pgmq.q_hsr_public_scan_jobs, pgmq.a_hsr_public_scan_jobs from public,anon,authenticated,hsr_worker;

create function hsr.valid_public_source(p_url text,p_host text) returns boolean
language sql immutable set search_path='' as $$
    select coalesce(length(p_url) between 1 and 2048 and length(p_host) between 3 and 253
        and p_host=lower(p_host) and p_host like '%.%' and p_host !~ '^[0-9.]+$'
        and p_host ~ '^[a-z0-9.-]+$'
        and not exists(select 1 from unnest(string_to_array(p_host,'.')) label
            where length(label) not between 1 and 63 or label like '-%' or label like '%-')
        and left(p_url,length('https://'||p_host||'/'))='https://'||p_host||'/'
        and p_url !~ '[[:space:][:cntrl:]#]' and position(chr(92) in p_url)=0,false);
$$;
revoke all on function hsr.valid_public_source(text,text) from public,anon,authenticated;

create table hsr.public_scans(
    organization_id uuid not null references hsr.organizations(id),
    id uuid not null default gen_random_uuid(),request_id uuid not null,
    created_by uuid not null references auth.users(id),target_url text not null,allowed_host text not null,
    reader_profile text not null check(reader_profile='hsr-approved-public-readonly-v1'),
    state text not null default 'queued' check(state in('queued','leased','observed','blocked','cancelled')),
    created_at timestamptz not null default transaction_timestamp(),
    primary key(organization_id,id),unique(organization_id,request_id),
    check(request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
    check(hsr.valid_public_source(target_url,allowed_host))
);
create index public_scans_recent on hsr.public_scans(organization_id,created_at desc,id);
create table hsr.public_scan_jobs(
    organization_id uuid not null,scan_id uuid not null,id uuid not null default gen_random_uuid(),
    queue_message_id bigint not null unique,
    state text not null default 'queued' check(state in('queued','leased','completed','blocked','cancelled')),
    generation bigint not null default 0 check(generation between 0 and 3),
    created_at timestamptz not null default transaction_timestamp(),
    last_error_code text check(last_error_code ~ '^PUBLIC_WEBSITE_[A-Z_]{1,80}$'),
    result_text text,result_digest text check(result_digest ~ '^[a-f0-9]{64}$'),result_worker_id uuid,
    primary key(organization_id,id),unique(organization_id,scan_id),
    foreign key(organization_id,scan_id) references hsr.public_scans(organization_id,id),
    check((state='completed')=(result_text is not null and result_digest is not null and result_worker_id is not null)),
    check(result_text is null or octet_length(result_text) between 1 and 1048576)
);
create index public_jobs_state on hsr.public_scan_jobs(state,created_at,id);
create table hsr.public_scan_leases(
    organization_id uuid not null,job_id uuid not null,generation bigint not null check(generation between 1 and 3),
    worker_id uuid not null,token uuid not null,expires_at timestamptz not null,
    primary key(organization_id,job_id),
    foreign key(organization_id,job_id) references hsr.public_scan_jobs(organization_id,id)
);
create table hsr.public_scan_attempts(
    organization_id uuid not null,job_id uuid not null,generation bigint not null check(generation between 1 and 3),
    worker_id uuid not null,started_at timestamptz not null,finished_at timestamptz,
    state text not null check(state in('leased','completed','blocked','expired')),
    error_code text check(error_code ~ '^PUBLIC_WEBSITE_[A-Z_]{1,80}$'),
    primary key(organization_id,job_id,generation),
    foreign key(organization_id,job_id) references hsr.public_scan_jobs(organization_id,id)
);
alter table hsr.public_scans enable row level security;
alter table hsr.public_scan_jobs enable row level security;
alter table hsr.public_scan_leases enable row level security;
alter table hsr.public_scan_attempts enable row level security;
create policy public_scan_members on hsr.public_scans for select to authenticated using(hsr.is_member(organization_id));
create policy public_job_members on hsr.public_scan_jobs for select to authenticated using(hsr.is_member(organization_id));
create policy public_attempt_members on hsr.public_scan_attempts for select to authenticated using(hsr.is_member(organization_id));
revoke all on hsr.public_scans,hsr.public_scan_jobs,hsr.public_scan_leases,hsr.public_scan_attempts from public,anon,authenticated,hsr_worker;
grant select on hsr.public_scans,hsr.public_scan_jobs,hsr.public_scan_attempts to authenticated;

create function hsr.create_public_scan(p_organization_id uuid,p_request_id uuid,p_target_url text,p_allowed_host text)
returns table(scan_id uuid,job_id uuid)
language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid(); selected hsr.public_scans%rowtype; selected_job uuid:=gen_random_uuid(); message bigint;
begin
    perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=actor for key share;
    if not found then raise exception using errcode='42501',message='HSR_MEMBERSHIP_REQUIRED'; end if;
    if p_request_id is null or p_request_id='00000000-0000-0000-0000-000000000000'::uuid
        or not hsr.valid_public_source(p_target_url,p_allowed_host) then
        raise exception using errcode='22023',message='HSR_INVALID_PUBLIC_SCAN_REQUEST';
    end if;
    -- Serialize a workspace's admission count and idempotency decision, without network I/O.
    perform pg_advisory_xact_lock(hashtextextended('hsr-public-admission:'||p_organization_id::text,0));
    select s.* into selected from hsr.public_scans s where s.organization_id=p_organization_id and s.request_id=p_request_id;
    if found then
        if selected.created_by<>actor or selected.target_url<>p_target_url or selected.allowed_host<>p_allowed_host then
            raise exception using errcode='22023',message='HSR_REQUEST_CONFLICT';
        end if;
        return query select selected.id,j.id from hsr.public_scan_jobs j where j.organization_id=p_organization_id and j.scan_id=selected.id;
        return;
    end if;
    if (select count(*) from hsr.public_scan_jobs j where j.organization_id=p_organization_id and j.state in('queued','leased'))>=50 then
        raise exception using errcode='54000',message='HSR_PUBLIC_SCAN_QUEUE_FULL';
    end if;
    insert into hsr.public_scans(organization_id,request_id,created_by,target_url,allowed_host,reader_profile)
        values(p_organization_id,p_request_id,actor,p_target_url,p_allowed_host,'hsr-approved-public-readonly-v1') returning * into selected;
    select pgmq.send('hsr_public_scan_jobs',jsonb_build_object('schema','hsr-public-stage-job-v1',
        'organizationId',p_organization_id,'scanId',selected.id,'jobId',selected_job,'stage','observe-public')) into strict message;
    insert into hsr.public_scan_jobs(organization_id,scan_id,id,queue_message_id) values(p_organization_id,selected.id,selected_job,message);
    return query select selected.id,selected_job;
end $$;
revoke all on function hsr.create_public_scan(uuid,uuid,text,text) from public,anon,authenticated;
grant execute on function hsr.create_public_scan(uuid,uuid,text,text) to authenticated;

-- The trusted single-host coordinator recovers its complete native inventory
-- before calling claim. No expired attempt is automatically replayed.
create function hsr.claim_public_scan(p_worker_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare item record; selected hsr.public_scan_jobs%rowtype; source hsr.public_scans%rowtype;
    capability uuid; expired record;
begin
    if p_worker_id is null or p_worker_id='00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using errcode='22023',message='HSR_PUBLIC_WORKER_INVALID';
    end if;
    perform pg_advisory_xact_lock(hashtextextended('hsr-public-claim-v1',0));
    for expired in select l.organization_id,l.job_id,l.generation,j.scan_id,j.queue_message_id from hsr.public_scan_leases l
        join hsr.public_scan_jobs j on j.organization_id=l.organization_id and j.id=l.job_id
        where l.expires_at<=clock_timestamp() for update of l,j loop
        update hsr.public_scan_jobs set state='blocked',last_error_code='PUBLIC_WEBSITE_WORKER_EXPIRED'
            where organization_id=expired.organization_id and id=expired.job_id;
        update hsr.public_scans set state='blocked' where organization_id=expired.organization_id and id=expired.scan_id;
        update hsr.public_scan_attempts set state='expired',finished_at=clock_timestamp(),error_code='PUBLIC_WEBSITE_WORKER_EXPIRED'
            where organization_id=expired.organization_id and job_id=expired.job_id and generation=expired.generation;
        delete from hsr.public_scan_leases where organization_id=expired.organization_id and job_id=expired.job_id;
        perform pgmq.delete('hsr_public_scan_jobs',expired.queue_message_id);
    end loop;
    if (select count(*) from hsr.public_scan_leases)>=2 then return jsonb_build_object('status','AtCapacity'); end if;
    select * into item from pgmq.read('hsr_public_scan_jobs',30,1);
    if not found then return jsonb_build_object('status','Empty'); end if;
    select j.* into selected from hsr.public_scan_jobs j where j.queue_message_id=item.msg_id for update skip locked;
    if not found then
        if exists(select 1 from hsr.public_scan_jobs j where j.queue_message_id=item.msg_id) then
            return jsonb_build_object('status','Deferred');
        end if;
        perform pgmq.archive('hsr_public_scan_jobs',item.msg_id); return jsonb_build_object('status','InvalidMessage');
    end if;
    select s.* into strict source from hsr.public_scans s where s.organization_id=selected.organization_id and s.id=selected.scan_id;
    if item.message is distinct from jsonb_build_object('schema','hsr-public-stage-job-v1','organizationId',selected.organization_id,
        'scanId',selected.scan_id,'jobId',selected.id,'stage','observe-public') or selected.state<>'queued' or selected.generation<>0 then
        return jsonb_build_object('status','Unconfirmed');
    end if;
    perform 1 from hsr.memberships m where m.organization_id=source.organization_id and m.user_id=source.created_by for key share;
    if not found then
        update hsr.public_scan_jobs set state='blocked',last_error_code='PUBLIC_WEBSITE_AUTHORITY_LOST' where organization_id=selected.organization_id and id=selected.id;
        update hsr.public_scans set state='blocked' where organization_id=source.organization_id and id=source.id;
        perform pgmq.delete('hsr_public_scan_jobs',item.msg_id); return jsonb_build_object('status','AuthorityLost');
    end if;
    if exists(select 1 from hsr.public_scan_leases l where l.organization_id=selected.organization_id) then
        return jsonb_build_object('status','WorkspaceBusy');
    end if;
    capability:=gen_random_uuid();
    update hsr.public_scan_jobs set state='leased',generation=1 where organization_id=selected.organization_id and id=selected.id;
    update hsr.public_scans set state='leased' where organization_id=source.organization_id and id=source.id;
    insert into hsr.public_scan_leases values(selected.organization_id,selected.id,1,p_worker_id,capability,clock_timestamp()+interval '30 seconds');
    insert into hsr.public_scan_attempts values(selected.organization_id,selected.id,1,p_worker_id,clock_timestamp(),null,'leased',null);
    return jsonb_build_object('status','Claimed','token',capability,'job',jsonb_build_object('OrganizationId',selected.organization_id,
        'ScanId',selected.scan_id,'JobId',selected.id,'Generation',1,'Source',jsonb_build_object('Profile',source.reader_profile,
        'TargetUrl',source.target_url,'AllowedHost',source.allowed_host)));
end $$;
revoke all on function hsr.claim_public_scan(uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_public_scan(uuid) to hsr_worker;

create function hsr.renew_public_scan(p_organization_id uuid,p_job_id uuid,p_generation bigint,p_worker_id uuid,p_token uuid) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected_message_id bigint;
begin
    select j.queue_message_id into selected_message_id from hsr.public_scan_jobs j
        join hsr.public_scan_leases l on l.organization_id=j.organization_id and l.job_id=j.id
        join hsr.public_scans s on s.organization_id=j.organization_id and s.id=j.scan_id
        join hsr.memberships m on m.organization_id=s.organization_id and m.user_id=s.created_by
        where j.organization_id=p_organization_id and j.id=p_job_id and j.state='leased' and j.generation=p_generation
        and l.generation=p_generation and l.worker_id=p_worker_id and l.token=p_token and l.expires_at>clock_timestamp()
        for update of j,l for key share of m;
    if not found then return false; end if;
    perform 1 from pgmq.q_hsr_public_scan_jobs q where q.msg_id=selected_message_id for update;
    if not found then return false; end if;
    -- The row/membership lock may have waited past the initially checked expiry.
    if not exists(select 1 from hsr.public_scan_leases l where l.organization_id=p_organization_id and l.job_id=p_job_id
        and l.generation=p_generation and l.worker_id=p_worker_id and l.token=p_token and l.expires_at>clock_timestamp()) then return false; end if;
    perform pgmq.set_vt('hsr_public_scan_jobs',selected_message_id,30);
    if not found then return false; end if;
    update hsr.public_scan_leases set expires_at=clock_timestamp()+interval '30 seconds' where organization_id=p_organization_id and job_id=p_job_id;
    return true;
end $$;
revoke all on function hsr.renew_public_scan(uuid,uuid,bigint,uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.renew_public_scan(uuid,uuid,bigint,uuid,uuid) to hsr_worker;

create function hsr.finish_public_scan(p_organization_id uuid,p_job_id uuid,p_generation bigint,p_worker_id uuid,p_token uuid,p_result_text text,p_digest text)
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
    if p_result_text is null or octet_length(p_result_text) not between 1 and 1048576
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

create function hsr.block_public_scan(p_organization_id uuid,p_job_id uuid,p_generation bigint,p_worker_id uuid,p_token uuid,p_code text)
returns boolean language plpgsql security definer set search_path='' as $$
declare selected hsr.public_scan_jobs%rowtype;
begin
    if p_code is null or p_code not in('PUBLIC_WEBSITE_SOURCE_UNCONFIRMED','PUBLIC_WEBSITE_AUTHORITY_LOST','PUBLIC_WEBSITE_CLEANUP_UNCONFIRMED',
        'PUBLIC_WEBSITE_PUBLICATION_UNCONFIRMED','PUBLIC_WEBSITE_EXECUTION_CANCELLED') then return false; end if;
    select j.* into selected from hsr.public_scan_jobs j join hsr.public_scan_leases l on l.organization_id=j.organization_id and l.job_id=j.id
        where j.organization_id=p_organization_id and j.id=p_job_id and j.state='leased' and j.generation=p_generation
        and l.generation=p_generation and l.worker_id=p_worker_id and l.token=p_token and l.expires_at>clock_timestamp() for update of j,l;
    if not found then return false; end if;
    perform 1 from pgmq.q_hsr_public_scan_jobs q where q.msg_id=selected.queue_message_id for update;
    if not found then return false; end if;
    if not exists(select 1 from hsr.public_scan_leases l where l.organization_id=p_organization_id and l.job_id=p_job_id
        and l.generation=p_generation and l.worker_id=p_worker_id and l.token=p_token and l.expires_at>clock_timestamp()) then return false; end if;
    if not pgmq.delete('hsr_public_scan_jobs',selected.queue_message_id) then return false; end if;
    update hsr.public_scan_jobs set state='blocked',last_error_code=p_code where organization_id=selected.organization_id and id=selected.id;
    update hsr.public_scans set state='blocked' where organization_id=selected.organization_id and id=selected.scan_id;
    update hsr.public_scan_attempts set state='blocked',finished_at=clock_timestamp(),error_code=p_code where organization_id=selected.organization_id and job_id=selected.id and generation=p_generation;
    delete from hsr.public_scan_leases where organization_id=selected.organization_id and job_id=selected.id;
    return true;
end $$;
revoke all on function hsr.block_public_scan(uuid,uuid,bigint,uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.block_public_scan(uuid,uuid,bigint,uuid,uuid,text) to hsr_worker;
