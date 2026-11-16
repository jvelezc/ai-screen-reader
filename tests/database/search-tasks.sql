-- Disposable database only. Synthetic actors; no customer sessions or recorded website input.
begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,anon,hsr_worker;
select no_plan();
insert into auth.users(id) values('a1000000-0000-0000-0000-000000000001'),('a1000000-0000-0000-0000-000000000002');
insert into auth.sessions(id,user_id,not_after) values
 ('a1100000-0000-0000-0000-000000000001','a1000000-0000-0000-0000-000000000001',clock_timestamp()+interval '1 hour'),
 ('a1100000-0000-0000-0000-000000000002','a1000000-0000-0000-0000-000000000002',clock_timestamp()+interval '1 hour');
insert into hsr.organizations(id,name) values('a1200000-0000-0000-0000-000000000001','Search A'),('a1200000-0000-0000-0000-000000000002','Search B');
insert into hsr.memberships values('a1200000-0000-0000-0000-000000000001','a1000000-0000-0000-0000-000000000001'),
 ('a1200000-0000-0000-0000-000000000001','a1000000-0000-0000-0000-000000000002'),
 ('a1200000-0000-0000-0000-000000000002','a1000000-0000-0000-0000-000000000002');
set local request.jwt.claim.sub='a1000000-0000-0000-0000-000000000001';
set local request.jwt.claims='{"sub":"a1000000-0000-0000-0000-000000000001","session_id":"a1100000-0000-0000-0000-000000000001"}';
create temporary table tasks(tag text,body jsonb);
create temporary table claim(body jsonb);
grant select,insert on tasks,claim to authenticated,hsr_worker;
set local role anon;
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working')$$,'42501',null,'AnonymousCannotAdmitSearchTask');
select throws_ok($$select hsr.read_search_task('a1200000-0000-0000-0000-000000000001')$$,'42501',null,'AnonymousCannotReadSearchTask');
select throws_ok($$select hsr.cancel_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid())$$,'42501',null,'AnonymousCannotCancelSearchTask');
reset role;
set local role authenticated;
select throws_ok($$select * from hsr.search_tasks$$,'42501',null,'SearchClientsCannotReadPrivateLeases');
select throws_ok($$select * from pgmq.q_hsr_search_task_jobs$$,'42501',null,'SearchClientsCannotReadInputQueue');
select throws_ok($$select hsr.claim_search_task(gen_random_uuid())$$,'42501',null,'SearchClientsCannotClaimWorkerAuthority');
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000002',gen_random_uuid(),'search-working')$$,'42501','HSR_SESSION_REQUIRED','SearchAdmissionRequiresWorkspaceMembership');
select throws_ok($$select hsr.read_search_task('a1200000-0000-0000-0000-000000000002')$$,'42501','HSR_SESSION_REQUIRED','SearchReadsRequireWorkspaceMembership');
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001',null,'search-working')$$,'22023','HSR_SEARCH_TASK_REQUEST_INVALID','SearchAdmissionRequiresRequestIdentity');
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com')$$,'22023','HSR_SEARCH_TASK_REQUEST_INVALID','SearchAdmissionCannotAcceptPublicUrl');
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'checkout-working')$$,'22023','HSR_SEARCH_TASK_REQUEST_INVALID','SearchAdmissionCannotSubstituteAnotherMock');
insert into tasks values('initial',hsr.create_search_task('a1200000-0000-0000-0000-000000000001','a1300000-0000-0000-0000-000000000001','search-working'));
select is((select body->>'State' from tasks where tag='initial'),'queued','SearchAdmissionDoesNotClaimPhysicalExecution');
select is((select body->'Job'->>'Profile' from tasks where tag='initial'),'hsr-embedded-search-task-v1','SearchAdmissionKeepsItsIndependentProfile');
select is((select body->'Job'->'Source'->>'Sha256' from tasks where tag='initial'),'b6de520a94841c634d567a30a3a5809a7d51f0dee43354c1e6f6297ef35a2f12','SearchAdmissionPinsCompiledSourceHash');
select is((select body->'Job'->>'Generation' from tasks where tag='initial'),'1','SearchAdmissionNeverGrantsReplayGeneration');
select ok((select not(body ? 'Token') and not(body ? 'SessionId') and not(body ? 'LeaseToken') and not(body->'Job' ? 'ParentScanId') from tasks where tag='initial'),'SearchMetadataOmitsSecretsAndPublicReviewIdentity');
select is(hsr.create_search_task('a1200000-0000-0000-0000-000000000001','a1300000-0000-0000-0000-000000000001','search-working'),(select body from tasks where tag='initial'),'SearchRequestReconciliationKeepsOneOriginalJob');
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001','a1300000-0000-0000-0000-000000000001','search-keyboard-blocked')$$,'22023','HSR_REQUEST_CONFLICT','SearchRequestCannotChangeCompiledSource');
reset role;
set local request.jwt.claim.sub='a1000000-0000-0000-0000-000000000002';
set local request.jwt.claims='{"sub":"a1000000-0000-0000-0000-000000000002","session_id":"a1100000-0000-0000-0000-000000000002"}';
set local role authenticated;
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001','a1300000-0000-0000-0000-000000000001','search-working')$$,'22023','HSR_REQUEST_CONFLICT','SearchRequestCannotChangeOriginalCreator');
reset role;
set local request.jwt.claim.sub='a1000000-0000-0000-0000-000000000001';
set local request.jwt.claims='{"sub":"a1000000-0000-0000-0000-000000000001","session_id":"a1100000-0000-0000-0000-000000000001"}';
set local role hsr_worker;
select throws_ok($$select * from hsr.search_tasks$$,'42501',null,'SearchWorkerCannotBypassLeaseRpc');
insert into claim select hsr.claim_search_task('a1400000-0000-0000-0000-000000000001');
select is((select body->>'Status' from claim),'Claimed','SearchWorkerClaimsOneDurableJob');
select is(hsr.claim_search_task(gen_random_uuid())->>'Status','AtCapacity','SearchNativeJobsCannotOverlap');
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',null,'Collecting'),'NullCapabilityCannotRenewSearchInput');
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),gen_random_uuid(),(select (body->>'Token')::uuid from claim),'Collecting'),'ForeignWorkerCannotRenewSearchInput');
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000002',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'Collecting'),'ForeignOrganizationCannotRenewSearchInput');
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'ArbitraryInput'),'SearchInputPhaseIsClosed');
select ok(hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'Collecting'),'CurrentSearchCapabilityRenewsInput');
reset role;
set local role authenticated;
select is(hsr.cancel_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->>'State','cancelled','SearchCancellationRevokesFutureInput');
reset role;
set local role hsr_worker;
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'Collecting'),'CancelledSearchCannotRegainAuthority');
select is(hsr.claim_search_task(gen_random_uuid())->>'Status','Empty','CancelledSearchIsNeverRequeued');
reset role;
set local role authenticated;
select is(hsr.create_search_task('a1200000-0000-0000-0000-000000000001','a1300000-0000-0000-0000-000000000001','search-working')->'Job',
 (select body->'Job' from tasks where tag='initial'),'CancelledSearchRequestKeepsOriginalIdentity');
