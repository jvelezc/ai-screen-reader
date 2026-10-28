begin;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated;
select plan(14);
insert into auth.users(id) values ('78000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values ('78000000-0000-0000-0000-000000000003','Interrupted keyboard proof');
insert into hsr.memberships values ('78000000-0000-0000-0000-000000000003','78000000-0000-0000-0000-000000000001');
select set_config('request.jwt.claim.sub','78000000-0000-0000-0000-000000000001',true);
set local role authenticated;
create temporary table recorded as select * from hsr.create_recorded_traversal('78000000-0000-0000-0000-000000000003','78000000-0000-0000-0000-000000000004',1);
reset role;
set local role hsr_worker;
create temporary table original_claim as select * from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000007',5);
select is((select status from original_claim),'Claimed','ReplayFenceAllowsFirstExplicitKeyboardAttempt');
reset role;
create temporary table original_attempt as select to_jsonb(a) as value from hsr.job_attempts a join recorded r on r.job_id=a.job_id;
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from recorded),0);
set local role hsr_worker;
select is((select status from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000008',5)),'Deferred','ReplayFenceDefersStillActiveKeyboardCapability');
reset role;
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from recorded);
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from recorded),0);
set local role hsr_worker;
create temporary table stopped as select * from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000008',5);
select is((select status from stopped),'Blocked','ReplayFenceBlocksExpiredKeyboardInsteadOfReexecutingKeys');
select is((select error_code from stopped),'WORKER_INPUT_OUTCOME_UNCONFIRMED','ReplayFenceNamesUncertainInputWithoutClaimingItOccurred');
select ok((select lease_token is null and lease_until is null from stopped),'ReplayFenceIssuesNoReplacementInputCapability');
select is((select generation from stopped),1::bigint,'ReplayFenceDoesNotAllocateAnotherKeyboardGeneration');
reset role;
select results_eq($$select organization_id,scan_id,job_id from stopped$$,$$select '78000000-0000-0000-0000-000000000003'::uuid,scan_id,job_id from recorded$$,'ReplayFenceRetainsOriginalAdmittedIdentityForDiagnostics');
select ok((select count(*)=1 and bool_and(worker_id='78000000-0000-0000-0000-000000000007'::uuid) from hsr.job_attempts where job_id=(select job_id from recorded)),
    'ReplayFenceRetainsOneOriginalWorkerAttempt');
select is((select to_jsonb(a) from hsr.job_attempts a join recorded r on r.job_id=a.job_id),(select value from original_attempt),'ReplayFenceLeavesOriginalAttemptImmutable');
select ok((select j.state='blocked' and s.state='blocked' and j.last_error_code='WORKER_INPUT_OUTCOME_UNCONFIRMED' and j.archive_index_sha256 is null
    from hsr.scan_jobs j join recorded r on r.job_id=j.id join hsr.scans s on s.id=j.scan_id),'ReplayFenceDoesNotPublishACompletedResultOrArchive');
select ok((select count(*) filter(where kind='Expired')=1 and count(*) filter(where kind='Blocked' and error_code='WORKER_INPUT_OUTCOME_UNCONFIRMED')=1
    from hsr.attempt_events where job_id=(select job_id from recorded)),'ReplayFenceRetainsExpiryAndBlockedHistory');
grant select on original_claim to hsr_worker;
set local role hsr_worker;
select is((select hsr.complete_scan_job_with_archive(c.organization_id,c.job_id,'78000000-0000-0000-0000-000000000007',c.generation,c.lease_token,
    repeat('a',64),replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json',
    repeat('b',64),repeat('c',64),200000) from original_claim c),false,'ReplayFenceRejectsOriginalWorkersLateCompletion');
reset role;

-- Zero-input modes remain eligible for their existing bounded retries.
set local role authenticated;
create temporary table ordinary as select * from hsr.create_scan('78000000-0000-0000-0000-000000000003','78000000-0000-0000-0000-000000000005');
reset role;
set local role hsr_worker;
create temporary table ordinary_first_claim as select * from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000008',5);
reset role;
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from ordinary);
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from ordinary),0);
set local role hsr_worker;
create temporary table retried_observation as select * from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000007',5);
select ok((select status='Claimed' and generation=2 and not record_observation and traversal_route=0 from retried_observation),'ReplayFencePreservesSafeZeroInputObservationRetry');
select hsr.block_scan_job(c.organization_id,c.job_id,'78000000-0000-0000-0000-000000000007',c.generation,c.lease_token,'WORKER_STOP_REQUESTED') from retried_observation c;
reset role;
set local role authenticated;
create temporary table passive as select * from hsr.create_recorded_observation('78000000-0000-0000-0000-000000000003','78000000-0000-0000-0000-000000000006');
reset role;
set local role hsr_worker;
create temporary table passive_first_claim as select * from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000008',5);
reset role;
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from passive);
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from passive),0);
set local role hsr_worker;
select ok((select status='Claimed' and generation=2 and record_observation and traversal_route=0 from hsr.claim_scan_job_with_traversal('78000000-0000-0000-0000-000000000007',5)),
    'ReplayFencePreservesSafeZeroInputPassiveRecordingRetry');
reset role;
select * from finish();
rollback;
