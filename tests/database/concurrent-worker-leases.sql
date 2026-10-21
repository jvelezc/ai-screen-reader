create extension if not exists dblink with schema extensions;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker;
select plan(10);
insert into auth.users(id) values('60000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('61000000-0000-0000-0000-000000000001','Concurrent worker fixture');
insert into hsr.memberships values('61000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000001');
set request.jwt.claim.sub='60000000-0000-0000-0000-000000000001';
create temporary table concurrent_job as select * from hsr.create_scan(
    '61000000-0000-0000-0000-000000000001','63000000-0000-0000-0000-000000000001');
-- Only this disposable test coordinator opens privileged local connections.
-- All worker entrypoints below execute after SET ROLE hsr_worker.
select dblink_connect('worker_first','dbname=postgres user=supabase_admin application_name=hsr_worker_first');
select dblink_connect('worker_second','dbname=postgres user=supabase_admin application_name=hsr_worker_second');
select dblink_exec('worker_first','set role hsr_worker');
select dblink_exec('worker_second','set role hsr_worker');
select dblink_exec('worker_first','begin');
create temporary table concurrent_capability as select * from dblink('worker_first',
    $$select * from hsr.claim_scan_job('62000000-0000-0000-0000-000000000001',60)$$)
    as t(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,lease_token uuid,lease_until timestamptz,error_code text);
select is((select job_id from concurrent_capability),(select job_id from concurrent_job),
    'ConcurrentFirstClaimBindsTheActualQueuedJob');
select dblink_send_query('worker_second',
    $$select * from hsr.claim_scan_job('62000000-0000-0000-0000-000000000002',60)$$);
do $$
declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';
begin
    loop
        perform pg_stat_clear_snapshot();
        exit when dblink_is_busy('worker_second')=0 or exists(select 1 from pg_stat_activity
            where application_name='hsr_worker_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;
        perform pg_sleep(0.01);
    end loop;
end $$;
select ok(dblink_is_busy('worker_second')=0 or exists(select 1 from pg_stat_activity
    where application_name='hsr_worker_second' and wait_event_type='Lock'),
    'SecondClaimReachesDatabaseBeforeFirstCommit');
select dblink_exec('worker_first','commit');
create temporary table second_concurrent_capability as select * from dblink_get_result('worker_second')
    as t(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,lease_token uuid,lease_until timestamptz,error_code text);
select ok((select status in ('Empty','Deferred') and lease_token is null from second_concurrent_capability),
    'ConcurrentSecondClaimCannotObtainAnotherCapability');
select is((select count(*) from hsr.job_attempts where job_id=(select job_id from concurrent_job)),1::bigint,
    'ConcurrentClaimsCommitOnlyOneAttempt');
select count(*) from dblink_get_result('worker_second')
    as t(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,lease_token uuid,lease_until timestamptz,error_code text);

select dblink_exec('worker_first','reset role');
select dblink_exec('worker_first','begin');
select dblink_exec('worker_first',format('update hsr.scan_jobs set state=state where id=%L::uuid',
    (select job_id from concurrent_job)));
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from concurrent_job),0);
select dblink_send_query('worker_second',
    $$select * from hsr.claim_scan_job('62000000-0000-0000-0000-000000000002',60)$$);
do $$
declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';
begin
    while dblink_is_busy('worker_second')<>0 and clock_timestamp()<deadline loop
        perform pg_sleep(0.01);
    end loop;
end $$;
select is(dblink_is_busy('worker_second'),0,'BusyJobClaimDefersWithoutWaitingForOwnedJobLock');
select dblink_exec('worker_first','commit');
create temporary table busy_job_claim as select * from dblink_get_result('worker_second')
    as t(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,lease_token uuid,lease_until timestamptz,error_code text);
select ok((select status='Deferred' and lease_token is null from busy_job_claim),
    'BusyJobClaimCannotDiscloseCapability');
select count(*) from dblink_get_result('worker_second')
    as t(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,lease_token uuid,lease_until timestamptz,error_code text);

select dblink_exec('worker_first','begin');
select dblink_exec('worker_first',format('update hsr.scan_jobs set state=state where id=%L::uuid',
    (select job_id from concurrent_job)));
select dblink_exec('worker_first',format('update hsr.job_leases set expires_at=clock_timestamp()+interval ''2 seconds'' where job_id=%L::uuid',
    (select job_id from concurrent_job)));
create temporary table locked_deadline as select * from dblink('worker_first',
    'select expires_at from hsr.job_leases') as t(expires_at timestamptz);
select dblink_send_query('worker_second',format(
    'select hsr.complete_scan_job(%L::uuid,%L::uuid,%L::uuid,%L::bigint,%L::uuid,%L,%L)',
    c.organization_id,c.job_id,'62000000-0000-0000-0000-000000000001',c.generation,c.lease_token,repeat('a',64),
    replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json'))
    from concurrent_capability c;
do $$
declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';
begin
    loop
        perform pg_stat_clear_snapshot();
        exit when exists(select 1 from pg_stat_activity where application_name='hsr_worker_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;
        perform pg_sleep(0.01);
    end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_worker_second' and wait_event_type='Lock'),
    'CompletionActuallyWaitsForOwnedJobLock');
do $$
begin
    while clock_timestamp()<(select expires_at from locked_deadline) loop
        perform pg_sleep(0.01);
    end loop;
end $$;
select dblink_exec('worker_first','commit');
select is((select completed from dblink_get_result('worker_second') as t(completed boolean)),false,
    'CompletionRechecksExpiryAfterActualLockWait');
select is((select count(*) from hsr.attempt_events where kind='Completed'),0::bigint,
    'ExpiredBlockedCompletionLeavesNoCompletionFact');
select dblink_disconnect('worker_first');
select dblink_disconnect('worker_second');
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from concurrent_job),0);
set role hsr_worker;
create temporary table recovered_claim as select * from hsr.claim_scan_job('62000000-0000-0000-0000-000000000002');
select is((select hsr.block_scan_job(c.organization_id,c.job_id,'62000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,'WORKER_STOP_REQUESTED') from recovered_claim c),true,
    'ExpiredConcurrentAttemptRemainsRecoverableByNewOwner');
reset role;
select * from finish();