insert into tasks values('expiry',hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-keyboard-blocked'));
reset role;
truncate claim;
set local role hsr_worker;
insert into claim select hsr.claim_search_task('a1400000-0000-0000-0000-000000000001');
reset role;
update hsr.search_tasks set lease_expires_at=clock_timestamp()-interval '1 second' where state='leased';
set local role authenticated;
select is(hsr.read_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->>'State','interrupted','SearchReadDoesNotDescribeExpiredLeaseAsRunning');
select is(hsr.read_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->>'ErrorCode','SEARCH_TASK_LEASE_EXPIRED','ExpiredSearchReadKeepsPreciseClosedReason');
reset role;
set local role hsr_worker;
select is(hsr.claim_search_task(gen_random_uuid())->>'Status','Empty','ExpiredSearchInputIsNeverReplayed');
reset role;
select is((select state from hsr.search_tasks where id=(select (body->'Job'->>'JobId')::uuid from claim)),'interrupted','ExpiredSearchRetainsTerminalState');
set local role authenticated;
insert into tasks values('timeout',hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working'));
reset role;
truncate claim;
set local role hsr_worker;
insert into claim select hsr.claim_search_task('a1400000-0000-0000-0000-000000000001');
reset role;
update hsr.search_tasks set started_at=clock_timestamp()-interval '6 minutes' where state='leased';
set local role hsr_worker;
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'Collecting'),'SearchHeartbeatCannotExtendMaximumLifetime');
select ok(hsr.block_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'SEARCH_TASK_AUTHORITY_LOST'),'TimedOutSearchRetainsBlockedReason');
reset role;
set local role authenticated;
insert into tasks values('publish',hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-keyboard-blocked'));
reset role;
truncate claim;
set local role hsr_worker;
insert into claim select hsr.claim_search_task('a1400000-0000-0000-0000-000000000001');
reset role;
create temporary table publication as select jsonb_build_object('Schema','hsr-search-task-publication-v1',
 'ExecutionId','a1500000-0000-0000-0000-000000000001','ManifestSha256',repeat('a',64),'ManifestBytes',4000,
 'ArchiveSha256',repeat('b',64),'ArchiveBytes',1000000,'Files',100,'Bytes',900000,
 'Observation','KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved','NativeRemoved',true,'OriginalsVerified',true,
 'Scope','ControlledEmbeddedSearchTaskExperiments','WebsiteEvidenceAssessment','NotAssessed','VideoAlignment','UnverifiedWithoutCalibrationMarkers','LegalCaseDetermined',false,'IndexSha256',repeat('c',64)) body;
grant select on publication to hsr_worker;
set local role hsr_worker;
select ok(hsr.prepare_search_task_publication('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),(select jsonb_set(body,'{OriginalsVerified}','false') from publication)),'SearchPreparesOnlyRetainedLocalOriginals');
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'Collecting'),'PreparedSearchCannotRegainBrowserInputAuthority');
select ok(hsr.renew_search_publication('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim)),'SearchPreparationGrantsStorageOnlyRenewal');
reset role;
set local role authenticated;
select ok(hsr.read_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->>'State'='publishing'
 and hsr.read_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->'Publication'='null'::jsonb,'PreparedSearchDoesNotClaimProviderReadbackOrCompletion');
reset role;
set local role hsr_worker;
select ok(not hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),null),'SearchPublicationCannotOmitReceipt');
select ok(not hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'[]'),'SearchPublicationCannotAcceptNonObject');
select ok(not hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),(select body-'OriginalsVerified' from publication)),'SearchPublicationRequiresEveryNamedFact');
select ok(not hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),
 (select jsonb_set(body,array[mutation.key],mutation.value) from publication)),mutation.name)
