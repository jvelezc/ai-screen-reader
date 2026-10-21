create extension if not exists pgtap with schema extensions;
begin;
set search_path = extensions, public;
grant usage on schema extensions to authenticated, anon;
select plan(41);
insert into auth.users(id) values
    ('00000000-0000-0000-0000-000000000001'),
    ('00000000-0000-0000-0000-000000000002'),
    ('00000000-0000-0000-0000-000000000003');
insert into hsr.organizations(id,name) values
    ('10000000-0000-0000-0000-000000000001','First organization'),
    ('10000000-0000-0000-0000-000000000002','Second organization');
insert into hsr.memberships values
    ('10000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000001'),
    ('10000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000002'),
    ('10000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000003');

set local role authenticated;
set local request.jwt.claim.sub = '00000000-0000-0000-0000-000000000001';
select is((select count(*) from hsr.scans),0::bigint,'EmptyTenantHasNoScans');
create temporary table first_receipt as select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001');
select is((select count(*) from first_receipt),1::bigint,'AdmissionReturnsExactlyOneReceipt');
select is((select count(*) from hsr.scans),1::bigint,'MemberSeesOwnScan');
select is((select count(*) from hsr.organizations),1::bigint,'MemberSeesOnlyOwnOrganization');
select is((select count(*) from hsr.memberships),1::bigint,'MembershipReadDoesNotExposeOtherMembers');
select is((select count(*) from hsr.scan_jobs),1::bigint,'MemberSeesOwnJob');
select is((select created_by::text from hsr.scans),
    '00000000-0000-0000-0000-000000000001','CreatorDerivedFromAuthenticatedSubject');
select results_eq($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001')$$,
    $$select * from first_receipt$$,'DuplicateRequestReturnsExactPriorReceipt');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000002')$$,
    '42501','HSR_MEMBERSHIP_REQUIRED','NonmemberCannotAdmitAnotherTenantScan');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001',null)$$,
    '22023','HSR_INVALID_SCAN_REQUEST','NullIdempotencyKeyRejected');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002',null)$$,
    '22023','HSR_INVALID_SCAN_REQUEST','NullFixtureRejected');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002','')$$,
    '22023','HSR_INVALID_SCAN_REQUEST','EmptyFixtureRejected');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002','https://example.com')$$,
    '22023','HSR_INVALID_SCAN_REQUEST','ArbitraryTargetRejected');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002','synthetic-shop',null)$$,
    '22023','HSR_INVALID_SCAN_REQUEST','NullProfileRejected');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002','synthetic-shop','future-profile')$$,
    '22023','HSR_INVALID_SCAN_REQUEST','UnsupportedProfileRejected');
select throws_ok($$insert into hsr.memberships values(
    '10000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001')$$,
    '42501',null,'ClientCannotGrantMembership');
select throws_ok($$update hsr.scans set state='queued'$$,'42501',null,'ClientCannotRewriteScan');
select throws_ok($$delete from hsr.scan_jobs$$,'42501',null,'ClientCannotDeleteJob');
select throws_ok($$insert into hsr.scans(organization_id,request_id,created_by,fixture,reader_profile)
    values ('10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000004',
    '00000000-0000-0000-0000-000000000001','synthetic-shop','hsr-desktop-reader-v13')$$,
    '42501',null,'ClientCannotBypassAtomicAdmission');
select throws_ok($$insert into hsr.scan_jobs(organization_id,scan_id,stage,queue_message_id)
    select '10000000-0000-0000-0000-000000000001',scan_id,'observe-fixture',999999 from first_receipt$$,
    '42501',null,'ClientCannotForgeJob');
select throws_ok($$select * from pgmq.q_hsr_scan_jobs$$,'42501',null,'ClientCannotReadQueue');
select throws_ok($$select pgmq.send('hsr_scan_jobs','{}'::jsonb)$$,'42501',null,'ClientCannotEnqueueRawMessage');
select throws_ok($$select * from pgmq.a_hsr_scan_jobs$$,'42501',null,'ClientCannotReadQueueArchive');
set local request.jwt.claim.sub = '';
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001')$$,
    '42501','HSR_MEMBERSHIP_REQUIRED','MissingAuthenticatedSubjectCannotAdmitScan');

set local request.jwt.claim.sub = '00000000-0000-0000-0000-000000000003';
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001')$$,
    '22023','HSR_REQUEST_CONFLICT','AnotherMemberCannotReuseCreatorRequest');
set local request.jwt.claim.sub = '00000000-0000-0000-0000-000000000002';
select is((select count(*) from hsr.scans),0::bigint,'SecondTenantCannotReadFirstScan');
select is((select count(*) from hsr.scan_jobs),0::bigint,'SecondTenantCannotReadFirstJob');
create temporary table second_receipt as select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001');
select isnt((select scan_id from second_receipt),(select scan_id from first_receipt),
    'SameRequestKeyInAnotherTenantCreatesIndependentScan');
reset role;
select is((select count(*) from pgmq.q_hsr_scan_jobs),2::bigint,'DuplicateAdmissionDoesNotDuplicateQueueMessage');
select is((select message from pgmq.q_hsr_scan_jobs where msg_id=(select queue_message_id from first_receipt)),
    (select jsonb_build_object('schema','hsr-stage-job-v1',
        'organizationId','10000000-0000-0000-0000-000000000001',
        'scanId',scan_id,'jobId',job_id,'stage','observe-fixture') from first_receipt),
    'QueuePayloadExactlyBindsTenantScanJobAndStage');
select throws_ok($$insert into hsr.scan_jobs(organization_id,scan_id,stage,queue_message_id)
    select '10000000-0000-0000-0000-000000000002',scan_id,'observe-fixture',999999 from first_receipt$$,
    '23503',null,'CompositeForeignKeyRejectsCrossTenantJob');

create function pg_temp.reject_job() returns trigger language plpgsql as $$
begin raise exception 'INJECTED_JOB_WRITE_FAILURE'; end $$;
create trigger reject_test_job before insert on hsr.scan_jobs
    for each row execute function pg_temp.reject_job();
set local role authenticated;
set local request.jwt.claim.sub = '00000000-0000-0000-0000-000000000001';
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000003')$$,
    'P0001','INJECTED_JOB_WRITE_FAILURE','FailedJobWriteAbortsAdmission');
reset role;
drop trigger reject_test_job on hsr.scan_jobs;
select is((select count(*) from hsr.scans),2::bigint,'FailedAdmissionLeavesNoScan');
select is((select count(*) from hsr.scan_jobs),2::bigint,'FailedAdmissionLeavesNoJob');
select is((select count(*) from pgmq.q_hsr_scan_jobs),2::bigint,'FailedAdmissionRollsBackActualPgmqSend');
set local role authenticated;
select lives_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000003')$$,
    'FailedAdmissionRequestCanBeRetriedAfterRollback');
reset role;
select is((select count(*) from pgmq.q_hsr_scan_jobs),3::bigint,'SuccessfulRetryAddsExactlyOneMessage');
delete from hsr.memberships where user_id='00000000-0000-0000-0000-000000000001';
set local role authenticated;
select is((select count(*) from hsr.scans),0::bigint,'RevocationImmediatelyHidesRetainedScans');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001')$$,
    '42501','HSR_MEMBERSHIP_REQUIRED','RevocationRejectsEvenIdempotentAdmission');
set local role anon;
select throws_ok($$select * from hsr.scans$$,'42501',null,'AnonymousCannotReadScans');
select throws_ok($$select * from hsr.create_scan(
    '10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001')$$,
    '42501',null,'AnonymousCannotAdmitScan');
reset role;
select * from finish();
rollback;
