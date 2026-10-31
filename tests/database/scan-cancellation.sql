begin;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated,anon;
select plan(41);
insert into auth.users(id) values
    ('7b000000-0000-0000-0000-000000000001'),('7b000000-0000-0000-0000-000000000002'),
    ('7b000000-0000-0000-0000-000000000003');
insert into hsr.organizations(id,name) values
    ('7b000000-0000-0000-0000-000000000010','Cancellation owner workspace'),
    ('7b000000-0000-0000-0000-000000000011','Cancellation foreign workspace');
insert into hsr.memberships values
    ('7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000001'),
    ('7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000002'),
    ('7b000000-0000-0000-0000-000000000011','7b000000-0000-0000-0000-000000000003');
select ok(has_function_privilege('authenticated','hsr.request_scan_cancellation(uuid,uuid)','EXECUTE'), 'CreatorCancellationRpcIsAvailableToAuthenticatedCallers');
select ok(not has_function_privilege('anon','hsr.request_scan_cancellation(uuid,uuid)','EXECUTE')
    and not has_function_privilege('hsr_worker','hsr.request_scan_cancellation(uuid,uuid)','EXECUTE'), 'AnonymousAndWorkersCannotImpersonateCancellationCaller');
select ok(not has_table_privilege('authenticated','hsr.scan_jobs','UPDATE'), 'CancellationMarkerCannotBeWrittenDirectlyByCaller');
set local role anon;
select throws_ok($$select * from hsr.request_scan_cancellation(null,null)$$,'42501',null,'AnonymousCancellationCannotReachSecurityDefiner');
set local role hsr_worker;
select throws_ok($$select * from hsr.request_scan_cancellation(null,null)$$,'42501',null,'WorkerCannotInvokeOriginalCallerCancellation');
reset role;

select set_config('request.jwt.claim.sub','7b000000-0000-0000-0000-000000000001',true);
set local role authenticated;
create temporary table queued_cancel_job as select * from hsr.create_scan(
    '7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000101');
create temporary table queued_cancel as select c.* from queued_cancel_job j cross join lateral
    hsr.request_scan_cancellation('7b000000-0000-0000-0000-000000000010',j.scan_id) c;
select is((select status from queued_cancel),'Requested','QueuedCancellationIsAStopRequest');
select is((select input_assessment from queued_cancel),'NoAttemptStarted','QueuedCancellationKnowsOnlyThatNoAttemptStarted');
select ok((select generation=0 and producer_id is null and requested_at is not null from queued_cancel), 'QueuedCancellationPreservesUnstartedIdentity');
reset role;
select ok((select j.state='blocked' and j.generation=0 and j.last_error_code='WORKER_CANCELLED_BY_CALLER'
    and j.cancellation_requested_by='7b000000-0000-0000-0000-000000000001' and j.cancellation_requested_at=c.requested_at
    from hsr.scan_jobs j join queued_cancel c on c.job_id=j.id), 'QueuedCancellationPersistsOriginalActorAndServerTime');
select is((select s.state from hsr.scans s join queued_cancel c on c.scan_id=s.id),'blocked','CancelledScanStateChangesAtomically');
select is((select count(*) from hsr.job_attempts a join queued_cancel c on c.job_id=a.job_id),0::bigint,'QueuedCancellationAllocatesNoExecutionAttempt');
select is((select count(*) from pgmq.q_hsr_scan_jobs q join queued_cancel_job j on q.msg_id=j.queue_message_id),0::bigint,'QueuedCancellationArchivesOnlyItsMessage');
set local role authenticated;
select ok((select c.requested_at=prior.requested_at and c.job_id=prior.job_id and c.generation=prior.generation
    from queued_cancel prior cross join lateral hsr.request_scan_cancellation(prior.organization_id,prior.scan_id) c), 'RepeatedCancellationPreservesTheOriginalRequest');
select throws_ok($$select * from hsr.request_scan_cancellation(null,null)$$,'22023',null,'InvalidCancellationScopeIsRejected');
reset role;
select set_config('request.jwt.claim.sub','7b000000-0000-0000-0000-000000000002',true);
set local role authenticated;
select throws_ok($$select c.* from queued_cancel q cross join lateral hsr.request_scan_cancellation(q.organization_id,q.scan_id) c$$,
    '42501',null,'WorkspaceMembershipDoesNotAuthorizeCancellingAnotherCreator');
reset role;
select set_config('request.jwt.claim.sub','7b000000-0000-0000-0000-000000000003',true);
set local role authenticated;
select throws_ok($$select c.* from queued_cancel q cross join lateral hsr.request_scan_cancellation(q.organization_id,q.scan_id) c$$,
    '42501',null,'ForeignWorkspaceCannotCancelOriginalJob');
