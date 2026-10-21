begin;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated,anon;
select plan(101);
insert into auth.users(id) values
    ('70000000-0000-0000-0000-000000000001'),('70000000-0000-0000-0000-000000000002');
insert into hsr.organizations(id,name) values
    ('71000000-0000-0000-0000-000000000001','Lease tenant A'),
    ('71000000-0000-0000-0000-000000000002','Lease tenant B');
insert into hsr.memberships values
    ('71000000-0000-0000-0000-000000000001','70000000-0000-0000-0000-000000000001'),
    ('71000000-0000-0000-0000-000000000002','70000000-0000-0000-0000-000000000002');
create temporary table jobs(tag text,organization_id uuid,scan_id uuid,job_id uuid,queue_message_id bigint,reference text);
grant select on jobs to hsr_worker;
create function pg_temp.add_job(p_tag text,p_org uuid,p_user uuid,p_request uuid) returns void
language plpgsql as $$
declare receipt record;
begin
    perform set_config('request.jwt.claim.sub',p_user::text,true);
    select * into receipt from hsr.create_scan(p_org,p_request);
    insert into jobs values(p_tag,p_org,receipt.scan_id,receipt.job_id,receipt.queue_message_id,
        replace(p_org::text,'-','')||'/'||replace(receipt.scan_id::text,'-','')||'/'||
        replace(receipt.job_id::text,'-','')||'/1/observation.json');
end $$;
create function pg_temp.expire_job(p_tag text) returns void language plpgsql as $$
begin
    update hsr.job_leases l set expires_at=clock_timestamp()-interval '1 second'
        from jobs j where j.tag=p_tag and l.organization_id=j.organization_id and l.job_id=j.job_id;
    perform pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from jobs where tag=p_tag),0);
end $$;

set local role anon;
select throws_ok($$select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')$$,
    '42501',null,'AnonymousCannotClaimWorkerJob');
set local role authenticated;
select throws_ok($$select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')$$,
    '42501',null,'AuthenticatedClientCannotClaimWorkerJob');
select throws_ok($$select hsr.renew_scan_job(null,null,null,null,null)$$,
    '42501',null,'AuthenticatedClientCannotRenewWorkerJob');
select throws_ok($$select hsr.complete_scan_job(null,null,null,null,null,null,null)$$,
    '42501',null,'AuthenticatedClientCannotCompleteWorkerJob');
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,null)$$,
    '42501',null,'AuthenticatedClientCannotBlockWorkerJob');
select throws_ok($$select * from hsr.job_leases$$,'42501',null,'ClientCannotReadLeaseCapabilities');
set local role hsr_worker;
select is((select status from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')),
    'Empty','EmptyQueueReturnsNoLease');
select throws_ok($$select * from hsr.job_leases$$,'42501',null,'WorkerCannotReadOtherLeaseCapabilities');
select throws_ok($$select * from pgmq.q_hsr_scan_jobs$$,'42501',null,'WorkerCannotReadRawQueue');
select throws_ok($$select hsr._archive_message(1)$$,'42501',null,'WorkerCannotBypassFencingThroughHelper');
select throws_ok($$select hsr._block_job(null::hsr.scan_jobs,'WORKER_STOP_REQUESTED')$$,
    '42501',null,'WorkerCannotUseInternalUnfencedBlockHelper');
reset role;
select pg_temp.add_job('A','71000000-0000-0000-0000-000000000001',
    '70000000-0000-0000-0000-000000000001','73000000-0000-0000-0000-000000000001');
set local role hsr_worker;
select throws_ok($$select * from hsr.claim_scan_job(null)$$,
    '22023','INVALID_WORKER_LEASE_REQUEST','MissingWorkerIdentityCannotConsumeMessage');
select throws_ok($$select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001',null)$$,
    '22023','INVALID_WORKER_LEASE_REQUEST','NullLeaseDurationRejected');
select throws_ok($$select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001',4)$$,
    '22023','INVALID_WORKER_LEASE_REQUEST','BelowMinimumLeaseDurationRejected');
select throws_ok($$select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001',61)$$,
    '22023','INVALID_WORKER_LEASE_REQUEST','AboveMaximumLeaseDurationRejected');
reset role;
select is((select read_ct from pgmq.q_hsr_scan_jobs),0,'InvalidLeaseRequestDoesNotConsumeVisibility');
set local role hsr_worker;
create temporary table first_claim as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001',5);
select is((select status from first_claim),'Claimed','MinimumLeaseClaimsOneJob');
select results_eq($$select organization_id,scan_id,job_id,generation from first_claim$$,
    $$select organization_id,scan_id,job_id,1::bigint from jobs where tag='A'$$,'ClaimBindsExactTenantScanJobAndGeneration');
reset role;
select is((select extract(epoch from initial_expiry-claimed_at) from hsr.job_attempts),5::numeric,
    'MinimumLeaseHasExactFiveSecondInitialBounds');
select is((select token_hash from hsr.job_attempts),
    (select sha256(convert_to(lease_token::text,'UTF8')) from first_claim),'ImmutableAttemptStoresOnlyCapabilityHash');
-- The exact minimum was asserted above. Give the following independent guard
-- cases a generous controlled active state rather than race five wall seconds.
update hsr.job_leases set expires_at=clock_timestamp()+interval '60 seconds';
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from jobs where tag='A'),0);
set local role hsr_worker;
create temporary table deferred_claim as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000002');
select is((select status from deferred_claim),'Deferred','PrematureRedeliveryCannotReplaceActiveLease');
select is((select lease_token from deferred_claim),null::uuid,'DeferredClaimDoesNotDiscloseCapability');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',c.generation,c.lease_token)
    from first_claim c),false,'WrongWorkerCannotRenewCurrentLease');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,
    '72000000-0000-0000-0000-000000000099') from first_claim c),false,'WrongCapabilityCannotRenewCurrentLease');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',2,c.lease_token)
    from first_claim c),false,'WrongGenerationCannotRenewCurrentLease');
