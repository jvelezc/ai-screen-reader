-- One explicit, bounded admission of ordinary independent service-link tasks.
-- Metadata groups their identities; it never aggregates evidence or declares conformance.
create function hsr.valid_public_task_set_kinds(p_kinds text[],p_maximum integer) returns boolean
language sql immutable set search_path='' as $$
 select coalesce(p_maximum between 1 and 4 and p_kinds is not null
  and cardinality(p_kinds)<=p_maximum
  and p_kinds=coalesce((select array_agg(kind order by ordinal)
   from unnest(array['Pricing','Catalog','Contact','Help']) with ordinality as supported(kind,ordinal)
   where kind=any(p_kinds)),'{}'::text[]),false);
$$;
revoke all on function hsr.valid_public_task_set_kinds(text[],integer) from public,anon,authenticated,hsr_worker;

create table hsr.public_task_sets(
 organization_id uuid not null references hsr.organizations(id),id uuid not null default gen_random_uuid(),
 parent_scan_id uuid not null,request_id uuid not null,created_by uuid not null references auth.users(id),session_id uuid not null,
 target_url text not null,allowed_host text not null,
 parent_original_sha256 text not null check(parent_original_sha256 ~ '^[a-f0-9]{64}$'),
 maximum_tasks integer not null,goal_kinds text[] not null,created_at timestamptz not null default clock_timestamp(),
 primary key(organization_id,id),unique(organization_id,request_id),
 foreign key(organization_id,parent_scan_id) references hsr.public_scans(organization_id,id),
 check(request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 check(hsr.valid_public_source(target_url,allowed_host) and position('?' in target_url)=0),
 check(hsr.valid_public_task_set_kinds(goal_kinds,maximum_tasks))
);
create index public_task_sets_parent on hsr.public_task_sets(organization_id,parent_scan_id,created_at desc,id);
create index public_task_sets_creator on hsr.public_task_sets(created_by);
alter table hsr.public_task_sets enable row level security;
revoke all on hsr.public_task_sets from public,anon,authenticated,hsr_worker;

create table hsr.public_task_set_members(
 organization_id uuid not null,set_id uuid not null,ordinal integer not null check(ordinal between 1 and 4),
 goal_kind text not null check(goal_kind in('Pricing','Catalog','Contact','Help')),job_id uuid not null,
 primary key(organization_id,set_id,ordinal),unique(organization_id,set_id,goal_kind),unique(organization_id,job_id),
 foreign key(organization_id,set_id) references hsr.public_task_sets(organization_id,id),
 foreign key(organization_id,job_id) references hsr.public_tasks(organization_id,id)
);
alter table hsr.public_task_set_members enable row level security;
revoke all on hsr.public_task_set_members from public,anon,authenticated,hsr_worker;

create function hsr.public_task_set_member_metadata(p hsr.public_tasks) returns jsonb
language sql stable set search_path='' as $$
 select (hsr.public_task_metadata(p)-'Publication'-'ProducerId')
  ||jsonb_build_object('Schema','hsr-public-task-set-member-v1');
$$;
revoke all on function hsr.public_task_set_member_metadata(hsr.public_tasks) from public,anon,authenticated,hsr_worker;

create function hsr.public_task_set_metadata(p hsr.public_task_sets) returns jsonb
language sql stable set search_path='' as $$
 select jsonb_build_object('Schema','hsr-public-task-set-v1','OrganizationId',p.organization_id,
  'SetId',p.id,'ParentScanId',p.parent_scan_id,'RequestId',p.request_id,'CreatedBy',p.created_by,
  'CreatedAt',p.created_at,'ParentOriginalSha256',p.parent_original_sha256,
  'MaximumTasks',p.maximum_tasks,'GoalKinds',to_jsonb(p.goal_kinds),
  'Source',jsonb_build_object('Profile','hsr-approved-public-readonly-v1','TargetUrl',p.target_url,'AllowedHost',p.allowed_host),
  'Scope','CapturedPageServiceLinks','TaskResultsRequireOriginalRead',true,'IsAssessment',false,
  'Tasks',coalesce((select jsonb_agg(hsr.public_task_set_member_metadata(t) order by m.ordinal)
   from hsr.public_task_set_members m join hsr.public_tasks t
    on t.organization_id=m.organization_id and t.id=m.job_id
   where m.organization_id=p.organization_id and m.set_id=p.id),'[]'::jsonb));
$$;
revoke all on function hsr.public_task_set_metadata(hsr.public_task_sets) from public,anon,authenticated,hsr_worker;

create function hsr.create_public_task_set(p_organization_id uuid,p_parent_scan_id uuid,p_request_id uuid,
 p_parent_original_sha256 text,p_maximum_tasks integer,p_goal_kinds text[]) returns jsonb
language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid(); sid uuid; selected hsr.public_task_sets%rowtype;
 parent hsr.public_scans%rowtype; observed hsr.public_scan_jobs%rowtype;
child hsr.public_tasks%rowtype; child_request uuid; kind text; member_ordinal integer:=0;
begin
 begin sid:=(auth.jwt()->>'session_id')::uuid; exception when others then
  raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=actor for key share;
 if not found or not hsr.public_task_caller_alive(actor,sid,p_organization_id) then
  raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_request_id is null or p_request_id='00000000-0000-0000-0000-000000000000'::uuid
  or p_parent_original_sha256 is null or p_parent_original_sha256 !~ '^[a-f0-9]{64}$'
  or not hsr.valid_public_task_set_kinds(p_goal_kinds,p_maximum_tasks) then
  raise exception using errcode='22023',message='HSR_PUBLIC_TASK_SET_REQUEST_INVALID'; end if;
 -- The same order/lock as individual admissions; child calls are transaction-reentrant.
 perform pg_advisory_xact_lock(hashtextextended('hsr-public-task-admission:'||p_organization_id::text,0));
 if not hsr.public_task_caller_alive(actor,sid,p_organization_id) then
  raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 select * into selected from hsr.public_task_sets s where s.organization_id=p_organization_id and s.request_id=p_request_id;
 if found then
  if selected.parent_scan_id<>p_parent_scan_id or selected.created_by<>actor
   or selected.parent_original_sha256<>p_parent_original_sha256 or selected.maximum_tasks<>p_maximum_tasks
   or selected.goal_kinds is distinct from p_goal_kinds then
   raise exception using errcode='22023',message='HSR_REQUEST_CONFLICT'; end if;
  return hsr.public_task_set_metadata(selected);
 end if;
 select * into parent from hsr.public_scans s where s.organization_id=p_organization_id and s.id=p_parent_scan_id for key share;
 if not found then raise exception using errcode='42501',message='HSR_PUBLIC_REVIEW_REQUIRED'; end if;
 select * into observed from hsr.public_scan_jobs j where j.organization_id=p_organization_id and j.scan_id=p_parent_scan_id for key share;
 if not found or observed.state<>'completed' or observed.result_digest<>p_parent_original_sha256
  or position('?' in parent.target_url)>0 then
  raise exception using errcode='22023',message='HSR_VERIFIED_PUBLIC_REVIEW_REQUIRED'; end if;
 insert into hsr.public_task_sets(organization_id,parent_scan_id,request_id,created_by,session_id,target_url,allowed_host,
  parent_original_sha256,maximum_tasks,goal_kinds)
 values(p_organization_id,p_parent_scan_id,p_request_id,actor,sid,parent.target_url,parent.allowed_host,
  p_parent_original_sha256,p_maximum_tasks,p_goal_kinds) returning * into selected;
 foreach kind in array p_goal_kinds loop
  member_ordinal:=member_ordinal+1;
  child_request:=gen_random_uuid();
  if exists(select 1 from hsr.public_tasks t where t.organization_id=p_organization_id and t.request_id=child_request) then
   raise exception using errcode='22023',message='HSR_PUBLIC_TASK_SET_REQUEST_COLLISION'; end if;
  perform hsr.create_public_task_for_goal(p_organization_id,p_parent_scan_id,child_request,p_parent_original_sha256,kind);
  select * into strict child from hsr.public_tasks t where t.organization_id=p_organization_id and t.request_id=child_request;
  insert into hsr.public_task_set_members(organization_id,set_id,ordinal,goal_kind,job_id)
   values(p_organization_id,selected.id,member_ordinal,kind,child.id);
 end loop;
 return hsr.public_task_set_metadata(selected);
end $$;
revoke all on function hsr.create_public_task_set(uuid,uuid,uuid,text,integer,text[]) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.create_public_task_set(uuid,uuid,uuid,text,integer,text[]) to authenticated;

create function hsr.read_public_task_set(p_organization_id uuid,p_parent_scan_id uuid,p_request_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.public_task_sets%rowtype;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then
  raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_request_id is null or p_request_id='00000000-0000-0000-0000-000000000000'::uuid then
  raise exception using errcode='22023',message='HSR_PUBLIC_TASK_SET_REQUEST_INVALID'; end if;
 select * into selected from hsr.public_task_sets s where s.organization_id=p_organization_id
  and s.parent_scan_id=p_parent_scan_id and s.request_id=p_request_id and s.created_by=auth.uid();
 if not found then return null; end if;
 return hsr.public_task_set_metadata(selected);
end $$;
revoke all on function hsr.read_public_task_set(uuid,uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.read_public_task_set(uuid,uuid,uuid) to authenticated;
