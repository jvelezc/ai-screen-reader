begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,hsr_worker;
select plan(18);
insert into auth.users(id) values('8a000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('8a000000-0000-0000-0000-000000000010','Publication failure fixture');
insert into hsr.memberships values('8a000000-0000-0000-0000-000000000010','8a000000-0000-0000-0000-000000000001');
set local request.jwt.claim.sub='8a000000-0000-0000-0000-000000000001';
select ok(has_function_privilege('hsr_worker','hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text)','EXECUTE')
    and not has_function_privilege('authenticated','hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text)','EXECUTE')
    and not has_function_privilege('anon','hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text)','EXECUTE'),
    'PublicationDiagnosticsRetainWorkerOnlyAuthority');
set local role authenticated;
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,'WORKER_PUBLICATION_STORAGE_UNAVAILABLE')$$,
    '42501',null,'ApplicationCallerCannotWritePublicationCause');
create temporary table publication_cases(code text primary key,scan_id uuid,job_id uuid);
do $$declare code text;created record;begin
    foreach code in array array['WORKER_PUBLICATION_STORAGE_UNAVAILABLE','WORKER_PUBLICATION_WRITE_UNCONFIRMED',
        'WORKER_PUBLICATION_CONTENT_INVALID','WORKER_PUBLICATION_ORIGINAL_MISSING','Fence','Cancel'] loop
        select * into created from hsr.create_scan('8a000000-0000-0000-0000-000000000010',gen_random_uuid());
        insert into publication_cases values(code,created.scan_id,created.job_id);
    end loop;
end $$;
grant select on publication_cases to hsr_worker;
set local role hsr_worker;
select throws_ok($$select hsr.block_scan_job(null,null,null,null,null,'WORKER_PUBLICATION_PRIVATE_DETAIL')$$,
    '22023','INVALID_WORKER_FAILURE_CODE','PublicationPrefixCannotAuthorizeArbitraryProviderDetails');
create temporary table publication_leases as select * from hsr.claim_scan_job('8a000000-0000-0000-0000-000000000020',60);
do $$begin for i in 1..5 loop
    insert into publication_leases select * from hsr.claim_scan_job('8a000000-0000-0000-0000-000000000020',60);
end loop;end $$;
grant select on publication_leases to authenticated;
select is(hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000020',l.generation,l.lease_token,c.code),
    true,'PublicationCodeAcceptedOnlyForBoundAttempt_'||c.code)
    from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code like 'WORKER_%' order by c.code;
select is((select hsr.block_scan_job('8a000000-0000-0000-0000-000000000011',l.job_id,'8a000000-0000-0000-0000-000000000020',
    l.generation,l.lease_token,'WORKER_PUBLICATION_STORAGE_UNAVAILABLE') from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='Fence'),
    false,'PublicationCauseCannotBypassOrganizationFence');
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000021',
    l.generation,l.lease_token,'WORKER_PUBLICATION_STORAGE_UNAVAILABLE') from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='Fence'),
    false,'PublicationCauseCannotBypassProducerFence');
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000020',
    l.generation+1,l.lease_token,'WORKER_PUBLICATION_STORAGE_UNAVAILABLE') from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='Fence'),
    false,'PublicationCauseCannotBypassGenerationFence');
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000020',
    l.generation,'8a000000-0000-0000-0000-000000000022','WORKER_PUBLICATION_STORAGE_UNAVAILABLE') from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='Fence'),
    false,'PublicationCauseCannotBypassCapabilityFence');
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000020',
    l.generation,l.lease_token,'WORKER_PUBLICATION_WRITE_UNCONFIRMED') from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='WORKER_PUBLICATION_STORAGE_UNAVAILABLE'),
    false,'TerminalPublicationCauseCannotBeReplacedByAnotherBlock');
reset role;
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second'
    where job_id=(select job_id from publication_cases where code='Fence');
set local role hsr_worker;
select is((select hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000020',
    l.generation,l.lease_token,'WORKER_PUBLICATION_STORAGE_UNAVAILABLE') from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='Fence'),
    false,'PublicationCauseCannotResurrectExpiredAuthority');
set local role authenticated;
select count(*) from publication_cases c cross join lateral hsr.request_scan_cancellation('8a000000-0000-0000-0000-000000000010',c.scan_id) where c.code='Cancel';
set local role hsr_worker;
select hsr.block_scan_job(l.organization_id,l.job_id,'8a000000-0000-0000-0000-000000000020',l.generation,l.lease_token,'WORKER_PUBLICATION_STORAGE_UNAVAILABLE')
    from publication_leases l join publication_cases c on c.job_id=l.job_id where c.code='Cancel';
reset role;
select ok(j.state='blocked' and j.last_error_code=c.code and j.generation=1 and j.result_digest is null and j.result_reference is null
    and j.archive_index_sha256 is null and j.archive_worker_id is null
    and not exists(select 1 from hsr.job_leases l where l.job_id=j.id)
    and (select count(*) from hsr.job_attempts a where a.job_id=j.id)=1
    and exists(select 1 from hsr.attempt_events e where e.job_id=j.id and e.kind='Blocked' and e.error_code=c.code),
    'PublicationCauseRetainsOriginalAttemptWithoutEvidenceReceipt_'||c.code)
    from publication_cases c join hsr.scan_jobs j on j.id=c.job_id where c.code like 'WORKER_%' order by c.code;
select ok(j.state='blocked' and j.last_error_code='WORKER_CANCELLED_BY_CALLER' and j.result_reference is null,
    'CommittedCallerCancellationOutranksPublicationCause')
    from publication_cases c join hsr.scan_jobs j on j.id=c.job_id where c.code='Cancel';
select * from finish();
rollback;
