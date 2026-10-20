#!/bin/sh
set -eu
case "${HSR_PROXY_IP:-}" in *[!0-9.]*|'') exit 2 ;; esac
# These rules affect only this task's private network namespace.
for command in iptables ip6tables; do
    "$command" -P INPUT DROP
    "$command" -P OUTPUT DROP
    "$command" -P FORWARD DROP
    "$command" -A INPUT -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
    "$command" -A OUTPUT -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
    "$command" -A INPUT -i lo -j ACCEPT
    "$command" -A OUTPUT -o lo -j ACCEPT
done
iptables -A OUTPUT -p tcp -d "$HSR_PROXY_IP/32" --dport 3128 -j ACCEPT
touch /tmp/fence-ready
# Bound abandoned infrastructure even if a client terminates its launcher abruptly.
exec timeout 1800 /bin/sh -c 'while timeout 2 /bin/bash -c "exec 3<>/dev/tcp/$HSR_PROXY_IP/3128" 2>/dev/null; do sleep 1; done'
