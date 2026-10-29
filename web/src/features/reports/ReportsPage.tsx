import { useState } from "react";
import { queryOptions, useQuery, type QueryClient } from "@tanstack/react-query";
import { api, errorMessage } from "../../shared/api/client";
import type { MonthReport, ReportRow } from "../../shared/api/types";
import { APPOINTMENT_STATES, label, SOURCES, TASK_TYPES, CLIENT_TYPES, matterLabel } from "../../shared/lib/labels";
import { Card, ErrorState, Loading } from "../../shared/ui/ui";

/** «This month» in Cairo, as YYYY-MM. */
function thisMonth(): string {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit" }).formatToParts(new Date());
  return `${parts.find((p) => p.type === "year")!.value}-${parts.find((p) => p.type === "month")!.value}`;
}

/**
 * The month's report (TASK 07c, the manager and the lawyer): the tasks (by type and by staff
 * member, and how many were done before their due time) and the new clients (by source, topic and
 * kind). TASK 09: the month's bookings by state and their money as the office marked it.
 */
const reportQuery = (month: string) => queryOptions({ queryKey: ["report", month], queryFn: () => api<MonthReport>("GET", `/reports?month=${month}`) });

/** This month's report (what the page opens on), fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(reportQuery(thisMonth()));
}

export function ReportsPage() {
  const [month, setMonth] = useState(thisMonth);
  const report = useQuery(reportQuery(month));
  const r = report.data;
  const pct = (a: number, b: number) => (b === 0 ? "—" : `${Math.round((a / b) * 100)}%`);

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h1 className="text-2xl">التقارير</h1>
        <label className="grid gap-1 text-sm">
          <span className="font-semibold">الشهر</span>
          <input type="month" className="surface min-h-11 rounded-[var(--radius-control)] px-3" value={month} max={thisMonth()}
            onChange={(e) => e.target.value && setMonth(e.target.value)} />
        </label>
      </div>
      {report.isPending ? <Loading /> : report.isError ? <ErrorState message={errorMessage(report.error)} onRetry={() => void report.refetch()} /> : r ? (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Number title="المهام" value={r.tasks} />
            <Number title="خلصت" value={r.done} note={pct(r.done, r.tasks)} />
            <Number title="خلصت في وقتها" value={r.done_on_time} note={pct(r.done_on_time, r.done)} />
            <Number title="عملاء جداد" value={r.new_clients} />
          </div>
          <div className="grid gap-4 lg:grid-cols-2">
            <Table title="المهام حسب النوع" rows={r.by_type} name={(k) => label(TASK_TYPES, k)} />
            <Card>
              <h2 className="text-lg">المهام حسب الموظف</h2>
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
          <div className="grid gap-3 sm:grid-cols-3">
            <Number title="وصل (جنيه)" value={r.received} />
            <Number title="ناقص (جنيه)" value={r.missing} />
            <Number title="اترد (جنيه)" value={r.refunded} />
          </div>
          <p className="text-xs text-ink-soft">«في وقتها» = خلصت قبل الميعاد المطلوب. الفلوس = اللي المكتب علّم إنه وصل فعلًا لحجوزات الشهر.</p>
        </>
      ) : null}
    </div>
  );
}

function Number({ title, value, note }: { title: string; value: number; note?: string }) {
  return (
    <Card>
      <h2 className="text-sm text-ink-soft">{title}</h2>
      <p className="tabular text-3xl font-bold">{value}{note ? <span className="ms-2 text-sm font-normal text-ink-soft">{note}</span> : null}</p>
    </Card>
  );
}

function Table({ title, rows, name }: { title: string; rows: ReportRow[]; name: (key: string) => string }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <Card>
      <h2 className="text-lg">{title}</h2>
      {rows.length === 0 ? <p className="text-sm text-ink-soft">مفيش حاجة الشهر ده.</p> : (
        <ul className="grid gap-2">
          {rows.map((r) => (
            <li key={r.key} className="grid gap-1">
              <span className="flex justify-between text-sm"><span>{name(r.key)}</span><span className="tabular font-semibold">{r.count}</span></span>
              <span className="h-2 rounded-full bg-muted"><span className="block h-2 rounded-full bg-gold" style={{ width: `${(r.count / max) * 100}%` }} /></span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