select throws_ok($$select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,null) from first_claim c$$,'22023','INVALID_WORKER_LEASE_REQUEST','NullRenewalDurationRejected');
select throws_ok($$select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,4) from first_claim c$$,'22023','INVALID_WORKER_LEASE_REQUEST','BelowMinimumRenewalRejected');
select throws_ok($$select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,61) from first_claim c$$,'22023','INVALID_WORKER_LEASE_REQUEST','AboveMaximumRenewalRejected');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,c.lease_token,5)
    from first_claim c),true,'CurrentOwnerCanRenewAtMinimumDuration');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,c.lease_token,60)
    from first_claim c),true,'CurrentOwnerCanRenewAtMaximumDuration');
reset role;
select is((select generation from hsr.scan_jobs where id=(select job_id from jobs where tag='A')),1::bigint,
    'DeferredAndRenewedLeaseRetainsGeneration');
select is((select lease_until from hsr.attempt_events where kind='Renewed' order by id desc limit 1),
    (select expires_at from hsr.job_leases),'RenewalAuditBindsActualExpiry');
select ok((select q.vt>=l.expires_at from pgmq.q_hsr_scan_jobs q cross join hsr.job_leases l),
    'RenewalExtendsActualQueueVisibilityThroughLease');

select pg_temp.add_job('B','71000000-0000-0000-0000-000000000002',
    '70000000-0000-0000-0000-000000000002','73000000-0000-0000-0000-000000000002');
set local role hsr_worker;
create temporary table second_tenant_claim as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000002',60);
select is((select organization_id from second_tenant_claim),'71000000-0000-0000-0000-000000000002'::uuid,
    'IndependentTenantJobCanBeClaimedWhileFirstLeaseIsActive');
select throws_ok($$select hsr.block_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,'sensitive-arbitrary-detail') from second_tenant_claim c$$,
    '22023','INVALID_WORKER_FAILURE_CODE','ArbitraryFailureDetailCannotEnterAttemptLog');
select is((select hsr.block_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,'72000000-0000-0000-0000-000000000099','WORKER_STOP_REQUESTED') from second_tenant_claim c),false,
    'WrongCapabilityCannotBlockCurrentWork');
select is((select hsr.block_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,'OWNED_BROWSER_CLOSE_UNCONFIRMED') from second_tenant_claim c),true,
    'CurrentWorkerCanRecordSafeEngineFailure');
reset role;
select is((select extract(epoch from initial_expiry-claimed_at) from hsr.job_attempts where organization_id='71000000-0000-0000-0000-000000000002'),
    60::numeric,'MaximumLeaseHasExactSixtySecondInitialBounds');
select is((select last_error_code from hsr.scan_jobs where organization_id='71000000-0000-0000-0000-000000000002'),
    'OWNED_BROWSER_CLOSE_UNCONFIRMED','BlockedJobRetainsSafeEngineError');
