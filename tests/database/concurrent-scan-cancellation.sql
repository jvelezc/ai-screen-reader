create extension if not exists dblink with schema extensions;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated;
select plan(15);
insert into auth.users(id) values('7c000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('7c000000-0000-0000-0000-000000000010','Concurrent cancellation fixture');
insert into hsr.memberships values('7c000000-0000-0000-0000-000000000010','7c000000-0000-0000-0000-000000000001');
set request.jwt.claim.sub='7c000000-0000-0000-0000-000000000001';
select dblink_connect('cancel_first','dbname=postgres user=supabase_admin application_name=hsr_cancel_first');
select dblink_connect('cancel_second','dbname=postgres user=supabase_admin application_name=hsr_cancel_second');
select dblink_exec('cancel_first','set statement_timeout=''10s''');
select dblink_exec('cancel_second','set statement_timeout=''10s''');
select dblink_exec('cancel_first','set request.jwt.claim.sub=''7c000000-0000-0000-0000-000000000001''');
select dblink_exec('cancel_second','set request.jwt.claim.sub=''7c000000-0000-0000-0000-000000000001''');
create function pg_temp.wait_cancel_second() returns void language plpgsql as $$
declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';begin
    loop
        perform pg_stat_clear_snapshot();
        exit when exists(select 1 from pg_stat_activity where application_name='hsr_cancel_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;perform pg_sleep(0.01);
    end loop;
end $$;

-- Cancellation owns the job lock first. An already-dispatched completion must wait and fail.
set role authenticated;
create temporary table cancel_a_job as select * from hsr.create_scan('7c000000-0000-0000-0000-000000000010','7c000000-0000-0000-0000-000000000101');
grant select on cancel_a_job to hsr_worker;
set role hsr_worker;
create temporary table cancel_a_lease as select * from hsr.claim_scan_job('7c000000-0000-0000-0000-000000000201',60);
reset role;
select dblink_exec('cancel_first','set role authenticated');select dblink_exec('cancel_second','set role hsr_worker');
select dblink_exec('cancel_first','begin');
create temporary table cancel_a_request as select x.* from cancel_a_job j cross join lateral dblink('cancel_first',format(
    'select * from hsr.request_scan_cancellation(%L::uuid,%L::uuid)','7c000000-0000-0000-0000-000000000010',j.scan_id))
    as x(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,producer_id uuid,status text,input_assessment text,requested_at timestamptz);
select ok((select status='Requested' and generation=1 and input_assessment='NotAssessed' from cancel_a_request),'ConcurrentCancellationLocksTheActualClaimedAttempt');
select dblink_send_query('cancel_second',format('select hsr.complete_scan_job(%L::uuid,%L::uuid,%L::uuid,1,%L::uuid,%L,%L)',
    c.organization_id,c.job_id,'7c000000-0000-0000-0000-000000000201',c.lease_token,repeat('a',64),
    replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json')) from cancel_a_lease c;
select pg_temp.wait_cancel_second();
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_cancel_second' and wait_event_type='Lock'),'ExecutionCompletionActuallyWaitsForCancellationCommit');
select dblink_exec('cancel_first','commit');
select is((select completed from dblink_get_result('cancel_second') as x(completed boolean)),false,'WaitingExecutionCompletionCannotOverrideCancellation');
select count(*) from dblink_get_result('cancel_second') as x(completed boolean);
select ok((select j.state='blocked' and j.generation=1 and j.cancellation_requested_at is not null and j.result_digest is null
    from hsr.scan_jobs j join cancel_a_job r on r.job_id=j.id)
    and (select count(*)=1 from hsr.job_attempts a join cancel_a_job r on r.job_id=a.job_id),'CancellationRacePreservesOneAttemptWithoutCompletionOrEvidence');

-- An actual committed completion wins if it owns the lock first; cancellation cannot rewrite it.
set role authenticated;
create temporary table cancel_b_job as select * from hsr.create_scan('7c000000-0000-0000-0000-000000000010','7c000000-0000-0000-0000-000000000102');
grant select on cancel_b_job to hsr_worker;
set role hsr_worker;
create temporary table cancel_b_lease as select * from hsr.claim_scan_job('7c000000-0000-0000-0000-000000000202',60);
reset role;
select dblink_exec('cancel_first','set role hsr_worker');select dblink_exec('cancel_second','set role authenticated');
select dblink_exec('cancel_first','begin');
select ok((select completed from cancel_b_lease c cross join lateral dblink('cancel_first',format(
    'select hsr.complete_scan_job(%L::uuid,%L::uuid,%L::uuid,1,%L::uuid,%L,%L)',c.organization_id,c.job_id,
    '7c000000-0000-0000-0000-000000000202',c.lease_token,repeat('b',64),
    replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json')) as x(completed boolean)),
    'ConcurrentCompletionOwnsItsOriginalReceiptBeforeCancellation');
select dblink_send_query('cancel_second',format('select * from hsr.request_scan_cancellation(%L::uuid,%L::uuid)',
    '7c000000-0000-0000-0000-000000000010',r.scan_id)) from cancel_b_job r;
select pg_temp.wait_cancel_second();
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_cancel_second' and wait_event_type='Lock'),'CancellationActuallyWaitsForOriginalCompletionCommit');
select dblink_exec('cancel_first','commit');
create temporary table cancel_b_request as select * from dblink_get_result('cancel_second')
    as x(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,producer_id uuid,status text,input_assessment text,requested_at timestamptz);
select ok((select status='AlreadyCompleted' and requested_at is null and input_assessment='NotAssessed' from cancel_b_request),'CompletedRaceReturnsNoopWithoutInventingAStopOrZeroInput');
select count(*) from dblink_get_result('cancel_second')
    as x(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,producer_id uuid,status text,input_assessment text,requested_at timestamptz);
select ok((select j.state='completed' and j.cancellation_requested_at is null and j.result_digest=repeat('b',64)
    from hsr.scan_jobs j join cancel_b_job r on r.job_id=j.id),'CompletionRacePreservesTheExactOriginalDigestAndState');

-- A separately issued storage capability must also be fenced after waiting on cancellation.
set role authenticated;
create temporary table cancel_c_job as select * from hsr.create_recorded_traversal('7c000000-0000-0000-0000-000000000010','7c000000-0000-0000-0000-000000000103',1);
grant select on cancel_c_job to hsr_worker;
set role hsr_worker;
create temporary table cancel_c_lease as select * from hsr.claim_scan_job_with_traversal('7c000000-0000-0000-0000-000000000203',60);
reset role;
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from cancel_c_job);
set role hsr_worker;
select is((select status from cancel_c_job r cross join lateral hsr.claim_recorded_publication('7c000000-0000-0000-0000-000000000010',r.scan_id,r.job_id,1,
    '7c000000-0000-0000-0000-000000000203',repeat('c',64),'7c000000-0000-0000-0000-000000000303',60)),'Claimed','PublicationRaceStartsWithARealSeparateStorageCapability');
reset role;
select dblink_exec('cancel_first','set role authenticated');select dblink_exec('cancel_second','set role hsr_worker');
select dblink_exec('cancel_first','begin');
select count(*) from cancel_c_job j cross join lateral dblink('cancel_first',format('select * from hsr.request_scan_cancellation(%L::uuid,%L::uuid)',
    '7c000000-0000-0000-0000-000000000010',j.scan_id))
    as x(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,producer_id uuid,status text,input_assessment text,requested_at timestamptz);
select dblink_send_query('cancel_second',format('select hsr.complete_recorded_publication(%L::uuid,%L::uuid,1,%L::uuid,%L,%L::uuid,%L,%L,%L,%L,20000)',
    '7c000000-0000-0000-0000-000000000010',r.job_id,'7c000000-0000-0000-0000-000000000203',repeat('c',64),'7c000000-0000-0000-0000-000000000303',
    repeat('d',64),replace('7c000000-0000-0000-0000-000000000010','-','')||'/'||replace(r.scan_id::text,'-','')||'/'||replace(r.job_id::text,'-','')||'/1/observation.json',
    repeat('e',64),repeat('f',64))) from cancel_c_job r;
select pg_temp.wait_cancel_second();
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_cancel_second' and wait_event_type='Lock'),'PublicationCompletionActuallyWaitsForCancellationCommit');
select dblink_exec('cancel_first','commit');
select is((select completed from dblink_get_result('cancel_second') as x(completed boolean)),false,'WaitingPublicationCapabilityCannotCompleteACancelledRun');
select count(*) from dblink_get_result('cancel_second') as x(completed boolean);
select ok((select j.state='blocked' and j.archive_index_sha256 is null and j.cancellation_requested_at is not null from hsr.scan_jobs j join cancel_c_job r on r.job_id=j.id)
    and not exists(select 1 from hsr.recorded_publication_events e join cancel_c_job r on r.job_id=e.job_id where e.kind='Completed'),
    'PublicationRaceCannotCreateAnArchiveOrCompletionReceipt');

-- Original-user membership is held to the commit boundary, then fresh revocation takes effect.
select dblink_exec('cancel_first','begin');
select count(*) from cancel_a_job j cross join lateral dblink('cancel_first',format('select * from hsr.request_scan_cancellation(%L::uuid,%L::uuid)',
    '7c000000-0000-0000-0000-000000000010',j.scan_id))
    as x(organization_id uuid,scan_id uuid,job_id uuid,generation bigint,producer_id uuid,status text,input_assessment text,requested_at timestamptz);
select dblink_exec('cancel_second','reset role');
select dblink_send_query('cancel_second',$$delete from hsr.memberships where organization_id='7c000000-0000-0000-0000-000000000010' and user_id='7c000000-0000-0000-0000-000000000001' returning user_id$$);
select pg_temp.wait_cancel_second();
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_cancel_second' and wait_event_type='Lock'),'CancellationHoldsOriginalMembershipUntilItsTransactionCommits');
select dblink_exec('cancel_first','commit');
select is((select user_id from dblink_get_result('cancel_second') as x(user_id uuid)),'7c000000-0000-0000-0000-000000000001'::uuid,'WaitingRevocationRemovesOnlyTheOriginalMembership');
select count(*) from dblink_get_result('cancel_second') as x(user_id uuid);
set role authenticated;
select throws_ok(format('select * from hsr.request_scan_cancellation(%L::uuid,%L::uuid)','7c000000-0000-0000-0000-000000000010',(select scan_id from cancel_a_job)),
    '42501',null,'FreshCancellationRejectsTheCommittedMembershipRevocation');
reset role;
select dblink_disconnect('cancel_first');select dblink_disconnect('cancel_second');
select * from finish();
