begin;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated,anon;
select plan(64);
insert into auth.users(id) values('79000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('79000000-0000-0000-0000-000000000003','Recorded publication fixture');
insert into hsr.memberships values('79000000-0000-0000-0000-000000000003','79000000-0000-0000-0000-000000000001');
select set_config('request.jwt.claim.sub','79000000-0000-0000-0000-000000000001',true);
set local role authenticated;
create temporary table recorded as select '79000000-0000-0000-0000-000000000003'::uuid as organization_id,x.*
    from hsr.create_recorded_traversal('79000000-0000-0000-0000-000000000003','79000000-0000-0000-0000-000000000004',1) x;
reset role;
grant select on recorded to hsr_worker;
set local role hsr_worker;
create temporary table original_claim as select * from hsr.claim_scan_job_with_traversal('79000000-0000-0000-0000-000000000007',60);
reset role;
create temporary table original_attempt as select to_jsonb(a) as value from hsr.job_attempts a join recorded r on r.job_id=a.job_id;

create function pg_temp.pub_claim(p_token uuid,p_bundle text default repeat('a',64),
    p_producer uuid default '79000000-0000-0000-0000-000000000007',p_generation bigint default 1,p_ttl integer default 60)
returns table(status text,lease_until timestamptz,record_observation boolean,traversal_route integer)
language sql as $$select x.* from pg_temp.recorded r cross join lateral hsr.claim_recorded_publication(r.organization_id,r.scan_id,r.job_id,p_generation,p_producer,p_bundle,p_token,p_ttl) x$$;
create function pg_temp.pub_renew(p_token uuid,p_bundle text default repeat('a',64),
    p_producer uuid default '79000000-0000-0000-0000-000000000007',p_generation bigint default 1)
returns boolean language sql as $$select hsr.renew_recorded_publication(r.organization_id,r.job_id,p_generation,p_producer,p_bundle,p_token,60) from pg_temp.recorded r$$;
create function pg_temp.pub_finish(p_token uuid,p_index text default repeat('b',64),p_digest text default repeat('d',64),
    p_bundle text default repeat('a',64),p_producer uuid default '79000000-0000-0000-0000-000000000007',p_generation bigint default 1)
returns boolean language sql as $$select hsr.complete_recorded_publication(r.organization_id,r.job_id,p_generation,p_producer,p_bundle,p_token,p_digest,
    replace(r.organization_id::text,'-','')||'/'||replace(r.scan_id::text,'-','')||'/'||replace(r.job_id::text,'-','')||'/'||p_generation::text||'/observation.json',
    p_index,repeat('c',64),200000) from pg_temp.recorded r$$;
grant execute on function pg_temp.pub_claim(uuid,text,uuid,bigint,integer),pg_temp.pub_renew(uuid,text,uuid,bigint),
    pg_temp.pub_finish(uuid,text,text,text,uuid,bigint) to hsr_worker,authenticated,anon;

select ok((select bool_and(relrowsecurity) from pg_class where oid in ('hsr.recorded_publications'::regclass,'hsr.recorded_publication_events'::regclass)), 'PublicationTablesEnforceRls');
select ok(not has_table_privilege('authenticated','hsr.recorded_publications','SELECT') and not has_table_privilege('hsr_worker','hsr.recorded_publications','SELECT'), 'PublicationCapabilitiesAreNotReadableByUsersOrWorker');
select ok(not has_table_privilege('anon','hsr.recorded_publication_events','SELECT') and not has_table_privilege('hsr_worker','hsr.recorded_publications','UPDATE'), 'PublicationTablesExposeNoAnonymousOrWorkerDml');
select ok(not has_function_privilege('anon','hsr.claim_recorded_publication(uuid,uuid,uuid,bigint,uuid,text,uuid,integer)','EXECUTE') and not has_function_privilege('authenticated','hsr.claim_recorded_publication(uuid,uuid,uuid,bigint,uuid,text,uuid,integer)','EXECUTE'), 'PublicationClaimIsNotACallerRpc');
select ok(not has_function_privilege('hsr_worker','hsr._active_recorded_publication(uuid,uuid,bigint,uuid,text,uuid)','EXECUTE'), 'PublicationPrivateHelperCannotBecomeAWorkerBypass');

set local role hsr_worker;
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011')),'Busy','PublicationCannotOverlapAnActiveExecutionLease');
select throws_ok($$select * from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011',repeat('a',64),'79000000-0000-0000-0000-000000000007',1,4)$$,'22023','INVALID_RECORDED_PUBLICATION_REQUEST','PublicationRejectsTtlBelowFive');
select throws_ok($$select * from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011',repeat('a',64),'79000000-0000-0000-0000-000000000007',1,61)$$,'22023','INVALID_RECORDED_PUBLICATION_REQUEST','PublicationRejectsTtlAboveSixty');
select throws_ok($$select * from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011','invalid')$$,'22023','INVALID_RECORDED_PUBLICATION_REQUEST','PublicationRejectsMalformedBundleDigest');
select throws_ok($$select * from pg_temp.pub_claim('00000000-0000-0000-0000-000000000000')$$,'22023','INVALID_RECORDED_PUBLICATION_REQUEST','PublicationRejectsEmptyCapability');
reset role;
select is((select count(*) from hsr.recorded_publications),0::bigint,'BusyPublicationDoesNotCreateAnyLease');
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from recorded);
set local role hsr_worker;
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011',repeat('a',64),'79000000-0000-0000-0000-000000000008')),'Rejected','PublicationRejectsForeignOriginalProducer');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011',repeat('a',64),'79000000-0000-0000-0000-000000000007',2)),'Rejected','PublicationRejectsReplacementGeneration');
create temporary table publication as select * from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011');
select is((select status from publication),'Claimed','PublicationAcquiresOnlyStorageAuthorityForTheOriginalAttempt');
select ok((select record_observation and traversal_route=1 and lease_until>clock_timestamp() from publication),'PublicationReturnsActualAdmittedRecordingFlags');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'79000000-0000-0000-0000-000000000007',1,'79000000-0000-0000-0000-000000000011',30) from original_claim c),false,'PublicationTokenCannotRenewExecution');
select is((select hsr.renew_scan_job(c.organization_id,c.job_id,'79000000-0000-0000-0000-000000000007',1,c.lease_token,30) from original_claim c),false,'PublicationDoesNotResurrectOriginalExecutionToken');
select is((select pg_temp.pub_renew(c.lease_token) from original_claim c),false,'ExecutionTokenCannotRenewPublication');
select is(pg_temp.pub_renew('79000000-0000-0000-0000-000000000011'),true,'PublicationRenewsItsIndependentStorageFence');
select is(pg_temp.pub_renew('79000000-0000-0000-0000-000000000011',repeat('e',64)),false,'PublicationRenewalCannotSwitchOriginalBundle');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000012')),'Busy','PublicationFencesAConcurrentDifferentRecovery');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000011')),'Claimed','PublicationSameCapabilityClaimIsIdempotent');
reset role;
select is((select to_jsonb(a) from hsr.job_attempts a join recorded r on r.job_id=a.job_id),(select value from original_attempt),'PublicationPreservesOriginalExecutionAttemptExactly');
select ok((select j.state='blocked' and j.generation=1 and j.result_digest is null and j.archive_index_sha256 is null from hsr.scan_jobs j join recorded r on r.job_id=j.id),'PublicationClaimCannotInventACompletedReceipt');
select is((select count(*) from hsr.job_leases l join recorded r on r.job_id=l.job_id),0::bigint,'PublicationCreatesNoExecutionLease');
select is((select count(*) from hsr.recorded_publication_events where kind='Claimed'),1::bigint,'IdempotentPublicationClaimDoesNotDuplicateAuditHistory');
delete from hsr.memberships where organization_id='79000000-0000-0000-0000-000000000003';
set local role hsr_worker;
select is(pg_temp.pub_renew('79000000-0000-0000-0000-000000000011'),false,'PublicationRenewalRechecksOriginalCreatorMembership');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000011'),false,'RevokedMembershipCannotCompletePublication');
reset role;
insert into hsr.memberships values('79000000-0000-0000-0000-000000000003','79000000-0000-0000-0000-000000000001');
update hsr.scan_jobs set traversal_route=2 where id=(select job_id from recorded);
set local role hsr_worker;
select ok(not pg_temp.pub_renew('79000000-0000-0000-0000-000000000011') and not pg_temp.pub_finish('79000000-0000-0000-0000-000000000011'),'PublicationCannotChangeTheRecordedRouteAfterClaim');
reset role;
update hsr.scan_jobs set traversal_route=1 where id=(select job_id from recorded);
set local role hsr_worker;
select is((select hsr.fail_recorded_publication(r.organization_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000011','WORKER_PUBLICATION_STORAGE_UNCONFIRMED') from recorded r),true,'PublicationFailureReleasesOnlyItsStorageFence');
select is(pg_temp.pub_renew('79000000-0000-0000-0000-000000000011'),false,'FailedPublicationCannotContinueWritingUnderOldAuthority');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000012')),'Claimed','PublicationCanRetrySameOriginalAfterExplicitFailure');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000011'),false,'StalePublicationCannotFinishAfterReplacementStorageClaim');
select throws_ok($$select pg_temp.pub_finish('79000000-0000-0000-0000-000000000012','invalid')$$,'22023','INVALID_RECORDED_PUBLICATION_RECEIPT','PublicationRejectsMalformedArchiveReceipt');
reset role;
select ok((select count(*)=1 and bool_and(error_code='WORKER_PUBLICATION_STORAGE_UNCONFIRMED') from hsr.recorded_publication_events where kind='Failed'),'PublicationKeepsTheActualSafeFailureHistory');
update hsr.recorded_publications set claimed_at=clock_timestamp()-interval '11 minutes' where job_id=(select job_id from recorded);
set local role hsr_worker;
select is(pg_temp.pub_renew('79000000-0000-0000-0000-000000000012'),false,'PublicationCannotRenewPastItsSeparateTenMinuteBudget');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000012'),false,'PublicationCannotCompletePastItsSeparateTenMinuteBudget');
reset role;
update hsr.recorded_publications set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from recorded);
set local role hsr_worker;
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000013',repeat('e',64))),'Rejected','ExpiredPublicationCannotRebindToDifferentRetainedBytes');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000013')),'Claimed','ExpiredStorageLeaseCanBeReclaimedWithoutAnInputAttempt');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000013'),true,'PublicationCompletesExactOriginalProducerReceipt');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000013'),true,'CompletedPublicationAcceptsOnlyExactIdempotentReceipt');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000013',repeat('e',64)),false,'CompletedPublicationCannotOverwriteArchiveIndex');
select is(pg_temp.pub_finish('79000000-0000-0000-0000-000000000013',repeat('b',64),repeat('e',64)),false,'CompletedPublicationCannotOverwriteObservationDigest');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000014')),'AlreadyCompleted','LostPublicationResponseReconcilesWithoutStorageOrInputReplay');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000014',repeat('e',64))),'Rejected','CompletedPublicationStillBindsItsOriginalBundle');
reset role;
select ok((select j.state='completed' and j.generation=1 and j.last_error_code is null and j.archive_worker_id='79000000-0000-0000-0000-000000000007' and j.result_digest=repeat('d',64) and j.archive_index_sha256=repeat('b',64) from hsr.scan_jobs j join recorded r on r.job_id=j.id),'PublicationStoresTheOriginalIdentityAndExactReceipt');
select is((select to_jsonb(a) from hsr.job_attempts a join recorded r on r.job_id=a.job_id),(select value from original_attempt),'CompletedPublicationNeverRewritesExecutionProvenance');
select is((select count(*) from hsr.recorded_publication_events where kind='Completed'),1::bigint,'ExactCompletionRetryDoesNotDuplicateThePublicationEvent');
create temporary table completed_job as select * from hsr.scan_jobs where id=(select job_id from recorded);
update hsr.scan_jobs set state='blocked',result_digest=null,result_reference=null,archive_index_sha256=null,
    archive_manifest_sha256=null,archive_bytes=null,archive_worker_id=null where id=(select job_id from recorded);
