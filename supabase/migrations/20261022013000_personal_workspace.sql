-- Authenticated portal bootstrap. Identity comes only from the verified JWT subject.
-- The mapping is private; existing tenant RLS and fenced worker grants are unchanged.
create table hsr.personal_workspaces (
    user_id uuid primary key references auth.users(id),
    organization_id uuid not null unique references hsr.organizations(id)
);
alter table hsr.personal_workspaces enable row level security;
revoke all on hsr.personal_workspaces from public,anon,authenticated,hsr_worker;

create function hsr.ensure_personal_workspace()
returns table (organization_id uuid, created boolean)
language plpgsql security definer set search_path='' as $$
declare
    actor uuid := auth.uid();
    selected_organization uuid;
begin
    if actor is null then
        raise exception using errcode='42501',message='HSR_AUTHENTICATED_USER_REQUIRED';
    end if;
    -- Serialize only this subject's bootstrap and fence deletion of the real user.
    -- No client-supplied user/organization ID is accepted by this function.
    perform 1 from auth.users u where u.id=actor for update;
    if not found then
        raise exception using errcode='42501',message='HSR_AUTHENTICATED_USER_REQUIRED';
    end if;
    select p.organization_id into selected_organization
        from hsr.personal_workspaces p where p.user_id=actor;
    if found then
        -- Revocation remains effective: bootstrap must never silently restore it.
        perform 1 from hsr.memberships m
            where m.organization_id=selected_organization and m.user_id=actor for key share;
        if not found then
            raise exception using errcode='42501',message='HSR_WORKSPACE_MEMBERSHIP_REVOKED';
        end if;
        return query select selected_organization,false;
        return;
    end if;
    selected_organization := pg_catalog.gen_random_uuid();
    insert into hsr.organizations(id,name) values(selected_organization,'Personal workspace');
    insert into hsr.memberships(organization_id,user_id) values(selected_organization,actor);
    insert into hsr.personal_workspaces(user_id,organization_id) values(actor,selected_organization);
    return query select selected_organization,true;
end $$;
revoke all on function hsr.ensure_personal_workspace() from public,anon,authenticated,hsr_worker;
grant execute on function hsr.ensure_personal_workspace() to authenticated;
