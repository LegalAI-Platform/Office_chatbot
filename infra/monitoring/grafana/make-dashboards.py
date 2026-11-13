#!/usr/bin/env python3
"""Hazem's Grafana dashboards (v1.109), as code: this file writes infra/monitoring/grafana/dashboards/*.json.

    python3 infra/monitoring/grafana/make-dashboards.py           write the 6 dashboards
    python3 infra/monitoring/grafana/make-dashboards.py --check   fail if a committed JSON differs from what this writes (CI)

Change a dashboard here, run it, commit both. One look for all six: a row of status tiles, a row of key numbers, then the
graphs; the same sizes, units and thresholds everywhere (the thresholds are the alert rules' own). English only.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "dashboards")
PROM = {"type": "prometheus", "uid": "prometheus"}
LOKI = {"type": "loki", "uid": "loki"}
STREAM = "/api/panel/alerts/stream"
TURNS = "/api/hazem/turns"
PANEL_ROUTES = f'job="api",http_route=~"/api/panel/.+",http_route!="{STREAM}"'
RUN = 'container_status="running",oneoff="False"'

# ---------------------------------------------------------------- the look
UP, DOWN = "semi-dark-green", "semi-dark-red"
OK, WARN, BAD = "green", "orange", "red"
TILE_H, KPI_H, GRAPH_H, TABLE_H = 3, 4, 8, 8
UPDOWN = [
    {"type": "value", "options": {"0": {"text": "DOWN", "color": DOWN, "index": 0},
                                  "1": {"text": "UP", "color": UP, "index": 1}}},
    {"type": "special", "options": {"match": "null+nan", "result": {"text": "NO DATA", "color": "gray", "index": 2}}},
]


def steps(*pairs):
    """steps(OK, (70, WARN), (85, BAD)) -> Grafana's absolute threshold steps."""
    first, rest = pairs[0], pairs[1:]
    return {"mode": "absolute", "steps": [{"color": first, "value": None}] + [{"color": c, "value": v} for v, c in rest]}


class Board:
    def __init__(self, uid, title, description, tags, time_from="now-6h"):
        self.d = {
            "uid": uid, "title": title, "description": description, "tags": ["hazem"] + tags,
            "timezone": "Africa/Cairo", "editable": False, "graphTooltip": 1, "refresh": "30s", "liveNow": False,
            "schemaVersion": 41, "version": 1, "fiscalYearStartMonth": 0, "weekStart": "saturday",
            "time": {"from": time_from, "to": "now"},
            "timepicker": {"refresh_intervals": ["30s", "1m", "5m", "15m"]},
            "templating": {"list": []}, "annotations": {"list": []},
            "links": [{"title": "Dashboards", "type": "dashboards", "tags": ["hazem"], "asDropdown": True,
                       "includeVars": False, "keepTime": True, "icon": "external link", "targetBlank": False}],
            "panels": [],
        }
        self.x = self.y = self.row_h = 0
        self.next_id = 1

    def _place(self, panel, w, h):
        if self.x + w > 24:
            self.newline()
        panel["id"] = self.next_id
        panel["gridPos"] = {"h": h, "w": w, "x": self.x, "y": self.y}
        self.next_id += 1
        self.x += w
        self.row_h = max(self.row_h, h)
        self.d["panels"].append(panel)

    def newline(self):
        if self.x:
            self.y += self.row_h
            self.x = self.row_h = 0

    def row(self, title):
        self.newline()
        self.d["panels"].append({"type": "row", "title": title, "collapsed": False, "panels": [], "id": self.next_id,
                                 "gridPos": {"h": 1, "w": 24, "x": 0, "y": self.y}})
        self.next_id += 1
        self.y += 1

    def add(self, panel, w, h):
        self._place(panel, w, h)
        return self

    def var(self, v):
        self.d["templating"]["list"].append(v)

    def json(self):
        return json.dumps(self.d, ensure_ascii=False, indent=2) + "\n"


def q(expr, legend="", instant=False, fmt=None, ds=PROM):
    t = {"datasource": ds, "expr": expr, "legendFormat": legend or "__auto", "range": not instant, "instant": instant}
    if fmt:
        t["format"] = fmt
    return t


def targets(items):
    for i, t in enumerate(items):
        t["refId"] = chr(65 + i)
    return items


def ds_of(items):
    return items[0]["datasource"]


def tile(title, expr, desc=""):
    """A status tile: UP (green) / DOWN (red), the same size everywhere."""
    return {
        "type": "stat", "title": title, "description": desc, "datasource": PROM, "targets": targets([q(expr, title)]),
        "fieldConfig": {"defaults": {"unit": "none", "decimals": 0, "mappings": UPDOWN, "noValue": "NO DATA",
                                     "color": {"mode": "thresholds"}, "thresholds": steps(DOWN, (1, UP))},
                        "overrides": []},
        "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}, "orientation": "auto",
                    "textMode": "value", "colorMode": "background_solid", "graphMode": "none", "justifyMode": "center",
                    "wideLayout": True, "showPercentChange": False, "text": {"titleSize": 12, "valueSize": 18}},
    }


def kpi(title, items, unit, thresholds, desc="", decimals=None, graph=True, no_value="0"):
    """A key number: its value coloured by the thresholds, with a small trend under it."""
    d = {"unit": unit, "noValue": no_value, "color": {"mode": "thresholds"}, "thresholds": thresholds, "mappings": []}
    if decimals is not None:
        d["decimals"] = decimals
    return {
        "type": "stat", "title": title, "description": desc, "datasource": ds_of(items), "targets": targets(items),
        "fieldConfig": {"defaults": d, "overrides": []},
        "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}, "orientation": "auto",
                    "textMode": "value", "colorMode": "value", "graphMode": "area" if graph else "none",
                    "justifyMode": "center", "wideLayout": True, "showPercentChange": False,
                    "text": {"titleSize": 12, "valueSize": 26}},
    }


