import { useState } from "react";
import { queryOptions, useQuery, type QueryClient } from "@tanstack/react-query";
import { api, errorMessage } from "../../shared/api/client";
import type { MonthReport, ReportRow } from "../../shared/api/types";
import { APPOINTMENT_STATES, label, SOURCES, TASK_TYPES, CLIENT_TYPES, matterLabel } from "../../shared/lib/labels";
import type { Folder } from "../../shared/ui/folders";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card, ErrorState, Loading, PageHeader } from "../../shared/ui/ui";

/** «This month» in Cairo, as YYYY-MM. */
function thisMonth(): string {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit" }).formatToParts(new Date());
  return `${parts.find((p) => p.type === "year")!.value}-${parts.find((p) => p.type === "month")!.value}`;
}

/** One month's report; the home screen reads the same key. */
const reportQuery = (month: string) => queryOptions({ queryKey: ["report", month], queryFn: () => api<MonthReport>("GET", `/reports?month=${month}`) });

/** This month and the one before (the page compares them), fetched while the pointer is on the link. */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(reportQuery(thisMonth()));
  void qc.prefetchQuery(reportQuery(shift(thisMonth(), -1)));
}

const shift = (month: string, by: number): string => {
  const [y, m] = month.split("-").map((p) => parseInt(p, 10)) as [number, number];
  const d = new Date(Date.UTC(y, m - 1 + by, 1));
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}`;
};
const monthName = (month: string) =>
  new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`));

/** «أكتوبر 2026» between the month before and the month after (never past this month). */
function MonthPicker({ value, onChange }: { value: string; onChange: (m: string) => void }) {
  const last = value >= thisMonth();
  return (
    <div className="flex items-center gap-1" role="group" aria-label="الشهر">
      <Button size="sm" variant="ghost" aria-label="الشهر اللي فات" onClick={() => onChange(shift(value, -1))}><Icon name="chevron-start" className="size-4" /></Button>
      <span className="tabular min-w-28 text-center text-[14.5px] font-medium" aria-live="polite">{monthName(value)}</span>
      <Button size="sm" variant="ghost" aria-label="الشهر الجاي" disabled={last} onClick={() => onChange(shift(value, 1))}><Icon name="chevron-end" className="size-4" /></Button>
    </div>
  );
}

/**
 * The month's report (TASK 07c, the manager and the lawyer): the tasks (by type and by staff
 * member, and how many were done before their due time) and the new clients (by source, topic and
 * kind). TASK 09: the month's bookings by state and their money as the office marked it.
 */