set local role authenticated;
set local request.jwt.claim.sub='70000000-0000-0000-0000-000000000001';
select is((select count(*) from hsr.job_attempts),1::bigint,'FirstTenantReadsOnlyItsOwnAttempt');
select throws_ok($$update hsr.job_attempts set worker_id='72000000-0000-0000-0000-000000000099'$$,
    '42501',null,'ClientCannotRewriteImmutableAttempt');
select is((select count(*) from hsr.attempt_events where organization_id='71000000-0000-0000-0000-000000000002'),
    0::bigint,'FirstTenantCannotReadOtherAttemptEvents');
set local request.jwt.claim.sub='70000000-0000-0000-0000-000000000002';
select is((select count(*) from hsr.job_attempts where organization_id='71000000-0000-0000-0000-000000000001'),
    0::bigint,'SecondTenantCannotReadFirstAttempt');
select is((select count(*) from hsr.attempt_events),1::bigint,'SecondTenantReadsItsOwnBlockedEvent');
reset role;

set local role hsr_worker;
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,null,j.reference) from first_claim c join jobs j on j.tag='A'$$,
    '22023','INVALID_OBSERVATION_RECEIPT','NullObservationDigestRejected');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('a',63),j.reference) from first_claim c join jobs j on j.tag='A'$$,
    '22023','INVALID_OBSERVATION_RECEIPT','ShortObservationDigestRejected');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('a',65),j.reference) from first_claim c join jobs j on j.tag='A'$$,
    '22023','INVALID_OBSERVATION_RECEIPT','LongObservationDigestRejected');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('G',64),j.reference) from first_claim c join jobs j on j.tag='A'$$,
    '22023','INVALID_OBSERVATION_RECEIPT','NoncanonicalObservationDigestRejected');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('A',64),j.reference) from first_claim c join jobs j on j.tag='A'$$,
    '22023','INVALID_OBSERVATION_RECEIPT','UppercaseHexObservationDigestRejected');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('a',64),'../observation.json') from first_claim c$$,
    '22023','INVALID_OBSERVATION_RECEIPT','TraversalObservationReferenceRejected');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('a',64),null) from first_claim c$$,
    '22023','INVALID_OBSERVATION_RECEIPT','NullObservationReferenceRejected');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,repeat('a',64),j.reference) from first_claim c join jobs j on j.tag='A'),false,
    'WrongWorkerCannotCompleteCurrentLease');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,'72000000-0000-0000-0000-000000000099',repeat('a',64),j.reference) from first_claim c join jobs j on j.tag='A'),false,
    'WrongCapabilityCannotCompleteCurrentLease');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    2,c.lease_token,repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from first_claim c join jobs j on j.tag='A'),false,'CurrentCapabilityCannotFinalizeDifferentGeneration');
reset role;
select pg_temp.expire_job('A');
set local role hsr_worker;
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,c.lease_token)
    from first_claim c),false,'ExpiredLeaseCannotRenew');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,
    c.lease_token,repeat('a',64),j.reference) from first_claim c join jobs j on j.tag='A'),false,'ExpiredLeaseCannotComplete');
select is((select hsr.block_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,
    c.lease_token,'WORKER_STOP_REQUESTED') from first_claim c),false,'ExpiredLeaseCannotBlockWork');
create temporary table replacement_claim as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000002');
select is((select generation from replacement_claim),2::bigint,'RedeliveryAdvancesGeneration');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,
    c.lease_token,repeat('a',64),j.reference) from first_claim c join jobs j on j.tag='A'),false,
    'PriorGenerationCannotFinalizeReplacement');
reset role;
select is((select count(*) from hsr.job_attempts where organization_id='71000000-0000-0000-0000-000000000001'),
    2::bigint,'ReplacementRetainsBothImmutableAttempts');
select is((select count(*) from hsr.attempt_events where kind='Expired'),1::bigint,'ExpiredAttemptRemainsExplicit');
select isnt((select token_hash from hsr.job_attempts where organization_id='71000000-0000-0000-0000-000000000001' and generation=1),
    (select token_hash from hsr.job_attempts where organization_id='71000000-0000-0000-0000-000000000001' and generation=2),
    'ReplacementUsesIndependentCapability');