def graph(title, items, unit="short", desc="", stack=False, mn=0, mx=None, legend="list", calcs=("lastNotNull",),
          decimals=None, thresholds=None):
    d = {"unit": unit, "color": {"mode": "palette-classic"},
         "custom": {"drawStyle": "line", "lineWidth": 2, "fillOpacity": 12, "gradientMode": "opacity",
                    "showPoints": "never", "spanNulls": True, "pointSize": 4, "axisBorderShow": False,
                    "stacking": {"mode": "normal" if stack else "none", "group": "A"},
                    "thresholdsStyle": {"mode": "dashed" if thresholds else "off"}}}
    if mn is not None:
        d["min"] = mn
    if mx is not None:
        d["max"] = mx
    if decimals is not None:
        d["decimals"] = decimals
    d["thresholds"] = thresholds or steps(OK)
    return {
        "type": "timeseries", "title": title, "description": desc, "datasource": ds_of(items), "targets": targets(items),
        "fieldConfig": {"defaults": d, "overrides": []},
        "options": {"legend": {"showLegend": True, "displayMode": legend, "placement": "right" if legend == "table" else "bottom",
                               "calcs": list(calcs) if legend == "table" else []},
                    "tooltip": {"mode": "multi", "sort": "desc"}},
    }


def timeline(title, items, desc=""):
    """Each service's UP / DOWN over the time range, one line each."""
    return {
        "type": "state-timeline", "title": title, "description": desc, "datasource": PROM, "targets": targets(items),
        "fieldConfig": {"defaults": {"mappings": UPDOWN, "color": {"mode": "thresholds"}, "thresholds": steps(DOWN, (1, UP)),
                                     "custom": {"fillOpacity": 85, "lineWidth": 0}}, "overrides": []},
        "options": {"mergeValues": True, "showValue": "never", "alignValue": "left", "rowHeight": 0.75,
                    "legend": {"showLegend": False, "displayMode": "list", "placement": "bottom"},
                    "tooltip": {"mode": "single", "sort": "none"}},
    }


def column(name, unit=None, decimals=None, width=None, cell=None, mappings=None, thresholds=None, display=None):
    props = []
    if display:
        props.append({"id": "displayName", "value": display})
    if unit:
        props.append({"id": "unit", "value": unit})
    if decimals is not None:
        props.append({"id": "decimals", "value": decimals})
    if width:
        props.append({"id": "custom.width", "value": width})
    if cell:
        props.append({"id": "custom.cellOptions", "value": cell})
    if mappings:
        props.append({"id": "mappings", "value": mappings})
    if thresholds:
        props.append({"id": "thresholds", "value": thresholds})
    return {"matcher": {"id": "byName", "options": name}, "properties": props}


def words(colors):
    """Value mappings for text cells: {"running": "green", …}."""
    return [{"type": "value", "options": {k: {"color": c, "index": i} for i, (k, c) in enumerate(colors.items())}}]


def table(title, items, desc="", organize=None, columns=(), sort=None):
    p = {
        "type": "table", "title": title, "description": desc, "datasource": PROM, "targets": targets(items),
        "transformations": [{"id": "merge", "options": {}}] + ([{"id": "organize", "options": organize}] if organize else []),
        "fieldConfig": {"defaults": {"custom": {"align": "auto", "cellOptions": {"type": "auto"}, "inspect": False},
                                     "color": {"mode": "thresholds"}, "thresholds": steps(OK), "mappings": []},
                        "overrides": list(columns)},
        "options": {"showHeader": True, "cellHeight": "sm", "footer": {"show": False, "reducer": ["sum"], "fields": ""}},
    }
    if sort:
        p["options"]["sortBy"] = [{"displayName": sort[0], "desc": sort[1]}]
    return p


def bars(title, items, unit="short", desc="", thresholds=None, mx=None):
    d = {"unit": unit, "min": 0, "color": {"mode": "thresholds"}, "thresholds": thresholds or steps("blue")}
    if mx is not None:
        d["max"] = mx
    return {
        "type": "bargauge", "title": title, "description": desc, "datasource": ds_of(items), "targets": targets(items),
        "fieldConfig": {"defaults": d, "overrides": []},
        "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}, "orientation": "horizontal",
                    "displayMode": "basic", "valueMode": "color", "namePlacement": "left", "showUnfilled": True,
                    "sizing": "auto", "minVizHeight": 10, "maxVizHeight": 24},
    }


def logs(title, expr):
    return {"type": "logs", "title": title, "datasource": LOKI, "targets": targets([q(expr, ds=LOKI)]),
            "options": {"showTime": True, "wrapLogMessage": True, "sortOrder": "Descending", "enableLogDetails": True,
                        "dedupStrategy": "none", "prettifyLogMessage": False, "showLabels": False, "showCommonLabels": False}}


def first_lines(metric, window):
    """Lines counted in `window`, the counter's first value included (Alloy makes it at its first line, with 1)."""
    return (f"(sum(increase({metric}[{window}]) and {metric} offset {window}) or vector(0))"
            f" + (sum({metric} unless {metric} offset {window}) or vector(0))")


