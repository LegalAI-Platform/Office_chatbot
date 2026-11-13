#!/bin/bash
# The monitoring (v1.107, docs/runbooks/monitoring.md): Prometheus, Alertmanager, Loki, Alloy, Grafana and the exporters,
# profile "monitoring". Once started, deploy/deploy.sh keeps it on each new version (like the edge).
#   monitoring.sh up            start it, or bring it to this version's configs (they are recreated)
#   monitoring.sh down          stop it (its data stays: 30 days of numbers, 7 days of logs)
#   monitoring.sh status        its containers, and Prometheus' targets that do not answer
#   monitoring.sh test-email    a test alert: its e-mail within a minute, and «اتحلت» about 5 minutes later
#   monitoring.sh check         the configs, the alert rules' tests and the masking test (Docker only; the CI's job)
#   monitoring.sh grafana-password   Grafana's admin password set again from GRAFANA_ADMIN_PASSWORD in .env
source "$(dirname "$0")/_common.sh"

SERVICES=(monitoring-init prometheus alertmanager loki alloy docker-proxy node-exporter telegraf
          postgres-exporter chatwoot-db-exporter redis-exporter chatwoot-redis-exporter blackbox grafana)
# The ones that read a config file of this repo: compose does not see a changed file, so `up` recreates them.
CONFIGURED=(prometheus alertmanager loki alloy telegraf blackbox grafana)

image_of() { docker compose --profile monitoring config --format json 2>/dev/null | jq -r --arg s "$1" '.services[$s].image // empty'; }

