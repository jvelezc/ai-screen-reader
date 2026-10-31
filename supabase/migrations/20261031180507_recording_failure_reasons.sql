-- Only closed recording diagnostics may accompany an original worker's fenced block.
create or replace function hsr.block_scan_job(p_org uuid,p_job uuid,p_worker uuid,p_generation bigint,p_token uuid,p_code text)
returns boolean language plpgsql security definer set search_path='' as $$
declare v_job hsr.scan_jobs%rowtype;
begin
    if p_code is null or p_code not in (
        'FIXTURE_OBSERVATION_UNCONFIRMED','OWNED_BROWSER_CLOSE_UNCONFIRMED','WORKER_STOP_REQUESTED',
        'WORKER_PROGRESS_RECORDING_CAPTURE_BUDGET_EXCEEDED','WORKER_PROGRESS_RECORDING_CAPTURE_FRAME_INVALID',
        'WORKER_PROGRESS_RECORDING_CAPTURE_WRITE_FAILED','WORKER_PROGRESS_RECORDING_FAILED',
        'WORKER_PROGRESS_RECORDING_CANCELLED','WORKER_PROGRESS_RECORDING_SOURCE_METADATA_MISSING',
        'WORKER_PROGRESS_RECORDING_SOURCE_HASH_CHANGED','WORKER_PROGRESS_RECORDING_SOURCE_ARTIFACT_MISSING',
        'WORKER_PROGRESS_RECORDING_ARTIFACT_UNRECOGNIZED','WORKER_PROGRESS_RECORDING_ORIGINAL_CHANGED',
        'WORKER_PROGRESS_RECORDING_UNCONFIRMED','WORKER_PROGRESS_RECORDING_ACK_REJECTED',
        'WORKER_PROGRESS_RECORDING_SOURCE_EXPORT_UNCONFIRMED') then
        raise exception using errcode='22023',message='INVALID_WORKER_FAILURE_CODE';
    end if;
    select j.* into v_job from hsr._active_job(p_org,p_job,p_worker,p_generation,p_token) j;
    if not found then return false;end if;
    perform hsr._block_job(v_job,p_code);
    return true;
end $$;
revoke all on function hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text) from public,anon,authenticated,hsr_worker;
grant execute on function hsr.block_scan_job(uuid,uuid,uuid,bigint,uuid,text) to hsr_worker;
