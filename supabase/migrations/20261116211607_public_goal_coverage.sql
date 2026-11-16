-- Source-bound optional service-goal selection. Legacy jobs and original bytes remain unchanged.
alter table hsr.public_tasks add column goal_kind text
 check(goal_kind in('Pricing','Catalog','Contact','Help'));

create or replace function hsr.public_task_job(p hsr.public_tasks) returns jsonb
language sql immutable set search_path='' as $$
 select jsonb_build_object('OrganizationId',p.organization_id,'ParentScanId',p.parent_scan_id,'JobId',p.id,
 'RequestId',p.request_id,'Generation',1,'ParentOriginalSha256',p.parent_original_sha256,
 'Source',jsonb_build_object('Profile','hsr-approved-public-readonly-v1','TargetUrl',p.target_url,'AllowedHost',p.allowed_host))
 || case when p.goal_kind is null then '{}'::jsonb else jsonb_build_object('GoalKind',p.goal_kind) end;
$$;
revoke all on function hsr.public_task_job(hsr.public_tasks) from public,anon,authenticated,hsr_worker;

create function hsr.create_public_task_for_goal(p_organization_id uuid,p_parent_scan_id uuid,p_request_id uuid,p_parent_original_sha256 text,p_goal_kind text) returns jsonb
language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid(); sid uuid; parent hsr.public_scans%rowtype; selected hsr.public_tasks%rowtype; observed hsr.public_scan_jobs%rowtype;
begin
 begin sid:=(auth.jwt()->>'session_id')::uuid; exception when others then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=actor for key share;
 if not found or not hsr.public_task_caller_alive(actor,sid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_goal_kind is not null and p_goal_kind not in('Pricing','Catalog','Contact','Help') then raise exception using errcode='22023',message='HSR_PUBLIC_TASK_GOAL_INVALID'; end if;
 if p_request_id is null or p_request_id='00000000-0000-0000-0000-000000000000'::uuid or p_parent_original_sha256 is null or p_parent_original_sha256 !~ '^[a-f0-9]{64}$' then
  raise exception using errcode='22023',message='HSR_PUBLIC_TASK_REQUEST_INVALID'; end if;
 perform pg_advisory_xact_lock(hashtextextended('hsr-public-task-admission:'||p_organization_id::text,0));
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.request_id=p_request_id;
 if found then
  if selected.parent_scan_id<>p_parent_scan_id or selected.parent_original_sha256<>p_parent_original_sha256 or selected.created_by<>actor or selected.goal_kind is distinct from p_goal_kind then
   raise exception using errcode='22023',message='HSR_REQUEST_CONFLICT'; end if;
  return hsr.public_task_metadata(selected);
 end if;
 select * into parent from hsr.public_scans s where s.organization_id=p_organization_id and s.id=p_parent_scan_id for key share;
 if not found then raise exception using errcode='42501',message='HSR_PUBLIC_REVIEW_REQUIRED'; end if;
 select * into observed from hsr.public_scan_jobs j where j.organization_id=p_organization_id and j.scan_id=p_parent_scan_id for key share;
 if not found or observed.state<>'completed' or observed.result_digest<>p_parent_original_sha256 or position('?' in parent.target_url)>0 then
  raise exception using errcode='22023',message='HSR_VERIFIED_PUBLIC_REVIEW_REQUIRED'; end if;
 if (select count(*) from hsr.public_tasks t where t.organization_id=p_organization_id and t.state in('queued','leased'))>=5 then
  raise exception using errcode='54000',message='HSR_PUBLIC_TASK_QUEUE_FULL'; end if;
 insert into hsr.public_tasks(organization_id,parent_scan_id,request_id,created_by,session_id,target_url,allowed_host,parent_original_sha256,goal_kind)
 values(p_organization_id,p_parent_scan_id,p_request_id,actor,sid,parent.target_url,parent.allowed_host,p_parent_original_sha256,p_goal_kind) returning * into selected;
 perform pgmq.send('hsr_public_task_jobs',jsonb_build_object('schema','hsr-public-task-job-v1','organizationId',selected.organization_id,'jobId',selected.id));
 return hsr.public_task_metadata(selected);
end $$;
revoke all on function hsr.create_public_task_for_goal(uuid,uuid,uuid,text,text) from public,anon,authenticated;
grant execute on function hsr.create_public_task_for_goal(uuid,uuid,uuid,text,text) to authenticated;

create or replace function hsr.create_public_task(p_organization_id uuid,p_parent_scan_id uuid,p_request_id uuid,p_parent_original_sha256 text) returns jsonb
language sql security definer set search_path='' as $$
 select hsr.create_public_task_for_goal(p_organization_id,p_parent_scan_id,p_request_id,p_parent_original_sha256,null);
$$;
revoke all on function hsr.create_public_task(uuid,uuid,uuid,text) from public,anon,authenticated;
grant execute on function hsr.create_public_task(uuid,uuid,uuid,text) to authenticated;

create or replace function hsr.read_public_task(p_organization_id uuid,p_parent_scan_id uuid,p_job_id uuid default null,p_request_id uuid default null) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.public_tasks%rowtype;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.parent_scan_id=p_parent_scan_id
  and (p_job_id is not null or p_request_id is not null or t.goal_kind is null)
  and (p_job_id is null or t.id=p_job_id) and (p_request_id is null or t.request_id=p_request_id) order by t.created_at desc,t.id limit 1;
 if not found then return null; end if;
 return hsr.public_task_metadata(selected);
end $$;
revoke all on function hsr.read_public_task(uuid,uuid,uuid,uuid) from public,anon,authenticated;
grant execute on function hsr.read_public_task(uuid,uuid,uuid,uuid) to authenticated;

-- Bounded current metadata, one latest job for each declared goal and one unattributed automatic job.
-- This does not verify originals or infer an assessment from a missing job.
create function hsr.read_public_task_coverage(p_organization_id uuid,p_parent_scan_id uuid,p_parent_original_sha256 text) returns jsonb
language plpgsql security definer set search_path='' as $$
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then
  raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_parent_original_sha256 is null or p_parent_original_sha256 !~ '^[a-f0-9]{64}$' then
  raise exception using errcode='22023',message='HSR_PUBLIC_TASK_REQUEST_INVALID'; end if;
 return coalesce((select jsonb_agg(hsr.public_task_metadata(t.job) order by (t.job).goal_kind collate "C" nulls first)
 from (select distinct on (p.goal_kind) p as job from hsr.public_tasks p
 where p.organization_id=p_organization_id and p.parent_scan_id=p_parent_scan_id
 and p.parent_original_sha256=p_parent_original_sha256
 order by p.goal_kind,p.created_at desc,p.id desc) t),'[]'::jsonb);
end $$;
revoke all on function hsr.read_public_task_coverage(uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.read_public_task_coverage(uuid,uuid,text) to authenticated;
