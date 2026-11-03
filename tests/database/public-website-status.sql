-- Synthetic metadata only, exclusively in the disposable test database.
begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,anon,hsr_worker;
select no_plan();
insert into auth.users(id) values('88000000-0000-0000-0000-000000000001'),('88000000-0000-0000-0000-000000000002');
insert into hsr.organizations(id,name) values('88100000-0000-0000-0000-000000000001','Public status A'),('88100000-0000-0000-0000-000000000002','Public status B');
insert into hsr.memberships values('88100000-0000-0000-0000-000000000001','88000000-0000-0000-0000-000000000001'),
 ('88100000-0000-0000-0000-000000000002','88000000-0000-0000-0000-000000000002');
set local request.jwt.claim.sub='88000000-0000-0000-0000-000000000001';
create temporary table public_status_receipt as select * from hsr.create_public_scan('88100000-0000-0000-0000-000000000001',
 '88200000-0000-0000-0000-000000000001','https://example.com/','example.com');
create temporary table public_status_reads(tag text,body jsonb);
grant select on public_status_receipt to authenticated,anon,hsr_worker;
grant select,insert on public_status_reads to authenticated;
set local role anon;
select throws_ok($$select hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',(select scan_id from public_status_receipt))$$,
 '42501',null,'AnonymousCannotReadPublicJobStatus');
set local role hsr_worker;
select throws_ok($$select hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',(select scan_id from public_status_receipt))$$,
 '42501',null,'WorkerCapabilityDoesNotBecomeConsumerStatusAuthority');
set local role authenticated;
set local request.jwt.claim.sub='88000000-0000-0000-0000-000000000002';
select throws_ok($$select hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',(select scan_id from public_status_receipt))$$,
 '42501','HSR_PUBLIC_SCAN_ACCESS_DENIED','ForeignWorkspaceCannotReadPublicStatus');
set local request.jwt.claim.sub='88000000-0000-0000-0000-000000000001';
insert into public_status_reads select 'queued',hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',scan_id) from public_status_receipt;
select is((select body->>'state' from public_status_reads where tag='queued'),'queued','PublicQueuedStatusDoesNotImplyExecution');
select is((select body->>'generation' from public_status_reads where tag='queued'),'0','PublicQueuedStatusHasNoAttempt');
select ok((select body->'worker_permission'='null'::jsonb and body->'producer_id'='null'::jsonb and body->'result_digest'='null'::jsonb
 from public_status_reads where tag='queued'),'PublicQueuedStatusDoesNotInventPermissionOrOriginal');
select ok((select body->>'organization_id'='88100000-0000-0000-0000-000000000001' and body->>'id'=r.scan_id::text
 and body->>'job_id'=r.job_id::text and body->>'created_by'='88000000-0000-0000-0000-000000000001'
 and body->>'request_id'='88200000-0000-0000-0000-000000000001' and body->>'target_url'='https://example.com/'
 and body->>'reader_profile'='hsr-approved-public-readonly-v1' from public_status_reads cross join public_status_receipt r where tag='queued'),
 'PublicStatusRetainsExactAdmissionSourceAndCaller');
select ok((select not body ?| array['token','result_text','capability','password'] from public_status_reads where tag='queued'),
 'PublicStatusCannotReleasePrivateFieldsOrPayload');
select ok(hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001','88400000-0000-0000-0000-000000000001') is null,
 'UnknownScanWithinOwnWorkspaceRemainsAbsent');
select throws_ok($$select hsr.read_public_scan_status(null,(select scan_id from public_status_receipt))$$,'22023',
 'HSR_PUBLIC_SCAN_IDENTITY_INVALID','PublicStatusRejectsMissingWorkspace');
select throws_ok($$select hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000000')$$,
 '22023','HSR_PUBLIC_SCAN_IDENTITY_INVALID','PublicStatusRejectsEmptyScanIdentity');
select is((select count(*) from public_status_reads s,jsonb_object_keys(s.body) where tag='queued'),16::bigint,'PublicStatusHasOnlyItsDeclaredMetadataFields');
reset role;
create temporary table public_status_claim as select hsr.claim_public_scan('88300000-0000-0000-0000-000000000001') as body;
set local role authenticated;
insert into public_status_reads select 'active',hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',scan_id) from public_status_receipt;
select is((select body->>'state' from public_status_reads where tag='active'),'leased','PublicLeasedMetadataUsesSameSnapshotAsPermission');
select is((select body->'worker_permission'->>'status' from public_status_reads where tag='active'),'Active','PublicUnexpiredLeaseHasClockBoundActiveSnapshot');
select ok((select body->'worker_permission'->>'organization_id'=body->>'organization_id'
 and body->'worker_permission'->>'scan_id'=body->>'id' and body->'worker_permission'->>'job_id'=body->>'job_id'
 and body->'worker_permission'->>'generation'=body->>'generation' from public_status_reads where tag='active'),
 'PublicPermissionSnapshotCannotCrossItsJobScope');
