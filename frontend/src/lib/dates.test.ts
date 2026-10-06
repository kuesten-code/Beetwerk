import { describe, expect, it } from "vitest";
import { addDays, daysBetween, describeRecurrence, dueBucket, groupByDue, relativeDue, toIsoDate } from "./dates";
import type { GardenTask } from "./types";

const task = (dueDate: string): GardenTask => ({
  id: Math.random(),
  title: "t",
  description: null,
  objectId: null,
  objectName: null,
  geometry: null,
  dueDate,
  status: "Open",
  completedAt: null,
  notify: false,
  leadDays: 0,
  frequency: "None",
  interval: 1,
  seasonStartMonth: null,
  seasonEndMonth: null,
  rRule: null,
});

describe("dates", () => {
  it("formats local calendar days without timezone shifts", () => {
    expect(toIsoDate(new Date(2026, 0, 5, 23, 30))).toBe("2026-01-05");
  });

  it("adds days across month and DST boundaries", () => {
    expect(addDays("2026-03-28", 2)).toBe("2026-03-30");
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(daysBetween("2026-03-28", "2026-03-30")).toBe(2);
  });

  it("buckets tasks by due date", () => {
    const today = "2026-10-07";
    expect(dueBucket("2026-10-06", today)).toBe("overdue");
    expect(dueBucket("2026-10-07", today)).toBe("today");
    expect(dueBucket("2026-10-14", today)).toBe("week");
    expect(dueBucket("2026-10-15", today)).toBe("later");

    const groups = groupByDue([task("2026-10-01"), task("2026-10-07"), task("2026-12-01")], today);
    expect(groups.overdue).toHaveLength(1);
    expect(groups.today).toHaveLength(1);
    expect(groups.week).toHaveLength(0);
    expect(groups.later).toHaveLength(1);
  });

  it("describes relative due dates in German", () => {
    expect(relativeDue("2026-10-07", "2026-10-07")).toBe("heute");
    expect(relativeDue("2026-10-08", "2026-10-07")).toBe("morgen");
    expect(relativeDue("2026-10-06", "2026-10-07")).toBe("seit gestern überfällig");
    expect(relativeDue("2026-10-04", "2026-10-07")).toBe("seit 3 Tagen überfällig");
  });

  it("describes recurrence rules", () => {
    expect(describeRecurrence({ frequency: "None", interval: 1, seasonStartMonth: null, seasonEndMonth: null })).toBeNull();
    expect(describeRecurrence({ frequency: "Daily", interval: 1, seasonStartMonth: null, seasonEndMonth: null })).toBe("jeden Tag");
    expect(describeRecurrence({ frequency: "Weekly", interval: 1, seasonStartMonth: null, seasonEndMonth: null })).toBe("jede Woche");
    expect(describeRecurrence({ frequency: "Yearly", interval: 1, seasonStartMonth: null, seasonEndMonth: null })).toBe("jedes Jahr");
    expect(describeRecurrence({ frequency: "Weekly", interval: 2, seasonStartMonth: 3, seasonEndMonth: 5 })).toBe("alle 2 Wochen, März bis Mai");
  });
});
