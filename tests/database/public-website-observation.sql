-- Synthetic identities/capabilities only. Run exclusively in the disposable test database.
begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,anon,hsr_worker;
select no_plan();
insert into auth.users(id) values('83000000-0000-0000-0000-000000000001'),('83000000-0000-0000-0000-000000000002'),('83000000-0000-0000-0000-000000000003');
insert into hsr.organizations(id,name) values('83100000-0000-0000-0000-000000000001','Public source A'),('83100000-0000-0000-0000-000000000002','Public source B');
insert into hsr.memberships values('83100000-0000-0000-0000-000000000001','83000000-0000-0000-0000-000000000001'),
 ('83100000-0000-0000-0000-000000000002','83000000-0000-0000-0000-000000000002'),
 ('83100000-0000-0000-0000-000000000001','83000000-0000-0000-0000-000000000003');
create temporary table public_receipts(tag text,organization_id uuid,scan_id uuid,job_id uuid);
create temporary table public_claims(tag text,body jsonb);
create temporary table public_payload(body text);
grant select on public_receipts to authenticated,hsr_worker;
grant select,insert,update on public_claims,public_payload to hsr_worker;
create function pg_temp.public_add(p_tag text,p_org uuid,p_actor uuid,p_request uuid,p_url text default 'https://example.com/') returns void
language plpgsql as $$ declare receipt record; begin
 perform set_config('request.jwt.claim.sub',p_actor::text,true);
 select * into strict receipt from hsr.create_public_scan(p_org,p_request,p_url,'example.com');
 insert into public_receipts values(p_tag,p_org,receipt.scan_id,receipt.job_id);
end $$;
select pg_temp.public_add('first','83100000-0000-0000-0000-000000000001','83000000-0000-0000-0000-000000000001','83200000-0000-0000-0000-000000000001');

set local role anon;
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com/','example.com')$$,'42501',null,'AnonymousCannotAdmitPublicSource');
select throws_ok($$select hsr.claim_public_scan(gen_random_uuid())$$,'42501',null,'AnonymousCannotClaimPublicSource');
reset role;
set local role authenticated;
set local request.jwt.claim.sub='83000000-0000-0000-0000-000000000001';
select is((select count(*) from hsr.public_scans),1::bigint,'PublicMemberSeesOnlyOwnAdmittedSource');
select is((select target_url from hsr.public_scans),'https://example.com/','PublicAdmissionRetainsDeclaredCanonicalUrl');
select is((select allowed_host from hsr.public_scans),'example.com','PublicAdmissionRetainsExactDnsHost');
select is((select reader_profile from hsr.public_scans),'hsr-approved-public-readonly-v1','PublicAdmissionDoesNotAcquireFixtureProfile');
select is((select created_by::text from hsr.public_scans),'83000000-0000-0000-0000-000000000001','PublicCreatorComesFromAuthenticatedSubject');
select is((select state from hsr.public_scan_jobs),'queued','PublicSourceBeginsQueuedWithoutResult');
select ok((select result_text is null and generation=0 from hsr.public_scan_jobs),'QueuedPublicSourceHasNoInventedObservation');
select results_eq($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001','83200000-0000-0000-0000-000000000001','https://example.com/','example.com')$$,
 $$select scan_id,job_id from public_receipts where tag='first'$$,'PublicRequestRetryReturnsOriginalScanAndJob');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001','83200000-0000-0000-0000-000000000001','https://example.com/other','example.com')$$,'22023','HSR_REQUEST_CONFLICT','PublicRequestCannotChangeItsSource');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000002',gen_random_uuid(),'https://example.com/','example.com')$$,'42501','HSR_MEMBERSHIP_REQUIRED','PublicNonmemberCannotAdmitAnotherWorkspace');
