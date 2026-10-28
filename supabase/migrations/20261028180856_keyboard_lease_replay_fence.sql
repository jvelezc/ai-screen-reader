-- Preserve uncertain input after an expired authored-keyboard lease.
create or replace function hsr.claim_scan_job(p_worker uuid,p_ttl integer default 30)
returns table(status text,organization_id uuid,scan_id uuid,job_id uuid,generation bigint,
    lease_token uuid,lease_until timestamptz,error_code text)
language plpgsql security definer set search_path='' as $$
declare
    v_message pgmq.message_record;
    v_job hsr.scan_jobs%rowtype;
    v_lease hsr.job_leases%rowtype;
    v_now timestamptz;
    v_deadline timestamptz;
    v_token uuid;
    v_expiry timestamptz;
begin
    if p_worker is null or p_ttl is null or p_ttl<5 or p_ttl>60 then
        raise exception using errcode='22023',message='INVALID_WORKER_LEASE_REQUEST';
    end if;
    select m.* into v_message from pgmq.read('hsr_scan_jobs',p_ttl,1) m;
    if not found then status:='Empty';return next;return;end if;
    -- Queue read locks queue first. Never wait on the opposite job-first order
    -- used by completion/renewal: defer a busy job instead of forming a cycle.
    select j.* into v_job from hsr.scan_jobs j where j.queue_message_id=v_message.msg_id
        for update skip locked;
    if not found then
        if exists(select 1 from hsr.scan_jobs j where j.queue_message_id=v_message.msg_id) then
            status:='Deferred';return next;return;
        end if;
        perform hsr._archive_message(v_message.msg_id);
        insert into hsr.queue_failures(message_id,reason) values(v_message.msg_id,'UNBOUND_QUEUE_MESSAGE');
        status:='Rejected';error_code:='UNBOUND_QUEUE_MESSAGE';return next;return;
    end if;
    organization_id:=v_job.organization_id;scan_id:=v_job.scan_id;job_id:=v_job.id;
    generation:=v_job.generation;
    if v_message.message is distinct from (jsonb_build_object('schema','hsr-stage-job-v1',
        'organizationId',v_job.organization_id,'scanId',v_job.scan_id,'jobId',v_job.id,'stage','observe-fixture')
        || case when v_job.record_observation then jsonb_build_object('recordObservation',true) else '{}'::jsonb end
        || case when v_job.traversal_route>0 then jsonb_build_object('traversalRoute',v_job.traversal_route) else '{}'::jsonb end) then
        insert into hsr.queue_failures(message_id,reason) values(v_message.msg_id,'QUEUE_BINDING_UNCONFIRMED');
        perform hsr._block_job(v_job,'QUEUE_BINDING_UNCONFIRMED');
        status:='Rejected';error_code:='QUEUE_BINDING_UNCONFIRMED';return next;return;
    end if;
    -- Passive-only and legacy workers never obtain a traversal capability.
    if v_job.traversal_route>0 and current_setting('hsr.traversal_claim',true) is distinct from 'on' then
        perform pgmq.set_vt('hsr_scan_jobs',v_message.msg_id,2);
        if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
        status:='Deferred';return next;return;
    end if;
    -- Legacy workers never obtain a capability for a recording request.
    if v_job.record_observation and current_setting('hsr.recording_claim',true) is distinct from 'on' then
        perform pgmq.set_vt('hsr_scan_jobs',v_message.msg_id,2);
        if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
        status:='Deferred';return next;return;
    end if;
    if v_job.state in ('completed','blocked') then
        perform hsr._archive_message(v_message.msg_id);
        status:='Deduplicated';return next;return;
    end if;
    v_now:=clock_timestamp();
    v_deadline:=coalesce(v_job.first_claimed_at,v_now)+interval '10 minutes';
    select l.* into v_lease from hsr.job_leases l
        where l.organization_id=v_job.organization_id and l.job_id=v_job.id;
    if v_lease.expires_at>v_now and v_now<v_deadline then
        perform pgmq.set_vt('hsr_scan_jobs',v_message.msg_id,
            greatest(1,ceil(extract(epoch from v_lease.expires_at-v_now))::integer));
        if not found then raise exception 'QUEUE_VISIBILITY_UNCONFIRMED';end if;
        status:='Deferred';return next;return;
    end if;
    if v_job.state='leased' then
        insert into hsr.attempt_events(organization_id,job_id,generation,kind,error_code)
            values(v_job.organization_id,v_job.id,v_job.generation,'Expired','WORKER_LEASE_EXPIRED');
    end if;
    -- An expired keyboard capability cannot establish whether input completed.
    -- Never allocate a replacement attempt that silently replays those keys.
    -- Safe zero-input observation/passive jobs retain their bounded retry path.
    if v_job.state='leased' and v_job.traversal_route>0 then
        error_code:='WORKER_INPUT_OUTCOME_UNCONFIRMED';
        perform hsr._block_job(v_job,error_code);
        status:='Blocked';return next;return;
    end if;
    if v_now>=v_deadline or v_job.generation>=3 then
        error_code:=case when v_now>=v_deadline then 'WORKER_BUDGET_EXHAUSTED' else 'WORKER_ATTEMPTS_EXHAUSTED' end;
        perform hsr._block_job(v_job,error_code);
        status:='Blocked';return next;return;
    end if;
    v_token:=gen_random_uuid();v_expiry:=least(v_now+make_interval(secs=>p_ttl),v_deadline);
    update hsr.scan_jobs j set state='leased',generation=v_job.generation+1,
        first_claimed_at=coalesce(j.first_claimed_at,v_now) where j.organization_id=v_job.organization_id and j.id=v_job.id;
    update hsr.scans s set state='leased' where s.organization_id=v_job.organization_id and s.id=v_job.scan_id;
    delete from hsr.job_leases l where l.organization_id=v_job.organization_id and l.job_id=v_job.id;
    insert into hsr.job_leases values(v_job.organization_id,v_job.id,v_job.generation+1,p_worker,v_token,v_expiry);
    insert into hsr.job_attempts values(v_job.organization_id,v_job.id,v_job.generation+1,p_worker,
        sha256(convert_to(v_token::text,'UTF8')),v_now,v_expiry);
    generation:=v_job.generation+1;lease_token:=v_token;lease_until:=v_expiry;status:='Claimed';return next;
end $$;
