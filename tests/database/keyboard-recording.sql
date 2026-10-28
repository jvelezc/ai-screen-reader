begin;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated,anon;
select plan(27);
insert into auth.users(id) values ('77000000-0000-0000-0000-000000000001'),('77000000-0000-0000-0000-000000000002');
insert into hsr.organizations(id,name) values ('77000000-0000-0000-0000-000000000003','Keyboard fixture proof');
insert into hsr.memberships values ('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000001');
set local role anon;
select throws_ok($$select * from hsr.create_recorded_traversal(null,null,1)$$,'42501',null,'AuthoredAnonymousCannotAdmitKeyboardRecording');
set local role hsr_worker;
select throws_ok($$select * from hsr.create_recorded_traversal(null,null,1)$$,'42501',null,'AuthoredWorkerCannotAdmitKeyboardRecording');
select throws_ok($$select hsr._complete_scan_job(null,null,null,null,null,null,null)$$,'42501',null,'AuthoredWorkerCannotBypassKeyboardRecordingCompletionWrapper');
reset role;
select set_config('request.jwt.claim.sub','77000000-0000-0000-0000-000000000001',true);
set local role authenticated;
select throws_ok($$select * from hsr.create_recorded_traversal('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000009',0)$$,'22023','HSR_INVALID_TRAVERSAL_ROUTE','AuthoredZeroCannotSelectAnAuthoredRoute');
select throws_ok($$select * from hsr.create_recorded_traversal('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000009',5)$$,'22023','HSR_INVALID_TRAVERSAL_ROUTE','AuthoredUnknownRouteCannotAdmitCommands');
create temporary table recorded as select * from hsr.create_recorded_traversal('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000004',1);
select is((select count(*) from recorded),1::bigint,'AuthoredKeyboardAdmissionReturnsExactlyOneJob');
select results_eq($$select * from hsr.create_recorded_traversal('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000004',1)$$,
    $$select * from recorded$$,'AuthoredKeyboardAdmissionRetryRetainsExactOriginalJob');
select throws_ok($$select * from hsr.create_scan('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000004')$$,
    '22023','HSR_REQUEST_CONFLICT','AuthoredObservationRetryCannotDowngradeKeyboardAdmission');
create temporary table ordinary as select * from hsr.create_scan('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000006');
select throws_ok($$select * from hsr.create_recorded_traversal('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000006',1)$$,
    '22023','HSR_REQUEST_CONFLICT','AuthoredKeyboardRecordingRetryCannotUpgradeOriginalObservation');
reset role;
select ok((select j.record_observation and j.traversal_route=1 and q.message->'traversalRoute'='1'::jsonb and q.message->'recordObservation'='true'::jsonb from hsr.scan_jobs j
    join recorded r on r.job_id=j.id join pgmq.q_hsr_scan_jobs q on q.msg_id=j.queue_message_id),'AuthoredKeyboardRecordingIntentIsBoundIntoActualQueueMessage');
set local role hsr_worker;
select is((select status from hsr.claim_scan_job_with_recording('77000000-0000-0000-0000-000000000007')), 'Deferred','AuthoredPassiveOnlyWorkerDefersKeyboardJob');
reset role;
select is((select count(*) from hsr.job_attempts),0::bigint,'AuthoredLegacyDeferralCannotAllocateKeyboardRecordingCapability');
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from recorded),0);
set local role hsr_worker;
create temporary table passive_claim as select * from hsr.claim_scan_job_with_traversal('77000000-0000-0000-0000-000000000007');
select is((select record_observation from passive_claim),true,'AuthoredNewWorkerClaimRetainsExplicitKeyboardRecordingMode');
select isnt(current_setting('hsr.traversal_claim',true),'on','AuthoredKeyboardRecordingClaimDoesNotLeakItsTransactionSetting');
reset role;
select results_eq($$select job_id,scan_id,generation from passive_claim$$,$$select job_id,scan_id,1::bigint from recorded$$,'AuthoredKeyboardClaimBindsActualAdmittedAttempt');
grant select on passive_claim to hsr_worker;
create function pg_temp.finish_keyboard(p_worker uuid default '77000000-0000-0000-0000-000000000007',p_index text default repeat('b',64))
returns boolean language sql as $$
    select hsr.complete_scan_job_with_archive(c.organization_id,c.job_id,p_worker,c.generation,c.lease_token,repeat('a',64),
        replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json',
        p_index,repeat('c',64),2000000) from passive_claim c
$$;
set local role hsr_worker;
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'77000000-0000-0000-0000-000000000007',c.generation,c.lease_token,
    repeat('a',64),replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json') from passive_claim c),
    false,'AuthoredLegacyCompletionCannotConfirmRequestedKeyboardRecording');
select is(pg_temp.finish_keyboard(p_worker:='77000000-0000-0000-0000-000000000099'),false,'AuthoredKeyboardCompletionRequiresActualWorker');
select is(pg_temp.finish_keyboard(),true,'AuthoredKeyboardArchiveCompletesWithCurrentFencedCapability');
select is(pg_temp.finish_keyboard(),true,'AuthoredExactKeyboardCompletionRetryIsIdempotent');
select is(pg_temp.finish_keyboard(p_index:=repeat('d',64)),false,'AuthoredKeyboardOriginalIndexCannotBeReplaced');
reset role;
select ok((select j.state='completed' and j.record_observation and j.archive_bytes=2000000 and j.archive_index_sha256=repeat('b',64)
    from hsr.scan_jobs j join recorded r on r.job_id=j.id),'AuthoredCompletedKeyboardJobRetainsModeAndImmutableArchive');
set local role hsr_worker;
create temporary table ordinary_claim as select * from hsr.claim_scan_job_with_traversal('77000000-0000-0000-0000-000000000007');
select is((select record_observation from ordinary_claim),false,'AuthoredNewWorkerPreservesDefaultObservationMode');
select throws_ok($$select hsr.complete_scan_job_with_archive(c.organization_id,c.job_id,'77000000-0000-0000-0000-000000000007',c.generation,c.lease_token,
    repeat('a',64),replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json',repeat('b',64),repeat('c',64),2000000)
    from ordinary_claim c$$,'22023','INVALID_PRODUCER_ARCHIVE','AuthoredDefaultObservationArchiveBoundIsNotExpanded');
reset role;
set local role authenticated;
select is((select count(*) from hsr.scan_jobs where record_observation and archive_bytes=2000000),1::bigint,'AuthoredMemberCanRediscoverKeyboardReceipt');
select set_config('request.jwt.claim.sub','77000000-0000-0000-0000-000000000002',true);
select is((select count(*) from hsr.scan_jobs),0::bigint,'AuthoredPeerCannotReadKeyboardReceipt');
select throws_ok($$select * from hsr.create_recorded_traversal('77000000-0000-0000-0000-000000000003','77000000-0000-0000-0000-000000000099',1)$$,
    '42501','HSR_MEMBERSHIP_REQUIRED','AuthoredNonmemberCannotAdmitKeyboardFixture');
reset role;
delete from hsr.memberships where user_id='77000000-0000-0000-0000-000000000001';
select set_config('request.jwt.claim.sub','77000000-0000-0000-0000-000000000001',true);
set local role authenticated;
select is((select count(*) from hsr.scan_jobs),0::bigint,'AuthoredRevokedMemberCannotReadKeyboardReceipt');
select * from finish();
rollback;