select ok((select (body->'worker_permission'->>'checked_at')::timestamptz between clock_timestamp()-interval '5 seconds' and clock_timestamp()
 from public_status_reads where tag='active'),'PublicPermissionUsesCurrentDatabaseClock');
select ok((select (body->'worker_permission'->>'expires_at')::timestamptz>(body->'worker_permission'->>'checked_at')::timestamptz
 from public_status_reads where tag='active'),'PublicActiveSnapshotPrecedesActualLeaseDeadline');
select is((select count(*) from public_status_reads s,jsonb_object_keys(s.body->'worker_permission') where tag='active'),7::bigint,
 'PublicPermissionSnapshotOmitsWorkerToken');
reset role;
select is((select expires_at from hsr.public_scan_leases where job_id=(select job_id from public_status_receipt)),
 (select (body->'worker_permission'->>'expires_at')::timestamptz from public_status_reads where tag='active'),'PublicStatusReadDoesNotRenewLease');
update hsr.public_scan_leases set expires_at=clock_timestamp()-interval '1 second';
set local role authenticated;
insert into public_status_reads select 'expired',hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',scan_id) from public_status_receipt;
select is((select body->'worker_permission'->>'status' from public_status_reads where tag='expired'),'Expired','PublicExpiredLeaseIsNeverReportedActive');
select ok((select (body->'worker_permission'->>'expires_at')::timestamptz<=(body->'worker_permission'->>'checked_at')::timestamptz
 from public_status_reads where tag='expired'),'PublicExpiredSnapshotRetainsActualDeadline');
reset role;
delete from hsr.public_scan_leases;
set local role authenticated;
insert into public_status_reads select 'missing',hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',scan_id) from public_status_receipt;
select is((select body->'worker_permission'->>'status' from public_status_reads where tag='missing'),'Missing','PublicMissingLeaseCannotBecomeActive');
select ok((select body->'worker_permission'->'expires_at'='null'::jsonb from public_status_reads where tag='missing'),
 'PublicMissingLeaseCannotInventExpiry');
reset role;
-- This is deliberately synthetic metadata; no native collection is claimed.
update hsr.public_scan_jobs set state='completed',result_text='synthetic metadata only',result_digest=repeat('c',64),
 result_worker_id='88300000-0000-0000-0000-000000000001';
set local role authenticated;
insert into public_status_reads select 'completed',hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',scan_id) from public_status_receipt;
select is((select body->>'state' from public_status_reads where tag='completed'),'completed','PublicCompletedStatusReportsStoredMetadataOnly');
select ok((select body->>'producer_id'='88300000-0000-0000-0000-000000000001' and body->>'result_digest'=repeat('c',64)
 from public_status_reads where tag='completed'),'PublicCompletedMetadataRetainsActualStoredProducerAndDigest');
select ok((select body->'worker_permission'='null'::jsonb from public_status_reads where tag='completed'),'PublicCompletedMetadataHasNoExecutionPermission');
select ok((select not body ?| array['result_text','token','capability'] from public_status_reads where tag='completed'),
 'PublicCompletedStatusNeverLoadsOriginalPayload');
reset role;
update hsr.public_scan_jobs set state='blocked',result_text=null,result_digest=null,result_worker_id=null,last_error_code='PUBLIC_WEBSITE_SOURCE_UNCONFIRMED';
set local role authenticated;
select ok((select hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',scan_id)->>'last_error_code'='PUBLIC_WEBSITE_SOURCE_UNCONFIRMED'
 from public_status_receipt),'PublicEngineFailureRemainsExplicitMetadata');
reset role;
delete from hsr.memberships where organization_id='88100000-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($$select hsr.read_public_scan_status('88100000-0000-0000-0000-000000000001',(select scan_id from public_status_receipt))$$,
 '42501','HSR_PUBLIC_SCAN_ACCESS_DENIED','RevokedOriginalMemberCannotReadPublicStatus');
reset role;
select * from finish();
rollback;
