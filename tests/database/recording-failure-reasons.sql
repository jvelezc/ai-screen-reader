begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,hsr_worker;
select plan(30);
insert into auth.users(id) values('7f000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('7f000000-0000-0000-0000-000000000010','Recording failure reason fixture');
insert into hsr.memberships values('7f000000-0000-0000-0000-000000000010','7f000000-0000-0000-0000-000000000001');
set local request.jwt.claim.sub='7f000000-0000-0000-0000-000000000001';
select ok(has_function_privilege('hsr_worker','hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text)','EXECUTE')
    and not has_function_privilege('authenticated','hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text)','EXECUTE')
    and not has_function_privilege('anon','hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text)','EXECUTE'),
    'RecordingDiagnosticsPreserveWorkerOnlyBlockAuthority');
set local role authenticated;
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,'WORKER_PROGRESS_RECORDING_FAILED')$$,
    '42501',null,'ApplicationCallerCannotWriteWorkerRecordingReason');
create temporary table recording_reasons(code text primary key,scan_id uuid,job_id uuid);
do $$declare code text;created record;begin
    foreach code in array array[
        'WORKER_PROGRESS_RECORDING_CAPTURE_BUDGET_EXCEEDED','WORKER_PROGRESS_RECORDING_CAPTURE_FRAME_INVALID',
        'WORKER_PROGRESS_RECORDING_CAPTURE_WRITE_FAILED','WORKER_PROGRESS_RECORDING_FAILED',
        'WORKER_PROGRESS_RECORDING_CANCELLED','WORKER_PROGRESS_RECORDING_SOURCE_METADATA_MISSING',
        'WORKER_PROGRESS_RECORDING_SOURCE_HASH_CHANGED','WORKER_PROGRESS_RECORDING_SOURCE_ARTIFACT_MISSING',
        'WORKER_PROGRESS_RECORDING_ARTIFACT_UNRECOGNIZED','WORKER_PROGRESS_RECORDING_ORIGINAL_CHANGED',
        'WORKER_PROGRESS_RECORDING_UNCONFIRMED','WORKER_PROGRESS_RECORDING_ACK_REJECTED',
        'WORKER_PROGRESS_RECORDING_SOURCE_EXPORT_UNCONFIRMED',
        'WORKER_PROGRESS_RECORDING_COLLECTION_UNCONFIRMED','WORKER_PROGRESS_RECORDING_CALIBRATION_UNCONFIRMED',
        'WORKER_PROGRESS_RECORDING_TRAVERSAL_UNCONFIRMED','WORKER_PROGRESS_RECORDING_FINALIZATION_UNCONFIRMED',
        'WORKER_PROGRESS_RECORDING_ALIGNMENT_UNCONFIRMED','WORKER_PROGRESS_RECORDING_PREPARATION_UNCONFIRMED',
        'WORKER_PROGRESS_RECORDING_VALIDATION_UNCONFIRMED','WORKER_PROGRESS_RECORDING_CLEANUP_UNCONFIRMED',
        'WORKER_PROGRESS_RECORDING_CAPTURE_STOP_UNCONFIRMED','WORKER_PROGRESS_RECORDING_BROWSER_SHUTDOWN_UNCONFIRMED',
        'WORKER_PROGRESS_RECORDING_OUTPUT_TRANSFER_UNCONFIRMED'] loop
        select * into created from hsr.create_scan('7f000000-0000-0000-0000-000000000010',gen_random_uuid());
        insert into recording_reasons values(code,created.scan_id,created.job_id);
    end loop;
end $$;
grant select on recording_reasons to hsr_worker;
set local role hsr_worker;
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,'PRIVATE_BROWSER_DETAIL')$$,
    '22023','INVALID_WORKER_FAILURE_CODE','RawPrivateDetailsCannotBecomeDurableWorkerReason');
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,'WORKER_PROGRESS_RECORDING_PRIVATE_DETAIL')$$,
    '22023','INVALID_WORKER_FAILURE_CODE','ARecordingPrefixCannotAuthorizeAnUnknownReason');
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,null)$$,
    '22023','INVALID_WORKER_FAILURE_CODE','NullRecordingReasonCannotBecomeAWorkerBlock');
create temporary table recording_reason_leases as select * from hsr.claim_scan_job('7f000000-0000-0000-0000-000000000020',60);
do $$begin
    for i in 1..23 loop
        insert into recording_reason_leases select * from hsr.claim_scan_job('7f000000-0000-0000-0000-000000000020',60);
    end loop;
end $$;
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'7f000000-0000-0000-0000-000000000020',l.generation,
    '7f000000-0000-0000-0000-000000000021','WORKER_PROGRESS_RECORDING_FAILED') from recording_reason_leases l limit 1),
    false,'KnownRecordingReasonCannotBypassTheOriginalAttemptCapability');
select hsr.block_scan_job(l.organization_id,l.job_id,'7f000000-0000-0000-0000-000000000020',l.generation,l.lease_token,c.code)
    from recording_reason_leases l join recording_reasons c on c.job_id=l.job_id;
reset role;
select ok(j.state='blocked' and j.last_error_code=c.code and j.generation=1 and j.result_reference is null
    and not exists(select 1 from hsr.job_leases l where l.job_id=j.id)
    and (select count(*) from hsr.job_attempts a where a.job_id=j.id)=1
    and exists(select 1 from hsr.attempt_events e where e.job_id=j.id and e.kind='Blocked' and e.error_code=c.code),
    'BoundRecordingFailurePersistsWithoutCompletion_'||c.code)
    from recording_reasons c join hsr.scan_jobs j on j.id=c.job_id order by c.code;
select * from finish();
rollback;
