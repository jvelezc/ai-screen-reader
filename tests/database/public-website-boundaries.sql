-- Disposable database only. No native execution or hosted identity is implied.
begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,hsr_worker;
select no_plan();
insert into auth.users(id) values('84000000-0000-0000-0000-000000000001'),('84000000-0000-0000-0000-000000000002');
insert into hsr.organizations(id,name) values('84100000-0000-0000-0000-000000000001','Public boundaries A'),
 ('84100000-0000-0000-0000-000000000002','Public boundaries B'),('84100000-0000-0000-0000-000000000003','Public quota');
insert into hsr.memberships values('84100000-0000-0000-0000-000000000001','84000000-0000-0000-0000-000000000001'),
 ('84100000-0000-0000-0000-000000000002','84000000-0000-0000-0000-000000000002'),
 ('84100000-0000-0000-0000-000000000003','84000000-0000-0000-0000-000000000002');
create temporary table boundary_jobs(tag text,organization_id uuid,request_id uuid,scan_id uuid,job_id uuid);
create temporary table boundary_claims(tag text,body jsonb);
grant select on boundary_jobs to hsr_worker,authenticated;
grant select,insert on boundary_claims to hsr_worker;
create function pg_temp.boundary_add(p_tag text,p_org integer,p_request integer,p_url text default 'https://example.com/') returns void
language plpgsql as $$ declare receipt record;org uuid:=('84100000-0000-0000-0000-'||lpad(p_org::text,12,'0'))::uuid;
 actor uuid:=('84000000-0000-0000-0000-'||lpad(case when p_org=1 then '1' else '2' end,12,'0'))::uuid;
 req uuid:=('84200000-0000-0000-0000-'||lpad(p_request::text,12,'0'))::uuid;
begin
 perform set_config('request.jwt.claim.sub',actor::text,true);
 select * into strict receipt from hsr.create_public_scan(org,req,p_url,'example.com');
 insert into boundary_jobs values(p_tag,org,req,receipt.scan_id,receipt.job_id);
end $$;
set local role hsr_worker;
select is(hsr.claim_public_scan('84300000-0000-0000-0000-000000000001')->>'status','Empty','PublicEmptyQueueReturnsNoCapability');
select throws_ok($$select hsr.claim_public_scan('00000000-0000-0000-0000-000000000000')$$,'22023','HSR_PUBLIC_WORKER_INVALID','PublicEmptyWorkerIdentityRejected');
reset role;
select pg_temp.boundary_add('a',1,1);
set local role hsr_worker;
insert into boundary_claims values('a',hsr.claim_public_scan('84300000-0000-0000-0000-000000000001'));
select is((select body->>'status' from boundary_claims where tag='a'),'Claimed','PublicFirstCapacitySlotClaims');
reset role;
select pg_temp.boundary_add('a-wait',1,2);
set local role hsr_worker;
insert into boundary_claims values('busy',hsr.claim_public_scan('84300000-0000-0000-0000-000000000002'));
select is((select body->>'status' from boundary_claims where tag='busy'),'WorkspaceBusy','PublicOneWorkspaceCannotOccupyBothSlots');
select ok((select not(body ? 'token') from boundary_claims where tag='busy'),'PublicBusyWorkspaceDoesNotReceiveCapability');
reset role;
select pg_temp.boundary_add('b',2,3);
set local role hsr_worker;
insert into boundary_claims values('b',hsr.claim_public_scan('84300000-0000-0000-0000-000000000002'));
select is((select body->>'status' from boundary_claims where tag='b'),'Claimed','PublicSecondWorkspaceUsesSecondCapacitySlot');
insert into boundary_claims values('full',hsr.claim_public_scan('84300000-0000-0000-0000-000000000003'));
select is((select body->>'status' from boundary_claims where tag='full'),'AtCapacity','PublicThirdClaimCannotExceedTwoSlots');
select ok((select not(body ? 'token') from boundary_claims where tag='full'),'PublicCapacityDenialDoesNotDiscloseCapability');
select ok(not(select hsr.block_public_scan(j.organization_id,j.job_id,2,'84300000-0000-0000-0000-000000000002',(c.body->>'token')::uuid,'PUBLIC_WEBSITE_SOURCE_UNCONFIRMED')
 from boundary_jobs j join boundary_claims c using(tag) where tag='b'),'PublicDifferentGenerationCannotBlockOriginal');
select ok(not(select hsr.block_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000001',(c.body->>'token')::uuid,'PUBLIC_WEBSITE_SOURCE_UNCONFIRMED')
 from boundary_jobs j join boundary_claims c using(tag) where tag='b'),'PublicDifferentWorkerCannotBlockOriginal');
select ok((select hsr.block_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000002',(c.body->>'token')::uuid,'PUBLIC_WEBSITE_SOURCE_UNCONFIRMED')
 from boundary_jobs j join boundary_claims c using(tag) where tag='b'),'PublicExactWorkerCanBlockItsAttempt');
