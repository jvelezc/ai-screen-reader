-- Synthetic identities only, exclusively in the disposable database.
begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,anon,hsr_worker;
select no_plan();
insert into auth.users(id) values('7c5a1000-0000-0000-0000-000000000001'),('7c5a1000-0000-0000-0000-000000000002');
insert into auth.sessions(id,user_id,not_after) values
 ('7c5a1100-0000-0000-0000-000000000001','7c5a1000-0000-0000-0000-000000000001',clock_timestamp()+interval '1 hour'),
 ('7c5a1100-0000-0000-0000-000000000002','7c5a1000-0000-0000-0000-000000000002',clock_timestamp()+interval '1 hour');
insert into hsr.organizations(id,name) values('7c5a1200-0000-0000-0000-000000000001','Set test A'),('7c5a1200-0000-0000-0000-000000000002','Set test B');
insert into hsr.memberships values
 ('7c5a1200-0000-0000-0000-000000000001','7c5a1000-0000-0000-0000-000000000001'),
 ('7c5a1200-0000-0000-0000-000000000001','7c5a1000-0000-0000-0000-000000000002'),
 ('7c5a1200-0000-0000-0000-000000000002','7c5a1000-0000-0000-0000-000000000002');
set local request.jwt.claim.sub='7c5a1000-0000-0000-0000-000000000001';
set local request.jwt.claims='{"sub":"7c5a1000-0000-0000-0000-000000000001","session_id":"7c5a1100-0000-0000-0000-000000000001"}';
create temporary table parent as select * from hsr.create_public_scan('7c5a1200-0000-0000-0000-000000000001',gen_random_uuid(),'https://example.com/','example.com');
update hsr.public_scan_jobs set state='completed',result_text='{}',result_digest=repeat('a',64),result_worker_id=gen_random_uuid() where id=(select job_id from parent);
create temporary table task_sets(tag text,body jsonb);
grant select on parent to authenticated,anon,hsr_worker;
grant select,insert on task_sets to authenticated;

set local role anon;
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),1,array['Pricing'])$$,'42501',null,'AnonymousCannotAdmitTaskSet');
select throws_ok($$select hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid())$$,'42501',null,'AnonymousCannotReadTaskSet');
reset role;
set local role authenticated;
select throws_ok($$select * from hsr.public_task_sets$$,'42501',null,'CustomerCannotReadSetTableDirectly');
select throws_ok($$select * from hsr.public_task_set_members$$,'42501',null,'CustomerCannotReadMemberTableDirectly');
select throws_ok($$select hsr.public_task_set_metadata(null::hsr.public_task_sets)$$,'42501',null,'CustomerCannotBypassSetAuthorityThroughMetadataHelper');
select throws_ok($$select hsr.public_task_set_member_metadata(null::hsr.public_tasks)$$,'42501',null,'CustomerCannotBypassSetAuthorityThroughMemberHelper');
insert into task_sets values('one',hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),1,array['Pricing']));
select is((select body->>'Schema' from task_sets where tag='one'),'hsr-public-task-set-v1','TaskSetHasItsOwnMetadataSchema');
select is((select array_agg(key order by key) from task_sets,jsonb_object_keys(body) key where tag='one'),
 array['CreatedAt','CreatedBy','GoalKinds','IsAssessment','MaximumTasks','OrganizationId','ParentOriginalSha256','ParentScanId','RequestId','Schema','Scope','SetId','Source','TaskResultsRequireOriginalRead','Tasks']::text[],
 'SetMetadataHasOnlyItsDeclaredCoreWireFields');
select is((select jsonb_array_length(body->'Tasks') from task_sets where tag='one'),1,'OneTaskBudgetAdmitsExactlyOneOrdinaryJob');
select is((select body->'Tasks'->0->'Job'->>'GoalKind' from task_sets where tag='one'),'Pricing','SetChildPreservesExactRequestedGoal');
select is((select body->'Tasks'->0->>'Schema' from task_sets where tag='one'),'hsr-public-task-set-member-v1','SetMemberUsesItsOwnCompactMetadataContract');
select is((select array_agg(key order by key) from task_sets,jsonb_object_keys(body->'Tasks'->0) key where tag='one'),
 array['CreatedAt','CreatedBy','ErrorCode','FinishedAt','Job','Phase','ReportSha256','Schema','State','UpdatedAt']::text[],
 'SetMemberMetadataMatchesTheStrictCompactCoreWireContract');