set local role hsr_worker;
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000015')),'Rejected','CompletedPublicationCannotBeReopenedByAnInconsistentJobState');
reset role;
update hsr.scan_jobs j set state=c.state,result_digest=c.result_digest,result_reference=c.result_reference,
    archive_index_sha256=c.archive_index_sha256,archive_manifest_sha256=c.archive_manifest_sha256,
    archive_bytes=c.archive_bytes,archive_worker_id=c.archive_worker_id from completed_job c where j.id=c.id;
set local role authenticated;
select is((select count(*) from hsr.recorded_publication_events),5::bigint,'CurrentMemberCanReadSafePublicationHistory');
reset role;
select set_config('request.jwt.claim.sub','79000000-0000-0000-0000-000000000099',true);
set local role authenticated;
select is((select count(*) from hsr.recorded_publication_events),0::bigint,'ForeignMemberCannotReadPublicationHistory');
reset role;
set local role hsr_worker;
select is(pg_temp.pub_finish(null),false,'CompletedPublicationRejectsANullCapabilityExplicitly');
select is((select status from pg_temp.pub_claim('79000000-0000-0000-0000-000000000014',repeat('a',64),'79000000-0000-0000-0000-000000000007',1,5)),'AlreadyCompleted','PublicationAcceptsExactMinimumTtlWithoutReopeningCompletion');
select is((select status from recorded r cross join lateral hsr.claim_recorded_publication('79000000-0000-0000-0000-000000000099',r.scan_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000014',30)),'Rejected','PublicationCannotSelectAnotherTenant');
select is((select status from recorded r cross join lateral hsr.claim_recorded_publication(r.organization_id,'79000000-0000-0000-0000-000000000099',r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000014',30)),'Rejected','PublicationCannotSubstituteAnotherScan');
select throws_ok($$select hsr.fail_recorded_publication(r.organization_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000013','private detail') from recorded r$$,'22023','INVALID_RECORDED_PUBLICATION_ERROR','PublicationRejectsUnsafeDiagnosticText');
select throws_ok($$select hsr.complete_recorded_publication(r.organization_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000013',repeat('d',64),'unused',repeat('b',64),repeat('c',64),0) from recorded r$$,'22023','INVALID_RECORDED_PUBLICATION_RECEIPT','PublicationRejectsZeroArchiveBytes');
select throws_ok($$select hsr.complete_recorded_publication(r.organization_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000013',repeat('d',64),'unused',repeat('b',64),repeat('c',64),26218497) from recorded r$$,'22023','INVALID_RECORDED_PUBLICATION_RECEIPT','PublicationRejectsArchiveAboveRecordedLimit');
select throws_ok($$select hsr.complete_recorded_publication(r.organization_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000013',repeat('d',64),'unrelated/observation.json',repeat('b',64),repeat('c',64),200000) from recorded r$$,'22023','INVALID_RECORDED_PUBLICATION_RECEIPT','PublicationRejectsAnUnboundObservationReference');
select throws_ok($$select x.* from recorded r cross join lateral hsr._active_recorded_publication('79000000-0000-0000-0000-000000000003',r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000013') x$$,'42501',null,'WorkerCannotCallPrivatePublicationHelper');
reset role;
set local role authenticated;
select throws_ok($$select * from pg_temp.pub_claim('79000000-0000-0000-0000-000000000014')$$,'42501',null,'AuthenticatedCallerCannotExecutePublicationClaim');
reset role;
set local role anon;
select throws_ok($$select * from hsr.claim_recorded_publication('79000000-0000-0000-0000-000000000003','79000000-0000-0000-0000-000000000004','79000000-0000-0000-0000-000000000005',1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000014',30)$$,'42501',null,'AnonymousCallerCannotExecutePublicationClaim');
reset role;
select set_config('request.jwt.claim.sub','79000000-0000-0000-0000-000000000001',true);
create temporary table unrecorded as select * from hsr.create_scan('79000000-0000-0000-0000-000000000003','79000000-0000-0000-0000-000000000021');
create temporary table queued_recording as select * from hsr.create_recorded_traversal('79000000-0000-0000-0000-000000000003','79000000-0000-0000-0000-000000000022',1);
grant select on unrecorded,queued_recording to hsr_worker;
set local role hsr_worker;
select is((select status from unrecorded r cross join lateral hsr.claim_recorded_publication('79000000-0000-0000-0000-000000000003',r.scan_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000014',30)),'Rejected','UnrecordedAdmissionCannotAcquirePublicationAuthority');
select is((select status from queued_recording r cross join lateral hsr.claim_recorded_publication('79000000-0000-0000-0000-000000000003',r.scan_id,r.job_id,1,'79000000-0000-0000-0000-000000000007',repeat('a',64),'79000000-0000-0000-0000-000000000014',30)),'Rejected','UnstartedRecordingCannotInventAnOriginalProducer');
reset role;
select * from finish();
rollback;