reset role;

select set_config('request.jwt.claim.sub','7b000000-0000-0000-0000-000000000001',true);
set local role authenticated;
create temporary table active_cancel_job as select * from hsr.create_recorded_traversal(
    '7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000102',1);
grant select on active_cancel_job to hsr_worker;
set local role hsr_worker;
create temporary table active_cancel_lease as select * from hsr.claim_scan_job_with_traversal('7b000000-0000-0000-0000-000000000020',60);
reset role;
create temporary table active_cancel_attempt as select to_jsonb(a) as original from hsr.job_attempts a join active_cancel_job j on j.job_id=a.job_id;
set local role authenticated;
create temporary table active_cancel as select c.* from active_cancel_job j cross join lateral
    hsr.request_scan_cancellation('7b000000-0000-0000-0000-000000000010',j.scan_id) c;
select is((select input_assessment from active_cancel),'NotAssessed','LeasedCancellationCannotInventZeroInput');
select ok((select generation=1 and producer_id='7b000000-0000-0000-0000-000000000020' from active_cancel), 'ActiveCancellationRetainsTheActualProducerAndGeneration');
reset role;
select ok((select to_jsonb(a)=old.original from hsr.job_attempts a join active_cancel_job j on j.job_id=a.job_id cross join active_cancel_attempt old), 'CancellationDoesNotRewriteTheExecutionAttempt');
select is((select count(*) from hsr.job_leases l join active_cancel_job j on j.job_id=l.job_id),0::bigint,'CancellationRevokesExecutionCapability');
select ok((select j.state='blocked' and j.result_digest is null and j.archive_index_sha256 is null
    from hsr.scan_jobs j join active_cancel_job a on a.job_id=j.id), 'CancellationCannotInventEvidencePublication');
set local role hsr_worker;
select is((select hsr.renew_scan_job(l.organization_id,l.job_id,'7b000000-0000-0000-0000-000000000020',l.generation,l.lease_token,60)
    from active_cancel_lease l),false,'CancelledLeaseCannotRenew');
select is((select hsr.complete_scan_job(l.organization_id,l.job_id,'7b000000-0000-0000-0000-000000000020',l.generation,l.lease_token,
    repeat('a',64),replace(l.organization_id::text,'-','')||'/'||replace(l.scan_id::text,'-','')||'/'||replace(l.job_id::text,'-','')||'/1/observation.json')
    from active_cancel_lease l),false,'CancelledExecutionCannotComplete');
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'7b000000-0000-0000-0000-000000000020',l.generation,l.lease_token,'OWNED_BROWSER_CLOSE_UNCONFIRMED')
    from active_cancel_lease l),false,'StaleWorkerCannotOverwriteCancellationReason');
select is((select status from hsr.claim_scan_job_with_traversal('7b000000-0000-0000-0000-000000000021',60)), 'Empty','CancelledKeyboardJobCannotBeClaimedAgain');
reset role;
select is((select count(*) from hsr.job_attempts a join active_cancel_job j on j.job_id=a.job_id),1::bigint,'CancellationCreatesNoReplacementAttempt');

set local role authenticated;
create temporary table pub_cancel_job as select * from hsr.create_recorded_traversal(
    '7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000103',1);
grant select on pub_cancel_job to hsr_worker;
set local role hsr_worker;
create temporary table pub_cancel_lease as select * from hsr.claim_scan_job_with_traversal('7b000000-0000-0000-0000-000000000022',60);
select ok((select hsr.block_scan_job(l.organization_id,l.job_id,'7b000000-0000-0000-0000-000000000022',l.generation,l.lease_token,'FIXTURE_OBSERVATION_UNCONFIRMED')
    from pub_cancel_lease l), 'PublicationCancellationFixtureRetainsBlockedOriginal');
create temporary table pub_cancel_claim as select c.* from pub_cancel_job j cross join lateral hsr.claim_recorded_publication(
    '7b000000-0000-0000-0000-000000000010',j.scan_id,j.job_id,1,'7b000000-0000-0000-0000-000000000022',repeat('b',64),
    '7b000000-0000-0000-0000-000000000023',60) c;
select is((select status from pub_cancel_claim),'Claimed','IndependentPublicationCapabilityExistsBeforeCancellation');
reset role;
set local role authenticated;
create temporary table pub_cancel as select c.* from pub_cancel_job j cross join lateral
    hsr.request_scan_cancellation('7b000000-0000-0000-0000-000000000010',j.scan_id) c;
