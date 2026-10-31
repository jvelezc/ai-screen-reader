create extension if not exists dblink with schema extensions;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker;
select plan(8);
insert into auth.users(id) values('81000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('81000000-0000-0000-0000-000000000003','Concurrent publication fixture');
insert into hsr.memberships values('81000000-0000-0000-0000-000000000003','81000000-0000-0000-0000-000000000001');
set request.jwt.claim.sub='81000000-0000-0000-0000-000000000001';
create temporary table publication_job as select * from hsr.create_recorded_traversal('81000000-0000-0000-0000-000000000003','81000000-0000-0000-0000-000000000004',1);
grant select on publication_job to hsr_worker;
set role hsr_worker;
create temporary table execution as select * from hsr.claim_scan_job_with_traversal('81000000-0000-0000-0000-000000000007',60);
reset role;
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from publication_job);
select dblink_connect('publication_first','dbname=postgres user=supabase_admin application_name=hsr_publication_first');
select dblink_connect('publication_second','dbname=postgres user=supabase_admin application_name=hsr_publication_second');
select dblink_exec('publication_first','set role hsr_worker');
select dblink_exec('publication_second','set role hsr_worker');
select dblink_exec('publication_first','begin');
create temporary table first_publication as select x.* from publication_job r cross join lateral dblink('publication_first',format(
    'select * from hsr.claim_recorded_publication(%L::uuid,%L::uuid,%L::uuid,1,%L::uuid,%L,%L::uuid,60)',
    '81000000-0000-0000-0000-000000000003',r.scan_id,r.job_id,'81000000-0000-0000-0000-000000000007',repeat('a',64),'81000000-0000-0000-0000-000000000011'))
    as x(status text,lease_until timestamptz,record_observation boolean,traversal_route integer);
select is((select status from first_publication),'Claimed','ConcurrentPublicationFirstClaimOwnsOnlyStorage');
select dblink_send_query('publication_second',format(
    'select * from hsr.claim_recorded_publication(%L::uuid,%L::uuid,%L::uuid,1,%L::uuid,%L,%L::uuid,60)',
    '81000000-0000-0000-0000-000000000003',r.scan_id,r.job_id,'81000000-0000-0000-0000-000000000007',repeat('a',64),'81000000-0000-0000-0000-000000000012')) from publication_job r;
do $$declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';begin
    loop
        perform pg_stat_clear_snapshot();
        exit when exists(select 1 from pg_stat_activity where application_name='hsr_publication_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;perform pg_sleep(0.01);
    end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_publication_second' and wait_event_type='Lock'),'ConcurrentPublicationActuallyWaitsForTheOriginalJobLock');
select dblink_exec('publication_first','commit');
create temporary table second_publication as select * from dblink_get_result('publication_second') as x(status text,lease_until timestamptz,record_observation boolean,traversal_route integer);
select is((select status from second_publication),'Busy','ConcurrentPublicationCannotIssueASecondStorageCapability');
select count(*) from dblink_get_result('publication_second') as x(status text,lease_until timestamptz,record_observation boolean,traversal_route integer);
select ok((select count(*)=1 from hsr.recorded_publications p join publication_job r on r.job_id=p.job_id)
    and (select count(*)=1 from hsr.job_attempts a join publication_job r on r.job_id=a.job_id),'ConcurrentPublicationPreservesExactlyOneOriginalInputAttempt');
select dblink_exec('publication_first','reset role');
select dblink_exec('publication_first','begin');
select dblink_exec('publication_first',format('update hsr.scan_jobs set state=state where id=%L::uuid',r.job_id)) from publication_job r;
select dblink_exec('publication_first',format('update hsr.recorded_publications set expires_at=clock_timestamp()+interval ''2 seconds'' where job_id=%L::uuid',r.job_id)) from publication_job r;
create temporary table publication_deadline as select * from dblink('publication_first','select expires_at from hsr.recorded_publications where organization_id=''81000000-0000-0000-0000-000000000003''') as x(expires_at timestamptz);
select dblink_send_query('publication_second',format('select hsr.renew_recorded_publication(%L::uuid,%L::uuid,1,%L::uuid,%L,%L::uuid,60)',
    '81000000-0000-0000-0000-000000000003',r.job_id,'81000000-0000-0000-0000-000000000007',repeat('a',64),'81000000-0000-0000-0000-000000000011')) from publication_job r;
do $$declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';begin
    loop
        perform pg_stat_clear_snapshot();
        exit when exists(select 1 from pg_stat_activity where application_name='hsr_publication_second' and wait_event_type='Lock');
        exit when clock_timestamp()>=deadline;perform pg_sleep(0.01);
    end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity where application_name='hsr_publication_second' and wait_event_type='Lock'),'PublicationRenewalActuallyWaitsBeforeCheckingTheServerClock');
do $$begin while clock_timestamp()<(select expires_at from publication_deadline) loop perform pg_sleep(0.01);end loop;end $$;
select dblink_exec('publication_first','commit');
select is((select renewed from dblink_get_result('publication_second') as x(renewed boolean)),false,'PublicationRenewalCannotUseAPrelockTimestampToReviveExpiredAuthority');
select count(*) from dblink_get_result('publication_second') as x(renewed boolean);
select ok((select j.state='blocked' and j.generation=1 and j.archive_index_sha256 is null from hsr.scan_jobs j join publication_job r on r.job_id=j.id),'ExpiredPublicationDoesNotPromoteTheOriginalJob');
set role hsr_worker;
select is((select status from publication_job r cross join lateral hsr.claim_recorded_publication('81000000-0000-0000-0000-000000000003',r.scan_id,r.job_id,1,'81000000-0000-0000-0000-000000000007',repeat('a',64),'81000000-0000-0000-0000-000000000013',60)),'Claimed','PublicationCanReclaimOnlyTheSameOriginalAfterExpiry');
reset role;
select dblink_disconnect('publication_first');select dblink_disconnect('publication_second');
select * from finish();
