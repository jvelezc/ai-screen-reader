begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated,anon,hsr_worker;
select plan(29);
insert into auth.users(id) values
 ('91000000-0000-0000-0000-000000000001'),
 ('91000000-0000-0000-0000-000000000002'),
 ('91000000-0000-0000-0000-000000000003');
create temporary table workspace_counts as select
 (select count(*) from hsr.organizations) as organizations,
 (select count(*) from pgmq.q_hsr_scan_jobs) as messages;

set local role anon;
set local request.jwt.claim.sub='91000000-0000-0000-0000-000000000001';
select throws_ok($$select * from hsr.ensure_personal_workspace()$$,'42501',null,
 'AnonymousCannotProvisionPersonalWorkspace');
set local role hsr_worker;
select throws_ok($$select * from hsr.ensure_personal_workspace()$$,'42501',null,
 'WorkerCannotProvisionPersonalWorkspace');
set local role authenticated;
set local request.jwt.claim.sub='';
select throws_ok($$select * from hsr.ensure_personal_workspace()$$,'42501','HSR_AUTHENTICATED_USER_REQUIRED',
 'PersonalWorkspaceRequiresAuthenticatedSubject');
set local request.jwt.claim.sub='91000000-0000-0000-0000-000000000099';
select throws_ok($$select * from hsr.ensure_personal_workspace()$$,'42501','HSR_AUTHENTICATED_USER_REQUIRED',
 'PersonalWorkspaceRequiresExistingAuthUser');
set local request.jwt.claim.sub='91000000-0000-0000-0000-000000000001';
select throws_ok($$select * from hsr.ensure_personal_workspace('91000000-0000-0000-0000-000000000002'::uuid)$$,
 '42883',null,'PersonalWorkspaceAcceptsNoCallerIdentityArgument');
create temporary table first_workspace as select * from hsr.ensure_personal_workspace();
select is((select count(*) from first_workspace),1::bigint,'PersonalWorkspaceReturnsOneReceipt');
select is((select created from first_workspace),true,'FirstPersonalWorkspaceReportsCreated');
select is((select count(*) from hsr.organizations),1::bigint,'PersonalWorkspaceOwnerSeesOwnOrganization');
select is((select user_id::text from hsr.memberships),
 '91000000-0000-0000-0000-000000000001','PersonalWorkspaceMembershipComesFromSubject');
create temporary table repeated_workspace as select * from hsr.ensure_personal_workspace();
select is((select organization_id from repeated_workspace),(select organization_id from first_workspace),
 'PersonalWorkspaceRetryReturnsSameOrganization');
select is((select created from repeated_workspace),false,'PersonalWorkspaceRetryReportsExisting');
select throws_ok($$select * from hsr.personal_workspaces$$,'42501',null,'ClientCannotReadPrivateWorkspaceMap');
select throws_ok($$insert into hsr.personal_workspaces select
 '91000000-0000-0000-0000-000000000002'::uuid,organization_id from first_workspace$$,
 '42501',null,'ClientCannotInsertPrivateWorkspaceMap');
select throws_ok($$update hsr.personal_workspaces set user_id='91000000-0000-0000-0000-000000000002'$$,
 '42501',null,'ClientCannotReassignPrivateWorkspaceMap');
select throws_ok($$delete from hsr.personal_workspaces$$,'42501',null,'ClientCannotDeletePrivateWorkspaceMap');
reset role;
select is((select count(*) from hsr.organizations),(select organizations+1 from workspace_counts),
 'PersonalWorkspaceRetryDoesNotLeaveOrphanOrganization');
select is((select count(*) from hsr.personal_workspaces),1::bigint,'PersonalWorkspaceStoresOnePrivateMapping');
select is((select count(*) from pgmq.q_hsr_scan_jobs),(select messages from workspace_counts),
 'PersonalWorkspaceDoesNotAdmitAJob');
set local role authenticated;
set local request.jwt.claim.sub='91000000-0000-0000-0000-000000000002';
create temporary table peer_workspace as select * from hsr.ensure_personal_workspace();
select isnt((select organization_id from peer_workspace),(select organization_id from first_workspace),
 'PeerSubjectReceivesIndependentWorkspace');
select is((select count(*) from hsr.organizations),1::bigint,'PeerCannotReadFirstPersonalWorkspace');
select throws_ok($$select * from hsr.create_scan((select organization_id from first_workspace),
 '92000000-0000-0000-0000-000000000001')$$,'42501','HSR_MEMBERSHIP_REQUIRED',
 'PeerCannotAdmitFirstPersonalWorkspaceJob');
reset role;
delete from hsr.memberships where user_id='91000000-0000-0000-0000-000000000001';
set local role authenticated;
set local request.jwt.claim.sub='91000000-0000-0000-0000-000000000001';
select throws_ok($$select * from hsr.ensure_personal_workspace()$$,'42501','HSR_WORKSPACE_MEMBERSHIP_REVOKED',
 'PersonalWorkspaceBootstrapCannotUndoRevocation');
reset role;
select is((select count(*) from hsr.memberships where user_id='91000000-0000-0000-0000-000000000001'),
 0::bigint,'RevokedPersonalWorkspaceMembershipRemainsAbsent');
create function pg_temp.reject_workspace_member() returns trigger language plpgsql as $$
begin raise exception 'INJECTED_WORKSPACE_MEMBERSHIP_FAILURE'; end $$;
create trigger reject_workspace_member before insert on hsr.memberships
 for each row execute function pg_temp.reject_workspace_member();
set local role authenticated;
set local request.jwt.claim.sub='91000000-0000-0000-0000-000000000003';
select throws_ok($$select * from hsr.ensure_personal_workspace()$$,'P0001','INJECTED_WORKSPACE_MEMBERSHIP_FAILURE',
 'MembershipWriteFailureAbortsPersonalWorkspace');
reset role;
drop trigger reject_workspace_member on hsr.memberships;
select is((select count(*) from hsr.organizations),(select organizations+2 from workspace_counts),
 'FailedPersonalWorkspaceLeavesNoOrganization');
select is((select count(*) from hsr.personal_workspaces where user_id='91000000-0000-0000-0000-000000000003'),
 0::bigint,'FailedPersonalWorkspaceLeavesNoMapping');
select is((select count(*) from hsr.memberships where user_id='91000000-0000-0000-0000-000000000003'),
 0::bigint,'FailedPersonalWorkspaceLeavesNoMembership');
set local role authenticated;
create temporary table organizations(id uuid,name text);
create temporary table memberships(organization_id uuid,user_id uuid);
create temporary table personal_workspaces(user_id uuid,organization_id uuid);
set local search_path=pg_temp,hsr,extensions,public;
create temporary table shadow_workspace as select * from hsr.ensure_personal_workspace();
select is((select count(*) from pg_temp.organizations)+(select count(*) from pg_temp.memberships)
 +(select count(*) from pg_temp.personal_workspaces),0::bigint,'PersonalWorkspaceIgnoresShadowTables');
reset role;
select is((select organization_id from hsr.personal_workspaces
 where user_id='91000000-0000-0000-0000-000000000003'),(select organization_id from shadow_workspace),
 'PersonalWorkspaceWritesRealSubjectMappingDespiteSearchPath');
select * from finish();
rollback;