SERVICES = [  # (tile title, expression, what it checks)
    ("API", 'probe_success{job="probe",service="api"}', "Hazem's API: /health/ready (the database answers, schema hazem is there)."),
    ("Panel", 'probe_success{job="probe",service="web"}', "The panel's web container: /healthz."),
    ("n8n", 'probe_success{job="probe",service="n8n"}', "n8n's readiness: the WhatsApp layer, in and out."),
    ("Chatwoot", 'probe_success{job="probe",service="chatwoot"}', "Chatwoot's /api: the staff's conversations."),
    ("Sidekiq", 'clamp_max(max(redis_key_size{cluster="chatwoot",key="processes"}), 1)', "Chatwoot's Sidekiq: a process listed in its Redis."),
    ("Storage", 'probe_success{job="probe",service="garage"}', "Garage (S3): the clients' files and the backups."),
    ("Office DB", 'pg_up{cluster="office"}', "The office's PostgreSQL (databases office and n8n)."),
    ("Chatwoot DB", 'pg_up{cluster="chatwoot"}', "Chatwoot's PostgreSQL."),
    ("Office Redis", 'redis_up{cluster="office"}', "The office's Redis (n8n's grouping and dedupe)."),
    ("Chatwoot Redis", 'redis_up{cluster="chatwoot"}', "Chatwoot's Redis (Sidekiq's queues)."),
    ("WhatsApp webhook", 'probe_success{job="probe_public",service="edge"}', "From outside, as Meta reaches it: wa.<domain>/healthz through Cloudflare, the tunnel and the edge."),
    ("Website", 'probe_success{job="probe_public",service="web"}', "From outside: the landing page through Cloudflare, the tunnel, the edge and the panel."),
]
CPU = '100 * (1 - avg(rate(node_cpu_seconds_total{mode="idle"}[5m])))'
MEM = '100 * (1 - node_memory_MemAvailable_bytes / node_memory_MemTotal_bytes)'
DISK = '100 * (1 - node_filesystem_avail_bytes{mountpoint="/"} / node_filesystem_size_bytes{mountpoint="/"})'
REQ_BY_STATUS = ('sum by (status) (label_replace(rate(http_server_request_duration_seconds_count{job="api"}[$__rate_interval]),'
                 ' "status", "${1}xx", "http_response_status_code", "(.).."))')


def p95(selector, window="$__rate_interval", q_=0.95):
    return f"histogram_quantile({q_}, sum by (le) (rate(http_server_request_duration_seconds_bucket{{{selector}}}[{window}])))"


boards = {}

# ================================================================ Overview
b = Board("hazem-overview", "Hazem · Overview", "Every service up or down, the alerts firing, and the server's and the API's key numbers.",
          ["overview"], "now-3h")
b.row("Services")
for title, expr, desc in SERVICES:
    b.add(tile(title, expr, desc), 4, TILE_H)
b.row("Key numbers")
b.add(kpi("Firing alerts", [q('count(ALERTS{alertstate="firing"}) or vector(0)')], "none", steps(OK, (1, BAD)),
          "Alerts firing now (Alertmanager e-mails them).", 0, graph=False), 4, KPI_H)
b.add(kpi("CPU", [q(CPU)], "percent", steps(OK, (70, WARN), (85, BAD)), "The server's CPU in use (2 vCPU).", 0), 4, KPI_H)
b.add(kpi("Memory", [q(MEM)], "percent", steps(OK, (80, WARN), (90, BAD)), "The server's memory in use.", 0), 4, KPI_H)
b.add(kpi("Disk", [q(DISK)], "percent", steps(OK, (80, WARN), (90, BAD)), "The root disk in use.", 0), 4, KPI_H)
b.add(kpi("API requests", [q('sum(rate(http_server_request_duration_seconds_count{job="api"}[5m]))')], "reqps",
          steps("blue"), "Requests per second to the API (n8n's turns and the panel; health checks are not counted).", 2), 4, KPI_H)
b.add(kpi("API errors", [q('100 * sum(rate(http_server_request_duration_seconds_count{job="api",http_response_status_code=~"5.."}[15m]))'
                           ' / clamp_min(sum(rate(http_server_request_duration_seconds_count{job="api"}[15m])), 1e-9)')],
          "percent", steps(OK, (1, WARN), (5, BAD)), "Share of the API's answers that were 5xx, last 15 minutes.", 1), 4, KPI_H)
b.add(kpi("Panel latency p95", [q(p95(PANEL_ROUTES, "15m"))], "s", steps(OK, (1, WARN), (3, BAD)),
          "95% of the panel's API requests answered within this, last 15 minutes.", 2, no_value="–"), 4, KPI_H)
b.add(kpi("Hazem reply p95", [q(p95(f'job="api",http_route="{TURNS}"', "15m"))], "s", steps(OK, (20, WARN), (45, BAD)),
          "95% of Hazem's replies (n8n's turns, the AI included) within this, last 15 minutes.", 1, no_value="–"), 4, KPI_H)
b.add(kpi("Office DB connections", [q('100 * sum(pg_stat_activity_count{cluster="office"}) / max(pg_settings_max_connections{cluster="office"})')],
          "percent", steps(OK, (60, WARN), (80, BAD)), "Connections to the office's PostgreSQL, of max_connections.", 0), 4, KPI_H)
b.add(kpi("Office Redis memory", [q('100 * max(redis_memory_used_bytes{cluster="office"}) / max(redis_memory_max_bytes{cluster="office"} > 0)')],
          "percent", steps(OK, (60, WARN), (80, BAD)), "Of its 256 MB: full, it refuses writes (noeviction).", 0), 4, KPI_H)
b.add(kpi("Error log lines (1h)", [q('sum(count_over_time({service=~".+"} |~ "(?i)(error|fatal|panic|exception)" [1h]))',
                                     instant=True, ds=LOKI)], "short", steps(OK, (50, WARN), (500, BAD)),
          "Log lines with error, fatal, panic or exception in the last hour, every service.", 0, graph=False), 4, KPI_H)
b.add(kpi("Masked log lines (24h)", [q(first_lines("loki_process_custom_log_lines_with_phone_total", "24h") + " + "
                                       + first_lines("loki_process_custom_log_lines_with_secret_total", "24h"))],
          "short", steps(OK, (1, BAD)), "Lines that had a full phone number or a key, masked before Loki kept them: the code that logged them must change.",
          0, graph=False), 4, KPI_H)
