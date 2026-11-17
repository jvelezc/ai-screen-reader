-- Owned disposable database only. Independent sessions prove lock ordering and clock-bound authority.
set search_path=extensions,public;
select plan(10);
insert into auth.users(id) values('7c5b1000-0000-0000-0000-000000000001');
insert into auth.sessions(id,user_id,not_after) values
 ('7c5b1100-0000-0000-0000-000000000001','7c5b1000-0000-0000-0000-000000000001',clock_timestamp()+interval '1 hour');
insert into hsr.organizations(id,name) values('7c5b1200-0000-0000-0000-000000000001','Concurrent set test');
insert into hsr.memberships values('7c5b1200-0000-0000-0000-000000000001','7c5b1000-0000-0000-0000-000000000001');
set request.jwt.claim.sub='7c5b1000-0000-0000-0000-000000000001';
set request.jwt.claims='{"sub":"7c5b1000-0000-0000-0000-000000000001","session_id":"7c5b1100-0000-0000-0000-000000000001"}';
create temporary table concurrent_set_parent as select * from hsr.create_public_scan(
 '7c5b1200-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com/','example.com');
update hsr.public_scan_jobs set state='completed',result_text='{}',result_digest=repeat('a',64),result_worker_id=gen_random_uuid()
 where id=(select job_id from concurrent_set_parent);
select dblink_connect('hsr_set_first','dbname=postgres user=postgres application_name=hsr_set_first');
select dblink_connect('hsr_set_second','dbname=postgres user=postgres application_name=hsr_set_second');
select dblink_exec('hsr_set_first','begin');
select dblink_exec(connection,'set role authenticated') from unnest(array['hsr_set_first','hsr_set_second']) connection;
select dblink_exec(connection,$$set request.jwt.claim.sub='7c5b1000-0000-0000-0000-000000000001'$$)
 from unnest(array['hsr_set_first','hsr_set_second']) connection;
select dblink_exec(connection,$$set request.jwt.claims='{"sub":"7c5b1000-0000-0000-0000-000000000001","session_id":"7c5b1100-0000-0000-0000-000000000001"}'$$)
 from unnest(array['hsr_set_first','hsr_set_second']) connection;
create temporary table concurrent_set_sql as select format(
 $query$select hsr.create_public_task_set('7c5b1200-0000-0000-0000-000000000001',%L::uuid,
 '7c5b1300-0000-0000-0000-000000000001',repeat('a',64),4,array['Pricing','Catalog','Contact','Help'])$query$,
 scan_id) query from concurrent_set_parent;
create temporary table concurrent_set_first as select * from dblink('hsr_set_first',(select query from concurrent_set_sql)) as t(body jsonb);
select dblink_send_query('hsr_set_second',(select query from concurrent_set_sql));
do $$declare deadline timestamptz:=clock_timestamp()+interval '5 seconds'; begin
 loop
  perform pg_stat_clear_snapshot();
  exit when exists(select 1 from pg_stat_activity where application_name='hsr_set_second' and wait_event_type='Lock');
  exit when clock_timestamp()>=deadline;
  perform pg_sleep(0.01);
 end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_set_second' and wait_event_type='Lock'),
 'ConcurrentSetAdmissionWaitsForTheOriginalTransaction');
select dblink_exec('hsr_set_first','commit');
create temporary table concurrent_set_second as select * from dblink_get_result('hsr_set_second') as t(body jsonb);
select is((select body->>'SetId' from concurrent_set_first),(select body->>'SetId' from concurrent_set_second),
 'ConcurrentSetRetryReturnsTheSameCommittedSet');
select is((select body->'Tasks' from concurrent_set_first),(select body->'Tasks' from concurrent_set_second),
 'ConcurrentSetRetryPreservesEveryOriginalChild');
select is((select count(*)::integer from hsr.public_task_sets where organization_id='7c5b1200-0000-0000-0000-000000000001'),1,
 'ConcurrentSetRetryCreatesExactlyOneSet');
select is((select count(*)::integer from hsr.public_tasks where organization_id='7c5b1200-0000-0000-0000-000000000001'),4,
 'ConcurrentSetRetryCreatesExactlyFourJobs');
select is((select count(*)::integer from pgmq.q_hsr_public_task_jobs where message->>'organizationId'='7c5b1200-0000-0000-0000-000000000001'),4,
 'ConcurrentSetRetryLeavesExactlyFourQueueMessages');
select count(*) from dblink_get_result('hsr_set_second') as t(body jsonb);
select dblink_exec('hsr_set_second','reset role');
select dblink_exec('hsr_set_second',$fn$
 create function pg_temp.try_expiring_set(parent uuid) returns text language plpgsql as $body$
 begin
  perform hsr.create_public_task_set('7c5b1200-0000-0000-0000-000000000001',parent,
   '7c5b1300-0000-0000-0000-000000000002',repeat('a',64),1,array['Help']);
  return 'ADMITTED';
 exception when insufficient_privilege then return sqlstate||':'||sqlerrm;
 end $body$;
 $fn$);
select dblink_exec('hsr_set_second','set role authenticated');
select dblink_exec('hsr_set_first','begin');
select count(*) from dblink('hsr_set_first',
 $$select pg_advisory_xact_lock(hashtextextended('hsr-public-task-admission:7c5b1200-0000-0000-0000-000000000001',0))::text$$) as t(locked text);
update auth.sessions set not_after=clock_timestamp()+interval '1 second' where id='7c5b1100-0000-0000-0000-000000000001';
select dblink_send_query('hsr_set_second',format('select pg_temp.try_expiring_set(%L::uuid)',scan_id)) from concurrent_set_parent;
do $$declare deadline timestamptz:=clock_timestamp()+interval '5 seconds'; begin
 loop
  perform pg_stat_clear_snapshot();
  exit when exists(select 1 from pg_stat_activity where application_name='hsr_set_second' and wait_event_type='Lock');
  exit when clock_timestamp()>=deadline;
  perform pg_sleep(0.01);
 end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_set_second' and wait_event_type='Lock'),
 'ExpiringSetCallerActuallyWaitsOnAdmissionLock');
do $$begin
 loop
  exit when clock_timestamp()>(select not_after from auth.sessions where id='7c5b1100-0000-0000-0000-000000000001');
  perform pg_sleep(0.01);
 end loop;
end $$;
select dblink_exec('hsr_set_first','commit');
create temporary table expiring_set_result as select * from dblink_get_result('hsr_set_second') as t(result text);
select is((select result from expiring_set_result),'42501:HSR_SESSION_REQUIRED',
 'SetAdmissionRechecksActualSessionExpiryAfterTheLock');
select is((select count(*)::integer from hsr.public_task_sets where organization_id='7c5b1200-0000-0000-0000-000000000001'),1,
 'ExpiredWaitingCallerCreatesNoReplacementSet');
select is((select count(*)::integer from hsr.public_tasks where organization_id='7c5b1200-0000-0000-0000-000000000001'),4,
 'ExpiredWaitingCallerCreatesNoAdditionalJob');
select count(*) from dblink_get_result('hsr_set_second') as t(result text);
select dblink_disconnect('hsr_set_first');
select dblink_disconnect('hsr_set_second');
select * from finish();
