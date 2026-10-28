begin;
set search_path=extensions,public;
grant usage on schema extensions to hsr_worker,authenticated,anon;
select plan(27);
insert into auth.users(id) values ('74000000-0000-0000-0000-000000000001'),('74000000-0000-0000-0000-000000000002');
insert into hsr.organizations(id,name) values ('74000000-0000-0000-0000-000000000003','Producer scope');
insert into hsr.memberships values ('74000000-0000-0000-0000-000000000003','74000000-0000-0000-0000-000000000001');
select set_config('request.jwt.claim.sub','74000000-0000-0000-0000-000000000001',true);
create temporary table admitted as select * from hsr.create_scan('74000000-0000-0000-0000-000000000003','74000000-0000-0000-0000-000000000004');
set local role hsr_worker;
create temporary table claimed as select * from hsr.claim_scan_job('74000000-0000-0000-0000-000000000005',60);
reset role;
grant select on claimed to hsr_worker;
create function pg_temp.finish(p_worker uuid default '74000000-0000-0000-0000-000000000005',
    p_generation bigint default 1,p_token uuid default null,p_index text default repeat('b',64),
    p_manifest text default repeat('c',64),p_bytes bigint default 800,p_org uuid default null)
returns boolean language sql as $$
    select hsr.complete_scan_job_with_archive(coalesce(p_org,c.organization_id),c.job_id,p_worker,p_generation,
        coalesce(p_token,c.lease_token),repeat('a',64),replace(c.organization_id::text,'-','')||'/'||replace(c.scan_id::text,'-','')||'/'||
        replace(c.job_id::text,'-','')||'/'||p_generation::text||'/observation.json',p_index,p_manifest,p_bytes) from claimed c
$$;
set local role anon;
select throws_ok($$select hsr.complete_scan_job_with_archive(null,null,null,null,null,null,null,null,null,null)$$,'42501',null,'AnonymousCannotPublishProducerReceipt');
set local role authenticated;
select throws_ok($$select hsr.complete_scan_job_with_archive(null,null,null,null,null,null,null,null,null,null)$$,'42501',null,'AuthenticatedCannotPublishProducerReceipt');
select throws_ok($$update hsr.scan_jobs set archive_index_sha256=repeat('b',64)$$,'42501',null,'MemberCannotWriteProducerColumns');
set local role hsr_worker;
select throws_ok($$select pg_temp.finish(p_index:=repeat('B',64))$$,'22023','INVALID_PRODUCER_ARCHIVE','ProducerRejectsNoncanonicalIndex');
select throws_ok($$select pg_temp.finish(p_manifest:=null)$$,'22023','INVALID_PRODUCER_ARCHIVE','ProducerRejectsMissingManifest');
select throws_ok($$select pg_temp.finish(p_bytes:=1052673)$$,'22023','INVALID_PRODUCER_ARCHIVE','ProducerRejectsOversizedObservationPackage');
select is(pg_temp.finish(p_worker:='74000000-0000-0000-0000-000000000099'),false,'ProducerRequiresActualClaimedWorker');
select is(pg_temp.finish(p_token:='74000000-0000-0000-0000-000000000099'),false,'ProducerRequiresActualLeaseCapability');
select is(pg_temp.finish(p_generation:=2),false,'ProducerRequiresExactAttemptGeneration');
select is(pg_temp.finish(p_org:='74000000-0000-0000-0000-000000000099'),false,'ProducerCannotPublishToDifferentTenant');
reset role;
select ok((select state='leased' and archive_index_sha256 is null from hsr.scan_jobs where id=(select job_id from admitted))
    and (select count(*)=0 from hsr.attempt_events where kind='Completed'),'RejectedProducerCannotChangeJobOrCompletionEvent');
update hsr.job_leases set expires_at=clock_timestamp()-interval '1 second';
set local role hsr_worker;
select is(pg_temp.finish(),false,'ExpiredProducerAttemptCannotPublish');
reset role;
select ok((select state='leased' and archive_index_sha256 is null from hsr.scan_jobs where id=(select job_id from admitted)),
    'ExpiredProducerRetainsUnconfirmedJobWithoutReceipt');