b.row("Alerts and availability")
b.add(table("Firing alerts", [
    q('max by (alertname, severity, service) (ALERTS_FOR_STATE) * 1000 and on (alertname, severity, service)'
      ' max by (alertname, severity, service) (ALERTS{alertstate="firing"})', instant=True, fmt="table")],
    "What fires now, since when. Silences: Alertmanager (127.0.0.1:9093).",
    organize={"excludeByName": {"Time": True}, "indexByName": {"alertname": 0, "severity": 1, "service": 2, "Value": 3},
              "renameByName": {"alertname": "Alert", "severity": "Severity", "service": "Service", "Value": "Since"}},
    columns=[column("Since", unit="dateTimeFromNow", width=150),
             column("Severity", width=110, cell={"type": "color-text"}, mappings=words({"critical": BAD, "warning": WARN}))],
    sort=("Since", True)), 10, 9)
b.add(timeline("Availability", [q(e, t) for t, e, _ in SERVICES], "Each service UP (green) or DOWN (red) over the time range."), 14, 9)
b.row("Trends")
b.add(graph("Server CPU and memory", [q(CPU, "CPU"), q(MEM, "Memory")], "percent", mx=100,
            thresholds=steps(OK, (85, BAD))), 12, GRAPH_H)
b.add(graph("API requests by status", [q(REQ_BY_STATUS, "{{status}}")], "reqps", stack=True), 12, GRAPH_H)
boards["overview.json"] = b

# ================================================================ Server & containers
b = Board("hazem-host", "Hazem · Server & Containers", "The server (2 vCPU, 8 GB) and every container: CPU, memory, disk, network, state.", ["server"])
b.row("Server")
b.add(kpi("CPU", [q(CPU)], "percent", steps(OK, (70, WARN), (85, BAD)), "", 0), 4, KPI_H)
b.add(kpi("Memory", [q(MEM)], "percent", steps(OK, (80, WARN), (90, BAD)), "", 0), 4, KPI_H)
b.add(kpi("Disk", [q(DISK)], "percent", steps(OK, (80, WARN), (90, BAD)), "", 0), 4, KPI_H)
b.add(kpi("Load (1 min)", [q("node_load1")], "short", steps(OK, (2, WARN), (4, BAD)), "Processes waiting for a CPU; the server has 2.", 2), 4, KPI_H)
b.add(kpi("Uptime", [q("time() - node_boot_time_seconds")], "s", steps("blue"), "Since the server last started.", 0, graph=False), 4, KPI_H)
b.add(kpi("Clock offset", [q("node_timex_offset_seconds")], "s", steps(OK, (0.1, WARN), (0.5, BAD)),
          "From internet time (NTP). The authenticator codes and the booking depend on it.", 3, graph=False), 4, KPI_H)
b.add(graph("CPU", [q('100 * avg by (mode) (rate(node_cpu_seconds_total{mode=~"user|system|iowait|steal"}[$__rate_interval]))', "{{mode}}")],
            "percent", stack=True, mx=100), 12, GRAPH_H)
b.add(graph("Memory", [q("node_memory_MemTotal_bytes - node_memory_MemAvailable_bytes", "used"),
                       q("node_memory_MemAvailable_bytes", "available"),
                       q("node_memory_SwapTotal_bytes - node_memory_SwapFree_bytes", "swap used")], "bytes", stack=False), 12, GRAPH_H)
b.add(graph("Disk space (/)", [q('node_filesystem_size_bytes{mountpoint="/"} - node_filesystem_avail_bytes{mountpoint="/"}', "used"),
                               q('node_filesystem_avail_bytes{mountpoint="/"}', "free")], "bytes", stack=True), 12, GRAPH_H)
b.add(graph("Disk I/O", [q("sum(rate(node_disk_read_bytes_total[$__rate_interval]))", "read"),
                         q("sum(rate(node_disk_written_bytes_total[$__rate_interval]))", "written")], "Bps"), 12, GRAPH_H)
b.row("Containers")
b.add(table("Containers", [
    q('max by (service, container_status) (docker_container_status_exitcode{oneoff="False"})', instant=True, fmt="table"),
    q('max by (service, health_status) (docker_container_health_failing_streak{oneoff="False"})', instant=True, fmt="table"),
    q('max by (service) (increase(docker_container_status_restart_count{oneoff="False"}[24h]))', instant=True, fmt="table"),
    q(f'sum by (service) (docker_container_cpu_usage_percent{{{RUN},cpu="cpu-total"}})', instant=True, fmt="table"),
    q(f'sum by (service) (docker_container_mem_usage{{{RUN}}})', instant=True, fmt="table"),
    q(f'max by (service) (docker_container_mem_usage_percent{{{RUN}}})', instant=True, fmt="table")],
    "Every container of the project: Docker's state and healthcheck, restarts in 24 hours, CPU (100% = one CPU), memory, and memory of its limit.",
    organize={"excludeByName": {"Time": True, "Value #A": True, "Value #B": True},
              "indexByName": {"service": 0, "container_status": 1, "health_status": 2, "Value #C": 3, "Value #D": 4,
                              "Value #E": 5, "Value #F": 6},
              "renameByName": {"service": "Service", "container_status": "State", "health_status": "Health",
                               "Value #C": "Restarts (24h)", "Value #D": "CPU", "Value #E": "Memory", "Value #F": "Memory of limit"}},
    columns=[column("State", cell={"type": "color-text"}, mappings=words({"running": OK, "exited": BAD, "dead": BAD,
                                                                          "restarting": WARN, "created": "blue"}), width=110),
             column("Health", cell={"type": "color-text"}, mappings=words({"healthy": OK, "unhealthy": BAD, "starting": WARN}), width=110),
             column("Restarts (24h)", decimals=0, cell={"type": "color-text"}, thresholds=steps(OK, (1, WARN), (3, BAD)), width=130),
             column("CPU", unit="percent", decimals=1, width=100),
             column("Memory", unit="bytes", decimals=0, width=110),
             column("Memory of limit", unit="percent", decimals=0,
                    cell={"type": "gauge", "mode": "basic", "valueDisplayMode": "text"}, thresholds=steps(OK, (80, WARN), (90, BAD)))],
    sort=("Service", False)), 24, 11)
