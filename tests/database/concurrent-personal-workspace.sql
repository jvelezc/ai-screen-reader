create extension if not exists dblink with schema extensions;
set search_path=extensions,public;
select plan(6);
insert into auth.users(id) values('93000000-0000-0000-0000-000000000001');
create temporary table workspace_before as select count(*) as organizations from hsr.organizations;
select dblink_connect('hsr_workspace_first','dbname=postgres user=postgres application_name=hsr_workspace_first');
select dblink_connect('hsr_workspace_second','dbname=postgres user=postgres application_name=hsr_workspace_second');
select dblink_exec('hsr_workspace_first','begin');
select dblink_exec('hsr_workspace_first','set role authenticated');
select dblink_exec('hsr_workspace_first',$$set request.jwt.claim.sub='93000000-0000-0000-0000-000000000001'$$);
select dblink_exec('hsr_workspace_second','set role authenticated');
select dblink_exec('hsr_workspace_second',$$set request.jwt.claim.sub='93000000-0000-0000-0000-000000000001'$$);
create temporary table workspace_first as select * from dblink('hsr_workspace_first',
 $$select * from hsr.ensure_personal_workspace()$$) as t(organization_id uuid,created boolean);
select dblink_send_query('hsr_workspace_second',$$select * from hsr.ensure_personal_workspace()$$);
do $$
declare deadline timestamptz:=clock_timestamp()+interval '5 seconds';
begin
 loop
  perform pg_stat_clear_snapshot();
  exit when exists(select 1 from pg_stat_activity
   where application_name='hsr_workspace_second' and wait_event_type='Lock');
  exit when clock_timestamp()>=deadline;
  perform pg_sleep(0.01);
 end loop;
end $$;
select ok(exists(select 1 from pg_stat_activity
 where application_name='hsr_workspace_second' and wait_event_type='Lock'),
 'ConcurrentPersonalWorkspaceWaitsForSubjectBootstrap');
select dblink_exec('hsr_workspace_first','commit');
create temporary table workspace_second as select * from dblink_get_result('hsr_workspace_second')
 as t(organization_id uuid,created boolean);
select is((select organization_id from workspace_first),(select organization_id from workspace_second),
 'ConcurrentPersonalWorkspaceReturnsSameCommittedOrganization');
select is((select created from workspace_second),false,'ConcurrentPersonalWorkspaceReportsExistingAfterCommit');
select is((select count(*) from hsr.personal_workspaces where user_id='93000000-0000-0000-0000-000000000001'),
 1::bigint,'ConcurrentPersonalWorkspaceCreatesOneMapping');
select is((select count(*) from hsr.memberships where user_id='93000000-0000-0000-0000-000000000001'),
 1::bigint,'ConcurrentPersonalWorkspaceCreatesOneOwnerMembership');
select is((select count(*) from hsr.organizations),(select organizations+1 from workspace_before),
 'ConcurrentPersonalWorkspaceLeavesNoExtraOrganization');
select count(*) from dblink_get_result('hsr_workspace_second') as t(organization_id uuid,created boolean);
select dblink_disconnect('hsr_workspace_first');
select dblink_disconnect('hsr_workspace_second');
select * from finish();