export function ReportsPage() {
  const [month, setMonth] = useState(thisMonth);
  const report = useQuery(reportQuery(month));
  // the month before, so each number says whether it went up or down
  const before = shift(month, -1);
  const prev = useQuery(reportQuery(before)).data;
  const r = report.data;
  const pct = (a: number, b: number) => (b === 0 ? "—" : `${Math.round((a / b) * 100)}%`);

  return (
    <div className="grid gap-5">
      <PageHeader section="/reports" title="التقارير" description="أرقام الشهر: المهام، والعملاء الجداد، والحجوزات وفلوسها زي ما المكتب علّمها."
        actions={<MonthPicker value={month} onChange={setMonth} />} />
      {report.isPending ? <Loading /> : report.isError ? <ErrorState message={errorMessage(report.error)} onRetry={() => void report.refetch()} /> : r ? (
        <>
          <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
            <Number folder="cobalt" title="المهام" value={r.tasks} prev={prev?.tasks} />
            <Number folder="jade" title="خلصت" value={r.done} prev={prev?.done} note={pct(r.done, r.tasks)} />
            <Number folder="saffron" title="خلصت في وقتها" value={r.done_on_time} prev={prev?.done_on_time} note={pct(r.done_on_time, r.done)} />
            <Number folder="orchid" title="عملاء جداد" value={r.new_clients} prev={prev?.new_clients} />
          </div>
          <div className="grid gap-4 lg:grid-cols-2">
            <Table title="المهام حسب النوع" rows={r.by_type} name={(k) => label(TASK_TYPES, k)} />
            <Card>
              <h2 className="text-[16px] font-semibold">المهام حسب الموظف</h2>
              {r.by_staff.length === 0 ? <p className="text-sm text-ink-soft">مفيش مهام الشهر ده.</p> : (
                <table className="w-full text-sm">
                  <thead><tr className="text-ink-soft"><th className="py-1.5 text-start">الموظف</th><th className="text-start">ليه</th><th className="text-start">خلصت</th><th className="text-start">في وقتها</th></tr></thead>
                  <tbody className="divide-y divide-line">
                    {r.by_staff.map((s) => (
                      <tr key={s.staff}><td className="py-1.5">{s.staff || "مش متعيّنة"}</td><td className="tabular">{s.assigned}</td><td className="tabular">{s.done}</td><td className="tabular">{s.on_time}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Card>
            <Table title="العملاء الجداد حسب المصدر" rows={r.by_source} name={(k) => (k === "unknown" ? "مش معروف" : label(SOURCES, k))} />
            <Table title="العملاء الجداد حسب الموضوع" rows={r.by_matter} name={(k) => (k === "unknown" ? "لسه ما اتحددش" : matterLabel(k))} />
            <Table title="حسب نوع العميل" rows={r.by_client_type} name={(k) => (k === "unknown" ? "لسه ما اتحددش" : label(CLIENT_TYPES, k))} />
            <Table title="الحجوزات حسب الحالة" rows={r.by_booking} name={(k) => label(APPOINTMENT_STATES, k)} />
          </div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Number folder="jade" title="وصل (جنيه)" value={r.received} prev={prev?.received} />
            <Number folder="saffron" title="ناقص (جنيه)" value={r.missing} prev={prev?.missing} />
            <Number folder="coral" title="اترد (جنيه)" value={r.refunded} prev={prev?.refunded} />
          </div>
          <p className="text-xs text-ink-soft">«في وقتها» = خلصت قبل الميعاد المطلوب. الفلوس = اللي المكتب علّم إنه وصل فعلًا لحجوزات الشهر.</p>
        </>
      ) : null}
    </div>
  );
}

/** A month's number, with the change from the month before (more is not always better: it just says so). */
function Number({ title, value, note, prev }: { folder: Folder; title: string; value: number; note?: string; prev?: number | undefined }) {
  const delta = prev === undefined ? null : value - prev;
  return (
    <section className="surface grid gap-1 rounded-[var(--radius-card)] px-4 py-3.5">
      <h2 className="text-[14px] font-semibold text-ink-soft">{title}</h2>
      <p className="display text-[30px] leading-tight font-semibold">
        {value}{note && note !== "—" ? <span className="ms-2 text-[14px] font-medium opacity-80">{note}</span> : null}
      </p>
      <p className="text-[12.5px] text-ink-soft">
        {delta === null ? "…" : delta === 0 ? "زي الشهر اللي فات" : (
          <><span className="font-semibold text-ink">{delta > 0 ? "↑" : "↓"} {Math.abs(delta)}</span> عن الشهر اللي فات</>
        )}
      </p>
    </section>
  );
}

function Table({ title, rows, name, color = "var(--gold)" }: { title: string; rows: ReportRow[]; name: (key: string) => string; color?: string }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <Card>
      <h2 className="text-[16px] font-semibold">{title}</h2>
      {rows.length === 0 ? <p className="text-sm text-ink-soft">مفيش حاجة الشهر ده.</p> : (
        <ul className="grid gap-2">
          {rows.map((r) => (
            <li key={r.key} className="grid gap-1">
              <span className="flex justify-between text-sm"><span>{name(r.key)}</span><span className="tabular font-semibold">{r.count}</span></span>
              <span className="h-1.5 rounded-full bg-muted"><span className="block h-1.5 rounded-full" style={{ width: `${(r.count / max) * 100}%`, background: color }} /></span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