b.add(graph("CPU by container", [q(f'sum by (service) (docker_container_cpu_usage_percent{{{RUN},cpu="cpu-total"}})', "{{service}}")],
            "percent", legend="table", calcs=("mean", "max", "lastNotNull"), desc="100% = one CPU (the server has 2)."), 12, 9)
b.add(graph("Memory by container", [q(f'sum by (service) (docker_container_mem_usage{{{RUN}}})', "{{service}}")],
            "bytes", legend="table", calcs=("mean", "max", "lastNotNull")), 12, 9)
b.add(graph("Network in by container", [q(f'sum by (service) (rate(docker_container_net_rx_bytes{{{RUN},network="total"}}[$__rate_interval]))', "{{service}}")],
            "Bps", legend="table", calcs=("mean", "max")), 12, 9)
b.add(graph("Disk writes by container", [q(f'sum by (service) (rate(docker_container_blkio_io_service_bytes_recursive_write{{{RUN},device="total"}}[$__rate_interval]))', "{{service}}")],
            "Bps", legend="table", calcs=("mean", "max")), 12, 9)
boards["host.json"] = b

# ================================================================ API
b = Board("hazem-api", "Hazem · API", "Hazem's .NET API: its requests, the .NET runtime, the database pool, the calls it makes, and the bell's alert worker.", ["api"])
b.row("Traffic")
b.add(kpi("Requests", [q('sum(rate(http_server_request_duration_seconds_count{job="api"}[5m]))')], "reqps", steps("blue"),
          "Requests per second (n8n's turns and the panel; health checks are not counted).", 2), 4, KPI_H)
b.add(kpi("Errors (5xx)", [q('100 * sum(rate(http_server_request_duration_seconds_count{job="api",http_response_status_code=~"5.."}[15m]))'
                             ' / clamp_min(sum(rate(http_server_request_duration_seconds_count{job="api"}[15m])), 1e-9)')],
          "percent", steps(OK, (1, WARN), (5, BAD)), "Last 15 minutes.", 1), 4, KPI_H)
b.add(kpi("Panel latency p95", [q(p95(PANEL_ROUTES, "15m"))], "s", steps(OK, (1, WARN), (3, BAD)), "Last 15 minutes.", 2, no_value="–"), 4, KPI_H)
b.add(kpi("Hazem reply p95", [q(p95(f'job="api",http_route="{TURNS}"', "15m"))], "s", steps(OK, (20, WARN), (45, BAD)),
          "n8n's turns, the AI included; last 15 minutes.", 1, no_value="–"), 4, KPI_H)
b.add(kpi("Requests in flight", [q('sum(http_server_active_requests{job="api"})')], "short", steps("blue"),
          "The bell's live stream (one per open panel) stays open.", 0), 4, KPI_H)
b.add(kpi("Open connections", [q('sum(kestrel_active_connections{job="api"})')], "short", steps("blue"), "", 0), 4, KPI_H)
b.add(graph("Requests by status", [q(REQ_BY_STATUS, "{{status}}")], "reqps", stack=True), 12, GRAPH_H)
b.add(graph("Latency", [q(p95(f'job="api",http_route!="{STREAM}",http_route!="{TURNS}"', q_=v), n) for v, n in ((0.5, "p50"), (0.95, "p95"), (0.99, "p99"))],
            "s", desc="The panel and the webhooks: not Hazem's replies (they wait for the AI) nor the bell's live stream (open for minutes)."),
      12, GRAPH_H)
b.add(table("Busiest routes", [q('sum by (http_route, http_request_method) (increase(http_server_request_duration_seconds_count{job="api",http_route!=""}[$__range])) > 0',
                                 instant=True, fmt="table")], "Requests in the time range above.",
            organize={"excludeByName": {"Time": True}, "indexByName": {"http_request_method": 0, "http_route": 1, "Value": 2},
                      "renameByName": {"http_route": "Route", "http_request_method": "Method", "Value": "Requests"}},
            columns=[column("Method", width=80), column("Requests", decimals=0, width=110)], sort=("Requests", True)), 12, TABLE_H)
b.add(table("Slowest routes (p95)", [q(f'histogram_quantile(0.95, sum by (http_route, le) (rate(http_server_request_duration_seconds_bucket{{job="api",http_route!="",http_route!="{STREAM}"}}[$__range]))) > 0',
                                       instant=True, fmt="table")], "In the time range above.",
            organize={"excludeByName": {"Time": True}, "renameByName": {"http_route": "Route", "Value": "p95"}},
            columns=[column("p95", unit="s", decimals=2, width=110, cell={"type": "color-text"}, thresholds=steps(OK, (1, WARN), (3, BAD)))],
            sort=("p95", True)), 12, TABLE_H)
b.add(graph("Hazem's replies", [q(p95(f'job="api",http_route="{TURNS}"', q_=v), n) for v, n in ((0.5, "p50"), (0.95, "p95"))], "s",
            desc="From n8n handing over a client's messages to Hazem's answer, the AI included.",
            thresholds=steps(OK, (45, BAD))), 12, GRAPH_H)
b.add(graph("5xx by route", [q('sum by (http_route) (rate(http_server_request_duration_seconds_count{job="api",http_response_status_code=~"5.."}[$__rate_interval]))', "{{http_route}}")],
            "reqps"), 12, GRAPH_H)
b.row(".NET runtime")
b.add(graph("CPU", [q('sum(rate(dotnet_process_cpu_time_seconds_total{job="api"}[$__rate_interval])) * 100', "API")], "percent",
            desc="100% = one CPU."), 8, GRAPH_H)
