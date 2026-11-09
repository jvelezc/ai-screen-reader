-- Disposable test database ONLY. The postgres image omits GoTrue's sessions table.
create table if not exists auth.sessions(id uuid primary key,user_id uuid not null references auth.users(id),not_after timestamptz);
grant select on auth.sessions to postgres;
do $setup$ begin if to_regprocedure('auth.jwt()') is null then
 execute $fn$create function auth.jwt() returns jsonb language sql stable as $body$
   select coalesce(nullif(current_setting('request.jwt.claims',true),''),'{}')::jsonb
 $body$$fn$;
end if;end $setup$;