case "${1:-}" in
  up)
    require_env_file
    docker compose --profile monitoring up -d --wait "${SERVICES[@]}"
    docker compose --profile monitoring up -d --wait --no-deps --force-recreate "${CONFIGURED[@]}"
    echo
    port="$(env_value GRAFANA_PORT)"
    echo "The monitoring is up. Grafana: http://127.0.0.1:${port:-3300} (from the server: an SSH tunnel)."
    ;;
  down)
    require_env_file
    docker compose --profile monitoring stop "${SERVICES[@]}"
    docker compose --profile monitoring rm -f "${SERVICES[@]}" >/dev/null
    echo "The monitoring is stopped; its data is kept (monitoring.sh up starts it again)."
    ;;
  status)
    require_env_file
    docker compose --profile monitoring ps -a --format 'table {{.Service}}\t{{.Status}}\t{{.Ports}}' "${SERVICES[@]}"
    echo
    echo "Targets that do not answer (none is good):"
    docker compose --profile monitoring exec -T prometheus wget -qO- 'http://127.0.0.1:9090/api/v1/query?query=up%3D%3D0' \
      | grep -oE '"job":"[^"]+"|"instance":"[^"]+"' | paste -d' ' - - || true
    ;;
  test-email)
    require_env_file
    # critical, so it goes out after 30 s (its route's group_wait); it ends by itself after 2 minutes, then «اتحلت»
    # (each value in double quotes: amtool's UTF-8 parser)
    docker compose --profile monitoring exec -T alertmanager amtool alert add 'alertname="MonitoringTestEmail"' \
      'severity="critical"' 'service="monitoring"' \
      --annotation='summary="تجربة: إيميلات المراقبة شغالة"' \
      --annotation='description="ده تنبيه تجربة من monitoring.sh test-email. لو وصلك، الإيميلات شغالة. هيتحل لوحده بعد دقيقتين."' \
      --annotation='runbook_url="https://github.com/LegalAI-Platform/Office_chatbot/blob/main/docs/runbooks/monitoring.md#test-email"' \
      --end="$(date -u -d '+2 minutes' '+%Y-%m-%dT%H:%M:%SZ')" \
      --alertmanager.url=http://127.0.0.1:9093
    echo "Sent to Alertmanager: the e-mail goes to ALERT_EMAIL_TO within a minute, «اتحلت» a few minutes later."
    ;;
  check)
    # Each tool runs from its own pinned image (the one docker-compose.yml names), as in the CI.
    root="$(pwd -W 2>/dev/null || pwd)" # Git Bash: the Windows path that Docker Desktop understands
    prom="$(image_of prometheus)"; am="$(image_of alertmanager)"; loki="$(image_of loki)"; alloy="$(image_of alloy)"
    tele="$(image_of telegraf)"; bb="$(image_of blackbox)"
    for img in "$prom" "$am" "$loki" "$alloy" "$tele" "$bb"; do [[ -n "$img" ]] || die "no image found in docker-compose.yml"; done
    echo "== Prometheus: the config and the rules"
    docker run --rm -v "$root/infra/monitoring/prometheus:/etc/prometheus:ro" --entrypoint promtool "$prom" \
      check config /etc/prometheus/prometheus.yml
    echo "== Prometheus: the alert rules' tests"
    docker run --rm -v "$root/infra/monitoring/prometheus:/etc/prometheus:ro" -w /etc/prometheus/tests --entrypoint promtool "$prom" \
      test rules alerts.yml
    echo "== Alertmanager: the config as start.sh fills it (made-up values)"
    docker run --rm -v "$root/infra/monitoring/alertmanager:/etc/alertmanager:ro" --entrypoint /bin/sh "$am" -c '
      sed -e "s|__ALERT_EMAIL_TO__|alerts@example.com|g" -e "s|__SMTP_HOST__|smtp.example.com|g" -e "s|__SMTP_PORT__|587|g" \
          -e "s|__SMTP_FROM__|no-reply@example.com|g" -e "s|__SMTP_USERNAME__|user|g" -e "s|__SMTP_REQUIRE_TLS__|true|g" \
          /etc/alertmanager/alertmanager.yml > /tmp/am.yml && touch /tmp/smtp_password && amtool check-config /tmp/am.yml'
    echo "== Loki"
    docker run --rm -v "$root/infra/monitoring/loki/loki.yml:/etc/loki/loki.yml:ro" "$loki" \
      -config.file=/etc/loki/loki.yml -verify-config -log.level=info
    echo "== Alloy: the pipeline, then the masking test"
    docker run --rm -e COMPOSE_PROJECT_NAME=office -v "$root/infra/monitoring/alloy:/etc/alloy:ro" "$alloy" validate /etc/alloy/config.alloy
    bash infra/monitoring/alloy/test/run.sh
    echo "== Telegraf"
    docker run --rm -e COMPOSE_PROJECT_NAME=office -v "$root/infra/monitoring/telegraf/telegraf.conf:/etc/telegraf/telegraf.conf:ro" \
      "$tele" telegraf config check --config /etc/telegraf/telegraf.conf
    echo "== Blackbox"
    docker run --rm -v "$root/infra/monitoring/blackbox:/etc/blackbox:ro" "$bb" --config.file=/etc/blackbox/blackbox.yml --config.check
    echo "== Grafana: the dashboards are JSON with a unique uid each, on the provisioned data sources only"
    for f in infra/monitoring/grafana/dashboards/*.json; do
      jq -e '.uid and .title and (.panels | length > 0)' "$f" >/dev/null || die "$f is not a dashboard"
    done
    [[ "$(jq -r .uid infra/monitoring/grafana/dashboards/*.json | sort | uniq -d)" == "" ]] || die "two dashboards share a uid"
    if jq -r '.. | objects | select(has("datasource")) | .datasource | objects | .uid' infra/monitoring/grafana/dashboards/*.json \
         | grep -vxE 'prometheus|loki' | grep -q .; then die "a dashboard uses a data source that is not provisioned"; fi
    echo "OK: $(ls infra/monitoring/grafana/dashboards/*.json | wc -l) dashboards"
    echo
    echo "The monitoring's checks passed."
    ;;
  grafana-password)
    require_env_file
    # from the container's own environment (GRAFANA_ADMIN_PASSWORD, from .env), never on a command line
    docker compose --profile monitoring exec -T grafana sh -c \
      'printf "%s" "$GF_SECURITY_ADMIN_PASSWORD" | grafana cli --homepath /usr/share/grafana admin reset-admin-password --password-from-stdin' \
      >/dev/null && echo "Grafana's admin password is GRAFANA_ADMIN_PASSWORD again."
    ;;
  *)
    sed -n '2,10p' "$0"
    exit 2
    ;;
esac