select throws_ok($$select hsr.claim_public_scan(gen_random_uuid())$$,'42501',null,'PublicConsumerCannotBecomeWorker');
select throws_ok($$select hsr.renew_public_scan(gen_random_uuid(),gen_random_uuid(),1,gen_random_uuid(),gen_random_uuid())$$,'42501',null,'PublicConsumerCannotRenewCapability');
select throws_ok($$select hsr.finish_public_scan(gen_random_uuid(),gen_random_uuid(),1,gen_random_uuid(),gen_random_uuid(),'{}',repeat('a',64))$$,'42501',null,'PublicConsumerCannotPublishObservation');
select throws_ok($$select hsr.block_public_scan(gen_random_uuid(),gen_random_uuid(),1,gen_random_uuid(),gen_random_uuid(),'PUBLIC_WEBSITE_SOURCE_UNCONFIRMED')$$,'42501',null,'PublicConsumerCannotBlockWorkerJobs');
select throws_ok($$select * from hsr.public_scan_leases$$,'42501',null,'PublicConsumerCannotReadLeaseCapabilities');
select throws_ok($$update hsr.public_scan_jobs set state='completed'$$,'42501',null,'PublicConsumerCannotWriteJobOutcome');
select throws_ok($$delete from hsr.public_scans$$,'42501',null,'PublicConsumerCannotDeleteScanLedger');
select throws_ok($$select * from pgmq.q_hsr_public_scan_jobs$$,'42501',null,'PublicConsumerCannotReadRawQueue');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',null,'https://example.com/','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicNullRequestKeyRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000000','https://example.com/','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicEmptyRequestKeyRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'http://example.com/','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicPlainHttpRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://user:password@example.com/','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicUrlUserInfoRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://127.0.0.1/','127.0.0.1')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicIpLiteralRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://foreign.example/','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicSourceHostMismatchRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com:443/','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicDatabaseRequiresCanonicalDefaultPort');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com/#fragment','example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicFragmentRejected');
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001',gen_random_uuid(),rpad('https://example.com/',2049,'a'),'example.com')$$,'22023','HSR_INVALID_PUBLIC_SCAN_REQUEST','PublicOversizedUrlRejected');
set local request.jwt.claim.sub='83000000-0000-0000-0000-000000000003';
select throws_ok($$select * from hsr.create_public_scan('83100000-0000-0000-0000-000000000001','83200000-0000-0000-0000-000000000001','https://example.com/','example.com')$$,'22023','HSR_REQUEST_CONFLICT','PublicMemberCannotAdoptAnotherCreatorsRequest');
set local request.jwt.claim.sub='83000000-0000-0000-0000-000000000002';
select is((select count(*) from hsr.public_scans),0::bigint,'PublicForeignWorkspaceCannotReadSource');
select is((select count(*) from hsr.public_scan_jobs),0::bigint,'PublicForeignWorkspaceCannotReadJob');
reset role;
select is((select count(*) from pgmq.q_hsr_public_scan_jobs),1::bigint,'PublicDuplicateAdmissionLeavesOneQueueMessage');

set local role hsr_worker;
insert into public_claims values('first',hsr.claim_public_scan('83300000-0000-0000-0000-000000000001'));
select is((select body->>'status' from public_claims where tag='first'),'Claimed','PublicWorkerClaimsAdmittedSource');
select ok((select body->>'token' ~ '^[a-f0-9-]{36}$' from public_claims where tag='first'),'PublicClaimReturnsBoundNonemptyCapability');
select is((select body->'job'->'Source'->>'TargetUrl' from public_claims where tag='first'),'https://example.com/','PublicClaimUsesAdmittedSourceInsteadOfCallerPayload');
select is((select body->'job'->>'Generation' from public_claims where tag='first'),'1','PublicClaimCreatesOneOriginalAttempt');
select ok((select hsr.renew_public_scan(r.organization_id,r.job_id,1,'83300000-0000-0000-0000-000000000001',(c.body->>'token')::uuid)
 from public_receipts r join public_claims c on c.tag=r.tag where r.tag='first'),'PublicExactCapabilityRenews');
select ok(not (select hsr.renew_public_scan(r.organization_id,r.job_id,1,'83300000-0000-0000-0000-000000000002',(c.body->>'token')::uuid)
 from public_receipts r join public_claims c on c.tag=r.tag where r.tag='first'),'PublicDifferentWorkerCannotRenew');
