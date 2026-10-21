create extension if not exists dblink with schema extensions;
set search_path = extensions,public;
select plan(6);
insert into auth.users(id) values ('80000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values ('80000000-0000-0000-0000-000000000002','Concurrent admission');
insert into hsr.memberships values ('80000000-0000-0000-0000-000000000002','80000000-0000-0000-0000-000000000001');
-- The harness uses the disposable image's supabase_admin only to open local
-- Unix-socket test sessions. Both admission calls below run as authenticated.
select dblink_connect('hsr_first','dbname=postgres user=postgres application_name=hsr_first');
select dblink_connect('hsr_second','dbname=postgres user=postgres application_name=hsr_second');
select dblink_exec('hsr_first','begin');
select dblink_exec('hsr_first','set role authenticated');
select dblink_exec('hsr_first',$$set request.jwt.claim.sub='80000000-0000-0000-0000-000000000001'$$);
select dblink_exec('hsr_second','set role authenticated');
select dblink_exec('hsr_second',$$set request.jwt.claim.sub='80000000-0000-0000-0000-000000000001'$$);
create temporary table first_concurrent as
    select * from dblink('hsr_first',$$select * from hsr.create_scan(
        '80000000-0000-0000-0000-000000000002','80000000-0000-0000-0000-000000000003')$$)
    as t(scan_id uuid,job_id uuid,queue_message_id bigint);
select dblink_send_query('hsr_second',$$select * from hsr.create_scan(
    '80000000-0000-0000-0000-000000000002','80000000-0000-0000-0000-000000000003')$$);
do $$
declare deadline timestamptz := clock_timestamp()+interval '5 seconds';
begin
    loop
        perform pg_stat_clear_snapshot();
        exit when exists (select 1 from pg_stat_activity
            where application_name='hsr_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;
        perform pg_sleep(0.01);
    end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity
    where application_name='hsr_second' and wait_event_type='Lock'),
    'ConcurrentDuplicateWaitsForUncommittedAdmission');
select dblink_exec('hsr_first','commit');
create temporary table second_concurrent as select * from dblink_get_result('hsr_second')
    as t(scan_id uuid,job_id uuid,queue_message_id bigint);
select results_eq('select * from first_concurrent','select * from second_concurrent',
    'ConcurrentDuplicateReturnsSameCommittedReceipt');
select is((select count(*) from pgmq.q_hsr_scan_jobs where
    message->>'organizationId'='80000000-0000-0000-0000-000000000002'),1::bigint,
    'ConcurrentDuplicateCommitsOnlyOneQueueMessage');
-- Drain the asynchronous connection's final empty result before reusing it.
select count(*) from dblink_get_result('hsr_second')
    as t(scan_id uuid,job_id uuid,queue_message_id bigint);
select dblink_exec('hsr_first','begin');
select * from dblink('hsr_first',$$select * from hsr.create_scan(
    '80000000-0000-0000-0000-000000000002','80000000-0000-0000-0000-000000000003')$$)
    as t(scan_id uuid,job_id uuid,queue_message_id bigint);
select dblink_exec('hsr_second','reset role');
select dblink_send_query('hsr_second',$$delete from hsr.memberships
    where organization_id='80000000-0000-0000-0000-000000000002'
    and user_id='80000000-0000-0000-0000-000000000001' returning user_id$$);
do $$
declare deadline timestamptz := clock_timestamp()+interval '5 seconds';
begin
    loop
        perform pg_stat_clear_snapshot();
        exit when exists (select 1 from pg_stat_activity
            where application_name='hsr_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;
        perform pg_sleep(0.01);
    end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity
    where application_name='hsr_second' and wait_event_type='Lock'),
    'MembershipRevocationWaitsForAdmittedTransaction');
select dblink_exec('hsr_first','commit');
select results_eq($$select user_id::text from dblink_get_result('hsr_second') as t(user_id uuid)$$,
    $$values ('80000000-0000-0000-0000-000000000001'::text)$$,
    'WaitingRevocationRemovesExactMembership');
select throws_ok($test$select * from dblink('hsr_first',$query$select * from hsr.create_scan(
    '80000000-0000-0000-0000-000000000002','80000000-0000-0000-0000-000000000003')$query$)
    as t(scan_id uuid,job_id uuid,queue_message_id bigint)$test$,
    '42501','HSR_MEMBERSHIP_REQUIRED','CommittedRevocationRejectsPriorRequest');
select dblink_disconnect('hsr_first');
select dblink_disconnect('hsr_second');
select * from finish();