b.add(graph("Memory", [q('dotnet_process_memory_working_set_bytes{job="api"}', "working set"),
                       q('sum(dotnet_gc_last_collection_heap_size_bytes{job="api"})', "GC heap")], "bytes"), 8, GRAPH_H)
b.add(graph("GC collections", [q('sum by (gc_heap_generation) (rate(dotnet_gc_collections_total{job="api"}[$__rate_interval]))', "{{gc_heap_generation}}")],
            "ops"), 8, GRAPH_H)
b.add(graph("Thread pool", [q('dotnet_thread_pool_thread_count_total{job="api"}', "threads"),
                            q('dotnet_thread_pool_queue_length_total{job="api"}', "queued work")], "short"), 8, GRAPH_H)
b.add(graph("Exceptions", [q('sum by (error_type) (rate(dotnet_exceptions_total{job="api"}[$__rate_interval]))', "{{error_type}}")], "ops"), 8, GRAPH_H)
b.add(graph("GC pause", [q('rate(dotnet_gc_pause_time_seconds_total{job="api"}[$__rate_interval]) * 100', "time paused")], "percent", decimals=2),
      8, GRAPH_H)
b.row("Database pool (Npgsql)")
b.add(graph("Connections", [q('sum by (db_client_connection_state) (db_client_connection_count{job="api"})', "{{db_client_connection_state}}"),
                            q('max(db_client_connection_max{job="api"})', "max")], "short",
            desc="used = running a command now; idle = open and waiting."), 8, GRAPH_H)
b.add(graph("Command time", [q('histogram_quantile(0.95, sum by (le) (rate(db_client_operation_duration_seconds_bucket{job="api"}[$__rate_interval])))', "p95"),
                             q('histogram_quantile(0.5, sum by (le) (rate(db_client_operation_duration_seconds_bucket{job="api"}[$__rate_interval])))', "p50")],
            "s"), 8, GRAPH_H)
b.add(graph("Commands", [q('sum(rate(db_client_operation_duration_seconds_count{job="api"}[$__rate_interval]))', "commands/s"),
                         q('sum(db_client_connection_pending_requests{job="api"}) or vector(0)', "waiting for a connection")], "short"), 8, GRAPH_H)
b.row("Calls out (the AI, Chatwoot, n8n, Google)")
b.add(graph("Latency by host (p95)", [q('histogram_quantile(0.95, sum by (server_address, le) (rate(http_client_request_duration_seconds_bucket{job="api"}[$__rate_interval])))', "{{server_address}}")],
            "s"), 12, GRAPH_H)
b.add(graph("Calls by host and status", [q('sum by (server_address, http_response_status_code) (rate(http_client_request_duration_seconds_count{job="api"}[$__rate_interval]))',
                                           "{{server_address}} {{http_response_status_code}}")], "reqps"), 12, GRAPH_H)
b.row("The bell's alert worker")
b.add(kpi("Last good round", [q('time() - office_alerts_last_ok_round_timestamp_seconds{job="api"}')], "s", steps(OK, (30, WARN), (180, BAD)),
          "A round runs every 3 seconds; past 3 minutes AlertWorkerStalled is e-mailed.", 0, graph=False, no_value="–"), 6, 7)
b.add(graph("Rounds", [q('sum by (result) (rate(office_alerts_rounds_total{job="api"}[$__rate_interval]))', "{{result}}")], "ops"), 9, 7)
b.add(graph("Alert e-mails", [q('sum by (result) (increase(office_alerts_emails_total{job="api"}[$__rate_interval]))', "{{result}}")], "short"), 9, 7)
boards["api.json"] = b

# ================================================================ Databases & Redis
b = Board("hazem-databases", "Hazem · Databases", "Both PostgreSQL (the office's and Chatwoot's) and both Redis.", ["databases"])
b.row("PostgreSQL")
b.add(tile("Office DB", 'pg_up{cluster="office"}'), 4, TILE_H)
b.add(tile("Chatwoot DB", 'pg_up{cluster="chatwoot"}'), 4, TILE_H)
b.add(kpi("Office connections", [q('100 * sum(pg_stat_activity_count{cluster="office"}) / max(pg_settings_max_connections{cluster="office"})')],
          "percent", steps(OK, (60, WARN), (80, BAD)), "Of max_connections.", 0), 4, TILE_H)
b.add(kpi("Chatwoot connections", [q('100 * sum(pg_stat_activity_count{cluster="chatwoot"}) / max(pg_settings_max_connections{cluster="chatwoot"})')],
          "percent", steps(OK, (60, WARN), (80, BAD)), "Of max_connections.", 0), 4, TILE_H)
b.add(kpi("Office DB size", [q('pg_database_size_bytes{cluster="office",datname="office"}')], "bytes", steps("blue"), "", 1, graph=False), 4, TILE_H)
b.add(kpi("Longest transaction", [q('max(pg_stat_activity_max_tx_duration)')], "s", steps(OK, (300, WARN), (900, BAD)),
          "Either database; past 15 minutes it is an alert.", 0, graph=False), 4, TILE_H)
b.add(graph("Connections by state", [q('sum by (cluster, state) (pg_stat_activity_count{state!=""})', "{{cluster}} · {{state}}")], "short"), 12, GRAPH_H)
b.add(graph("Transactions", [q('sum by (cluster) (rate(pg_stat_database_xact_commit{datname!~"template.*"}[$__rate_interval]))', "{{cluster}} · commit"),
                             q('sum by (cluster) (rate(pg_stat_database_xact_rollback{datname!~"template.*"}[$__rate_interval]))', "{{cluster}} · rollback")],
            "ops"), 12, GRAPH_H)
