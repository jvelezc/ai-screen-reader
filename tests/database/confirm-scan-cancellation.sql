begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,hsr_worker;
select plan(14);
insert into auth.users(id) values('7e000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values('7e000000-0000-0000-0000-000000000010','Cancellation confirmation fixture');
insert into hsr.memberships values('7e000000-0000-0000-0000-000000000010','7e000000-0000-0000-0000-000000000001');
set local request.jwt.claim.sub='7e000000-0000-0000-0000-000000000001';
select ok(has_function_privilege('hsr_worker','hsr.confirm_scan_cancellation(uuid,uuid,uuid,bigint,uuid)','EXECUTE'),'WorkerMayReadItsCapabilityBoundCancellation');
select ok(not has_function_privilege('authenticated','hsr.confirm_scan_cancellation(uuid,uuid,uuid,bigint,uuid)','EXECUTE')
    and not has_function_privilege('anon','hsr.confirm_scan_cancellation(uuid,uuid,uuid,bigint,uuid)','EXECUTE'),'UsersAndAnonymousCallersCannotReadWorkerCancellationCapability');
set local role authenticated;
select throws_ok($$select hsr.confirm_scan_cancellation(null,null,null,null,null)$$,'42501',null,'UserCannotInvokeWorkerCancellationConfirmation');
create temporary table confirmation_job as select * from hsr.create_scan('7e000000-0000-0000-0000-000000000010','7e000000-0000-0000-0000-000000000101');
grant select on confirmation_job to hsr_worker;
set local role hsr_worker;
create temporary table confirmation_lease as select * from hsr.claim_scan_job('7e000000-0000-0000-0000-000000000020',60);
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token) from confirmation_lease c),false,'ActiveLeaseIsNotAConfirmedCallerCancellation');
select hsr.block_scan_job(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token,'FIXTURE_OBSERVATION_UNCONFIRMED') from confirmation_lease c;
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token) from confirmation_lease c),false,'AnEngineBlockCannotMasqueradeAsCallerCancellation');
set local role authenticated;
select count(*) from confirmation_job j cross join lateral hsr.request_scan_cancellation('7e000000-0000-0000-0000-000000000010',j.scan_id);
reset role;
create temporary table confirmation_before as select to_jsonb(j) as job,
    (select count(*) from hsr.job_attempts a where a.job_id=j.id) as attempts,
    (select count(*) from hsr.attempt_events e where e.job_id=j.id) as events
    from hsr.scan_jobs j join confirmation_job c on c.job_id=j.id;
set local role hsr_worker;
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token) from confirmation_lease c),true,'OnlyTheOriginalAttemptCapabilityConfirmsDurableCallerCancellation');
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,'7e000000-0000-0000-0000-000000000021') from confirmation_lease c),false,'DifferentAttemptTokenCannotConfirmCancellation');
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000022',1,c.lease_token) from confirmation_lease c),false,'DifferentProducerCannotConfirmCancellation');
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',2,c.lease_token) from confirmation_lease c),false,'DifferentGenerationCannotConfirmCancellation');
select is((select hsr.confirm_scan_cancellation('7e000000-0000-0000-0000-000000000011',c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token) from confirmation_lease c),false,'ForeignOrganizationCannotConfirmCancellation');
select is((select hsr.confirm_scan_cancellation(c.organization_id,c.job_id,'00000000-0000-0000-0000-000000000000',1,c.lease_token) from confirmation_lease c),false,'EmptyProducerCannotConfirmCancellation');
select is((select hsr.confirm_scan_cancellation(c.organization_id,'7e000000-0000-0000-0000-000000000099','7e000000-0000-0000-0000-000000000020',1,c.lease_token) from confirmation_lease c),false,'MissingAttemptCannotConfirmCancellation');
reset role;
select ok((select to_jsonb(j)=b.job and b.attempts=(select count(*) from hsr.job_attempts a where a.job_id=j.id)
    and b.events=(select count(*) from hsr.attempt_events e where e.job_id=j.id)
    from hsr.scan_jobs j join confirmation_job c on c.job_id=j.id cross join confirmation_before b),'ConfirmationCannotMutateOriginalJobAttemptOrEventHistory');
set local role hsr_worker;
select ok((select not hsr.renew_scan_job(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token,30)
    and not hsr.complete_scan_job(c.organization_id,c.job_id,'7e000000-0000-0000-0000-000000000020',1,c.lease_token,repeat('a',64),
    replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||replace(c.job_id::text,'-','')||'/1/observation.json')
    from confirmation_lease c),'ConfirmationCannotRenewExecutionOrPublishCompletion');
select * from finish();
rollback;