update hsr.job_leases set expires_at=clock_timestamp()+interval '60 seconds';
create function pg_temp.reject_archive_write() returns trigger language plpgsql as $$
begin
    if new.archive_index_sha256 is not null then raise exception 'ARCHIVE_WRITE_INJECTED';end if;
    return new;
end $$;
create trigger producer_write_failure before update on hsr.scan_jobs for each row execute function pg_temp.reject_archive_write();
set local role hsr_worker;
select throws_ok($$select pg_temp.finish()$$,'P0001','ARCHIVE_WRITE_INJECTED','ProducerWriteFailureRollsBackActualCompletion');
reset role;
select ok((select state='leased' and result_digest is null and archive_index_sha256 is null from hsr.scan_jobs where id=(select job_id from admitted))
    and (select count(*)=1 from pgmq.q_hsr_scan_jobs) and (select count(*)=1 from hsr.job_leases)
    and (select count(*)=0 from hsr.attempt_events where kind='Completed'),'FailedProducerWritePreservesQueueLeaseAndNoCompletionEvent');
drop trigger producer_write_failure on hsr.scan_jobs;
set local role hsr_worker;
select is(pg_temp.finish(),true,'FencedCompletionPublishesProducerReceipt');
reset role;
select ok((select archive_index_sha256=repeat('b',64) and archive_manifest_sha256=repeat('c',64) and archive_bytes=800
    and archive_worker_id='74000000-0000-0000-0000-000000000005'::uuid and state='completed'
    from hsr.scan_jobs where id=(select job_id from admitted)),'ProducerIdentityMatchesActualCompletedAttempt');
select ok((select count(*)=0 from pgmq.q_hsr_scan_jobs) and (select count(*)=0 from hsr.job_leases)
    and (select count(*)=1 from hsr.attempt_events where kind='Completed'),'ProducerReceiptAndQueueAckCommitAtomically');
set local role hsr_worker;
select is(pg_temp.finish(),true,'ExactProducerRetryIsIdempotent');
select is(pg_temp.finish(p_index:=repeat('d',64)),false,'CompletedProducerIndexCannotBeReplaced');
select is(pg_temp.finish(p_bytes:=801),false,'CompletedProducerInventoryCannotBeReplaced');
select is(pg_temp.finish(p_token:='74000000-0000-0000-0000-000000000099'),false,'ProducerRetryStillRequiresOriginalCapability');
reset role;
set local role authenticated;
select is((select count(*) from hsr.scan_jobs where archive_index_sha256=repeat('b',64)),1::bigint,'MemberCanRediscoverProducerReceiptUnderRls');
select set_config('request.jwt.claim.sub','74000000-0000-0000-0000-000000000002',true);
select is((select count(*) from hsr.scan_jobs),0::bigint,'PeerCannotDiscoverProducerReceipt');
reset role;
delete from hsr.memberships where user_id='74000000-0000-0000-0000-000000000001';
select set_config('request.jwt.claim.sub','74000000-0000-0000-0000-000000000001',true);
set local role authenticated;
select is((select count(*) from hsr.scan_jobs),0::bigint,'RevokedMemberCannotRediscoverProducerReceipt');
reset role;
-- Simulate the old completion path, without backfilling provenance. An original
-- token and valid observation cannot attach a new archive after completion.
update hsr.scan_jobs set archive_index_sha256=null,archive_manifest_sha256=null,archive_bytes=null,archive_worker_id=null;
set local role hsr_worker;
select is(pg_temp.finish(),false,'LegacyCompletionCannotAcquireRetrospectiveProducerReceipt');
reset role;
select ok((select archive_index_sha256 is null and archive_manifest_sha256 is null from hsr.scan_jobs
    where id=(select job_id from admitted)),'LegacyObservationRemainsExplicitlyWithoutProducerArchive');
select * from finish();
rollback;
