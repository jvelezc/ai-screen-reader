-- Independent controlled demonstrations. These are not findings about a public page review.
select pgmq.create('hsr_search_task_jobs');
alter table pgmq.q_hsr_search_task_jobs enable row level security;
alter table pgmq.a_hsr_search_task_jobs enable row level security;
revoke all on pgmq.q_hsr_search_task_jobs,pgmq.a_hsr_search_task_jobs from public,anon,authenticated,hsr_worker;

create table hsr.search_tasks(
 organization_id uuid not null references hsr.organizations(id),id uuid not null default gen_random_uuid(),
 scan_id uuid not null default gen_random_uuid(),request_id uuid not null,created_by uuid not null references auth.users(id),session_id uuid not null,
 source_id text not null check(source_id in('search-working','search-keyboard-blocked')),
 state text not null default 'queued' check(state in('queued','leased','publishing','completed','blocked','cancelled','interrupted')),
 phase text not null default 'Queued' check(phase in('Queued','Starting','Collecting','Publishing','Finished')),
 created_at timestamptz not null default clock_timestamp(),updated_at timestamptz not null default clock_timestamp(),
 worker_id uuid,lease_token uuid,lease_expires_at timestamptz,started_at timestamptz,finished_at timestamptz,
 publication jsonb,pending_publication jsonb,publication_worker_id uuid,publication_attempts integer not null default 0 check(publication_attempts between 0 and 3),
 publication_started_at timestamptz,publication_retry_at timestamptz,error_code text check(error_code in('SEARCH_TASK_CANCELLED','SEARCH_TASK_LEASE_EXPIRED','SEARCH_TASK_AUTHORITY_LOST',
 'SEARCH_TASK_EXECUTION_UNCONFIRMED','SEARCH_TASK_CLEANUP_UNCONFIRMED','SEARCH_TASK_PUBLICATION_UNCONFIRMED','SEARCH_TASK_PUBLICATION_INVALID')),
 primary key(organization_id,id),unique(organization_id,scan_id),unique(organization_id,request_id),
 check(request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 check((state='completed')=(publication is not null)),check(publication is null or octet_length(publication::text)<=8192),check(pending_publication is null or octet_length(pending_publication::text)<=8192)
);
create index search_tasks_creator on hsr.search_tasks(created_by);
create index search_tasks_history on hsr.search_tasks(organization_id,created_at desc,id);
create index search_tasks_pending on hsr.search_tasks(organization_id,state,created_at,id) where state in('queued','leased','publishing');
create index search_tasks_expiring on hsr.search_tasks(lease_expires_at) where state in('leased','publishing');
alter table hsr.search_tasks enable row level security;
-- No direct client/worker table access: the scoped RPCs below are the complete access surface.
revoke all on hsr.search_tasks from public,anon,authenticated,hsr_worker;

create function hsr.search_task_job(p hsr.search_tasks) returns jsonb language sql immutable set search_path='' as $$
 select jsonb_build_object('OrganizationId',p.organization_id,'ScanId',p.scan_id,'JobId',p.id,'Generation',1,
 'Profile','hsr-embedded-search-task-v1','Source',jsonb_build_object('Id',p.source_id,'Sha256',case p.source_id
 when 'search-working' then 'b6de520a94841c634d567a30a3a5809a7d51f0dee43354c1e6f6297ef35a2f12'
 when 'search-keyboard-blocked' then 'e486f3e0e3fd694cd80fbad8ab6955e29ae19b41b52f98bafab7fd0af5b19d9f' end));
$$;
revoke all on function hsr.search_task_job(hsr.search_tasks) from public,anon,authenticated,hsr_worker;
create function hsr.search_task_metadata(p hsr.search_tasks) returns jsonb language sql stable set search_path='' as $$
 select jsonb_build_object('Schema','hsr-search-task-status-v1','Job',hsr.search_task_job(p),'RequestId',p.request_id,
 'CreatedBy',p.created_by,'CreatedAt',p.created_at,'UpdatedAt',p.updated_at,
 'State',case when p.state='leased' and p.lease_expires_at<=clock_timestamp() then 'interrupted'
 when p.state='publishing' and p.lease_expires_at<=clock_timestamp() then 'blocked' else p.state end,
 'Phase',p.phase,'FinishedAt',case when p.state in('leased','publishing') and p.lease_expires_at<=clock_timestamp() then p.lease_expires_at else p.finished_at end,
 'ProducerId',p.worker_id,'Publication',p.publication,
 'ErrorCode',case when p.state='leased' and p.lease_expires_at<=clock_timestamp() then 'SEARCH_TASK_LEASE_EXPIRED'
 when p.state='publishing' and p.lease_expires_at<=clock_timestamp() then 'SEARCH_TASK_PUBLICATION_UNCONFIRMED' else p.error_code end);
$$;
revoke all on function hsr.search_task_metadata(hsr.search_tasks) from public,anon,authenticated,hsr_worker;

create function hsr.create_search_task(p_organization_id uuid,p_request_id uuid,p_source_id text) returns jsonb
language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid(); sid uuid; selected hsr.search_tasks%rowtype;
begin
 begin sid:=(auth.jwt()->>'session_id')::uuid; exception when others then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=actor for key share;
 if not found or not hsr.public_task_caller_alive(actor,sid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_request_id is null or p_request_id='00000000-0000-0000-0000-000000000000'::uuid
  or p_source_id is null or p_source_id not in('search-working','search-keyboard-blocked') then
  raise exception using errcode='22023',message='HSR_SEARCH_TASK_REQUEST_INVALID'; end if;
 perform pg_advisory_xact_lock(hashtextextended('hsr-search-task-admission:'||p_organization_id::text,0));
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and request_id=p_request_id;
 if found then
  if selected.created_by<>actor or selected.source_id<>p_source_id then raise exception using errcode='22023',message='HSR_REQUEST_CONFLICT'; end if;
  return hsr.search_task_metadata(selected);
 end if;
 if (select count(*) from hsr.search_tasks where organization_id=p_organization_id and state in('queued','leased','publishing'))>=5 then
  raise exception using errcode='54000',message='HSR_SEARCH_TASK_QUEUE_FULL'; end if;
 insert into hsr.search_tasks(organization_id,request_id,created_by,session_id,source_id)
 values(p_organization_id,p_request_id,actor,sid,p_source_id) returning * into selected;
 perform pgmq.send('hsr_search_task_jobs',jsonb_build_object('schema','hsr-search-task-job-v1','organizationId',selected.organization_id,'jobId',selected.id));
 return hsr.search_task_metadata(selected);
end $$;
revoke all on function hsr.create_search_task(uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.create_search_task(uuid,uuid,text) to authenticated;

create function hsr.read_search_task(p_organization_id uuid,p_job_id uuid default null,p_request_id uuid default null) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and (p_job_id is null or id=p_job_id)
  and (p_request_id is null or request_id=p_request_id) order by created_at desc,id limit 1;
 if not found then return null; end if;
 return hsr.search_task_metadata(selected);
end $$;
revoke all on function hsr.read_search_task(uuid,uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.read_search_task(uuid,uuid,uuid) to authenticated;

create function hsr.cancel_search_task(p_organization_id uuid,p_job_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and id=p_job_id for update;
 if not found then return null; end if;
 if selected.state in('queued','leased','publishing') then
  update hsr.search_tasks set state='cancelled',error_code='SEARCH_TASK_CANCELLED',finished_at=clock_timestamp(),updated_at=clock_timestamp(),lease_token=null
   where organization_id=p_organization_id and id=p_job_id returning * into selected;
 end if;
 return hsr.search_task_metadata(selected);
end $$;
revoke all on function hsr.cancel_search_task(uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.cancel_search_task(uuid,uuid) to authenticated;

create function hsr.claim_search_task(p_worker_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare message record; selected hsr.search_tasks%rowtype; capability uuid;
begin
 if p_worker_id is null or p_worker_id='00000000-0000-0000-0000-000000000000'::uuid then raise exception using errcode='22023',message='HSR_WORKER_REQUIRED'; end if;
 perform pg_advisory_xact_lock(hashtextextended('hsr-search-task-worker',0));
 -- Expiry is terminal. The native journal separately requires exact owned cleanup before another execution.
 update hsr.search_tasks set state='interrupted',error_code='SEARCH_TASK_LEASE_EXPIRED',finished_at=clock_timestamp(),updated_at=clock_timestamp(),lease_token=null
  where state='leased' and lease_expires_at<=clock_timestamp();
 if exists(select 1 from hsr.search_tasks where state in('leased','publishing')) then return jsonb_build_object('Status','AtCapacity'); end if;
 for message in select * from pgmq.read('hsr_search_task_jobs',30,1) loop
  perform pgmq.archive('hsr_search_task_jobs',message.msg_id);
  if jsonb_typeof(message.message) is distinct from 'object' then return jsonb_build_object('Status','InvalidMessage'); end if;
  if (select count(*) from jsonb_object_keys(message.message))<>3 or message.message->>'schema' is distinct from 'hsr-search-task-job-v1'
   or coalesce(message.message->>'organizationId','') !~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'
   or coalesce(message.message->>'jobId','') !~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$' then
   return jsonb_build_object('Status','InvalidMessage'); end if;
  select * into selected from hsr.search_tasks where organization_id=(message.message->>'organizationId')::uuid and id=(message.message->>'jobId')::uuid for update;
  if selected.id is null or selected.state<>'queued' then return jsonb_build_object('Status','Deferred'); end if;
  if not hsr.public_task_caller_alive(selected.created_by,selected.session_id,selected.organization_id) then
   update hsr.search_tasks set state='cancelled',error_code='SEARCH_TASK_AUTHORITY_LOST',finished_at=clock_timestamp(),updated_at=clock_timestamp()
    where organization_id=selected.organization_id and id=selected.id;
   return jsonb_build_object('Status','AuthorityLost','RejectedJobId',selected.id);
  end if;
  capability:=gen_random_uuid();
  update hsr.search_tasks set state='leased',phase='Starting',worker_id=p_worker_id,lease_token=capability,
   lease_expires_at=clock_timestamp()+interval '45 seconds',started_at=clock_timestamp(),updated_at=clock_timestamp()
   where organization_id=selected.organization_id and id=selected.id returning * into selected;
  return jsonb_build_object('Status','Claimed','Token',capability,'Job',hsr.search_task_job(selected));
 end loop;
 return jsonb_build_object('Status','Empty');
end $$;
revoke all on function hsr.claim_search_task(uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_search_task(uuid) to hsr_worker;

create function hsr.renew_search_task(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_phase text) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype;
begin
 if p_phase is null or p_phase not in('Starting','Collecting','Publishing') then return false; end if;
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and id=p_job_id for update;
 if not found or selected.state<>'leased' or selected.worker_id is distinct from p_worker_id or selected.lease_token is distinct from p_token
  or p_token is null or selected.lease_expires_at<=clock_timestamp() or selected.started_at+interval '5 minutes'<=clock_timestamp() then return false; end if;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=selected.created_by for key share;
 if not found or not hsr.public_task_caller_alive(selected.created_by,selected.session_id,p_organization_id) then return false; end if;
 update hsr.search_tasks set phase=p_phase,lease_expires_at=clock_timestamp()+interval '45 seconds',updated_at=clock_timestamp()
  where organization_id=p_organization_id and id=p_job_id;
 return true;
end $$;
revoke all on function hsr.renew_search_task(uuid,uuid,uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.renew_search_task(uuid,uuid,uuid,uuid,text) to hsr_worker;

create function hsr.valid_search_task_publication(p_publication jsonb,p_verified boolean) returns boolean
language plpgsql immutable set search_path='' as $$
begin
 if jsonb_typeof(p_publication) is distinct from 'object' or p_verified is null then return false; end if;
 if (select count(*) from jsonb_object_keys(p_publication))<>16 then return false; end if;
 if not coalesce(octet_length(p_publication::text)<=8192 and p_publication->>'Schema'='hsr-search-task-publication-v1'
  and p_publication->>'Scope'='ControlledEmbeddedSearchTaskExperiments' and p_publication->>'WebsiteEvidenceAssessment'='NotAssessed'
  and p_publication->>'VideoAlignment'='UnverifiedWithoutCalibrationMarkers' and p_publication->'LegalCaseDetermined'='false'::jsonb
  and p_publication->'NativeRemoved'='true'::jsonb and p_publication->'OriginalsVerified'=to_jsonb(p_verified)
  and p_publication->>'ExecutionId' ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'
  and p_publication->>'ExecutionId'<>'00000000-0000-0000-0000-000000000000'
  and p_publication->>'IndexSha256' ~ '^[a-f0-9]{64}$'
  and p_publication->>'ManifestSha256' ~ '^[a-f0-9]{64}$' and p_publication->>'ArchiveSha256' ~ '^[a-f0-9]{64}$'
  and jsonb_typeof(p_publication->'ManifestBytes')='number' and case when p_publication->>'ManifestBytes' ~ '^[0-9]{1,6}$' then (p_publication->>'ManifestBytes')::bigint between 1 and 524288 else false end
  and jsonb_typeof(p_publication->'ArchiveBytes')='number' and case when p_publication->>'ArchiveBytes' ~ '^[0-9]{1,8}$' then (p_publication->>'ArchiveBytes')::bigint between 1 and 27262976 else false end
  and jsonb_typeof(p_publication->'Files')='number' and case when p_publication->>'Files' ~ '^[0-9]{1,4}$' then (p_publication->>'Files')::bigint between 28 and 1400 else false end
  and jsonb_typeof(p_publication->'Bytes')='number' and case when p_publication->>'Bytes' ~ '^[0-9]{1,8}$' then (p_publication->>'Bytes')::bigint between 1 and 26214400 else false end
  and case when p_publication->>'Bytes' ~ '^[0-9]{1,8}$' and p_publication->>'ArchiveBytes' ~ '^[0-9]{1,8}$'
   then (p_publication->>'Bytes')::bigint<=(p_publication->>'ArchiveBytes')::bigint else false end
  and p_publication->>'Observation' in('AllTestedKeyboardGoalsObserved','AKeyboardAlternativeReachedTheGoal','KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved'),false) then return false; end if;
 return true;
end $$;
revoke all on function hsr.valid_search_task_publication(jsonb,boolean) from public,anon,authenticated,hsr_worker;

create function hsr.prepare_search_task_publication(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_prepared jsonb) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype;
begin
 if not hsr.valid_search_task_publication(p_prepared,false) then return false; end if;
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and id=p_job_id for update;
 if not found or not hsr.public_task_caller_alive(selected.created_by,selected.session_id,p_organization_id) then return false; end if;
 if selected.state='publishing' then return coalesce(selected.publication_worker_id=p_worker_id and selected.lease_token=p_token
  and selected.lease_expires_at>clock_timestamp() and selected.pending_publication=p_prepared,false); end if;
 if not hsr.renew_search_task(p_organization_id,p_job_id,p_worker_id,p_token,'Publishing') then return false; end if;
 update hsr.search_tasks set state='publishing',phase='Publishing',pending_publication=p_prepared,publication_worker_id=p_worker_id,
  publication_attempts=1,publication_started_at=clock_timestamp(),publication_retry_at=null,lease_expires_at=clock_timestamp()+interval '45 seconds',updated_at=clock_timestamp()
  where organization_id=p_organization_id and id=p_job_id;
 return true;
end $$;
revoke all on function hsr.prepare_search_task_publication(uuid,uuid,uuid,uuid,jsonb) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.prepare_search_task_publication(uuid,uuid,uuid,uuid,jsonb) to hsr_worker;

create function hsr.renew_search_publication(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype;
begin
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and id=p_job_id for update;
 if not found or selected.state<>'publishing' or selected.publication_worker_id is distinct from p_worker_id or p_token is null
  or selected.lease_token is distinct from p_token or selected.lease_expires_at<=clock_timestamp()
  or selected.publication_started_at+interval '5 minutes'<=clock_timestamp() then return false; end if;
 perform 1 from hsr.memberships m where m.organization_id=p_organization_id and m.user_id=selected.created_by for key share;
 if not found or not hsr.public_task_caller_alive(selected.created_by,selected.session_id,p_organization_id) then return false; end if;
 update hsr.search_tasks set lease_expires_at=clock_timestamp()+interval '45 seconds',updated_at=clock_timestamp()
  where organization_id=p_organization_id and id=p_job_id;
 return true;
end $$;
revoke all on function hsr.renew_search_publication(uuid,uuid,uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.renew_search_publication(uuid,uuid,uuid,uuid) to hsr_worker;

create function hsr.claim_search_publication(p_worker_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype; capability uuid;
begin
 if p_worker_id is null or p_worker_id='00000000-0000-0000-0000-000000000000'::uuid then raise exception using errcode='22023',message='HSR_WORKER_REQUIRED'; end if;
 perform pg_advisory_xact_lock(hashtextextended('hsr-search-task-worker',0));
 update hsr.search_tasks set state='blocked',error_code='SEARCH_TASK_PUBLICATION_UNCONFIRMED',lease_token=null,
  finished_at=clock_timestamp(),updated_at=clock_timestamp(),publication_retry_at=clock_timestamp()+interval '30 seconds'
  where state='publishing' and lease_expires_at<=clock_timestamp();
 if exists(select 1 from hsr.search_tasks where state in('leased','publishing')) then return jsonb_build_object('Status','AtCapacity'); end if;
 select * into selected from hsr.search_tasks where state='blocked' and error_code='SEARCH_TASK_PUBLICATION_UNCONFIRMED'
  and pending_publication is not null and publication_attempts<3 and publication_retry_at<=clock_timestamp()
  order by publication_retry_at,created_at,id for update skip locked limit 1;
 if not found then return jsonb_build_object('Status','Empty'); end if;
 if not hsr.public_task_caller_alive(selected.created_by,selected.session_id,selected.organization_id) then
  update hsr.search_tasks set state='cancelled',error_code='SEARCH_TASK_AUTHORITY_LOST',publication_retry_at=null,
   finished_at=clock_timestamp(),updated_at=clock_timestamp() where organization_id=selected.organization_id and id=selected.id;
  return jsonb_build_object('Status','AuthorityLost','RejectedJobId',selected.id); end if;
 capability:=gen_random_uuid();
 update hsr.search_tasks set state='publishing',phase='Publishing',publication_worker_id=p_worker_id,publication_attempts=publication_attempts+1,
  publication_started_at=clock_timestamp(),publication_retry_at=null,lease_token=capability,lease_expires_at=clock_timestamp()+interval '45 seconds',
  updated_at=clock_timestamp(),finished_at=null,error_code=null where organization_id=selected.organization_id and id=selected.id returning * into selected;
 return jsonb_build_object('Status','PublicationOnly','Token',capability,'Job',hsr.search_task_job(selected),'ProducerId',selected.worker_id,'Prepared',selected.pending_publication);
end $$;
revoke all on function hsr.claim_search_publication(uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.claim_search_publication(uuid) to hsr_worker;

create function hsr.finish_search_task(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_publication jsonb) returns boolean
language plpgsql security definer set search_path='' as $$
declare selected hsr.search_tasks%rowtype;
begin
 if not hsr.valid_search_task_publication(p_publication,true) then return false; end if;
 select * into selected from hsr.search_tasks where organization_id=p_organization_id and id=p_job_id for update;
 if not found or not hsr.public_task_caller_alive(selected.created_by,selected.session_id,p_organization_id) then return false; end if;
 if selected.state='completed' then return coalesce(selected.publication_worker_id=p_worker_id and selected.publication=p_publication,false); end if;
 if selected.pending_publication is distinct from jsonb_set(p_publication,'{OriginalsVerified}','false')
  or not hsr.renew_search_publication(p_organization_id,p_job_id,p_worker_id,p_token) then return false; end if;
 update hsr.search_tasks set state='completed',phase='Finished',publication=p_publication,finished_at=clock_timestamp(),updated_at=clock_timestamp(),lease_token=null,error_code=null
  where organization_id=p_organization_id and id=p_job_id;
 return true;
end $$;
revoke all on function hsr.finish_search_task(uuid,uuid,uuid,uuid,jsonb) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.finish_search_task(uuid,uuid,uuid,uuid,jsonb) to hsr_worker;

create function hsr.block_search_task(p_organization_id uuid,p_job_id uuid,p_worker_id uuid,p_token uuid,p_code text) returns boolean
language plpgsql security definer set search_path='' as $$
begin
 if p_code is null or p_code not in('SEARCH_TASK_EXECUTION_UNCONFIRMED','SEARCH_TASK_AUTHORITY_LOST','SEARCH_TASK_CLEANUP_UNCONFIRMED','SEARCH_TASK_PUBLICATION_UNCONFIRMED','SEARCH_TASK_PUBLICATION_INVALID') then return false; end if;
 update hsr.search_tasks set state='blocked',error_code=p_code,finished_at=clock_timestamp(),updated_at=clock_timestamp(),lease_token=null,
  publication_retry_at=case when p_code='SEARCH_TASK_PUBLICATION_UNCONFIRMED' and pending_publication is not null and publication_attempts<3 then clock_timestamp()+interval '30 seconds' else null end
  where organization_id=p_organization_id and id=p_job_id and lease_token=p_token
  and ((state='leased' and worker_id=p_worker_id) or (state='publishing' and publication_worker_id=p_worker_id));
 return found;
end $$;
revoke all on function hsr.block_search_task(uuid,uuid,uuid,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.block_search_task(uuid,uuid,uuid,uuid,text) to hsr_worker;

-- Keep each storage-only failure after recovery. No session/lease token, page body or provider exception is logged.
create table hsr.search_task_publication_history(
 organization_id uuid not null,job_id uuid not null,number integer not null check(number between 1 and 3),
 producer_id uuid not null,publisher_id uuid not null,execution_id uuid not null,index_sha256 text not null check(index_sha256 ~ '^[a-f0-9]{64}$'),
 started_at timestamptz not null,finished_at timestamptz,state text not null check(state in('Publishing','Published','Blocked','Cancelled')),
 error_code text,primary key(organization_id,job_id,number),foreign key(organization_id,job_id) references hsr.search_tasks(organization_id,id)
);
alter table hsr.search_task_publication_history enable row level security;
revoke all on hsr.search_task_publication_history from public,anon,authenticated,hsr_worker;
create function hsr.record_search_task_publication_transition() returns trigger
language plpgsql security definer set search_path='' as $$
begin
 if new.state='publishing' and (old.state<>'publishing' or old.publication_attempts<>new.publication_attempts) then
  insert into hsr.search_task_publication_history(organization_id,job_id,number,producer_id,publisher_id,execution_id,index_sha256,started_at,state)
  values(new.organization_id,new.id,new.publication_attempts,new.worker_id,new.publication_worker_id,
   (new.pending_publication->>'ExecutionId')::uuid,new.pending_publication->>'IndexSha256',new.publication_started_at,'Publishing');
 elsif old.state='publishing' and new.state<>'publishing' then
  update hsr.search_task_publication_history set finished_at=clock_timestamp(),state=case new.state when 'completed' then 'Published' when 'cancelled' then 'Cancelled' else 'Blocked' end,
   error_code=case when old.lease_expires_at<=clock_timestamp() then 'SEARCH_TASK_PUBLICATION_LEASE_EXPIRED' else new.error_code end
   where organization_id=new.organization_id and job_id=new.id and number=old.publication_attempts and finished_at is null;
 end if;
 return new;
end $$;
revoke all on function hsr.record_search_task_publication_transition() from public,anon,authenticated,hsr_worker;
create trigger record_search_task_publication after update on hsr.search_tasks
 for each row execute function hsr.record_search_task_publication_transition();

create function hsr.read_search_task_diagnostics(p_organization_id uuid,p_job_id uuid) returns jsonb
language plpgsql security definer set search_path='' as $$
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id) then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if not exists(select 1 from hsr.search_tasks where organization_id=p_organization_id and id=p_job_id) then return null; end if;
 return jsonb_build_object('Schema','hsr-search-task-diagnostics-v1','JobId',p_job_id,'PublicationAttempts',coalesce((
  select jsonb_agg(jsonb_build_object('Number',number,'ProducerId',producer_id,'PublisherId',publisher_id,'ExecutionId',execution_id,
   'IndexSha256',index_sha256,'StartedAt',started_at,'FinishedAt',finished_at,'State',state,'ErrorCode',error_code,'WebsiteInputRetried',false) order by number)
  from hsr.search_task_publication_history where organization_id=p_organization_id and job_id=p_job_id),'[]'::jsonb));
end $$;
revoke all on function hsr.read_search_task_diagnostics(uuid,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.read_search_task_diagnostics(uuid,uuid) to authenticated;