b.add(graph("Connections by database", [q('sum by (cluster, datname) (pg_stat_database_numbackends{datname!~"template.*"})', "{{cluster}} · {{datname}}")],
            "short"), 8, GRAPH_H)
b.add(graph("Cache hit ratio", [q('100 * sum by (cluster) (rate(pg_stat_database_blks_hit[$__rate_interval])) / clamp_min(sum by (cluster) '
                                  '(rate(pg_stat_database_blks_hit[$__rate_interval]) + rate(pg_stat_database_blks_read[$__rate_interval])), 1e-9)', "{{cluster}}")],
            "percent", mn=None, mx=100, desc="Reads served from memory, not the disk: above 99% is normal."), 8, GRAPH_H)
b.add(graph("Database size", [q('pg_database_size_bytes{datname!~"template.*"}', "{{cluster}} · {{datname}}")], "bytes"), 8, GRAPH_H)
b.add(graph("Locks", [q('sum by (cluster, mode) (pg_locks_count{datname!~"template.*"}) > 0', "{{cluster}} · {{mode}}")], "short"), 12, GRAPH_H)
b.add(graph("Deadlocks", [q('sum by (cluster) (increase(pg_stat_database_deadlocks[$__rate_interval]))', "{{cluster}}")], "short"), 12, GRAPH_H)
b.row("Redis")
b.add(tile("Office Redis", 'redis_up{cluster="office"}'), 4, TILE_H)
b.add(tile("Chatwoot Redis", 'redis_up{cluster="chatwoot"}'), 4, TILE_H)
b.add(kpi("Office Redis memory", [q('100 * max(redis_memory_used_bytes{cluster="office"}) / max(redis_memory_max_bytes{cluster="office"} > 0)')],
          "percent", steps(OK, (60, WARN), (80, BAD)), "Of its 256 MB (noeviction: full, it refuses writes).", 0), 4, TILE_H)
b.add(kpi("Office clients", [q('max(redis_connected_clients{cluster="office"})')], "short", steps("blue"), "", 0), 4, TILE_H)
b.add(kpi("Chatwoot clients", [q('max(redis_connected_clients{cluster="chatwoot"})')], "short", steps("blue"), "", 0), 4, TILE_H)
b.add(kpi("Rejected connections (24h)", [q('sum(increase(redis_rejected_connections_total[24h])) or vector(0)')], "short",
          steps(OK, (1, BAD)), "", 0, graph=False), 4, TILE_H)
b.add(graph("Memory", [q("redis_memory_used_bytes", "{{cluster}} · used"), q("redis_memory_max_bytes > 0", "{{cluster}} · limit")], "bytes"), 8, GRAPH_H)
b.add(graph("Commands", [q("rate(redis_commands_processed_total[$__rate_interval])", "{{cluster}}")], "ops"), 8, GRAPH_H)
b.add(graph("Keys", [q("sum by (cluster) (redis_db_keys)", "{{cluster}}")], "short"), 8, GRAPH_H)
boards["databases.json"] = b

# ================================================================ Services (n8n, Chatwoot, storage)
b = Board("hazem-apps", "Hazem · Services", "n8n (the WhatsApp layer), Chatwoot and its Sidekiq, and the storage.", ["services"])
b.row("n8n")
b.add(tile("n8n", 'probe_success{job="probe",service="n8n"}'), 4, TILE_H)
b.add(kpi("Active workflows", [q('max(n8n_active_workflow_count{job="n8n"})')], "short", steps(BAD, (1, OK)),
          "0 means no WhatsApp message is handled.", 0, graph=False, no_value="–"), 4, TILE_H)
b.add(kpi("Event loop lag p99", [q('max(n8n_nodejs_eventloop_lag_p99_seconds{job="n8n"})')], "s", steps(OK, (0.5, WARN), (1, BAD)),
          "Past a second every workflow slows down.", 3), 4, TILE_H)
b.add(kpi("Memory (RSS)", [q('max(n8n_process_resident_memory_bytes{job="n8n"})')], "bytes", steps("blue"), "", 0), 4, TILE_H)
b.add(kpi("Failed workflows (24h)", [q('sum(increase(n8n_workflow_failed_total{job="n8n"}[24h])) or vector(0)')], "short",
          steps(OK, (1, WARN), (10, BAD)), "The bell also tells the managers, once an hour.", 0, graph=False), 4, TILE_H)
b.add(kpi("CPU", [q('max(rate(n8n_process_cpu_seconds_total{job="n8n"}[5m])) * 100')], "percent", steps(OK, (70, WARN), (90, BAD)),
          "100% = one CPU.", 0), 4, TILE_H)
b.add(graph("Workflows", [q('sum by (__name__) (rate({__name__=~"n8n_workflow_(started|success|failed)_total",job="n8n"}[$__rate_interval]))', "{{__name__}}")],
            "ops"), 12, GRAPH_H)
b.add(graph("Memory and event loop", [q('n8n_process_resident_memory_bytes{job="n8n"}', "RSS"), q('n8n_nodejs_heap_size_used_bytes{job="n8n"}', "heap")],
            "bytes"), 12, GRAPH_H)
b.row("Chatwoot and Sidekiq")
b.add(tile("Chatwoot", 'probe_success{job="probe",service="chatwoot"}'), 4, TILE_H)
b.add(tile("Sidekiq", 'clamp_max(max(redis_key_size{cluster="chatwoot",key="processes"}), 1)'), 4, TILE_H)
b.add(kpi("Chatwoot response", [q('max(probe_duration_seconds{job="probe",service="chatwoot"})')], "s", steps(OK, (1, WARN), (3, BAD)),
          "Time to answer /api.", 2), 4, TILE_H)
b.add(kpi("Queued jobs", [q('sum(redis_key_size{cluster="chatwoot",key=~"queue:.+"}) or vector(0)')], "short", steps(OK, (20, WARN), (100, BAD)),
          "Sidekiq's queues; an empty queue does not exist in Redis.", 0), 4, TILE_H)
