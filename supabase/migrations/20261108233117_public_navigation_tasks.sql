-- Independent one-shot input authority. Passive website jobs and their zero-input contract are unchanged.
select pgmq.create('hsr_public_task_jobs');
alter table pgmq.q_hsr_public_task_jobs enable row level security;
alter table pgmq.a_hsr_public_task_jobs enable row level security;
revoke all on pgmq.q_hsr_public_task_jobs,pgmq.a_hsr_public_task_jobs from public,anon,authenticated,hsr_worker;

create table hsr.public_tasks(
 organization_id uuid not null references hsr.organizations(id),id uuid not null default gen_random_uuid(),
 parent_scan_id uuid not null,request_id uuid not null,created_by uuid not null references auth.users(id),session_id uuid not null,
 target_url text not null,allowed_host text not null,parent_original_sha256 text not null check(parent_original_sha256 ~ '^[a-f0-9]{64}$'),
 state text not null default 'queued' check(state in('queued','leased','completed','blocked','cancelled','interrupted')),
 phase text not null default 'Queued' check(phase in('Queued','Starting','KeyboardAttempt1','KeyboardAttempt2','PointerComparison','Finalizing','Finished')),
 created_at timestamptz not null default clock_timestamp(),updated_at timestamptz not null default clock_timestamp(),
 worker_id uuid,lease_token uuid,lease_expires_at timestamptz,started_at timestamptz,finished_at timestamptz,
 report_sha256 text check(report_sha256 ~ '^[a-f0-9]{64}$'),publication jsonb,
 error_code text check(error_code ~ '^PUBLIC_TASK_[A-Z_]{1,80}$'),
 primary key(organization_id,id),unique(organization_id,request_id),
 foreign key(organization_id,parent_scan_id) references hsr.public_scans(organization_id,id),
 check(request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 check(hsr.valid_public_source(target_url,allowed_host) and position('?' in target_url)=0),
 check((state='completed')=(report_sha256 is not null and publication is not null)),
 check(publication is null or octet_length(publication::text)<=65536)
);
create index public_tasks_parent on hsr.public_tasks(organization_id,parent_scan_id,created_at desc,id);
create index public_tasks_creator on hsr.public_tasks(created_by);
create index public_tasks_pending on hsr.public_tasks(state,created_at,id);
alter table hsr.public_tasks enable row level security;
revoke all on hsr.public_tasks from public,anon,authenticated,hsr_worker;

create function hsr.public_task_caller_alive(p_actor uuid,p_session uuid,p_org uuid) returns boolean
language sql security definer set search_path='' as $$
 select exists(select 1 from hsr.memberships m where m.organization_id=p_org and m.user_id=p_actor)
 and exists(select 1 from auth.sessions s where s.id=p_session and s.user_id=p_actor and (s.not_after is null or s.not_after>clock_timestamp()));
$$;
revoke all on function hsr.public_task_caller_alive(uuid,uuid,uuid) from public,anon,authenticated,hsr_worker;

create function hsr.public_task_job(p hsr.public_tasks) returns jsonb
language sql immutable set search_path='' as $$
 select jsonb_build_object('OrganizationId',p.organization_id,'ParentScanId',p.parent_scan_id,'JobId',p.id,
 'RequestId',p.request_id,'Generation',1,'ParentOriginalSha256',p.parent_original_sha256,
 'Source',jsonb_build_object('Profile','hsr-approved-public-readonly-v1','TargetUrl',p.target_url,'AllowedHost',p.allowed_host));
$$;
revoke all on function hsr.public_task_job(hsr.public_tasks) from public,anon,authenticated,hsr_worker;

create function hsr.public_task_metadata(p hsr.public_tasks) returns jsonb
language sql stable set search_path='' as $$
 select jsonb_build_object('Schema','hsr-public-task-status-v1','Job',hsr.public_task_job(p),
 'State',case when p.state='leased' and p.lease_expires_at<=clock_timestamp() then 'interrupted' else p.state end,
 'Phase',p.phase,'CreatedBy',p.created_by,'CreatedAt',p.created_at,'UpdatedAt',p.updated_at,'FinishedAt',p.finished_at,
 'ProducerId',case when p.state='completed' then p.worker_id else null end,
 'ReportSha256',p.report_sha256,'Publication',p.publication,'ErrorCode',p.error_code);
$$;
revoke all on function hsr.public_task_metadata(hsr.public_tasks) from public,anon,authenticated,hsr_worker;

create function hsr.create_public_task(p_organization_id uuid,p_parent_scan_id uuid,p_request_id uuid,p_parent_original_sha256 text) returns jsonb
language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid(); sid uuid; parent hsr.public_scans%rowtype; selected hsr.public_tasks%rowtype; observed hsr.public_scan_jobs%rowtype;
begin
 begin sid:=(auth.jwt()->>'session_id')::uuid; exception when others then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=actor for key share;
 if not found or not hsr.public_task_caller_alive(actor,sid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_request_id is null or p_request_id='00000000-0000-0000-0000-000000000000'::uuid or p_parent_original_sha256 is null or p_parent_original_sha256 !~ '^[a-f0-9]{64}$' then
  raise exception using errcode='22023',message='HSR_PUBLIC_TASK_REQUEST_INVALID'; end if;
 perform pg_advisory_xact_lock(hashtextextended('hsr-public-task-admission:'||p_organization_id::text,0));
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.request_id=p_request_id;
 if found then
  if selected.parent_scan_id<>p_parent_scan_id or selected.parent_original_sha256<>p_parent_original_sha256 or selected.created_by<>actor then
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
 insert into hsr.public_tasks(organization_id,parent_scan_id,request_id,created_by,session_id,target_url,allowed_host,parent_original_sha256)
 values(p_organization_id,p_parent_scan_id,p_request_id,actor,sid,parent.target_url,parent.allowed_host,p_parent_original_sha256) returning * into selected;
 perform pgmq.send('hsr_public_task_jobs',jsonb_build_object('schema','hsr-public-task-job-v1','organizationId',selected.organization_id,'jobId',selected.id));
 return hsr.public_task_metadata(selected);
end $$;
revoke all on function hsr.create_public_task(uuid,uuid,uuid,text) from public,anon,authenticated;
grant execute on function hsr.create_public_task(uuid,uuid,uuid,text) to authenticated;

create function hsr.read_public_task(p_organization_id uuid,p_parent_scan_id uuid,p_job_id uuid default null,p_request_id uuid default null) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.public_tasks%rowtype;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.parent_scan_id=p_parent_scan_id
  and (p_job_id is null or t.id=p_job_id) and (p_request_id is null or t.request_id=p_request_id) order by t.created_at desc,t.id limit 1;
 if not found then return null; end if;
 return hsr.public_task_metadata(selected);
end $$;
revoke all on function hsr.read_public_task(uuid,uuid,uuid,uuid) from public,anon,authenticated;
grant execute on function hsr.read_public_task(uuid,uuid,uuid,uuid) to authenticated;

create function hsr.cancel_public_task(p_organization_id uuid,p_parent_scan_id uuid,p_job_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.public_tasks%rowtype;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.parent_scan_id=p_parent_scan_id and t.id=p_job_id for update;
 if not found then return null; end if;
 if selected.state in('queued','leased') then
  update hsr.public_tasks t set state='cancelled',error_code='PUBLIC_TASK_CANCELLED',updated_at=clock_timestamp(),finished_at=clock_timestamp(),lease_token=null
   where t.organization_id=p_organization_id and t.id=p_job_id returning * into selected;
 end if;
 return hsr.public_task_metadata(selected);
end $$;
revoke all on function hsr.cancel_public_task(uuid,uuid,uuid) from public,anon,authenticated;
grant execute on function hsr.cancel_public_task(uuid,uuid,uuid) to authenticated;

create function hsr.claim_public_task(p_worker_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare message record; selected hsr.public_tasks%rowtype; capability uuid;
begin
 if p_worker_id is null or p_worker_id='00000000-0000-0000-0000-000000000000'::uuid then raise exception using errcode='22023',message='HSR_WORKER_REQUIRED'; end if;
 perform pg_advisory_xact_lock(hashtextextended('hsr-public-task-worker',0));
 -- Expiration never requeues input. Possible dispatch and cleanup remain explicit and fenced on disk.
 update hsr.public_tasks set state='interrupted',error_code='PUBLIC_TASK_LEASE_EXPIRED',updated_at=clock_timestamp(),finished_at=clock_timestamp(),lease_token=null
  where state='leased' and lease_expires_at<=clock_timestamp();
 if exists(select 1 from hsr.public_tasks where state='leased') then return jsonb_build_object('Status','AtCapacity'); end if;
 for message in select * from pgmq.read('hsr_public_task_jobs',30,1) loop
  if message.message->>'schema'<>'hsr-public-task-job-v1' then
   perform pgmq.archive('hsr_public_task_jobs',message.msg_id);return jsonb_build_object('Status','InvalidMessage'); end if;
  select * into selected from hsr.public_tasks t where t.organization_id=(message.message->>'organizationId')::uuid and t.id=(message.message->>'jobId')::uuid for update;
  perform pgmq.archive('hsr_public_task_jobs',message.msg_id);
  if selected.id is null or selected.state<>'queued' then return jsonb_build_object('Status','Deferred'); end if;
  if not hsr.public_task_caller_alive(selected.created_by,selected.session_id,selected.organization_id) then
   update hsr.public_tasks set state='cancelled',error_code='PUBLIC_TASK_AUTHORITY_LOST',updated_at=clock_timestamp(),finished_at=clock_timestamp()
    where organization_id=selected.organization_id and id=selected.id;
   return jsonb_build_object('Status','AuthorityLost');
  end if;
  capability:=gen_random_uuid();
  update hsr.public_tasks set state='leased',phase='Starting',worker_id=p_worker_id,lease_token=capability,
   lease_expires_at=clock_timestamp()+interval '45 seconds',started_at=clock_timestamp(),updated_at=clock_timestamp()
   where organization_id=selected.organization_id and id=selected.id returning * into selected;
  return jsonb_build_object('Status','Claimed','Token',capability,'Job',hsr.public_task_job(selected));
 end loop;
 return jsonb_build_object('Status','Empty');
end $$;
revoke all on function hsr.claim_public_task(uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_public_task(uuid) to hsr_worker;

create function hsr.renew_public_task(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_phase text) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected hsr.public_tasks%rowtype;
begin
 if p_phase is null or p_phase not in('Starting','KeyboardAttempt1','KeyboardAttempt2','PointerComparison','Finalizing') then return false; end if;
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.id=p_job_id for update;
 if not found or selected.state<>'leased' or selected.worker_id is distinct from p_worker_id or selected.lease_token is distinct from p_token or selected.lease_expires_at<=clock_timestamp() then return false; end if;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=selected.created_by for key share;
 if not found or not hsr.public_task_caller_alive(selected.created_by,selected.session_id,p_organization_id) then return false; end if;
 update hsr.public_tasks set phase=p_phase,lease_expires_at=clock_timestamp()+interval '45 seconds',updated_at=clock_timestamp()
  where organization_id=p_organization_id and id=p_job_id;
 return true;
end $$;
revoke all on function hsr.renew_public_task(uuid,uuid,uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.renew_public_task(uuid,uuid,uuid,uuid,text) to hsr_worker;

create function hsr.finish_public_task(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_publication jsonb) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected hsr.public_tasks%rowtype;
begin
 select * into selected from hsr.public_tasks t where t.organization_id=p_organization_id and t.id=p_job_id for update;
 if not found or not hsr.public_task_caller_alive(selected.created_by,selected.session_id,p_organization_id) then return false; end if;
 if jsonb_typeof(p_publication) is distinct from 'object'
  or jsonb_typeof(p_publication->'Assessment'->'Conditions') is distinct from 'array' then return false; end if;
 if (select count(*) from jsonb_object_keys(p_publication))<>8
  or jsonb_array_length(p_publication->'Assessment'->'Conditions')<>8
  or exists(select 1 from jsonb_array_elements(p_publication->'Assessment'->'Conditions') c
    where jsonb_typeof(c) is distinct from 'object' or jsonb_typeof(c->'Satisfied') is distinct from 'boolean'
      or coalesce(c->>'Id','') not in('MeaningfulTask','RecordedBarrier','ExactTrigger','AccessibilityContext','RepeatedFailure','PointerComparison','ReproducibleEnvironment','OriginalArtifacts'))
  or (select count(distinct c->>'Id') from jsonb_array_elements(p_publication->'Assessment'->'Conditions') c)<>8
  or (p_publication->'Assessment'->>'Outcome'='WebsiteEvidenceReady' and exists(
    select 1 from jsonb_array_elements(p_publication->'Assessment'->'Conditions') c where c->'Satisfied'<>'true'::jsonb)) then return false; end if;
 if not coalesce(jsonb_typeof(p_publication)='object' and octet_length(p_publication::text)<=65536
  and p_publication->>'Schema'='hsr-public-task-publication-v1'
  and p_publication->>'ReportSha256' ~ '^[a-f0-9]{64}$' and jsonb_typeof(p_publication->'ReportBytes')='number'
  and case when p_publication->>'ReportBytes' ~ '^[0-9]{1,6}$' then (p_publication->>'ReportBytes')::bigint between 1 and 786432 else false end
  and p_publication->'NativeRemoved'='true'::jsonb and p_publication->'OriginalsVerified'='true'::jsonb
  and p_publication->>'ParentOriginalSha256'=selected.parent_original_sha256
  and p_publication->>'ExecutionId' ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'
  and p_publication->'Assessment'->>'Outcome' in('WebsiteEvidenceReady','NoBlockingBarrierInTestedTask','InsufficientEvidence'),false) then return false; end if;
 if selected.state='completed' then return selected.worker_id=p_worker_id and selected.publication=p_publication; end if;
 if not hsr.renew_public_task(p_organization_id,p_job_id,p_worker_id,p_token,'Finalizing') then return false; end if;
 update hsr.public_tasks set state='completed',phase='Finished',report_sha256=p_publication->>'ReportSha256',publication=p_publication,
  finished_at=clock_timestamp(),updated_at=clock_timestamp(),lease_token=null,error_code=null where organization_id=p_organization_id and id=p_job_id;
 return true;
end $$;
revoke all on function hsr.finish_public_task(uuid,uuid,uuid,uuid,jsonb) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.finish_public_task(uuid,uuid,uuid,uuid,jsonb) to hsr_worker;

create function hsr.block_public_task(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_code text) returns boolean
language plpgsql security definer set search_path='' as $$
begin
 if p_code is null or p_code not in('PUBLIC_TASK_EXECUTION_UNCONFIRMED','PUBLIC_TASK_AUTHORITY_LOST','PUBLIC_TASK_CLEANUP_UNCONFIRMED','PUBLIC_TASK_PUBLICATION_UNCONFIRMED') then return false; end if;
 update hsr.public_tasks set state='blocked',error_code=p_code,updated_at=clock_timestamp(),finished_at=clock_timestamp(),lease_token=null
 where organization_id=p_organization_id and id=p_job_id and worker_id=p_worker_id and lease_token=p_token and state='leased';
 return found;
end $$;
revoke all on function hsr.block_public_task(uuid,uuid,uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.block_public_task(uuid,uuid,uuid,uuid,text) to hsr_worker;
