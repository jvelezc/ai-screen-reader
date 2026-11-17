-- Metadata only. Existing original/result resources retain their independent authorization.
create index search_tasks_caller_history on hsr.search_tasks
 (organization_id,created_by,source_id,created_at desc,id);

create function hsr.read_search_task_history(p_organization_id uuid,p_source_id text,p_before_job_id uuid default null)
returns jsonb language plpgsql security definer set search_path='' as $$
declare cursor_row hsr.search_tasks%rowtype; selected_ids uuid[]; items jsonb; next_id uuid;
begin
 if not hsr.public_task_caller_alive(auth.uid(),(auth.jwt()->>'session_id')::uuid,p_organization_id)
 then raise exception using errcode='42501',message='HSR_SESSION_REQUIRED'; end if;
 if p_source_id is null or p_source_id not in ('search-working','search-keyboard-blocked')
 then raise exception using errcode='22023',message='HSR_REQUEST_INVALID'; end if;
 if p_before_job_id is not null then
  select * into cursor_row from hsr.search_tasks where organization_id=p_organization_id
   and created_by=auth.uid() and source_id=p_source_id and id=p_before_job_id;
  if not found then raise exception using errcode='42501',message='HSR_READ_DENIED'; end if;
 end if;
 select array_agg(p.id order by p.created_at desc,p.id) into selected_ids from
  (select id,created_at from hsr.search_tasks where organization_id=p_organization_id and created_by=auth.uid()
   and source_id=p_source_id and (p_before_job_id is null or created_at<cursor_row.created_at
    or created_at=cursor_row.created_at and id>cursor_row.id)
   order by created_at desc,id limit 11) p;
 if cardinality(selected_ids)>10 then next_id:=selected_ids[10]; end if;
 selected_ids:=selected_ids[1:10];
 select coalesce(jsonb_agg(hsr.search_task_metadata(p) order by p.created_at desc,p.id),'[]'::jsonb)
 into items from hsr.search_tasks p where p.organization_id=p_organization_id and p.id=any(selected_ids);
 return jsonb_build_object('Schema','hsr-search-task-history-v1','OrganizationId',p_organization_id,
  'SourceId',p_source_id,'Items',items,'NextBeforeJobId',next_id,'WebsiteInputStarted',false,'IsAssessment',false);
end $$;
revoke all on function hsr.read_search_task_history(uuid,text,uuid) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.read_search_task_history(uuid,text,uuid) to authenticated;