select ok((select not(body->'Tasks'->0 ? 'Publication') and not(body->'Tasks'->0 ? 'ProducerId') from task_sets where tag='one'),'SetMemberDoesNotExposeUnverifiedAssessmentOrPublicationClaims');
select ok((select body->>'IsAssessment'='false' and body->>'TaskResultsRequireOriginalRead'='true' from task_sets where tag='one'),'TaskSetMetadataCannotEstablishAnAssessment');
select is((select body->'Source'->>'TargetUrl' from task_sets where tag='one'),'https://example.com/','TaskSetRetainsTheExactParentSource');
select is(hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one')),(select body from task_sets where tag='one'),'ExactSetReadPreservesItsOriginalChildren');
select is(hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('a',64),1,array['Pricing']),(select body from task_sets where tag='one'),'LostSetResponseReconcilesWithoutReplacementJobs');
select is(hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid()),null::jsonb,'UnknownSetReadDoesNotCreateAnAdmission');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('a',64),2,array['Pricing'])$$,'22023','HSR_REQUEST_CONFLICT','SameSetIntentCannotChangeItsBudget');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('a',64),1,array['Catalog'])$$,'22023','HSR_REQUEST_CONFLICT','SameSetIntentCannotChangeItsGoalPlan');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('b',64),1,array['Pricing'])$$,'22023','HSR_REQUEST_CONFLICT','SameSetIntentCannotChangeItsOriginal');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',gen_random_uuid(),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('a',64),1,array['Pricing'])$$,'22023','HSR_REQUEST_CONFLICT','SameSetIntentCannotChangeItsParent');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',gen_random_uuid(),gen_random_uuid(),repeat('a',64),1,array['Pricing'])$$,'42501','HSR_PUBLIC_REVIEW_REQUIRED','MissingParentCannotAdmitASet');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('b',64),1,array['Pricing'])$$,'22023','HSR_VERIFIED_PUBLIC_REVIEW_REQUIRED','DifferentParentOriginalCannotAdmitASet');
select throws_ok(format($query$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',%L::uuid,gen_random_uuid(),repeat('a',64),%s,%L::text[])$query$,
 (select scan_id from parent),coalesce(budget::text,'null'),kinds),'22023','HSR_PUBLIC_TASK_SET_REQUEST_INVALID',test_name)
from (values
 (0,array['Pricing'],'ZeroTaskBudgetIsRejected'),
 (5,array['Pricing'],'AboveFourTaskBudgetIsRejected'),
 (null,array['Pricing'],'NullTaskBudgetIsRejected'),
 (1,null::text[],'NullKindPlanIsRejected'),
 (1,array['Pricing','Catalog'],'KindPlanCannotExceedItsBudget'),
 (2,array['Pricing','Pricing'],'DuplicateSetKindsAreRejected'),
 (2,array['Catalog','Pricing'],'NoncanonicalSetOrderIsRejected'),
 (1,array['Search'],'UnsupportedSetKindCannotAuthorizeSearchInput'),
 (1,array['pricing'],'WrongCaseSetKindIsRejected'),
 (1,array['Pricing '],'TrailingSpaceSetKindIsRejected'),
 (1,array[null]::text[],'NullMemberKindIsRejected'),
 (1,array[''],'EmptyMemberKindIsRejected'),
 (2,'{{Pricing,Catalog}}'::text[],'MultidimensionalKindPlanIsRejected'),
 (1,'[0:0]={Pricing}'::text[],'NoncanonicalArrayBoundsAreRejected')) cases(budget,kinds,test_name);
