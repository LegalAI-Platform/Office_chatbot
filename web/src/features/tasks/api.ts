import { keepPreviousData, queryOptions, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../shared/api/client";
import { dashboardKey } from "../../shared/api/dashboard";
import { toast } from "../../shared/ui/feedback";
import type { StaffRef, TaskDetail, TaskPage } from "../../shared/api/types";

export type Scope = "mine" | "unassigned" | "all";
export type StateFilter = "active" | "overdue" | "done" | "cancelled";

export interface TaskFilter {
  scope: Scope;
  state: StateFilter;
  page: number;
  /** part of the client's name, or the last digits of the number */
  q?: string;
}

const tasksKey = (f?: TaskFilter) => (f ? ["tasks", f] : ["tasks"]);
const taskKey = (id: string) => ["task", id];

/** One page of the list, the same key wherever it is read or fetched ahead (a hover on «المهام»). */
export const tasksQuery = (filter: TaskFilter) => queryOptions({
  queryKey: tasksKey(filter),
  queryFn: () =>
    api<TaskPage>("GET", `/tasks?scope=${filter.scope}&state=${filter.state}&page=${filter.page}${filter.q ? `&q=${encodeURIComponent(filter.q)}` : ""}`),
});

export function useTasks(filter: TaskFilter) {
  return useQuery({
    ...tasksQuery(filter),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000, // new tasks show up without a reload
  });
}

export function useTask(id: string) {
  return useQuery({ queryKey: taskKey(id), queryFn: () => api<TaskDetail>("GET", `/tasks/${id}`) });
}

export function useStaff(enabled: boolean) {
  return useQuery({ queryKey: ["staff"], queryFn: () => api<StaffRef[]>("GET", "/staff"), enabled, staleTime: 300_000 });
}

export type TaskAction = "start" | "done" | "cancel";

export function useTaskAction(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (action: TaskAction) => api<TaskDetail>("POST", `/tasks/${id}/${action}`),
    onSuccess: (detail, action) => {
      qc.setQueryData(taskKey(id), detail);
      void qc.invalidateQueries({ queryKey: tasksKey() });
      void qc.invalidateQueries({ queryKey: dashboardKey });
      toast(action === "start" ? "اتسجّل إنك شغال عليها" : action === "done" ? "المهمة خلصت" : "المهمة اتلغت");
    },
  });
}

export function useAssign(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (staffId: string | null) => api<TaskDetail>("POST", `/tasks/${id}/assign`, { staff_id: staffId }),
    onSuccess: (detail) => {
      qc.setQueryData(taskKey(id), detail);
      void qc.invalidateQueries({ queryKey: tasksKey() });
      void qc.invalidateQueries({ queryKey: dashboardKey });
      toast(detail.staff ? `المهمة اتحوّلت لـ ${detail.staff.name}` : "المهمة بقت مش متعيّنة");
    },
  });
}