create temporary table lost_message as select * from pgmq.q_hsr_scan_jobs where msg_id=(select queue_message_id from jobs where tag='A');
delete from pgmq.q_hsr_scan_jobs where msg_id=(select queue_message_id from jobs where tag='A');
set local role hsr_worker;
select throws_ok($$select hsr.block_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,'WORKER_STOP_REQUESTED') from replacement_claim c$$,
    'P0001','QUEUE_ACK_UNCONFIRMED','MissingArchiveAckCannotReportBlocked');
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'$$,'P0001','QUEUE_ACK_UNCONFIRMED','MissingQueueAckCannotReportCompletion');
select throws_ok($$select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token) from replacement_claim c$$,'P0001','QUEUE_VISIBILITY_UNCONFIRMED',
    'MissingVisibilityAckCannotReportRenewal');
reset role;
select is((select state from hsr.scan_jobs where id=(select job_id from jobs where tag='A')),'leased',
    'FailedQueueAckRollsBackResultState');
select is((select count(*) from hsr.attempt_events where kind='Completed'),0::bigint,'FailedQueueAckLeavesNoCompletionEvent');
select is((select count(*) from hsr.job_leases),1::bigint,'FailedQueueAckRetainsCurrentCapability');
insert into pgmq.q_hsr_scan_jobs overriding system value select * from lost_message;
create function pg_temp.reject_completion_event() returns trigger language plpgsql as $$
begin raise exception 'INJECTED_COMPLETION_EVENT_FAILURE';end $$;
create trigger reject_completion_event before insert on hsr.attempt_events for each row
    when (new.kind='Completed') execute function pg_temp.reject_completion_event();
set local role hsr_worker;
select throws_ok($$select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'$$,'P0001','INJECTED_COMPLETION_EVENT_FAILURE',
    'PostDeleteFailureAbortsActualCompletion');
reset role;
drop trigger reject_completion_event on hsr.attempt_events;
select is((select count(*) from pgmq.q_hsr_scan_jobs),1::bigint,'PostDeleteFailureRestoresActualQueueMessage');
select is((select state from hsr.scan_jobs where id=(select job_id from jobs where tag='A')),'leased',
    'PostDeleteFailureRestoresLeasedJob');
select is((select count(*) from hsr.job_leases),1::bigint,'PostDeleteFailureRestoresCapability');
set local role hsr_worker;
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'),true,'CurrentReplacementCompletesBoundObservation');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'),true,'ExactCompletionRetryReturnsPriorSuccess');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,c.lease_token,repeat('b',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'),false,'ConflictingCompletionCannotRewriteDigest');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'),false,'CompletedRetryStillRequiresOriginalWorker');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000002',
    c.generation,'72000000-0000-0000-0000-000000000099',repeat('a',64),replace(j.reference,'/1/observation.json','/2/observation.json'))
    from replacement_claim c join jobs j on j.tag='A'),false,'CompletedRetryStillRequiresOriginalCapability');
reset role;
select is((select count(*) from pgmq.q_hsr_scan_jobs),0::bigint,'CompletedAndBlockedJobsLeaveNoPendingMessages');
select is((select count(*) from hsr.job_leases),0::bigint,'CompletionRemovesActiveCapability');
select is((select count(*) from hsr.attempt_events where kind='Completed'),1::bigint,'CompletionRetryDoesNotDuplicateEvent');
select is((select state from hsr.scans where id=(select scan_id from jobs where tag='A')),'observed',
    'CompletedStageClaimsObservationRatherThanGoalVerification');
select is((select result_digest from hsr.scan_jobs where id=(select job_id from jobs where tag='A')),repeat('a',64),
    'ConflictingRetryRetainsOriginalDigest');
insert into pgmq.q_hsr_scan_jobs overriding system value select * from lost_message;
select pgmq.set_vt('hsr_scan_jobs',(select queue_message_id from jobs where tag='A'),0);
set local role hsr_worker;
select is((select status from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')),
    'Deduplicated','CompletedMessageRedeliveryCannotCreateNewLease');
reset role;
select is((select count(*) from hsr.job_attempts where job_id=(select job_id from jobs where tag='A')),2::bigint,
    'CompletedRedeliveryDoesNotInventAnotherAttempt');

select pg_temp.add_job('C','71000000-0000-0000-0000-000000000001',
    '70000000-0000-0000-0000-000000000001','73000000-0000-0000-0000-000000000003');
set local role hsr_worker;
create temporary table limit_first as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001');
reset role;
select pg_temp.expire_job('C');
set local role hsr_worker;
create temporary table limit_second as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000002');
reset role;
select pg_temp.expire_job('C');
set local role hsr_worker;
create temporary table limit_third as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001');
select is((select generation from limit_third),3::bigint,'ThirdAttemptIsStillPermitted');
reset role;
select pg_temp.expire_job('C');
set local role hsr_worker;
create temporary table limit_blocked as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000002');
select is((select status from limit_blocked),'Blocked','FourthAttemptIsBlocked');
select is((select error_code from limit_blocked),'WORKER_ATTEMPTS_EXHAUSTED','AttemptLimitHasExplicitFailure');
reset role;
select is((select count(*) from hsr.job_attempts where job_id=(select job_id from jobs where tag='C')),3::bigint,
    'AttemptLimitDoesNotInventFourthAttempt');

select pg_temp.add_job('D','71000000-0000-0000-0000-000000000001',
    '70000000-0000-0000-0000-000000000001','73000000-0000-0000-0000-000000000004');
set local role hsr_worker;
create temporary table budget_claim as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001');
reset role;
update hsr.scan_jobs set first_claimed_at=clock_timestamp()-interval '9 minutes 50 seconds'
    where id=(select job_id from jobs where tag='D');
set local role hsr_worker;
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,c.lease_token,60)
    from budget_claim c),true,'CurrentLeaseCanRenewBeforeHardDeadline');