select ok(not (select hsr.renew_public_scan(r.organization_id,r.job_id,1,'83300000-0000-0000-0000-000000000001',gen_random_uuid()) from public_receipts r where r.tag='first'),'PublicWrongCapabilityCannotRenew');
insert into public_payload select jsonb_build_object('Schema','hsr-public-website-observation-v1','Job',body->'job','WorkerId','83300000-0000-0000-0000-000000000001',
 'ReceivedAt','2026-11-02T19:00:00Z','NativeShutdownConfirmed',true,'ResourcesAbsenceConfirmed',true,'DispatchedInputs',0,'GoalVerified',false,
 'Captures',jsonb_build_array(jsonb_build_object('DocumentId','actual-test-document','CaptureEpoch',1,'DocumentUrl','https://example.com/','Nodes',jsonb_build_array(jsonb_build_object('Id','n1','Role','RootWebArea','Name','Test source'))),
 jsonb_build_object('DocumentId','actual-test-document','CaptureEpoch',1,'DocumentUrl','https://example.com/','Nodes',jsonb_build_array(jsonb_build_object('Id','n1','Role','RootWebArea','Name','Test source')))))::text from public_claims where tag='first';
select ok(not (select hsr.finish_public_scan(r.organization_id,r.job_id,1,'83300000-0000-0000-0000-000000000001',(c.body->>'token')::uuid,p.body,repeat('0',64))
 from public_receipts r join public_claims c on c.tag=r.tag cross join public_payload p where r.tag='first'),'PublicWrongDigestCannotComplete');
select ok(not (select hsr.block_public_scan(r.organization_id,r.job_id,1,'83300000-0000-0000-0000-000000000001',(c.body->>'token')::uuid,null)
 from public_receipts r join public_claims c on c.tag=r.tag where r.tag='first'),'PublicMissingFailureCauseCannotRetireAttempt');
reset role;

-- Each change recomputes its digest: byte integrity cannot authorize invented facts.
create function pg_temp.try_public_payload(p_value text,p_worker uuid default '83300000-0000-0000-0000-000000000001',p_token uuid default null) returns boolean
language plpgsql as $$ declare r record;c jsonb;begin
 select * into strict r from public_receipts where tag='first';select body into strict c from public_claims where tag='first';
 return hsr.finish_public_scan(r.organization_id,r.job_id,1,p_worker,coalesce(p_token,(c->>'token')::uuid),p_value,encode(sha256(convert_to(p_value,'UTF8')),'hex'));
end $$;
grant execute on function pg_temp.try_public_payload(text,uuid,uuid) to hsr_worker;
-- Publication is rolled back so every boundary tests the same live capability.
create function pg_temp.probe_public_payload(p_value text) returns boolean language plpgsql as $$
declare accepted boolean;
begin
 begin
  accepted:=pg_temp.try_public_payload(p_value);
  raise exception using errcode='PZ001';
 exception when sqlstate 'PZ001' then return accepted;
 end;