select is((select status from pub_cancel),'Requested','AlreadyBlockedOriginalCanBeExplicitlyCancelled');
select is((select input_assessment from pub_cancel),'NotAssessed','PublicationCancellationDoesNotAssessRecordedInput');
set local role hsr_worker;
select is((select hsr.renew_recorded_publication('7b000000-0000-0000-0000-000000000010',j.job_id,1,
    '7b000000-0000-0000-0000-000000000022',repeat('b',64),'7b000000-0000-0000-0000-000000000023',60) from pub_cancel_job j),false,
    'CancellationRevokesIndependentPublicationRenewal');
select is((select hsr.complete_recorded_publication('7b000000-0000-0000-0000-000000000010',j.job_id,1,
    '7b000000-0000-0000-0000-000000000022',repeat('b',64),'7b000000-0000-0000-0000-000000000023',repeat('c',64),
    replace('7b000000-0000-0000-0000-000000000010','-','')||'/'||replace(j.scan_id::text,'-','')||'/'||replace(j.job_id::text,'-','')||'/1/observation.json',
    repeat('d',64),repeat('e',64),20000) from pub_cancel_job j),false,'CancelledOriginalCannotBePublishedByStaleCapability');
select is((select c.status from pub_cancel_job j cross join lateral hsr.claim_recorded_publication(
    '7b000000-0000-0000-0000-000000000010',j.scan_id,j.job_id,1,'7b000000-0000-0000-0000-000000000022',repeat('b',64),
    '7b000000-0000-0000-0000-000000000024',60) c),'Rejected','CancellationCannotBeBypassedThroughStorageOnlyRecovery');
reset role;
select is((select j.last_error_code from hsr.scan_jobs j join pub_cancel_job a on a.job_id=j.id),'WORKER_CANCELLED_BY_CALLER','CancelledPublicationRetainsExplicitCallerReason');

set local role authenticated;
create temporary table done_cancel_job as select * from hsr.create_scan(
    '7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000104');
grant select on done_cancel_job to hsr_worker;
set local role hsr_worker;
create temporary table done_cancel_lease as select * from hsr.claim_scan_job_with_traversal('7b000000-0000-0000-0000-000000000025',60);
select ok((select hsr.complete_scan_job(l.organization_id,l.job_id,'7b000000-0000-0000-0000-000000000025',l.generation,l.lease_token,
    repeat('f',64),replace(l.organization_id::text,'-','')||'/'||replace(l.scan_id::text,'-','')||'/'||replace(l.job_id::text,'-','')||'/1/observation.json')
    from done_cancel_lease l),'CompletedCancellationFixtureHasRealFencedReceipt');
reset role;
create temporary table done_before_cancel as select to_jsonb(j) as original from hsr.scan_jobs j join done_cancel_job a on a.job_id=j.id;
set local role authenticated;
create temporary table done_cancel as select c.* from done_cancel_job j cross join lateral
    hsr.request_scan_cancellation('7b000000-0000-0000-0000-000000000010',j.scan_id) c;
select is((select status from done_cancel),'AlreadyCompleted','CancellationCannotReplaceCompletedReceipt');
select ok((select requested_at is null and input_assessment='NotAssessed' from done_cancel),'CompletedNoopDoesNotInventCancellationOrZeroInput');
reset role;
select ok((select to_jsonb(j)=old.original from hsr.scan_jobs j join done_cancel_job a on a.job_id=j.id cross join done_before_cancel old),'CompletedJobIsByteEquivalentAfterCancellationRequest');
select throws_ok($$update hsr.scan_jobs set state='leased' where id=(select job_id from active_cancel_job)$$,
    '23514',null,'PersistentCancellationMarkerCannotResumeJob');
set local role authenticated;
create temporary table revoked_cancel_job as select * from hsr.create_scan(
    '7b000000-0000-0000-0000-000000000010','7b000000-0000-0000-0000-000000000105');
reset role;
delete from hsr.memberships where organization_id='7b000000-0000-0000-0000-000000000010' and user_id='7b000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($$select c.* from revoked_cancel_job j cross join lateral hsr.request_scan_cancellation('7b000000-0000-0000-0000-000000000010',j.scan_id) c$$,
    '42501',null,'CreatorMembershipMustStillExistAtCancellation');
reset role;
select ok((select j.state='queued' and j.cancellation_requested_at is null from hsr.scan_jobs j join revoked_cancel_job a on a.job_id=j.id),'DeniedCancellationCannotChangeTheQueuedJob');
select * from finish();
rollback;
