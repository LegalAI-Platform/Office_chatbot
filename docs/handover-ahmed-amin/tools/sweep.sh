#!/bin/bash
# Test-server only. Usage: sweep.sh <fake number> <messages file>
# Plays the messages through the real Hazem API as that fake client (added to hazem.recipients for the run), prints for each
# turn the replies and, from the database, how each written reply ended (generated_replies: purpose | guard | prompt), then
# puts the setting back and forgets the number.
set -u
cd /opt/office
NUM="$1"; FILE="$2"
pg() { docker compose exec -T postgres sh -c "psql -U \$POSTGRES_USER -d office -Atc \"$1\"" </dev/null; }
ORIG="$(pg "select value from hazem.settings where key='hazem.recipients'")"
printf '%s' "$ORIG" > /tmp/recipients.orig
pg "update hazem.settings set value = value || ',$NUM' where key='hazem.recipients'" >/dev/null
TOKEN="$(grep -E '^HAZEM_API_TOKEN=' .env | cut -d= -f2- | tr -d '\r')"

turn() {
  local text="$1" id now
  id="wt:$(cat /proc/sys/kernel/random/uuid)"
  now="$(date -u +%Y-%m-%dT%H:%M:%S+00:00)"
  python3 - "$id" "$NUM" "$text" "$now" > /tmp/turn.json <<'PY'
import json,sys,uuid
tid,num,text,now=sys.argv[1:5]
print(json.dumps({"turn_id":tid,"wa_id":num,"contact_name":"Claude Test","messages":[{"wamid":"wamid."+uuid.uuid4().hex,"type":"text","text":text,"sent_at":now,"context_wamid":None}]},ensure_ascii=False))
PY
  echo "IN: $text"
  docker compose exec -T api sh -c 'cat > /tmp/turn.json' < /tmp/turn.json
  docker compose exec -T -e T="$TOKEN" api sh -c 'wget -qO- --header="Content-Type: application/json" --header="X-Hazem-Token: $T" --post-file=/tmp/turn.json http://localhost:8080/api/hazem/turns' </dev/null \
    | python3 -c '
import json,sys
try:
    r=json.load(sys.stdin)
except Exception as e:
    print("  (no JSON:",e,")"); sys.exit()
acts=r.get("actions") or []
if not acts: print("  (no reply)", r.get("status"))
for a in acts:
    t=a.get("text") or ""
    i=a.get("interactive")
    extra=""
    if i: extra=" {"+" / ".join(c.get("title","") for c in i.get("choices",[]))+"}"
    print("  OUT:", t.replace("\n"," | ")+extra)
'
  pg "select '  ✎ '||c.intent||' '||c.confidence||' '||coalesce(c.info_topic,'')||' keys='||coalesce((select string_agg(r.text_key, ',') from hazem.turn_replies r where r.turn_id='$id'),'-')||' written='||coalesce((select string_agg(g.purpose||':'||g.guard, ',') from hazem.generated_replies g where g.turn_id='$id'),'-') from hazem.classifications c where c.turn_id='$id'"
}

while IFS= read -r m <&3; do [ -n "$m" ] && turn "$m"; done 3< "$FILE"

pg "update hazem.settings set value = '$(cat /tmp/recipients.orig)' where key='hazem.recipients'" >/dev/null
echo "recipients restored: $(pg "select length(value) from hazem.settings where key='hazem.recipients'")"
bash scripts/linux/forget-number.sh "$NUM" 2>&1 | tail -1
rm -f /tmp/turn.json