end $$;
grant execute on function pg_temp.probe_public_payload(text) to hsr_worker;
set local role hsr_worker;
select ok(not pg_temp.try_public_payload('[]'),'PublicArrayCannotBecomeObservation');
select ok(not pg_temp.try_public_payload('{'),'PublicMalformedJsonCannotBecomeObservation');
select ok(not pg_temp.try_public_payload((select (body::jsonb||'{"Schema":"fixture-observation"}')::text from public_payload)),'PublicWrongSchemaCannotComplete');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Job,OrganizationId}','"83100000-0000-0000-0000-000000000002"')::text from public_payload)),'PublicChangedTenantCannotComplete');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Job,Source,TargetUrl}','"https://example.com/other"')::text from public_payload)),'PublicChangedSourceCannotComplete');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Job,Generation}','2')::text from public_payload)),'PublicChangedAttemptCannotComplete');
select ok(not pg_temp.try_public_payload((select (body::jsonb||'{"NativeShutdownConfirmed":false}')::text from public_payload)),'PublicMissingNativeClosureCannotComplete');
select ok(not pg_temp.try_public_payload((select (body::jsonb||'{"ResourcesAbsenceConfirmed":false}')::text from public_payload)),'PublicUnknownResourceAbsenceCannotComplete');
select ok(not pg_temp.try_public_payload((select (body::jsonb||'{"NativeShutdownConfirmed":"true"}')::text from public_payload)),'PublicStringCannotImpersonateNativeClosure');
select ok(not pg_temp.try_public_payload((select (body::jsonb||'{"DispatchedInputs":1}')::text from public_payload)),'PublicPassiveResultCannotInventInput');
select ok(not pg_temp.try_public_payload((select (body::jsonb||'{"GoalVerified":true}')::text from public_payload)),'PublicPassiveResultCannotInventGoal');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Captures}','[]')::text from public_payload)),'PublicEmptyCaptureArrayCannotComplete');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Captures,1,DocumentId}','"different-document"')::text from public_payload)),'PublicChangedDocumentCannotComplete');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Captures,1,DocumentUrl}','"https://foreign.example/"')::text from public_payload)),'PublicForeignDocumentUrlCannotComplete');
select ok(not pg_temp.try_public_payload((select jsonb_set(body::jsonb,'{Captures,1,Nodes}','[]')::text from public_payload)),'PublicEmptySnapshotCannotComplete');
select ok(not pg_temp.try_public_payload((select body from public_payload),'83300000-0000-0000-0000-000000000002'),'PublicWrongWorkerCannotComplete');
select ok(not pg_temp.try_public_payload((select body from public_payload),'83300000-0000-0000-0000-000000000001',gen_random_uuid()),'PublicWrongTokenCannotComplete');
select ok(pg_temp.probe_public_payload((select rpad(body,1048577,' ') from public_payload)),'PublicOriginalBeyondLegacyMiBCanComplete');
select ok(pg_temp.probe_public_payload((select rpad(body,4194304,' ') from public_payload)),'PublicExactFourMiBOriginalCanComplete');
select ok(not pg_temp.probe_public_payload((select rpad(body,4194305,' ') from public_payload)),'PublicOriginalAboveFourMiBCannotComplete');
select ok(pg_temp.try_public_payload((select body from public_payload)),'PublicExactBoundPassiveObservationCompletes');
select ok(not pg_temp.try_public_payload((select body from public_payload)),'PublicRetiredCapabilityCannotCompleteAgain');
reset role;
select is((select state from hsr.public_scan_jobs where id=(select job_id from public_receipts where tag='first')),'completed','PublicCompletedJobStatePersists');
select is((select state from hsr.public_scans where id=(select scan_id from public_receipts where tag='first')),'observed','PublicScanDescribesObservationInsteadOfVerifiedTask');
select is((select count(*) from hsr.public_scan_leases),0::bigint,'PublicCompletionRemovesLiveCapability');
select is((select count(*) from pgmq.q_hsr_public_scan_jobs),0::bigint,'PublicCompletionAcknowledgesOnlyOwnedQueueMessage');
select ok((select result_digest=encode(sha256(convert_to(result_text,'UTF8')),'hex') and result_worker_id='83300000-0000-0000-0000-000000000001'::uuid from hsr.public_scan_jobs),'PublicStoredOriginalBytesAndProducerMatch');
select ok((select state='completed' and finished_at is not null from hsr.public_scan_attempts),'PublicAttemptRetainsItsCompletedHistory');
set local role authenticated;
set local request.jwt.claim.sub='83000000-0000-0000-0000-000000000002';
select is((select count(*) from hsr.public_scan_jobs),0::bigint,'PublicForeignReaderCannotReadCompletedObservation');
select is((select count(*) from hsr.public_scan_attempts),0::bigint,'PublicForeignReaderCannotReadAttemptHistory');
reset role;
select * from finish();
rollback;
