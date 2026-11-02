-- Disposable local database only. Privileged connections coordinate real lock
-- waits; the worker connection executes every operation as hsr_worker.
create extension if not exists dblink with schema extensions;
set search_path=extensions,public;
select no_plan();
insert into auth.users(id) values('87000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('87100000-0000-0000-0000-000000000001','Concurrent public source');
insert into hsr.memberships values('87100000-0000-0000-0000-000000000001','87000000-0000-0000-0000-000000000001');
set request.jwt.claim.sub='87000000-0000-0000-0000-000000000001';
create temporary table public_concurrent_job as select * from hsr.create_public_scan(
    '87100000-0000-0000-0000-000000000001','87200000-0000-0000-0000-000000000001','https://example.com/','example.com');
select dblink_connect('public_lock','dbname=postgres user=supabase_admin application_name=hsr_public_lock');
select dblink_connect('public_worker','dbname=postgres user=supabase_admin application_name=hsr_public_worker');
select dblink_exec('public_lock','set role hsr_worker');
select dblink_exec('public_worker','set role hsr_worker');
select dblink_exec('public_lock','begin');
create temporary table public_first_claim as select body from dblink('public_lock',
    $$select hsr.claim_public_scan('87300000-0000-0000-0000-000000000001')$$) as t(body jsonb);
select is((select body->'job'->>'JobId' from public_first_claim),(select job_id::text from public_concurrent_job),
    'PublicConcurrentFirstClaimBindsActualJob');
select dblink_send_query('public_worker',$$select hsr.claim_public_scan('87300000-0000-0000-0000-000000000002')$$);
do $$declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';begin
    loop
        perform pg_stat_clear_snapshot();
        exit when dblink_is_busy('public_worker')=0 or exists(select 1 from pg_stat_activity
            where application_name='hsr_public_worker' and wait_event_type='Lock') or clock_timestamp()>=deadline;
        perform pg_sleep(0.01);
    end loop;
end $$;
select ok(dblink_is_busy('public_worker')=0 or exists(select 1 from pg_stat_activity
    where application_name='hsr_public_worker' and wait_event_type='Lock'),'PublicConcurrentSecondWorkerActuallyArrives');
select dblink_exec('public_lock','commit');
create temporary table public_second_claim as select body from dblink_get_result('public_worker') as t(body jsonb);
select ok((select body->>'status'='Empty' and not body ? 'token' from public_second_claim),'PublicConcurrentSecondWorkerHasNoCapability');
select is((select count(*) from hsr.public_scan_attempts where job_id=(select job_id from public_concurrent_job)),1::bigint,
    'PublicConcurrentClaimsCreateOneAttempt');
select count(*) from dblink_get_result('public_worker') as t(body jsonb);

select dblink_exec('public_lock','reset role');
select dblink_exec('public_lock',$$set request.jwt.claim.sub='87000000-0000-0000-0000-000000000001'$$);
select dblink_exec('public_lock','begin');
select dblink_exec('public_lock',format('update hsr.public_scan_jobs set state=state where id=%L::uuid',(select job_id from public_concurrent_job)));
select pgmq.set_vt('hsr_public_scan_jobs',(select queue_message_id from hsr.public_scan_jobs where id=(select job_id from public_concurrent_job)),0);
select dblink_send_query('public_worker',$$select hsr.claim_public_scan('87300000-0000-0000-0000-000000000002')$$);
do $$declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';begin
    while dblink_is_busy('public_worker')<>0 and clock_timestamp()<deadline loop perform pg_sleep(0.01);end loop;
end $$;
select is(dblink_is_busy('public_worker'),0,'PublicBusyKnownJobDoesNotWaitForOwnedRow');
select dblink_exec('public_lock','commit');
create temporary table public_busy_claim as select body from dblink_get_result('public_worker') as t(body jsonb);
select ok((select body->>'status'='Deferred' and not body ? 'token' from public_busy_claim),'PublicBusyKnownJobDefersWithoutCapability');
select is((select count(*) from pgmq.q_hsr_public_scan_jobs),1::bigint,'PublicBusyKnownJobRetainsQueueMessage');
select is((select count(*) from pgmq.a_hsr_public_scan_jobs),0::bigint,'PublicBusyKnownJobIsNeverArchivedAsUnknown');
select is((select count(*) from hsr.public_scan_attempts),1::bigint,'PublicBusyKnownJobCannotCreateAnotherAttempt');
select count(*) from dblink_get_result('public_worker') as t(body jsonb);
update hsr.public_scan_leases set expires_at=clock_timestamp()-interval '1 second';

create temporary table public_lock_outcomes(operation text,actually_waited boolean,accepted boolean,unchanged boolean,queue_preserved boolean,missing_queue_rejected boolean);
do $$
declare operation text;job record;claim jsonb;payload text;query text;deadline timestamptz;waited boolean;accepted boolean;unchanged boolean;preserved boolean;missing boolean;
begin
    foreach operation in array array['Renew','Finish','Block'] loop
        -- Setup changes must commit on the other connection. Writes in this DO
        -- transaction would remain invisible to the actual worker connection.
        select * into job from dblink('public_lock',format('select * from hsr.create_public_scan(%L::uuid,%L::uuid,%L,%L)',
            '87100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com/','example.com')) as t(scan_id uuid,job_id uuid);
        select body into claim from dblink('public_lock',$q$select hsr.claim_public_scan('87300000-0000-0000-0000-000000000001')$q$) as t(body jsonb);
        if claim->>'status'<>'Claimed' or claim->'job'->>'JobId'<>job.job_id::text then raise exception 'Concurrent public setup unconfirmed';end if;
        payload:=jsonb_build_object('Schema','hsr-public-website-observation-v1','Job',claim->'job','WorkerId','87300000-0000-0000-0000-000000000001',
            'ReceivedAt',clock_timestamp(),'NativeShutdownConfirmed',true,'ResourcesAbsenceConfirmed',true,'DispatchedInputs',0,'GoalVerified',false,
            'Captures',jsonb_build_array(jsonb_build_object('DocumentId','synthetic-lock-test','CaptureEpoch',1,'DocumentUrl','https://example.com/','Nodes',
                jsonb_build_array(jsonb_build_object('Id','n1','Role','RootWebArea','Name','Synthetic database fixture'))),
                jsonb_build_object('DocumentId','synthetic-lock-test','CaptureEpoch',1,'DocumentUrl','https://example.com/','Nodes',
                jsonb_build_array(jsonb_build_object('Id','n1','Role','RootWebArea','Name','Synthetic database fixture')))))::text;
        query:=case operation
            when 'Renew' then format('select hsr.renew_public_scan(%L::uuid,%L::uuid,1,%L::uuid,%L::uuid)',
                '87100000-0000-0000-0000-000000000001',job.job_id,'87300000-0000-0000-0000-000000000001',claim->>'token')
            when 'Finish' then format('select hsr.finish_public_scan(%L::uuid,%L::uuid,1,%L::uuid,%L::uuid,%L,%L)',
                '87100000-0000-0000-0000-000000000001',job.job_id,'87300000-0000-0000-0000-000000000001',claim->>'token',payload,encode(sha256(convert_to(payload,'UTF8')),'hex'))
            else format('select hsr.block_public_scan(%L::uuid,%L::uuid,1,%L::uuid,%L::uuid,%L)',
                '87100000-0000-0000-0000-000000000001',job.job_id,'87300000-0000-0000-0000-000000000001',claim->>'token','PUBLIC_WEBSITE_SOURCE_UNCONFIRMED') end;
        perform dblink_exec('public_lock',format('update hsr.public_scan_leases set expires_at=clock_timestamp()+interval ''2 seconds'' where job_id=%L::uuid',job.job_id));
        select expires_at into deadline from dblink('public_lock',format('select expires_at from hsr.public_scan_leases where job_id=%L::uuid',job.job_id)) as t(expires_at timestamptz);
        perform dblink_exec('public_lock','begin');
        perform dblink_exec('public_lock',format('update pgmq.q_hsr_public_scan_jobs set vt=vt where msg_id=(select queue_message_id from hsr.public_scan_jobs where id=%L::uuid)',job.job_id));
        perform dblink_send_query('public_worker',query);
        loop
            perform pg_stat_clear_snapshot();
            waited:=exists(select 1 from pg_stat_activity where application_name='hsr_public_worker' and wait_event_type='Lock');
            exit when waited or clock_timestamp()>=deadline;
            perform pg_sleep(0.01);
        end loop;
        while clock_timestamp()<deadline loop perform pg_sleep(0.01);end loop;
        perform dblink_exec('public_lock','commit');
        select value into accepted from dblink_get_result('public_worker') as t(value boolean);
        perform value from dblink_get_result('public_worker') as t(value boolean);
        select state='leased' and result_text is null and result_digest is null and result_worker_id is null into unchanged from hsr.public_scan_jobs where id=job.job_id;
        select exists(select 1 from pgmq.q_hsr_public_scan_jobs where msg_id=(select queue_message_id from hsr.public_scan_jobs where id=job.job_id)) into preserved;
        perform dblink_exec('public_lock',format('update hsr.public_scan_leases set expires_at=clock_timestamp()+interval ''30 seconds'' where job_id=%L::uuid',job.job_id));
        perform dblink_exec('public_lock',format('delete from pgmq.q_hsr_public_scan_jobs where msg_id=(select queue_message_id from hsr.public_scan_jobs where id=%L::uuid)',job.job_id));
        select not value into missing from dblink('public_worker',query) as t(value boolean);
        unchanged:=unchanged and (select state='leased' and result_text is null and result_digest is null and result_worker_id is null from hsr.public_scan_jobs where id=job.job_id);
        insert into public_lock_outcomes values(operation,waited,accepted,unchanged,preserved,missing);
        perform dblink_exec('public_lock',format('update hsr.public_scan_leases set expires_at=clock_timestamp()-interval ''1 second'' where job_id=%L::uuid',job.job_id));
    end loop;
end $$;
select ok(actually_waited,'Public'||operation||'ActuallyWaitsForQueueRow') from public_lock_outcomes order by operation;
select is(accepted,false,'Public'||operation||'RechecksExpiryAfterQueueWait') from public_lock_outcomes order by operation;
select ok(unchanged,'Public'||operation||'CannotPublishOrRetireAfterExpiryOrMissingQueue') from public_lock_outcomes order by operation;
select ok(queue_preserved,'Public'||operation||'PreservesMessageAfterExpiredWait') from public_lock_outcomes order by operation;
select ok(missing_queue_rejected,'Public'||operation||'RejectsMissingQueueMessage') from public_lock_outcomes order by operation;
select dblink_disconnect('public_lock');select dblink_disconnect('public_worker');
delete from hsr.public_scan_leases where organization_id='87100000-0000-0000-0000-000000000001';
delete from hsr.public_scan_attempts where organization_id='87100000-0000-0000-0000-000000000001';
delete from hsr.public_scan_jobs where organization_id='87100000-0000-0000-0000-000000000001';
delete from hsr.public_scans where organization_id='87100000-0000-0000-0000-000000000001';
delete from hsr.memberships where organization_id='87100000-0000-0000-0000-000000000001';
delete from hsr.organizations where id='87100000-0000-0000-0000-000000000001';
delete from auth.users where id='87000000-0000-0000-0000-000000000001';
select * from finish();
