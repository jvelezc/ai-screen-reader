begin;
set search_path=extensions,public;
grant usage on schema extensions to authenticated;
select plan(8);
insert into auth.users(id) values
    ('8b000000-0000-0000-0000-000000000001'),
    ('8b000000-0000-0000-0000-000000000002'),
    ('8b000000-0000-0000-0000-000000000003');
insert into hsr.organizations(id,name) values
    ('8b000000-0000-0000-0000-000000000010','Catalog own first'),
    ('8b000000-0000-0000-0000-000000000020','Catalog foreign middle'),
    ('8b000000-0000-0000-0000-000000000030','Catalog own last');
insert into hsr.memberships values
    ('8b000000-0000-0000-0000-000000000010','8b000000-0000-0000-0000-000000000001'),
    ('8b000000-0000-0000-0000-000000000030','8b000000-0000-0000-0000-000000000001'),
    ('8b000000-0000-0000-0000-000000000020','8b000000-0000-0000-0000-000000000002');
set local request.jwt.claim.sub='8b000000-0000-0000-0000-000000000001';
set local role authenticated;
select is((select string_agg(id::text,',' order by id) from hsr.organizations),
    '8b000000-0000-0000-0000-000000000010,8b000000-0000-0000-0000-000000000030',
    'CatalogReturnsEveryOwnMembershipWithoutForeignNames');
select is((select string_agg(id::text,',' order by id) from hsr.organizations
    where id>'8b000000-0000-0000-0000-000000000010'),
    '8b000000-0000-0000-0000-000000000030','CatalogContinuationRetainsOriginalCallerRls');
select is((select string_agg(id::text,',' order by id) from hsr.organizations
    where id>'8b000000-0000-0000-0000-000000000020'),
    '8b000000-0000-0000-0000-000000000030','ForeignCursorCannotGrantForeignMembership');
set local request.jwt.claim.sub='8b000000-0000-0000-0000-000000000002';
select is((select string_agg(id::text,',' order by id) from hsr.organizations),
    '8b000000-0000-0000-0000-000000000020','CatalogReevaluatesTheCurrentCallerWithoutCachedNames');
reset role;
delete from hsr.memberships where organization_id='8b000000-0000-0000-0000-000000000030'
    and user_id='8b000000-0000-0000-0000-000000000001';
set local request.jwt.claim.sub='8b000000-0000-0000-0000-000000000001';
set local role authenticated;
select is((select string_agg(id::text,',' order by id) from hsr.organizations),
    '8b000000-0000-0000-0000-000000000010','RevocationRemovesOnlyTheIsolatedCatalogMembership');
select is((select count(*) from hsr.organizations where id>'8b000000-0000-0000-0000-000000000010'),
    0::bigint,'NextCatalogPageCannotRestoreRevokedMembership');
select is((select count(*) from hsr.organizations where id='8b000000-0000-0000-0000-000000000030'),
    0::bigint,'SelectingRevokedWorkspaceCannotBypassTheSameRls');
set local request.jwt.claim.sub='8b000000-0000-0000-0000-000000000003';
select is((select count(*) from hsr.organizations),0::bigint,'NonmemberCatalogRemainsEmptyWithoutPersonalBootstrap');
reset role;
select * from finish();
rollback;