from (values
 ('NativeRemoved','false'::jsonb,'SearchPublicationRequiresNativeAbsence'),
 ('OriginalsVerified','false'::jsonb,'SearchPublicationRequiresOriginalReadback'),
 ('LegalCaseDetermined','true'::jsonb,'SearchPublicationCannotInventLegalCase'),
 ('WebsiteEvidenceAssessment','"WebsiteEvidenceReady"'::jsonb,'SearchPublicationCannotClaimWebsiteAssessment'),
 ('Scope','"PublicServiceNavigationOnly"'::jsonb,'SearchPublicationCannotBecomePublicWebsiteFinding'),
 ('ManifestBytes','524289'::jsonb,'SearchManifestPublicationIsBounded'),
 ('ArchiveBytes','27262977'::jsonb,'SearchArchivePublicationIsBounded'),
 ('Bytes','26214401'::jsonb,'SearchOriginalPublicationIsBounded'),
 ('Files','1'::jsonb,'SearchPublicationCannotOmitFiveContextArtifacts'),
 ('Observation','"WebsiteCompliant"'::jsonb,'SearchPublicationCannotInventCompliance'),
 ('ExecutionId','"00000000-0000-0000-0000-000000000000"'::jsonb,'SearchPublicationRequiresExecutionIdentity'),
 ('Files','"100"'::jsonb,'SearchPublicationRejectsStringEncodedCounts'),
 ('ArchiveBytes','1'::jsonb,'SearchPublicationCannotShrinkUncompressedOriginals'),
 ('IndexSha256','"invalid"'::jsonb,'SearchPublicationRequiresExactStoredIndexDigest')) mutation(key,value,name);
