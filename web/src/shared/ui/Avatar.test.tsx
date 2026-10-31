import { cleanup, fireEvent, render } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { Avatar, initials } from "./Avatar";

afterEach(cleanup);

describe("Avatar (an account's photo, migration 0036)", () => {
  it("shows the photo when there is one", () => {
    const { container } = render(<Avatar name="ياسمين علي" photo="/api/panel/users/u1/avatar?v=1" className="size-12" />);
    const img = container.querySelector("img")!;
    expect(img.getAttribute("src")).toBe("/api/panel/users/u1/avatar?v=1");
    expect(img.className).toContain("size-12");
    expect(container.textContent).toBe("");
  });

  it("shows the initials without a photo, and when the photo does not load", () => {
    const { container, rerender } = render(<Avatar name="ياسمين علي" photo={null} />);
    expect(container.querySelector("img")).toBeNull();
    expect(container.textContent).toBe("ي ع");

    rerender(<Avatar name="ياسمين علي" photo="/api/panel/users/u1/avatar?v=2" />);
    fireEvent.error(container.querySelector("img")!);
    expect(container.querySelector("img")).toBeNull();
    expect(container.textContent).toBe("ي ع");
  });

  it("takes two letters of a single name, and a mark for none", () => {
    expect(initials("  ياسمين ")).toBe("يا");
    expect(initials("")).toBe("؟");
  });
});
