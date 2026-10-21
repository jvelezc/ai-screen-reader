-- Apply transactionally after inspecting the selected project's schema and grants.
-- This admission slice queues only a bundled synthetic fixture; it executes no input.
create schema hsr;
revoke all on schema hsr from public, anon, authenticated;
grant usage on schema hsr to authenticated;

create extension if not exists pgmq;
do $$
begin
    if exists (select 1 from pgmq.list_queues() where queue_name = 'hsr_scan_jobs') then
        raise exception 'HSR_QUEUE_ALREADY_EXISTS';
    end if;
end $$;
select pgmq.create('hsr_scan_jobs');
alter table pgmq.q_hsr_scan_jobs enable row level security;
alter table pgmq.a_hsr_scan_jobs enable row level security;
revoke all on pgmq.q_hsr_scan_jobs, pgmq.a_hsr_scan_jobs from public, anon, authenticated;

create table hsr.organizations (
    id uuid primary key default gen_random_uuid(),
    name text not null check (length(btrim(name)) between 1 and 120)
);
create table hsr.memberships (
    organization_id uuid not null references hsr.organizations(id),
    user_id uuid not null references auth.users(id),
    primary key (organization_id, user_id)
);
create index memberships_user on hsr.memberships(user_id, organization_id);

create table hsr.scans (
    organization_id uuid not null references hsr.organizations(id),
    id uuid not null default gen_random_uuid(),
    request_id uuid not null,
    created_by uuid not null references auth.users(id),
    fixture text not null check (fixture = 'synthetic-shop'),
    reader_profile text not null check (reader_profile = 'hsr-desktop-reader-v13'),
    state text not null default 'queued' check (state = 'queued'),
    created_at timestamptz not null default transaction_timestamp(),
    primary key (organization_id, id),
    unique (organization_id, request_id)
);
create table hsr.scan_jobs (
    organization_id uuid not null,
    scan_id uuid not null,
    id uuid not null default gen_random_uuid(),
    stage text not null check (stage = 'observe-fixture'),
    queue_message_id bigint not null unique,
    created_at timestamptz not null default transaction_timestamp(),
    primary key (organization_id, id),
    unique (organization_id, scan_id, stage),
    foreign key (organization_id, scan_id) references hsr.scans(organization_id, id)
);

create function hsr.is_member(p_organization_id uuid) returns boolean
language sql stable security definer set search_path = '' as $$
    select exists (
        select 1 from hsr.memberships m
        where m.organization_id = p_organization_id and m.user_id = (select auth.uid())
    );
$$;
revoke all on function hsr.is_member(uuid) from public, anon, authenticated;
grant execute on function hsr.is_member(uuid) to authenticated;

alter table hsr.organizations enable row level security;
alter table hsr.memberships enable row level security;
alter table hsr.scans enable row level security;
alter table hsr.scan_jobs enable row level security;
create policy member_organizations on hsr.organizations for select to authenticated
    using (hsr.is_member(id));
create policy own_memberships on hsr.memberships for select to authenticated
    using (user_id = (select auth.uid()));
create policy member_scans on hsr.scans for select to authenticated
    using (hsr.is_member(organization_id));
create policy member_jobs on hsr.scan_jobs for select to authenticated
    using (hsr.is_member(organization_id));

create function hsr.create_scan(
    p_organization_id uuid,
    p_request_id uuid,
    p_fixture text default 'synthetic-shop',
    p_reader_profile text default 'hsr-desktop-reader-v13'
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
    if p_request_id is null or p_fixture is distinct from 'synthetic-shop'
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
            or selected_scan.reader_profile <> p_reader_profile then
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
    )) into strict selected_message;
    insert into hsr.scan_jobs(organization_id, scan_id, id, stage, queue_message_id)
        values (p_organization_id, selected_scan.id, selected_job, 'observe-fixture', selected_message);
    return query select selected_scan.id, selected_job, selected_message;
end $$;
revoke all on function hsr.create_scan(uuid, uuid, text, text) from public, anon, authenticated;
grant execute on function hsr.create_scan(uuid, uuid, text, text) to authenticated;

-- Admission is the only client write path. Provisioning and worker dispatch are
-- deliberately not exposed by this migration. Never give a browser a DB secret.
revoke all on all tables in schema hsr from public, anon, authenticated;
grant select on hsr.organizations, hsr.memberships, hsr.scans, hsr.scan_jobs to authenticated;