insert into task_sets values('empty',hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),4,'{}'));
select is((select jsonb_array_length(body->'Tasks') from task_sets where tag='empty'),0,'EmptyApplicablePlanQueuesNoTasks');
select ok((select body->>'IsAssessment'='false' from task_sets where tag='empty'),'EmptyPlanIsNotANoBarrierFinding');
insert into task_sets values('four',hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),4,array['Pricing','Catalog','Contact','Help']));
select is((select jsonb_array_length(body->'Tasks') from task_sets where tag='four'),4,'FourTaskBudgetAdmitsFourIndependentJobs');
select is((select jsonb_agg(value->'Job'->>'GoalKind' order by ordinal) from task_sets,jsonb_array_elements(body->'Tasks') with ordinality as items(value,ordinal) where tag='four'),'["Pricing","Catalog","Contact","Help"]'::jsonb,'TaskSetOrderMatchesItsClosedGoalPlan');
select is(hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='four'),repeat('a',64),4,array['Pricing','Catalog','Contact','Help']),(select body from task_sets where tag='four'),'ExistingSetReconcilesEvenWhenWorkspaceQueueIsFull');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),1,array['Help'])$$,'54000','HSR_PUBLIC_TASK_QUEUE_FULL','TaskSetsShareTheExistingFiveJobQueueLimit');
reset role;
select is((select count(*)::integer from hsr.public_tasks where organization_id='7c5a1200-0000-0000-0000-000000000001'),5,'RejectedOrReconciledSetsCreateNoExtraJobs');
select is((select count(*)::integer from hsr.public_task_sets where organization_id='7c5a1200-0000-0000-0000-000000000001'),3,'FullQueueRollsBackTheNewSetRow');
select is((select count(distinct job_id)::integer from hsr.public_task_set_members where organization_id='7c5a1200-0000-0000-0000-000000000001'),5,'EverySetMemberOwnsItsDistinctOriginalJob');
update hsr.public_tasks set state='blocked',error_code='PUBLIC_TASK_TEST_STOPPED' where id in
 (select id from hsr.public_tasks where organization_id='7c5a1200-0000-0000-0000-000000000001' order by created_at,id limit 2);
set local role authenticated;
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),4,array['Pricing','Catalog','Contact','Help'])$$,'54000','HSR_PUBLIC_TASK_QUEUE_FULL','MidSetCapacityFailureRollsBackEarlierChildren');
reset role;
select is((select count(*)::integer from hsr.public_tasks where organization_id='7c5a1200-0000-0000-0000-000000000001'),5,'FailedAtomicSetRetainsExactlyThePriorJobs');
select is((select count(*)::integer from pgmq.q_hsr_public_task_jobs where message->>'organizationId'='7c5a1200-0000-0000-0000-000000000001'),5,'FailedAtomicSetCannotLeaveOrphanQueueMessages');
select is((select count(*)::integer from hsr.public_task_sets where organization_id='7c5a1200-0000-0000-0000-000000000001'),3,'MidSetFailureCannotLeavePartialSetMetadata');
set local role authenticated;
select throws_ok($$select hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000002',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'))$$,'42501','HSR_SESSION_REQUIRED','ForeignTenantCannotReadTaskSets');
reset role;
set local request.jwt.claim.sub='7c5a1000-0000-0000-0000-000000000002';
set local request.jwt.claims='{"sub":"7c5a1000-0000-0000-0000-000000000002","session_id":"7c5a1100-0000-0000-0000-000000000002"}';
set local role authenticated;
select is(hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one')),null::jsonb,'SameTenantDifferentCreatorCannotBorrowASet');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('a',64),1,array['Pricing'])$$,'22023','HSR_REQUEST_CONFLICT','SameTenantDifferentCreatorCannotReconcileASet');
reset role;
set local role hsr_worker;
select throws_ok($$select hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid())$$,'42501',null,'WorkerCannotUseCustomerSetRead');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),gen_random_uuid(),repeat('a',64),1,array['Pricing'])$$,'42501',null,'WorkerCannotAdmitCustomerTaskSets');
reset role;
set local request.jwt.claim.sub='7c5a1000-0000-0000-0000-000000000001';
set local request.jwt.claims='{"sub":"7c5a1000-0000-0000-0000-000000000001","session_id":"7c5a1100-0000-0000-0000-000000000001"}';
update auth.sessions set not_after=clock_timestamp()-interval '1 second' where id='7c5a1100-0000-0000-0000-000000000001';
set local role authenticated;
select throws_ok($$select hsr.read_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'))$$,'42501','HSR_SESSION_REQUIRED','ExpiredCallerCannotReadAnExistingSet');
select throws_ok($$select hsr.create_public_task_set('7c5a1200-0000-0000-0000-000000000001',(select scan_id from parent),(select (body->>'RequestId')::uuid from task_sets where tag='one'),repeat('a',64),1,array['Pricing'])$$,'42501','HSR_SESSION_REQUIRED','ExpiredCallerCannotReconcileAnExistingSet');
reset role;
select * from finish();
rollback;
