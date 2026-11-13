import { describe, expect, it } from "vitest";
import { firstAndMore, groupByClient, shortType, thingsWords } from "./group";

// fix/alert-flood: one client's alerts are one row
describe("groupByClient", () => {
  it("puts one client's items together, in the order of each client's first item, and never merges an item with no client", () => {
    const items = [{ id: 1, c: "سارة" }, { id: 2, c: "منى" }, { id: 3, c: "سارة" }, { id: 4 }, { id: 5 }, { id: 6, c: "سارة" }];
    const groups = groupByClient(items, (i) => i.c);
    expect(groups.map((g) => g.items.map((i) => i.id))).toEqual([[1, 3, 6], [2], [4], [5]]);
  });

  it("says a task's kind in a few words, and how many things", () => {
    expect(shortType("payment_review_due")).toBe("اقترب الموعد ولم يُراجَع التحويل بعد");
    expect(shortType("complaint")).toBe("شكوى");
    expect([1, 2, 3, 10, 11, 99, 100, 103].map(thingsWords)).toEqual(["عنصر واحد", "عنصران", "3 عناصر", "10 عناصر", "11 عنصرًا", "99 عنصرًا", "100 عنصر", "103 عناصر"]);
  });
});

describe("firstAndMore", () => {
  it("names the first task's kind and counts the others", () => {
    expect(firstAndMore(["complaint"])).toBe("شكوى");
    expect(firstAndMore(["complaint", "refund"])).toBe("شكوى ومهمة أخرى");
    expect(firstAndMore(["complaint", "refund", "price_quote"])).toBe("شكوى ومهمتان أخريان");
    expect(firstAndMore(["complaint", "refund", "price_quote", "refund"])).toBe("شكوى و3 مهام أخرى");
  });
});
