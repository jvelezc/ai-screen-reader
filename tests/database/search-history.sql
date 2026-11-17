-- Disposable database only; no target input or customer identities.
begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,anon,hsr_worker;
select no_plan();
insert into auth.users(id) values('b2100000-0000-4000-8000-000000000001'),('b2100000-0000-4000-8000-000000000002');
insert into auth.sessions(id,user_id,not_after) values
 ('b2110000-0000-4000-8000-000000000001','b2100000-0000-4000-8000-000000000001',clock_timestamp()+interval '1 hour');
insert into hsr.organizations(id,name) values('b2120000-0000-4000-8000-000000000001','History A'),('b2120000-0000-4000-8000-000000000002','History B');
insert into hsr.memberships values
 ('b2120000-0000-4000-8000-000000000001','b2100000-0000-4000-8000-000000000001'),
 ('b2120000-0000-4000-8000-000000000001','b2100000-0000-4000-8000-000000000002');
set local request.jwt.claim.sub='b2100000-0000-4000-8000-000000000001';
set local request.jwt.claims='{"sub":"b2100000-0000-4000-8000-000000000001","session_id":"b2110000-0000-4000-8000-000000000001"}';
create temporary table pages(tag text,body jsonb);
grant select,insert on pages to authenticated;
set local role anon;
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working')$$,'42501',null,'AnonymousHistoryDoesNotExposeSavedJobs');
reset role;
set local role hsr_worker;
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working')$$,'42501',null,'WorkerHistoryDoesNotAcquireCallerAuthority');
reset role;
set local role authenticated;
insert into pages values('empty',hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working'));
select is((select body->'Items' from pages where tag='empty'),'[]'::jsonb,'EmptyHistoryIsAnAuthorizedEmptyPage');
select is((select body->'NextBeforeJobId' from pages where tag='empty'),'null'::jsonb,'EmptyHistoryHasNoContinuation');
select is((select body->>'Schema' from pages where tag='empty'),'hsr-search-task-history-v1','HistoryKeepsItsMetadataOnlySchema');
select ok((select body->'WebsiteInputStarted'='false'::jsonb and body->'IsAssessment'='false'::jsonb from pages where tag='empty'),'HistoryNeverClaimsInputOrAssessment');
reset role;
insert into hsr.search_tasks(organization_id,id,request_id,created_by,session_id,source_id,created_at,updated_at)
select 'b2120000-0000-4000-8000-000000000001',('b2130000-0000-4000-8000-'||lpad(to_hex(i),12,'0'))::uuid,gen_random_uuid(),
 'b2100000-0000-4000-8000-000000000001','b2110000-0000-4000-8000-000000000001','search-working',
 statement_timestamp()-make_interval(secs=>case when i<=2 then 0 else i end),statement_timestamp()
from generate_series(1,13) i;
insert into hsr.search_tasks(organization_id,id,request_id,created_by,session_id,source_id) values
 ('b2120000-0000-4000-8000-000000000001','b2130000-0000-4000-8000-00000000000e',gen_random_uuid(),'b2100000-0000-4000-8000-000000000001','b2110000-0000-4000-8000-000000000001','search-keyboard-blocked'),
 ('b2120000-0000-4000-8000-000000000001','b2130000-0000-4000-8000-00000000000f',gen_random_uuid(),'b2100000-0000-4000-8000-000000000002',gen_random_uuid(),'search-working'),
 ('b2120000-0000-4000-8000-000000000002','b2130000-0000-4000-8000-000000000010',gen_random_uuid(),'b2100000-0000-4000-8000-000000000002',gen_random_uuid(),'search-working');
set local role authenticated;
insert into pages values('first',hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working'));
select is((select jsonb_array_length(body->'Items') from pages where tag='first'),10,'HistoryFirstPageIsBoundedToTen');
select is((select body->>'NextBeforeJobId' from pages where tag='first'),'b2130000-0000-4000-8000-00000000000a','ContinuationNamesTheLastReturnedJob');
select is((select body->'Items'->0->'Job'->>'JobId' from pages where tag='first'),'b2130000-0000-4000-8000-000000000001','TiedTimestampsUseAscendingJobIdentity');
select is((select body->'Items'->1->'Job'->>'JobId' from pages where tag='first'),'b2130000-0000-4000-8000-000000000002','TiedSecondItemIsNotSkipped');
insert into pages values('second',hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working',(select (body->>'NextBeforeJobId')::uuid from pages where tag='first')));
select is((select jsonb_array_length(body->'Items') from pages where tag='second'),3,'LastPageHasExactlyTheRemainingCallerJobs');
select is((select body->'NextBeforeJobId' from pages where tag='second'),'null'::jsonb,'LastPageDoesNotInventContinuation');
select is((select count(distinct value->'Job'->>'JobId')::integer from pages p cross join lateral jsonb_array_elements(p.body->'Items') where tag in('first','second')),13,'PaginationNeitherRepeatsNorLosesAJob');
select ok((select bool_and(value->>'CreatedBy'='b2100000-0000-4000-8000-000000000001' and value->'Job'->'Source'->>'Id'='search-working') from pages p cross join lateral jsonb_array_elements(p.body->'Items') where tag in('first','second')),'HistoryNeverBorrowsAnotherCreatorOrSample');
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','https://customer.invalid/')$$,'22023','HSR_REQUEST_INVALID','HistoryCannotAcceptCustomerTargets');
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000002','search-working')$$,'42501','HSR_SESSION_REQUIRED','HistoryRequiresCurrentTenantMembership');
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working','b2130000-0000-4000-8000-00000000000e')$$,'42501','HSR_READ_DENIED','CursorCannotBelongToAnotherSample');
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working','b2130000-0000-4000-8000-00000000000f')$$,'42501','HSR_READ_DENIED','CursorCannotBelongToAnotherCreator');
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working','b2130000-0000-4000-8000-000000000010')$$,'42501','HSR_READ_DENIED','CursorCannotBelongToAnotherTenant');
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working','00000000-0000-0000-0000-000000000001')$$,'42501','HSR_READ_DENIED','MissingCursorIsDeniedRatherThanRestartingTheList');
reset role;
select is((select count(*)::integer from hsr.search_tasks),16,'HistoryReadsCreateNoReplacementJobs');
select is((select count(*)::integer from pgmq.q_hsr_search_task_jobs),0,'HistoryReadsQueueNoWebsiteInput');
delete from hsr.memberships where organization_id='b2120000-0000-4000-8000-000000000001' and user_id='b2100000-0000-4000-8000-000000000001';
set local role authenticated;
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working')$$,'42501','HSR_SESSION_REQUIRED','MembershipRevocationWinsOverSavedHistory');
reset role;
insert into hsr.memberships values('b2120000-0000-4000-8000-000000000001','b2100000-0000-4000-8000-000000000001');
update auth.sessions set not_after=clock_timestamp()-interval '1 second' where id='b2110000-0000-4000-8000-000000000001';
set local role authenticated;
select throws_ok($$select hsr.read_search_task_history('b2120000-0000-4000-8000-000000000001','search-working')$$,'42501','HSR_SESSION_REQUIRED','ExpiredSessionCannotReadHistory');
reset role;
select * from finish();
rollback;