reset role;
select is((select l.expires_at from hsr.job_leases l join jobs j on j.job_id=l.job_id where j.tag='D'),
    (select s.first_claimed_at+interval '10 minutes' from hsr.scan_jobs s join jobs j on j.job_id=s.id where j.tag='D'),
    'RenewedLeaseIsClampedToOriginalHardBudget');
update hsr.scan_jobs set first_claimed_at=clock_timestamp()-interval '10 minutes'
    where id=(select job_id from jobs where tag='D');
set local role hsr_worker;
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,c.lease_token)
    from budget_claim c),false,'RenewalCannotExtendHardJobBudget');
select is((select hsr.complete_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',
    c.generation,c.lease_token,repeat('a',64),j.reference) from budget_claim c join jobs j on j.tag='D'),false,
    'HardBudgetExpiryRejectsCompletionDespiteFutureLeaseTimestamp');
reset role;
select pg_temp.expire_job('D');
set local role hsr_worker;
select is((select error_code from hsr.claim_scan_job('72000000-0000-0000-0000-000000000002')),
    'WORKER_BUDGET_EXHAUSTED','ExhaustedHardBudgetBlocksRedelivery');
reset role;

select pg_temp.add_job('F','71000000-0000-0000-0000-000000000001',
    '70000000-0000-0000-0000-000000000001','73000000-0000-0000-0000-000000000006');
update hsr.scan_jobs set created_at=clock_timestamp()-interval '1 day' where id=(select job_id from jobs where tag='F');
set local role hsr_worker;
create temporary table backlog_claim as select * from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001');
select is((select status from backlog_claim),'Claimed','QueueBacklogDoesNotConsumeExecutionBudget');
select is((select hsr.block_scan_job(c.organization_id,c.job_id,'72000000-0000-0000-0000-000000000001',c.generation,
    c.lease_token,'WORKER_STOP_REQUESTED') from backlog_claim c),true,'CurrentWorkerCanRecordExplicitStop');
reset role;

select pg_temp.add_job('E','71000000-0000-0000-0000-000000000001',
    '70000000-0000-0000-0000-000000000001','73000000-0000-0000-0000-000000000005');
update pgmq.q_hsr_scan_jobs set message='{"bad":"synthetic-untrusted-payload"}'
    where msg_id=(select queue_message_id from jobs where tag='E');
set local role hsr_worker;
select is((select error_code from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')),
    'QUEUE_BINDING_UNCONFIRMED','MalformedBoundMessageIsRejected');
reset role;
select is((select state from hsr.scan_jobs where id=(select job_id from jobs where tag='E')),'blocked',
    'MalformedMessageRetainsBlockedJob');
select pgmq.send('hsr_scan_jobs','{"bad":"synthetic-untrusted-payload"}'::jsonb);
set local role hsr_worker;
select is((select error_code from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')),
    'UNBOUND_QUEUE_MESSAGE','UnboundMessageIsRejected');
select is((select status from hsr.claim_scan_job('72000000-0000-0000-0000-000000000001')),
    'Empty','RejectedMessagesDoNotPoisonTheQueue');
reset role;
select is((select count(*) from hsr.queue_failures),2::bigint,'BothQueueFailuresHaveDurableSafeReceipts');
select ok((select bool_and((to_jsonb(f)-array['message_id','reason','observed_at'])='{}'::jsonb)
    from hsr.queue_failures f),'QuarantineReceiptsContainOnlySafeIdentityReasonAndTime');
select * from finish();
rollback;
