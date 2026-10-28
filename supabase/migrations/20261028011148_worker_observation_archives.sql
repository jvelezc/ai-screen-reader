-- Producer receipts extend the existing tenant-scoped, RLS-protected job result.
-- Historical jobs and operator imports are deliberately not backfilled.
alter table hsr.scan_jobs
    add column archive_index_sha256 text,
    add column archive_manifest_sha256 text,
    add column archive_bytes bigint,
    add column archive_worker_id uuid,
    add constraint producer_archive_complete check (
        num_nonnulls(archive_index_sha256,archive_manifest_sha256,archive_bytes,archive_worker_id)=0
        or (num_nonnulls(archive_index_sha256,archive_manifest_sha256,archive_bytes,archive_worker_id)=4
            and state='completed' and archive_index_sha256 ~ '^[a-f0-9]{64}$'
            and length(archive_index_sha256)=64 and archive_manifest_sha256 ~ '^[a-f0-9]{64}$'
            and length(archive_manifest_sha256)=64 and archive_bytes between 1 and 1052672
            and archive_worker_id <> '00000000-0000-0000-0000-000000000000'::uuid));

-- Private worker capability only. Existing complete_scan_job performs the actual
-- lease/worker/generation checks, transactional pgmq ack and attempt event.
create function hsr.complete_scan_job_with_archive(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,
    p_token uuid,p_digest text,p_reference text,p_index text,p_manifest text,p_bytes bigint)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;
begin
    if p_index is null or length(p_index)<>64 or p_index !~ '^[a-f0-9]{64}$'
        or p_manifest is null or length(p_manifest)<>64 or p_manifest !~ '^[a-f0-9]{64}$'
        or p_bytes is null or p_bytes not between 1 and 1052672 then
        raise exception using errcode='22023',message='INVALID_PRODUCER_ARCHIVE';
    end if;
    select j.* into v_job from hsr.scan_jobs j where j.organization_id=p_org and j.id=p_job for update;
    if not found then return false;end if;
    if v_job.state='completed' then
        -- Exact idempotent retry only. An older completed job cannot acquire a
        -- retrospective producer receipt, even with its original capability.
        return v_job.archive_index_sha256 is not distinct from p_index
            and v_job.archive_manifest_sha256 is not distinct from p_manifest
            and v_job.archive_bytes is not distinct from p_bytes
            and v_job.archive_worker_id is not distinct from p_worker
            and hsr.complete_scan_job(p_org,p_job,p_worker,p_generation,p_token,p_digest,p_reference);
    end if;
    if not hsr.complete_scan_job(p_org,p_job,p_worker,p_generation,p_token,p_digest,p_reference) then return false;end if;
    update hsr.scan_jobs set archive_index_sha256=p_index,archive_manifest_sha256=p_manifest,
        archive_bytes=p_bytes,archive_worker_id=p_worker where organization_id=p_org and id=p_job;
    return true;
end $$;
revoke all on function hsr.complete_scan_job_with_archive(uuid,uuid,uuid,bigint,uuid,text,text,text,text,bigint)
    from public,anon,authenticated,hsr_worker;
grant execute on function hsr.complete_scan_job_with_archive(uuid,uuid,uuid,bigint,uuid,text,text,text,text,bigint) to hsr_worker;
