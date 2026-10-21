begin;
set search_path = extensions,public;
select plan(3);
set local role authenticated;
set local request.jwt.claim.sub = '90000000-0000-0000-0000-000000000001';
select results_eq($$select id from hsr.scans$$,$$select scan_id from hsr_test.expected$$,
    'CommittedScanSurvivesDatabaseRestart');
select results_eq($$select * from hsr.create_scan(
    '90000000-0000-0000-0000-000000000002','90000000-0000-0000-0000-000000000003')$$,
    $$select * from hsr_test.expected$$,'RestartedAdmissionReturnsOriginalReceipt');
reset role;
select is((select count(*) from pgmq.q_hsr_scan_jobs q join hsr_test.expected e on q.msg_id=e.queue_message_id),
    1::bigint,'ExactlyOneBoundQueueMessageSurvivesDatabaseRestart');
select * from finish();
rollback;
