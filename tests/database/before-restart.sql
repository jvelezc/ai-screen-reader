insert into auth.users(id) values ('90000000-0000-0000-0000-000000000001');
insert into hsr.organizations(id,name) values ('90000000-0000-0000-0000-000000000002','Restart proof');
insert into hsr.memberships values ('90000000-0000-0000-0000-000000000002','90000000-0000-0000-0000-000000000001');
create schema hsr_test;
grant usage,create on schema hsr_test to authenticated;
set role authenticated;
set request.jwt.claim.sub = '90000000-0000-0000-0000-000000000001';
create table hsr_test.expected as select * from hsr.create_scan(
    '90000000-0000-0000-0000-000000000002','90000000-0000-0000-0000-000000000003');
reset role;