select ok(not hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),gen_random_uuid(),(select (body->>'Token')::uuid from claim),(select body from publication)),'ForeignProducerCannotPublishSearchOriginals');
select ok(hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),(select body from publication)),'SearchOriginalCapabilityPublishesBoundReceipt');
select ok(hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),(select body from publication)),'SearchPublicationRetryDoesNotResendInput');
select ok(not hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),(select jsonb_set(body,'{ManifestSha256}',to_jsonb(repeat('c',64))) from publication)),'SearchPublicationCannotRewriteOriginalReceipt');
reset role;
set local role authenticated;
select is(hsr.read_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->>'State','completed','SearchCompletedReadKeepsPublication');
-- A storage-only recovery receives a new capability and publisher, preserving the original producer and input generation.
insert into tasks values('recover',hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working'));
reset role;
truncate claim;
set local role hsr_worker;
insert into claim select hsr.claim_search_task('a1400000-0000-0000-0000-000000000001');
select ok(hsr.prepare_search_task_publication('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),(select jsonb_set(body,'{OriginalsVerified}','false') from publication)),'RecoverableSearchHasDurableOriginalReference');
select ok(hsr.block_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000001',(select (body->>'Token')::uuid from claim),'SEARCH_TASK_PUBLICATION_UNCONFIRMED'),'SearchProviderFailureRetainsPreparedOriginal');
select is(hsr.claim_search_task(gen_random_uuid())->>'Status','Empty','FailedSearchPublicationNeverRequeuesWebsiteInput');
select is(hsr.claim_search_publication(gen_random_uuid())->>'Status','Empty','SearchPublicationRecoveryHonorsCooldown');
reset role;
update hsr.search_tasks set publication_retry_at=clock_timestamp()-interval '1 second' where state='blocked' and pending_publication is not null;
truncate claim;
set local role hsr_worker;
insert into claim select hsr.claim_search_publication('a1400000-0000-0000-0000-000000000002');
select is((select body->>'Status' from claim),'PublicationOnly','SearchRecoveryGrantsOnlyStorageCapability');
select is((select body->>'ProducerId' from claim),'a1400000-0000-0000-0000-000000000001','SearchRecoveryPreservesOriginalProducer');
select is((select body->'Job'->>'Generation' from claim),'1','SearchRecoveryDoesNotCreateAnotherInputGeneration');
select ok(not hsr.renew_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000002',(select (body->>'Token')::uuid from claim),'Collecting'),'SearchRecoveryCapabilityCannotAuthorizeNativeInput');
select ok(hsr.renew_search_publication('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000002',(select (body->>'Token')::uuid from claim)),'SearchRecoveryRenewsOnlyItsStorageLease');
select ok(hsr.finish_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim),'a1400000-0000-0000-0000-000000000002',(select (body->>'Token')::uuid from claim),(select body from publication)),'SearchRecoveryPublishesSameRetainedOriginals');
reset role;
set local role authenticated;
select is(hsr.read_search_task('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->>'ProducerId','a1400000-0000-0000-0000-000000000001','SearchCompletedRecoveryKeepsCollectorIdentity');
select is(jsonb_array_length(hsr.read_search_task_diagnostics('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->'PublicationAttempts'),2,'SearchDiagnosticsKeepBothStorageAttempts');
select is(hsr.read_search_task_diagnostics('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->'PublicationAttempts'->0->>'State','Blocked','SearchRecoveryPreservesOriginalStorageFailure');
select is(hsr.read_search_task_diagnostics('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->'PublicationAttempts'->1->>'State','Published','SearchRecoveryAppendsSuccessfulReadback');
select ok(not(hsr.read_search_task_diagnostics('a1200000-0000-0000-0000-000000000001',(select (body->'Job'->>'JobId')::uuid from claim))->'PublicationAttempts'->1->'WebsiteInputRetried')::boolean,'SearchStorageRecoveryCannotClaimWebsiteInputReplay');
select throws_ok($$select * from hsr.search_task_publication_history$$,'42501',null,'SearchClientsCannotReadUnscopedStorageHistory');
insert into tasks values('revoked',hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working'));
reset role;
update auth.sessions set not_after=clock_timestamp()-interval '1 second' where id='a1100000-0000-0000-0000-000000000001';
set local role hsr_worker;
select is(hsr.claim_search_task(gen_random_uuid())->>'Status','AuthorityLost','ExpiredSessionCannotStartQueuedSearchInput');
reset role;
set local role authenticated;
select throws_ok($$select hsr.read_search_task('a1200000-0000-0000-0000-000000000001')$$,'42501','HSR_SESSION_REQUIRED','ExpiredSessionCannotReadCompletedSearch');
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working')$$,'42501','HSR_SESSION_REQUIRED','ExpiredSessionCannotAdmitSearch');
reset role;
update auth.sessions set not_after=clock_timestamp()+interval '1 hour' where id='a1100000-0000-0000-0000-000000000001';
set local role authenticated;
insert into tasks select 'quota',hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working') from generate_series(1,5);
select throws_ok($$select hsr.create_search_task('a1200000-0000-0000-0000-000000000001',gen_random_uuid(),'search-working')$$,'54000','HSR_SEARCH_TASK_QUEUE_FULL','SearchAdmissionBoundsPendingWorkspaceJobs');
select is(hsr.create_search_task('a1200000-0000-0000-0000-000000000001',(select (body->>'RequestId')::uuid from tasks where tag='quota' limit 1),'search-working')->>'State','queued','FullSearchQueueStillReconcilesExistingRequest');
reset role;
delete from hsr.memberships where organization_id='a1200000-0000-0000-0000-000000000001' and user_id='a1000000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($$select hsr.read_search_task('a1200000-0000-0000-0000-000000000001')$$,'42501','HSR_SESSION_REQUIRED','RevokedMembershipCannotReadSearchOriginals');
reset role;
select * from finish();
rollback;