reset role;
select is((select state from hsr.public_scan_jobs where id=(select job_id from boundary_jobs where tag='b')),'blocked','PublicBlockedJobRetainsExplicitFailure');
select ok((select result_text is null and result_worker_id is null from hsr.public_scan_jobs where id=(select job_id from boundary_jobs where tag='b')),'PublicEngineFailureCreatesNoObservation');
update hsr.public_scan_leases set expires_at=clock_timestamp()-interval '1 second' where job_id=(select job_id from boundary_jobs where tag='a');
set local role hsr_worker;
select ok(not(select hsr.renew_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000001',(c.body->>'token')::uuid)
 from boundary_jobs j join boundary_claims c using(tag) where tag='a'),'PublicExpiredCapabilityCannotRenew');
select ok(not(select hsr.finish_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000001',(c.body->>'token')::uuid,'{}',encode(sha256(convert_to('{}','UTF8')),'hex'))
 from boundary_jobs j join boundary_claims c using(tag) where tag='a'),'PublicExpiredCapabilityCannotPublish');
reset role;
select pgmq.set_vt('hsr_public_scan_jobs',(select queue_message_id from hsr.public_scan_jobs where id=(select job_id from boundary_jobs where tag='a-wait')),0);
set local role hsr_worker;
insert into boundary_claims values('a-wait',hsr.claim_public_scan('84300000-0000-0000-0000-000000000003'));
select is((select body->>'status' from boundary_claims where tag='a-wait'),'Claimed','PublicExpiredAttemptDoesNotPreventDistinctAdmittedJob');
reset role;
select is((select state from hsr.public_scan_jobs where id=(select job_id from boundary_jobs where tag='a')),'blocked','PublicExpiredAttemptBlocksInsteadOfReplay');
select is((select generation from hsr.public_scan_jobs where id=(select job_id from boundary_jobs where tag='a')),1::bigint,'PublicExpiredAttemptDoesNotAcquireReplacementGeneration');
select is((select state from hsr.public_scan_attempts where job_id=(select job_id from boundary_jobs where tag='a')),'expired','PublicExpiryRetainsHistoricalAttempt');
select ok((select result_text is null from hsr.public_scan_jobs where id=(select job_id from boundary_jobs where tag='a')),'PublicExpiryCannotBecomeAResult');
delete from hsr.memberships where organization_id='84100000-0000-0000-0000-000000000001' and user_id='84000000-0000-0000-0000-000000000001';
set local role hsr_worker;
select ok(not(select hsr.renew_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000003',(c.body->>'token')::uuid)
 from boundary_jobs j join boundary_claims c using(tag) where tag='a-wait'),'PublicRevokedCreatorCannotRenewWorkerPermission');
select ok(not(select hsr.finish_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000003',(c.body->>'token')::uuid,'{}',encode(sha256(convert_to('{}','UTF8')),'hex'))
 from boundary_jobs j join boundary_claims c using(tag) where tag='a-wait'),'PublicRevokedCreatorCannotPublishLateData');
select ok((select hsr.block_public_scan(j.organization_id,j.job_id,1,'84300000-0000-0000-0000-000000000003',(c.body->>'token')::uuid,'PUBLIC_WEBSITE_AUTHORITY_LOST')
 from boundary_jobs j join boundary_claims c using(tag) where tag='a-wait'),'PublicRevokedAttemptCanRetainSafeFailureMetadata');
reset role;
set local role authenticated;
set local request.jwt.claim.sub='84000000-0000-0000-0000-000000000001';
select is((select count(*) from hsr.public_scans),0::bigint,'PublicRevokedCreatorCannotReadFormerWorkspace');
reset role;

select pg_temp.boundary_add('max-url',2,4,rpad('https://example.com/',2048,'a'));
select is((select length(target_url) from hsr.public_scans where id=(select scan_id from boundary_jobs where tag='max-url')),2048,'PublicExactUrlCeilingIsAdmitted');
do $$ begin for i in 1..50 loop perform pg_temp.boundary_add('quota-'||i::text,3,100+i); end loop; end $$;
select is((select count(*) from hsr.public_scan_jobs where organization_id='84100000-0000-0000-0000-000000000003'),50::bigint,'PublicWorkspaceAdmitsExactQueuedJobCeiling');
set local role authenticated;
set local request.jwt.claim.sub='84000000-0000-0000-0000-000000000002';
select throws_ok($$select * from hsr.create_public_scan('84100000-0000-0000-0000-000000000003',gen_random_uuid(),'https://example.com/','example.com')$$,'54000','HSR_PUBLIC_SCAN_QUEUE_FULL','PublicNextQueuedJobIsDeniedAtCapacity');
select results_eq($$select * from hsr.create_public_scan('84100000-0000-0000-0000-000000000003','84200000-0000-0000-0000-000000000101','https://example.com/','example.com')$$,
 $$select scan_id,job_id from boundary_jobs where tag='quota-1'$$,'PublicIdempotentRetryStillWorksAtQueueCapacity');
reset role;
select is((select count(*) from hsr.public_scan_jobs where organization_id='84100000-0000-0000-0000-000000000003'),50::bigint,'PublicDeniedAdmissionDoesNotCreateAnotherJob');
select * from finish();
rollback;