b.add(kpi("Retries", [q('max(redis_key_size{cluster="chatwoot",key="retry"}) or vector(0)')], "short", steps(OK, (1, WARN), (20, BAD)), "", 0), 4, TILE_H)
b.add(kpi("Dead jobs", [q('max(redis_key_size{cluster="chatwoot",key="dead"}) or vector(0)')], "short", steps("blue"),
          "Failed after every retry; see Chatwoot's /monitoring/sidekiq.", 0), 4, TILE_H)
b.add(graph("Jobs per minute", [q('rate(redis_key_value{cluster="chatwoot",key="stat:processed"}[$__rate_interval]) * 60', "processed"),
                                q('rate(redis_key_value{cluster="chatwoot",key="stat:failed"}[$__rate_interval]) * 60', "failed")], "short"), 12, GRAPH_H)
b.add(graph("Containers' memory", [q('sum by (service) (docker_container_mem_usage{service=~"chatwoot|chatwoot-sidekiq",container_status="running"})', "{{service}}")],
            "bytes"), 12, GRAPH_H)
b.row("Storage (Garage)")
b.add(tile("Storage", 'probe_success{job="probe",service="garage"}'), 4, TILE_H)
b.add(kpi("Storage memory", [q('sum(docker_container_mem_usage{service="garage",container_status="running"})')], "bytes", steps("blue"), "", 0), 4, TILE_H)
b.newline()
b.add(graph("Storage disk I/O", [q('sum(rate(docker_container_blkio_io_service_bytes_recursive_write{service="garage",device="total"}[$__rate_interval]))', "written"),
                                 q('sum(rate(docker_container_blkio_io_service_bytes_recursive_read{service="garage",device="total"}[$__rate_interval]))', "read")],
            "Bps"), 24, GRAPH_H)
boards["apps.json"] = b

# ================================================================ Logs
b = Board("hazem-logs", "Hazem · Logs", "Every container's log for 7 days, phone numbers, passwords and keys masked before they are kept.", ["logs"], "now-1h")
b.var({"name": "service", "label": "Service", "type": "query", "datasource": LOKI,
       "query": {"label": "service", "stream": "", "type": 1, "refId": "LokiVariableQueryEditor-VariableQuery"},
       "definition": "label_values(service)", "refresh": 2, "multi": True, "includeAll": True, "allValue": ".+",
       "current": {"selected": True, "text": ["All"], "value": ["$__all"]}, "sort": 1, "options": []})
b.var({"name": "search", "label": "Search", "type": "textbox", "query": "", "current": {"text": "", "value": ""}, "options": []})
b.row("Volume")
b.add(kpi("Lines (1h)", [q('sum(count_over_time({service=~"$service"} [1h]))', instant=True, ds=LOKI)], "short", steps("blue"),
          "", 0, graph=False), 4, KPI_H)
b.add(kpi("Error lines (1h)", [q('sum(count_over_time({service=~"$service"} |~ "(?i)(error|fatal|panic|exception)" [1h]))', instant=True, ds=LOKI)],
          "short", steps(OK, (50, WARN), (500, BAD)), "error, fatal, panic or exception.", 0, graph=False), 4, KPI_H)
b.add(kpi("Lines with a number (24h)", [q(first_lines("loki_process_custom_log_lines_with_phone_total", "24h"))], "short", steps(OK, (1, BAD)),
          "Masked before Loki, but Docker keeps the line: the night's log-scan fails (LogsHavePhoneNumber).", 0, graph=False), 4, KPI_H)
b.add(kpi("Lines with a key (24h)", [q(first_lines("loki_process_custom_log_lines_with_secret_total", "24h"))], "short", steps(OK, (1, BAD)),
          "Treat the key as seen and change it (LogsHaveSecret).", 0, graph=False), 4, KPI_H)
b.add(kpi("Dropped lines (24h)", [q('sum(increase(loki_write_dropped_entries_total{job="alloy"}[24h])) or vector(0)')], "short",
          steps(OK, (1, WARN), (100, BAD)), "Lines Alloy could not send to Loki.", 0, graph=False), 4, KPI_H)
b.add(tile("Loki", 'max(up{job="loki"})', "Loki answers Prometheus."), 4, KPI_H)
b.add(graph("Lines by service", [q('sum by (service) (count_over_time({service=~"$service"} [$__auto]))', "{{service}}", ds=LOKI)], "short",
            stack=True, legend="table", calcs=("sum",)), 12, GRAPH_H)
b.add(graph("Error lines by service", [q('sum by (service) (count_over_time({service=~"$service"} |~ "(?i)(error|fatal|panic|exception)" [$__auto]))',
                                         "{{service}}", ds=LOKI)], "short", stack=True, legend="table", calcs=("sum",)), 12, GRAPH_H)
b.row("Lines")
b.add(logs("Logs", '{service=~"$service"} |~ "(?i)$search"'), 24, 18)
boards["logs.json"] = b


def main():
    check = "--check" in sys.argv
    stale = []
    for name, board in boards.items():
        path = os.path.join(OUT, name)
        text = board.json()
        if check:
            try:
                with open(path, encoding="utf-8") as f:
                    if f.read().replace("\r\n", "\n") != text:
                        stale.append(name)
            except FileNotFoundError:
                stale.append(name)
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
    extra = sorted(set(n for n in os.listdir(OUT) if n.endswith(".json")) - set(boards))
    if check and (stale or extra):
        print("The dashboards differ from make-dashboards.py:", ", ".join(stale + extra))
        print("Run: python3 infra/monitoring/grafana/make-dashboards.py, and commit the JSON.")
        sys.exit(1)
    print(("checked " if check else "wrote ") + ", ".join(boards))


if __name__ == "__main__":
    main()
